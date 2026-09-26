using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The only place ④ reads from. It gathers the six prefab values
    /// (<see cref="TyphoonPrefabFacts"/>) and <c>WeatherManager</c>'s measured values
    /// into a <see cref="TyphoonSnapshot"/>.
    ///
    /// **Call it from the sim thread.** <c>DisasterManager</c> / <c>WeatherManager</c> /
    /// <c>SimulationManager</c> are all owned by the simulation. Touch them directly from
    /// the main thread and vanilla throws an <c>IndexOutOfRangeException</c> popup with
    /// no stack trace, some time later (this mod's try/catch cannot catch it).
    /// The shape is copied straight from ②'s <see cref="EarthquakeReader"/>.
    ///
    /// **Always look at <c>Singleton&lt;T&gt;.exists</c> first.**
    /// <c>Singleton&lt;T&gt;.instance</c> runs <c>FindObjectOfType</c> and
    /// <c>new GameObject</c> when <c>sInstance</c> is null, which makes it a **main
    /// thread only API**; step on it from the sim thread and you crash (the same note is
    /// on <c>LongPeriodDamage.Sweep</c> / <c>TsunamiChain</c>).
    ///
    /// ★ **One correction to what the design doc says.**
    /// The design doc §6 and its appendix say "<c>VortexAI</c>'s <c>m_maxSpeed</c>", but
    /// there is **no** field called <c>m_maxSpeed</c> on <c>VortexAI</c>. What
    /// <c>IL_00AF maxSpeed = m_info.m_maxSpeed</c> in §B-1 of the IL facts document reads
    /// is <c>VehicleAI.m_info</c>, i.e. **<c>VehicleInfo.m_maxSpeed</c>**.
    /// Re-confirmed on this mod's side by reflection (<c>VortexAI</c>'s declared fields
    /// are only the five <c>m_destructionRadiusMin</c> / <c>m_destructionRadiusMax</c> /
    /// <c>m_upgradeRadiusMin</c> / <c>m_upgradeRadiusMax</c> / <c>m_debrisCount</c>).
    /// The route to it is <c>TornadoAI.m_vortexInfo</c> (a <c>VehicleInfo</c>)
    /// <c>.m_maxSpeed</c>.
    /// **Do not go looking for <c>m_maxSpeed</c> on <c>VortexAI</c>** — you will not find
    /// it, and you will end up guessing at some other field.
    /// </summary>
    public static class TyphoonReader
    {
        /// <summary>
        /// How many calls there have been since the last prefab scan failed.
        /// <see cref="Read"/> is called on every sim tick, so retrying the failure every
        /// time would sweep all prefabs every tick. The same thinning as
        /// <c>EarthquakeReader._missCallCount</c>.
        /// </summary>
        private static int _missCallCount;

        /// <summary>How many calls the failure cache holds for. Never make it 0 (that
        /// means retrying every time).</summary>
        private const int MissRetryCalls = 64;

        private static TyphoonPrefabFacts _prefab;
        private static bool _prefabSearched;

        /// <summary>
        /// Whether an unexpected exception inside <see cref="Read"/> has already been
        /// sounded through <c>Log.Error</c>.
        ///
        /// <c>Log.Warn</c> / <c>Log.Error</c> are not throttled. <see cref="Read"/> is
        /// called once per sim tick (roughly 50 times a second at normal speed), so a
        /// state that throws permanently would write 50 lines a second into
        /// output_log.txt and make the log useless. Make the first one stand out
        /// reliably, then drop to <c>Log.Diag</c>'s per-key throttle (once per 512 sim
        /// frames).
        ///
        /// **Not reset on level unload.** "It throws" is a fact about the game build this
        /// DLL is referencing, not per-city state.
        /// </summary>
        private static bool _readErrorLogged;

        /// <summary>
        /// Call on level load/unload. Do not carry the prefab cache across cities
        /// ("all per-session state is reset on level unload" is this mod's rule).
        /// </summary>
        public static void Reset()
        {
            _prefab = new TyphoonPrefabFacts();
            _prefabSearched = false;
            _missCallCount = 0;
        }

        /// <summary>**Sim thread only.**</summary>
        public static TyphoonSnapshot Read()
        {
            try
            {
                if (!SimulationManager.exists) return TyphoonSnapshot.Invalid();

                var prefab = ResolvePrefabFacts();
                uint frame = SimulationManager.instance.m_currentFrameIndex;

                float rain = 0f, cloud = 0f, fog = 0f, windDirection = 0f;
                bool weatherEnabled = false;
                bool weatherReadable = ReadWeather(out rain, out cloud, out fog,
                                                   out windDirection, out weatherEnabled);

                // ★ TyphoonController is sim-thread static state, and we read it from the
                //    same thread as this Read() (TyphoonFeature.OnSimulationTick).
                //    What goes in is the state from **before TyphoonController.Tick
                //    runs**, i.e. the previous tick's (see the note in TyphoonSnapshot's
                //    T3 section).
                return new TyphoonSnapshot(true, prefab, frame,
                                           rain, cloud, fog, windDirection,
                                           weatherEnabled, weatherReadable,
                                           TyphoonController.Active,
                                           TyphoonController.DisasterId,
                                           TyphoonController.Centre,
                                           TyphoonController.HeadingRadians,
                                           TyphoonController.Intensity,
                                           TyphoonController.StormRadius,
                                           TyphoonController.GaleRadius,
                                           TyphoonController.TrackPlan,
                                           TyphoonController.Phase,
                                           TyphoonController.ElapsedFrames,
                                           TyphoonController.TotalFrames,
                                           TyphoonController.OverLand,
                                           TyphoonController.LandfallKnown,
                                           TyphoonController.MinutesToLandfall,
                                           TyphoonController.LastRefusal,
                                           TyphoonWeather.Driving,
                                           TyphoonWeather.LastRain,
                                           TyphoonWeather.LastCloud,
                                           TyphoonWeather.LastDirectionDegrees,
                                           TyphoonLightning.InFlight,
                                           TyphoonLightning.TotalQueued,
                                           TyphoonLightning.TotalRejected,
                                           TyphoonLightning.LastVanillaReserve,
                                           TyphoonWind.Passes,
                                           TyphoonWind.LastCollapsed,
                                           TyphoonWind.TotalCollapsed,
                                           TyphoonWind.LastScanned,
                                           TyphoonWind.LastRefused,
                                           TyphoonWind.LastCapped,
                                           TyphoonWind.LastUnknownHeight,
                                           TyphoonFlood.State,
                                           TyphoonFlood.NaturalSourceCount,
                                           TyphoonFlood.TouchedCount,
                                           TyphoonFlood.LastPeakRiseMetres,
                                           TyphoonGust.LastActive,
                                           TyphoonGust.LastCollapsed,
                                           TyphoonGust.TotalCollapsed,
                                           TyphoonGust.LastRefused);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("typhoon read failed", e);
                }
                else
                {
                    Log.Diag("TyRead", "typhoon read failed: " + e.GetType().Name);
                }
                return TyphoonSnapshot.Invalid();
            }
        }

        /// <summary>
        /// The measured weather values. **The only provenance in ④ allowed to claim
        /// <c>[measured]</c>** (design doc §7-1).
        ///
        /// If it cannot be read it returns false and the caller shows no numbers.
        /// **Do not mix up 0 with "could not read"** (a discipline ① and ② established
        /// over and over).
        /// </summary>
        private static bool ReadWeather(out float rain, out float cloud, out float fog,
                                        out float windDirection, out bool weatherEnabled)
        {
            rain = 0f;
            cloud = 0f;
            fog = 0f;
            windDirection = 0f;
            weatherEnabled = false;

            try
            {
                // ★ Look at exists first (class doc).
                if (!Singleton<WeatherManager>.exists) return false;

                var w = Singleton<WeatherManager>.instance;
                rain = w.m_currentRain;
                cloud = w.m_currentCloud;
                fog = w.m_currentFog;
                windDirection = w.m_windDirection;
                weatherEnabled = w.m_enableWeather;
                return true;
            }
            catch
            {
                rain = 0f;
                cloud = 0f;
                fog = 0f;
                windDirection = 0f;
                weatherEnabled = false;
                return false;
            }
        }

        /// <summary>
        /// Returns the six prefab values through the cache. **Sim thread only**
        /// (it writes <c>_prefabSearched</c> / <c>_missCallCount</c>).
        ///
        /// Until it resolves it rescans, thinned out. In an environment without the DLC
        /// it never resolves, so there it settles at one scan per 64 calls.
        /// </summary>
        private static TyphoonPrefabFacts ResolvePrefabFacts()
        {
            if (_prefabSearched && _prefab.StormResolved) return _prefab;

            // If the previous scan failed, it may simply be that we are right after a
            // level load and the prefabs are not all in place yet. So do not make it
            // "never look again" — but do not lick every prefab every tick either. Thin
            // it out by call count.
            if (_prefabSearched)
            {
                _missCallCount++;
                if (_missCallCount < MissRetryCalls) return _prefab;
            }
            _missCallCount = 0;

            _prefabSearched = true;
            _prefab = ScanPrefabFacts();

            if (!_prefab.StormResolved)
            {
                // Warn is not throttled, so drop to Diag. In an environment without the
                // DLC this is the permanent, correct state.
                Log.Diag("TyPrefab",
                    "no ThunderStormAI DisasterInfo found; Natural Disasters DLC required for typhoons");
            }
            return _prefab;
        }

        /// <summary>
        /// A pure function that only scans the prefabs. It touches neither the cache nor
        /// the miss count, so **calling it from any thread cannot damage this class's
        /// state**. <see cref="Assumptions"/> (main thread) must use this one — calling
        /// <see cref="ResolvePrefabFacts"/> would rewind, from the main thread, a cache
        /// that the sim thread is driving (we have fixed exactly this defect before, in
        /// <c>FireWhirlSpawner.HasTornadoPrefab</c>).
        ///
        /// <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> is a public static generic
        /// that simply sweeps <c>PrefabCollection&lt;DisasterInfo&gt;</c> and returns the
        /// first prefab where <c>m_disasterAI is T</c>. There is no DLC check inside it;
        /// the authority is that **without the DLC the prefab does not exist at all and
        /// null comes back**.
        ///
        /// </summary>
        public static TyphoonPrefabFacts ScanPrefabFacts()
        {
            bool stormResolved = false;
            float stormRadius = 0f;
            uint emerging = 0u;
            uint active = 0u;

            try
            {
                var info = DisasterManager.FindDisasterInfo<ThunderStormAI>();
                var ai = info == null ? null : info.m_disasterAI as ThunderStormAI;
                if (ai != null)
                {
                    stormResolved = true;
                    stormRadius = ai.m_radius;
                    emerging = ai.m_emergingDuration;
                    active = ai.m_activeDuration;
                }
            }
            catch
            {
                stormResolved = false;
                stormRadius = 0f;
                emerging = 0u;
                active = 0u;
            }

            // ★ **We no longer read the tornado prefab.** The accompanying tornado was
            //   retired and tornado-grade damage is now produced by TyphoonGust itself,
            //   so there is not one place left that uses VortexAI's destruction radii or
            //   VehicleInfo.m_maxSpeed. Keep listing a value in the diagnostics just
            //   because it can be read, and the next person will read it as "this is
            //   having an effect".

            return new TyphoonPrefabFacts(stormResolved, stormRadius, emerging, active);
        }
    }
}
