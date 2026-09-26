using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class FloodTargetTests
    {
        [Fact]
        public void TheUnitIsOneSixtyFourthOfAMetre()
        {
            // §D-4: m_target is an absolute water level in units of 1/64 m
            // (sea level 40 m = 2560). Mistake it for units of 1 m and an intended rise of
            // 2 m becomes a rise of 128 m.
            Assert.Equal(64, FloodTarget.UnitsPerMetre);
            Assert.Equal(2560, 40 * FloodTarget.UnitsPerMetre);
        }

        [Fact]
        public void RaisingAddsTheRightNumberOfUnits()
        {
            Assert.Equal((ushort)(2560 + 2 * 64), FloodTarget.RaisedTarget(2560, 2f));
            Assert.Equal((ushort)2560, FloodTarget.RaisedTarget(2560, 0f));
        }

        [Fact]
        public void RaisingSaturatesInsteadOfWrappingAround()
        {
            // ★ A ushort wrap-around is the worst way for this to break. The moment it goes
            //   past 65535, m_target lands near 0 and the water source **drains the river dry**.
            Assert.Equal(FloodTarget.MaxTargetUnits, FloodTarget.RaisedTarget(65500, 10f));
            Assert.Equal(FloodTarget.MaxTargetUnits,
                         FloodTarget.RaisedTarget(FloodTarget.MaxTargetUnits, 1f));
        }

        [Fact]
        public void NegativeOrGarbageRiseNeverLowersTheWater()
        {
            // Lower it and the river dries up. This feature only ever raises.
            Assert.Equal((ushort)2560, FloodTarget.RaisedTarget(2560, -5f));
            Assert.Equal((ushort)2560, FloodTarget.RaisedTarget(2560, float.NaN));
        }

        [Fact]
        public void StrengthZeroProducesNoRiseAtAll()
        {
            Assert.Equal(0f, FloodTarget.RiseMetresOf(255, 1f, 0), 5);
            Assert.Equal(0f, FloodTarget.RiseMetresOf(255, 1f, -3), 5);
        }

        [Fact]
        public void LightRainProducesNoRise()
        {
            // It would be odd for the river to burst its banks after merely being grazed by
            // the outer edge of the typhoon.
            Assert.Equal(0f, FloodTarget.RiseMetresOf(255, FloodTarget.MinRainForRise, 10), 5);
            Assert.True(FloodTarget.RiseMetresOf(255, 1f, 10) > 0f);
        }

        [Fact]
        public void TheRiseIsCappedAndGrowsWithIntensity()
        {
            Assert.True(FloodTarget.RiseMetresOf(255, 1f, 10)
                        > FloodTarget.RiseMetresOf(60, 1f, 10));
            for (int i = 0; i <= 255; i += 5)
            {
                Assert.InRange(FloodTarget.RiseMetresOf((byte)i, 1f, 10),
                               0f, FloodTarget.MaxRiseMetres);
            }
        }

        [Fact]
        public void OnlySourcesNearTheStormAreRaised()
        {
            const float gale = 3000f;
            Assert.Equal(2f, FloodTarget.RiseAt(0f, gale, 2f), 4);
            Assert.True(FloodTarget.RiseAt(gale * 0.5f, gale, 2f) < 2f);
            Assert.Equal(0f, FloodTarget.RiseAt(gale, gale, 2f), 5);
            Assert.Equal(0f, FloodTarget.RiseAt(gale + 1f, gale, 2f), 5);
            // If the gale area cannot be read (0), do nothing.
            Assert.Equal(0f, FloodTarget.RiseAt(0f, 0f, 2f), 5);
        }
    }
}
