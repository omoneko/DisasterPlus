using ColossalFramework;
using DisasterPlus.Core.Forecast;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Reads the current weather and the disaster probability from WeatherManager and
    /// DisasterManager.
    ///
    /// **Call this from the sim thread.** These managers are owned by the simulation.
    /// Touch them directly from the main thread and vanilla throws an
    /// IndexOutOfRangeException popup with no stack trace at some later point (which this
    /// mod's try/catch cannot catch).
    ///
    /// Reading both current and target is the heart of this feature. Vanilla only ever
    /// shows you the present value; it never shows which way it is heading.
    ///
    /// We also count the "located, under-way storms" here. For the reason see the doc on
    /// <see cref="WeatherSnapshot.LocatedLightningStorms"/> (vanilla's hazard map is not a
    /// static risk surface but the predicted damage area of storms the radar has located
    /// and that are under way, and with none of those every cell is 0).
    /// </summary>
    public static class WeatherReader
    {
        /// <summary>
        /// The mask for deciding "Created and not Deleted".
        ///
        /// From the IL: DisasterManager.UpdateTexture makes the same test itself when it
        /// sweeps m_disasters, with
        /// <c>ldfld m_flags; ldc.i4.3; and; ldc.i4.1; bne.un</c>
        /// (i.e. <c>(m_flags &amp; 3) == 1</c>).
        /// The flags are Created=1, Deleted=2 (measured by reflection).
        /// Copying vanilla's sweep condition exactly guarantees that the population we
        /// count is the same population UpdateTexture actually draws.
        /// </summary>
        private const int CreatedNotDeletedMask = (int)DisasterData.Flags.Created
                                                | (int)DisasterData.Flags.Deleted;

        /// <summary>
        /// UpdateHazardMap's first gate. 4096 = DisasterData.Flags.Located.
        /// Unless a radar or the like has "located" it, nothing is drawn on the hazard
        /// map at all.
        /// </summary>
        private const int LocatedMask = (int)DisasterData.Flags.Located;

        /// <summary>
        /// UpdateHazardMap's second gate. 12 = Emerging(4) | Active(8).
        /// If it is not emerging or under way, again nothing is drawn.
        /// </summary>
        private const int InProgressMask = (int)DisasterData.Flags.Emerging
                                         | (int)DisasterData.Flags.Active;

        /// <summary>
        /// Whether an unexpected exception inside Read() has already been sounded with
        /// Log.Error.
        ///
        /// Read() is called every sim tick (around 50 times a second at normal speed). If
        /// it reaches a state where it throws permanently (a game update removing a field,
        /// say), an unconditional Log.Error keeps writing 50 lines a second into
        /// output_log.txt and makes the log useless. Make the first one impossible to
        /// miss, then drop to Log.Diag's per-key throttle (once per 512 sim frames).
        /// The same trick as its sibling HazardMapReader._sampleErrorLogged (the fix made
        /// there during the Task 4 review, carried over to this class, which had the same
        /// defect).
        ///
        /// Not reset on level unload, for the same reason. "It throws" is a fact about the
        /// build of the game this DLL is running against, not per-city state.
        /// </summary>
        private static bool _readErrorLogged;

        public static WeatherSnapshot Read()
        {
            try
            {
                if (!Singleton<WeatherManager>.exists) return WeatherSnapshot.Invalid();
                var w = Singleton<WeatherManager>.instance;

                float band = TrendMath.DefaultDeadband;

                // The temperature has a much larger scale, so its deadband is wider (at
                // 0.02 degrees it would count as changing all the time). For where the
                // number comes from, and why it lives in Core, see
                // TrendMath.TemperatureDeadband.
                var temperature = new ForecastReading(
                    w.m_currentTemperature, w.m_targetTemperature, TrendMath.TemperatureDeadband);
                var rain = new ForecastReading(w.m_currentRain, w.m_targetRain, band);
                var cloud = new ForecastReading(w.m_currentCloud, w.m_targetCloud, band);
                var fog = new ForecastReading(w.m_currentFog, w.m_targetFog, band);

                // Return a valid weather snapshot even when DisasterManager is absent.
                // Dropping the whole thing to "cannot read" merely because the disaster
                // probability is unreadable would be excessive. But leaving
                // probability=0f while setting Valid=true gives the caller a fabricated
                // zero it cannot tell apart from "it really was 0%" (review finding).
                // disasterInfoAvailable makes the distinction explicit.
                float probability = 0f;
                int cooldown = 0;
                int lightningStorms = 0;
                int tornadoes = 0;
                bool disasterInfoAvailable = false;
                if (Singleton<DisasterManager>.exists)
                {
                    var d = Singleton<DisasterManager>.instance;
                    probability = d.m_randomDisastersProbability;
                    cooldown = d.m_randomDisasterCooldown;
                    CountLocatedStorms(d, out lightningStorms, out tornadoes);
                    disasterInfoAvailable = true;
                }

                return new WeatherSnapshot(
                    temperature, rain, cloud, fog, w.m_windDirection,
                    probability, cooldown, lightningStorms, tornadoes,
                    disasterInfoAvailable, true);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("weather read failed", e);
                }
                else
                {
                    Log.Diag("WeatherRead", "weather read failed: " + e.GetType().Name);
                }
                return WeatherSnapshot.Invalid();
            }
        }

        /// <summary>
        /// Counts the thunderstorms and tornadoes that meet the conditions for actually
        /// being drawn on the hazard map.
        /// **Sim thread only** (m_disasters is a buffer owned by the simulation).
        ///
        /// The shape of the sweep is copied straight from the IL of
        /// DisasterManager.UpdateTexture: run <c>m_disasters.m_buffer</c> up to
        /// <c>m_disasters.m_size</c> and look only at live elements, via
        /// <c>(m_flags &amp; 3) == 1</c>. Adding UpdateHazardMap's own two gates (Located /
        /// Emerging|Active) to that gives the exact definition of "a disaster that draws
        /// anything on the hazard map right now".
        ///
        /// DisasterData.Info just calls
        /// <c>PrefabCollection&lt;DisasterInfo&gt;.GetPrefab(m_infoIndex)</c> with no
        /// bounds check (from the IL: get_Info is 4 instructions). A corrupt save or a bad
        /// m_infoIndex from another mod can make it throw, so each element is wrapped in
        /// its own try/catch to stop one failure taking down the whole count.
        /// </summary>
        private static void CountLocatedStorms(DisasterManager d, out int lightning, out int tornado)
        {
            lightning = 0;
            tornado = 0;

            var list = d.m_disasters;
            if (list == null) return;

            var buffer = list.m_buffer;
            if (buffer == null) return;

            // Do not put full trust in m_size. Vanilla only runs as far as m_size, but we
            // cap against the array length as well (an IndexOutOfRange raised here would
            // come out as a popup with no stack trace, because this is the sim thread).
            int size = list.m_size;
            if (size > buffer.Length) size = buffer.Length;

            for (int i = 0; i < size; i++)
            {
                int flags = (int)buffer[i].m_flags;

                if ((flags & CreatedNotDeletedMask) != (int)DisasterData.Flags.Created) continue;
                if ((flags & LocatedMask) == 0) continue;
                if ((flags & InProgressMask) == 0) continue;

                DisasterAI ai;
                try
                {
                    var info = buffer[i].Info;
                    // UnityEngine.Object's == overload also rejects destroyed objects
                    // (fake-null). UpdateTexture uses Object::op_Inequality at the same
                    // point.
                    if (info == null) continue;
                    ai = info.m_disasterAI;
                }
                catch
                {
                    // Do not throw away the whole count over one bad m_infoIndex.
                    continue;
                }

                if (ai == null) continue;

                // ThunderStormAI and TornadoAI both derive from
                // WeatherDisasterAI -> DisasterAI (measured by reflection).
                // ai's static type is DisasterAI, so this `is` is a downcast test; it
                // will not be "always false" and trip CS0184 (an error in this project).
                if (ai is ThunderStormAI) lightning++;
                else if (ai is TornadoAI) tornado++;
            }
        }
    }
}
