using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TyphoonIntensityTests
    {
        [Fact]
        public void ZeroIsRaisedToTheFloorBecauseATyphoonThatDoesNothingIsNotATyphoon()
        {
            // Vanilla's slider goes all the way down to 0. Raise it at 0 and you get a
            // typhoon that consumes a disaster slot and does nothing (to whoever pressed the
            // button it looks as though it "did not work").
            Assert.Equal(TyphoonIntensity.MinPeak, TyphoonIntensity.PeakOf(0));
            Assert.True(TyphoonIntensity.WasRaised(0));
        }

        [Fact]
        public void NegativeAndOversizedValuesAreClampedBeforeTheByteCast()
        {
            // ★ Clamp before the cast to (byte). Cast first and -1 becomes 255, turning the
            //   weakest setting into the strongest typhoon.
            Assert.Equal(TyphoonIntensity.MinPeak, TyphoonIntensity.PeakOf(-1));
            Assert.Equal(TyphoonIntensity.MinPeak, TyphoonIntensity.PeakOf(int.MinValue));
            Assert.Equal(TyphoonIntensity.MaxPeak, TyphoonIntensity.PeakOf(256));
            Assert.Equal(TyphoonIntensity.MaxPeak, TyphoonIntensity.PeakOf(int.MaxValue));
        }

        [Fact]
        public void ValuesInsideTheRangeAreLeftAlone()
        {
            Assert.Equal(TyphoonIntensity.MinPeak, TyphoonIntensity.PeakOf(TyphoonIntensity.MinPeak));
            Assert.Equal(120, TyphoonIntensity.PeakOf(120));
            Assert.Equal(TyphoonIntensity.MaxPeak, TyphoonIntensity.PeakOf(TyphoonIntensity.MaxPeak));

            Assert.False(TyphoonIntensity.WasRaised(TyphoonIntensity.MinPeak));
            Assert.False(TyphoonIntensity.WasRaised(120));
        }
    }
}
