using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The only place a building's height (m) is read. **Returns 0 if it cannot be read.**
    ///
    /// ── What was pinned down in the IL (Task 10 Step 1; an open item in the design
    ///    doc's appendix) ─────────
    ///
    /// The design doc said "building height comes from the
    /// <c>Building.Info.m_generatedInfo</c> family. **Pin the exact field name down in
    /// the IL before writing it.**" Measured:
    ///
    /// ```
    /// Building (the struct) has no height field.
    ///   What exists is m_baseHeight (Byte, the terrain-side reference height) and
    ///   m_width / m_length (Byte, counts of 8 m cells). **There is no m_height.**
    ///
    /// The height is on the prefab side:
    ///   BuildingInfoGen.m_size / m_min / m_max : Vector3   (mesh bounds, m)
    ///   BuildingInfo.m_size                    : Vector3
    ///   BuildingInfo.m_collisionHeight         : Single
    ///
    /// BuildingInfo.InitializePrefab
    ///   IL_01EE  m_generatedInfo.m_size == Vector3.zero -> PrefabException("Generated info has zero size")
    ///   IL_09B3  m_size = m_generatedInfo.m_size
    ///   IL_0AC2  m_collisionHeight = m_size.y
    /// BuildingInfo.CheckReferences
    ///   IL_0019  m_collisionHeight = m_size.y
    ///   IL_02E8  m_collisionHeight = Max(m_collisionHeight, top of prop)   // likewise for sub-buildings
    /// ```
    ///
    /// **What settles that the unit is metres**
    /// (<c>CommonBuildingAI.CollapseIfFlooded</c>, IL_0031-0053):
    ///
    /// ```
    /// if (TerrainManager.WaterLevel(XZ(m_position)) > m_position.y + Mathf.Max(4f, m_info.m_collisionHeight))
    ///     ... collapse from flooding
    /// ```
    ///
    /// <c>m_position.y</c> is a world coordinate (m) and <c>4f</c> is metres too, so
    /// <c>m_collisionHeight</c> is settled as **a height above the building's base plane,
    /// in metres**. That <c>BuildingInfo.IsMeshSmallOrMissing</c> clamps <c>m_min</c> /
    /// <c>m_max</c> by <c>m_cellWidth * 4</c> (half of an 8 m cell) corroborates that this
    /// Vector3 is in metres.
    ///
    /// **Reliability:** <c>InitializePrefab</c> throws a <c>PrefabException</c> when
    /// <c>m_generatedInfo.m_size == zero</c>, so **any BuildingInfo that initialised
    /// successfully has a non-zero size**. The y can still be small for buildings that are
    /// genuinely low (parks, decorations), and that is a measurement, not a failed read.
    /// Anything below <see cref="LongPeriodResponse.MinHeightMetres"/> falls outside the
    /// feature's scope.
    ///
    /// ── **Which one to use (swapped over in layer-2 review I2)** ──────────────
    ///
    /// Originally <c>m_collisionHeight</c> was preferred, on the grounds that it is "the
    /// value the game itself uses as a building's height". **That was wrong.**
    /// <c>CheckReferences</c> folds in the height of the <b>trees</b> on the lot
    /// (measured in the IL for this fix):
    ///
    /// ```
    /// BuildingInfo::CheckReferences
    ///   IL_0019-0025  m_collisionHeight = m_size.y                (the starting point)
    ///   IL_0270-02F6  h = prop.m_generatedInfo.m_center.y
    ///                     + prop.m_generatedInfo.m_size.y * 0.5
    ///                 h *= prop.m_maxScale
    ///                 if (m_props[i].m_fixedHeight) h += m_props[i].m_position.y
    ///                 m_collisionHeight = Mathf.Max(m_collisionHeight, h)   // props
    ///   IL_03EA-0470  the same expression repeated for **TreeInfo** (m_finalTree /
    ///                 TreeInfoGen::m_center and m_size / TreeInfo::m_maxScale)  // trees
    /// ```
    ///
    /// Vanilla's low-density residential lots carry tree props, and
    /// <c>TreeInfoGen.m_size.y</c> can reach 15-25 m before <c>m_maxScale</c> is even
    /// applied. In other words <b>a bungalow claims 20 m or more and becomes a candidate
    /// for long-period damage</b>. The feature's own premise ("the taller it is, the more
    /// likely it is to fall") and in-game checklist item 82 (that at the same distance,
    /// tall buildings fall more often than short ones) would both be measured against
    /// **a height fabricated by trees**.
    ///
    /// **So the primary source is <c>BuildingInfo.m_size.y</c> (the mesh bounds).** That
    /// this value is uncontaminated was also pinned down in the IL:
    ///
    /// ```
    /// BuildingInfo::InitializePrefab  IL_09BE  m_size = m_generatedInfo.m_size
    /// BuildingInfoBase::CalculateGeneratedInfo(MeshFilter[], SkinnedMeshRenderer[])
    ///   IL_0135-014A  y = Mathf.Max(y, mesh.vertices[k].y)   ← mesh vertices only
    ///   IL_05E6       m_generatedInfo.m_size = new Vector3(x*2, y, z*2)
    /// Across the whole assembly, the only stfld to BuildingInfo::m_size is in
    /// InitializePrefab, and the only stfld to BuildingInfoGen::m_size is in
    /// CalculateGeneratedInfo (confirmed by scanning everything).
    /// ```
    ///
    /// **Never fall back to <c>m_collisionHeight</c>.** A fallback is only needed when
    /// <c>m_size.y</c> is unusable, and in that situation <c>m_collisionHeight</c>'s
    /// starting point (IL_0019) is that same unusable value, so what survives the
    /// <c>Max</c> from there is **precisely the height fabricated by props and trees**. In
    /// other words, in the one situation where a fallback would matter, the fallback
    /// returns a lie. If it cannot be read, return 0 (meaning unknown) and the caller does
    /// nothing.
    ///
    /// **A side effect we accept:** <c>m_size.y</c> does not include sub-buildings, so a
    /// prefab whose main mesh is low and whose height comes from sub-buildings reads low.
    /// Under-estimating errs on the side of doing no damage, which is safer than
    /// over-estimating.
    ///
    /// The plan specified the form <c>MetresOf(ushort id, ref Building b)</c>, but since
    /// the measurements above settled that <b>the height exists only on the prefab side
    /// and the building ID is never needed</b>, the unused parameter is not there.
    /// </summary>
    public static class BuildingHeight
    {
        /// <summary>
        /// This building's height (m). **0 if it cannot be read.** The caller must treat
        /// 0 as "unknown", not "short", and apply no damage at all.
        /// </summary>
        public static float MetresOf(ref Building b)
        {
            try
            {
                var info = b.Info;
                // UnityEngine.Object's == overload also rejects a destroyed (fake-null)
                // object.
                if (info == null) return 0f;

                // ★ The mesh bounds' height. **m_collisionHeight is not used** (see
                //   "Which one to use" in the class doc; it is inflated by the lot's
                //   trees).
                float meshHeight = info.m_size.y;
                if (!float.IsNaN(meshHeight) && meshHeight > 0f) return meshHeight;

                // The only fallback is m_size's own source (InitializePrefab IL_09BE).
                // Even on a prefab where m_size was never written, it can be read as long
                // as the generated info survives.
                var generated = info.m_generatedInfo;
                if (generated == null) return 0f;

                float generatedHeight = generated.m_size.y;
                return !float.IsNaN(generatedHeight) && generatedHeight > 0f
                    ? generatedHeight : 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }

    /// <summary>
    /// **The second part of layer 2. Long-period ground motion brings down tall buildings
    /// that would otherwise have survived.** <b>Sim thread only.</b> Off by default.
    ///
    /// ── This is not a visualisation ──────────────────────────────
    ///
    /// As <see cref="LongPeriodResponse"/>'s class doc explains, vanilla's shaking has no
    /// long-period component, and building height enters neither the shaking nor the
    /// damage (§A-7 / §A-3). The buildings that fall here are **buildings that would not
    /// have fallen in vanilla**. That is why it is off by default, why it is always
    /// displayed as layer 2 (<c>Strings.SourceModel</c>), and why the strength slider can
    /// take it to 0.
    ///
    /// ── Additive only. Nothing is suppressed ─────────────────────
    ///
    /// Vanilla's destruction is neither patched nor interfered with. A building that has
    /// already collapsed is rejected by <c>CollapseBuilding</c> itself
    /// (<c>CommonBuildingAI.CollapseBuilding</c> IL_0007: immediately false on
    /// <c>m_flags &amp; 0x400000</c>), so there is no double damage.
    ///
    /// ── We do not go through <c>DisasterHelpers</c> (§E-2) ────────────────
    ///
    /// Natural Disasters Renewal replaces <c>DisasterHelpers.DestroyBuildings</c>
    /// wholesale with a Prefix. Calling <c>BuildingAI.CollapseBuilding</c> directly
    /// **routes around both of NDR's patch surfaces entirely**, so this feature runs on
    /// its own terms regardless of that mod's destruction settings (the same judgement as
    /// ③'s fire whirl).
    ///
    /// **Never write <c>Building.m_fireIntensity</c> directly.** The only family that
    /// consumes that field is <c>CommonBuildingAI</c>'s; write it on any other AI and you
    /// get a permanent ghost fire that nobody puts out, and because **it lives in
    /// vanilla's building array it is baked into the save and survives removing the
    /// mod**. This project shipped exactly that once. The <c>burnAmount</c> passed here
    /// is <b>0</b> (long-period damage is "shaken until it collapses", not burning), and
    /// vanilla looks after the fire intensity.
    ///
    /// ── The ceiling on work per tick (stated explicitly) ──────────────────
    ///
    /// The sweep only runs **while the earthquake is Active**, and its interval is the
    /// **elapsed game time** corresponding to <see cref="IntervalFrames"/> frames (never
    /// <c>frameIndex % N</c> — <c>m_currentFrameIndex</c> advances by
    /// <c>FinalSimulationSpeed</c> (1/3/9) per tick, so a modulo makes the check fire
    /// erratically with game speed. Fire whirl design doc, appendix A-4).
    ///
    /// One sweep is capped at <b><see cref="MaxCellsPerPass"/> grid cells</b> and
    /// <b><see cref="MaxBuildingsPerPass"/> buildings</b>. The reach can be up to
    /// 2 × 7100 m, i.e. the whole building grid (270×270 cells of 64 m), so on hitting
    /// the cap we **stop there and the next sweep resumes where we stopped**
    /// (<see cref="_cursorOrdinal"/>). The selection depends on (quake, building) alone
    /// with no frame mixed in, so cutting off part way does not change the conclusion.
    ///
    /// **The sweep order runs outwards from the epicentre**
    /// (<see cref="OutwardCellOrder"/>). In row-major order the first thing you look at
    /// is the corner of the rectangle — the furthest point from the epicentre, where the
    /// probability is near 0 — so the cap would **throw away the buildings most likely to
    /// fall** (layer-2 review I1. The full reasoning behind inventing the order is in
    /// <see cref="OutwardCellOrder"/>'s class doc).
    ///
    /// For reference: vanilla's own whole-quake disc sweeps a grid of
    /// <c>preRadius + 72</c>, up to 7,172 m, **on every simulation step**, with no cap at
    /// all (§A-3). The cap here is far more conservative than that.
    ///
    /// **Do not sweep on the tick right after the earthquake changed.**
    /// <c>SeismographRecorder.Rescan</c> walks every slot of the building buffer on an
    /// earthquake's starting tick, so do not pile on top of it. But <b>do not wind the
    /// interval accumulator (<see cref="_minutesSincePass"/>) back</b> — with several
    /// earthquakes running at once and <c>SelectDamaging</c>'s choice switching back and
    /// forth, an implementation that winds it back resets the accumulator to 0 every time
    /// and **the sweep never runs at all** (layer-2 review I3).
    ///
    /// ── Only one earthquake, "the strongest right now" (stated explicitly) ──────
    ///
    /// It tracks only the one <c>QuakeSelection.SelectDamaging</c> picks. Even with two
    /// or more earthquakes Active at once, only the area around that one takes extra
    /// damage (the same limit as <c>TsunamiChain</c>, and as there it is declared in the
    /// class doc). When the choice switches, the sweep position
    /// (<see cref="_cursorOrdinal"/>) is discarded, measurement restarts from the new
    /// epicentre, and the fact is logged in one diagnostic line.
    ///
    /// ── Diagnostics (not repeating ③'s failure) ──────────────────────────
    ///
    /// In ③ we shipped a defect with "no way whatsoever to see from the diagnostics
    /// whether fire spread was running". Here we always carry <c>scanned</c>,
    /// <c>selected</c>, <c>attempted</c>, <c>refused</c> and <c>collapsed</c>, and
    /// **print a line even when nothing collapsed** — otherwise "it is broken" and "there
    /// is nothing nearby to act on" become indistinguishable in the log.
    /// </summary>
    public static class LongPeriodDamage
    {
        /// <summary>
        /// The interval between sweeps (in game time equivalent to frames). It matches
        /// vanilla's <c>SimulationStep</c> interval per disaster.
        /// </summary>
        private const int IntervalFrames = 256;

        /// <summary>The maximum grid cells looked at in one sweep. The reach can cover the whole grid.</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>The maximum buildings examined in one sweep.</summary>
        private const int MaxBuildingsPerPass = 2048;

        /// <summary>The building grid's side length in cells (each cell is 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>
        /// The maximum number of hops along one cell's linked list (insurance against a
        /// corrupt save). The same 49152 as the inner loop of vanilla's
        /// <c>DisasterHelpers.DestroyBuildings</c>, i.e. the size of the building buffer
        /// (the <c>ldc.i4 49152</c> at IL_0521).
        /// </summary>
        private const int GridChainGuard = 49152;

        /// <summary>
        /// The flag condition for being a candidate. **The same mask and the same
        /// comparison as vanilla's first-pass cull in <c>DestroyBuildings</c>**
        /// (§A-3, <c>(m_flags &amp; 0x80013) != 1</c>), plus the exclusion of
        /// <c>Collapsed</c>.
        ///
        /// <c>Collapsed</c> is excluded because <c>CollapseBuilding</c> always returns
        /// false for it; without the exclusion, the rubble left where we already succeeded
        /// piles into `refused` every pass and the diagnostic numbers become unreadable
        /// (the same reason as <c>FireWhirlDamage.CollectMask</c>).
        ///
        /// **A burning building is not excluded.**
        /// <c>CommonBuildingAI.CollapseBuilding</c> collapses it without looking at the
        /// fire intensity (measured in the IL). A burning tower crushed by long-period
        /// motion is exactly the behaviour this feature exists to express.
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>The key for self-reporting a degraded state (<c>FeatureHost.NoteDegraded</c>).</summary>
        private const string HeightNoteKey = "eqLongPeriodHeight";

        private static float _minutesSincePass;
        private static ushort _quakeId;

        /// <summary>
        /// The ordinal of the next cell to look at (in <see cref="OutwardCellOrder"/>'s
        /// order). 0 is the epicentre's cell. It stays non-zero only when the sweep was
        /// cut off at the cap.
        /// </summary>
        private static int _cursorOrdinal;

        private static bool _heightNotePosted;
        private static bool _errorLogged;

        // ── Diagnostic counters (all read and written from the sim thread only) ─────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastSelected;
        private static int _lastAttempted;
        private static int _lastRefused;
        private static int _lastCollapsed;
        private static int _lastUnknownHeight;
        private static bool _lastCapped;
        private static int _totalCollapsed;

        /// <summary>How many sweeps have run so far (session total).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>Buildings examined in the last sweep (those that passed the candidate mask and were in range).</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>Buildings that won the probability draw in the last sweep.</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>Buildings vanilla's dry run said it would accept, in the last sweep.</summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>Buildings vanilla said it refuses by design, in the last sweep.</summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>Buildings that actually collapsed in the last sweep.</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>
        /// Buildings in the last sweep **whose height could not be read**. A non-zero
        /// value is itself a sign something is wrong, and no extra damage at all was
        /// applied to those buildings.
        /// </summary>
        public static int LastUnknownHeight { get { return _lastUnknownHeight; } }

        /// <summary>Whether the last sweep was cut off at the cap (it resumes next time).</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>Session total of buildings collapsed.</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>**Always call this on level unload.** Carry nothing across cities.</summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _quakeId = 0;
            _cursorOrdinal = 0;
            _passes = 0;
            _lastScanned = 0;
            _lastSelected = 0;
            _lastAttempted = 0;
            _lastRefused = 0;
            _lastCollapsed = 0;
            _lastUnknownHeight = 0;
            _lastCapped = false;
            _totalCollapsed = 0;
            if (_heightNotePosted)
            {
                _heightNotePosted = false;
                FeatureHost.ClearDegraded(EarthquakeFeature.FeatureName, HeightNoteKey);
            }
            // _errorLogged is not reset. "It throws" is a fact about the game build this
            // DLL is referencing, not per-city state (the same judgement as
            // EarthquakeReader._readErrorLogged).
        }

        /// <summary>
        /// Sim thread. **Always call it below the pause guard in
        /// <c>EarthquakeFeature.OnSimulationTick</c>** (otherwise buildings collapse while
        /// the game is paused). When the setting is off, the caller does not call it.
        /// </summary>
        public static void Apply(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            if (snapshot == null || !snapshot.Valid) return;

            try
            {
                Step(snapshot, deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("long-period damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqLongPeriod", "long-period damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            // ★ Advance the interval accumulator **before** looking for the target
            //    earthquake. Whether the earthquake changes or disappears, time is
            //    treated as continuing to pass. Make this per-earthquake state and the
            //    accumulator resets to 0 every time several earthquakes move in and out
            //    of Emerging/Active, so the sweep never runs at all (layer-2 review I3).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;

            // Cap the accumulator at the interval, so that however many hours pass with
            // no earthquake at all the value does not keep growing (and does not eat
            // float precision). Capping it does not change the conclusion "the interval
            // has been reached".
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            // Only consider an earthquake whose destruction is actually running. Vanilla's
            // whole-quake DestroyBuildings exists **only in SimulationStep's Active
            // branch** (§A-3), so flattening buildings before the main shock (Emerging)
            // would mean they fall before the ground shakes.
            var quake = QuakeSelection.SelectDamaging(snapshot.Quakes);
            if (quake == null || quake.Phase != EarthquakePhase.Active)
            {
                if (_quakeId != 0) Forget();
                return;
            }

            if (quake.DisasterId != _quakeId)
            {
                // ★ Do not sweep on the tick where the earthquake changed.
                //    SeismographRecorder.Rescan walks every slot of the building buffer
                //    on that same tick, so do not pile on top of it.
                //    **Do not wind the accumulator back** (see the comment above). Only
                //    the sweep position is discarded — with a different epicentre, both
                //    the rectangle and the rings' centre are different things, so there
                //    is no sense in resuming where we stopped.
                ushort previous = _quakeId;
                Forget();
                _quakeId = quake.DisasterId;

                // Never switch silently. Log the moment the "only one target" limit (see
                // the class doc) actually bites. Log.Diag is throttled per key.
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "longPeriodSwitch",
                    "damaging quake changed #" + previous + " -> #" + quake.DisasterId
                    + "; sweep position discarded, interval carried over");
                return;
            }

            // If the conversion is unavailable we do not run (the interval is undefined).
            // The accumulator has advanced above, so we start working normally from the
            // tick where it becomes available.
            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;

            // Do not carry the remainder over. Even if a large deltaMinutes arrives (just
            // after a load, say), we do not fire again on the next tick and keep to "once
            // per interval" (the same as FireWhirlDamage).
            _minutesSincePass = 0f;

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            if (strength < 0) strength = 0;

            // ★ The time-of-day factor (Task 11). **This is the one and only point where
            //    it is applied, and it applies only to layer 2's extra damage**; it
            //    touches neither vanilla's damage nor layer 1's display (see
            //    TimeOfDayFactor's class doc). The time comes from the sim thread's
            //    m_dayTimeFrame, not the m_currentDayTimeHour the main thread writes
            //    (§F-1; EarthquakeReader is the one reading it).
            //
            //    **The formula does not change when the day/night cycle is off.** The
            //    clock is pinned at 12.0 (§F-1), so the factor naturally comes out as
            //    1.0. Adding a special-case branch would add a path that only runs with
            //    day/night off, which is hard to verify. The panel and the diagnostic
            //    dump state the fact instead (Strings.EarthquakeNoDayNight).
            float timeFactor = TimeOfDayFactor.Of(snapshot.HourOfDay);

            Sweep(quake, strength, timeFactor);
        }

        /// <summary>
        /// Stops tracking. **The interval accumulator
        /// (<see cref="_minutesSincePass"/>) is left alone** — it is state about time, not
        /// about the earthquake, and winding it back as earthquakes come and go stops the
        /// sweep running at all (see the class doc / layer-2 review I3). The counters are
        /// kept for diagnostics.
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _cursorOrdinal = 0;
        }

        /// <summary>
        /// One sweep. On hitting the cap it stops, and the next one resumes from
        /// <see cref="_cursorOrdinal"/> (see "the ceiling on work per tick" in the class
        /// doc).
        ///
        /// **Cells are visited outwards from the epicentre**
        /// (<see cref="OutwardCellOrder"/>). Once a circuit completes, the ordinal returns
        /// to 0 and measurement restarts from the epicentre — <see cref="IsSelected"/>
        /// mixes in neither the frame nor the cell order, so the same building gives the
        /// same conclusion, and a building that has fallen is dropped by
        /// <see cref="CandidateMask"/>'s <c>Collapsed</c>.
        /// </summary>
        private static void Sweep(EarthquakeReading quake, int strength, float timeFactor)
        {
            // ★ When sInstance is null, Singleton<T>.instance runs FindObjectOfType and
            //    new GameObject, which is a main-thread-only API, so check `exists` first
            //    (the same reason as Log.CurrentFrame; likewise in TsunamiChain and
            //    EarthquakeReader).
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            float range = LongPeriodResponse.RangeOf(quake.Intensity);
            var epicentre = quake.Epicentre.ToVec2();

            // The building grid is 270x270 cells of 64 m. Clamp so we never run off the
            // edge (the same cell size of 64, offset of 135 and [0,269] as vanilla's
            // DestroyBuildings).
            int minX = Clamp((int)((epicentre.X - range) / 64f + 135f));
            int maxX = Clamp((int)((epicentre.X + range) / 64f + 135f));
            int minZ = Clamp((int)((epicentre.Z - range) / 64f + 135f));
            int maxZ = Clamp((int)((epicentre.Z + range) / 64f + 135f));

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // The rings are centred on the epicentre's cell. The same clamp as the
            // rectangle is applied, so even with the epicentre outside the map the centre
            // always lands inside the grid.
            int centreX = Clamp((int)(epicentre.X / 64f + 135f));
            int centreZ = Clamp((int)(epicentre.Z / 64f + 135f));
            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(quake.DisasterId);

            int scanned = 0, selected = 0, attempted = 0, refused = 0, collapsed = 0;
            int unknownHeight = 0, cells = 0;
            bool capped = false;

            int ordinal = _cursorOrdinal;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;
            int startOrdinal = ordinal;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass || scanned >= MaxBuildingsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                bool ok = OutwardCellOrder.Offset(ordinal, out dx, out dz);
                ordinal++;
                if (!ok) continue;

                int x = centreX + dx;
                int z = centreZ + dz;

                // ★ Cells outside the rectangle are **skipped without being counted**
                //    (see OutwardCellOrder's class doc). Counting them eats into the cap
                //    by however much lies outside, and the diagnostic's `cells` stops
                //    being "the number of cells actually looked at".
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ Take the next ID **before** doing anything. Vanilla's
                    //    DisasterHelpers.DestroyBuildings has the same shape: it copies
                    //    m_nextGridBuilding into a local at the top of the loop (IL_00E2)
                    //    and uses it at IL_0516, after calling CollapseBuilding twice.
                    //    No vanilla CollapseBuilding implementation calls ReleaseBuilding,
                    //    so today the two are equivalent, but with a third-party AI that
                    //    releases the building on collapse, buildings[id] is zeroed and
                    //    **the rest of this cell is silently skipped**. One local closes
                    //    that off.
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        float d = Distance(epicentre, p.x, p.z);
                        if (d < range)
                        {
                            scanned++;
                            float metres = BuildingHeight.MetresOf(ref buildings[id]);
                            if (metres <= 0f)
                            {
                                // ★ **Do nothing** to a building whose height is unknown.
                                //    Applying "the taller it is, the more likely it is to
                                //    fall" using a guessed height would be exactly the
                                //    kind of lie this mod hates most.
                                unknownHeight++;
                            }
                            else if (IsSelected(quake, id, metres, d, strength, timeFactor))
                            {
                                selected++;
                                bool accepted;
                                if (Collapse(buildings, id, group, out accepted)) collapsed++;
                                if (accepted) attempted++; else refused++;
                            }
                        }
                    }

                    id = next;

                    // Insurance against looping forever on a save whose linked list is
                    // corrupt. The limit is the same 49152 as vanilla's inner loop (the
                    // size of the building buffer; DestroyBuildings IL_0521). More than
                    // that cannot possibly be chained in one cell.
                    if (++guard > GridChainGuard) break;
                }
            }

            // If a circuit completed, start from the epicentre next time. If we were cut
            // off, resume where we stopped.
            _cursorOrdinal = ordinal >= ordinalCount ? 0 : ordinal;
            _passes++;
            _lastScanned = scanned;
            _lastSelected = selected;
            _lastAttempted = attempted;
            _lastRefused = refused;
            _lastCollapsed = collapsed;
            _lastUnknownHeight = unknownHeight;
            _lastCapped = capped;
            _totalCollapsed += collapsed;

            UpdateHeightNote(scanned, unknownHeight);

            // ★ Never wrap this in `collapsed > 0`. That makes "the feature is dead" and
            //    "there are no tall buildings nearby" indistinguishable in the log (the
            //    exact shape that bit us in ③). Log.Diag is throttled per key, so writing
            //    it every time does not flood anything.
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "longPeriod",
                "pass#" + _passes + " quake#" + quake.DisasterId
                + " strength=" + strength
                + " timeFactor=" + timeFactor.ToString("F2")
                + " range=" + range.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                // The sweep runs outwards from the epicentre's cell (ordinal 0). The next
                // resume point is printed too, so that "it never reached the epicentre"
                // is visible from the diagnostics.
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
                + " scanned=" + scanned + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " collapsed=" + collapsed
                + " unknownHeight=" + unknownHeight
                + (capped ? " (capped; resumes next pass)" : ""));
        }

        /// <summary>
        /// Whether to select this building. **The randomness is
        /// <see cref="DeterministicRandom"/>**, not <c>VanillaRandomizer</c> — this is
        /// **a judgement this mod invented**, and it need not agree with the values
        /// vanilla draws. Making them agree would be worse: it would blend into the
        /// threshold layer 1 reads ahead, and you could no longer tell which layer a
        /// conclusion came from.
        ///
        /// **Never mix in the frame.** Mix it in and the same building is re-drawn on
        /// every sweep, so the number of buildings destroyed grows without limit over
        /// time.
        /// </summary>
        private static bool IsSelected(EarthquakeReading quake, ushort buildingId,
                                       float heightMetres, float distance, int strength,
                                       float timeFactor)
        {
            float chance = LongPeriodResponse.ExtraCollapseChance(
                heightMetres, distance, quake.Intensity, strength);
            // The time-of-day factor is applied **after** the cap (MaxExtraChance). We
            // want the cap to keep meaning "the largest value this model itself
            // produces", so there is no re-clamp here (the maximum is
            // 0.25 x 1.15 = 0.2875).
            chance *= timeFactor;
            if (chance <= 0f) return false;

            float roll = DeterministicRandom.Unit(quake.DisasterId, buildingId);
            return roll < chance;
        }

        /// <summary>
        /// Actually brings it down. **It does not go through <c>DisasterHelpers</c>** (see
        /// the class doc).
        ///
        /// The dry run (<c>testOnly: true</c>) is a side-effect-free query
        /// (<c>CommonBuildingAI.CollapseBuilding</c> IL_0013: on <c>testOnly</c> it does
        /// <c>ldc.i4.1; ret</c> before any write). **But we make the real call even when
        /// the dry run returns false** — <c>PowerPoleAI.CollapseBuilding</c> performs the
        /// actual collapse right after <c>if (testOnly) return false;</c> (measured in the
        /// IL), so trusting the dry run and not calling would swallow a real collapse
        /// (the same judgement as <c>FireWhirlDamage.Ignite</c>).
        /// </summary>
        /// <param name="accepted">
        /// Whether vanilla itself answered the dry run with "I will accept this". The
        /// same definition as <c>FireWhirlDamage</c>'s `attempted`, and this count is the
        /// only evidence that lets us detect the feature swinging at nothing.
        /// </param>
        private static bool Collapse(Building[] buildings, ushort id,
                                     InstanceManager.Group group, out bool accepted)
        {
            accepted = false;

            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            var ai = info.m_buildingAI;

            // demolish: false (leave the rubble), burnAmount: 0 (it is shaken until it
            // collapses, not burnt). m_fireIntensity is never touched.
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// The disaster group. Passing it makes vanilla's own tallies (buildings damaged
        /// per disaster) add up correctly. <c>null</c> if <c>InstanceManager</c> is not
        /// there yet (<c>Group</c> is a reference type and vanilla itself has paths that
        /// pass <c>null</c>; only the tally is lost, and the collapse still happens).
        /// <c>DisasterAI.CreateDisaster</c> creates it with
        /// <c>m_ownerInstance.Disaster = the disaster ID</c> and registers it with
        /// <c>InstanceManager</c> (confirmed in the IL during ③).
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // When sInstance is null, Singleton<T>.instance runs FindObjectOfType and
            // new GameObject, which is a main-thread-only API (see Log.CurrentFrame's
            // doc). This is the sim thread, so check `exists` first.
            if (!Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// Self-reports "not one building's height could be read". **Never create a state
        /// where it silently does nothing.** In an environment where heights cannot be
        /// read this feature correctly does nothing, but that is indistinguishable from
        /// "it is not working", so it always declares itself.
        /// </summary>
        private static void UpdateHeightNote(int scanned, int unknownHeight)
        {
            bool broken = scanned > 0 && unknownHeight == scanned;
            if (broken == _heightNotePosted) return;

            _heightNotePosted = broken;
            if (broken)
            {
                FeatureHost.NoteDegraded(EarthquakeFeature.FeatureName, HeightNoteKey,
                    "no building height could be read (BuildingInfo.m_size.y and "
                    + "m_generatedInfo.m_size.y were both unusable); long-period damage is "
                    + "applying nothing rather than guessing a height");
            }
            else
            {
                FeatureHost.ClearDegraded(EarthquakeFeature.FeatureName, HeightNoteKey);
            }
        }

        private static float Distance(Vec2 epicentre, float x, float z)
        {
            float dx = x - epicentre.X;
            float dz = z - epicentre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > GridSide - 1) return GridSide - 1;
            return v;
        }
    }
}
