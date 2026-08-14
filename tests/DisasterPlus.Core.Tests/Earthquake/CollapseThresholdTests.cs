using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class CollapseThresholdTests
    {
        [Fact]
        public void SeedMatchesTheVanillaComposition()
        {
            // IL 事実文書 §A-3: new Randomizer(buildingID | (disasterID << 16))
            Assert.Equal(0x0007_0000 | 1234, CollapseThreshold.SeedFor(1234, 7));
            Assert.Equal(1234, CollapseThreshold.SeedFor(1234, 0));
        }

        [Fact]
        public void CollapseIsDrawnBeforeBurn()
        {
            // 順序が逆だと、倒壊しきい値と出火しきい値が入れ替わったまま
            // もっともらしい数字が出続ける。
            var rnd = new VanillaRandomizer(CollapseThreshold.SeedFor(4242, 9));
            int expectedCollapse = rnd.Int32(CollapseThreshold.Draws);
            int expectedBurn = rnd.Int32(CollapseThreshold.Draws);

            var t = CollapseThreshold.For(4242, 9);
            Assert.Equal(expectedCollapse, t.Collapse);
            Assert.Equal(expectedBurn, t.Burn);
        }

        [Fact]
        public void ThresholdsAreStableForTheSameBuildingAndDisaster()
        {
            // フレームにもステップにも依存しない、が主張の土台。
            var a = CollapseThreshold.For(500, 3);
            var b = CollapseThreshold.For(500, 3);
            Assert.Equal(a.Collapse, b.Collapse);
            Assert.Equal(a.Burn, b.Burn);
        }

        [Fact]
        public void ThresholdsAreInsideTheDrawRange()
        {
            for (ushort id = 1; id < 400; id++)
            {
                var t = CollapseThreshold.For(id, 5);
                Assert.InRange(t.Collapse, 0, (int)CollapseThreshold.Draws - 1);
                Assert.InRange(t.Burn, 0, (int)CollapseThreshold.Draws - 1);
            }
        }

        [Fact]
        public void DifferentBuildingsGetDifferentThresholds()
        {
            int distinct = 0;
            int first = CollapseThreshold.For(1, 5).Collapse;
            for (ushort id = 2; id < 64; id++)
            {
                if (CollapseThreshold.For(id, 5).Collapse != first) distinct++;
            }
            Assert.True(distinct >= 60, "too few distinct thresholds: " + distinct);
        }

        [Fact]
        public void HitsUsesStrictLessThan()
        {
            // IL: rnd.Int32(10000) < f * probability * 10000。等号は含まない。
            // f * p * 10000 == 200 のとき、しきい値 199 は当たり、200 は外れる。
            Assert.True(CollapseThreshold.Hits(199, 1f, 0.02f));
            Assert.False(CollapseThreshold.Hits(200, 1f, 0.02f));
        }

        [Fact]
        public void HitsIsFalseOutsideTheDisc()
        {
            Assert.False(CollapseThreshold.Hits(0, 0f, 0.02f));
        }

        [Fact]
        public void CollapseDistanceAgreesWithHits()
        {
            // 「震央から X m 以内なら倒れる」という主張そのものを固定する。
            //
            // Task 5 で署名が変わった。以前の CollapseDistance(threshold, intensity,
            // probability) は probability を受け取りながらランプの分母を
            // R = RadiusOf(intensity)（＝全体円盤の幾何）に決め打ちしていたので、
            // 断層 4 円盤の probability = 1 を渡すと意味の無い数字が返っていた。
            // 引数を消して、対になる GlobalDiscHits と揃えてある。
            const byte intensity = 100;
            for (int threshold = 0; threshold < 250; threshold += 7)
            {
                float limit = CollapseThreshold.GlobalDiscCollapseDistance(threshold, intensity);

                if (limit <= 0f)
                {
                    // どの距離でも倒れないこと（震央でも）。
                    Assert.False(CollapseThreshold.GlobalDiscHits(
                        threshold, SeismicIntensity.At(0f, intensity)));
                    continue;
                }

                float inside = limit * 0.99f;
                float outside = limit * 1.01f;
                Assert.True(CollapseThreshold.GlobalDiscHits(
                    threshold, SeismicIntensity.At(inside, intensity)));
                Assert.False(CollapseThreshold.GlobalDiscHits(
                    threshold, SeismicIntensity.At(outside, intensity)));
            }
        }

        [Fact]
        public void CollapseDistanceGrowsWithIntensity()
        {
            float at55 = CollapseThreshold.GlobalDiscCollapseDistance(50, 55);
            float at255 = CollapseThreshold.GlobalDiscCollapseDistance(50, 255);
            Assert.True(at255 > at55, "a stronger quake must reach further");
        }

        [Fact]
        public void GlobalDiscHelpersUseTheVanillaProbability()
        {
            // 2 つの入口が同じ 0.02 を使っていること。片方だけ書き換えると、
            // 表示された倒壊距離と実際の判定が静かに食い違う。
            Assert.Equal(
                CollapseThreshold.Hits(199, 1f, CollapseThreshold.GlobalDiscProbability),
                CollapseThreshold.GlobalDiscHits(199, 1f));
            Assert.True(CollapseThreshold.GlobalDiscHits(199, 1f));
            Assert.False(CollapseThreshold.GlobalDiscHits(200, 1f));

            // しきい値 0 の建物は震央で必ず倒れ、その境界は R そのものになる。
            Assert.Equal(SeismicIntensity.RadiusOf(100),
                         CollapseThreshold.GlobalDiscCollapseDistance(0, 100), 3);
        }
    }
}
