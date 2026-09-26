namespace DisasterPlus.Game
{
    /// <summary>
    /// The sim thread calls Publish and the main thread reads Latest.
    /// The same shape as DiagnosticsHub / FireWhirlRegistry (net35 has no Concurrent
    /// collections, so a plain lock). WeatherSnapshot is immutable, so handing over the
    /// reference is safe on its own.
    /// </summary>
    public static class ForecastHub
    {
        private static readonly object _gate = new object();
        private static WeatherSnapshot _latest;

        public static void Publish(WeatherSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>Null until something has been published. The caller must check.</summary>
        public static WeatherSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>On level unload. State is never carried across cities.</summary>
        public static void Clear()
        {
            lock (_gate) { _latest = null; }
        }
    }
}
