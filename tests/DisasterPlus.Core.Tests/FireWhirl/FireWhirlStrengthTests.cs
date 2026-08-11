using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlStrengthTests
    {
        [Fact]
        public void Radius_IsMonotonicNonDecreasing()
        {
            float prev = 0f;
            for (int n = 0; n <= 300; n++)
            {
                float r = FireWhirlStrength.RadiusFor(n);
                Assert.True(r >= prev, "radius decreased at n=" + n);
                prev = r;
            }
        }

        [Fact]
        public void Radius_IsClampedToBounds()
        {
            Assert.Equal(FireWhirlStrength.MinRadius, FireWhirlStrength.RadiusFor(0), 3);
            Assert.Equal(FireWhirlStrength.MinRadius, FireWhirlStrength.RadiusFor(-5), 3);
            Assert.Equal(FireWhirlStrength.MaxRadius, FireWhirlStrength.RadiusFor(100000), 3);
        }

        [Fact]
        public void Radius_SmallFire_IsSmallerThanLargeFire()
        {
            Assert.True(FireWhirlStrength.RadiusFor(12) < FireWhirlStrength.RadiusFor(80));
        }

        [Fact]
        public void DamageScale_IsMonotonicAndBounded()
        {
            float prev = 0f;
            for (int n = 0; n <= 300; n++)
            {
                float s = FireWhirlStrength.DamageScaleFor(n);
                Assert.True(s >= prev, "scale decreased at n=" + n);
                Assert.True(s >= 0f && s <= 1f, "scale out of range: " + s);
                prev = s;
            }
        }
    }
}
