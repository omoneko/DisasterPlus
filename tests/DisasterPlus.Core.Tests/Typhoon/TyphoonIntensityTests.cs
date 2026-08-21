using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TyphoonIntensityTests
    {
        [Fact]
        public void ZeroIsRaisedToTheFloorBecauseATyphoonThatDoesNothingIsNotATyphoon()
        {
            // バニラのスライダーは 0 まで下がる。0 のまま起こすと、災害スロットを
            // 1 個使って何も起こさない台風になる（押した人には「効かなかった」に見える）。
            Assert.Equal(TyphoonIntensity.MinPeak, TyphoonIntensity.PeakOf(0));
            Assert.True(TyphoonIntensity.WasRaised(0));
        }

        [Fact]
        public void NegativeAndOversizedValuesAreClampedBeforeTheByteCast()
        {
            // ★ (byte) へのキャストの前にクランプすること。先にキャストすると
            //   -1 が 255 になり、いちばん弱い設定がいちばん強い台風になる。
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
