using System;
using System.Reflection;
using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The only place ⑤ reads. It resolves the terrain API's reach paths and publishes them as a
    /// <see cref="VolcanoSnapshot"/>.
    ///
    /// **Call <see cref="Read"/> from the sim thread.**
    /// <c>TerrainManager</c> / <c>SimulationManager</c> / <c>ToolManager</c> are all owned by the
    /// simulation. Touch the write-side of them from the main thread and an
    /// <c>IndexOutOfRangeException</c> popup with no stack trace comes out of vanilla later
    /// (this mod's try/catch cannot catch it).
    /// The shape follows ②'s <c>EarthquakeReader</c> and ④'s <see cref="TyphoonReader"/> exactly.
    ///
    /// **Reads alone are safe from either thread** (the existing doc of
    /// <c>TerrainHeightSampler</c> says so). That is why <see cref="ScanTerrainFacts"/> can also
    /// be called from the main thread's <see cref="Assumptions"/>.
    ///
    /// **Always check <c>Singleton&lt;T&gt;.exists</c> first.** <c>Singleton&lt;T&gt;.instance</c>
    /// is a **main-thread-only API** that runs <c>FindObjectOfType</c> and <c>new GameObject</c>
    /// when <c>sInstance</c> is null, and stepping on it from the sim thread crashes
    /// (the same note in <c>LongPeriodDamage.Sweep</c> / <c>TyphoonWind.Sweep</c>).
    ///
    /// > **⑤ does not touch the disaster-related types at all.** Design doc §2 decided not to
    /// > occupy a disaster slot, and the facts doc §D-11 settled the reason (without ND there is
    /// > not one vanilla prefab, and the detection path NREs because an internal wrapper is null)
    /// > (trap 6). The guarantee is a grep, so **do not write those API names in the docs
    /// > either** — when the names are needed, point at §D-11 (the class doc of
    /// > <see cref="VolcanoFeature"/> decided that, and this file broke it once.
    /// > Whole-project review M15).
    /// </summary>
    public static class VolcanoReader
    {
        /// <summary>
        /// Whether an unexpected exception inside <see cref="Read"/> has been sounded with
        /// <c>Log.Error</c>.
        ///
        /// <c>Log.Warn</c> / <c>Log.Error</c> are not throttled. <see cref="Read"/> is called on
        /// every sim tick (about 50 times a second at normal speed), so if it gets into a state
        /// where it throws permanently it would write 50 lines a second into output_log.txt and
        /// make the log useless.
        /// Make the first one impossible to miss, then drop to <c>Log.Diag</c>'s per-key
        /// throttling (once per 512 sim frames).
        ///
        /// **Not reset on level unload.** "It throws" is a fact about the build of the game this
        /// DLL references, not per-city state.
        /// </summary>
        private static bool _readErrorLogged;

        private static VolcanoTerrainFacts _facts;
        private static bool _factsScanned;

        /// <summary>
        /// Call on level load and unload. Do not carry the terrain cache across cities
        /// ("all session state is reset on level unload" is this mod's rule).
        /// The <c>RawHeights</c> array is rebuilt per city, so **carrying the measured length over
        /// would make the second city quote the previous city's facts.**
        /// </summary>
        public static void Reset()
        {
            _facts = new VolcanoTerrainFacts();
            _factsScanned = false;
        }

        /// <summary>**Sim thread only.**</summary>
        public static VolcanoSnapshot Read()
        {
            try
            {
                if (!SimulationManager.exists) return VolcanoSnapshot.Invalid();

                uint frame = SimulationManager.instance.m_currentFrameIndex;

                // ★ The phase and the survey result are read directly from the sim thread's
                //   VolcanoState. This Read runs above the pause guard, so what gets carried is
                //   **the state before VolcanoState.Tick runs on this tick**
                //   (the doc of VolcanoSnapshot.Phase).
                // The clearing's tally is likewise read straight from sim-thread statics (same
                // thread).
                return new VolcanoSnapshot(true, ResolveTerrainFacts(), frame, ReadGameMode(),
                                           VolcanoState.Phase, VolcanoState.Footprint,
                                           VolcanoState.ProgressUnit, VolcanoState.LastRefusal,
                                           VolcanoClearing.ClearedRadiusMetres,
                                           VolcanoClearing.Complete,
                                           VolcanoClearing.TotalBuildingsDestroyed,
                                           VolcanoClearing.TotalSegmentsDestroyed,
                                           VolcanoClearing.LastBuildingsRefused,
                                           VolcanoClearing.LastCapped,
                                           VolcanoClearing.ClearingPathAvailable,
                                           VolcanoUplift.SummitMetres,
                                           VolcanoUplift.ActiveRadiusMetres,
                                           VolcanoUplift.Complete,
                                           VolcanoUplift.CraterFormed,
                                           VolcanoUplift.TileCount,
                                           VolcanoUplift.TileCursor,
                                           VolcanoEruption.Active,
                                           VolcanoEruption.IntensityUnit,
                                           VolcanoEruption.VentWorld,
                                           VolcanoEruption.InClimax,
                                           // ★ The ring of fissures is the caldera's edge itself.
                                           //   0 for an eruption that is not foundering (= there
                                           //   is no ring).
                                           VolcanoState.RingFissureRadiusMetres,
                                           VolcanoLava.FlowCount,
                                           VolcanoLava.AliveCount,
                                           VolcanoLava.LongestMetres,
                                           VolcanoLava.BuildingsIgnited,
                                           VolcanoLava.TreesIgnited,
                                           VolcanoLava.TreesAvailable,
                                           // ★ These arrays cannot be rewritten after publishing
                                           //   (VolcanoLava swaps them out whole on every
                                           //   advance). That is why no copy is taken, and why it
                                           //   is safe for the main thread to hold the reference.
                                           VolcanoLava.TrailPoints,
                                           VolcanoLava.TrailPointCounts,
                                           VolcanoLava.CoolUnit);
            }
            catch (Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("volcano read failed", e);
                }
                else
                {
                    Log.Diag("VolcRead", "volcano read failed: " + e.GetType().Name);
                }
                return VolcanoSnapshot.Invalid();
            }
        }

        /// <summary>
        /// Whether this is game mode (false in the map editor).
        ///
        /// The catch-up speed of <c>m_blockHeights</c> changes (2 m upwards in the game, 8 m in
        /// the editor, §A-2).
        /// **If it cannot be read, return true (game mode)** — reporting the slower one is the
        /// safe direction, so we never say "you can build here now" prematurely.
        ///
        /// ★ **One correction to what plan §2.3 says.** The plan writes
        /// <c>ToolController.m_mode</c>, but <c>m_mode</c> is a **public instance field** on
        /// <c>ToolController</c> (of type <c>ItemClass.Availability</c>), not a static.
        /// The reach path is <c>ToolManager.instance.m_properties.m_mode</c>
        /// (confirmed by reflection on this mod's side).
        /// </summary>
        private static bool ReadGameMode()
        {
            try
            {
                if (!Singleton<ToolManager>.exists) return true;

                var properties = Singleton<ToolManager>.instance.m_properties;
                if (properties == null) return true;

                return (properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Returns the terrain facts through the cache. **Sim thread only** (it writes
        /// <c>_factsScanned</c>).
        ///
        /// Unlike ④'s <c>ResolvePrefabFacts</c>, it **does not re-scan on a throttle.**
        /// That one is looking at prefabs, which were sometimes not yet in place immediately after
        /// a level load. This one is looking at the array <c>TerrainManager.Awake</c> allocated
        /// and at the assembly's method table, and **both are settled the moment the level is
        /// loaded**. Not scanning every tick is purely about cost.
        /// </summary>
        private static VolcanoTerrainFacts ResolveTerrainFacts()
        {
            if (_factsScanned) return _facts;

            _factsScanned = true;
            _facts = ScanTerrainFacts();

            if (!_facts.Usable)
            {
                // Warn is not throttled, so drop to Diag.
                Log.Diag("VolcTerrain",
                    "the terrain write path is not usable: RawHeights=" + _facts.RawArrayLength
                    + " updateArea=" + (_facts.UpdateAreaResolved ? "ok" : "MISSING")
                    + "; no volcano will be built");
            }
            return _facts;
        }

        /// <summary>
        /// A pure function that only probes the terrain API. It touches no cache, so
        /// **calling it from any thread cannot corrupt this class's state**.
        /// <see cref="Assumptions"/> (main thread) should use this one —
        /// call <see cref="ResolveTerrainFacts"/> and the main thread would be rewinding a cache
        /// the sim thread is driving
        /// (the same defect was fixed once in <c>FireWhirlSpawner.HasTornadoPrefab</c>).
        ///
        /// **Wrap each item in its own try.** Give up on one because the other failed, and an
        /// environment where only the lava cannot flow stops the mountain too.
        ///
        /// Methods are looked up with <c>GetMethod</c> **specifying the parameter types too**. A
        /// name-only <c>GetMethod</c> throws <c>AmbiguousMatchException</c> on overloads and also
        /// misses signature changes (the form ② established).
        /// That goes double for <c>SampleDetailHeight</c>, which has four overloads of the same
        /// name.
        /// </summary>
        public static VolcanoTerrainFacts ScanTerrainFacts()
        {
            bool heightsResolved = false;
            int rawLength = 0;

            try
            {
                // ★ Check exists first (class doc).
                if (Singleton<TerrainManager>.exists)
                {
                    // It is a read, so it is safe from either thread.
                    // RawHeights is a public property of type ushort[]
                    // (verified at compile time. §C-8 / §B-6).
                    ushort[] raw = Singleton<TerrainManager>.instance.RawHeights;
                    if (raw != null)
                    {
                        heightsResolved = true;
                        rawLength = raw.Length;
                    }
                }
            }
            catch
            {
                heightsResolved = false;
                rawLength = 0;
            }

            bool updateAreaResolved = HasMethod(typeof(TerrainModify), "UpdateArea", true,
                new Type[]
                {
                    typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(bool), typeof(bool), typeof(bool)
                });

            bool burnGroundResolved = HasMethod(typeof(DisasterHelpers), "BurnGround", true,
                new Type[]
                {
                    typeof(UnityEngine.Vector2), typeof(float), typeof(float)
                });

            // ★ The three-argument version (out float slopeX, out float slopeZ). The
            //   one-argument version gives no gradient, so the lava cannot find downhill (§B-6).
            //   out is specified with MakeByRefType.
            bool slopeSampleResolved = HasMethod(typeof(TerrainManager), "SampleDetailHeight", false,
                new Type[]
                {
                    typeof(UnityEngine.Vector3),
                    typeof(float).MakeByRefType(), typeof(float).MakeByRefType()
                });

            bool dlc;
            try
            {
                dlc = ModCompat.NaturalDisastersOwned;
            }
            catch
            {
                dlc = false;
            }

            return new VolcanoTerrainFacts(heightsResolved, rawLength, updateAreaResolved,
                                           burnGroundResolved,
                                           slopeSampleResolved, dlc);
        }

        /// <summary>
        /// A <c>GetMethod</c> that specifies the parameter types too. false when it is not found
        /// or something throws.
        /// **Do not turn "could not resolve" into "an exception"** — <see cref="ScanTerrainFacts"/>
        /// can be called from outside <see cref="Assumptions"/>'s <c>Check</c> as well.
        /// </summary>
        private static bool HasMethod(Type declaringType, string name, bool isStatic,
                                      Type[] parameterTypes)
        {
            try
            {
                var flags = BindingFlags.Public
                            | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
                return declaringType.GetMethod(name, flags, null, parameterTypes, null) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
