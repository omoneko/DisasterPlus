namespace DisasterPlus.Core.Forecast
{
    /// <summary>1 つの気象値の現在・目標・傾向。不変。</summary>
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
