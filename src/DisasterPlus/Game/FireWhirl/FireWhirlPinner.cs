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
        /// 車両を自前で解放しない。移動目標を現在位置に置けば、やがて
        /// VortexAI.ArriveAtDestination が true を返し（m_waitCounter > 4）、
        /// バニラが DisasterAI.DeactivateNow と Vehicle.Unspawn を正しく実行する。
        ///
        /// スロット 0 だけでは足りない（IL 実測で判明。設計書 5.6 の旧記述は誤り）。
        /// VortexAI.SimulationStep(6 引数) は冒頭で
        ///     if (LengthXZ(m_targetPos0 - frame.m_position) &lt; m_info.m_maxSpeed)
        ///         m_targetPos0 = m_targetPos1;
        /// を行う。TornadoAI.ActivateDisaster は SetTargetPos(0, m_targetPosition) と
        /// SetTargetPos(1, m_targetPosition - dir*(intensity*10+400)) を入れているので、
        /// スロット 0 だけ現在位置に書いても次のステップで 1000m 先のスロット 1 に
        /// 上書きされ、ArriveAtDestination には永遠に到達しない。両方に書く。
        ///
        /// w は 0 にする。バニラは Vector3 -&gt; Vector4 の暗黙変換で目標を入れており
        /// （ActivateDisaster の IL に op_Implicit）、VortexAI 側も Vector4 -&gt; Vector3 の
        /// 暗黙変換で読むだけなので w は元から 0 で、意味を持たない。
        /// </summary>
        public static void BeginEnding(FireWhirlView v)
        {
            if (v.VehicleId == 0)
            {
                // 車両が付く前に寿命が尽きた。レジストリから外すだけだと、バニラの災害は
                // 生きたまま追跡不能なドリフト竜巻になる。正規に停止できたときだけ外す。
                if (!TryDeactivateDisasterNow(v.DisasterId))
                {
                    // まだ Active になっていない（DisasterAI.DeactivateNow は
                    // m_flags に Active が立っていなければ何もしない。IL 確認済み）。
                    // Ending も付けずにこのまま生かし、次 tick に再判定させる。
                    // 寿命判定は単調なので、Active になった時点で必ずここへ戻ってくる。
                    Log.Diag("fwEndWait", "fire whirl " + v.DisasterId +
                             " is not active yet; deferring teardown");
                    return;
                }

                FireWhirlRegistry.Remove(v.DisasterId, ModSettings.MaxLifetimeMinutes.value);
                return;
            }

            FireWhirlRegistry.MarkEnding(v.DisasterId);

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            Vector3 here = buffer[v.VehicleId].GetLastFrameData().m_position;
            var target = new Vector4(here.x, here.y, here.z, 0f);

            buffer[v.VehicleId].SetTargetPos(0, target);
            buffer[v.VehicleId].SetTargetPos(1, target);

            Log.Info("fire whirl " + v.DisasterId + " ending; both target slots moved to current position");
        }

        /// <summary>
        /// 渦車両を持たない災害をバニラの経路で止める。sim スレッド専用。
        ///
        /// DisasterAI.DeactivateNow は public（IL 確認済み）だが、
        /// m_flags に Active(8) が立っているときしか DeactivateDisaster に委譲しない。
        /// まだ Emerging の災害には効かないので、その場合は false を返して呼び出し側に待たせる。
        /// </summary>
        /// <returns>災害が停止した（あるいは既に消えていた）なら true。まだ止められないなら false。</returns>
        private static bool TryDeactivateDisasterNow(ushort disasterId)
        {
            try
            {
                var disasters = DisasterManager.instance.m_disasters.m_buffer;
                if (disasterId == 0 || disasterId >= disasters.Length) return true;

                // 既に消えている。掃除するだけでよい。
                if ((disasters[disasterId].m_flags & DisasterData.Flags.Created) == DisasterData.Flags.None)
                    return true;

                if ((disasters[disasterId].m_flags & DisasterData.Flags.Active) == DisasterData.Flags.None)
                    return false;

                var info = disasters[disasterId].Info;
                if (info == null || info.m_disasterAI == null)
                {
                    Log.Warn("fire whirl " + disasterId +
                             " has no vortex vehicle and no DisasterInfo; leaving it to vanilla");
                    return true;   // これ以上できることが無いので追跡だけやめる
                }

                info.m_disasterAI.DeactivateNow(disasterId, ref disasters[disasterId]);
                Log.Info("fire whirl " + disasterId + " had no vortex vehicle; deactivated directly");
                return true;
            }
            catch (System.Exception e)
            {
                Log.Error("could not deactivate vehicle-less fire whirl " + disasterId, e);
                return true;   // 例外で毎 tick 再突入させない
            }
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
