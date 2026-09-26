using System;
using System.Reflection;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Clearing — the staged destruction of roads and buildings. **Sim thread only.**
    ///
    /// ★★ <b>This task is the heart of ⑤.</b> Design doc §1.2 discovered that "raise the ground
    /// and you get a mountain" does not hold in a built-up area, and rewrote ⑤'s design itself.
    /// Putting the clearing ahead of the uplift is that conclusion, and this type is what
    /// implements it.
    ///
    /// ── why it comes before the uplift (design doc §1.2 / IL facts doc §A-2 / §A-3) ─────────
    ///
    /// <c>TerrainModify.UpdateAreaImplementation</c> makes seven managers <c>ApplyQuad</c> every
    /// time. Two of them kill ⑤ — <c>NetSegment.TerrainUpdated</c> applies
    /// <c>Heights.PrimaryLevel</c> under roads with <c>m_flattenTerrain</c> and sets
    /// <c>primaryMin = primaryMax = the road's y</c>, and <c>Building.TerrainUpdated</c> pins it
    /// to the building's y through <c>SecondaryLevel</c> (with a 1:4 skirt).
    /// The apply step is <c>Clamp(Clamp(target, secondaryMin, secondaryMax), primaryMin, primaryMax)</c>,
    /// so <b>if the lower and upper bounds are the same value, whatever you put in target is
    /// cancelled out.</b> And it is **redone from scratch on every flush**, so writing harder
    /// does not win. On top of that <b>the game destroys nothing</b>
    /// (<c>NetAI.AfterTerrainUpdate</c> is a single <c>ret</c> instruction, §A-3).
    ///
    /// > Therefore, unless ⑤ destroys them itself, flat trenches for roads and funnels for
    /// > buildings are guaranteed to remain inside the mountain.
    /// > **This is not a matter of implementation quality but of ordering.**
    ///
    /// ── <see cref="ClearedRadiusMetres"/> is the only input T6 is allowed ───────────────────
    ///
    /// The only cells <c>VolcanoUplift</c> (T6) may write are those inside
    /// <c>UpliftSchedule.ActiveRadiusMetres(shapeRadius, VolcanoClearing.ClearedRadiusMetres)</c>.
    /// **Never pass anything else as the second argument** (plan trap 1).
    /// It is 0 until the first thing has been destroyed, and the Core-side tests pin
    /// "0 in, 0 out".
    ///
    /// ★ <b><see cref="ClearedRadiusMetres"/> means "finished sweeping", not "finished destroying".</b>
    /// The sweep is finished even if refused buildings (below) remain. **Those are places ⑤ is
    /// known to be unable to clear**, so T6 may raise them — it will be pushed back, but that is
    /// the visualisation of the fact "there is one building ⑤ cannot destroy", not a defect in ⑤.
    ///
    /// ── the clearing runs ahead of the uplift (ring lockstep) ──────────────────────────────
    ///
    /// <code>
    /// front   = UpliftSchedule.ClearingFrontMetres(R, frontUnit, VolcanoClearingLeadMetres)
    /// cleared = the radius actually swept (at most front; if it falls short, only as far as it got)
    /// </code>
    ///
    /// If a single sweep does not reach <c>front</c>, <see cref="ClearedRadiusMetres"/>
    /// **advances only as far as it got**. Reporting "all done" while capped would have T6 raise
    /// ground that has not been cleared. <b>A volcano's centre does not move</b>, so unlike ④'s
    /// wind damage, the only condition for discarding the sweep position
    /// (<see cref="_buildingCursor"/> / <see cref="_segmentCursor"/>) is "it became a different
    /// volcano".
    ///
    /// ── buildings use <c>demolish: true</c> (the opposite call to ④'s wind damage) ──────────
    ///
    /// ④'s wind damage is meant to "look as if the typhoon **broke** it", so leaving the ruin
    /// behind was fine and it used <c>false</c>.
    /// ⑤'s clearing is meant to **free up the site**, so no ruin may remain — a ruin keeps
    /// <c>Building.TerrainUpdated</c> pinning the terrain (§A-2, §G-16 (c)).
    /// <c>burnAmount</c> is <b>0</b> — the clearing is not scorching (setting fires is T8's job).
    ///
    /// > **Trap 5. Do not write <c>Building.m_fireIntensity</c> directly.** Only the
    /// > <c>CommonBuildingAI</c> family consumes it; write it on any other AI and nobody ever
    /// > clears it, giving a permanent ghost fire — and **because it goes into the vanilla
    /// > building array it is baked into the save and survives removing the mod**.
    /// > This project has shipped that once already.
    /// > Review grep (★ actually run, with the counts made to match. Whole-project review M12 —
    /// > run plainly, **the sentences of this very rule** got caught, so the "0 hits" procedure
    /// > never held in the first place):
    /// > <code>
    /// > grep -rn --include=*.cs "m_fireIntensity" src/DisasterPlus/Game/Volcano/ \
    /// >   | grep -vE ':[0-9]+: *//' | wc -l        # -> 0
    /// > </code>
    /// > <c>grep -v '///'</c> is not enough (single <c>//</c> comments must be dropped too).
    ///
    /// > **Never call <c>DisasterHelpers</c>' destruction helpers** (§E-14). Natural Disasters
    /// > Renewal replaces them wholesale with a Prefix. Call <c>BuildingAI.CollapseBuilding</c>
    /// > and <c>NetAI.CollapseSegment</c> directly and you **bypass the patch surface entirely**.
    ///
    /// ── ★ T5 Step 1: the destruction path settled in IL (the source is IL facts doc §G-16; do not re-derive) ──
    ///
    /// These are the answers to the two items the design doc appendix and plan T5 Step 1 named as
    /// "unsettled". **The full text and the IL offsets are in §G-16.** All that is written here
    /// is which conclusions the code in this file rests on:
    ///
    ///   1. **Roads really are released** (§G-16 (a)). <c>demolish: true</c> funnels into
    ///      <c>PlayerNetAI.CollapseSegment</c>, which calls
    ///      <c>NetManager.ReleaseSegment(id, keepNodes: false)</c>.
    ///      Of the 19 overrides, the only one that refuses is <c>SupportCableAI</c>
    ///      (cable-car spans; a type that does not flatten terrain, so leaving it does not make a
    ///      trench). An <c>Untouchable</c> segment is refused together with its owning building
    ///      when that building's collapse is refused
    ///   2. **A node whose last segment is gone is released on the spot** (§G-16 (b)).
    ///      No orphan nodes are left. But <b>releasing a road can take buildings with it</b>
    ///      (<c>ReleaseNodeImplementation</c> → <c>ReleaseBuilding</c>), so the building-side
    ///      sweep must always keep to the discipline of "note the next ID before acting"
    ///   3. **Setting <c>Collapsed</c> alone does not stop the terrain pinning** (§G-16 (c)).
    ///      <c>NetSegment.TerrainUpdated</c> only looks at <c>m_flags &amp; 3</c>, and
    ///      <c>Building.TerrainUpdated</c> only at <c>m_flags &amp; 524291</c>
    ///      (<c>Created|Deleted|Demolishing</c>). **This is the IL-level reason the call goes the
    ///      opposite way to ④'s <c>demolish: false</c>**
    ///   4. **The five refusing AIs let <c>demolish: true</c> through** (§G-16 (d)).
    ///      ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / TsunamiBuoyAI delegate to
    ///      <c>CommonBuildingAI</c>, and DecorationBuildingAI sets <c>Demolishing</c>.
    ///      **"Terrain left standing only under the disaster facilities" does not happen.**
    ///      <see cref="LastBuildingsRefused"/> is kept anyway because there may be other paths
    ///      that refuse, and **if even one building is refused, that cell stays pinned for good
    ///      and is left behind by the uplift**, so it is made visible in the diagnostics and on
    ///      the panel.
    ///      <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> / <c>MonorailPylonAI</c> lie in a dry run
    ///      (④ §F-2), but ⑤ **does not use the dry run as a filter**, so it does not miss them
    ///   5. **Pass <c>null</c> for <c>InstanceManager.Group</c>** (§G-16 (e)).
    ///      ⑤ does not occupy a disaster slot, so there is nothing to group into, and all three
    ///      call sites do check for null. Passing null matches the measurements better than
    ///      newing an empty <c>Group</c>
    ///
    /// > **If 1 and 2 do not hold, ⑤ does not start the uplift** (plan T5 Step 1).
    /// > <see cref="ClearingPathAvailable"/> goes false, <c>VolcanoState.HandleStart</c> falls
    /// > through to <c>Refused</c> without destroying a single thing, and the panel shows
    /// > <c>Strings.VolcanoRoadPathUnavailable</c>.
    /// > **Do not choose "give up on the roads and uplift anyway"** — that would mean shipping,
    /// > knowingly, the very failure design doc §1.2 discovered (flat trenches inside the
    /// > mountain).
    ///
    /// ── the per-sweep work budget (stated explicitly) ──────────────────────────────────────
    ///
    /// The sweep interval is the **elapsed in-game time** worth of <see cref="IntervalFrames"/>
    /// frames. Never <c>frameIndex % N</c> — <c>m_currentFrameIndex</c> advances by
    /// <c>FinalSimulationSpeed</c> (1/3/9) per tick, so a modulo makes the test fire erratically
    /// depending on game speed (firestorm appendix A-4).
    ///
    /// One sweep is limited to <b><see cref="MaxCellsPerPass"/> grid cells on each of the
    /// building and road sides</b> and <b><see cref="MaxBuildingsPerPass"/> buildings /
    /// <see cref="MaxSegmentsPerPass"/> road segments</b>. Even at radius 3000 m (the largest
    /// form) the rectangle is 95×95 = 9025 cells, so the cell limit only bites when the centre is
    /// at the edge of the map; what actually bites is the count limit. If it is cut short, the
    /// next sweep resumes from the cursor, and <see cref="ClearedRadiusMetres"/> **advances only
    /// as far as it got**.
    ///
    /// ── the candidate mask differs from ② and ④ (this part is specific to ⑤) ───────────────
    ///
    /// ② and ④ excluded <c>Collapsed</c> from the candidates (<c>CollapseBuilding</c> always
    /// returns false for it, so the leftover rubble piles up in "refused" every time and makes
    /// the diagnostics unreadable).
    /// **⑤ does not exclude it.** As per point 3 above, rubble with only <c>Collapsed</c> set
    /// <b>keeps pinning the terrain</b>, so for ⑤ it is precisely what has to be removed.
    /// Instead it excludes <c>Demolishing</c> (there the terrain pinning has already stopped).
    /// </summary>
    public static partial class VolcanoClearing
    {
        /// <summary>The sweep interval (in-game time equivalent to this many frames).</summary>
        private const int IntervalFrames = 64;

        /// <summary>
        /// Maximum grid cells examined in one sweep (on each of the building and road sides).
        ///
        /// ★ **32768 was a dead constant** (whole-project review M16). The running index of the
        /// rings being swept tops out at 99² = 9801 even for the largest form (R=3000 m), so
        /// 32768 is **structurally unreachable**. It has been lowered to a value that actually
        /// bites — even the largest volcano now has to be swept in three passes, which puts a
        /// limit on how much of the grid is walked in a single tick. Whatever did not get done
        /// continues from the cursor next time, and <see cref="ClearedRadiusMetres"/> advances
        /// only as far as it got.
        /// </summary>
        private const int MaxCellsPerPass = 4096;

        /// <summary>
        /// Maximum buildings attacked in one sweep.
        ///
        /// ★ **Lowered from 2048** (whole-project review M16). One <c>demolish: true</c> does not
        /// merely set a flag — it drags along releasing the building, recursing into its
        /// sub-buildings, and, on the road side, releasing nodes, invalidating paths and issuing
        /// an <c>UpdateArea</c>. 2048 buildings + 2048 roads = **4096 calls in a single sim tick**
        /// is not a number you can call "putting a limit on the per-tick work".
        /// The sweep runs every 64 frames, so even 128 clears about 90 buildings per in-game
        /// minute.
        /// </summary>
        private const int MaxBuildingsPerPass = 128;

        /// <summary>Maximum road segments attacked in one sweep (same reasoning as the building side).</summary>
        private const int MaxSegmentsPerPass = 128;

        /// <summary>Cells along one side of the building/road grid (one cell is 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>Grid cell size (m).</summary>
        private const float GridCellSize = 64f;

        /// <summary>Offset from world coordinates to cell index.</summary>
        private const float GridCellOffset = 135f;

        /// <summary>Guard on how many times the building linked list is walked (the size of the building buffer).</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>Guard on how many times the road linked list is walked (<c>Array16&lt;NetSegment&gt;(36864)</c>).</summary>
        private const int SegmentChainGuard = 36864;

        // ★★ **Neither the masks nor the margins nor the hit test belong here**
        //    (whole-project review I2 / I4). The single set in <see cref="VolcanoScan"/> is shared
        //    with the survey (VolcanoSurvey). The same rules used to be copied into two files,
        //    with a **note** promising "these must stay the same value or they drift". The note
        //    did not prevent the masks actually drifting — the survey alone was excluding
        //    Untouchable and Collapsed, and so it was showing fewer things about to be destroyed
        //    than there really were, immediately before an irreversible operation.

        /// <summary>
        /// Minimum lead distance (m). One raw cell. **Allow 0 and the ring lockstep waits on
        /// itself and stalls for good** (<see cref="LeadMetres"/>).
        /// </summary>
        private const float MinLeadMetres = 16f;

        private static VolcanoDestructionFacts _facts;
        private static bool _factsScanned;

        private static float _minutesSincePass;
        private static Vec3 _centre;
        private static bool _centreValid;

        private static float _clearedRadius;
        private static float _frontRadius;
        private static float _shapeRadius;

        private static int _buildingCursor;
        private static int _segmentCursor;

        // ★ The upper bound on the radius a sweep carried over mid-cursor may claim to have
        //   "gone all the way round" (whole-project review M8; <c>PassFront</c> in
        //   <c>VolcanoClearing.Sweep.cs</c>). 0 means "not recorded".
        private static float _buildingPassFront;
        private static float _segmentPassFront;

        // ★★ **The two sweeps complete their laps separately, so the radii they reach are
        //    remembered separately too.**
        //    (2026-08-22; the cause of a shield volcano at r=3000 m never finishing in the live
        //    game.)
        //
        //    These two did not exist before; <c>Sweep</c> took the smaller of the building and
        //    road sides **for that pass** and put it into <see cref="_clearedRadius"/>.
        //    But the number of passes needed for one lap differs between them (the road side's
        //    rectangle is wider by <c>VolcanoScan.SegmentGridMargin</c>), so
        //    **the lap phases of the two drift apart while the front is still advancing.**
        //    Once drifted, "the pass on which one finished its lap" and "the pass on which the
        //    other finished its lap" are forever different passes, and
        //    **the two never again reach the front on the same pass.**
        //    The live log records exactly that state — <c>cleared=2816/3000</c> did not move for
        //    over 660 passes, the uplift sat in <c>Uplifting</c> at <c>progress=1.000</c>, and
        //    the volcanic tremor would not stop.
        //
        //    Remember each side monotonically and **take the smaller at the end**.
        //    The meaning is unchanged ("the radius both have finished sweeping"), but the two no
        //    longer have to line up on the same pass.
        private static float _buildingReachedRadius;
        private static float _segmentReachedRadius;

        private static bool _errorLogged;

        // ── nine diagnostic counters (all read and written from the sim thread only) ─────────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastBuildingsDestroyed;
        private static int _lastBuildingsRefused;
        private static int _lastSegmentsDestroyed;
        private static int _lastSegmentsRefused;
        private static int _totalBuildingsDestroyed;
        private static int _totalSegmentsDestroyed;
        private static bool _lastCapped;

        private static string _lastFailure;

        /// <summary>
        /// ★★ <b>The only input T6 is allowed.</b> The radius (m) that has been swept.
        /// **It is 0 until the first thing has been destroyed**, and at that point
        /// <c>UpliftSchedule.ActiveRadiusMetres</c> returns 0 (i.e. the uplift moves not one cell).
        ///
        /// It means "**finished sweeping**", not "finished destroying" (class doc).
        /// </summary>
        public static float ClearedRadiusMetres { get { return _clearedRadius; } }

        /// <summary>
        /// The front being chased right now (m). Once <see cref="ClearedRadiusMetres"/> reaches
        /// it, the clearing for that much progress is done. Used for diagnostics and the phase
        /// transitions.
        /// </summary>
        public static float FrontMetres { get { return _frontRadius; } }

        /// <summary>Whether the sweep has reached the current front (i.e. the clearing for this much progress is done).</summary>
        public static bool FrontReached
        {
            get { return _passes > 0 && _clearedRadius >= _frontRadius; }
        }

        /// <summary>Whether the sweep has reached the mountain's radius (i.e. the clearing is finished entirely).</summary>
        public static bool Complete
        {
            get { return _shapeRadius > 0f && _clearedRadius >= _shapeRadius; }
        }

        /// <summary>
        /// Whether the clearing's destruction path holds in this environment. **If false, ⑤
        /// creates no volcano at all** (Step 1 in the class doc). It can answer even if
        /// <see cref="Tick"/> has never been called.
        ///
        /// ★★ <b>The predicate must be the same expression <see cref="Sweep"/> actually gates on</b>
        /// (whole-project review M9). Back when this was <c>RoadPathUsable</c> (roads only), in an
        /// environment where <c>CollapseBuilding</c> could not be resolved but the road side
        /// could, **the volcano was confirmed, entered <c>Clearing</c>, and stalled there for
        /// good** — <c>Sweep</c> turns back on <c>Facts().Usable</c> every time, so the pass count
        /// never went up, <c>FrontReached</c> never became true, and the phase did not even reach
        /// <c>Refused</c>.
        /// </summary>
        public static bool ClearingPathAvailable
        {
            get { return Facts().Usable && SegmentGridUsable(); }
        }

        /// <summary>Sweeps run so far (cumulative for this volcano).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>Buildings plus roads examined as candidates in the last sweep.</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>Buildings actually removed in the last sweep.</summary>
        public static int LastBuildingsDestroyed { get { return _lastBuildingsDestroyed; } }

        /// <summary>
        /// Buildings **vanilla refused** in the last sweep. If it is not 0, the cells under them
        /// stay pinned at their original height and are left behind by the uplift (point 4 in the
        /// class doc).
        /// </summary>
        public static int LastBuildingsRefused { get { return _lastBuildingsRefused; } }

        /// <summary>Road segments actually removed in the last sweep.</summary>
        public static int LastSegmentsDestroyed { get { return _lastSegmentsDestroyed; } }

        /// <summary>
        /// Road segments **vanilla refused** in the last sweep. <c>SupportCableAI</c> and
        /// <c>Untouchable</c> segments whose owning building refused to collapse land here.
        /// </summary>
        public static int LastSegmentsRefused { get { return _lastSegmentsRefused; } }

        /// <summary>Buildings removed so far for this volcano.</summary>
        public static int TotalBuildingsDestroyed { get { return _totalBuildingsDestroyed; } }

        /// <summary>Road segments removed so far for this volcano.</summary>
        public static int TotalSegmentsDestroyed { get { return _totalSegmentsDestroyed; } }

        /// <summary>Whether the last sweep was cut short by a limit (it continues next time; the front was not reached).</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>
        /// The most recent reason it could not run (**English, for diagnostics**). null if it ran.
        /// This is the mouth that stops us **failing silently**.
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// Call when letting go of the volcano and on level unload. Idempotent.
        /// **A volcano in progress is not saved**, so leaving and re-entering the city restarts
        /// the clearing from 0 (the terrain stays in whatever shape it was in. Design doc §1.3).
        /// </summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _centre = new Vec3(0f, 0f, 0f);
            _centreValid = false;
            _clearedRadius = 0f;
            _frontRadius = 0f;
            _shapeRadius = 0f;
            _buildingCursor = 0;
            _segmentCursor = 0;
            _buildingPassFront = 0f;
            _segmentPassFront = 0f;
            _buildingReachedRadius = 0f;
            _segmentReachedRadius = 0f;
            _passes = 0;
            _lastScanned = 0;
            _lastBuildingsDestroyed = 0;
            _lastBuildingsRefused = 0;
            _lastSegmentsDestroyed = 0;
            _lastSegmentsRefused = 0;
            _totalBuildingsDestroyed = 0;
            _totalSegmentsDestroyed = 0;
            _lastCapped = false;
            _lastFailure = null;

            // ★ _errorLogged and the measured destruction path are not reset. Both are "facts
            //    about the build of the game this DLL references", not per-city state
            //    (the same call as in TyphoonWind / VolcanoReader).
        }

        /// <summary>
        /// A pure function that only probes the destruction path. It touches no cache, so
        /// **calling it from any thread cannot corrupt this type's state**.
        /// <see cref="Assumptions"/> (main thread) should use this one.
        ///
        /// Methods are looked up with <c>GetMethod</c> **specifying the parameter types too**
        /// (the form ② established). A name-only <c>GetMethod</c> throws on overloads and also
        /// misses signature changes.
        /// </summary>
        public static VolcanoDestructionFacts ScanFacts()
        {
            bool building = HasMethod(typeof(BuildingAI), "CollapseBuilding", new Type[]
            {
                typeof(ushort), typeof(Building).MakeByRefType(),
                typeof(InstanceManager.Group), typeof(bool), typeof(bool), typeof(int)
            });

            bool segment = HasMethod(typeof(NetAI), "CollapseSegment", new Type[]
            {
                typeof(ushort), typeof(NetSegment).MakeByRefType(),
                typeof(InstanceManager.Group), typeof(bool)
            });

            bool release = HasMethod(typeof(NetManager), "ReleaseSegment", new Type[]
            {
                typeof(ushort), typeof(bool)
            });

            return new VolcanoDestructionFacts(building, segment, release);
        }

        /// <summary>
        /// Sim thread. **Always call it from below the pause guard in
        /// <c>VolcanoFeature.OnSimulationTick</c>** (otherwise buildings vanish while paused).
        ///
        /// <paramref name="frontUnit"/> is <b>the uplift front [0,1]</b>. If the clearing has only
        /// just begun (the uplift has not moved yet) it is 0, and the front is just
        /// <c>ModSettings.VolcanoClearingLeadMetres</c>.
        ///
        /// ★★ **Do not pass the progress itself** (pass <c>VolcanoUplift.GrowthFrontUnit</c>).
        ///   The cone is rebuilt to allow for the crater, so the uplift front runs ahead of the
        ///   progress (<c>UpliftSchedule.GrowthFrontUnit</c>). Pass the progress and the uplift
        ///   arrives before the clearing does, and the outer rim of the mountain appears to stop
        ///   at a sheer circle — **not the dangerous direction** (<c>ActiveRadiusMetres</c> stops
        ///   the writes), but it waits there every time.
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, float frontUnit, float deltaMinutes)
        {
            try
            {
                Step(footprint, frontUnit, deltaMinutes);
            }
            catch (Exception e)
            {
                _lastFailure = "the clearing pass threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano clearing failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcClear",
                             "volcano clearing failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float frontUnit, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            // ★ If the volcano has been swapped, discard the sweep position and the tally.
            //    **The centre does not move**, so this is the only condition under which the
            //    cursors are discarded (class doc).
            if (!_centreValid || !SamePoint(_centre, footprint.Centre))
            {
                float keptMinutes = _minutesSincePass;
                Reset();
                _minutesSincePass = keptMinutes;
                _centre = footprint.Centre;
                _centreValid = true;
            }

            _shapeRadius = footprint.RadiusMetres;

            // ★ Advance the interval accumulator before the subject (the same shape as ④'s
            //    TyphoonWind; rewind it and the sweep never runs at all).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (framesPerMinute <= 0f) return;

            _frontRadius = UpliftSchedule.ClearingFrontMetres(
                footprint.RadiusMetres, frontUnit, LeadMetres());

            // If it already reaches the front, do not run. **The accumulator is not consumed**,
            // so it runs immediately on the next tick after the front advances.
            if (_clearedRadius >= _frontRadius) return;

            if (_minutesSincePass < interval) return;

            // Do not carry the remainder over (no repeated firing after a load hands us a large
            // deltaMinutes).
            _minutesSincePass = 0f;

            Sweep(footprint);
        }

        /// <summary>
        /// One sweep. Walks the buildings and the roads outwards from the centre and removes what
        /// is inside the front.
        ///
        /// **The radius reached is the smaller of the two.** Even if one side reaches the front,
        /// if the other has not, the place is not yet "cleared".
        ///
        /// ★★ <b>But it is not "the smaller of the two for this pass".</b>
        /// Buildings and roads complete their laps separately, and **they need a different number
        /// of passes per lap**. Compare them directly and the moment their lap phases drift apart
        /// the front is never reached again, so each is remembered monotonically
        /// (<c>_buildingReachedRadius</c> / <c>_segmentReachedRadius</c>) and the smaller is
        /// taken from those.
        /// </summary>
        private static void Sweep(VolcanoFootprint footprint)
        {
            VolcanoDestructionFacts facts = Facts();
            if (!facts.Usable)
            {
                _lastFailure = "no usable destruction path in this build of the game";
                return;
            }

            _lastFailure = null;

            bool buildingsCapped;
            float buildingsReached;
            int buildingsScanned = ClearBuildings(footprint, out buildingsCapped,
                                                  out buildingsReached);

            bool segmentsCapped;
            float segmentsReached;
            int segmentsScanned = ClearSegments(footprint, out segmentsCapped,
                                                out segmentsReached);

            // ★★ **Remember each side monotonically, then take the smaller.**
            //    Do not compare this pass's two values directly — the number of passes per lap
            //    differs between the two, so their lap phases drift and **the two never again
            //    reach the front on the same pass** (the origin of <see cref="_buildingReachedRadius"/>).
            //    The meaning is unchanged: the smaller is still "the radius both have finished
            //    sweeping".
            if (buildingsReached > _buildingReachedRadius)
            {
                _buildingReachedRadius = buildingsReached;
            }
            if (segmentsReached > _segmentReachedRadius)
            {
                _segmentReachedRadius = segmentsReached;
            }

            float reached = _buildingReachedRadius < _segmentReachedRadius
                ? _buildingReachedRadius
                : _segmentReachedRadius;
            if (reached > _clearedRadius) _clearedRadius = reached;

            _passes++;
            _lastScanned = buildingsScanned + segmentsScanned;
            _lastCapped = buildingsCapped || segmentsCapped;

            WriteDiag();
        }

        /// <summary>
        /// How many metres ahead of the uplift front the clearing front runs.
        ///
        /// **Never negative** — negative means "raise ground that has not been cleared yet",
        /// which is trap 1 itself.
        ///
        /// ★★ **Not 0 either.** The clearing and the uplift mesh as a ring lockstep — the radius
        /// that may be uplifted is the radius the clearing reached, and the clearing front is
        /// decided by the uplift progress. With a lead of 0, at progress 0 the front is 0 too,
        /// and you can never escape the loop of <b>nothing destroyed → nothing raised → progress
        /// does not move</b>. Not a single exception is thrown; the phase just sits silently at
        /// <c>Clearing</c>.
        /// The floor is one raw cell (16 m) — a finer lead than that is meaningless (the terrain
        /// grid does not get any finer).
        /// </summary>
        private static float LeadMetres()
        {
            int lead = ModSettings.VolcanoClearingLeadMetres.value;
            return lead > MinLeadMetres ? lead : MinLeadMetres;
        }

        /// <summary>
        /// Whether the road grid is <see cref="GridSide"/>² as measured (§F-15).
        /// **Do not run on a guess** (design doc §6) — index with <c>z*270+x</c> when it does not
        /// match and you destroy roads somewhere else entirely.
        ///
        /// Returns <b>true</b> when <c>NetManager</c> is not around yet (main menu, assumption
        /// checks). That is so "cannot read it yet" is not reported as "unusable"; it is always
        /// around immediately before anything is actually destroyed.
        /// </summary>
        private static bool SegmentGridUsable()
        {
            try
            {
                if (!Singleton<NetManager>.exists) return true;

                var nm = Singleton<NetManager>.instance;
                if (nm == null) return true;

                var grid = nm.m_segmentGrid;
                return grid != null && grid.Length == GridSide * GridSide;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Returns the measured destruction path through the cache. **Sim thread only** (it writes the cache).</summary>
        private static VolcanoDestructionFacts Facts()
        {
            if (_factsScanned) return _facts;

            _factsScanned = true;
            _facts = ScanFacts();
            return _facts;
        }

        private static bool HasMethod(Type declaringType, string name, Type[] parameterTypes)
        {
            try
            {
                return declaringType.GetMethod(name,
                    BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null) != null;
            }
            catch
            {
                return false;
            }
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
        /// **Emit it every time, including when nothing was destroyed.** Otherwise "the feature is
        /// dead" and "there is nothing in range" become indistinguishable in the log (which is
        /// what actually happened in ③).
        ///
        /// <c>Log.Diag</c> is throttled to once per 512 sim frames for a given key, but
        /// **the string concatenation of the arguments runs every time**, so drop it early with
        /// <c>DiagEnabled</c> (C# evaluates the arguments fully before the call).
        /// </summary>
        private static void WriteDiag()
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcClear",
                "clearing pass#" + _passes
                + " front=" + _frontRadius.ToString("F0")
                + " cleared=" + _clearedRadius.ToString("F0")
                + "/" + _shapeRadius.ToString("F0")
                // ★ Report the two separately. One of them being stuck could not be read from the
                //   single merged number (the origin of _buildingReachedRadius above).
                + " reach b=" + _buildingReachedRadius.ToString("F0")
                + " r=" + _segmentReachedRadius.ToString("F0")
                + " scanned=" + _lastScanned
                + " buildings=" + _lastBuildingsDestroyed + " (refused " + _lastBuildingsRefused + ")"
                + " roads=" + _lastSegmentsDestroyed + " (refused " + _lastSegmentsRefused + ")"
                + " total=" + _totalBuildingsDestroyed + "/" + _totalSegmentsDestroyed
                + (_lastCapped ? " (capped; the front was not reached this pass)" : ""));
        }
    }
}
