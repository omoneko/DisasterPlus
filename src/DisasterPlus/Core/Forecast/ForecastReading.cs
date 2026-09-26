namespace DisasterPlus.Core.Forecast
{
    /// <summary>Current value, target and trend for one weather reading. Immutable.</summary>
    public struct ForecastReading
    {
        public readonly float Current;
        public readonly float Target;
        public readonly Trend Trend;

        public ForecastReading(float current, float target, float deadband)
        {
            Current = current;
            Target = target;
            Trend = TrendMath.Of(current, target, deadband);
        }
    }
}
