using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラの竜巻災害を生成する。渦のメッシュ・音・破壊・災害通知・避難行動が
    /// そのまま手に入る。自前で作り直すのは割に合わない。
    ///
    /// sim スレッドからのみ呼ぶこと。CreateDisaster は災害バッファを触る。
    /// </summary>
    public static class FireWhirlSpawner
    {
        private static DisasterInfo _tornadoInfo;
        private static bool _searched;

        public static void Reset()
        {
            _tornadoInfo = null;
            _searched = false;
        }

        /// <summary>
        /// 竜巻の DisasterInfo を探す。
        /// 名前ではなく AI の型で判定する。ローカライズや MOD の改名に影響されない。
        /// TornadoAI は WeatherDisasterAI 派生であって MeteorAI(VehicleAI) の仲間ではない。
        /// </summary>
        public static DisasterInfo FindTornadoInfo()
        {
            // Unity のフェイク null に対応するため、参照だけでなく実体を毎回確認する。
            if (_searched && _tornadoInfo != null) return _tornadoInfo;

            _searched = true;
            _tornadoInfo = null;

            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info;
                try { info = PrefabCollection<DisasterInfo>.GetLoaded(i); }
                catch { continue; }   // 境界チェックをしない API なので 1 件ずつ守る

                if (info == null) continue;
                if (info.m_disasterAI is TornadoAI) { _tornadoInfo = info; break; }
            }

            if (_tornadoInfo == null)
            {
                Log.Warn("no TornadoAI DisasterInfo found; Natural Disasters DLC required for fire whirls");
            }
            return _tornadoInfo;
        }

        /// <summary>
        /// 指定地点に竜巻災害を作り、その渦車両の ID を返す。
        /// 渦車両は ActivateDisaster が作るので、生成直後にはまだ存在しない。
        /// vehicleId は 0 で返り、Task 11 の追従処理が後から埋める。
        /// </summary>
        public static bool TrySpawn(Vec3 center, byte intensity, out ushort disasterId)
        {
            disasterId = 0;

            var info = FindTornadoInfo();
            if (info == null) return false;

            ushort id;
            if (!DisasterManager.instance.CreateDisaster(out id, info))
            {
                Log.Diag("spawnFail", "CreateDisaster returned false (disaster buffer full?)");
                return false;
            }

            var buffer = DisasterManager.instance.m_disasters.m_buffer;
            buffer[id].m_targetPosition = new Vector3(center.X, center.Y, center.Z);
            buffer[id].m_intensity = intensity;
            buffer[id].m_angle = 0f;

            // DisasterAI.StartDisaster は protected（IL 確認済み）なので直接は呼べない。
            // 公開ラッパーの StartNow を使う。StartNow は data.m_flags & 0x3C
            // （Emerging|Active|Clearing|Finished）が立っていなければ StartDisaster を呼ぶだけで、
            // CreateDisaster 直後は m_flags = Created(0x01) のみなので、ここでは必ず
            // StartDisaster が呼ばれる（IL 確認済み）。起動後は StartDisaster -> ActivateDisaster
            // の順で渦車両が作られる。
            info.m_disasterAI.StartNow(id, ref buffer[id]);

            disasterId = id;
            Log.Info("fire whirl disaster created id=" + id + " intensity=" + intensity);
            return true;
        }
    }
}
