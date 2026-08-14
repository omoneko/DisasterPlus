using DisasterPlus.Core.Forecast;

namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// 一度作ったら書き換えない。
    /// </summary>
    public class WeatherSnapshot
    {
        public readonly ForecastReading Temperature;
        public readonly ForecastReading Rain;
        public readonly ForecastReading Cloud;
        public readonly ForecastReading Fog;
        public readonly float WindDegrees;
        public readonly float GroundWetness;
        public readonly float LastLightning;
        public readonly float DisasterProbability;
        public readonly int DisasterCooldown;

        /// <summary>読み取りに成功したか。false ならパネルは「読み取れません」と出す。</summary>
        public readonly bool Valid;

        public WeatherSnapshot(ForecastReading temperature, ForecastReading rain,
                               ForecastReading cloud, ForecastReading fog,
                               float windDegrees, float groundWetness, float lastLightning,
                               float disasterProbability, int disasterCooldown, bool valid)
        {
            Temperature = temperature;
            Rain = rain;
            Cloud = cloud;
            Fog = fog;
            WindDegrees = windDegrees;
            GroundWetness = groundWetness;
            LastLightning = lastLightning;
            DisasterProbability = disasterProbability;
            DisasterCooldown = disasterCooldown;
            Valid = valid;
        }

        public static WeatherSnapshot Invalid()
        {
            var zero = new ForecastReading(0f, 0f, TrendMath.DefaultDeadband);
            return new WeatherSnapshot(zero, zero, zero, zero, 0f, 0f, 0f, 0f, 0, false);
        }
    }
}
