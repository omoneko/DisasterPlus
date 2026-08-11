using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class LifetimeClockTests
    {
        [Fact]
        public void Start_IsZero()
        {
            Assert.Equal(0f, LifetimeClock.Start().ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_Accumulates()
        {
            var c = LifetimeClock.Start().Advance(1.5f).Advance(2.5f);
            Assert.Equal(4f, c.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ClampsNegativeToZero()
        {
            Assert.Equal(3f, LifetimeClock.Start().Advance(3f).Advance(-10f).ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ReturnsNewValue_DoesNotMutate()
        {
            var a = LifetimeClock.Start().Advance(2f);
            var b = a.Advance(3f);
            Assert.Equal(2f, a.ElapsedMinutes, 4);
            Assert.Equal(5f, b.ElapsedMinutes, 4);
        }
    }
}
