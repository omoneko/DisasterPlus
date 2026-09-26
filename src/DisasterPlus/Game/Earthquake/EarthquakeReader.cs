using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Reads the earthquakes in progress, the seismograph coverage and the sim thread's
    /// time of day.
    ///
    /// **Call it from the sim thread.** <c>DisasterManager</c>,
    /// <c>ImmaterialResourceManager</c> and <c>SimulationManager</c> are all owned by the
    /// simulation. Touch them directly from the main thread and an
    /// <c>IndexOutOfRangeException</c> popup with no stack trace turns up later, out of
    /// vanilla's own code (where this mod's try/catch cannot catch it). The shape of the
    /// sweep follows ①'s <c>WeatherReader.CountLocatedStorms</c> exactly.
    ///
    /// **How to read the time (the trap in §F-1).**
    /// <c>SimulationManager.m_currentDayTimeHour</c> is a field written by **the main
    /// thread** from <c>m_referenceFrameIndex</c> (the render-interpolation side). Read
    /// it from the sim thread and you both cross a thread boundary and read a different
    /// quantity to begin with. On the sim thread there is exactly one right answer:
    /// <c>m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR</c>. If <c>m_currentDayTimeHour</c> ever
    /// appears in this file, that is a defect.
    /// </summary>
    public static class EarthquakeReader
    {
        /// <summary>
        /// How many calls there have been since the last prefab scan failed.
        /// <see cref="Read"/> is called every sim tick, so retrying a failure every time
        /// would run a scan of every prefab every tick. The same throttle as
        /// <c>FireWhirlSpawner._missCallCount</c>.
        /// </summary>
        private static int _missCallCount;

        /// <summary>How many calls the failure cache holds for. Never set it to 0 (that means retrying every time).</summary>
        private const int MissRetryCalls = 64;

        private static EarthquakePrefabFacts _prefab;
        private static bool _prefabSearched;

        /// <summary>
        /// Whether an unexpected exception inside <see cref="Read"/> has already been
        /// shouted about with <c>Log.Error</c>.
        ///
        /// <c>Log.Warn</c> / <c>Log.Error</c> are not throttled. Read() is called every
        /// sim tick (roughly 50 times a second at normal speed), so a state where it
        /// throws consistently would write 50 lines a second into output_log.txt and
        /// render the log useless. Make the first one impossible to miss, then drop down
        /// to <c>Log.Diag</c>'s per-key throttle (once every 512 sim frames) — the shape
        /// established by <c>WeatherReader._readErrorLogged</c> /
        /// <c>HazardMapReader._sampleErrorLogged</c>.
        ///
        /// **Do not reset this on level unload.** "It throws" is a fact about the game
        /// build this DLL is referencing, not per-city state.
        /// </summary>
        private static bool _readErrorLogged;

        /// <summary>
        /// Call this on level load and unload. Never carry the prefab cache across cities.
        /// (It is unlikely that a second city would be opened in an environment with a
        ///  different DLC set-up, but "all session state resets on level unload" is this
        ///  mod's rule.)
        /// </summary>
        public static void Reset()
        {
            _prefab = new EarthquakePrefabFacts();
            _prefabSearched = false;
            _missCallCount = 0;
        }

        /// <summary>**Sim thread only.**</summary>
        public static EarthquakeSnapshot Read()
        {
            try
            {
                if (!Singleton<DisasterManager>.exists) return EarthquakeSnapshot.Invalid();
                if (!SimulationManager.exists) return EarthquakeSnapshot.Invalid();

                var prefab = ResolvePrefabFacts();
                var quakes = CollectQuakes(Singleton<DisasterManager>.instance, prefab);

                var sim = SimulationManager.instance;

                // ★ Never read m_currentDayTimeHour (§F-1). This is the sim thread's only
                //   correct answer.
                float hour = sim.m_dayTimeFrame * SimulationManager.DAYTIME_FRAME_TO_HOUR;

                // With the day/night cycle off, hour is pinned at 12.0 forever. Let it
                // through silently here and put the fact on the snapshot so the display
                // side can decide what to do (see EarthquakeSnapshot.DayNightEnabled's
                // doc).
                bool dayNight = sim.m_enableDayNight;

                // ★ Take the cursor exactly once and share it between the building sweep
                //   (only when there is an earthquake) and the coverage read (regardless
                //   of whether there is one). Call TakeCursor twice and, if the main
                //   thread republishes in between, you end up producing two values for
                //   two different positions.
                Vec3 cursor;
                bool haveCursor = EarthquakeHub.TakeCursor(out cursor);

                ushort cursorQuakeId;
                BuildingProbeOutcome cursorProbe;
                float cursorHeight;
                var cursorBuilding = ProbeCursorBuilding(quakes, haveCursor, cursor,
                                                         out cursorQuakeId, out cursorProbe,
                                                         out cursorHeight);

                // The coverage at the cursor is read **even when there is no earthquake
                // at all**. "Does a seismograph reach here" is a property of the city and
                // has nothing to do with whether a quake is happening (design doc §3.4).
                int cursorCoverage = 0;
                bool cursorCoverageValid = haveCursor
                    && TryReadCoverage(new Vector3(cursor.X, cursor.Y, cursor.Z),
                                       out cursorCoverage);

                // The waveforms are **what accumulated up to the previous tick**. This
                // tick's sampling is done by EarthquakeFeature below the pause guard, so
                // what can be read here is always the state as of one tick ago (the same
                // sort of designed-in lag as BuildingProbe's cursor tracking).
                //
                // ★ RecordingQuakeId is one tick stale in the same way. On the tick where
                //    a new earthquake is chosen, the WaveformQuakeId put here is **the
                //    previous quake's ID** (or 0), because Sample() has not run yet; it
                //    lines up again by itself on the next tick. Traces come from the same
                //    moment, so the ID and the contents never disagree (both are one tick
                //    old).
                var traces = SeismographRecorder.Snapshot();

                // ★ Layer 2's state (the tsunami chain) is read here on the sim thread
                //    too. TsunamiChain belongs to this same thread, so this is just a
                //    local read (it funnels the one route to the main thread through the
                //    snapshot). Read() runs before TsunamiChain.Tick(), so what lands
                //    here is up to one tick old (see EarthquakeSnapshot.TsunamiState's
                //    doc).
                return new EarthquakeSnapshot(quakes, prefab, sim.m_currentFrameIndex,
                                              hour, dayNight, cursorBuilding, cursorQuakeId,
                                              cursorProbe, cursorHeight,
                                              cursorCoverage, cursorCoverageValid,
                                              traces, SeismographRecorder.RecordingQuakeId,
                                              TsunamiChain.State, TsunamiChain.DueFrame,
                                              TsunamiChain.QuakeId,
                                              // ★ Layer 2's long-period cut-off flag goes the
                                              //    same way: funnelled through here so the main
                                              //    thread never reads LongPeriodDamage's static
                                              //    state directly (as with TsunamiChain).
                                              LongPeriodDamage.LastCapped, true);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("earthquake read failed", e);
                }
                else
                {
                    Log.Diag("EqRead", "earthquake read failed: " + e.GetType().Name);
                }
                return EarthquakeSnapshot.Invalid();
            }
        }

        /// <summary>
        /// Picks up every live earthquake. **Sim thread only.**
        ///
        /// <c>DisasterData.Info</c> just calls
        /// <c>PrefabCollection&lt;DisasterInfo&gt;.GetPrefab(m_infoIndex)</c> with no bounds
        /// check (measured in the IL: <c>get_Info</c> is 4 instructions). It can throw on
        /// a corrupt save or an invalid <c>m_infoIndex</c> left by another mod, so wrap
        /// **each element** in try/catch and stop one failure taking down the whole tally.
        /// </summary>
        private static IList<EarthquakeReading> CollectQuakes(DisasterManager d,
                                                             EarthquakePrefabFacts prefab)
        {
            var list = d.m_disasters;
            if (list == null) return EarthquakeSnapshot.EmptyQuakeList;

            var buffer = list.m_buffer;
            if (buffer == null) return EarthquakeSnapshot.EmptyQuakeList;

            // Don't take m_size entirely on trust. Vanilla only iterates up to m_size,
            // but we cap it by the array length as well (an IndexOutOfRange on the sim
            // thread becomes a popup with no stack trace).
            int size = list.m_size;
            if (size > buffer.Length) size = buffer.Length;

            // There are normally zero earthquakes, so don't allocate the list until one
            // is found.
            List<EarthquakeReading> found = null;

            for (int i = 0; i < size; i++)
            {
                int flags = (int)buffer[i].m_flags;
                if (!DisasterPhases.IsAlive(flags)) continue;

                DisasterAI ai;
                try
                {
                    var info = buffer[i].Info;
                    // UnityEngine.Object's == overload also rejects a destroyed
                    // (fake-null) object.
                    if (info == null) continue;
                    ai = info.m_disasterAI;
                }
                catch
                {
                    continue;
                }

                // ai's static type is DisasterAI, so this `is` is a downcast check and
                // will not be "always false" and trigger CS0184 (which this project
                // treats as an error).
                if (!(ai is EarthquakeAI)) continue;

                if (found == null) found = new List<EarthquakeReading>();
                found.Add(BuildReading((ushort)i, ref buffer[i], flags, prefab));
            }

            // Wrap it read-only before handing it over. The snapshot being immutable is a
            // precondition of the thread boundary, so guarantee it with the type rather
            // than with a promise not to modify it.
            return found == null ? EarthquakeSnapshot.EmptyQuakeList : found.AsReadOnly();
        }

        private static EarthquakeReading BuildReading(ushort id, ref DisasterData data, int flags,
                                                      EarthquakePrefabFacts prefab)
        {
            var pos = data.m_targetPosition;
            byte intensity = data.m_intensity;

            int coverage;
            bool coverageKnown = TryReadCoverage(pos, out coverage);

            // L / W from §A-3. If the prefab could not be resolved, put 0 in and let the
            // receiving side (Task 4's FaultBand) treat it as "unknown".
            float scale = 0.5f + intensity * 0.005f;
            float crackLength = prefab.Resolved ? prefab.CrackLength * scale : 0f;
            float crackWidth = prefab.Resolved ? prefab.CrackWidth * scale : 0f;

            return new EarthquakeReading(
                id,
                new Vec3(pos.x, pos.y, pos.z),
                data.m_angle,
                intensity,
                DisasterPhases.PhaseOf(flags),
                DisasterPhases.IsLocated(flags),
                data.m_startFrame,
                data.m_activationFrame,
                // ★ 0 means "not yet decided", not "now". An earthquake without
                //    SelfTrigger(64) set stays at 0 here and sits in Emerging forever
                //    (§A-1).
                data.m_activationFrame != 0u,
                coverage,
                coverageKnown,
                crackLength,
                crackWidth);
        }

        /// <summary>
        /// The headroom of the building under the cursor. **Sim thread only** (it touches
        /// the building buffers).
        ///
        /// It considers **only the one earthquake whose destruction pass is about to run
        /// or is running now**; it does not sweep for every earthquake (that would make
        /// the per-sim-tick cost proportional to the number of quakes).
        ///
        /// Excluding Clearing / Finished is **for accuracy, not performance**. The
        /// whole-quake disc's <c>DestroyBuildings</c> exists **only in the Active
        /// branch** of <c>EarthquakeAI.SimulationStep</c> (§A-3). Saying "it will
        /// collapse" about an earthquake that has already subsided is saying that
        /// something which can no longer happen will happen.
        ///
        /// The choice is left to <see cref="QuakeSelection.SelectDamaging"/> (so the
        /// ranking is not copied into several places; the background is in its class
        /// doc). Which earthquake was chosen is stated by
        /// <see cref="EarthquakeSnapshot.CursorQuakeId"/>.
        /// </summary>
        private static BuildingMargin ProbeCursorBuilding(IList<EarthquakeReading> quakes,
                                                          bool haveCursor, Vec3 cursor,
                                                          out ushort cursorQuakeId,
                                                          out BuildingProbeOutcome outcome,
                                                          out float heightMetres)
        {
            cursorQuakeId = 0;
            outcome = BuildingProbeOutcome.NotProbed;
            heightMetres = 0f;
            if (quakes.Count == 0) return BuildingMargin.None();

            // If the main thread has not said "the cursor is here right now", look at
            // nothing (panel closed, mouse over the UI, or off the terrain).
            if (!haveCursor) return BuildingMargin.None();

            var target = QuakeSelection.SelectDamaging(quakes);
            if (target == null) return BuildingMargin.None();

            // ★ Set this here, before any building is found. CursorQuakeId != 0 is the
            //    mark of "we actually looked at this position for this earthquake", not
            //    of "there was a building". Without that distinction, when the only
            //    quakes are subsiding ones (whose destruction pass no longer runs) the
            //    display puts out the wrong explanation — "there is no building under the
            //    cursor" — while the cursor sits on one.
            cursorQuakeId = target.DisasterId;

            var band = new FaultBand(target.Epicentre.ToVec2(), target.AngleRadians,
                                     target.CrackLength, target.CrackWidth);

            // ★ If the destruction code has been replaced by another mod, the headroom
            //    gives no verdict (§E-2; BuildingMargin.Evaluate's damageModelReplaced).
            //    ModCompat.NdrPresent is evaluated once at startup and cached, so even
            //    though this runs every tick, PluginManager is not walked again.
            return BuildingProbe.ProbeAt(cursor, target, band, ModCompat.NdrPresent,
                                         out outcome, out heightMetres);
        }

        /// <summary>
        /// The seismograph coverage at a given position. **Not clamped** (it carries the
        /// raw value; the <c>Min(cov, 100)</c> is done on the display side by
        /// <see cref="WarningLeadTime"/>).
        ///
        /// There are two call sites, and they **mean completely different things**:
        ///   - the epicentre (<c>m_targetPosition</c>) … the single point vanilla really
        ///     uses for the warning lead time and the <c>located</c> decision (§A-2).
        ///   - the cursor position … a value that exists only so the player can check
        ///     whether a seismograph reaches this spot.
        ///     **Never derive the lead time from this.**
        ///
        /// Never return 0 and true when the read failed. A coverage of 0 is a
        /// **meaningful measurement** — "there is no seismograph, so an empty hazard map
        /// is correct" — and it would end up as the same value as a failed read.
        /// </summary>
        private static bool TryReadCoverage(Vector3 position, out int coverage)
        {
            coverage = 0;
            try
            {
                if (!Singleton<ImmaterialResourceManager>.exists) return false;
                Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(
                    ImmaterialResourceManager.Resource.EarthquakeCoverage, position, out coverage);
                return true;
            }
            catch
            {
                coverage = 0;
                return false;
            }
        }

        /// <summary>
        /// Returns the four prefab values through the cache. **Sim thread only** (it
        /// writes <c>_prefabSearched</c> / <c>_missCallCount</c>).
        /// </summary>
        private static EarthquakePrefabFacts ResolvePrefabFacts()
        {
            if (_prefabSearched && _prefab.Resolved) return _prefab;

            // If the previous scan failed, it may simply be that we are just after a
            // level load and the prefabs are not all in place yet. So do not make it
            // "never look again", but do not walk every prefab every tick either.
            // Throttle by call count.
            if (_prefabSearched)
            {
                _missCallCount++;
                if (_missCallCount < MissRetryCalls) return _prefab;
            }
            _missCallCount = 0;

            _prefabSearched = true;
            _prefab = ScanPrefabFacts();

            if (!_prefab.Resolved)
            {
                // Warn is not throttled, so drop down to Diag. Without the DLC this is
                // the permanent, correct state of affairs.
                Log.Diag("EqPrefab",
                    "no EarthquakeAI DisasterInfo found; Natural Disasters DLC required for earthquakes");
            }
            return _prefab;
        }

        /// <summary>
        /// A pure function that only scans the prefabs. It touches neither the cache nor
        /// the miss count, so **calling it from any thread cannot corrupt this class's
        /// state**. <c>Assumptions.Run()</c> (main thread) must use this one — calling
        /// <see cref="ResolvePrefabFacts"/> would mean the main thread rewinding a cache
        /// the sim thread is driving (we fixed exactly this defect once before, in
        /// <c>FireWhirlSpawner.HasTornadoPrefab</c>).
        ///
        /// <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> is a public static generic
        /// that walks <c>PrefabCollection&lt;DisasterInfo&gt;</c> and returns the first
        /// prefab whose <c>m_disasterAI is T</c>, and nothing more (IL facts doc §B-5).
        /// There is no DLC check inside it; the authority is that
        /// **without the DLC the prefab simply does not exist and null comes back**.
        /// </summary>
        public static EarthquakePrefabFacts ScanPrefabFacts()
        {
            try
            {
                var info = DisasterManager.FindDisasterInfo<EarthquakeAI>();
                if (info == null) return new EarthquakePrefabFacts();

                var ai = info.m_disasterAI as EarthquakeAI;
                if (ai == null) return new EarthquakePrefabFacts();

                return new EarthquakePrefabFacts(
                    ai.m_crackLength, ai.m_crackWidth, ai.m_emergingDuration, ai.m_activeDuration);
            }
            catch
            {
                return new EarthquakePrefabFacts();
            }
        }
    }
}
