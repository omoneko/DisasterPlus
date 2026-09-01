using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>台風の下で木を激しく揺らす。</b>**sim スレッド専用。**
    ///
    /// ── なぜ雨を強くしても木が揺れなかったのか（2026-09-02、IL 実測）────────
    ///
    /// 木の揺れは <c>TreeInstance.RenderInstance</c> が
    /// <c>WeatherManager.GetWindSpeed(pos)</c> から取る。その中身は:
    ///
    /// <code>
    /// cell     = m_windGrid[(z/135+64) * 128 + (x/135+64)]
    /// exposure = pos.y - cell.m_totalHeight / 64
    /// return Clamp(exposure * 0.02 + 1, 0, 2)
    /// </code>
    ///
    /// ★★ **天候がどこにも入っていない。**<c>GetWindSpeedFactor()</c>
    ///   （＝ <c>1 + 雨×0.5 - 霧×0.5</c>）が掛かるのは <c>SampleWindSpeed</c> だけで、
    ///   木が通る <c>GetWindSpeed(Vector3)</c> には掛からない。
    ///   つまり<b>木の揺れは遮蔽の高さだけで決まる静的な量</b>であり、
    ///   雨を 1.0 に張り付かせても 1 ミリも変わらなかった。
    ///
    /// ── 動かせる唯一の口 ────────────────────────────────────────
    ///
    /// <c>m_totalHeight</c>（＝地形＋建物の遮蔽高さ、1/64 m）を下げると
    /// <c>exposure</c> が増え、揺れが 1.0 → 最大 2.0 まで上がる。**倍になる。**
    ///
    /// ★★ **これはセーブに焼き付く。**<c>WeatherManager+Data.Serialize</c> が
    ///   <c>m_windGrid</c> を書く（IL 実測）。戻し損ねると、その都市の遮蔽図が
    ///   狂ったまま保存され、MOD を外しても直らない（その区画の地形か建物が
    ///   変わって <c>CalculateTotalHeight</c> が走り直すまで）。
    ///   だから <c>TyphoonFlood</c> / <c>TsunamiRing</c> と<b>同じ作法</b>で戻す:
    ///
    ///   <list type="bullet">
    ///   <item>台風が終わったとき</item>
    ///   <item>都市を出るとき</item>
    ///   <item><b>保存の直前</b>（MOD の <c>OnSaveData</c> はバニラの配列書き込みより先）</item>
    ///   </list>
    ///
    /// ★ 戻すときは<b>自分が書いた値のままのセルだけ</b>戻す。あいだに建物が建って
    ///   ゲームが計算し直したセルを上書きしない。
    ///
    /// ── 副作用（承知のうえ）────────────────────────────────────
    ///
    /// <c>SampleWindSpeed</c> も同じ格子を読むので、<b>風力発電の出力が上がる</b>。
    /// 台風で風車が回るのは筋が通っているので、これは直さない。
    /// </summary>
    public static class TyphoonTreeSway
    {
        /// <summary>格子の一辺（<c>WINDGRID_RESOLUTION</c>、IL 実測 128）。</summary>
        private const int Resolution = 128;

        /// <summary>格子 1 マスの大きさ（m、<c>WINDGRID_CELL_SIZE</c> 相当）。</summary>
        private const float CellSizeMetres = 135f;

        /// <summary>
        /// 遮蔽高さをどこまで下げるか（1/64 m）。
        ///
        /// ★ <c>exposure × 0.02 + 1</c> が 2.0 で頭打ちなので、
        ///   <c>exposure = 50 m</c> で上限に届く。海面 40 m のマップなら
        ///   遮蔽を 0 にすれば地上の木は 1.8 前後になる。
        ///   **下げ幅で足りるので、0 に潰す必要は無い** —— 建物の陰は残したい。
        /// </summary>
        private const int DropUnits = 64 * 45;

        private static readonly object _gate = new object();

        /// <summary>下げる前の値。<c>_changed</c> が立っているセルだけ意味がある。</summary>
        private static ushort[] _original;

        /// <summary>こちらが書いた値。戻すときの照合に使う。</summary>
        private static ushort[] _written;

        private static bool[] _changed;
        private static int _changedCount;

        /// <summary>
        /// 保存のために外している最中か。**この間は <see cref="Apply"/> を素通しする。**
        ///
        /// ★★ これが無いと、<c>OnSaveData</c> で戻した直後に sim tick が
        ///   下げ直し、<b>バニラが配列を書く前に元に戻ってしまう</b>。
        ///   保存中も sim スレッドは回っている。
        /// </summary>
        private static volatile bool _suspended;

        /// <summary>いまいくつのセルを下げているか（診断）。</summary>
        public static int ChangedCells { get { return _changedCount; } }

        /// <summary>都市を出るとき／機能を切るときに呼ぶ。**冪等。例外を投げない。**</summary>
        public static void Reset()
        {
            lock (_gate)
            {
                RestoreLocked();
                _original = null;
                _written = null;
                _changed = null;
                _changedCount = 0;
            }
        }

        /// <summary>
        /// 台風の下の遮蔽を下げる。**sim スレッド。** 毎 tick 呼んでよい。
        /// </summary>
        /// <param name="centreX">台風の中心（ワールド m）。</param>
        /// <param name="radiusMetres">強風域の半径（m）。</param>
        /// <param name="strength">0..1。1 で最大まで下げる。</param>
        public static void Apply(float centreX, float centreZ, float radiusMetres,
                                 float strength)
        {
            if (radiusMetres <= 0f || strength <= 0f) { Reset(); return; }
            if (_suspended) return;

            lock (_gate)
            {
                if (!Singleton<WeatherManager>.exists) return;

                WeatherManager.WindCell[] grid = Singleton<WeatherManager>.instance.m_windGrid;
                if (grid == null || grid.Length < Resolution * Resolution) return;

                EnsureBuffers();

                int drop = (int)(DropUnits * (strength > 1f ? 1f : strength));
                if (drop <= 0) { RestoreLocked(); return; }

                int cells = (int)(radiusMetres / CellSizeMetres) + 1;
                int cx = CellOf(centreX);
                int cz = CellOf(centreZ);

                // ★ 前の位置で下げたセルは、範囲から外れたら戻す。台風は動くので、
                //   これをしないと通り過ぎた跡が下がったまま残る。
                RestoreOutsideLocked(grid, cx, cz, cells);

                for (int dz = -cells; dz <= cells; dz++)
                {
                    int gz = cz + dz;
                    if (gz < 0 || gz >= Resolution) continue;

                    for (int dx = -cells; dx <= cells; dx++)
                    {
                        if (dx * dx + dz * dz > cells * cells) continue;

                        int gx = cx + dx;
                        if (gx < 0 || gx >= Resolution) continue;

                        int at = gz * Resolution + gx;

                        if (!_changed[at])
                        {
                            _original[at] = grid[at].m_totalHeight;
                            _changed[at] = true;
                            _changedCount++;
                        }
                        else if (grid[at].m_totalHeight != _written[at])
                        {
                            // ★ ゲームが計算し直した。新しい値を基準にやり直す。
                            _original[at] = grid[at].m_totalHeight;
                        }

                        int lowered = _original[at] - drop;
                        if (lowered < 0) lowered = 0;

                        grid[at].m_totalHeight = (ushort)lowered;
                        _written[at] = (ushort)lowered;
                    }
                }
            }
        }

        /// <summary>
        /// **保存の直前に呼ぶ。** 下げた遮蔽を戻して、戻すべきかどうかを返す。
        /// </summary>
        public static bool SuspendForSave()
        {
            lock (_gate)
            {
                if (_changedCount == 0) return false;
                RestoreLocked();
                _suspended = true;
                return true;
            }
        }

        /// <summary>保存が終わってから <c>AddAction</c> 越しに呼ぶ。**sim スレッド。**</summary>
        public static void ReapplyAfterSave()
        {
            _suspended = false;
        }

        private static void EnsureBuffers()
        {
            if (_original == null) _original = new ushort[Resolution * Resolution];
            if (_written == null) _written = new ushort[Resolution * Resolution];
            if (_changed == null) _changed = new bool[Resolution * Resolution];
        }

        private static void RestoreOutsideLocked(WeatherManager.WindCell[] grid,
                                                 int cx, int cz, int cells)
        {
            if (_changedCount == 0) return;

            for (int at = 0; at < _changed.Length; at++)
            {
                if (!_changed[at]) continue;

                int gx = at % Resolution;
                int gz = at / Resolution;
                int dx = gx - cx;
                int dz = gz - cz;

                if (dx * dx + dz * dz <= cells * cells) continue;

                RestoreCell(grid, at);
            }
        }

        private static void RestoreLocked()
        {
            if (_changedCount == 0) return;

            try
            {
                if (!Singleton<WeatherManager>.exists) { ForgetAll(); return; }

                WeatherManager.WindCell[] grid = Singleton<WeatherManager>.instance.m_windGrid;
                if (grid == null || grid.Length < Resolution * Resolution)
                {
                    ForgetAll();
                    return;
                }

                for (int at = 0; at < _changed.Length; at++)
                {
                    if (_changed[at]) RestoreCell(grid, at);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("typhoon: restoring the wind grid failed ("
                         + e.GetType().Name + "); this city's shelter map may stay lowered");
                ForgetAll();
            }
        }

        /// <summary>
        /// 1 セル戻す。**自分が書いた値のままのときだけ**戻す ——
        /// あいだに建物が建ってゲームが計算し直した値を上書きしない。
        /// </summary>
        private static void RestoreCell(WeatherManager.WindCell[] grid, int at)
        {
            if (grid[at].m_totalHeight == _written[at])
            {
                grid[at].m_totalHeight = _original[at];
            }

            _changed[at] = false;
            _changedCount--;
        }

        private static void ForgetAll()
        {
            if (_changed != null)
            {
                for (int i = 0; i < _changed.Length; i++) _changed[i] = false;
            }

            _changedCount = 0;
        }

        /// <summary>ワールド座標を格子のマスへ（<c>GetWindSpeed</c> の IL と同じ式）。</summary>
        private static int CellOf(float world)
        {
            int c = Mathf.FloorToInt(world / CellSizeMetres + 64f - 0.5f);
            return c < 0 ? 0 : (c > Resolution - 1 ? Resolution - 1 : c);
        }
    }
}
