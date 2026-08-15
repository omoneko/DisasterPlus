using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class FloodTargetTests
    {
        [Fact]
        public void TheUnitIsOneSixtyFourthOfAMetre()
        {
            // §D-4: m_target は絶対水位、1/64 m 単位（海面 40 m = 2560）。
            // ここを 1 m 単位と取り違えると、2 m 上げたつもりが 128 m 上がる。
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
            // ★ ushort の巻き戻りが最悪の壊れ方。65535 を超えた瞬間に
            //   m_target が 0 付近になり、水源が川を**吸い尽くす**。
            Assert.Equal(FloodTarget.MaxTargetUnits, FloodTarget.RaisedTarget(65500, 10f));
            Assert.Equal(FloodTarget.MaxTargetUnits,
                         FloodTarget.RaisedTarget(FloodTarget.MaxTargetUnits, 1f));
        }

        [Fact]
        public void NegativeOrGarbageRiseNeverLowersTheWater()
        {
            // 下げると川が干上がる。上げるだけの機能にする。
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
            // 台風の外縁を掠めただけで川が溢れるのはおかしい。
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
            // 強風域が読めていない（0）なら何もしない。
            Assert.Equal(0f, FloodTarget.RiseAt(0f, 0f, 2f), 5);
        }
    }
}
