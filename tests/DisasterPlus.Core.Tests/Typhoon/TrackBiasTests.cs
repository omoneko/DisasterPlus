using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TrackBiasTests
    {
        private const float HalfPi = 1.57079633f;
        private const float Pi = 3.14159265f;

        [Fact]
        public void FacingNorthTheRightSideIsEast()
        {
            // Exactly the worked example in the class doc. Get this the wrong way round and
            // the bias ends up on the opposite side.
            // Travelling north = heading 90 degrees ((cos, sin) = (0, 1) = +Z).
            Assert.Equal(1f, TrackBias.SideOf(HalfPi, 100f, 0f), 4);    // east = directly right
            Assert.Equal(-1f, TrackBias.SideOf(HalfPi, -100f, 0f), 4);  // west = directly left
            Assert.Equal(0f, TrackBias.SideOf(HalfPi, 0f, 100f), 4);    // north = straight ahead
            Assert.Equal(0f, TrackBias.SideOf(HalfPi, 0f, -100f), 4);   // south = directly behind
        }

        [Fact]
        public void FacingEastTheRightSideIsSouth()
        {
            // Travelling east = heading 0 degrees ((1, 0) = +X). Right is south (-Z).
            Assert.Equal(1f, TrackBias.SideOf(0f, 0f, -100f), 4);
            Assert.Equal(-1f, TrackBias.SideOf(0f, 0f, 100f), 4);
        }

        [Fact]
        public void TheBiasTurnsWithTheTrack()
        {
            // ★ This is the test for "it follows a curving track".
            //   Even at the same point, which half of the dangerous semicircle it falls in
            //   changes as the heading rotates.
            const float dx = 100f;
            const float dz = 0f;

            // Heading north: a point to the east is directly right = it gets boosted.
            Assert.True(TrackBias.RadiusFactor(HalfPi, dx, dz, false) > 1f);

            // Turn half a circle to face south and the same point is directly left = no boost.
            Assert.Equal(1f, TrackBias.RadiusFactor(-HalfPi, dx, dz, false), 5);
            Assert.Equal(1f, TrackBias.RadiusFactor(HalfPi + Pi, dx, dz, false), 5);

            // Rotating a little at a time, it falls off continuously (there is no step).
            float previous = TrackBias.RadiusFactor(HalfPi, dx, dz, false);
            for (int i = 1; i <= 18; i++)
            {
                float heading = HalfPi + Pi * i / 18f;
                float now = TrackBias.RadiusFactor(heading, dx, dz, false);
                Assert.True(now <= previous + 1e-5f);
                previous = now;
            }
        }

        [Fact]
        public void TheLeftSideIsNeverWeakenedAndTheRightIsNeverExtreme()
        {
            // The instruction was "boost the right side slightly", not "weaken the left side".
            for (int i = 0; i < 72; i++)
            {
                float angle = 6.2831853f * i / 72f;
                float dx = (float)System.Math.Cos(angle) * 500f;
                float dz = (float)System.Math.Sin(angle) * 500f;

                float radius = TrackBias.RadiusFactor(0.7f, dx, dz, false);
                float chance = TrackBias.ChanceFactor(0.7f, dx, dz, false);

                Assert.InRange(radius, 1f, 1f + TrackBias.MaxRadiusBoost);
                Assert.InRange(chance, 1f, 1f + TrackBias.MaxChanceBoost);
            }
        }

        [Fact]
        public void TheSouthernHemisphereMirrorsTheDangerousSemicircle()
        {
            const float heading = HalfPi;   // heading north

            // In the northern hemisphere it is the right (east).
            Assert.True(TrackBias.DangerousSideOf(heading, 100f, 0f, false) > 0.99f);
            Assert.Equal(0f, TrackBias.DangerousSideOf(heading, -100f, 0f, false), 5);

            // In the southern hemisphere it is the left (west).
            Assert.Equal(0f, TrackBias.DangerousSideOf(heading, 100f, 0f, true), 5);
            Assert.True(TrackBias.DangerousSideOf(heading, -100f, 0f, true) > 0.99f);
        }

        [Fact]
        public void TheEyeItselfAndBrokenInputsGetNoBias()
        {
            // At the centre itself (length 0) it must not divide by zero.
            Assert.Equal(0f, TrackBias.SideOf(1f, 0f, 0f), 6);
            Assert.Equal(1f, TrackBias.RadiusFactor(1f, 0f, 0f, false), 6);

            Assert.Equal(0f, TrackBias.SideOf(float.NaN, 100f, 0f), 6);
            Assert.Equal(0f, TrackBias.SideOf(1f, float.NaN, 0f), 6);
            Assert.Equal(0f, TrackBias.SideOf(1f, 0f, float.NaN), 6);
            Assert.Equal(1f, TrackBias.ChanceFactor(float.NaN, 100f, 100f, false), 6);
        }

        [Fact]
        public void TheBoostIsModest()
        {
            // Pins down in numbers that it is "noticeable, but not a different typhoon".
            Assert.True(TrackBias.MaxRadiusBoost > 0.05f);
            Assert.True(TrackBias.MaxRadiusBoost <= 0.25f);
            Assert.True(TrackBias.MaxChanceBoost > 0.10f);
            Assert.True(TrackBias.MaxChanceBoost <= 0.50f);
        }
    }
}
