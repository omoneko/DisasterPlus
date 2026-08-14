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
            const byte intensity = 100;
            for (int threshold = 0; threshold < 250; threshold += 7)
            {
                float limit = CollapseThreshold.CollapseDistance(
                    threshold, intensity, CollapseThreshold.GlobalDiscProbability);

                if (limit <= 0f)
                {
                    // どの距離でも倒れないこと（震央でも）。
                    Assert.False(CollapseThreshold.Hits(
                        threshold, SeismicIntensity.At(0f, intensity),
                        CollapseThreshold.GlobalDiscProbability));
                    continue;
                }

                float inside = limit * 0.99f;
                float outside = limit * 1.01f;
                Assert.True(CollapseThreshold.Hits(
                    threshold, SeismicIntensity.At(inside, intensity),
                    CollapseThreshold.GlobalDiscProbability));
                Assert.False(CollapseThreshold.Hits(
                    threshold, SeismicIntensity.At(outside, intensity),
                    CollapseThreshold.GlobalDiscProbability));
            }
        }

        [Fact]
        public void CollapseDistanceGrowsWithIntensity()
        {
            float at55 = CollapseThreshold.CollapseDistance(50, 55, CollapseThreshold.GlobalDiscProbability);
            float at255 = CollapseThreshold.CollapseDistance(50, 255, CollapseThreshold.GlobalDiscProbability);
            Assert.True(at255 > at55, "a stronger quake must reach further");
        }
    }
}
