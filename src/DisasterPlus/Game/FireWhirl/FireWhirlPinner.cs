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
        private static readonly FastList<InstanceID> _tempInstances = new FastList<InstanceID>();

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

                ushort found = FindVortexVehicle(views[i].DisasterId);
                if (found == 0) continue;

                FireWhirlRegistry.SetVehicle(views[i].DisasterId, found);
                Log.Info("fire whirl " + views[i].DisasterId + " attached to vortex vehicle " + found);
            }
        }

        /// <summary>
        /// 災害グループに属する車両のうち、渦の VehicleAI を持つものを探す。
        /// TornadoAI.GetPosition が使っているのと同じ経路
        /// （InstanceID.Disaster → InstanceManager.GetAllGroupInstances）を辿る。
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId)
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
                if (info != null && info.m_vehicleAI is VortexAI) return v;
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
