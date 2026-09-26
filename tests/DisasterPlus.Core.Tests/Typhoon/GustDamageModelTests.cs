using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class GustDamageModelTests
    {
        [Fact]
        public void NothingOutsideThePatchIsTouched()
        {
            Assert.Equal(0f, GustDamageModel.CollapseChance(1f, 1f, 10), 6);
            Assert.Equal(0f, GustDamageModel.CollapseChance(1.5f, 1f, 10), 6);
        }

        [Fact]
        public void TheCentreIsFarMoreDestructiveThanTheAmbientWind()
        {
            // Pins down "far more violent than the surrounding wind damage" as a number.
            float gust = GustDamageModel.CollapseChance(0f, 1f, 10);
            float wind = WindDamageModel.CollapseChance(1f, 200f, 10);

            Assert.True(gust > wind * 5f,
                        "gust=" + gust + " wind=" + wind + " - the patch must be much more "
                        + "destructive locally than the ambient wind field");
        }

        [Fact]
        public void TheChanceFallsOffToTheRim()
        {
            float previous = float.MaxValue;
            for (float d = 0f; d < 1f; d += 0.05f)
            {
                float now = GustDamageModel.CollapseChance(d, 1f, 10);
                Assert.True(now < previous);
                previous = now;
            }
            Assert.True(previous >= 0f);
        }

        [Fact]
        public void StrengthZeroDisablesTheModelCompletely()
        {
            for (float d = 0f; d < 1f; d += 0.05f)
            {
                Assert.Equal(0f, GustDamageModel.CollapseChance(d, 1f, 0), 6);
            }
            // A negative value from a hand-edited .cgs stays 0 as well.
            Assert.Equal(0f, GustDamageModel.CollapseChance(0f, 1f, -3), 6);
        }

        [Fact]
        public void StrengthAndPatchStrengthBothScaleIt()
        {
            float weakSlider = GustDamageModel.CollapseChance(0f, 1f, 2);
            float strongSlider = GustDamageModel.CollapseChance(0f, 1f, 10);
            Assert.True(strongSlider > weakSlider);

            float weakPatch = GustDamageModel.CollapseChance(0f, 0.6f, 10);
            Assert.True(strongSlider > weakPatch);
        }

        [Fact]
        public void TheChanceNeverLeavesItsRange()
        {
            for (float d = 0f; d <= 1.2f; d += 0.03f)
            {
                for (int s = 0; s <= 12; s++)
                {
                    float chance = GustDamageModel.CollapseChance(d, 1f, s);
                    Assert.InRange(chance, 0f, GustDamageModel.MaxCollapseChance);
                }
            }
        }

        [Fact]
        public void BrokenInputsCollapseNothing()
        {
            // Never create "a NaN distance collapses every building".
            Assert.Equal(0f, GustDamageModel.CollapseChance(float.NaN, 1f, 10), 6);
            Assert.Equal(0f, GustDamageModel.CollapseChance(-1f, 1f, 10), 6);
            Assert.Equal(0f, GustDamageModel.CollapseChance(0f, float.NaN, 10), 6);
            Assert.Equal(0f, GustDamageModel.CollapseChance(0f, -1f, 10), 6);
        }
    }
}
