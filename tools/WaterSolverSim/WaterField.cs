using System;
using System.Collections.Generic;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b><c>WaterSimulation.SimulateWater</c> のオフライン再現。</b>
    /// UnityEngine も Cities の API も使わない、素の .NET だけ。
    ///
    /// ── 何のためのものか ─────────────────────────────────────────
    ///
    /// 津波の発生源（<see cref="Impulse"/> ＝ TYPE_IMPACT の水波）を、
    /// <b>ゲームを起動せずに</b>調整するため。MOD が実機で出す外力を
    /// そのままここへ流し込み、海面が何 m 上がるか、波がどこまで走るかを測る。
    ///
    /// ── ゲームとの対応 ───────────────────────────────────────────
    ///
    /// 実機は 1081x1081（<c>Cell[(1080+1)*(1080+1)]</c>、Awake IL_002B-0032）。
    /// ここは <c>N x N</c> にしてある —— <b>格子の大きさは 1 セルあたりの物理に効かない</b>ので、
    /// 512 まで落として確認を速くする。添字は実機と同じく <c>z*N + x</c>、
    /// x も z も <c>0 .. N-1</c> の<b>両端込み</b>（実機の 0..1080 に相当）。
    ///
    /// ★ <b>1 回の <see cref="Step"/> が 1 回の SimulateWater 呼び出し</b>である。
    ///   実機の SimulateWater は帯ではなく<b>盤面まるごと</b>を 1 回で処理する
    ///   （唯一の外側ループ <c>for (loc46 = -5; loc46 &lt;= 1080; loc46++)</c>、IL_049D / IL_181B）。
    ///
    /// ── 3 段のソフトウェアパイプライン ────────────────────────────
    ///
    /// 外側ループ変数は z ではない。1 回の反復で
    /// <list type="bullet">
    /// <item>描画タイル（IL_04A6）── **物理ではないので写していない**</item>
    /// <item>流速の更新: z = loc46 + 3（IL_0670-0674）</item>
    /// <item>質量の輸送 + 海面 + 外周: z = loc46（IL_0FEC-0FEE）</item>
    /// </list>
    /// が別々の行に対して走る。**この 3 行のずれは飾りではない**:
    /// 輸送は流速の結果を読み、外周は輸送の結果を読むので、
    /// 「全行の流速 → 全行の輸送」と 2 周に割ると結果が変わる。
    /// </summary>
    internal sealed partial class WaterField
    {
        /// <summary>高さの単位。1 単位 = 1/64 m（実機と同じ）。</summary>
        public const int UnitsPerMetre = 64;

        /// <summary>1 セルの一辺（m）。実機の loc6 = 16f（IL_004E）。</summary>
        public const float CellSizeMetres = 16f;

        private readonly int _n;
        private readonly int _last;      // 最後の添字。実機の loc7 = 1080 に相当
        private readonly ushort[] _terrain;
        private readonly Cell[] _bufferA;
        private readonly Cell[] _bufferB;

        /// <summary>いま最新の状態が入っているほうの配列。</summary>
        private Cell[] _current;

        /// <summary>実機の <c>m_stepIndex</c>。**乱数の種であり、位相マスクの元でもある。**</summary>
        private int _stepIndex;

        private Lcg _rng;

        /// <summary>実機の <c>m_currentSeaLevel</c>（m）。前フレームの海面。</summary>
        private float _appliedSeaLevel;

        private bool _resetWater;

        /// <summary>実機の <c>m_nextSeaLevel</c>（m）。既定 40f（Awake IL_00F9-010E）。</summary>
        public float SeaLevel;

        /// <summary>
        /// 外周に差し込む DLC の津波。null なら輪はただの海面固定。
        ///
        /// ★★ **これが在るときだけ、盤面は水を「もらう」ことができる。**
        ///   <see cref="EdgeWave"/> のクラス doc を参照。
        /// </summary>
        public EdgeWave Edge;

        /// <summary>
        /// <c>SimulateWater</c> の唯一の引数 <c>m_finalPollutionDisposeRate</c>（IL_02D1-02E8）。
        /// 汚染は水の動きに影響しないので、既定の 1 のままでよい。
        /// </summary>
        public int PollutionDisposeRate = 1;

        // ── 診断用カウンタ（ゲームには存在しない）────────────────────────
        //   ★ 物理には一切触らない。総水量の増減を「どこで起きたか」に分解する
        //     ためだけの数え上げ。理屈の上では
        //       dV = RingAdded - RingRemoved - EvapRemoved - SatLost + SatGained
        //     が**恒等式**になるはずで、ならなければどこかに未知の漏れがある。
        public long RingAdded;      // 外周の輪が湧かせた量
        public long RingRemoved;    // 外周の輪が捨てた量
        public long EvapRemoved;    // 蒸発で消えた量
        public long SatLost;        // Max(h-m,0) で消えた量（水の無いセルから汲んだぶん）
        public long SatGained;      // Min(h+m,65535) で捨てた量（65535 で溢れたぶん）→ 実際は損失

        public int Size { get { return _n; } }
        public int LastIndex { get { return _last; } }
        public ushort[] Terrain { get { return _terrain; } }
        public Cell[] Cells { get { return _current; } }
        public int StepIndex { get { return _stepIndex; } }

        /// <param name="n">一辺のセル数。実機は 1081。確認用は 512 で足りる。</param>
        /// <param name="seaLevelMetres">海面（m）。実機の既定は 40f。</param>
        public WaterField(int n, float seaLevelMetres)
        {
            if (n < 4) throw new ArgumentOutOfRangeException("n", "格子が小さすぎる（外周の輪が潰れる）");

            _n = n;
            _last = n - 1;
            _terrain = new ushort[n * n];
            _bufferA = new Cell[n * n];
            _bufferB = new Cell[n * n];
            _current = _bufferA;

            SeaLevel = seaLevelMetres;
            _appliedSeaLevel = seaLevelMetres;
            _resetWater = false;
        }

        /// <summary>海面（1/64 m）。実機の loc4 = <c>(int)(m_nextSeaLevel * 64f)</c>（IL_0036-003E）。</summary>
        public int SeaLevelUnits { get { return (int)(SeaLevel * 64f); } }

        /// <summary>添字。実機は <c>z*1081 + x</c>（IL_079D-07A7）。</summary>
        public int Index(int x, int z) { return z * _n + x; }

        /// <summary>水面の標高（1/64 m）＝ 地形 + 水柱。</summary>
        public int SurfaceUnits(int x, int z)
        {
            int i = z * _n + x;
            return _terrain[i] + _current[i].Height;
        }

        /// <summary>水面が海面より何 m 高いか（負なら低い）。</summary>
        public float SurfaceAboveSeaMetres(int x, int z)
        {
            return (SurfaceUnits(x, z) - SeaLevelUnits) / (float)UnitsPerMetre;
        }

        /// <summary>
        /// 平らな海を作る。地形を海面より <paramref name="depthMetres"/> だけ下げ、
        /// 水をちょうど海面まで張る。
        ///
        /// ★ 張り方は実機の「海面パス・分岐 A」と同じ（IL_1589-15A7）:
        ///   <c>h = min(seaLevel - terrain, 65535)</c>、0 以下ならセルごと空にする。
        /// </summary>
        public void FillFlatSea(float depthMetres)
        {
            int sea = SeaLevelUnits;
            int floor = sea - (int)(depthMetres * UnitsPerMetre);
            if (floor < 0) floor = 0;              // 地形は ushort。海面より深く掘れる限界
            if (floor > 65535) floor = 65535;

            for (int i = 0; i < _terrain.Length; i++)
            {
                _terrain[i] = (ushort)floor;

                Cell c = new Cell();
                int h = sea - floor;
                if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                _current[i] = c;
            }

            // もう一方の配列にも同じものを入れておく。実機では「今フレーム触られなかった
            // セル」が古い値のまま残るが、こちらは毎フレーム全セルを走査するので
            // 1 フレーム目の書き込みで完全に上書きされる。念のため揃えておく。
            Array.Copy(_current, (_current == _bufferA) ? _bufferB : _bufferA, _current.Length);
        }

        /// <summary>
        /// **深い海 → 大陸棚 → 海岸 → 陸**の断面を作る（+X 方向に浅くなる）。
        ///
        /// ★★ **陸の無い海で取った保証は、海岸には及ばない。**（2026-08-30）
        ///   ソルバの流量は <c>v = min(v, m_height)</c> で<b>水深に頭打ち</b>される。
        ///   実際の津波は浅瀬でせり上がる（shoaling）が、<b>この解法は逆に絞る</b> ——
        ///   深い海で 20 m あった波も、10 m の棚に乗れば 1 歩に 10 m しか運べない。
        ///   実機報告「震源では 82 m なのに海岸では高潮程度」はこれで説明が付く。
        ///   だから<b>海岸で何 m 届くか</b>を測れるようにする。
        /// </summary>
        /// <param name="deepMetres">沖の水深（m）。</param>
        /// <param name="shelfStartCells">この列より +X 側で浅くなりはじめる。</param>
        /// <param name="shoreCells">この列で水深 0（＝汀線）。以降は陸。</param>
        /// <param name="landRiseMetres">汀線から先、1 セルあたり何 m 上がるか。</param>
        public void FillShelf(float deepMetres, int shelfStartCells, int shoreCells,
                              float landRiseMetres)
        {
            int sea = SeaLevelUnits;

            for (int z = 0; z < Size; z++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float depth;

                    if (x <= shelfStartCells)
                    {
                        depth = deepMetres;
                    }
                    else if (x < shoreCells)
                    {
                        float k = (x - shelfStartCells)
                                  / (float)(shoreCells - shelfStartCells);
                        depth = deepMetres * (1f - k);
                    }
                    else
                    {
                        // 陸。汀線から離れるほど高い。
                        depth = -(x - shoreCells) * landRiseMetres;
                    }

                    int floor = sea - (int)(depth * UnitsPerMetre);
                    if (floor < 0) floor = 0;
                    if (floor > 65535) floor = 65535;

                    int i = z * Size + x;
                    _terrain[i] = (ushort)floor;

                    Cell c = new Cell();
                    int h = sea - floor;
                    if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                    _current[i] = c;
                }
            }

            Array.Copy(_current, (_current == _bufferA) ? _bufferB : _bufferA,
                       _current.Length);
        }

        /// <summary>その列の海面が平常からどれだけ上がっているか（m）。</summary>
        public float ColumnRiseMetres(int x, int z)
        {
            int i = z * Size + x;
            if (i < 0 || i >= _terrain.Length) return 0f;

            float surface = (_terrain[i] + _current[i].Height) / (float)UnitsPerMetre;
            return surface - SeaLevel;
        }

        /// <summary>
        /// <b>1 回の <c>SimulateWater</c>。</b>
        /// </summary>
        /// <param name="impulses">この 1 フレームに効かせる TYPE_IMPACT の波。null 可。</param>
        public void Step(List<Impulse> impulses)
        {
            // ── 海面のラッチ（IL_0000-004C）────────────────────────────
            //   ★ 先頭で m_currentSeaLevel ← m_nextSeaLevel を**即座に**書く。
            //     つまり oldSea は「前フレームの海面」で、seaLevelPass は
            //     海面が変わったフレームにちょうど 1 回だけ立つ。
            float oldSea = _appliedSeaLevel;
            float newSea = SeaLevel;
            bool reset = _resetWater;
            _appliedSeaLevel = newSea;
            _resetWater = false;

            int oldSea64 = (int)(oldSea * 64f);
            int newSea64 = (int)(newSea * 64f);
            bool seaLevelPass = (newSea64 != oldSea64) || reset;   // loc5, IL_0040-004C

            // ── 位相マスク（IL_0234-02B8）──────────────────────────────
            //   4 つのうち<b>ちょうど 1 つだけ</b>が全ビット 1 になる。
            //   これは流量を按分するときの割り算を「切り上げ」に変える項で、
            //   4 方向を 1 ステップずつ順に切り上げることで**丸めの偏りを消す**。
            //   ★ ここを落とすと水が系統的に減り、津波の高さが合わなくなる。
            int phase = _stepIndex & 3;
            int mask0 = (phase == 0) ? int.MaxValue : 0;   // loc33 → 左隣の velocityX
            int mask1 = (phase == 1) ? int.MaxValue : 0;   // loc34 → 自分の velocityX
            int mask2 = (phase == 2) ? int.MaxValue : 0;   // loc35 → 上隣の velocityZ
            int mask3 = (phase == 3) ? int.MaxValue : 0;   // loc36 → 自分の velocityZ
            int maskPollution = (phase <= 1) ? int.MaxValue : 0;  // loc37, IL_029F-02B8

            // ── 蒸発（IL_02BA-02CF、適用は IL_07D4 ほか）────────────────
            //   ★ **4 フレームに 1 回、水のあるセルから 1/64 m ずつ減る。**
            //     津波の寿命のあいだ効き続ける、無視できない排水である。
            ushort evaporation = (ushort)((phase == 0) ? 1 : 0);
            ushort pollutionDecay = (ushort)(((_stepIndex & 15) < PollutionDisposeRate) ? 1 : 0);

            // ── 乱数（IL_02EA-0300）────────────────────────────────────
            //   種は**増やす前**の m_stepIndex。1 フレームに 1 個だけ作る。
            _rng = new Lcg((ulong)(long)_stepIndex);   // Randomizer::.ctor(Int32) は conv.i8（符号拡張）
            _stepIndex = _stepIndex + 1;

            // ── 二重バッファ（IL_0213-0232）────────────────────────────
            //   流速パスは src を読んで dst に書く。輸送・海面・外周は dst を
            //   読み書きする。**同じ配列 1 本では再現できない。**
            Cell[] src = _current;
            Cell[] dst = (src == _bufferA) ? _bufferB : _bufferA;

            // ── 唯一の外側ループ（IL_049D / IL_181B）──────────────────
            //   実機は -5 から始めるが、先頭の 2 反復は描画タイル段のためだけの
            //   助走で、物理には効かない。ここでは流速段の助走ぶん -3 から。
            for (int cursor = -3; cursor <= _last; cursor++)
            {
                int zFlow = cursor + 3;                     // IL_0670-0674
                if (zFlow >= 0 && zFlow <= _last)
                    VelocityRow(zFlow, src, dst, impulses, evaporation, pollutionDecay,
                                mask0, mask1, mask2, mask3);

                int zMove = cursor;                          // IL_0FEC-0FEE
                if (zMove < 0 || zMove > _last) continue;

                TransferRow(zMove, dst, maskPollution);

                if (seaLevelPass)
                    SeaLevelRow(zMove, dst, oldSea64, newSea64, reset);

                RingRow(zMove, dst, newSea64, maskPollution);
            }

            _current = dst;
        }

        /// <summary>
        /// 海面パス（IL_153C-168B）。<b>海面が動いたフレームだけ</b>走る。
        /// 海面を固定して回すぶんには一度も走らない —— つまり内側のセルには
        /// 「海面へ引き戻す力」が<b>まったく無い</b>。波はそれ自体の慣性だけで走る。
        /// </summary>
        private void SeaLevelRow(int z, Cell[] buf, int oldSea64, int newSea64, bool reset)
        {
            int i = z * _n;
            for (int x = 0; x <= _last; x++, i++)
            {
                Cell c = buf[i];
                int terrain = _terrain[i];
                int surface = terrain + c.Height;

                if (c.Height == 0 || reset)
                {
                    // 分岐 A: 乾いたセル、または全面リセット。新しい海面まで一気に張る。
                    // ★ 汚染と流速は**消さない**（IL_1598-15A7 は m_height しか書かない）。
                    int h = newSea64 - terrain;
                    if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                    else c = new Cell();               // initobj: 4 フィールドとも 0（IL_15D4）
                }
                else
                {
                    // 分岐 B: 水のあるセル。海面の差ぶんだけずらすが、
                    // **旧海面より 128（= 2 m）以上高い水面は差を減らして守る**（IL_15E7-1634）。
                    // 山の上の湖が海面変動で溢れないための細工。
                    int delta = newSea64 - oldSea64;
                    if (surface > oldSea64 + 128)
                    {
                        int excess = surface - oldSea64 - 128;
                        if (delta < 0) { delta += excess; if (delta > 0) delta = 0; }
                        else { delta -= excess; if (delta < 0) delta = 0; }
                    }
                    int h = c.Height + delta;
                    if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                    else c = new Cell();
                }
                buf[i] = c;
            }
        }

        /// <summary>
        /// 外周の輪（IL_1690-1810）。<b>毎フレーム走る Dirichlet 境界。</b>
        ///
        /// 実機ではここが<b>津波の唯一の入口</b>で、<c>WaterWave.GetSeaLevel</c> を通した
        /// 海面へ外周セルを強制的に合わせる。こちらは外力（<see cref="Impulse"/>）で
        /// 内側から駆動するので、輪は<b>ただの海面固定</b>として使う —— 要件どおり。
        ///
        /// ★ z が 0 か最終行のときだけ全 x を回り、それ以外の行では x = 0 と x = 最終列
        ///   だけを触る（IL_1699-16B1 の歩幅）。つまり触るのはちょうど盤面の縁だけ。
        /// </summary>
        private void RingRow(int z, Cell[] buf, int baseLevel, int maskPollution)
        {
            int step = (z == 0 || z == _last) ? 1 : _last;
            int i = z * _n;

            for (int x = 0; x <= _last; x += step, i += step)
            {
                // ★★ 実機はここで WaterWave.GetSeaLevel を通す（IL_16C7-16F9）。
                //    津波の入口はこの 1 行だけである。
                int level = (Edge == null) ? baseLevel : Edge.LevelAt(x, z, baseLevel);

                Cell c = buf[i];
                int excess = _terrain[i] + c.Height - level;   // IL_170E-171E

                if (excess > 0 && c.Height != 0)
                {
                    // 余った水は**捨てる**。波は縁から出ていって戻ってこない。
                    if (excess > c.Height) excess = c.Height;
                    if (c.Pollution != 0)
                    {
                        int p = (excess * c.Pollution + ((c.Height - 1) & maskPollution)) / c.Height;
                        c.Pollution = (ushort)(c.Pollution - p);   // ★ この汚染はどこにも行かず消滅する
                    }
                    c.Height = (ushort)(c.Height - excess);
                    RingRemoved += excess;
                    buf[i] = c;
                }
                else if (excess < 0)
                {
                    // 足りなければ**湧かせる**。結果はちょうど level - terrain。
                    // ★ ここに 65535 の頭打ちが無いのは IL のまま（IL_17B9-17C6 は conv.u2 だけ）。
                    int before = c.Height;
                    c.Height = (ushort)(c.Height - excess);
                    RingAdded += c.Height - before;   // ushort へ落ちたあとの実増分で数える
                    buf[i] = c;
                }
                // excess == 0、または excess > 0 で水が無い場合は何もしない。
            }
        }
    }
}
