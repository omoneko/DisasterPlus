using DisasterPlus.Core.Forecast;
using Xunit;

namespace DisasterPlus.Core.Tests.Forecast
{
    public class HazardLevelTests
    {
        [Fact]
        public void Zero_IsStepZero()
        {
            Assert.Equal(0, HazardLevel.StepOf(0));
        }

        [Fact]
        public void Max_IsTopStep()
        {
            Assert.Equal(HazardLevel.Steps, HazardLevel.StepOf(255));
        }

        [Fact]
        public void StepIsMonotonic()
        {
            int prev = -1;
            for (int i = 0; i <= 255; i++)
            {
                int s = HazardLevel.StepOf((byte)i);
                Assert.True(s >= prev, "step decreased at " + i);
                Assert.True(s >= 0 && s <= HazardLevel.Steps, "step out of range at " + i);
                prev = s;
            }
        }

        [Fact]
        public void BarLengthIsAlwaysSteps()
        {
            for (int i = 0; i <= 255; i++)
            {
                Assert.Equal(HazardLevel.Steps, HazardLevel.BarOf((byte)i).Length);
            }
        }

        [Fact]
        public void BarFilledCountMatchesStep()
        {
            for (int i = 0; i <= 255; i++)
            {
                string bar = HazardLevel.BarOf((byte)i);
                int filled = 0;
                foreach (char c in bar) if (c == HazardLevel.FilledChar) filled++;
                Assert.Equal(HazardLevel.StepOf((byte)i), filled);
            }
        }

        [Fact]
        public void BarIsAsciiOnly()
        {
            // There is no guarantee that the CS UI font has the block elements (▓ ░),
            // so they can come out as tofu boxes.
            // This project has already failed several times over assumptions about
            // what can be drawn.
            for (int i = 0; i <= 255; i += 17)
            {
                foreach (char c in HazardLevel.BarOf((byte)i))
                {
                    Assert.True(c < 128, "non-ASCII character in bar: " + (int)c);
                    Assert.True(c == HazardLevel.FilledChar || c == HazardLevel.EmptyChar,
                        "unexpected char: " + c);
                }
            }
        }
    }
}
