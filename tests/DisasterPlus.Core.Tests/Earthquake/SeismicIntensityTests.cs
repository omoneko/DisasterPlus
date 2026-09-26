using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class SeismicIntensityTests
    {
        [Fact]
        public void RadiusMatchesTheVanillaFormula()
        {
            // IL facts document §A-3: R = 2000 + m_intensity * 20
            Assert.Equal(2000f, SeismicIntensity.RadiusOf(0), 3);
            Assert.Equal(3100f, SeismicIntensity.RadiusOf(55), 3);   // the vanilla default
            Assert.Equal(4000f, SeismicIntensity.RadiusOf(100), 3);  // vanilla's cap for random events
            Assert.Equal(7100f, SeismicIntensity.RadiusOf(255), 3);  // the cap this mod unlocked
        }

        [Fact]
        public void AtEpicentre_IsOne()
        {
            Assert.Equal(1f, SeismicIntensity.At(0f, 55), 4);
        }

        [Fact]
        public void AtTheRadius_IsZero()
        {
            Assert.Equal(0f, SeismicIntensity.At(SeismicIntensity.RadiusOf(55), 55), 4);
        }

        [Fact]
        public void HalfwayOut_IsAHalf()
        {
            // Pins down that it is a linear ramp. Do not make it quadratic or smooth it.
            // This straight line is exactly what vanilla uses for its collapse test.
            Assert.Equal(0.5f, SeismicIntensity.At(1550f, 55), 4);
        }

        [Fact]
        public void BeyondTheRadius_IsZeroAndOutside()
        {
            // Beyond R vanilla rejects first via preRadius, so the test never runs at all.
            // We return 0, but the caller must display this as "out of range", not as
            // "not shaking".
            Assert.Equal(0f, SeismicIntensity.At(9999f, 55), 4);
            Assert.False(SeismicIntensity.IsInside(9999f, 55));
            Assert.True(SeismicIntensity.IsInside(0f, 55));
            Assert.False(SeismicIntensity.IsInside(SeismicIntensity.RadiusOf(55), 55));
        }

        [Fact]
        public void IsMonotonicallyDecreasingWithDistance()
        {
            float prev = 2f;
            for (float d = 0f; d < 3200f; d += 10f)
            {
                float s = SeismicIntensity.At(d, 55);
                Assert.True(s <= prev, "s increased at " + d);
                Assert.InRange(s, 0f, 1f);
                prev = s;
            }
        }

        [Fact]
        public void GarbageInput_IsZeroNotNaN()
        {
            // Never display "intensity NaN" because of a broken reading.
            Assert.Equal(0f, SeismicIntensity.At(float.NaN, 55), 4);
            Assert.Equal(0f, SeismicIntensity.At(-1f, 55), 4);
        }
    }
}
