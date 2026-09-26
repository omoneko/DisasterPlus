using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The relief of the mountainside. **What we pin down are two hard constraints and
    /// "it must not be axisymmetric"**. The look of the relief itself is drawn by
    /// tools/VolcanoPreview and checked by eye.
    /// </summary>
    public class VolcanoReliefTests
    {
        private static readonly VolcanoForm[] AllForms =
            new VolcanoForm[] { VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome };

        private const uint Seed = 0x1A2B3C4Du;

        [Fact]
        public void StrengthZeroIsExactlyTheProfileWeShipToday()
        {
            // ★★ Whoever sets the option to 0 gets not "a mountain with less relief" but
            //    **exactly today's output**. One bit of drift makes the promise that you
            //    can go back a lie.
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, 0f);

                for (float dz = -r - 32f; dz <= r + 32f; dz += 16f)
                {
                    for (float dx = -r - 32f; dx <= r + 32f; dx += 16f)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        Assert.Equal(VolcanoShape.ProfileAt(form, d, r, h),
                                     relief.ProfileAt(dx, dz, r, h));
                    }
                }
            }
        }

        [Fact]
        public void NegativeAndNaNStrengthFallBackToTheSmoothCone()
        {
            // The .cgs can be edited by hand. Do not discard the value silently; fall back
            // to the safest side (today's shape).
            foreach (float bad in new float[] { -1f, float.NaN })
            {
                var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, bad);
                Assert.Equal(0f, relief.StrengthUnit, 6);
                Assert.Equal(VolcanoShape.ProfileAt(VolcanoForm.Strato, 400f, 1200f, 600f),
                             relief.ProfileAt(400f, 0f, 1200f, 600f));
            }
        }

        [Fact]
        public void StrengthIsClampedToTheDeclaredCeiling()
        {
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 99f);
            Assert.Equal(VolcanoRelief.MaxStrengthUnit, relief.StrengthUnit, 5);
        }

        [Fact]
        public void NothingIsRaisedBeyondTheRadius()
        {
            // ★★ Constraint 1. If even 1 mm is left outside the radius we end up raising a
            //    cell the preparation never reached, the roads push it back on every flush,
            //    and a flat trench is left inside the mountain (design doc §1.2).
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, VolcanoRelief.MaxStrengthUnit);

                // Walk every cell of the 16 m grid. **The test is made on that cell's actual
                // distance** —— a point built in polar coordinates can land slightly inside
                // the radius through rounding, and that is not "outside the radius".
                for (float dz = -r - 64f; dz <= r + 64f; dz += 16f)
                {
                    for (float dx = -r - 64f; dx <= r + 64f; dx += 16f)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d < r) continue;
                        Assert.Equal(0f, relief.ProfileAt(dx, dz, r, h));
                    }
                }

                // Not 1 mm may be left just outside the circumference either.
                for (int a = 0; a < 720; a++)
                {
                    double th = 2.0 * Math.PI * a / 720.0;
                    foreach (float d in new float[] { r + 1f, r + 16f, r * 2f })
                    {
                        Assert.Equal(0f, relief.ProfileAt((float)(Math.Cos(th) * d),
                                                          (float)(Math.Sin(th) * d), r, h));
                    }
                }
            }
        }

        [Fact]
        public void TheProfileNeverExceedsTheFinalHeight()
        {
            // ★★ Constraint 2. HeightFor / HeadroomMetres and the raw 1024 m ceiling (§C-10)
            //    all reason in terms of H. The promise is **not to exceed the H we were
            //    given**; passing a virtual summit rebuilt to allow for the crater is the
            //    caller's business (VolcanoCrater always cuts it off).
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.MaxRadiusOf(form);
                float h = VolcanoShape.MaxHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, VolcanoRelief.MaxStrengthUnit);

                for (float dz = -r; dz <= r; dz += 16f)
                {
                    for (float dx = -r; dx <= r; dx += 16f)
                    {
                        float v = relief.ProfileAt(dx, dz, r, h);
                        Assert.InRange(v, 0f, h);
                    }
                }
            }
        }

        [Fact]
        public void TheSummitIsStillExactlyTheFinalHeight()
        {
            // If the summit drifts, the crater rim no longer reaches the mountain height H
            //    (VolcanoCrater.SummitScale assumes this is exactly H).
            for (int i = 0; i < AllForms.Length; i++)
            {
                var relief = VolcanoRelief.For(AllForms[i], Seed, 1f);
                Assert.Equal(400f, relief.ProfileAt(0f, 0f, 1000f, 400f), 4);
            }
        }

        [Fact]
        public void TheShapeIsNoLongerAxisymmetric()
        {
            // ★★ The task itself. This pins down that we have broken the "cone turned on a
            //    lathe", where **the same distance gives the same height whatever the
            //    bearing**.
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float min = float.MaxValue, max = float.MinValue;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float d = 0.55f * r;
                float v = relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h);
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float smooth = VolcanoShape.ProfileAt(VolcanoForm.Strato, 0.55f * r, r, h);
            Assert.True(max - min > 0.15f * smooth,
                "the flank is still axisymmetric: spread " + (max - min) + " m at 0.55R");
        }

        [Fact]
        public void TheFootprintIsNotACircle()
        {
            // If the foot is a perfect circle it looks less like "a mountain was placed"
            // and more like "a disc was placed".
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float shortest = float.MaxValue, longest = 0f;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float reach = 0f;
                for (float d = 0f; d <= r; d += 4f)
                {
                    if (relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h) > 0f)
                    {
                        reach = d;
                    }
                }
                if (reach < shortest) shortest = reach;
                if (reach > longest) longest = reach;
            }

            Assert.True(longest <= r, "the footprint reaches " + longest + " m, past R = " + r);
            Assert.True(longest - shortest > 0.04f * r,
                "the footprint is still a circle: " + shortest + " m .. " + longest + " m");
        }

        [Fact]
        public void TheGulliesAreShallowNearTheSummitAndDeepenDownslope()
        {
            // The direction of the "grooves running from top to bottom" itself. Reverse it
            // and you get a different mountain, one carved at the summit.
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            Assert.True(RelativeSpread(relief, r, h, 0.15f) < RelativeSpread(relief, r, h, 0.60f),
                "the relief is not deeper downslope");
        }

        [Fact]
        public void TheAzimuthalReliefIsSuppressedWhereItWouldAliasOnTheSixteenMetreGrid()
        {
            // ★ The closer to the summit, the shorter the azimuthal wavelength
            //   (circumference / number of gullies). Unless it is killed where it bites into
            //   the 16 m grid, the area around the summit becomes a chequerboard rather
            //   than relief.
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, VolcanoRelief.MaxStrengthUnit);

            // At a radius of 48 m (= 3 cells) there must be almost no azimuthal relief.
            Assert.True(RelativeSpread(relief, r, h, 48f / r) < 0.02f,
                "the summit ring still carries azimuthal relief and will alias");
        }

        [Fact]
        public void TheShieldStaysCloserToTheSmoothConeThanTheStratovolcano()
        {
            // The character of each form. A shield volcano really is smoother, and radial
            // gullies belong to the stratovolcano.
            var shield = VolcanoRelief.For(VolcanoForm.Shield, Seed, 1f);
            var strato = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float shieldSpread = RelativeSpread(shield, VolcanoShape.DefaultRadiusOf(VolcanoForm.Shield),
                                                VolcanoShape.DefaultHeightOf(VolcanoForm.Shield), 0.55f);
            float stratoSpread = RelativeSpread(strato, VolcanoShape.DefaultRadiusOf(VolcanoForm.Strato),
                                                VolcanoShape.DefaultHeightOf(VolcanoForm.Strato), 0.55f);

            Assert.True(shieldSpread < stratoSpread,
                "the shield is not the smoothest: " + shieldSpread + " vs " + stratoSpread);
        }

        [Fact]
        public void NoWavelengthGoesBelowTheGridsLimit()
        {
            // On a 16 m grid, a component below 64 m turns into noise rather than relief.
            for (int i = 0; i < AllForms.Length; i++)
            {
                var relief = VolcanoRelief.For(AllForms[i], Seed, 1f);
                Assert.True(relief.FineWavelengthMetres >= VolcanoRelief.MinWavelengthMetres,
                    AllForms[i] + " uses " + relief.FineWavelengthMetres + " m");
                Assert.True(relief.FineWavelengthMetres
                            >= 4f * VolcanoShape.RawCellSizeMetres);

                // ★ The 4th octave (the finest skin) must not go below the same floor either.
                Assert.True(relief.MicroWavelengthMetres >= VolcanoRelief.MinWavelengthMetres,
                    AllForms[i] + " micro octave uses " + relief.MicroWavelengthMetres + " m");
                Assert.True(relief.MicroWavelengthMetres
                            >= 4f * VolcanoShape.RawCellSizeMetres);
            }
        }

        [Fact]
        public void TheLowerFlankCarriesMoreChannelsThanTheMainGulliesAlone()
        {
            // The answer to "only the lines of the big valleys". Going round the
            // circumference at the foot and counting the valleys, the count must be
            // **greater** than the number of main gullies, or not one fine channel is
            // taking effect.
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            Assert.True(TroughsAround(relief, r, h, 0.85f) > relief.GullyCount,
                "the lower flank has no finer channels than the main gullies");
        }

        [Fact]
        public void SmallConesGetNoRillsAtAll()
        {
            // On a lava dome (default R = 350 m) the azimuthal wavelength is insufficient
            // everywhere. **Do not silently turn "there are none" into "there are some".**
            var relief = VolcanoRelief.For(VolcanoForm.Dome, Seed, 1f);
            float r = VolcanoShape.DefaultRadiusOf(VolcanoForm.Dome);

            // ★ Because of the azimuthal floor, the smaller the mountain the thinner the
            //   outer ring the rills fit into. If the ring is too thin it looks not like
            //   grooves but like "a ring of dimples lined up at the foot", so unless we can
            //   take the outer 30 per cent of the slope we emit none at all.
            Assert.False(relief.RillsFitOn(r),
                "the lava dome claims rills the 16 m grid cannot carry as lines");

            // ★ The radius at which they begin is decided solely by the azimuthal floor
            //   (96 m) and the order of the rills. It must not creep in further than the
            //   16 m grid can carry.
            Assert.True(relief.RillOnsetRadiusMetres
                        >= VolcanoRelief.MinAzimuthWavelengthMetres * relief.RillCount
                           / 6.2831853f,
                "the onset radius is closer to the summit than the azimuthal floor allows");

            // On a stratovolcano (default R = 1200 m) they do fit. This is not
            // "they never appear on any mountain".
            var strato = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            Assert.True(strato.RillsFitOn(VolcanoShape.DefaultRadiusOf(VolcanoForm.Strato)),
                "the stratovolcano cannot carry rills either, so the tier is pointless");
        }

        /// <summary>The number of valleys (the number of minima) along the circumference at
        /// radius <paramref name="fraction"/>R.</summary>
        private static int TroughsAround(VolcanoRelief relief, float r, float h, float fraction)
        {
            float ring = fraction * r;
            int reversals = 0;
            int sign = 0;
            float previous = 0f;

            for (int a = 0; a < 1440; a++)
            {
                double th = 2.0 * Math.PI * a / 1440.0;
                float v = relief.ProfileAt((float)(Math.Cos(th) * ring),
                                           (float)(Math.Sin(th) * ring), r, h);
                if (a > 0)
                {
                    float delta = v - previous;
                    int next = delta > 0f ? 1 : (delta < 0f ? -1 : sign);
                    if (sign != 0 && next != 0 && next != sign) reversals++;
                    sign = next;
                }
                previous = v;
            }

            return reversals / 2;
        }

        [Fact]
        public void TheSameSeedAlwaysGivesTheSameMountain()
        {
            // Rebuilding at the same spot must grow the same mountain (reproducibility for
            // save/load and for the tests).
            var a = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            var b = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            var other = VolcanoRelief.For(VolcanoForm.Strato, Seed ^ 0xFFFFu, 1f);

            bool differs = false;
            for (float dz = -1200f; dz <= 1200f; dz += 64f)
            {
                for (float dx = -1200f; dx <= 1200f; dx += 64f)
                {
                    float va = a.ProfileAt(dx, dz, 1200f, 600f);
                    Assert.Equal(va, b.ProfileAt(dx, dz, 1200f, 600f));
                    if (Math.Abs(va - other.ProfileAt(dx, dz, 1200f, 600f)) > 0.5f) differs = true;
                }
            }
            Assert.True(differs, "a different seed produced the same mountain");
        }

        [Fact]
        public void BadInputIsZeroAndNeverNaN()
        {
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            Assert.Equal(0f, relief.ProfileAt(float.NaN, 0f, 1200f, 600f));
            Assert.Equal(0f, relief.ProfileAt(0f, float.NaN, 1200f, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, float.NaN, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 1200f, float.NaN));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 0f, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 1200f, 0f));
        }

        /// <summary>The spread of heights around the circumference at that distance
        /// (as a ratio to the height of the smooth cone).</summary>
        private static float RelativeSpread(VolcanoRelief relief, float r, float h, float t)
        {
            float d = t * r;
            float min = float.MaxValue, max = float.MinValue;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float v = relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h);
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float smooth = VolcanoShape.ProfileAt(VolcanoForm.Strato, d, r, h);
            if (!(smooth > 0f)) return 0f;
            return (max - min) / smooth;
        }

        [Fact]
        public void TheSeedComesFromTheDeterministicHashNotSystemRandom()
        {
            // Core is engine-free and must produce the same values on net35 and net8.0.
            // Pin down how the seed is built (DeterministicRandom.Hash(round(X), round(Z))).
            uint seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));
            var a = VolcanoRelief.For(VolcanoForm.Strato, seed, 1f);
            var b = VolcanoRelief.For(VolcanoForm.Strato,
                                      DeterministicRandom.Hash(275u, unchecked((uint)(-283))), 1f);
            Assert.Equal(a.ProfileAt(300f, 200f, 1200f, 600f),
                         b.ProfileAt(300f, 200f, 1200f, 600f));
        }
    }
}
