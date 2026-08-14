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

        /// <summary>
        /// DisasterProbability / DisasterCooldown が実際に DisasterManager から読めたか。
        ///
        /// レビュー指摘（コーディネーターからのフィードバック）: DisasterManager が
        /// 居ない場合、以前は DisasterProbability を黙って 0f のまま返していた。
        /// これは「確率 0%」という実際の読み取り結果と外見上区別が付かない
        /// **捏造されたゼロ**であり、ハザード数値を表示中でない種別のラベルで出す
        /// のと同種の「確信を持って誤った数値」になる。このフラグで
        /// 「読めなかった（不明）」と「読んだ結果 0 だった」を呼び出し側が
        /// 区別できるようにする。false のときパネルは確率の行そのものを出さない
        /// （0.0% と表示しない）。
        /// </summary>
        public readonly bool DisasterInfoAvailable;

        /// <summary>読み取りに成功したか。false ならパネルは「読み取れません」と出す。</summary>
        public readonly bool Valid;

        public WeatherSnapshot(ForecastReading temperature, ForecastReading rain,
                               ForecastReading cloud, ForecastReading fog,
                               float windDegrees, float groundWetness, float lastLightning,
                               float disasterProbability, int disasterCooldown,
                               bool disasterInfoAvailable, bool valid)
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
            DisasterInfoAvailable = disasterInfoAvailable;
            Valid = valid;
        }

        public static WeatherSnapshot Invalid()
        {
            var zero = new ForecastReading(0f, 0f, TrendMath.DefaultDeadband);
            return new WeatherSnapshot(zero, zero, zero, zero, 0f, 0f, 0f, 0f, 0, false, false);
        }
    }
}
