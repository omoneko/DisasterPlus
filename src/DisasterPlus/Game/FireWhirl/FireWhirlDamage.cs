using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Scatters embers around the whirl. Call only from the sim thread (it writes the
    /// building buffer).
    ///
    /// It deliberately does not go through DisasterHelpers.
    /// A competing mod (Natural Disasters Renewal) replaces
    /// DisasterHelpers.DestroyBuildings outright with a Prefix, halving the destruction
    /// probability for calls it decides are a tornado and, depending on its settings,
    /// disabling the destruction altogether. On top of that the burnRadius its tornado
    /// test uses is pinned to the literal 0 inside VortexAI and cannot be changed from
    /// outside (design document §3.3(b)).
    ///
    /// So we put the one behaviour that defines this feature — scattering fire around —
    /// on a path of our own, where another mod's settings cannot affect it.
    /// </summary>
    public static class FireWhirlDamage
    {
        /// <summary>
        /// How often the spread check runs (expressed in sim frames). Checking every tick
        /// is expensive, and the fire spreads too fast.
        ///
        /// A modulo of the frame number cannot decide this.
        /// SimulationManager.SimulationStep loops FinalSimulationSpeed times within one
        /// tick (1/3/9 times at game speed 1/2/3), so m_currentFrameIndex jumps by 1/3/9
        /// per tick rather than by 1. With frameIndex % 16 the check becomes 3 times
        /// sparser at speed 2 and 9 times sparser at speed 3, so the spread rate quietly
        /// ends up depending on the game speed.
        /// We accumulate elapsed in-game time instead and check when it crosses the
        /// threshold.
        /// </summary>
        private const int IntervalFrames = 16;

        /// <summary>
        /// The flag condition for taking a building as a candidate: Created set and
        /// Collapsed clear.
        ///
        /// The point is to reject Collapsed (= BurnedDown, measured as 0x400000; they are
        /// the same value). Burnt-out rubble keeps Created and goes back to
        /// m_fireIntensity == 0, so without this exclusion it keeps being picked as a
        /// "building not yet on fire" and is always refused by
        /// CommonBuildingAI.BurnBuilding. The aftermath of a successful fire whirl is
        /// full of exactly this, so both the candidate and the selected counts would be
        /// permanently inflated and the numbers on the overlay would become unreadable.
        /// </summary>
        private const Building.Flags CollectMask =
            Building.Flags.Created | Building.Flags.Collapsed;

        private static readonly List<IgnitionCandidate> _candidates = new List<IgnitionCandidate>();
        private static readonly List<ushort> _selected = new List<ushort>();

        /// <summary>In-game minutes since the last spread check. Always reset to 0 on
        /// level unload.</summary>
        private static float _minutesSinceSpread;

        // --- Diagnostic counters -------------------------------------------
        // All of these are read and written only from the sim thread (both Apply and
        // WriteDiagnostics are on the sim thread).
        // Whether the spread was working at all was completely invisible in
        // WriteDiagnostics' enabled/active/scan. This is the path where the third wrong
        // assumption about the IL was born (writing m_fireIntensity directly) and which
        // logged nothing at all, so this is the one place we put numbers out.
        private static int _passes;
        private static int _lastCandidates;
        private static int _lastSelected;
        private static int _lastAttempted;
        private static int _lastRefused;
        private static int _lastIgnited;
        private static int _totalIgnited;

        private static readonly BarrenSpreadTracker _barren = new BarrenSpreadTracker();
        private static bool _barrenAlertPending;
        private static bool _barrenRecoveryPending;

        /// <summary>How many spread checks have run so far (session total).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>How many buildings the most recent check found within the radius
        /// (summed over all whirls).</summary>
        public static int LastCandidates { get { return _lastCandidates; } }

        /// <summary>How many buildings passed the probabilistic selection in the most
        /// recent check (summed over all whirls).</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>
        /// How many times the most recent check called BurnBuilding on a building vanilla
        /// said it would accept (summed over all whirls). This is the count that counts
        /// as evidence for the no-effect detection.
        /// </summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>
        /// How many buildings in the most recent check were selected but are refused by
        /// vanilla by design (summed over all whirls): rubble, parks, fire stations,
        /// buildings under water and so on. Not counted as evidence.
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>How many buildings the most recent check set alight (summed over all
        /// whirls).</summary>
        public static int LastIgnited { get { return _lastIgnited; } }

        /// <summary>The session total of buildings set alight.</summary>
        public static int TotalIgnited { get { return _totalIgnited; } }

        /// <summary>How many times in a row we tried and got 0 buildings.</summary>
        public static int BarrenStreak { get { return _barren.Streak; } }

        /// <summary>Whether the no-effect streak has reached the threshold. For the badge
        /// on the overlay.</summary>
        public static bool SpreadLooksBroken { get { return _barren.Tripped; } }

        public static int BarrenThreshold { get { return _barren.Threshold; } }

        /// <summary>
        /// Returns, once only, whether the no-effect detection has "just now" reached the
        /// threshold. The caller (FireWhirlFeature) uses it to emit the log line and the
        /// Degraded record exactly once.
        /// </summary>
        public static bool ConsumeBarrenAlert()
        {
            if (!_barrenAlertPending) return false;
            _barrenAlertPending = false;
            return true;
        }

        /// <summary>
        /// Returns, once only, whether a no-effect streak that had reached the threshold
        /// has "just now" cleared. The caller (FireWhirlFeature) uses it to withdraw its
        /// self-reported Degraded state.
        /// </summary>
        public static bool ConsumeBarrenRecovery()
        {
            if (!_barrenRecoveryPending) return false;
            _barrenRecoveryPending = false;
            return true;
        }

        public static void Reset()
        {
            _candidates.Clear();
            _selected.Clear();
            _minutesSinceSpread = 0f;

            _passes = 0;
            _lastCandidates = 0;
            _lastSelected = 0;
            _lastAttempted = 0;
            _lastRefused = 0;
            _lastIgnited = 0;
            _totalIgnited = 0;
            _barren.Reset();
            _barrenAlertPending = false;
            _barrenRecoveryPending = false;
        }

        public static void Apply(uint frameIndex, float deltaMinutes, int spreadStrength)
        {
            if (spreadStrength <= 0) return;

            if (deltaMinutes > 0f) _minutesSinceSpread += deltaMinutes;
            float interval = IntervalFrames / FeatureHost.FramesPerMinute;
            if (_minutesSinceSpread < interval) return;

            // Do not carry the remainder over. Even if a large deltaMinutes arrives, as
            // it can right after a load, this keeps it at "once per interval" instead of
            // firing repeatedly on the following ticks.
            _minutesSinceSpread = 0f;

            var views = FireWhirlRegistry.Snapshot();
            if (views.Count == 0) return;

            var buildings = BuildingManager.instance.m_buildings.m_buffer;

            int candidates = 0, selected = 0, attempted = 0, refused = 0, ignited = 0;

            for (int w = 0; w < views.Count; w++)
            {
                var v = views[w];
                if (v.Ending) continue;

                CollectNearby(buildings, v);
                candidates += _candidates.Count;
                if (_candidates.Count == 0) continue;

                IgnitionSpread.Select(v.Center.ToVec2(), v.Radius, spreadStrength,
                                      _candidates, frameIndex, _selected);
                selected += _selected.Count;
                if (_selected.Count == 0) continue;

                int tried, refusedHere;
                int lit = Ignite(buildings, v.DisasterId, out tried, out refusedHere);
                attempted += tried;
                refused += refusedHere;
                ignited += lit;

                if (lit > 0)
                {
                    Log.Diag("ignite", "fire whirl " + v.DisasterId + " ignited " + lit);
                }
            }

            _passes++;
            _lastCandidates = candidates;
            _lastSelected = selected;
            _lastAttempted = attempted;
            _lastRefused = refused;
            _lastIgnited = ignited;
            _totalIgnited += ignited;

            // Do not wrap this output in ignited > 0. A spread that is completely dead
            // would then produce no output at all, making "the spread is broken"
            // indistinguishable in the log from "there is nothing nearby to burn" — the
            // exact shape of the failure that actually happened in ③.
            // Log.Diag is throttled per key, so writing every time does not flood it.
            Log.Diag("spread",
                "pass#" + _passes + " strength=" + spreadStrength
                + " candidates=" + candidates + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " ignited=" + ignited);

            bool wasTripped = _barren.Tripped;
            if (_barren.Record(attempted, ignited)) _barrenAlertPending = true;
            if (wasTripped && !_barren.Tripped) _barrenRecoveryPending = true;
        }

        /// <summary>
        /// Sets the selected buildings alight.
        ///
        /// Never write m_fireIntensity directly (established by reading the IL). The only
        /// thing that consumes this field is CommonBuildingAI.SimulationStepActive →
        /// HandleFire; BuildingAI.SimulationStep has no fire handling at all. PowerPoleAI /
        /// DecorationBuildingAI / WaterJunctionAI / OutsideConnectionAI / IntersectionAI /
        /// CableCarPylonAI / MonorailPylonAI / RaceStartGantryAI / WildlifeSpawnPointAI
        /// (and any mod-made direct descendant of BuildingAI) inherit BuildingAI
        /// directly, so nobody ever clears an intensity written into them.
        /// BurningBuildingScanner would count them as "burning" forever — and since the
        /// value goes into vanilla's own building array, it burns into the save and
        /// survives even after the mod is removed.
        ///
        /// Call vanilla's BuildingAI.BurnBuilding instead (public virtual, confirmed in
        /// the IL). BuildingAI's own default implementation just returns false, so
        /// buildings that cannot burn are rejected naturally. On the CommonBuildingAI side
        /// it goes through the flammability test in GetFireParameters (PlayerBuildingAI
        /// returns false when m_fireHazard == 0), the under-water test and the
        /// Collapsed/BurnedDown test, and then looks after m_fireIntensity,
        /// Frame.m_fireDamage, the Active flag, BuildingDeactivated, the renderer/colour/
        /// flag updates, propagation to sub-buildings and DisasterData.m_buildingFireCount.
        /// The fire intensity is decided there, per building, so we keep no constant of
        /// our own.
        ///
        /// We still never go through DisasterHelpers, so another mod's settings cannot
        /// affect this (the premise in the comment at the top of the class holds).
        /// </summary>
        /// <param name="attempted">
        /// How many times we called BurnBuilding on a building vanilla itself said, in the
        /// dry run, that it would accept. This is the only count that serves as evidence
        /// for the diagnostics (BarrenSpreadTracker). BurnBuilding is called on buildings
        /// where <see cref="CanBurn"/> is false too, but those are not counted here.
        /// Thanks to that definition, attempted &gt; 0 with ignited == 0 means "vanilla
        /// went back on its own dry-run answer", which is strong evidence.
        /// </param>
        /// <param name="refused">
        /// How many selected buildings vanilla refuses by design. Kept only so that the
        /// reason for attempted &lt; selected can be read off the overlay; it is not used
        /// as evidence.
        /// </param>
        private static int Ignite(Building[] buildings, ushort disasterId,
                                  out int attempted, out int refused)
        {
            // Passing the disaster group makes m_buildingFireCount accumulate correctly.
            // DisasterAI.CreateDisaster builds the group with
            // m_ownerInstance.Disaster = the disaster ID and registers it with
            // InstanceManager (confirmed in the IL), so we can look it up here.
            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            var group = InstanceManager.instance.GetGroup(groupId);

            attempted = 0;
            refused = 0;
            int ignited = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                ushort id = _selected[i];
                if (id == 0 || id >= buildings.Length) continue;
                if (buildings[id].m_fireIntensity != 0) continue;   // lit since the check

                var info = buildings[id].Info;
                if (info == null || info.m_buildingAI == null) continue;

                bool burnable = CanBurn(id, ref buildings[id], info.m_buildingAI, group);
                if (burnable) attempted++; else refused++;

                // Make the real call even when burnable is false. Trusting the dry run
                // and skipping the call would suppress a genuine ignition wherever the
                // dry run and the real call disagree (another mod's patch, for
                // instance). And if it does ignite here, ignited goes up and the
                // no-effect verdict is cleared, so its correctness as evidence is not
                // broken either.
                if (info.m_buildingAI.BurnBuilding(id, ref buildings[id], group, false)) ignited++;
            }
            return ignited;
        }

        /// <summary>
        /// Whether a BurnBuilding on this building could get through "as vanilla designed
        /// it". We ask vanilla itself, as a dry run (testOnly = true).
        ///
        /// Why this is needed: to keep the things "vanilla refuses by design" out of the
        /// evidence for the no-effect detection. Without it, in the steady state after a
        /// successful fire whirl — still-burning buildings plus burnt-out rubble — it is
        /// the rubble alone that is tried and refused, over and over, so the no-effect
        /// streak is guaranteed to pile up even in a perfectly healthy city. The streak
        /// only comes down on an ignition, so it reaches the threshold for certain and
        /// produces a warning that "BurnBuilding is refusing everything" — literally true
        /// and utterly misleading.
        ///
        /// Do not copy the refusal conditions yourself. This is in fact a polymorphic
        /// call, and reading "the body of CommonBuildingAI.BurnBuilding" is not enough.
        /// Four types in the assembly declare BurnBuilding (measured):
        ///     BuildingAI        (directly under PrefabAI)   ldc.i4.0; ret  = always false
        ///     CommonBuildingAI  (BuildingAI)                168 instructions = the body
        ///     ShelterAI         (via PlayerBuildingAI)      ldc.i4.0; ret  = always false
        ///     TsunamiBuoyAI     (via PlayerBuildingAI)      ldc.i4.0; ret  = always false
        /// ShelterAI and TsunamiBuoyAI derive from PlayerBuildingAI → CommonBuildingAI, so
        /// `is CommonBuildingAI` is true for them, but they do not declare
        /// GetFireParameters (i.e. they inherit PlayerBuildingAI's "flammable if
        /// m_fireHazard != 0") and their BurnBuilding returns false without looking at any
        /// condition at all. In other words, a copy of "the three conditions" would
        /// wrongly judge them flammable. Both are Natural Disasters DLC buildings — that
        /// is, exactly what a player using this feature is building — so this is not a
        /// theoretical hole.
        ///
        /// You could also reject them by listing the type names, but such a list goes
        /// quietly stale the moment a game update adds a new override (the very shape of
        /// failure this groundwork is trying to catch). It would not cover another mod's
        /// BuildingAI descendants either.
        /// We delegate to the real thing instead. testOnly = true answers precisely this
        /// question:
        ///
        ///   From the IL (CommonBuildingAI.BurnBuilding):
        ///     IL_000F callvirt GetFireParameters / brfalse -> false
        ///     IL_0019 m_flags &amp; 0x400000 (Collapsed, same value as BurnedDown) -> false
        ///     IL_003A TerrainManager.WaterLevel(pos.xz) &gt; m_position.y -> false
        ///     IL_0053 ldarg.s 4 (testOnly) / brtrue IL_020C -> ldc.i4.1; ret
        ///   Every write in the method (m_buildingFireCount / m_flags / m_fireIntensity /
        ///   Frame.m_fireDamage) is at IL_00BB or later, i.e. only after that branch.
        ///   So the testOnly = true path is a pure query with no side effects (all 16
        ///   implementations of GetFireParameters were also measured to contain no
        ///   stfld/stsfld).
        ///
        /// With this delegation the verdict always comes back from the same override that
        /// will actually be called. If an override is added in future, or a mod swaps one
        /// in, it follows automatically.
        /// </summary>
        private static bool CanBurn(ushort id, ref Building b, BuildingAI ai,
                                    InstanceManager.Group group)
        {
            return ai.BurnBuilding(id, ref b, group, true);
        }

        /// <summary>
        /// Collects the buildings within the whirl's radius.
        /// Uses BuildingManager's spatial grid to avoid sweeping all 49152 slots.
        /// </summary>
        private static void CollectNearby(Building[] buildings, FireWhirlView v)
        {
            _candidates.Clear();

            var bm = BuildingManager.instance;
            float r = v.Radius;

            // The building grid is 270x270 cells of 64 m each. Clamp so we do not run off
            // the edges.
            int minX = Clamp((int)((v.Center.X - r) / 64f + 135f));
            int maxX = Clamp((int)((v.Center.X + r) / 64f + 135f));
            int minZ = Clamp((int)((v.Center.Z - r) / 64f + 135f));
            int maxZ = Clamp((int)((v.Center.Z + r) / 64f + 135f));

            var centre2d = v.Center.ToVec2();
            float r2 = r * r;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ushort id = bm.m_buildingGrid[z * 270 + x];
                    int guard = 0;

                    while (id != 0)
                    {
                        if ((buildings[id].m_flags & CollectMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            var pos = new Vec2(p.x, p.z);
                            if (centre2d.DistanceSquaredTo(pos) <= r2)
                            {
                                _candidates.Add(new IgnitionCandidate(
                                    id, pos, buildings[id].m_fireIntensity != 0));
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // Insurance against an infinite loop on saved data whose linked
                        // list is corrupt.
                        if (++guard > 32768) break;
                    }
                }
            }
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 269) return 269;
            return v;
        }
    }
}
