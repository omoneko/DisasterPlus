using System;
using System.Collections.Generic;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b>An offline reproduction of <c>WaterSimulation.SimulateWater</c>.</b>
    /// Plain .NET only — no UnityEngine and no Cities API.
    ///
    /// ── What it is for ─────────────────────────────────────────
    ///
    /// To tune the tsunami source (<see cref="Impulse"/>, a TYPE_IMPACT water wave)
    /// <b>without launching the game</b>. Feed in exactly the external force the mod applies
    /// in the real game and measure how many metres the sea surface rises and how far the
    /// wave runs.
    ///
    /// ── How it maps onto the game ───────────────────────────────────────────
    ///
    /// The game uses 1081x1081 (<c>Cell[(1080+1)*(1080+1)]</c>, Awake IL_002B-0032).
    /// Here it is <c>N x N</c> —— <b>the size of the grid has no effect on the per-cell
    /// physics</b>, so it is dropped to 512 to make checks faster. The index is <c>z*N + x</c>
    /// as in the game, and x and z both run <c>0 .. N-1</c> <b>inclusive at both ends</b>
    /// (the equivalent of the game's 0..1080).
    ///
    /// ★ <b>One call to <see cref="Step"/> is one call to SimulateWater.</b>
    ///   The game's SimulateWater processes <b>the whole board</b> in one go, not a strip
    ///   (the single outer loop <c>for (loc46 = -5; loc46 &lt;= 1080; loc46++)</c>,
    ///   IL_049D / IL_181B).
    ///
    /// ── A three-stage software pipeline ────────────────────────────
    ///
    /// The outer loop variable is not z. In a single iteration
    /// <list type="bullet">
    /// <item>the render tile (IL_04A6) ── **not physics, so not reproduced here**</item>
    /// <item>the velocity update: z = loc46 + 3 (IL_0670-0674)</item>
    /// <item>mass transfer + sea level + outer ring: z = loc46 (IL_0FEC-0FEE)</item>
    /// </list>
    /// each run on a different row. **The three-row offset is not decoration**:
    /// transfer reads the result of the velocity stage and the ring reads the result of
    /// transfer, so splitting it into two passes ("velocity for all rows, then transfer for
    /// all rows") changes the result.
    /// </summary>
    internal sealed partial class WaterField
    {
        /// <summary>Unit of height. 1 unit = 1/64 m (the same as the game).</summary>
        public const int UnitsPerMetre = 64;

        /// <summary>Side length of one cell (m). The game's loc6 = 16f (IL_004E).</summary>
        public const float CellSizeMetres = 16f;

        private readonly int _n;
        private readonly int _last;      // last index; the equivalent of the game's loc7 = 1080
        private readonly ushort[] _terrain;
        private readonly Cell[] _bufferA;
        private readonly Cell[] _bufferB;

        /// <summary>Whichever array currently holds the latest state.</summary>
        private Cell[] _current;

        /// <summary>The game's <c>m_stepIndex</c>. **It is the random seed and also the
        /// source of the phase masks.**</summary>
        private int _stepIndex;

        private Lcg _rng;

        /// <summary>The game's <c>m_currentSeaLevel</c> (m). The previous frame's sea
        /// level.</summary>
        private float _appliedSeaLevel;

        private bool _resetWater;

        /// <summary>The game's <c>m_nextSeaLevel</c> (m). Defaults to 40f
        /// (Awake IL_00F9-010E).</summary>
        public float SeaLevel;

        /// <summary>
        /// The DLC tsunami injected at the outer ring. If null, the ring simply holds the
        /// sea level fixed.
        ///
        /// ★★ **Only when this is present can the board "receive" water.**
        ///   See the class doc of <see cref="EdgeWave"/>.
        /// </summary>
        public EdgeWave Edge;

        /// <summary>
        /// The <c>WaterSource</c> (TYPE_NATURAL) placed at the epicentre. Does nothing if null.
        ///
        /// ★★ **This is the thing that "brings the boundary condition to the middle of the
        ///   map"** (the class doc of <see cref="SourceDisc"/>).
        /// </summary>
        internal SourceDisc Source;

        /// <summary>Circles laid out along the fault. If null, only <see cref="Source"/> is
        /// used.</summary>
        internal SourceDisc[] Sources;

        /// <summary>
        /// <c>m_finalPollutionDisposeRate</c>, the only argument of <c>SimulateWater</c>
        /// (IL_02D1-02E8). Pollution does not affect the motion of the water, so the default
        /// of 1 is fine.
        /// </summary>
        public int PollutionDisposeRate = 1;

        // ── Diagnostic counters (no counterpart in the game) ────────────────────────
        //   ★ They never touch the physics. They only break the change in total water volume
        //     down by "where it happened". In principle
        //       dV = RingAdded - RingRemoved - EvapRemoved - SatLost + SatGained
        //     should be an **identity**; if it is not, there is an unknown leak somewhere.
        public long RingAdded;      // water created by the outer ring
        public long RingRemoved;    // water discarded by the outer ring
        public long EvapRemoved;    // water lost to evaporation
        public long SatLost;        // water lost to Max(h-m,0) (drawn from a cell with no water)
        public long SatGained;      // water discarded by Min(h+m,65535) (the overflow past
                                    // 65535) -> in fact a loss

        public int Size { get { return _n; } }
        public int LastIndex { get { return _last; } }
        public ushort[] Terrain { get { return _terrain; } }
        public Cell[] Cells { get { return _current; } }
        public int StepIndex { get { return _stepIndex; } }

        /// <param name="n">Cells per side. The game uses 1081; 512 is enough for checks.</param>
        /// <param name="seaLevelMetres">Sea level (m). The game's default is 40f.</param>
        public WaterField(int n, float seaLevelMetres)
        {
            if (n < 4) throw new ArgumentOutOfRangeException("n", "grid too small (the outer ring collapses)");

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

        /// <summary>Sea level (1/64 m). The game's loc4 =
        /// <c>(int)(m_nextSeaLevel * 64f)</c> (IL_0036-003E).</summary>
        public int SeaLevelUnits { get { return (int)(SeaLevel * 64f); } }

        /// <summary>Index. The game uses <c>z*1081 + x</c> (IL_079D-07A7).</summary>
        public int Index(int x, int z) { return z * _n + x; }

        /// <summary>Elevation of the water surface (1/64 m) = terrain + water column.</summary>
        public int SurfaceUnits(int x, int z)
        {
            int i = z * _n + x;
            return _terrain[i] + _current[i].Height;
        }

        /// <summary>How many metres the water surface is above sea level (negative means
        /// below).</summary>
        public float SurfaceAboveSeaMetres(int x, int z)
        {
            return (SurfaceUnits(x, z) - SeaLevelUnits) / (float)UnitsPerMetre;
        }

        /// <summary>
        /// Builds a flat sea. Lowers the terrain <paramref name="depthMetres"/> below sea
        /// level and fills the water exactly up to sea level.
        ///
        /// ★ The filling matches the game's "sea level pass, branch A" (IL_1589-15A7):
        ///   <c>h = min(seaLevel - terrain, 65535)</c>, and if that is 0 or less the whole
        ///   cell is cleared.
        /// </summary>
        public void FillFlatSea(float depthMetres)
        {
            int sea = SeaLevelUnits;
            int floor = sea - (int)(depthMetres * UnitsPerMetre);
            if (floor < 0) floor = 0;              // terrain is ushort: max depth below sea level
            if (floor > 65535) floor = 65535;

            for (int i = 0; i < _terrain.Length; i++)
            {
                _terrain[i] = (ushort)floor;

                Cell c = new Cell();
                int h = sea - floor;
                if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                _current[i] = c;
            }

            // Put the same thing into the other array. In the game, "cells not touched this
            // frame" keep their old values, but here every cell is visited every frame, so the
            // writes of the first frame overwrite it completely. Match it anyway, to be safe.
            Array.Copy(_current, (_current == _bufferA) ? _bufferB : _bufferA, _current.Length);
        }

        /// <summary>
        /// Builds a **deep sea -&gt; continental shelf -&gt; shore -&gt; land** cross-section
        /// (shallowing towards +X).
        ///
        /// ★★ **A guarantee obtained on a sea with no land does not extend to the shore.**
        ///   (2026-08-30)
        ///   The solver's flow is <b>capped by the water depth</b> via
        ///   <c>v = min(v, m_height)</c>. A real tsunami piles up in shallow water (shoaling),
        ///   but <b>this scheme does the opposite and throttles it</b> —— a wave that was 20 m
        ///   in deep water can only carry 10 m per step once it is on a 10 m shelf.
        ///   That explains the report from the game: "82 m at the epicentre but only about
        ///   storm-surge height at the coast". So we make it possible to measure <b>how many
        ///   metres actually arrive at the shore</b>.
        /// </summary>
        /// <param name="deepMetres">Offshore water depth (m).</param>
        /// <param name="shelfStartCells">Shallowing begins on the +X side of this column.</param>
        /// <param name="shoreCells">Depth is 0 at this column (the shoreline). Beyond it is land.</param>
        /// <param name="landRiseMetres">How many metres it rises per cell beyond the shoreline.</param>
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
                        // Land. The further from the shoreline, the higher.
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

        /// <summary>How far the water surface in that column has risen above its normal
        /// level (m).</summary>
        public float ColumnRiseMetres(int x, int z)
        {
            int i = z * Size + x;
            if (i < 0 || i >= _terrain.Length) return 0f;

            float surface = (_terrain[i] + _current[i].Height) / (float)UnitsPerMetre;
            return surface - SeaLevel;
        }

        /// <summary>
        /// Thickness of the water sitting on that cell (m).
        ///
        /// ★★ **Always use this one when measuring over land.** <see cref="ColumnRiseMetres"/>
        ///   subtracts sea level from terrain + water column, so <b>it returns a positive
        ///   value equal to the elevation even on dry land</b>. Measuring the inundation
        ///   distance with that was why every condition reported "the whole of the land was
        ///   swallowed" (found on 2026-08-31; exactly the same pitfall that was hit in
        ///   <c>SeaWatch</c> on the game side).
        /// </summary>
        public float WaterDepthMetres(int x, int z)
        {
            int i = z * Size + x;
            if (i < 0 || i >= _terrain.Length) return 0f;
            return _current[i].Height / (float)UnitsPerMetre;
        }

        /// <summary>
        /// <b>One call of <c>SimulateWater</c>.</b>
        /// </summary>
        /// <param name="impulses">The TYPE_IMPACT waves to apply on this one frame. May be null.</param>
        public void Step(List<Impulse> impulses)
        {
            // ── Latching the sea level (IL_0000-004C) ────────────────────────────
            //   ★ At the very top it writes m_currentSeaLevel <- m_nextSeaLevel **immediately**.
            //     So oldSea is "the previous frame's sea level", and seaLevelPass is raised
            //     exactly once, on the frame where the sea level changed.
            float oldSea = _appliedSeaLevel;
            float newSea = SeaLevel;
            bool reset = _resetWater;
            _appliedSeaLevel = newSea;
            _resetWater = false;

            int oldSea64 = (int)(oldSea * 64f);
            int newSea64 = (int)(newSea * 64f);
            bool seaLevelPass = (newSea64 != oldSea64) || reset;   // loc5, IL_0040-004C

            // ── Phase masks (IL_0234-02B8) ──────────────────────────────
            //   <b>Exactly one</b> of the four is all-bits-one.
            //   This is the term that turns the division used to apportion flow into a
            //   "round up", and rounding up one direction per step in turn **cancels the
            //   rounding bias**.
            //   ★ Drop this and water is lost systematically, and the tsunami height no
            //     longer matches.
            int phase = _stepIndex & 3;
            int mask0 = (phase == 0) ? int.MaxValue : 0;   // loc33 -> left neighbour's velocityX
            int mask1 = (phase == 1) ? int.MaxValue : 0;   // loc34 -> own velocityX
            int mask2 = (phase == 2) ? int.MaxValue : 0;   // loc35 -> upper neighbour's velocityZ
            int mask3 = (phase == 3) ? int.MaxValue : 0;   // loc36 -> own velocityZ
            int maskPollution = (phase <= 1) ? int.MaxValue : 0;  // loc37, IL_029F-02B8

            // ── Evaporation (IL_02BA-02CF, applied at IL_07D4 and others) ────────────────
            //   ★ **Once every four frames, every cell with water loses 1/64 m.**
            //     It keeps acting for the whole life of the tsunami; it is drainage that
            //     cannot be ignored.
            ushort evaporation = (ushort)((phase == 0) ? 1 : 0);
            ushort pollutionDecay = (ushort)(((_stepIndex & 15) < PollutionDisposeRate) ? 1 : 0);

            // ── Random numbers (IL_02EA-0300) ────────────────────────────────────
            //   The seed is m_stepIndex **before** it is incremented. Only one is made per frame.
            _rng = new Lcg((ulong)(long)_stepIndex);   // Randomizer::.ctor(Int32) uses conv.i8 (sign extension)
            _stepIndex = _stepIndex + 1;

            // ── Double buffering (IL_0213-0232) ────────────────────────────
            //   The velocity pass reads src and writes dst. Transfer, sea level and the outer
            //   ring read and write dst. **A single array cannot reproduce this.**
            Cell[] src = _current;
            Cell[] dst = (src == _bufferA) ? _bufferB : _bufferA;

            // ── The single outer loop (IL_049D / IL_181B) ──────────────────
            //   The game starts at -5, but the first two iterations are run-up for the render
            //   tile stage only and have no effect on the physics. Here we start at -3, the
            //   run-up needed for the velocity stage.
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

            // ★★ The water sources come after the ring (IL_184A is after the outer-ring loop
            //    IL_1690-1810). It **must** be after the swap of _current —— put it before and
            //    the writes land in "the array that gets thrown away this frame", so the water
            //    source <b>amounts to nothing at all</b> (hit on 2026-08-31: noticed when
            //    intensity 100 and 255 produced output that did not differ by a single bit).
            if (Sources != null)
            {
                for (int k = 0; k < Sources.Length; k++) Sources[k].Apply(this);
            }
            else if (Source != null) Source.Apply(this);
        }

        /// <summary>
        /// The sea level pass (IL_153C-168B). Runs <b>only on frames where the sea level
        /// moved</b>. Running with a fixed sea level, it never runs at all —— that is, the
        /// interior cells have <b>no "force pulling them back to sea level" whatsoever</b>.
        /// The wave travels on its own inertia alone.
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
                    // Branch A: dry cell, or a full reset. Fill straight up to the new sea level.
                    // ★ Pollution and velocity are **not** cleared (IL_1598-15A7 only writes
                    //   m_height).
                    int h = newSea64 - terrain;
                    if (h > 0) c.Height = (ushort)Math.Min(h, 65535);
                    else c = new Cell();               // initobj: all four fields 0 (IL_15D4)
                }
                else
                {
                    // Branch B: a cell with water. Shift it by the sea level difference, but
                    // **a surface more than 128 (= 2 m) above the old sea level is protected by
                    // reducing that difference** (IL_15E7-1634). A trick to keep lakes up in the
                    // mountains from overflowing when the sea level changes.
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
        /// The outer ring (IL_1690-1810). <b>A Dirichlet boundary that runs every frame.</b>
        ///
        /// In the game this is <b>the tsunami's only entrance</b>: it forces the outermost
        /// cells to match the sea level as returned by <c>WaterWave.GetSeaLevel</c>. Here we
        /// drive from the inside with an external force (<see cref="Impulse"/>), so the ring
        /// is used as <b>nothing more than a sea level clamp</b> —— as required.
        ///
        /// ★ Only when z is 0 or the last row does it walk every x; on other rows it touches
        ///   only x = 0 and x = the last column (the stride at IL_1699-16B1). In other words
        ///   it touches exactly the edge of the board.
        /// </summary>
        private void RingRow(int z, Cell[] buf, int baseLevel, int maskPollution)
        {
            int step = (z == 0 || z == _last) ? 1 : _last;
            int i = z * _n;

            for (int x = 0; x <= _last; x += step, i += step)
            {
                // ★★ Here the game goes through WaterWave.GetSeaLevel (IL_16C7-16F9).
                //    This single line is the tsunami's entrance.
                int level = (Edge == null) ? baseLevel : Edge.LevelAt(x, z, baseLevel);

                Cell c = buf[i];
                int excess = _terrain[i] + c.Height - level;   // IL_170E-171E

                if (excess > 0 && c.Height != 0)
                {
                    // Surplus water is **discarded**. A wave leaves through the edge and
                    // never comes back.
                    if (excess > c.Height) excess = c.Height;
                    if (c.Pollution != 0)
                    {
                        int p = (excess * c.Pollution + ((c.Height - 1) & maskPollution)) / c.Height;
                        c.Pollution = (ushort)(c.Pollution - p);   // ★ this pollution goes nowhere; it vanishes
                    }
                    c.Height = (ushort)(c.Height - excess);
                    RingRemoved += excess;
                    buf[i] = c;
                }
                else if (excess < 0)
                {
                    // If there is not enough, water is **created**. The result is exactly
                    // level - terrain.
                    // ★ The absence of a 65535 clamp here is as in the IL (IL_17B9-17C6 is
                    //   just a conv.u2).
                    int before = c.Height;
                    c.Height = (ushort)(c.Height - excess);
                    RingAdded += c.Height - before;   // count the real gain after truncation to ushort
                    buf[i] = c;
                }
                // When excess == 0, or excess > 0 with no water, nothing is done.
            }
        }
    }
}
