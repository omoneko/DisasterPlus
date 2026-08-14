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
    }
}
