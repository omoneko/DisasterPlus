using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦車両の紐づけと、寿命が尽きたときの終了。すべて sim スレッドから呼ぶこと。
    /// </summary>
    public static class FireWhirlPinner
    {
        // sim スレッド専用の使い回しバッファ。毎 tick 確保しない。
        // 型は DisasterAI.m_tempList と同じ FastList<InstanceID>（確認済み）。
        //
        // 注: リフレクションで実測したところ、この版の Assembly-CSharp.dll では
        // FastList<T> は実際にはグローバル名前空間にあり、この using は無くても
        // ビルドは通る（obj/bin を全消去したクリーンビルドで 0 警告 0 エラーを確認済み）。
        // ここでは将来のゲームバージョン差やレビュー可読性のために残す（実害はない）。
        private static readonly FastList<InstanceID> _tempInstances = new FastList<InstanceID>();

        /// <summary>
        /// 紐づけを受理する最大距離（発生地点からの水平距離）。
        ///
        /// IL 実測（TornadoAI.ActivateDisaster）により、渦車両の生成位置は
        /// targetPosition から distance = intensity*10 + 400 だけ離れた点である
        /// ことが分かっている。この MOD の火災旋風は常に intensity = 60 固定
        /// （FireWhirlFeature.SpawnIntensityBase）で生成するので、正規の紐づけは
        /// 距離 1000 以内に必ず収まる。disasterId が使い回されて別の（無関係な）
        /// 渦がこのグループに紛れ込むケースを弾きつつ、正規の紐づけを絶対に
        /// 誤って弾かないよう、3 倍近い余裕（3000）を持たせている。
        /// 誤って厳しすぎるより緩すぎる方が安全（緩ければ次 tick に再試行されるだけだが、
        /// 厳しすぎるとバニラの竜巻が誤って固定されてしまう）。
        /// </summary>
        private const float MaxAttachDistance = 3000f;
        private const float MaxAttachDistanceSq = MaxAttachDistance * MaxAttachDistance;

        /// <summary>
        /// まだ車両 ID が分かっていない旋風に、渦車両を紐づける。
        /// 車両は ActivateDisaster が作るので、CreateDisaster の直後には存在しない。
        /// </summary>
        public static void AttachVehicles()
        {
            var views = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].VehicleId != 0) continue;

                ushort found = FindVortexVehicle(views[i].DisasterId, views[i].Center);
                if (found == 0) continue;

                FireWhirlRegistry.SetVehicle(views[i].DisasterId, found);
                Log.Info("fire whirl " + views[i].DisasterId + " attached to vortex vehicle " + found);
            }
        }

        /// <summary>
        /// 災害グループに属する車両のうち、渦の VehicleAI を持つものを探す。
        /// TornadoAI.GetPosition が使っているのと同じ経路
        /// （InstanceID.Disaster → InstanceManager.GetAllGroupInstances）を辿る。
        ///
        /// disasterId が解放されて別の災害に使い回された場合、古い（VehicleId==0 のまま
        /// 待っている）レジストリのエントリが無関係な渦車両を拾ってしまう恐れがある。
        /// そうなるとバニラの竜巻が誤って固定されてしまう（このパッチが最も避けたい事故）ので、
        /// 見つけた候補が自分の発生地点 (expectedCenter) から離れすぎていないかを必ず確認する。
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId, Vec3 expectedCenter)
        {
            var id = InstanceID.Empty;
            id.Disaster = disasterId;

            _tempInstances.Clear();
            InstanceManager.GetAllGroupInstances(id, _tempInstances);

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            for (int i = 0; i < _tempInstances.m_size; i++)
            {
                ushort v = _tempInstances.m_buffer[i].Vehicle;
                if (v == 0) continue;

                var info = buffer[v].Info;
                if (info == null || !(info.m_vehicleAI is VortexAI)) continue;

                Vector3 pos = buffer[v].GetLastFrameData().m_position;
                float dx = pos.x - expectedCenter.X;
                float dz = pos.z - expectedCenter.Z;
                if (dx * dx + dz * dz > MaxAttachDistanceSq)
                {
                    // 遠すぎる = disasterId 使い回しで無関係な渦を拾った可能性。
                    // ここで固定してしまうとバニラの竜巻が動かなくなるので、候補として採用しない。
                    Log.Diag("fwAttachReject",
                        "vortex vehicle " + v + " for disaster " + disasterId +
                        " is too far from expected centre; rejecting (stale/reused id?)");
                    continue;
                }

                return v;
            }
            return 0;
        }

        /// <summary>
        /// 寿命が尽きた旋風を、バニラの解体経路に乗せる。
        ///
        /// 車両を自前で解放しない。移動目標を現在位置に置けば、数ステップ後に
        /// VortexAI.ArriveAtDestination が true を返し（m_waitCounter > 4）、
        /// バニラが DisasterAI.DeactivateNow と Vehicle.Unspawn を正しく実行する。
        /// </summary>
        public static void BeginEnding(FireWhirlView v)
        {
            FireWhirlRegistry.MarkEnding(v.DisasterId);

            if (v.VehicleId == 0)
            {
                // 車両が付く前に寿命が尽きた。災害だけ落として掃除する。
                FireWhirlRegistry.Remove(v.DisasterId, ModSettings.MaxLifetimeMinutes.value);
                return;
            }

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            Vector3 here = buffer[v.VehicleId].GetLastFrameData().m_position;

            // w には元の値を残す（速度・半径の意味を持つため、0 にすると挙動が変わる）。
            Vector4 t0 = buffer[v.VehicleId].m_targetPos0;
            buffer[v.VehicleId].SetTargetPos(0, new Vector4(here.x, here.y, here.z, t0.w));

            Log.Info("fire whirl " + v.DisasterId + " ending; target moved to current position");
        }

        /// <summary>バニラに解体された旋風をレジストリから外す。</summary>
        public static void CollectFinished()
        {
            var views = FireWhirlRegistry.Snapshot();
            var disasters = DisasterManager.instance.m_disasters.m_buffer;
            float cooldown = ModSettings.MaxLifetimeMinutes.value;

            for (int i = 0; i < views.Count; i++)
            {
                ushort d = views[i].DisasterId;
                if (d >= disasters.Length) { FireWhirlRegistry.Remove(d, cooldown); continue; }

                if ((disasters[d].m_flags & DisasterData.Flags.Created) == DisasterData.Flags.None)
                {
                    FireWhirlRegistry.Remove(d, cooldown);
                    Log.Info("fire whirl " + d + " collected");
                }
            }
        }
    }
}
