using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>マップの海全体をひとつの物差しで測る。</b>**sim スレッド専用の診断。**
    ///
    /// ── なぜ要るのか（2026-08-31、所有者の提案）──────────────────────
    ///
    /// &gt; 実際に CS オリジナルの津波を起こすので挙動を調べてみるのはどうでしょうか？
    ///
    /// **これが正しい。** それまでの数字はすべて「自分のものさしで測った自分の波」で、
    /// <b>満足できる津波が数字でどう見えるのかを知らないまま</b>調整していた。
    /// バニラの津波を<b>同じものさし</b>で測れば、初めて比べられる。
    ///
    /// ★★ だからこの計測は<b>自分の波に紐づかない</b>。海を見るだけである。
    ///   バニラの <c>TsunamiAI</c> が動いていても、こちらの波が動いていても、
    ///   <b>同じ 1 行</b>が出る。並べれば差が読める。
    ///
    /// ── 測り方 ────────────────────────────────────────────
    ///
    /// <c>WaterSimulation.BeginRead()</c> は<b>水セル配列そのもの</b>を返す
    /// （IL 実測: <c>Cell[]</c>、ロックは 1 回だけ）。だから
    /// <c>WaterLevel()</c> を何万回も呼ぶ必要はない —— **1 回借りて舐める。**
    ///
    /// <list type="bullet">
    /// <item><b>海</b>（<c>blockHeights &lt; 海面</c>）では平常からの上がりの最大</item>
    /// <item><b>陸</b>（<c>blockHeights &gt;= 海面</c>）では水があるセルの数
    ///   ＝ <b>浸水面積</b>。プレイヤーが「津波だ」と思うのは結局これである</item>
    /// </list>
    ///
    /// ★ 陸の標高を「波」と読まないこと。<c>WaterLevel</c> は地形＋水柱を返すので、
    ///   海面 207 m のマップで標高 277 m の山を叩けば水無しで +70 m になる
    ///   （<c>TsunamiWave.RiseAt</c> の ★★ で一度踏んだ）。
    ///
    /// ── ★★ 基準値を引く（2026-08-31、最初の実測で判明）────────────────
    ///
    /// 一度目の実装は<b>絶対値</b>を出していた。実機の 1 行目がこれである：
    ///
    /// <code>sea watch @0 steps: highest sea 55.1 m, land under water 3721 cells</code>
    ///
    /// **波が来る前の 0 歩目で 55 m・15 km2。** 数えていたのは
    /// <b>そのマップが元から持っている川と、川床が海面下に落ちる谷</b>だった。
    /// 川は「陸のセルに水が乗っている」ので浸水と区別が付かず、
    /// 谷は <c>ground &lt; 海面</c> なので海と区別が付かない。
    ///
    /// だから<b>測り始める瞬間の水面を丸ごと覚えて、以後はそこからの差だけ</b>を出す。
    /// 覚えるのは水面（地形＋水柱）で、地形だけではない —— 川は動かないので
    /// 差を取れば消える。バニラでもこちらでも同じ引き算をするので、比較は保たれる。
    /// </summary>
    public static class SeaWatch
    {
        /// <summary>16 m セルの数。<c>BlockHeights</c> の添字は <c>z*(1080+1)+x</c>。</summary>
        private const int GridCells = 1080;

        /// <summary>何セルおきに見るか。**4 なら 1/16 の点だけ見る。**</summary>
        private const int SampleStride = 4;

        /// <summary>
        /// 何 sim フレームおきに測るか（64 ＝ 1 水ステップ）。
        ///
        /// ★ 8 歩おきだと 1 回の津波で 300 行になり、<c>output_log.txt</c> を
        ///   埋め尽くす（2026-08-31、第 4 回検証）。60 歩 ≒ 1 実分おきで十分である。
        /// </summary>
        private const int EveryFrames = 64 * 60;

        /// <summary>陸の上に水があると認める深さ（m）。**波飛沫と浸水を分ける。**</summary>
        private const float FloodMetres = 0.5f;

        private static uint _lastFrame;
        private static bool _armed;
        private static uint _armedFrame;
        private static string _reason;
        private static float _peakRise;
        private static int _peakFloodCells;
        private static bool _errorLogged;
        private static bool _sawVanilla;

        /// <summary>
        /// 測り始めた瞬間の水面（1/64 m）。添字は<b>間引いた格子</b>で
        /// <c>(z/Stride)*BaseSide + (x/Stride)</c>。null は「まだ取っていない」。
        /// </summary>
        private static int[] _base;

        /// <summary>間引いた格子の一辺。</summary>
        private static readonly int BaseSide = GridCells / SampleStride + 1;

        /// <summary>いま測っているか。</summary>
        public static bool Armed { get { return _armed; } }

        /// <summary>これまでに見た海面の最大の上がり（m）。</summary>
        public static float PeakRiseMetres { get { return _peakRise; } }

        /// <summary>これまでに見た浸水セル数の最大。</summary>
        public static int PeakFloodCells { get { return _peakFloodCells; } }

        /// <summary>レベルのロード／アンロードで呼ぶ。</summary>
        public static void Reset()
        {
            // ★ 都市をまたいで基準値を持ち越さない。地形が別物になる。
            _base = null;
            _armed = false;
            _armedFrame = 0u;
            _lastFrame = 0u;
            _reason = null;
            _peakRise = 0f;
            _peakFloodCells = 0;
            _sawVanilla = false;
        }

        /// <summary>
        /// 測りはじめる。<paramref name="reason"/> はログに出る（「誰の波か」）。
        /// 既に測っていれば理由だけ足す。
        /// </summary>
        public static void Arm(string reason, uint frame)
        {
            if (_armed) return;

            _armed = true;
            _armedFrame = frame;
            _reason = reason;
            _peakRise = 0f;
            _peakFloodCells = 0;

            if (!CaptureBaseline())
            {
                _armed = false;
                Log.Info("sea watch could not read the water, so it is not measuring.");
                return;
            }

            Log.Info("sea watch armed (" + reason + "). From here the whole map's water is "
                     + "sampled every " + (EveryFrames / 64) + " water steps: the highest "
                     + "the sea gets anywhere, and how many land cells are under water. "
                     + "**The same ruler is used for the DLC tsunami and for ours, so the "
                     + "two runs can be compared directly.**");
        }

        /// <summary>
        /// いまの水面を丸ごと覚える。**これを取らない計測は嘘になる**（クラス doc）。
        /// </summary>
        private static bool CaptureBaseline()
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null) return false;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return false;

            if (_base == null) _base = new int[BaseSide * BaseSide];

            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
                if (cells == null) return false;

                for (int z = 0; z <= GridCells; z += SampleStride)
                {
                    int row = z * (GridCells + 1);
                    int baseRow = (z / SampleStride) * BaseSide;

                    for (int x = 0; x <= GridCells; x += SampleStride)
                    {
                        int at = row + x;
                        _base[baseRow + x / SampleStride] =
                            (at >= 0 && at < block.Length && at < cells.Length)
                                ? block[at] + cells[at].m_height
                                : int.MinValue;
                    }
                }
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }

            return true;
        }

        /// <summary>測るのをやめて、結びの 1 行を出す。</summary>
        public static void Disarm(uint frame)
        {
            if (!_armed) return;

            Log.Info("sea watch finished (" + _reason + ") after "
                     + ((frame - _armedFrame) / 64) + " water steps: the sea rose at most "
                     + _peakRise.ToString("F1") + " m above where it was, most newly "
                     + "flooded land "
                     + _peakFloodCells + " sampled cells = "
                     + (_peakFloodCells * SampleStride * SampleStride * 16f * 16f / 1000000f)
                       .ToString("F2") + " km2");

            _armed = false;
            _reason = null;
        }

        /// <summary>
        /// 測りっぱなしにしない上限（水ステップ）。
        ///
        /// ★ 発生源が 768 歩、そのあと波がマップを渡るのに同じくらい掛かる。
        ///   1500 では<b>浸水の最中に打ち切られる</b>ので余裕を持たせる
        ///   （2026-08-31、相互検証）。2400 歩 ≒ 43 実分。
        /// </summary>
        private const int MaxSteps = 2400;

        /// <summary>バニラの <c>TsunamiAI</c> のプレハブ索引。-1 は「まだ探していない」。</summary>
        private static int _tsunamiPrefabIndex = -1;

        /// <summary>
        /// <b>バニラの津波が動いていないか見る。</b>動いていたら勝手に測りはじめる。
        ///
        /// ★★ 所有者が DLC の津波を起こしたとき、こちらが何もしなければ
        ///   <b>比べる相手の数字が取れない</b>。だから自分から気づく。
        /// </summary>
        private static bool VanillaTsunamiRunning()
        {
            DisasterManager manager = Singleton<DisasterManager>.instance;
            if (manager == null || manager.m_disasters == null) return false;

            if (_tsunamiPrefabIndex < 0)
            {
                DisasterInfo info = DisasterManager.FindDisasterInfo<TsunamiAI>();
                if (info == null) return false;
                _tsunamiPrefabIndex = info.m_prefabDataIndex;
            }

            DisasterData[] buffer = manager.m_disasters.m_buffer;
            if (buffer == null) return false;

            int size = manager.m_disasters.m_size;
            if (size > buffer.Length) size = buffer.Length;

            for (int i = 1; i < size; i++)
            {
                if (buffer[i].m_flags == DisasterData.Flags.None) continue;
                if (buffer[i].m_infoIndex != _tsunamiPrefabIndex) continue;
                return true;
            }

            return false;
        }

        /// <summary>**sim スレッド。** 毎 tick 呼んでよい（自分で間引く）。</summary>
        public static void Tick(uint frame)
        {
            // ★ バニラの津波が始まったら、こちらから測りはじめる。
            if ((frame & 63u) == 0u)
            {
                bool vanilla = VanillaTsunamiRunning();

                if (!_armed && vanilla)
                {
                    Arm("the DLC's own tsunami - this is the yardstick", frame);
                }
                else if (_armed && vanilla && !_sawVanilla)
                {
                    // ★★ こちらの波を測っている最中にバニラが起きても取りこぼさない。
                    //    Arm は二度目を無視するので、ここで印を付けておかないと
                    //    <b>比べる相手の数字が「うちの波」と札を付けて出てしまう。</b>
                    _sawVanilla = true;
                    _reason += " + THE DLC TSUNAMI JOINED at step "
                               + ((frame - _armedFrame) / 64);
                    Log.Info("sea watch: the DLC's own tsunami started while we were "
                             + "already measuring. From here the numbers are both waves "
                             + "together, not ours alone.");
                }
            }

            if (_armed && (frame - _armedFrame) / 64 >= MaxSteps)
            {
                Disarm(frame);
                return;
            }

            if (!_armed) return;
            if (frame - _lastFrame < EveryFrames) return;
            _lastFrame = frame;

            try
            {
                Sample(frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("sea watch failed", e);
                }

                _armed = false;
            }
        }

        private static void Sample(uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null) return;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return;

            if (_base == null) return;

            float seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            int seaUnits = (int)(seaLevel * 64f);

            float best = 0f;
            int bestX = 0;
            int bestZ = 0;
            int flooded = 0;
            int floodUnits = (int)(FloodMetres * 64f);

            // ★★ **1 回借りて舐める。** WaterLevel() を何万回も呼ぶと、
            //    1 回ごとに BeginRead/EndRead の錠を取り直して水スレッドと奪い合う。
            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
                if (cells == null) return;

                for (int z = 0; z <= GridCells; z += SampleStride)
                {
                    int row = z * (GridCells + 1);
                    int baseRow = (z / SampleStride) * BaseSide;

                    for (int x = 0; x <= GridCells; x += SampleStride)
                    {
                        int at = row + x;
                        if (at < 0 || at >= block.Length || at >= cells.Length) continue;

                        int was = _base[baseRow + x / SampleStride];
                        if (was == int.MinValue) continue;

                        int ground = block[at];

                        // ★★ **平常時の水面からの差。** 絶対値ではない（クラス doc）。
                        int rise = ground + cells[at].m_height - was;
                        if (rise <= 0) continue;

                        if (ground < seaUnits)
                        {
                            // 海。どれだけ盛り上がったか。
                            float m = rise / 64f;
                            if (m > best) { best = m; bestX = x; bestZ = z; }
                        }
                        else if (rise > floodUnits)
                        {
                            // 陸。**元より 0.5 m 以上深くなった** ＝ 新しく浸かった。
                            flooded++;
                        }
                    }
                }
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }

            if (best > _peakRise) _peakRise = best;
            if (flooded > _peakFloodCells) _peakFloodCells = flooded;

            Log.Info("sea watch @" + ((frame - _armedFrame) / 64) + " steps ("
                     + _reason + "): sea up " + best.ToString("F1")
                     + " m at cell (" + bestX + "," + bestZ + ") = world ("
                     + (bestX * 16f - 8640f).ToString("F0") + ","
                     + (bestZ * 16f - 8640f).ToString("F0") + "), land under water "
                     + flooded + " sampled cells = "
                     + (flooded * SampleStride * SampleStride * 16f * 16f / 1000000f)
                       .ToString("F2") + " km2. Peaks so far: " + _peakRise.ToString("F1")
                     + " m / " + _peakFloodCells + " cells");
        }
    }
}
