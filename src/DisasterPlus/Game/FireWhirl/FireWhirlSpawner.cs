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

        /// <summary>
        /// 直近の走査が失敗してから何回呼ばれたか。密集火災が続く間、
        /// TrySpawnNew は毎 tick この関数を呼ぶので、失敗を毎回リトライすると
        /// 全 prefab 走査が毎 tick 走り、ログも埋まる。この回数ぶんは
        /// 「失敗キャッシュ」を効かせて呼び出しを間引く。
        /// </summary>
        private static int _missCallCount;

        /// <summary>失敗キャッシュを効かせる呼び出し回数。0 にはしない（=毎回リトライになる）。
        /// 大きすぎると DLC 有効化直後など prefab が後から揃うケースの再検出が遅れる。</summary>
        private const int MissRetryCalls = 64;

        public static void Reset()
        {
            _tornadoInfo = null;
            _searched = false;
            _missCallCount = 0;
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

            // 直前の走査が失敗している場合は、レベルロード直後で prefab がまだ
            // 揃っていないだけの可能性がある。「二度と探さない」にはせず、
            // かといって毎 tick 全 prefab を舐めもしない。呼び出し回数で間引く。
            if (_searched)
            {
                _missCallCount++;
                if (_missCallCount < MissRetryCalls) return null;
            }
            _missCallCount = 0;

            _searched = true;
            _tornadoInfo = ScanForTornadoInfo();

            if (_tornadoInfo == null)
            {
                // Warn はスロットルされないので密集火災が続く間ログを埋め尽くす。Diag に落として間引く。
                Log.Diag("noTornadoPrefab",
                    "no TornadoAI DisasterInfo found; Natural Disasters DLC required for fire whirls");
            }
            return _tornadoInfo;
        }

        /// <summary>
        /// prefab を走査するだけの純粋関数。キャッシュもミス回数も一切触らない。
        /// このクラスの他のメンバーと違い、どのスレッドから呼んでもこのクラスの
        /// 状態を壊さない（読むのは PrefabCollection だけ）。
        /// </summary>
        private static DisasterInfo ScanForTornadoInfo()
        {
            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info;
                try { info = PrefabCollection<DisasterInfo>.GetLoaded(i); }
                catch { continue; }   // 境界チェックをしない API なので 1 件ずつ守る

                if (info == null) continue;
                if (info.m_disasterAI is TornadoAI) return info;
            }
            return null;
        }

        /// <summary>
        /// 前提検証用。副作用なしに prefab の解決可否だけを返す。
        ///
        /// FindTornadoInfo() へ委譲してはいけない。あちらは _searched / _tornadoInfo /
        /// _missCallCount を書き換える「sim スレッドからのみ呼ぶこと」の関数で、
        /// Assumptions.Run() は _levelReady が立った後の main スレッドから呼ぶ
        /// ＝ sim スレッドが TrySpawnNew → FindTornadoInfo を回している最中に重なる。
        /// 破壊的ではないが、ミスキャッシュが巻き戻ったり、見つけた prefab が
        /// 捨てられたりする。そもそも「調べるだけの関数が調べる対象を書き換える」のが
        /// この場所では間違った形なので、純粋な走査に置き換える。
        /// 走査コストはレベルロード 1 回ぶんなので気にしなくてよい。
        /// </summary>
        public static bool HasTornadoPrefab()
        {
            return ScanForTornadoInfo() != null;
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
