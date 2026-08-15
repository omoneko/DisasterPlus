using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class WindDamageModelTests
    {
        [Fact]
        public void WeakWindDoesNothingAtAll()
        {
            // 強風域の縁で家が飛ぶ、を作らない。
            Assert.Equal(0f, WindDamageModel.CollapseChance(0f, 40f, 10), 6);
            Assert.Equal(0f, WindDamageModel.CollapseChance(WindDamageModel.MinWind, 40f, 10), 6);
            Assert.True(WindDamageModel.CollapseChance(WindDamageModel.MinWind + 0.01f, 40f, 10) > 0f);
        }

        [Fact]
        public void StrengthZeroDisablesTheModelCompletely()
        {
            // 設定で完全に切れることを保証する（スライダー 0）。
            for (float w = 0f; w <= 1f; w += 0.05f)
            {
                Assert.Equal(0f, WindDamageModel.CollapseChance(w, 200f, 0), 6);
            }
            // 手で編集された .cgs の負値でも 0 のまま。
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
            // ★ ②の長周期と判断が違う。あちらは高さが読めなければ何もしなかったが、
            //   台風は平屋も飛ばす。高さ 0（= 不明）はボーナス無しの基本確率で残す。
            //   これは高さを推測しているのではなく、ボーナスの適用を辞退している。
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
            // 天井を超えても伸び続けない。超高層 1 棟が確率 1.0 にならない。
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
