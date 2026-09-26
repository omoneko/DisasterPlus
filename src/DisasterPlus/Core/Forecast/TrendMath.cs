namespace DisasterPlus.Core.Forecast
{
    /// <summary>
    /// Works out the trend from the gap between WeatherManager's current and target.
    ///
    /// This is the heart of the feature. Vanilla only ever shows hazard (a static risk
    /// figure) and never tells you "which way it is heading right now". Both current and
    /// target are readable as public fields, so we build a time axis out of them.
    /// </summary>
    public static class TrendMath
    {
        /// <summary>
        /// The default deadband. current is interpolated continuously towards target, so a
        /// strict comparison would always say Rising or Falling and the arrow would stop
        /// meaning anything.
        ///
        /// Rain, clouds and fog are all normalised to 0.0-1.0, so this 0.02 means "2% of
        /// the full range". Do not use the same number for temperature (see
        /// <see cref="TemperatureDeadband"/>).
        /// </summary>
        public const float DefaultDeadband = 0.02f;

        /// <summary>
        /// The deadband for temperature only (degrees).
        ///
        /// Temperature is the one reading on a different scale. Rain, clouds and fog are
        /// normalised 0.0-1.0, but temperature is a real Celsius value (roughly -20 to +35
        /// as the seasons turn), so <see cref="DefaultDeadband"/>'s 0.02 would mean "0.02
        /// degrees", which is effectively zero. The seasonal interpolation moves a touch
        /// every tick, so the trend would sit permanently on Rising or Falling and the
        /// "which way is it heading right now" display — the whole point of this feature —
        /// would stop meaning anything.
        ///
        /// 0.5 degrees was chosen to be coarser than the panel's own F1 rounding (0.1
        /// degree steps), while still fine enough not to miss the in-game seasonal drift
        /// (a few degrees per in-game day).
        ///
        /// This constant living here (in Core) is deliberate. It used to be a hard-coded
        /// 0.5f inside WeatherReader (in Game/, where unit tests cannot reach), so
        /// temperature was the one deadband sitting outside the tests while every other
        /// one came from TrendMath.
        /// </summary>
        public const float TemperatureDeadband = 0.5f;

        public static Trend Of(float current, float target, float deadband)
        {
            // The arrow must not lie because of a corrupt value. Every comparison against
            // NaN is false, so reject it explicitly.
            if (float.IsNaN(current) || float.IsNaN(target)) return Trend.Steady;
            if (float.IsInfinity(current) || float.IsInfinity(target)) return Trend.Steady;

            float band = deadband > 0f ? deadband : 0f;
            float diff = target - current;

            // Exactly on the boundary falls to the Steady side (it cuts down on flicker).
            if (diff > band) return Trend.Rising;
            if (diff < -band) return Trend.Falling;
            return Trend.Steady;
        }
    }
}
