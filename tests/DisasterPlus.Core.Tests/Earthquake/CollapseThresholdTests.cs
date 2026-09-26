using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class CollapseThresholdTests
    {
        [Fact]
        public void SeedMatchesTheVanillaComposition()
        {
            // IL facts document §A-3: new Randomizer(buildingID | (disasterID << 16))
            Assert.Equal(0x0007_0000 | 1234, CollapseThreshold.SeedFor(1234, 7));
            Assert.Equal(1234, CollapseThreshold.SeedFor(1234, 0));
        }

        [Fact]
        public void CollapseIsDrawnBeforeBurn()
        {
            // If the order is reversed, the collapse threshold and the burn threshold
            // stay swapped while plausible-looking numbers keep coming out.
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
            // "Depends on neither the frame nor the step" is the basis of the claim.
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
            // IL: rnd.Int32(10000) < f * probability * 10000. Equality is not included.
            // When f * p * 10000 == 200, threshold 199 hits and 200 misses.
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
            // Pins down the claim itself: "it collapses within X m of the epicentre".
            //
            // The signature changed in Task 5. The old CollapseDistance(threshold,
            // intensity, probability) took a probability, yet hard-coded the denominator
            // of the ramp to R = RadiusOf(intensity) (= the geometry of the global disc),
            // so passing probability = 1 for the four fault discs returned a meaningless
            // number. The argument has been removed, lining it up with its counterpart
            // GlobalDiscHits.
            const byte intensity = 100;
            for (int threshold = 0; threshold < 250; threshold += 7)
            {
                float limit = CollapseThreshold.GlobalDiscCollapseDistance(threshold, intensity);

                if (limit <= 0f)
                {
                    // It must not collapse at any distance (not even at the epicentre).
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
            // The two entry points must use the same 0.02. Rewrite only one of them and
            // the displayed collapse distance and the actual test silently disagree.
            Assert.Equal(
                CollapseThreshold.Hits(199, 1f, CollapseThreshold.GlobalDiscProbability),
                CollapseThreshold.GlobalDiscHits(199, 1f));
            Assert.True(CollapseThreshold.GlobalDiscHits(199, 1f));
            Assert.False(CollapseThreshold.GlobalDiscHits(200, 1f));

            // A building with threshold 0 always collapses at the epicentre, and that
            // boundary is R itself.
            Assert.Equal(SeismicIntensity.RadiusOf(100),
                         CollapseThreshold.GlobalDiscCollapseDistance(0, 100), 3);
        }
    }
}
