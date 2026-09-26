using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class SeismicScaleTests
    {
        [Fact]
        public void Zero_IsStepZeroAndBandNone()
        {
            Assert.Equal(0, SeismicScale.StepOf(0f));
            Assert.Equal(SeismicBand.None, SeismicScale.BandOf(0f));
        }

        [Fact]
        public void One_IsTheTopStepAndSevere()
        {
            Assert.Equal(SeismicScale.Steps, SeismicScale.StepOf(1f));
            Assert.Equal(SeismicBand.Severe, SeismicScale.BandOf(1f));
        }

        [Fact]
        public void StepIsMonotonicAndBounded()
        {
            int prev = -1;
            for (int i = 0; i <= 1000; i++)
            {
                int step = SeismicScale.StepOf(i / 1000f);
                Assert.True(step >= prev, "step decreased at " + i);
                Assert.InRange(step, 0, SeismicScale.Steps);
                prev = step;
            }
        }

        [Fact]
        public void OutOfRangeInput_IsClampedNotWrapped()
        {
            Assert.Equal(0, SeismicScale.StepOf(-5f));
            Assert.Equal(SeismicScale.Steps, SeismicScale.StepOf(5f));
            Assert.Equal(0, SeismicScale.StepOf(float.NaN));
            Assert.Equal(SeismicBand.None, SeismicScale.BandOf(float.NaN));
        }

        [Fact]
        public void BarLengthIsAlwaysSteps()
        {
            for (int i = 0; i <= 1000; i += 7)
            {
                Assert.Equal(SeismicScale.Steps, SeismicScale.BarOf(i / 1000f).Length);
            }
        }

        [Fact]
        public void BarFilledCountMatchesStep()
        {
            for (int i = 0; i <= 1000; i += 7)
            {
                float s = i / 1000f;
                string bar = SeismicScale.BarOf(s);
                int filled = 0;
                foreach (char c in bar) if (c == SeismicScale.FilledChar) filled++;
                Assert.Equal(SeismicScale.StepOf(s), filled);
            }
        }

        [Fact]
        public void BarIsAsciiOnly()
        {
            // The same decision as feature no. 1's HazardLevel. There is no guarantee that
            // CS's UI font has box-drawing characters.
            for (int i = 0; i <= 1000; i += 13)
            {
                foreach (char c in SeismicScale.BarOf(i / 1000f))
                {
                    Assert.True(c < 128, "non-ASCII character in bar: " + (int)c);
                    Assert.True(c == SeismicScale.FilledChar || c == SeismicScale.EmptyChar,
                        "unexpected char: " + c);
                }
            }
        }
    }
}
