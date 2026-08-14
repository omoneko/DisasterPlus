using DisasterPlus.Core.Forecast;
using Xunit;

namespace DisasterPlus.Core.Tests.Forecast
{
    public class TrendMathTests
    {
        [Fact]
        public void TargetAboveCurrent_IsRising()
        {
            Assert.Equal(Trend.Rising, TrendMath.Of(0.2f, 0.8f, TrendMath.DefaultDeadband));
        }

        [Fact]
        public void TargetBelowCurrent_IsFalling()
        {
            Assert.Equal(Trend.Falling, TrendMath.Of(0.8f, 0.2f, TrendMath.DefaultDeadband));
        }

        [Fact]
        public void Equal_IsSteady()
        {
            Assert.Equal(Trend.Steady, TrendMath.Of(0.5f, 0.5f, TrendMath.DefaultDeadband));
        }

        [Fact]
        public void WithinDeadband_IsSteady()
        {
            // 補間中はごく小さな差が常に出る。これを Rising にすると矢印が無意味になる。
            Assert.Equal(Trend.Steady, TrendMath.Of(0.500f, 0.510f, 0.02f));
            Assert.Equal(Trend.Steady, TrendMath.Of(0.510f, 0.500f, 0.02f));
        }

        [Fact]
        public void ExactlyAtDeadband_IsSteady()
        {
            // 境界は Steady 側に倒す（ちらつきを減らす）
            Assert.Equal(Trend.Steady, TrendMath.Of(0.50f, 0.52f, 0.02f));
        }

        [Fact]
        public void JustBeyondDeadband_IsRising()
        {
            Assert.Equal(Trend.Rising, TrendMath.Of(0.50f, 0.5201f, 0.02f));
        }

        [Fact]
        public void NegativeValues_Work()
        {
            Assert.Equal(Trend.Rising, TrendMath.Of(-10f, -2f, 0.02f));
            Assert.Equal(Trend.Falling, TrendMath.Of(-2f, -10f, 0.02f));
        }

        [Fact]
        public void ZeroDeadband_AnyDifferenceCounts()
        {
            Assert.Equal(Trend.Rising, TrendMath.Of(0.5f, 0.5000001f, 0f));
        }

        [Fact]
        public void NegativeDeadband_IsTreatedAsZero()
        {
            // 呼び出し側の設定ミスで挙動が反転しないこと
            Assert.Equal(Trend.Rising, TrendMath.Of(0.2f, 0.8f, -1f));
        }

        [Fact]
        public void NaNInputs_AreSteady()
        {
            // 破損した読み取りで矢印が嘘をつかないこと
            Assert.Equal(Trend.Steady, TrendMath.Of(float.NaN, 0.5f, 0.02f));
            Assert.Equal(Trend.Steady, TrendMath.Of(0.5f, float.NaN, 0.02f));
        }

        [Fact]
        public void TemperatureDeadband_IsWiderThanDefault()
        {
            // 気温は 0.0-1.0 の正規化値ではなく摂氏の実値なので、既定の不感帯では
            // 実質ゼロになる。「広い」ことがこの定数の存在理由そのものなので固定する。
            Assert.True(TrendMath.TemperatureDeadband > TrendMath.DefaultDeadband);
        }

        [Fact]
        public void TemperatureDeadband_PinnedToHalfADegree()
        {
            // WeatherReader からベタ書きの 0.5f を移してきた値。挙動を変えずに
            // 移設したことを固定する（ここが動くと予報の傾向表示が黙って変わる）。
            Assert.Equal(0.5f, TrendMath.TemperatureDeadband);
        }

        [Fact]
        public void TemperatureDeadband_SuppressesSubDegreeDrift()
        {
            // 季節補間中の毎 tick のごく小さな変化で Rising/Falling がちらつかないこと。
            // 既定の不感帯だと同じ入力が Rising になってしまう（対比のため両方見る）。
            Assert.Equal(Trend.Steady, TrendMath.Of(18.0f, 18.3f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Steady, TrendMath.Of(18.3f, 18.0f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Rising, TrendMath.Of(18.0f, 18.3f, TrendMath.DefaultDeadband));
        }

        [Fact]
        public void TemperatureDeadband_StillSeesRealSeasonalChange()
        {
            // 鈍すぎて季節変化を取り逃してもいけない。1 度動けば必ず拾う。
            Assert.Equal(Trend.Rising, TrendMath.Of(18.0f, 19.0f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Falling, TrendMath.Of(19.0f, 18.0f, TrendMath.TemperatureDeadband));
        }
    }
}
