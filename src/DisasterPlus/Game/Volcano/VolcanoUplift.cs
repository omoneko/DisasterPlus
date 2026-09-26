using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Which shape <see cref="VolcanoUplift"/> is writing right now.
    ///
    /// ── added for the super-eruption (2026-08-22, owner's request) ─────────────────────────
    ///
    /// &gt; a volcano forms as magma rises underground → the magma chamber grows over tens of
    /// &gt; thousands of years → internal pressure reaches its limit and a super-eruption (a huge
    /// &gt; explosion) follows → the ground founders under its own weight and a caldera forms
    ///
    /// <b>Only one mechanism writes terrain.</b> Taking the rectangle, the snapshot, the flush
    /// and the ceiling count are identical in all three; the only differences are <b>the radius
    /// of the rectangle, the profile that gets baked, and the rule for advancing</b>. So the
    /// stages live in the same type.
    /// </summary>
    public enum UpliftStage
    {
        /// <summary>Raise the cone (the only shape until now). **It spreads outwards from the summit.**</summary>
        Cone = 0,

        /// <summary>
        /// The magma chamber inflating. Lifts a **far wider and far lower** dome than the skirt,
        /// uniformly (<c>SuperEruption.InflationAt</c>).
        /// </summary>
        Inflation = 1,

        /// <summary>
        /// The caldera foundering. Digs a **flat-bottomed basin** down uniformly
        /// (<c>SuperEruption.BowlProfileAt</c>; the profile is negative).
        /// </summary>
        Collapse = 2,
    }

    /// <summary>
    /// Uplift — writing <c>RawHeights</c>, the split <c>UpdateArea</c>, and the summit crater.
    /// **Sim thread only. This is the one type in ⑤ that writes terrain.**
    ///
    /// ── ★★ the radius you may raise is decided by <c>VolcanoClearing.ClearedRadiusMetres</c> alone ──
    ///
    /// <code>
    /// activeRadius = UpliftSchedule.ActiveRadiusMetres(
    ///                    footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres)
    /// </code>
    ///
    /// **Never pass anything else as the second argument** (plan trap 1). Raise ground the
    /// clearing has not reached and roads **pin** the cell to the road's y through
    /// <c>Heights.PrimaryLevel</c>, buildings pin it to the building's y through
    /// <c>SecondaryLevel</c> — and that is redone from scratch on every flush (§A-2).
    /// **You cannot win by writing harder.** Not a single exception is thrown; you simply get a
    /// "vaguely wrong mountain" — flat trenches and funnels inside the cone (design doc §1.2).
    /// If <c>ClearedRadiusMetres</c> is 0, <c>ActiveRadiusMetres</c> returns 0 and this type
    /// writes not one cell. **The Core-side tests pin that 0.**
    ///
    /// **Review grep**: there is exactly one call to <c>ActiveRadiusMetres</c> in this file, and
    /// its second argument is <c>VolcanoClearing.ClearedRadiusMetres</c>.
    ///
    /// ── write the absolute target for that instant, not an increment (trap 2) ──────────────
    ///
    /// The naive <c>raw[i] += step</c> **always breaks silently**. <c>RawHeights</c> is a
    /// <c>ushort</c> in units of <c>raw/64</c> metres, and the writer skips with
    /// <c>if (n != raw)</c> (§C-8 IL_01E7). If one tick's change in cell height falls below
    /// 1/64 m = 0.015625 m, it **vanishes in the rounding and that cell never moves again**.
    /// The outer rim rises slowest, so with the naive version **the skirt alone stops dead from
    /// the very start**.
    ///
    /// With an absolute target (<c>UpliftSchedule.RawTargetAt</c>), a cell that has not yet
    /// reached one raw unit is merely "not written"; its progress is accumulating in the
    /// <c>progress</c> float. Only the summit must move every tick, so
    /// <c>UpliftSchedule.TotalTicksFor</c> clamps the requested tick count by <c>H×64</c>.
    /// **Do not remove that clamp.**
    ///
    /// ── the mountain does not swell uniformly; it spreads outwards from the summit ─────────
    ///
    /// A cell's target is **not** <c>profile × progress</c>. That makes the whole mountain swell
    /// by the same fraction, so the finished cone looks as if it silently inflated out of the
    /// ground. SimCity 4's uplift is the other way round: the erupted material **piles up** into
    /// a mountain. The formula is the single line in <c>UpliftSchedule.GrowthMetresAt</c>:
    ///
    /// <code>
    /// grown(d, p) = max(0, profile(d) − H·(1 − p))
    /// </code>
    ///
    /// That is, "sink the final shape into the ground by H(1−p); whatever sticks out is the
    /// mountain right now". For a straight cone (stratovolcano) the front sits exactly at
    /// <c>R·p</c>, which meshes with the clearing front that
    /// <c>UpliftSchedule.ClearingFrontMetres</c> runs ahead of it.
    ///
    /// **It is in fact more robust against trap 2.** Every cell that is growing rises at <b>the
    /// same speed</b> <c>H/totalTicks</c> (<see cref="RiseMetresPerTick"/>), so the clamp in
    /// <c>TotalTicksFor</c> covers **every cell**. With <c>profile × progress</c> the per-tick
    /// change shrinks towards the rim, and "cells with a small total rise" were lost to rounding.
    ///
    /// ── the relief on the flanks is baked once, at start (<see cref="_profile"/>) ──────────
    ///
    /// The shape comes from <c>Core/Volcano/VolcanoRelief</c>. It **never exceeds** either the
    /// radius R or the final height H — the effective radius only ever moves inwards and the
    /// relief only ever cuts away (it is built from multiplications alone; see that class's doc).
    /// With the setting <c>ModSettings.VolcanoReliefStrength</c> at 0 it falls back to
    /// <c>VolcanoShape.ProfileAt</c> itself, **bit-for-bit identical to today's output**.
    /// It costs roughly 300 flops per cell, so **do not call it every tick**.
    ///
    /// ── <see cref="_baseRaw"/> is snapshotted exactly once, at start ───────────────────────
    ///
    /// **Never re-read it each tick.** Re-read it and the value ⑤ wrote last tick becomes the
    /// "original height", so **the profile is accumulated every tick and the mountain grows to
    /// the ceiling**. The real cost is one <c>ushort</c> per cell of the affected rectangle
    /// (31 KB at radius 1 km, 123 KB at 2 km, 279 KB at 3 km. §E-13).
    ///
    /// > **Do not use <c>TerrainManager.BackupHeights</c> / <c>UndoBuffer</c>** (§E-13).
    /// > Those belong to <c>TerrainTool</c> / <c>DistrictTool</c>, and <c>TerrainTool.OnEnable</c>
    /// > re-copies the whole of <c>RawHeights</c> —
    /// > **the moment the player opens the terrain tool, ⑤'s snapshot is gone.**
    /// >
    /// > **Do not mistake this array for the basis of an undo feature.** ⑤ has no way to remove
    /// > a volcano (design doc §1.3, "irreversible is fine"). Its only use is
    /// > **to compute absolute targets relative to the original height**, and it is not saved.
    ///
    /// ── <c>UpdateArea</c> exactly once per tick (trap 3) ───────────────────────────────────
    ///
    /// <c>UpdateArea</c> **silently truncates**, without tiling, anything beyond 128×128 raw
    /// cells (§A-1, <c>Min(m_maxX, m_minX + 120 + 8)</c>). On top of that, **a single request
    /// covering more than 10000 cells ignores the enclosing batch and flushes immediately**.
    /// <c>TileSplit</c> returns a rectangle that satisfies both at once (99×99 = 9801 cells).
    ///
    /// **Do not call all the tiles in one tick.** From the second tile on, the
    /// <c>merged &gt; 10000</c> test fires and you get a mid-batch flush every time (§A-1 IL_00A8).
    ///
    /// ── flush only the rectangle that actually changed (2026-08-20, live report ⑤) ─────────
    ///
    /// The report from the live game was "make the eruption animation smoother (right now it
    /// heaves up in steps)". **The visible step is not "the rise in one tick" but "the rise
    /// between two flushes of that tile".**
    ///
    /// Previously the whole footprint (151×151 at the default R=1200 m) was split into 4 tiles
    /// and flushed **one tile per tick, round robin**, so a given tile was re-flushed only every
    /// 4 ticks = 64 frames. At 600 m / 85 ticks = 7.06 m/tick, **the visible step was 28 m**,
    /// and it jumped one quarter at a time in sequence. Exactly what was reported.
    ///
    /// The fix does two things at once:
    ///
    ///   1. Take <see cref="IntervalFrames"/> from 16 to 4 (tick count 85 → 341, per-tick rise
    ///      7.06 m → 1.76 m). **The in-game duration is unchanged** (still 30 in-game minutes),
    ///      so neither the chase against the clearing (<c>VolcanoClearing</c>) nor the relation
    ///      to the eruption envelope changes in any way.
    ///   2. Flush **the bounding rectangle of the cells actually rewritten in that tick**.
    ///      The uplift spreads outwards from the summit (<c>UpliftSchedule.GrowthMetresAt</c>),
    ///      so the only thing changing is the disc of radius R×progress. For the default
    ///      stratovolcano it fits inside 95×95 cells up to progress 0.63, and during that window
    ///      **the whole changed area is flushed by the single <c>UpdateArea</c> of each tick**
    ///      = the visible step is 1.76 m at 15 Hz (speed 1).
    ///
    /// Once it no longer fits (past progress 0.63) it falls back to the round-robin tiling as
    /// before — even there it is 4 ticks × 4 frames = 16 frames, so the visible step is 7.0 m at
    /// 3.75 Hz, four times finer than the old 28 m at 0.94 Hz.
    ///
    /// **The <c>UpdateArea</c> count stays at one per tick** and the rectangle handed over still
    /// never exceeds 99×99 = 9801 cells (<c>TileSplit.FitsSinglePass</c> only permits a single
    /// pass at 95 cells or fewer). All that goes up is the **frequency**. The mean flushed area
    /// goes from 356 to about 1,370 cells/frame (default stratovolcano, offline estimate).
    /// **The peak per frame is unchanged.**
    ///
    /// No steps appear at the joins — <c>TileAt</c> / <c>ExpandForPass</c> both return a ±2 cell
    /// overlap.
    ///
    /// ── ★★ the crater is not a hole carved last but a hollow present from the start (2026-08-22, report ①) ──
    ///
    /// The owner's report was
    /// "it would be better if the crater were generated as a hollow from the start rather than
    /// being created last".
    ///
    /// This code used to call <c>DisasterHelpers.MakeCrater</c> once at the end of the uplift.
    /// That calls <c>TerrainModify.RefreshAllModifications()</c> up front (§C-8 IL_0006), so
    /// **every call forces one flush** and it cannot sit on the per-tick path — which means the
    /// "once, at the end" constraint was itself deciding when the hollow came into being.
    ///
    /// **The crater is now folded into <see cref="_profile"/> itself**
    /// (<c>Core/Volcano/VolcanoCrater</c>). The uplift writes "the absolute target at this
    /// instant" every tick, so if the target shape has a hollow in it, the hollow grows along
    /// with the mountain. The rim rises as <c>H·p</c>, the floor as <c>max(0, H·p − depth)</c>,
    /// and the hollow is at full depth from progress <c>depth/H</c> (10 % for the default
    /// stratovolcano) onwards, constant from then on.
    /// <b>⑤ no longer calls <c>MakeCrater</c> from anywhere</b> (one forced flush gone with it).
    ///
    /// **Review grep** (drop the comment lines; expect 0 hits):
    /// <code>
    /// grep -rn --include=*.cs "MakeCrater" src/DisasterPlus/ \
    ///   | grep -vE ':[0-9]+: *//' | grep -v '///' | wc -l          # -> 0
    /// </code>
    ///
    /// ── things that must not be called (§D-12) ─────────────────────────────────────────────
    ///
    /// **⑤ must not call <c>Begin/EndUpdateArea</c>.**
    /// <c>SimulationManager.SimulationStep</c> calls <c>BeginUpdateArea</c> at the top and
    /// <c>EndUpdateArea</c> at the bottom, and every mod sim-tick hook sits inside that.
    /// If ⑤ adds its own, <c>m_modifyingLevel</c> merely goes up by one and back down, and the
    /// inner <c>End</c> is swallowed by <c>if (loc1 != 0) return</c> (IL_0017). Harmless, but
    /// also pointless — all it leaves behind is the illusion that you are batching.
    /// **Review grep** (★ actually run, with the counts made to match. Whole-project review M12 —
    /// run plainly, **the sentences of this very rule** produced 4 hits, so the "0 hits"
    /// procedure never held in the first place. Drop the comment lines):
    ///
    /// <code>
    /// grep -rn --include=*.cs -E "BeginUpdateArea|EndUpdateArea" src/DisasterPlus/ \
    ///   | grep -vE ':[0-9]+: *//' | wc -l          # -> 0
    /// </code>
    ///
    /// <c>grep -v '///'</c> is not enough — single <c>//</c> comments have to be dropped too.
    ///
    /// **Calling <c>UpdateArea</c> from the main thread is the genuinely dangerous one.**
    /// There <c>m_modifyingLevel == 0</c>, so it flushes every time and races the partial
    /// accumulation the sim thread is building up. All of ⑤'s terrain writes live in this type
    /// (sim thread).
    ///
    /// ── buildable ground and the water level lag behind. That is not a bug (design doc §7.3) ──
    ///
    /// <c>m_blockHeights</c> only moves upwards by 2 m per 64 sim frames in game mode (§A-2),
    /// and <c>WaterSimulation.m_heightBuffer</c> is **that very array** (§A-4). The visuals
    /// (<c>m_finalHeights</c> / <c>m_detailHeights</c>) change at once, but a 600 m mountain
    /// takes 300 × 64 = 19200 sim frames ≒ 7 in-game hours to catch up.
    /// **Do not slow the uplift down to match this speed** — matching it is pointless, because
    /// <c>m_blockHeights</c> still only moves once every 64 frames. Report the estimate on the
    /// panel and in the diagnostics.
    ///
    /// ── the per-tick work budget (stated explicitly) ───────────────────────────────────────
    ///
    /// The interval is the **elapsed in-game time** worth of <see cref="IntervalFrames"/> frames
    /// (never <c>frameIndex % N</c>; firestorm appendix A-4). The reason it is not every sim tick
    /// is the feedback loop of §A-3: every time a building with <c>m_flattenTerrain == false</c>
    /// moves, <c>BuildingManager.SimulationStepImpl</c> emits an extra <c>UpdateArea</c>.
    /// The clearing stage has removed the buildings inside the footprint, so all that remains is
    /// the buildings ⑤ could not destroy, and <c>VolcanoClearing.LastBuildingsRefused</c> states
    /// that scale directly (it goes into the diagnostics).
    ///
    /// One <c>float[]</c> (<see cref="_profile"/>) is baked once at start. The real cost is one
    /// float per cell of the affected rectangle: 378² × 4 B = 558 KB at the 3 km maximum.
    /// Once baked, the per-tick work is a single subtraction, which is **actually cheaper** than
    /// today's "<c>sqrt</c> + <c>ProfileAt</c>".
    ///
    /// The per-tick budget is <b>one array write per cell of the affected rectangle</b> plus
    /// <b>exactly one <c>UpdateArea</c> (99×99 = 9801 cells)</b>.
    /// The rectangle is <c>(2R/16 + 3)²</c> cells at radius R — 153² ≒ 23,409 for the default
    /// stratovolcano (R=1200 m), and 378² ≒ 142,884 even at the largest form (R=3000 m).
    /// Writing to <c>RawHeights</c> is **just an array write**; nobody reads it until
    /// <c>UpdateArea</c> is called (§D-12).
    ///
    /// ── the file is split in two ───────────────────────────────────────────────────────────
    ///
    /// The part that actually touches terrain (writing heights, <c>UpdateArea</c>, the crater)
    /// lives in <c>VolcanoUplift.Terrain.cs</c>. The split is because of the project's 800-line
    /// rule, and **this class doc holds all of the discipline**
    /// (the same shape as <c>VolcanoClearing.Sweep.cs</c> / <c>VolcanoLava.Ignite.cs</c>).
    /// </summary>
    public static partial class VolcanoUplift
    {
        /// <summary>
        /// The uplift interval (in-game time equivalent to this many frames).
        /// **Not every sim tick** (§A-3).
        ///
        /// 16 → 4 (2026-08-20, live report ⑤). **The in-game duration is unchanged** —
        /// the tick count is <c>VolcanoUpliftMinutes</c> (default 30 in-game minutes = 1365 sim
        /// frames) divided by this interval, so quartering the interval merely quadruples the
        /// ticks and quarters the per-tick rise. The clearing front
        /// (<c>ClearingFrontMetres</c>) is a function of progress, and progress advances on
        /// in-game time, so it **does not change**.
        ///
        /// **Do not shorten it further.** One <c>UpdateArea</c> walks the target rectangle at
        /// detail resolution (4×4 per raw cell) and calls <c>SmoothSample</c> five times per cell
        /// (§A-1). That is about 780,000 calls for a 99×99 tile. Halve the interval and the
        /// per-frame average doubles accordingly.
        ///
        /// ★ <b>One step is at most once per sim tick.</b> <c>_minutesSinceTick</c> is capped at
        ///   <c>interval</c>, so no remainder is carried over, and <c>m_currentFrameIndex</c>
        ///   advances by <c>FinalSimulationSpeed</c> per tick (1, and up to 9 at speeds 1/2/3).
        ///   So the effective interval of one step is
        ///   <c>max(IntervalFrames, FinalSimulationSpeed)</c> frames, which means
        ///   **at speed 2 / 3 the uplift takes longer in in-game time**
        ///   (30 minutes → 45 / 67 minutes at the defaults; design doc §4.3, the table).
        ///   Stretching is the safe direction — the clearing (64 frame interval) gains slack as
        ///   progress slows, and the eruption stays parked at the entrance to its sustain phase
        ///   for as long as the mountain is growing.
        ///   **Do not compensate by issuing two <c>UpdateArea</c> calls in one tick.**
        /// </summary>
        private const int IntervalFrames = 4;

        /// <summary>Cells per row of <c>RawHeights</c> (1081², §C-8).</summary>
        private const int RawStride = 1081;

        /// <summary>The expected length of <c>RawHeights</c>. If it does not match, not one cell is written.</summary>
        private const int RawLength = RawStride * RawStride;

        /// <summary>
        /// The "original height" snapshotted at start. **Never re-read each tick** (class doc).
        /// Dropped once the crater is carved.
        /// </summary>
        private static ushort[] _baseRaw;

        /// <summary>
        /// The "rise of the final shape" (m), baked once at start. Same layout as <see cref="_baseRaw"/>.
        ///
        /// <c>VolcanoRelief.ProfileAt</c> costs about 300 flops per cell, so **do not call it for
        /// every cell every tick**. Once baked, the per-tick work is just
        /// <c>UpliftSchedule.GrowthMetresAt</c> (a single subtraction), which is **actually
        /// cheaper** than today's "sqrt + ProfileAt".
        /// It is held as float so that at strength 0 the output is bit-for-bit identical to
        /// today's (rounding it into raw units would introduce a 1/64 m double-rounding error).
        /// 378² × 4 B = 558 KB at the 3 km maximum. Dropped once the crater is carved.
        /// </summary>
        private static float[] _profile;

        private static int _minX, _minZ, _maxX, _maxZ;
        private static int _width;

        private static int _tileCount;

        /// <summary>
        /// Decides **which rectangle is flushed when** (<c>Core/Volcano/UpliftFlushPlan</c>).
        /// The decision is integer arithmetic only, so it lives in Core, where the unit tests and
        /// <c>tools/VolcanoPreview</c> can run the same thing without launching the game.
        /// </summary>
        private static readonly UpliftFlushPlan _flush = new UpliftFlushPlan();

        /// <summary>Bounding rectangle of the cells actually rewritten this tick. Valid only when <see cref="_dirtyValid"/>.</summary>
        private static int _dirtyMinX, _dirtyMinZ, _dirtyMaxX, _dirtyMaxZ;
        private static bool _dirtyValid;

        private static int _tick;
        private static int _totalTicks;

        private static float _progress;
        private static float _riseMetresPerTick;
        private static float _activeRadius;
        private static float _summitMetres;
        private static int _cellsWrittenLastTick;

        /// <summary>
        /// The number of cells **clipped against the game's height ceiling (1024 m)** in the last
        /// tick. If it is not 0, the summit has gone flat.
        /// The reason the ceiling cannot be raised is in the doc of <c>UpliftSchedule.CeilingClipped</c>.
        /// </summary>
        private static int _ceilingClippedCells;

        private static bool _started;
        private static bool _complete;

        /// <summary>Which shape is being written now. Only <see cref="StartStage"/> sets it.</summary>
        private static UpliftStage _stage = UpliftStage.Cone;

        /// <summary>Radius of the cone body (m). Only the caldera stage reads it.</summary>
        private static float _coneRadiusMetres;

        /// <summary>Seed for the roughness of the caldera floor. <see cref="BakeProfile"/> sets it.</summary>
        private static uint _floorSeed;

        /// <summary>Final height of this mountain (m). **Used to derive the crater depth and floor height.**</summary>
        private static float _heightMetres;

        /// <summary>
        /// The factor by which the cone was rebuilt so that the crater rim reaches H
        /// (<c>VolcanoCrater.SummitScale</c>).
        /// **It is also needed to decide the clearing front** — the uplift front runs ahead by
        /// this factor, so without it the clearing cannot keep up and the outer rim of the
        /// mountain appears to stop at a sheer circle.
        /// </summary>
        private static float _summitScale = 1f;

        private static Vec3 _centre;
        private static float _minutesSinceTick;

        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>Uplift progress [0,1]. **It does not advance while the clearing has not reached.**</summary>
        public static float ProgressUnit { get { return _progress; } }

        /// <summary>
        /// The radius (m) that may be raised this tick. **= the range the clearing has reached**
        /// (class doc). The panel shows it labelled "the range the clearing has reached" — it is
        /// the one line that lets you verify trap 1 with your own eyes in the live game.
        /// </summary>
        public static float ActiveRadiusMetres { get { return _activeRadius; } }

        /// <summary>The current rise of the summit (m). Relative to the original terrain height.</summary>
        public static float SummitMetres { get { return _summitMetres; } }

        /// <summary>
        /// The number of cells whose summit was **clipped by the game's height ceiling** (last tick).
        /// **A non-zero value is not a bug, but it must not go unmentioned either** — put a large
        /// mountain on high ground and this rises, flattening the summit.
        /// The ceiling is 1023.98 m, and **a mod cannot raise it**
        /// (the doc of <c>UpliftSchedule.CeilingClipped</c> has the IL measurements and the why).
        /// </summary>
        public static int CeilingClippedCells { get { return _ceilingClippedCells; } }

        /// <summary>
        /// How much a growing cell rises in one tick (m). **In an uplift that spreads outwards
        /// from the summit, every cell rises at the same speed**
        /// (the doc of <c>UpliftSchedule.GrowthMetresAt</c>).
        ///
        /// <c>VolcanoLava</c> uses this as "the tolerance for the terrain rising underneath it" —
        /// lava emitted while the uplift is still running can find that the elevation ahead of it
        /// has risen **because of the mountain, not because of the lava**, and without allowing
        /// for that the observation of the downhill gradient's sign fires spuriously.
        /// It is 0 once the uplift has finished.
        /// </summary>
        public static float RiseMetresPerTick
        {
            get { return _complete ? 0f : _riseMetresPerTick; }
        }

        /// <summary>
        /// How much a growing cell rises in **one sim frame** (m).
        ///
        /// ★ Do not hand <see cref="RiseMetresPerTick"/> straight to another feature.
        ///   ⑤'s stages have different intervals (uplift <see cref="IntervalFrames"/> = 4,
        ///   lava 8), so "one tick" is not the same length for each. **What the lava needs is
        ///   "how far the terrain rises during one step of its own"**, which is this value times
        ///   the lava's own interval.
        ///   This unit is what stops the lava's tolerance going wrong the moment the uplift
        ///   interval is changed.
        /// </summary>
        public static float RiseMetresPerFrame
        {
            get { return _complete ? 0f : _riseMetresPerTick / IntervalFrames; }
        }

        /// <summary>Whether the uplift has finished.</summary>
        public static bool Complete { get { return _complete; } }

        /// <summary>The shape being written now. Used for diagnostics and for the state machine to tell the stages apart.</summary>
        public static UpliftStage Stage { get { return _stage; } }

        /// <summary>
        /// Whether the summit hollow has reached its full depth. **Not "whether it was carved"** —
        /// the crater is part of the shape, so it is there from the first tick, and the depth is
        /// complete once the rim has risen by <c>depth</c> (class doc).
        /// </summary>
        public static bool CraterFormed
        {
            get { return VolcanoCrater.FullDepthReached(_summitMetres, _heightMetres); }
        }

        /// <summary>
        /// The current rise of the crater floor (m; relative to the original terrain height).
        /// It exists **purely to be reported in the diagnostics**.
        ///
        /// ★ **The vent's Y is not taken from here.** That one is re-sampled from the terrain at
        ///   the centre every tick by <c>VolcanoEruption.SampleVent</c>, so it is "the height
        ///   actually being drawn", lag of <c>UpdateArea</c> included.
        ///   This one is ⑤'s plan (the model-side value), so **if the two disagree, the terrain
        ///   is lagging behind**. That is what this line is for.
        /// </summary>
        public static float CraterFloorMetres
        {
            get { return VolcanoCrater.FloorMetresAt(_summitMetres, _heightMetres); }
        }

        /// <summary>
        /// How far out the uplift front currently reaches [0,1]. **What gets handed to the
        /// clearing (<c>VolcanoClearing</c>) is this, not the progress itself**
        /// (the doc of <c>UpliftSchedule.GrowthFrontUnit</c>).
        /// </summary>
        public static float GrowthFrontUnit
        {
            get { return UpliftSchedule.GrowthFrontUnit(_progress, _summitScale); }
        }

        /// <summary>Ticks elapsed so far.</summary>
        public static int Ticks { get { return _tick; } }

        /// <summary>Ticks used for the uplift (already clamped by <c>UpliftSchedule.TotalTicksFor</c>).</summary>
        public static int TotalTicks { get { return _totalTicks; } }

        /// <summary>Cells whose value actually changed in the last tick (0 means it is vanishing in the rounding).</summary>
        public static int CellsWrittenLastTick { get { return _cellsWrittenLastTick; } }

        /// <summary>
        /// **How many <c>UpdateArea</c> calls it takes, right now, to get the changed range fully
        /// on screen.**
        ///
        /// 1 means "the whole area changed this tick is flushed in the same tick" = the smoothest
        /// state (the first half of the uplift is here). 2 or more means it has been split and
        /// fallen back to round-robin tiling, and the visible step is the rise over that many
        /// flushes (the calculation is in the class doc).
        /// **It is not the tile count of the whole footprint** — that is <see cref="FootprintTileCount"/>.
        /// </summary>
        public static int TileCount { get { return _flush.TileCount; } }

        /// <summary>Which tile of the round robin. 0 when a single pass is enough.</summary>
        public static int TileCursor { get { return _flush.Cursor; } }

        /// <summary>Number of tiles covering the whole footprint (decided once at start).</summary>
        public static int FootprintTileCount { get { return _tileCount; } }

        /// <summary>
        /// The most recent reason nothing could be raised (**English, for diagnostics**). null if
        /// it was raised. This is the mouth that stops us **failing silently**.
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// Call when letting go of the volcano and on level unload. Idempotent.
        /// **Terrain already changed does not come back** (irreversible; design doc §1.3).
        /// All this folds away is the plan.
        /// </summary>
        public static void Reset()
        {
            // ★ Always drop the snapshot array. It is 279 KB at a 3 km radius, and carrying it
            //    across cities would mean claiming the previous city's terrain as the
            //    "original height".
            _baseRaw = null;
            _profile = null;
            _minX = 0;
            _minZ = 0;
            _maxX = 0;
            _maxZ = 0;
            _width = 0;
            _tileCount = 0;
            _flush.Reset();
            _dirtyValid = false;
            _dirtyMinX = 0;
            _dirtyMinZ = 0;
            _dirtyMaxX = 0;
            _dirtyMaxZ = 0;
            _tick = 0;
            _totalTicks = 0;
            _progress = 0f;
            _riseMetresPerTick = 0f;
            _activeRadius = 0f;
            _summitMetres = 0f;
            _cellsWrittenLastTick = 0;
            _ceilingClippedCells = 0;
            _started = false;
            _complete = false;
            _stage = UpliftStage.Cone;
            _coneRadiusMetres = 0f;
            _floorSeed = 0u;
            _heightMetres = 0f;
            _summitScale = 1f;
            _centre = new Vec3(0f, 0f, 0f);
            _minutesSinceTick = 0f;
            _lastFailure = null;

            // _errorLogged is not reset (it is a fact about the build of the game this DLL
            // references).
        }

        /// <summary>
        /// Sim thread. **Always call it from below the pause guard in
        /// <c>VolcanoFeature.OnSimulationTick</c>** (otherwise the mountain grows while paused).
        /// <paramref name="frame"/> is for diagnostics and **is not used to decide the period**
        /// (never <c>frameIndex % N</c>).
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes)
        {
            try
            {
                Step(footprint, deltaMinutes);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the uplift tick threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano uplift failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                             "volcano uplift failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre))
            {
                // ★ The only stage that starts on its own is **the cone**. Inflation and caldera
                //   are started explicitly by VolcanoState through StartStage.
                if (!StartStage(footprint, UpliftStage.Cone)) return;
            }

            if (_complete) return;

            // ★ Advance the interval accumulator before the subject (the same shape as
            //   TyphoonWind in ④).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceTick += deltaMinutes;
            if (interval > 0f && _minutesSinceTick > interval) _minutesSinceTick = interval;

            if (framesPerMinute <= 0f) return;
            if (_minutesSinceTick < interval) return;

            // ★★ **Trap 1. Nothing but this may be passed as the second argument.**
            _activeRadius = UpliftSchedule.ActiveRadiusMetres(
                footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres);

            // If the clearing has not reached by even a millimetre, do not advance the tick
            // either. Advancing it means "the progress finishes without anything being raised" —
            // the mountain completes without ever growing.
            // **The accumulator is not consumed**, so it runs immediately on the next tick after
            // the clearing arrives.
            if (!(_activeRadius > 0f)) return;

            _minutesSinceTick = 0f;

            _progress = UpliftSchedule.ProgressAt(_tick, _totalTicks);
            // The summit's profile is H, so the summit's rise is still H×progress.
            // ★★ **For the caldera it is not the summit but "how far the floor has dropped".**
            //    Put a positive value here and the volcano tab will call the foundering a
            //    "+400 m summit".
            _summitMetres = _stage == UpliftStage.Collapse
                ? -_riseMetresPerTick * _totalTicks * _progress
                : UpliftSchedule.GrowthMetresAt(
                      footprint.HeightMetres, footprint.HeightMetres, _progress);

            if (!WriteHeights(footprint)) return;
            FlushPending();

            if (_tick < _totalTicks)
            {
                _tick++;
                return;
            }

            // ★★ **Do not finish until the clearing has reached the outer rim.**
            //    Only the inside of activeRadius has been written, so cutting off here leaves
            //    the band between activeRadius and R **at its original height** — an annular
            //    step around the outside of the mountain. The clearing sweep runs on a longer
            //    interval than the uplift (64 frames against 4), so this wait happens routinely.
            //    Progress is already 1, so WriteHeights during the wait just fills in
            //    "as far as the clearing has reached" each tick.
            if (_activeRadius < footprint.RadiusMetres - VolcanoShape.MetresPerRawUnit) return;

            // ★★ **The target heights are written, but not all of them are visible yet.**
            //    Only one rectangle can be flushed per tick (trap 3), so of the final writes
            //    only one tile's worth is on screen; the rest is still showing last tick's
            //    heights — a shape 1/_totalTicks of the summit lower.
            //    **Carve the crater and finish there and the outside of the mountain stays low
            //    for good.** Calling all the tiles here at once trips the merged > 10000 test
            //    and flushes mid-batch every time (§A-1 IL_00A8), so **keep to one tile per tick
            //    and drain the rest** before moving on to the crater.
            //
            //    Once progress has reached 1, WriteHeights writes 0 cells, so
            //    _flush.HasPending naturally goes false as soon as the backlog is drained.
            //    **Do not re-count the tiles** — re-counting loses the final write that landed
            //    during the wait (the one from the moment the clearing reached the outer rim).
            if (_flush.HasPending) return;

            // ★ The crater is not carved here. **It is part of the shape from the first tick**
            //   (class doc).
            _progress = 1f;
            _summitMetres = _stage == UpliftStage.Collapse
                ? -_riseMetresPerTick * _totalTicks
                : footprint.HeightMetres;
            _complete = true;

            // No longer needed. Give the memory back (the cost table in the class doc).
            _baseRaw = null;
            _profile = null;
        }

        /// <summary>
        /// Start writing the shape for <paramref name="stage"/>. **The other stages come through
        /// here too** (<see cref="UpliftStage"/>). Taking the rectangle, the snapshot, the flush
        /// and the ceiling count are the same in all three stages; only the profile that is baked
        /// and the way it advances differ.
        ///
        /// Pass a <paramref name="footprint"/> that has been <b>widened for that stage</b>
        /// (<c>VolcanoFootprint.Resized</c>). The radius is not widened here because unless the
        /// clearing (<c>VolcanoClearing</c>) is looking at the same radius, the ground under the
        /// roads alone gets pushed back (design doc §1.2 / trap 1).
        ///
        /// ★★ <b>Set the stage after <see cref="Reset"/>.</b> Carry the stage across a Reset and
        ///   the previous volcano's caldera stage turns into the next volcano's cone.
        /// </summary>
        internal static bool StartStage(VolcanoFootprint footprint, UpliftStage stage)
        {
            return StartStage(footprint, stage, 0f);
        }

        /// <summary>
        /// As above. <paramref name="coneRadiusMetres"/> is **the radius of the cone body** (m),
        /// read only by the caldera stage — beyond it the original terrain is still intact, so
        /// the reference for dropping the floor is biased towards that
        /// (<see cref="ReferenceGroundFor"/>).
        /// </summary>
        internal static bool StartStage(VolcanoFootprint footprint, UpliftStage stage,
                                        float coneRadiusMetres)
        {
            Reset();
            _stage = stage;
            _coneRadiusMetres = coneRadiusMetres;
            return StartCore(footprint);
        }

        /// <summary>
        /// Decide the affected rectangle and snapshot the "original height". **Once, at start**
        /// (class doc). On failure it leaves <see cref="_lastFailure"/> and returns false.
        /// **<see cref="Reset"/> is not called here** (<see cref="StartStage"/> has done it).
        /// </summary>
        private static bool StartCore(VolcanoFootprint footprint)
        {
            ushort[] raw = ReadRawHeights();
            if (raw == null) return false;

            if (!TileSplit.CellRangeFor(footprint.Centre.X, footprint.Centre.Z,
                                        footprint.RadiusMetres,
                                        out _minX, out _minZ, out _maxX, out _maxZ))
            {
                _lastFailure = "the volcano footprint does not cover a single terrain cell";
                return false;
            }

            _width = _maxX - _minX + 1;
            int height = _maxZ - _minZ + 1;
            _baseRaw = new ushort[_width * height];

            for (int z = 0; z < height; z++)
            {
                int source = (_minZ + z) * RawStride + _minX;
                Array.Copy(raw, source, _baseRaw, z * _width, _width);
            }

            _tileCount = TileSplit.TileCountFor(_minX, _minZ, _maxX, _maxZ);

            // ★ The factor that rebuilds the cone to allow for the crater. **The clearing front
            //   looks at this too** (the doc of _summitScale).
            _heightMetres = footprint.HeightMetres;
            // ★ Rebuilding for the crater is **only needed for the cone**. Neither the inflation
            //   nor the caldera carves a summit, so 1 is right for them (multiplying would push
            //   the front further out than it really is).
            _summitScale = _stage == UpliftStage.Cone
                ? VolcanoCrater.SummitScale(footprint.Form, footprint.RadiusMetres)
                : 1f;

            // ★★ **Bake the profile first.** The caldera drops to an absolute target of
            //    "original ground − depth", so the distance a summit cell actually travels is
            //    "depth + mountain height", and **you cannot know it until it is baked**
            //    (<see cref="DeepestDropMetres"/>). Derive the step count from that measurement
            //    or the per-tick drop gets too large and you get cliff-like steps.
            BakeProfile(footprint, height);

            float travelMetres = _stage == UpliftStage.Collapse
                ? DeepestDropMetres()
                : footprint.HeightMetres;
            // If the bake came out 0 (i.e. not a single cell drops), count using the requested depth.
            if (!(travelMetres > 0f)) travelMetres = footprint.HeightMetres;

            // ★ Clamp so the summit moves at least one raw unit every tick (trap 2).
            //   Derive the conversion from FeatureHost.FramesPerMinute (never hard-code the constant).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            int requestedTicks = framesPerMinute > 0f
                ? (int)(StageMinutes() * framesPerMinute / IntervalFrames)
                : 1;
            _totalTicks = UpliftSchedule.TotalTicksFor(travelMetres, requestedTicks);

            // ★ Every growing cell rises at the same speed (the doc of GrowthMetresAt).
            //   TotalTicksFor has clamped totalTicks by H×64, so this is always at least one raw
            //   unit (1/64 m).
            _riseMetresPerTick = travelMetres / _totalTicks;

            _centre = footprint.Centre;
            _started = true;
            _lastFailure = null;

            Log.Info("volcano uplift started: rect " + _width + "x" + height
                     + " cells, " + _tileCount + " tiles, " + _totalTicks + " ticks, relief "
                     + ModSettings.VolcanoReliefStrength.value + "%, crater r="
                     + VolcanoShape.CraterRadiusOf(footprint.RadiusMetres).ToString("F0")
                     + " m depth=" + VolcanoShape.CraterDepthOf(footprint.HeightMetres).ToString("F0")
                     + " m (part of the profile from the first tick), cone scale "
                     + _summitScale.ToString("F3"));
            return true;
        }

        /// <summary>
        /// Bake the rise of the final shape for every cell, once (<see cref="_profile"/>).
        ///
        /// **This is the only place in ⑤ that calls <c>VolcanoRelief</c>.** Calling it every tick
        /// would be 140,000 cells × 300 flops per tick at a 3 km radius.
        /// The seed comes from the volcano's location (built the same way as in
        /// <c>VolcanoEruption</c> / <c>VolcanoLava</c>), so **rebuild in the same place and you
        /// get the same mountain**.
        /// <c>VanillaRandomizer</c> is not used — ⑤ does not occupy a vanilla disaster slot, so
        /// structurally there is not a single vanilla draw to stay in sync with.
        /// </summary>
        private static void BakeProfile(VolcanoFootprint footprint, int height)
        {
            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));

            var relief = VolcanoRelief.For(footprint.Form, seed,
                                           ModSettings.VolcanoReliefStrength.value / 100f);

            // ★ The floor roughness comes from the same seed. **Rebuild in the same place and you
            //   get the same floor.**
            _floorSeed = seed;

            float centreX = footprint.Centre.X;
            float centreZ = footprint.Centre.Z;
            float radius = footprint.RadiusMetres;
            float metres = footprint.HeightMetres;
            float radiusSquared = radius * radius;

            _profile = new float[_width * height];

            for (int z = 0; z < height; z++)
            {
                float worldZ = (_minZ + z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                float dz = worldZ - centreZ;
                float dz2 = dz * dz;
                if (dz2 > radiusSquared) continue;

                int row = z * _width;
                for (int x = 0; x < _width; x++)
                {
                    float worldX = (_minX + x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    float dx = worldX - centreX;
                    if (dx * dx + dz2 > radiusSquared) continue;

                    // ★★ **0 outside the radius, and it never exceeds the final height H.**
                    //    The relief works by multiplication alone and the crater by min alone,
                    //    so both are structurally guaranteed
                    //    (the class docs of VolcanoRelief / VolcanoCrater).
                    //    **The summit hollow goes in here. It is not carved afterwards.**
                    _profile[row + x] = ProfileFor(
                        relief, dx, dz, radius, metres,
                        _baseRaw[row + x] * VolcanoShape.MetresPerRawUnit,
                        footprint.GroundHeightMetres);
                }
            }
        }

        /// <summary>
        /// How long the current stage takes (in-game minutes).
        ///
        /// ★ The foundering is **fast**. It does not take tens of thousands of years for the roof
        ///   to give way and fall — the tens of thousands of years are in <b>the growth of the
        ///   magma chamber before it</b>. The inflation is the opposite: slow, and given longer
        ///   than the uplift.
        /// </summary>
        private static float StageMinutes()
        {
            float baseMinutes = ModSettings.VolcanoUpliftMinutes.value;

            switch (_stage)
            {
                case UpliftStage.Inflation: return baseMinutes * 1.5f;
                case UpliftStage.Collapse: return baseMinutes * 0.35f;
                default: return baseMinutes;
            }
        }

        /// <summary>
        /// The profile for one cell (m). **Only the caldera returns negative values.**
        ///
        /// ★ <paramref name="radius"/> and <paramref name="metres"/> are
        ///   <b>the affected range already widened for that stage</b> (<c>VolcanoState</c> builds
        ///   and passes it through <c>VolcanoFootprint.Resized</c>). Do not re-apply a factor
        ///   here — doing so makes the radius seen by the clearing (<c>VolcanoClearing</c>) and
        ///   by the uplift disagree, and the ground under the roads alone gets pushed back
        ///   (design doc §1.2 / trap 1).
        /// </summary>
        /// <param name="baseMetres">The **current** ground height of this cell (m). Only the caldera reads it.</param>
        /// <param name="groundMetres">
        /// The ground height before the volcano was placed (m; <c>VolcanoFootprint.GroundHeightMetres</c>).
        /// Only the caldera reads it.
        /// </param>
        private static float ProfileFor(VolcanoRelief relief, float dx, float dz,
                                        float radius, float metres,
                                        float baseMetres, float groundMetres)
        {
            switch (_stage)
            {
                case UpliftStage.Inflation:
                    return SuperEruption.InflationAt(
                        (float)Math.Sqrt(dx * dx + dz * dz), radius, metres);

                case UpliftStage.Collapse:
                {
                    // ★★ **Do not paint over the original terrain.** (2026-08-22, owner's report
                    //    "take the original terrain and the remains of the cone into account and
                    //    make it more realistic")
                    //
                    //    It used to drop relative to the ground at the single centre point
                    //    (groundMetres), so every valley and hill inside the caldera was erased
                    //    and the result was a **dead-flat floor**.
                    //
                    //    Outside the cone body, <b>the cell's current ground is the original
                    //    terrain</b> (the cone never reached that far). So the reference is
                    //    biased towards that. Only inside the cone body is the real thing buried
                    //    under the cone, so the centre's height stands in for it there.
                    //    To avoid a seam, blend over a one-cell-wide band outside the cone's rim.
                    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    float reference = ReferenceGroundFor(distance, baseMetres, groundMetres);

                    // ★★ **The foundering is not "subtract a fixed amount from the mountain".**
                    //    (2026-08-22, owner's report)
                    //
                    //    &gt; when the caldera forms, doesn't the cone body drop a long way and
                    //    &gt; explode massively…?
                    //
                    //    Quite so, and this code used to subtract the depth from the current
                    //    ground. The cone is +1000 m and the depth is 900 m, so **a 100 m stump
                    //    was left at the summit** with a 900 m trench dug only around it — the
                    //    picture of "a moat dug around the mountain", not "the mountain fell".
                    //
                    //    A real caldera <b>drops its roof as a single slab</b>, so the floor goes
                    //    flat at one height below the original ground and the cone body vanishes
                    //    without trace. So the target is set as an absolute height, and the
                    //    profile is "the distance down to it" = target − current.
                    // ★ The floor is not just a bowl — collapsed blocks and a central cone sit on
                    //   it (SuperEruption.CalderaFloorOffsetAt).
                    float offset = SuperEruption.CalderaFloorOffsetAt(
                        dx, dz, radius, metres, _floorSeed);

                    return SuperEruption.FounderDropAt(offset, baseMetres, reference);
                }

                default:
                    return VolcanoCrater.ProfileAt(relief, dx, dz, radius, metres);
            }
        }

        /// <summary>
        /// The reference height (m) that the caldera floor drops relative to.
        ///
        /// Outside the cone body (beyond <see cref="_coneRadiusMetres"/>) it is **that cell's
        /// real ground**; inside, it is the ground at the centre. In between, blend (so no seam
        /// appears).
        /// </summary>
        private static float ReferenceGroundFor(float distance, float baseMetres,
                                                float groundMetres)
        {
            float cone = _coneRadiusMetres;
            if (!(cone > 0f)) return baseMetres;

            // The blending band. Just outside the cone's rim, it moves from the stand-in to the
            // real thing.
            float band = cone * 0.25f;
            if (distance <= cone) return groundMetres;
            if (distance >= cone + band) return baseMetres;

            float t = (distance - cone) / band;
            float k = t * t * (3f - 2f * t);      // smoothstep
            return groundMetres + (baseMetres - groundMetres) * k;
        }

        /// <summary>
        /// The drop of the deepest-falling cell (m, **positive**). Used to decide the caldera's
        /// step size. Can only be called after <see cref="BakeProfile"/> (0 before that).
        /// </summary>
        private static float DeepestDropMetres()
        {
            if (_profile == null) return 0f;

            float deepest = 0f;
            for (int i = 0; i < _profile.Length; i++)
            {
                if (_profile[i] < deepest) deepest = _profile[i];
            }
            return -deepest;
        }

        /// <summary>
        /// Fetch <c>RawHeights</c>. **If the length is not 1081², not one cell is written** —
        /// the <c>z*1081 + x</c> index would point at a different cell and
        /// **an unrelated part of the map would be uplifted**
        /// (the same predicate as <see cref="VolcanoTerrainFacts.Usable"/>).
        ///
        /// Check <c>Singleton&lt;T&gt;.exists</c> first (<c>instance</c> is a main-thread-only API).
        /// </summary>
        private static ushort[] ReadRawHeights()
        {
            if (!Singleton<TerrainManager>.exists)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            ushort[] raw = tm.RawHeights;
            if (raw == null || raw.Length != RawLength)
            {
                _lastFailure = "RawHeights is not a ushort[1081^2]; nothing was raised";
                return null;
            }

            return raw;
        }

        /// <summary>Differences finer than 1/64 m (one raw unit) count as "the same point".</summary>
        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        /// <summary>
        /// **Report ticks that wrote no cells at all, too.** Otherwise "the feature is dead" and
        /// "the target has already been reached" become indistinguishable in the log.
        /// The string concatenation of the arguments runs every time, so drop it early with
        /// <c>DiagEnabled</c>.
        /// </summary>
        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                "uplift frame=" + frame
                + " tick " + _tick + "/" + _totalTicks
                + " progress=" + _progress.ToString("F3")
                + " summit=" + _summitMetres.ToString("F1")
                + " active=" + _activeRadius.ToString("F0")
                + " cells=" + _cellsWrittenLastTick
                // ★ flush=1/1 means "the whole area changed this tick made it on screen in the
                //   same tick" = the smoothest state. 2 or more means it has been split and has
                //   fallen back to round robin, and the visible step is the rise over that many
                //   flushes (class doc).
                + " flush=" + _flush.Cursor + "/" + _flush.TileCount
                + " rect=" + (_dirtyValid ? (_dirtyMaxX - _dirtyMinX + 1) + "x"
                                            + (_dirtyMaxZ - _dirtyMinZ + 1) : "0")
                + " tiles=" + _tileCount
                + " crater=" + (CraterFormed ? "full" : "growing")
                // ★ **Report it when it is 0 as well.** Otherwise "nothing was clipped" and
                //   "nobody looked at whether anything was clipped" are indistinguishable in the log.
                + " ceilingClipped=" + _ceilingClippedCells
                + " floor=" + CraterFloorMetres.ToString("F1")
                + (_complete ? " (complete)" : ""));
        }
    }
}
