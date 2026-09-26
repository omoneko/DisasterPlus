using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// In-game report (2026-08-22): "the animation of the eruption plume is still far from
    /// realistic. I would like a more natural, chaotic smoke animation instead of a
    /// geometric one."
    ///
    /// ★★ <b>"Chaos" can be tested.</b> "Looks natural" cannot be measured, but
    ///    <b>not being geometric</b> can —— are the parcels lined up at the same heights,
    ///    do they move over time, does the same shape not come back with a period.
    /// </summary>
    public class PlumeParcelsTests
    {
        private const float Vent = 351f;
        private const float Height = 1800f;
        private const uint Seed = 20260822u;

        private static PlumeParcel P(int i, float t)
        {
            return PlumeParcels.At(i, t, Vent, Height, 9f, 0f, Seed);
        }

        [Fact]
        public void TheParcelsAreNotStackedOnASmallNumberOfShelves()
        {
            // ★★ This was what "geometric" really meant —— the previous implementation was
            //    9 stacked discs, and the parcels only ever sat at those 9 heights.
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                seen.Add((int)(P(i, 40f).Y / 25f));   // in 25 m steps
            }

            Assert.True(seen.Count > 40,
                        "the parcels only occupy " + seen.Count + " distinct heights");
        }

        [Fact]
        public void EveryParcelMovesBetweenFrames()
        {
            // Every parcel must move even over 1/30 of a second. A stationary parcel looks
            // like "smoke stuck to the screen".
            int still = 0;
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                PlumeParcel a = P(i, 40f);
                PlumeParcel b = P(i, 40f + 1f / 30f);
                if (a.X == b.X && a.Y == b.Y && a.Z == b.Z) still++;
            }

            Assert.Equal(0, still);
        }

        [Fact]
        public void TheCrowdNeverRepeatsItselfWithinOneLifetime()
        {
            // The turbulence is built from eddies of differing periods. If it returned to
            // the same state after a single period, the eye could follow the repetition.
            float worst = float.MaxValue;
            for (int step = 1; step <= 12; step++)
            {
                float dt = PlumeParcels.TurbulenceBaseSeconds * step / 12f;
                float total = 0f;
                for (int i = 0; i < 120; i++)
                {
                    PlumeParcel a = P(i, 60f);
                    PlumeParcel b = P(i, 60f + dt);
                    float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                    total += dx * dx + dy * dy + dz * dz;
                }
                if (total < worst) worst = total;
            }

            Assert.True(worst > 1f, "the crowd returns to the same shape within one period");
        }

        [Fact]
        public void TheColumnIsTallerThanItIsWide()
        {
            // It must be a column, not a blob. **At first the flare was applied twice and
            // it became a cloud that filled the screen** (spotted with tools/PlumePreview).
            float top = 0f, halfWidth = 0f;
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                PlumeParcel p = P(i, 47f);
                if (p.Alpha <= 0.02f) continue;

                if (p.Y + p.RadiusMetres > top) top = p.Y + p.RadiusMetres;
                float ax = p.X < 0f ? -p.X : p.X;
                if (ax + p.RadiusMetres > halfWidth) halfWidth = ax + p.RadiusMetres;
            }

            Assert.True(top > halfWidth,
                        "the plume is " + halfWidth * 2f + " m wide but only " + top + " m tall");
        }

        [Fact]
        public void TheStemIsNarrowAndTheUmbrellaIsBroad()
        {
            // The profile must be the textbook one (gas thrust → convection → umbrella).
            float stem = PlumeParcels.ColumnRadiusAt(0.05f, Vent, Height);
            float middle = PlumeParcels.ColumnRadiusAt(0.5f, Vent, Height);
            float umbrella = PlumeParcels.ColumnRadiusAt(1f, Vent, Height);

            Assert.True(stem < middle, "the stem (" + stem + ") is not narrower than the middle");
            Assert.True(umbrella > middle * 2f,
                        "the umbrella (" + umbrella + ") does not flare out over the column ("
                        + middle + ")");
        }

        [Fact]
        public void TheColumnOnlyEverGetsWiderGoingUp()
        {
            float previous = -1f;
            for (int i = 0; i <= 200; i++)
            {
                float r = PlumeParcels.ColumnRadiusAt(i / 200f, Vent, Height);
                Assert.True(r >= previous - 1e-3f, "the column narrows at " + (i / 200f));
                previous = r;
            }
        }

        [Fact]
        public void EveryParcelFadesInAndOut()
        {
            // A parcel that disappears while still at full opacity looks as if it has
            // "popped out of existence" at that one spot.
            for (int i = 0; i < 40; i++)
            {
                float maxAlpha = 0f, minAlpha = 1f;
                for (int k = 0; k <= 60; k++)
                {
                    float a = P(i, k * PlumeParcels.LifeSeconds / 60f).Alpha;
                    if (a > maxAlpha) maxAlpha = a;
                    if (a < minAlpha) minAlpha = a;
                }

                Assert.True(maxAlpha > 0.5f, "parcel " + i + " is never visible");
                Assert.True(minAlpha < 0.1f, "parcel " + i + " never fades out");
            }
        }

        [Fact]
        public void TheAshIsDarkAtTheVentAndBrightAtTheTop()
        {
            float low = 0f, high = 0f;
            int lowCount = 0, highCount = 0;

            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                PlumeParcel p = P(i, 47f);
                if (p.Y < Height * 0.15f) { low += p.Brightness; lowCount++; }
                if (p.Y > Height * 0.8f) { high += p.Brightness; highCount++; }
            }

            Assert.True(lowCount > 0 && highCount > 0);
            Assert.True(low / lowCount < high / highCount,
                        "the ash near the vent is not darker than the umbrella");
        }

        [Fact]
        public void TheSameVolcanoAlwaysLooksTheSame()
        {
            // DeterministicRandom only (this mod's discipline for randomness).
            for (int i = 0; i < 50; i++)
            {
                PlumeParcel a = P(i, 33.25f);
                PlumeParcel b = P(i, 33.25f);
                Assert.Equal(a.X, b.X, 5);
                Assert.Equal(a.Y, b.Y, 5);
                Assert.Equal(a.Z, b.Z, 5);
            }
        }

        [Fact]
        public void BrokenInputStillProducesADrawableParcel()
        {
            foreach (float t in new[] { float.NaN, -50f, 0f })
            {
                foreach (float v in new[] { float.NaN, -5f, 0f, 351f })
                {
                    PlumeParcel p = PlumeParcels.At(0, t, v, float.NaN, float.NaN, 0f, Seed);

                    Assert.False(float.IsNaN(p.X));
                    Assert.False(float.IsNaN(p.Y));
                    Assert.False(float.IsNaN(p.Z));
                    Assert.True(p.RadiusMetres > 0f);
                    Assert.InRange(p.Alpha, 0f, 1f);
                    Assert.InRange(p.Brightness, 0f, 1f);
                }
            }
        }

        [Fact]
        public void AnOutOfRangeIndexStillReturnsAParcel()
        {
            PlumeParcel p = PlumeParcels.At(99999, 40f, Vent, Height, 0f, 0f, Seed);
            Assert.True(p.RadiusMetres > 0f);
        }

        [Fact]
        public void TheWindLeansThePlumeDownwindAndOnlyAtTheTop()
        {
            // Wind shear: the higher it is the further it is carried. If the base moves with
            // it, the whole column is simply being translated.
            PlumeParcel[] still = new PlumeParcel[PlumeParcels.Count];
            PlumeParcel[] blown = new PlumeParcel[PlumeParcels.Count];
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                still[i] = PlumeParcels.At(i, 47f, Vent, Height, 0f, 0f, Seed);
                blown[i] = PlumeParcels.At(i, 47f, Vent, Height, 24f, 0f, Seed);
            }

            float lowShift = 0f, highShift = 0f;
            int lowCount = 0, highCount = 0;
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                float shift = blown[i].X - still[i].X;
                if (still[i].Y < Height * 0.15f) { lowShift += shift; lowCount++; }
                if (still[i].Y > Height * 0.8f) { highShift += shift; highCount++; }
            }

            Assert.True(lowCount > 0 && highCount > 0);
            Assert.True(highShift / highCount > lowShift / lowCount * 4f,
                        "the wind moved the whole column instead of shearing it");
        }
    }
}
