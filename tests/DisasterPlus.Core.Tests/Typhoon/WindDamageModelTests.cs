using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class WindDamageModelTests
    {
        [Fact]
        public void WeakWindDoesNothingAtAll()
        {
            // Do not create houses being blown away at the edge of the strong-wind zone.
            Assert.Equal(0f, WindDamageModel.CollapseChance(0f, 40f, 10), 6);
            Assert.Equal(0f, WindDamageModel.CollapseChance(WindDamageModel.MinWind, 40f, 10), 6);
            Assert.True(WindDamageModel.CollapseChance(WindDamageModel.MinWind + 0.01f, 40f, 10) > 0f);
        }

        [Fact]
        public void StrengthZeroDisablesTheModelCompletely()
        {
            // Guarantee that the setting can switch it off completely (slider at 0).
            for (float w = 0f; w <= 1f; w += 0.05f)
            {
                Assert.Equal(0f, WindDamageModel.CollapseChance(w, 200f, 0), 6);
            }
            // A negative value from a hand-edited .cgs still stays 0.
            Assert.Equal(0f, WindDamageModel.CollapseChance(1f, 200f, -5), 6);
        }

        [Fact]
        public void StrongerWindMeansAHigherChance()
        {
            float low = WindDamageModel.CollapseChance(0.5f, 40f, 10);
            float high = WindDamageModel.CollapseChance(0.95f, 40f, 10);
            Assert.True(high > low);
        }

        [Fact]
        public void TallerBuildingsCatchMoreWind()
        {
            float low = WindDamageModel.CollapseChance(0.8f, 12f, 10);
            float high = WindDamageModel.CollapseChance(0.8f, 90f, 10);
            Assert.True(high > low);
        }

        [Fact]
        public void UnknownHeightDeclinesTheBonusButStaysEligible()
        {
            // ★ The decision here differs from ②'s long-period case. That one did nothing
            //   when the height could not be read, but a typhoon blows single-storey houses
            //   away too. Height 0 (= unknown) is kept at the base probability with no
            //   bonus. This is not guessing the height; it is declining to apply the bonus.
            Assert.Equal(1f, WindDamageModel.HeightFactor(0f), 4);
            Assert.True(WindDamageModel.CollapseChance(0.9f, 0f, 10) > 0f);
            Assert.True(WindDamageModel.CollapseChance(0.9f, 0f, 10)
                        < WindDamageModel.CollapseChance(0.9f, 90f, 10));
        }

        [Fact]
        public void HeightFactorIsBoundedAtBothEnds()
        {
            Assert.Equal(1f, WindDamageModel.HeightFactor(WindDamageModel.HeightFloorMetres), 4);
            float top = WindDamageModel.HeightFactor(WindDamageModel.HeightCeilingMetres);
            Assert.Equal(1f + WindDamageModel.HeightBonus, top, 4);
            // It does not keep growing past the ceiling. A single skyscraper never ends up
            // at probability 1.0.
            Assert.Equal(top, WindDamageModel.HeightFactor(100000f), 4);
        }

        [Fact]
        public void TheChanceIsCappedAndNeverNegative()
        {
            float ceiling = WindDamageModel.MaxCollapseChance * (1f + WindDamageModel.HeightBonus);
            for (float w = 0f; w <= 1f; w += 0.01f)
            {
                for (float h = 0f; h < 400f; h += 7f)
                {
                    Assert.InRange(WindDamageModel.CollapseChance(w, h, 10), 0f, ceiling);
                }
            }
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            Assert.Equal(0f, WindDamageModel.CollapseChance(float.NaN, 40f, 10), 6);
            Assert.Equal(0f, WindDamageModel.CollapseChance(0.9f, float.NaN, 10), 6);
            Assert.Equal(0f, WindDamageModel.CollapseChance(-1f, 40f, 10), 6);
        }
    }
}
