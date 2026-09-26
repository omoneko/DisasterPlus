using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>In a trench earthquake, fires and collapses happen even far from the
    /// epicentre.</b> **Sim thread only.**
    ///
    /// ── The owner's instruction (2026-09-02) ────────────────────────────
    ///
    /// &gt; The trench earthquake does too little damage. Make fires and collapses
    /// &gt; happen at some rate even far from the epicentre (scaled to the magnitude).
    ///
    /// The probability model, and the full explanation of why only the trench quake does
    /// so little damage, are in <see cref="DistantDamage"/>'s class doc. This file is
    /// <b>the part that distributes it to buildings</b>, and the skeleton of the sweep is
    /// copied straight from <see cref="LongPeriodDamage"/>.
    ///
    /// ── ★★ Why <b>only</b> the trench quake ────────────────────────────
    ///
    /// A fault quake's epicentre is <b>wherever the player pointed</b>, so vanilla's disc
    /// lands on the city. Do not change what is not broken — add this to every earthquake
    /// and <b>the difficulty of fault quakes, which nobody asked about, quietly goes up
    /// too</b>.
    ///
    /// The only thing used to tell them apart is <c>TrenchQuakeSlot.IsTrenchQuake</c>
    /// (the pair of disaster ID and random seed). **Never decide it from whether the
    /// hypocentre is over water** — an earthquake the player placed out at sea from
    /// vanilla's disaster panel was placed as a fault quake and meant as one (the
    /// discipline established in <c>TrenchQuakeSlot</c>'s class doc).
    ///
    /// ── The order of collapse and fire ──────────────────────────────────
    ///
    /// The collapse is drawn first, and only the buildings that miss draw for fire. It
    /// cannot be the other way round — <c>CommonBuildingAI.BurnBuilding</c> checks
    /// <c>Collapsed</c> and refuses (IL_0019), so a collapsed building does not burn.
    /// **The two draws use different keys** (<see cref="DistantDamage.FireSalt"/>). With
    /// the same key you get a correlation where a building that did not collapse is less
    /// likely to burn.
    ///
    /// ── The draw depends on (quake, building) and nothing else ──────────
    ///
    /// ★★ **Never mix in the frame or the sweep count** (the same discipline as
    ///   <see cref="LongPeriodDamage"/>). Mix them in and the same building is re-drawn
    ///   on every sweep, so <b>the longer the quake runs the more it destroys, without
    ///   limit</b>. Leave them out and the total damage <b>holds no matter how many
    ///   sweeps run</b>, so the rough figures in <see cref="DistantDamage"/>'s doc are the
    ///   expected values in the real game.
    ///
    /// ── Additive only. Nothing is suppressed ────────────────────────────
    ///
    /// Vanilla's destruction is neither patched nor interfered with. Nor do we go through
    /// <c>DisasterHelpers</c> (for the same reason as <see cref="LongPeriodDamage"/>: to
    /// route around NDR's patch surface).
    ///
    /// ★★ <b>Never write <c>Building.m_fireIntensity</c> directly.</b>
    ///   Do so and you get a permanent ghost fire that nobody puts out, and because
    ///   **it lives in vanilla's building array it is baked into the save and survives
    ///   removing the mod**. This project shipped exactly that once. Let vanilla
    ///   (<c>BurnBuilding</c>) look after the fire intensity.
    ///
    /// ── The ceiling on work per tick ────────────────────────────────────
    ///
    /// The reach can be up to 21,300 m, i.e. the whole building grid (270×270 cells of
    /// 64 m), so we cut off at the cap and the next sweep resumes where we stopped.
    /// **The order runs outwards from the epicentre** (<see cref="OutwardCellOrder"/>) —
    /// in row-major order the first thing you look at is the corner of the rectangle,
    /// which is where the probability is lowest.
    /// </summary>
    public static class TrenchQuakeDistantDamage
    {
        /// <summary>
        /// The interval between sweeps (in game time equivalent to frames).
        ///
        /// ★ Shorter than <see cref="LongPeriodDamage"/>'s 256. That one applies
        ///   <b>repeatedly</b> throughout the quake, whereas this one <b>finishes after a
        ///   single circuit</b> (<see cref="_circuitDone"/>), so <b>if that circuit does
        ///   not complete during the main shock, distant buildings never get drawn for at
        ///   all</b>. A circuit hits the cap and splits into 4 or 5 passes, so at 64 the
        ///   distribution is finished in about 6 game minutes.
        /// </summary>
        private const int IntervalFrames = 64;

        /// <summary>
        /// The maximum number of grid cells looked at in one sweep. The whole grid is
        /// 270x270 = 72,900.
        /// </summary>
        private const int MaxCellsPerPass = 65536;

        /// <summary>
        /// The maximum number of buildings examined in one sweep.
        ///
        /// ★ Larger than <see cref="LongPeriodDamage"/>'s 2,048. **The circuit has to
        ///   finish during the main shock** (<see cref="IntervalFrames"/>), and the cost
        ///   per building is only a distance and one hash (only the buildings that win
        ///   the draw go on to a virtual call), so 8,192 is still cheap.
        ///
        ///   For reference: vanilla's own whole-quake disc sweeps a grid of up to 7,172 m
        ///   <b>on every simulation step, with no cap at all</b> (§A-3).
        /// </summary>
        private const int MaxBuildingsPerPass = 8192;

        /// <summary>The building grid's side length in cells (each cell is 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>
        /// The maximum number of hops along one cell's linked list (insurance against a
        /// corrupt save). The same 49152 as the inner loop of vanilla's
        /// <c>DisasterHelpers.DestroyBuildings</c>, i.e. the size of the building buffer.
        /// </summary>
        private const int GridChainGuard = 49152;

        /// <summary>
        /// The flag condition for being a candidate. The same as
        /// <see cref="LongPeriodDamage"/>. Without excluding <c>Collapsed</c>, the rubble
        /// left where something already fell piles into `refused` every pass and the
        /// diagnostic numbers become unreadable.
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        private static float _minutesSincePass;
        private static ushort _quakeId;

        /// <summary>
        /// The <c>StartFrame</c> of the earthquake being tracked. **The disaster ID alone
        /// is not enough.**
        ///
        /// ★★ <c>DisasterManager.CreateDisaster</c> <b>reuses a free slot, taking the
        ///   first match</b> (pinned down in the IL by <c>TrenchQuakeSlot</c>'s class
        ///   doc). One trench quake ending and the next one <b>taking the same number</b>
        ///   happens routinely. Watch the ID alone and <see cref="_circuitDone"/> stays
        ///   raised, so <b>the second trench earthquake quietly does no damage at all</b>.
        ///   Look at the start frame too and it is clearly a different one.
        /// </summary>
        private static uint _quakeStartFrame;
        private static int _cursorOrdinal;
        private static bool _errorLogged;

        /// <summary>
        /// Whether this earthquake's distribution has completed one circuit. **Once it
        /// has, it never runs again.**
        ///
        /// ★★ Without this there are two ways it breaks. (2026-09-02, noticed while
        ///   implementing it.)
        ///
        ///   1. <b>A fire that was put out reignites again and again.</b> The draw is
        ///      decided by (quake, building), so a building that once came up "on fire"
        ///      <b>catches fire again on every circuit</b>. It burns again the instant
        ///      the fire service puts it out, and it does not stop until the quake ends.
        ///   2. We keep trying buildings vanilla refuses (parks, rubble, anything under
        ///      water) on every circuit, and the diagnostic's `refused` swells in
        ///      proportion to the quake's length.
        ///
        ///   The damage is distributed <b>once, as the shaking travels outwards</b>. That
        ///   is also the premise behind the rough figures in
        ///   <see cref="DistantDamage"/>'s doc.
        /// </summary>
        private static bool _circuitDone;

        // ── Diagnostic counters (all read and written from the sim thread only) ─────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastCollapsed;
        private static int _lastIgnited;
        private static int _lastRefused;
        private static bool _lastCapped;
        private static int _totalCollapsed;
        private static int _totalIgnited;

        /// <summary>How many sweeps have run so far (session total).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>Buildings examined in the last sweep (those that passed the candidate mask and were within reach).</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>Buildings that actually collapsed in the last sweep.</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>Buildings actually set alight in the last sweep.</summary>
        public static int LastIgnited { get { return _lastIgnited; } }

        /// <summary>
        /// Buildings selected in the last sweep that vanilla then refused: rubble, parks,
        /// anything under water. **The only number that distinguishes "the feature is
        /// dead" from "there is nothing to act on".**
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>Whether the last sweep was cut off at the cap.</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>Session total of buildings collapsed.</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>Session total of buildings set alight.</summary>
        public static int TotalIgnited { get { return _totalIgnited; } }

        /// <summary>On level unload. **Carry over no session state whatsoever.**</summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _quakeId = 0;
            _quakeStartFrame = 0u;
            _cursorOrdinal = 0;
            _circuitDone = false;
            _errorLogged = false;

            _passes = 0;
            _lastScanned = 0;
            _lastCollapsed = 0;
            _lastIgnited = 0;
            _lastRefused = 0;
            _lastCapped = false;
            _totalCollapsed = 0;
            _totalIgnited = 0;
        }

        /// <summary>
        /// Sim thread. **Always call it below the pause guard in
        /// <c>EarthquakeFeature.OnSimulationTick</c>** (otherwise buildings collapse while
        /// the game is paused).
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
                    Log.Error("trench-quake distant damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqDistant", "trench distant damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            // ★ Advance the interval accumulator **before** looking for the target
            //   earthquake (LongPeriodDamage's I3).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            EarthquakeReading quake = FindTrenchQuake(snapshot);
            if (quake == null)
            {
                if (_quakeId != 0) Forget();
                return;
            }

            if (quake.DisasterId != _quakeId || quake.StartFrame != _quakeStartFrame)
            {
                // ★ Do not sweep on the tick where the earthquake changed
                //    (SeismographRecorder.Rescan walks every slot on that same tick, so
                //    do not pile on top of it).
                //    **Do not wind the accumulator back.**
                Forget();
                _quakeId = quake.DisasterId;
                _quakeStartFrame = quake.StartFrame;
                return;
            }

            // ★★ This earthquake's distribution is already complete (see _circuitDone's
            //    doc). Let the accumulator advance but never enter the sweep.
            if (_circuitDone) return;

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;

            // Do not carry the remainder over (the same as LongPeriodDamage /
            // FireWhirlDamage).
            _minutesSincePass = 0f;

            int strength = ModSettings.EarthquakeTrenchDamageStrength.value;
            if (strength <= 0) return;

            Sweep(quake, strength);
        }

        /// <summary>
        /// The trench earthquake that should be distributing damage right now, or null.
        ///
        /// ★★ <c>QuakeSelection.SelectDamaging</c> is not used. That picks "the single
        ///   strongest earthquake", so <b>with a fault quake running at the same time it
        ///   picks that one and the trench quake's distant damage quietly stops</b>. What
        ///   this is looking for is not "the strong earthquake" but "the trench
        ///   earthquake".
        ///
        /// ★ Only <c>Active</c> qualifies. Vanilla's destruction likewise exists only in
        ///   <c>SimulationStep</c>'s Active branch (§A-3), so flattening buildings before
        ///   the main shock would mean <b>they fall before the ground shakes</b>.
        ///
        /// ★★ <b>Do not look at <c>Located</c>. Never add it.</b>
        ///   (2026-09-02; a Codex review dug it out of a work-in-progress version. The
        ///    author had put it there thinking it was insurance against a broken read.)
        ///
        ///   <c>Located</c> (<c>DisasterPhases.Located</c> = 4096) is only set <b>when
        ///   the epicentre falls inside a seismograph's coverage</b> (§A-2 / §C-2). It is
        ///   a flag for <b>whether the hazard map may be painted</b>, not a condition on
        ///   damage — neither vanilla's <c>DestroyBuildings</c> nor
        ///   <c>QuakeSelection.SelectDamaging</c> looks at it even once.
        ///
        ///   And <b>a trench quake's epicentre is several kilometres out to sea</b>.
        ///   **Nobody builds a seismograph there.** So making this flag a condition means
        ///   <b>the feature never runs at all in most cities</b> — and the damage that was
        ///   asked for vanishes entirely, with no exception and no warning.
        ///
        ///   The epicentre's position comes from <c>m_targetPosition</c>
        ///   (<c>EarthquakeReader</c>) and is populated regardless of whether there is a
        ///   seismograph. **Nothing goes wrong by not looking at the flag.**
        /// </summary>
        private static EarthquakeReading FindTrenchQuake(EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            if (quakes == null) return null;

            for (int i = 0; i < quakes.Count; i++)
            {
                EarthquakeReading q = quakes[i];
                if (q == null) continue;
                if (q.Phase != EarthquakePhase.Active) continue;
                if (!TrenchQuakeSlot.IsTrenchQuake(q.DisasterId)) continue;
                return q;
            }

            return null;
        }

        /// <summary>
        /// Stop tracking. **The interval accumulator is left alone** (it is state about
        /// time, not about the earthquake). The counters are kept for diagnostics.
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _quakeStartFrame = 0u;
            _cursorOrdinal = 0;
            _circuitDone = false;
        }

        private static void Sweep(EarthquakeReading quake, int strength)
        {
            // ★ When sInstance is null, Singleton<T>.instance takes a main-thread-only
            //    path, so check `exists` first.
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            float reach = DistantDamage.ReachMetres(quake.Intensity);
            if (!(reach > 0f)) return;

            var epicentre = quake.Epicentre.ToVec2();

            // The building grid is 270x270 cells of 64 m (the same cell size of 64,
            // offset of 135 and [0,269] clamp as vanilla's DestroyBuildings).
            int minX = Clamp((int)((epicentre.X - reach) / 64f + 135f));
            int maxX = Clamp((int)((epicentre.X + reach) / 64f + 135f));
            int minZ = Clamp((int)((epicentre.Z - reach) / 64f + 135f));
            int maxZ = Clamp((int)((epicentre.Z + reach) / 64f + 135f));

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // ★ A trench quake's epicentre <b>can be outside the map</b> (out at sea).
            //   The same clamp is applied, so the ring's centre always lands inside the
            //   grid.
            int centreX = Clamp((int)(epicentre.X / 64f + 135f));
            int centreZ = Clamp((int)(epicentre.Z / 64f + 135f));
            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(quake.DisasterId);

            int scanned = 0, collapsed = 0, ignited = 0, refused = 0, cells = 0;
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
                //   (counting them would eat into the cap).
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ Take the next ID **before** doing anything. With a third-party AI
                    //    that releases the building on collapse, the rest of this cell
                    //    would silently be skipped.
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        float d = Distance(epicentre, p.x, p.z);
                        if (d < reach)
                        {
                            scanned++;
                            Hit(buildings, id, group, quake, d, strength,
                                ref collapsed, ref ignited, ref refused);
                        }
                    }

                    id = next;

                    if (++guard > GridChainGuard) break;
                }
            }

            // ★ Once a circuit is complete, this earthquake is finished (see
            //   _circuitDone's doc). If we were cut off, resume where we stopped.
            bool finished = ordinal >= ordinalCount;
            _cursorOrdinal = finished ? 0 : ordinal;
            if (finished) _circuitDone = true;
            _passes++;
            _lastScanned = scanned;
            _lastCollapsed = collapsed;
            _lastIgnited = ignited;
            _lastRefused = refused;
            _lastCapped = capped;
            _totalCollapsed += collapsed;
            _totalIgnited += ignited;

            // ★ Print a line even when it is 0. Otherwise "the feature is dead" and
            //   "there are no buildings in range" become indistinguishable in the log
            //   (the exact shape that bit us in ③).
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "trenchDistant",
                "pass#" + _passes + " quake#" + quake.DisasterId
                + " intensity=" + quake.Intensity
                + " strength=" + strength
                + " reach=" + reach.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
                + " scanned=" + scanned
                + " collapsed=" + collapsed + " ignited=" + ignited
                + " refused=" + refused
                + (capped ? " (capped; resumes next pass)"
                          : " (circuit complete; this quake is done)"));
        }

        /// <summary>
        /// The decision and the action for one building.
        ///
        /// **Collapse first; fire only if that misses** (see the class doc). For a
        /// collapsed building <c>BurnBuilding</c> would refuse anyway.
        /// </summary>
        private static void Hit(Building[] buildings, ushort id,
                                InstanceManager.Group group, EarthquakeReading quake,
                                float distance, int strength,
                                ref int collapsed, ref int ignited, ref int refused)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return;

            var ai = info.m_buildingAI;

            float collapseChance =
                DistantDamage.CollapseChance(distance, quake.Intensity, strength);

            if (collapseChance > 0f
                && DeterministicRandom.Unit(quake.DisasterId, id) < collapseChance)
            {
                // demolish: false (leave the rubble), burnAmount: 0 (it is crushed by the
                // shaking, not burnt). m_fireIntensity is never touched.
                //
                // ★ We make the real call even when the dry run returns false —
                //   PowerPoleAI.CollapseBuilding performs the actual collapse right after
                //   `if (testOnly) return false;` (measured in the IL).
                bool accepted = ai.CollapseBuilding(id, ref buildings[id], group,
                                                    true, false, 0);
                if (!accepted) refused++;
                if (ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0))
                {
                    collapsed++;
                }

                return;
            }

            // Do nothing to a building that was already burning before the check (the
            // same as FireWhirlDamage).
            if (buildings[id].m_fireIntensity != 0) return;

            float fireChance = DistantDamage.FireChance(distance, quake.Intensity, strength);
            if (fireChance <= 0f) return;

            // ★ Draw with **a different key** from the collapse (see
            //   DistantDamage.FireSalt's doc).
            uint fireKey = quake.DisasterId ^ DistantDamage.FireSalt;
            if (DeterministicRandom.Unit(fireKey, id) >= fireChance) return;

            bool burnable = ai.BurnBuilding(id, ref buildings[id], group, true);
            if (ai.BurnBuilding(id, ref buildings[id], group, false)) ignited++;
            else if (!burnable) refused++;
        }

        /// <summary>
        /// The disaster group. Passing it makes vanilla's own tallies (buildings damaged
        /// per disaster) add up correctly. Null if <c>InstanceManager</c> is not there yet
        /// (vanilla itself has paths that pass null; only the tally is lost, and the
        /// collapses and fires still happen).
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            if (!Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        private static int Clamp(int cell)
        {
            if (cell < 0) return 0;
            return cell > GridSide - 1 ? GridSide - 1 : cell;
        }

        private static float Distance(Vec2 epicentre, float x, float z)
        {
            float dx = x - epicentre.X;
            float dz = z - epicentre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
