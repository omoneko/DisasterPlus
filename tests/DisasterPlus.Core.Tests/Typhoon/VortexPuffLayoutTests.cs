using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class VortexPuffLayoutTests
    {
        [Fact]
        public void TheEyeStaysAHole()
        {
            // ★★ "Put the centre of the puff outside the eye" is not enough. A single puff
            //   is scattered over the radius of its disc and spreads outward by half its
            //   particle size. The old implementation left that promise to the Game side
            //   (i.e. no test could catch it) and did in fact fill in the eye once.
            //   Both the disc radius and the particle size are now **declared by Core**,
            //   so we can pin it down here.
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float clearance = VortexPuffLayout.EyeClearanceOf(i);
                Assert.True(clearance >= -1e-5f,
                            "puff " + i + " reaches into the eye (clearance=" + clearance + ")");
            }
        }

        [Fact]
        public void TheEyeIsBigEnoughToRead()
        {
            // If the hole is smaller than a single puff it does not read as a hole at all.
            Assert.True(VortexPuffLayout.EyeFraction
                        >= VortexPuffLayout.TowerSizeFraction * 0.5f);
            // If the eyewall sits beyond half the outer radius it looks like a ring, not a vortex.
            Assert.True(VortexPuffLayout.EyeWallFraction < 0.5f);
            // The wall must sit outside the eye.
            Assert.True(VortexPuffLayout.EyeWallFraction > VortexPuffLayout.EyeFraction);
        }

        [Fact]
        public void EveryPuffIsInsideTheDrawableRanges()
        {
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff p = VortexPuffLayout.PuffAt(i);

                Assert.InRange(p.AngleRadians, 0f, 6.2831854f);
                Assert.InRange(p.HeightFraction, 0f, 1f);
                Assert.InRange(p.DiscFraction, 0f, 1f);
                Assert.InRange(p.BandFraction, 0f, 1f);
                Assert.InRange(p.DensityFraction, 0f, 1f);
                Assert.InRange(p.SwirlFraction, 0f, 1f);
                Assert.InRange(p.RiseFraction, 0f, 1f);
                Assert.InRange(p.RadialFraction, -1f, 1f);

                // It may overrun the outer radius by the jitter and the per-level offset,
                // but not without limit.
                Assert.True(p.RadiusFraction
                            <= 1f + VortexPuffLayout.RadiusJitterFraction + 0.07f);
            }
        }

        [Fact]
        public void TheLayoutIsTheSameEveryTime()
        {
            // A function of the index alone. Unless it gives the same shape every frame the
            // vortex looks like it is boiling.
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff a = VortexPuffLayout.PuffAt(i);
                VortexPuff b = VortexPuffLayout.PuffAt(i);

                Assert.Equal(a.AngleRadians, b.AngleRadians, 6);
                Assert.Equal(a.RadiusFraction, b.RadiusFraction, 6);
                Assert.Equal(a.HeightFraction, b.HeightFraction, 6);
                Assert.Equal(a.DensityFraction, b.DensityFraction, 6);
                Assert.Equal(a.Layer, b.Layer);
            }
        }

        [Fact]
        public void EveryColumnCarriesTheThreeLayersOfACumulonimbus()
        {
            // 1 deck, 2 towers, 1 canopy. **The two tower levels are what makes it billow**;
            // cut them to one and it goes back to being a flat scrap of cloud rather than a
            // thunderhead.
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                int deck = 0, tower = 0, canopy = 0;
                for (int level = 0; level < VortexPuffLayout.LevelsPerColumn; level++)
                {
                    VortexPuff p = VortexPuffLayout.PuffAt(
                        column * VortexPuffLayout.LevelsPerColumn + level);
                    if (p.Layer == VortexCloudLayer.Deck) deck++;
                    else if (p.Layer == VortexCloudLayer.Tower) tower++;
                    else canopy++;
                }

                Assert.Equal(1, deck);
                Assert.Equal(2, tower);
                Assert.Equal(1, canopy);
            }
        }

        [Fact]
        public void EveryColumnStandsUpFromADarkBaseToABrightTop()
        {
            // A cumulonimbus grows vertically. Each level must be higher than the last
            // (the step is larger than the jitter).
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                float previous = -1f;
                for (int level = 0; level < VortexPuffLayout.LevelsPerColumn; level++)
                {
                    VortexPuff p = VortexPuffLayout.PuffAt(
                        column * VortexPuffLayout.LevelsPerColumn + level);
                    Assert.True(p.HeightFraction > previous,
                                "column " + column + " level " + level + " does not rise");
                    previous = p.HeightFraction;
                }
            }
        }

        [Fact]
        public void TheEyeWallIsTheTallestAndTheArmsRunOutward()
        {
            // The eyewall columns (the first EyeWallColumns of them) must be the tallest.
            float wallTop = TopOf(0);
            for (int column = VortexPuffLayout.EyeWallColumns;
                 column < VortexPuffLayout.ColumnCount; column++)
            {
                Assert.True(TopOf(column) <= wallTop + 1e-3f,
                            "arm column " + column + " is taller than the eyewall");
            }

            // The arms run outward (within one arm the column radius increases monotonically).
            for (int arm = 0; arm < VortexPuffLayout.ArmCount; arm++)
            {
                float previous = -1f;
                for (int step = 0; step < VortexPuffLayout.ColumnsPerArm; step++)
                {
                    int column = VortexPuffLayout.EyeWallColumns
                                 + arm * VortexPuffLayout.ColumnsPerArm + step;
                    // The lower tower level (level 1) has an offset of 0, so the column's own
                    // radius can be read directly.
                    float r = VortexPuffLayout
                        .PuffAt(column * VortexPuffLayout.LevelsPerColumn + 1).RadiusFraction;
                    Assert.True(r > previous);
                    previous = r;
                }
            }
        }

        [Fact]
        public void TheLowLevelInflowTurnsIntoHighLevelOutflow()
        {
            // The secondary circulation of a typhoon. The lower levels draw in, the canopy
            // blows out. Reverse it and the vortex looks like it is scattering outward.
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                int at = column * VortexPuffLayout.LevelsPerColumn;
                Assert.True(VortexPuffLayout.PuffAt(at).RadialFraction < 0f);
                Assert.True(VortexPuffLayout.PuffAt(at + 3).RadialFraction > 0f);

                // The lower the level the faster it spins (the gale near the ground).
                Assert.True(VortexPuffLayout.PuffAt(at).SwirlFraction
                            > VortexPuffLayout.PuffAt(at + 3).SwirlFraction);
            }
        }

        [Fact]
        public void TheDeckIsWiderAndDenserThanTheTowers()
        {
            // The "dark, flat underside" is produced by a wide disc and a high density.
            // Make it narrower than the towers and you get candyfloss rather than a thunderhead.
            VortexPuff deck = VortexPuffLayout.PuffAt(0);
            VortexPuff towerLow = VortexPuffLayout.PuffAt(1);
            VortexPuff canopy = VortexPuffLayout.PuffAt(3);

            Assert.True(deck.DiscFraction > towerLow.DiscFraction);
            // "Flat below, billowing above" = the deck level is thin, the tower levels are thick.
            Assert.True(deck.BandFraction < towerLow.BandFraction * 0.5f);
            Assert.True(deck.DensityFraction > canopy.DensityFraction);
            Assert.True(VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Canopy)
                        > VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Deck));
            Assert.True(VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Deck)
                        > VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Tower));
        }

        [Fact]
        public void OutOfRangeIndicesDoNotThrow()
        {
            // This path runs every frame, so a miscount must not break loading the level.
            VortexPuff a = VortexPuffLayout.PuffAt(-1);
            Assert.True(VortexPuffLayout.EyeClearanceOf(-1) >= -1e-5f);
            Assert.Equal(VortexCloudLayer.Deck, a.Layer);

            VortexPuff b = VortexPuffLayout.PuffAt(VortexPuffLayout.PuffCount + 100);
            Assert.Equal(a.RadiusFraction, b.RadiusFraction, 6);
        }

        [Fact]
        public void MagnitudeHitsTheRequestedParticleBudget()
        {
            // Run the formula of §B-4 forwards and check that the count we asked for is
            // exactly what comes out.
            const float discRadius = 120f;
            const float rate = 20f;
            const float perSecond = 600f;

            float magnitude = VortexPuffLayout.MagnitudeFor(discRadius, rate, perSecond,
                                                            VortexPuffLayout.PuffCount);

            const float timeDelta = 1f / 60f;
            float area = 3.14159265f * discRadius * discRadius;
            float pps = timeDelta * magnitude * 0.01f * rate;
            float perFramePerPuff = area * pps;
            float perSecondTotal = perFramePerPuff * VortexPuffLayout.PuffCount / timeDelta;

            Assert.Equal(perSecond, perSecondTotal, 1);
        }

        [Fact]
        public void BrokenInputsProduceNoParticlesAtAll()
        {
            // Never create "a NaN particle count fills the sky".
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(0f, 20f, 600f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 0f, 600f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 20f, 0f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 20f, 600f, 0), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(float.NaN, 20f, 600f, 30), 6);
        }

        private static float TopOf(int column)
        {
            // The height of the canopy level stands in directly for the height of the column
            // (jitter included).
            return VortexPuffLayout
                .PuffAt(column * VortexPuffLayout.LevelsPerColumn + 3).HeightFraction;
        }
    }
}
