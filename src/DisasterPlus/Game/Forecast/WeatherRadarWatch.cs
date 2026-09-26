using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Whether the city has a working weather radar.</b> **The sim thread counts, the
    /// main thread reads.**
    ///
    /// ── What the owner asked for (2026-09-02) ──────────────────────────────
    ///
    /// &gt; A tab for the weather to come (unlocked by placing a weather radar)
    ///
    /// This makes the forecast <b>something you build to obtain</b>. Being able to read
    /// the weather because you are observing it makes sense, and it also gives the ND
    /// DLC's weather radar <b>a use it never had before</b> (in vanilla it only widens
    /// the detection range for thunderstorms and tornadoes).
    ///
    /// ── ★★ Do not hard-code the service type ──────────────────────────────
    ///
    /// <b>Do not write a guess</b> that it will be <c>ItemClass.Service.Disaster</c>.
    /// <b>Read</b> <c>m_class.m_service</c> off the prefab and remember it (see
    /// <see cref="ResolveService"/>). Hard-code it and get it wrong, and it breaks without
    /// any exception at all: <b>you build a radar and nothing ever unlocks</b>.
    ///
    /// Finding no prefab at all means the ND DLC is not owned. In that case
    /// <see cref="PrefabKnown"/> is false and the panel says "you need the DLC" rather
    /// than "it is not unlocked".
    ///
    /// ── Cost ───────────────────────────────────────────────────────────────
    ///
    /// The prefab sweep runs <b>once per city</b>. The counting side looks only at
    /// <b>that service's own list</b> as returned by <c>GetServiceBuildings</c>, so it is
    /// not a full sweep of the 49152-entry building buffer. And it only runs once every
    /// <see cref="IntervalMinutes"/> — radars are not built and demolished several times
    /// a minute.
    /// </summary>
    public static class WeatherRadarWatch
    {
        /// <summary>How often the count is redone (in-game minutes).</summary>
        private const float IntervalMinutes = 1f;

        /// <summary>
        /// What counts as "working". <c>Active</c> is only set when there is enough
        /// electricity, road access and staff (<c>WeatherRadarAI.GetColor</c> checks the
        /// same <c>131072</c> at IL_001D to pick the info-view colour).
        /// **A building that has merely been placed and has no power is not counted.**
        /// </summary>
        private const Building.Flags Working =
            Building.Flags.Created | Building.Flags.Active;

        private static float _minutesSinceScan;
        private static bool _serviceResolved;
        private static bool _prefabKnown;
        private static ItemClass.Service _service;

        private static volatile bool _hasWorking;
        private static volatile int _workingCount;
        private static bool _errorLogged;
        private static bool _announced;

        /// <summary>
        /// Whether there is at least one working radar. **May be read from the main
        /// thread.**
        /// </summary>
        public static bool HasWorkingRadar { get { return _hasWorking; } }

        /// <summary>How many are working (for the diagnostics and the panel).</summary>
        public static int WorkingCount { get { return _workingCount; } }

        /// <summary>
        /// Whether the weather radar prefab has been found.
        /// **false means "the DLC is not owned"**, not "one has not been built yet".
        /// Do not use the same wording for the two.
        /// </summary>
        public static bool PrefabKnown { get { return _prefabKnown; } }

        /// <summary>On level unload. **Nothing is carried across cities.**</summary>
        public static void Reset()
        {
            _minutesSinceScan = 0f;
            _serviceResolved = false;
            _prefabKnown = false;
            _hasWorking = false;
            _workingCount = 0;
            _errorLogged = false;
            _announced = false;
        }

        /// <summary>**Sim thread.** Safe to call every tick (it throttles inside).</summary>
        public static void Poll(float deltaMinutes)
        {
            if (deltaMinutes > 0f) _minutesSinceScan += deltaMinutes;
            if (_minutesSinceScan < IntervalMinutes && _serviceResolved) return;
            _minutesSinceScan = 0f;

            try
            {
                if (!_serviceResolved)
                {
                    _serviceResolved = true;
                    _prefabKnown = ResolveService(out _service);

                    if (!_prefabKnown)
                    {
                        // ★ A setup without the DLC. Never count again (there is no list
                        //   to count in the first place).
                        _hasWorking = false;
                        _workingCount = 0;
                        return;
                    }
                }

                if (!_prefabKnown) return;

                _workingCount = CountWorking(_service);
                bool has = _workingCount > 0;

                // ★ Leave one line at the moment it unlocks, and no more. Do not write
                //   every minute.
                if (has && !_announced)
                {
                    _announced = true;
                    Log.Info("weather radar is running (" + _workingCount
                             + "); the coming-weather forecast is unlocked");
                }

                _hasWorking = has;
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("could not count the weather radars", e);
                }
                // ★ If we could not count, **do not unlock**. Staying locked makes it
                //   easier to notice something is wrong than freezing in the unlocked
                //   state.
                _hasWorking = false;
                _workingCount = 0;
            }
        }

        /// <summary>
        /// Finds the weather radar prefab and remembers its <c>ItemClass.Service</c>.
        /// **Once only.** Returns false if none is found (i.e. the ND DLC is not owned).
        /// </summary>
        private static bool ResolveService(out ItemClass.Service service)
        {
            service = ItemClass.Service.None;

            int count = PrefabCollection<BuildingInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                var info = PrefabCollection<BuildingInfo>.GetLoaded((uint)i);
                if (info == null) continue;
                if (!(info.m_buildingAI is WeatherRadarAI)) continue;
                if (info.m_class == null) continue;

                service = info.m_class.m_service;
                Log.Info("weather radar prefab '" + info.name + "' is in service "
                         + service + "; the coming-weather forecast will watch that list");
                return true;
            }

            Log.Info("no weather radar prefab found; the coming-weather forecast needs "
                     + "the Natural Disasters DLC");
            return false;
        }

        /// <summary>
        /// Counts the working weather radars in that service's list.
        ///
        /// ★ Other disaster buildings (shelters, seismometers and so on) sit in the same
        ///   service list, so always narrow it down with <c>is WeatherRadarAI</c>.
        /// </summary>
        private static int CountWorking(ItemClass.Service service)
        {
            if (!Singleton<BuildingManager>.exists) return 0;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null || bm.m_buildings == null) return 0;

            var list = bm.GetServiceBuildings(service);
            if (list == null) return 0;

            var buildings = bm.m_buildings.m_buffer;
            if (buildings == null) return 0;

            int found = 0;
            for (int i = 0; i < list.m_size; i++)
            {
                ushort id = list.m_buffer[i];
                if (id == 0 || id >= buildings.Length) continue;
                if ((buildings[id].m_flags & Working) != Working) continue;

                var info = buildings[id].Info;
                if (info == null || !(info.m_buildingAI is WeatherRadarAI)) continue;

                found++;
            }

            return found;
        }
    }
}
