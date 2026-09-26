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
            // While interpolating there is always a tiny difference. Calling that Rising
            // makes the arrow meaningless.
            Assert.Equal(Trend.Steady, TrendMath.Of(0.500f, 0.510f, 0.02f));
            Assert.Equal(Trend.Steady, TrendMath.Of(0.510f, 0.500f, 0.02f));
        }

        [Fact]
        public void ExactlyAtDeadband_IsSteady()
        {
            // The boundary falls on the Steady side (this reduces flicker)
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
            // A misconfiguration on the caller's side must not invert the behaviour
            Assert.Equal(Trend.Rising, TrendMath.Of(0.2f, 0.8f, -1f));
        }

        [Fact]
        public void NaNInputs_AreSteady()
        {
            // A corrupted reading must not make the arrow lie
            Assert.Equal(Trend.Steady, TrendMath.Of(float.NaN, 0.5f, 0.02f));
            Assert.Equal(Trend.Steady, TrendMath.Of(0.5f, float.NaN, 0.02f));
        }

        [Fact]
        public void TemperatureDeadband_IsWiderThanDefault()
        {
            // The temperature is a real value in degrees Celsius, not a 0.0-1.0 normalised
            // one, so with the default deadband it is effectively zero. Being "wide" is the
            // very reason this constant exists, so pin it down.
            Assert.True(TrendMath.TemperatureDeadband > TrendMath.DefaultDeadband);
        }

        [Fact]
        public void TemperatureDeadband_PinnedToHalfADegree()
        {
            // This value was moved over from the hard-coded 0.5f in WeatherReader. Pins
            // down that it was moved without changing the behaviour (if this shifts, the
            // trend display in the forecast changes silently).
            Assert.Equal(0.5f, TrendMath.TemperatureDeadband);
        }

        [Fact]
        public void TemperatureDeadband_SuppressesSubDegreeDrift()
        {
            // Rising/Falling must not flicker on the tiny per-tick change during the
            // seasonal interpolation. With the default deadband the same input would come
            // out Rising (we look at both for contrast).
            Assert.Equal(Trend.Steady, TrendMath.Of(18.0f, 18.3f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Steady, TrendMath.Of(18.3f, 18.0f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Rising, TrendMath.Of(18.0f, 18.3f, TrendMath.DefaultDeadband));
        }

        [Fact]
        public void TemperatureDeadband_StillSeesRealSeasonalChange()
        {
            // Nor may it be so blunt that it misses the seasonal change. A move of 1 degree
            // is always picked up.
            Assert.Equal(Trend.Rising, TrendMath.Of(18.0f, 19.0f, TrendMath.TemperatureDeadband));
            Assert.Equal(Trend.Falling, TrendMath.Of(19.0f, 18.0f, TrendMath.TemperatureDeadband));
        }
    }
}
