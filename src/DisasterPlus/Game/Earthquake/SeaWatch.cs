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
    /// </summary>
    public static class SeaWatch
    {
        /// <summary>16 m セルの数。<c>BlockHeights</c> の添字は <c>z*(1080+1)+x</c>。</summary>
        private const int GridCells = 1080;

        /// <summary>何セルおきに見るか。**4 なら 1/16 の点だけ見る。**</summary>
        private const int SampleStride = 4;

        /// <summary>何 sim フレームおきに測るか（64 ＝ 1 水ステップ）。</summary>
        private const int EveryFrames = 64 * 8;

        /// <summary>陸の上に水があると認める深さ（m）。**波飛沫と浸水を分ける。**</summary>
        private const float FloodMetres = 0.5f;

        private static uint _lastFrame;
        private static bool _armed;
        private static uint _armedFrame;
        private static string _reason;
        private static float _peakRise;
        private static int _peakFloodCells;
        private static bool _errorLogged;

        /// <summary>いま測っているか。</summary>
        public static bool Armed { get { return _armed; } }

        /// <summary>これまでに見た海面の最大の上がり（m）。</summary>
        public static float PeakRiseMetres { get { return _peakRise; } }

        /// <summary>これまでに見た浸水セル数の最大。</summary>
        public static int PeakFloodCells { get { return _peakFloodCells; } }

        /// <summary>レベルのロード／アンロードで呼ぶ。</summary>
        public static void Reset()
        {
            _armed = false;
            _armedFrame = 0u;
            _lastFrame = 0u;
            _reason = null;
            _peakRise = 0f;
            _peakFloodCells = 0;
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

            Log.Info("sea watch armed (" + reason + "). From here the whole map's water is "
                     + "sampled every " + (EveryFrames / 64) + " water steps: the highest "
                     + "the sea gets anywhere, and how many land cells are under water. "
                     + "**The same ruler is used for the DLC tsunami and for ours, so the "
                     + "two runs can be compared directly.**");
        }

        /// <summary>測るのをやめて、結びの 1 行を出す。</summary>
        public static void Disarm(uint frame)
        {
            if (!_armed) return;

            Log.Info("sea watch finished (" + _reason + ") after "
                     + ((frame - _armedFrame) / 64) + " water steps: highest sea anywhere "
                     + _peakRise.ToString("F1") + " m above normal, most land under water "
                     + _peakFloodCells + " cells = "
                     + (_peakFloodCells * 16f * 16f / 1000000f).ToString("F2") + " km2");

            _armed = false;
            _reason = null;
        }

        /// <summary>測りっぱなしにしない上限（水ステップ）。</summary>
        private const int MaxSteps = 1500;

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
            if (!_armed && (frame & 63u) == 0u && VanillaTsunamiRunning())
            {
                Arm("the DLC's own tsunami - this is the yardstick", frame);
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

            float seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            int seaUnits = (int)(seaLevel * 64f);

            float best = 0f;
            int bestX = 0;
            int bestZ = 0;
            int flooded = 0;

            // ★★ **1 回借りて舐める。** WaterLevel() を何万回も呼ぶと、
            //    1 回ごとに BeginRead/EndRead の錠を取り直して水スレッドと奪い合う。
            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
                if (cells == null) return;

                for (int z = 0; z <= GridCells; z += SampleStride)
                {
                    int row = z * (GridCells + 1);

                    for (int x = 0; x <= GridCells; x += SampleStride)
                    {
                        int at = row + x;
                        if (at < 0 || at >= block.Length || at >= cells.Length) continue;

                        int ground = block[at];
                        int height = cells[at].m_height;

                        if (ground < seaUnits)
                        {
                            // 海。平常の海面からどれだけ上がっているか。
                            int rise = ground + height - seaUnits;
                            if (rise > 0)
                            {
                                float m = rise / 64f;
                                if (m > best) { best = m; bestX = x; bestZ = z; }
                            }
                        }
                        else if (height > FloodMetres * 64f)
                        {
                            // 陸の上に水がある ＝ 浸水。
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
                     + _reason + "): highest sea " + best.ToString("F1")
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
