using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class VolcanoShapeTests
    {
        private static readonly VolcanoForm[] AllForms =
            new VolcanoForm[] { VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome };

        [Fact]
        public void AllThreeFormsPeakAtTheCentre()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                Assert.Equal(400f, VolcanoShape.ProfileAt(AllForms[i], 0f, 1000f, 400f), 3);
            }
        }

        [Fact]
        public void NothingIsRaisedBeyondTheRadius()
        {
            // If even 1 mm is left outside the radius, the UpdateArea rectangle grows without bound.
            for (int i = 0; i < AllForms.Length; i++)
            {
                Assert.Equal(0f, VolcanoShape.ProfileAt(AllForms[i], 1000f, 1000f, 400f), 3);
                Assert.Equal(0f, VolcanoShape.ProfileAt(AllForms[i], 1500f, 1000f, 400f), 3);
            }
        }

        [Fact]
        public void TheProfileNeverGoesBackUp()
        {
            // Unless it is monotonically non-increasing, a ring-shaped ridge forms halfway
            // up the mountain.
            for (int i = 0; i < AllForms.Length; i++)
            {
                float previous = float.MaxValue;
                for (float d = 0f; d <= 1000f; d += 5f)
                {
                    float h = VolcanoShape.ProfileAt(AllForms[i], d, 1000f, 400f);
                    Assert.True(h <= previous + 0.001f,
                        AllForms[i] + " rises again at d=" + d);
                    previous = h;
                }
            }
        }

        [Fact]
        public void TheShieldMatchesTheProfileMakeCraterWouldHaveDrawn()
        {
            // Pins down, verbatim, three points from the measured table of §C-8
            // (raiseEdges:false, −H as the depth). Drift here and the premise
            // "the same shape as a single MakeCrater" becomes a lie.
            const float r = 1000f;
            const float h = 100f;
            Assert.Equal(100.00f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0f, r, h), 2);
            Assert.Equal(95.97f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.375f * r, r, h), 1);
            Assert.Equal(36.23f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.750f * r, r, h), 1);

            // The boundary between the inner and outer branches (0.73R) must be continuous
            // (to within an error of 7e-4).
            float inner = VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.7299f * r, r, h);
            float outer = VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.7301f * r, r, h);
            Assert.True(System.Math.Abs(inner - outer) < 0.01f,
                "the shield profile jumps at the 0.73R branch: " + inner + " vs " + outer);
        }

        [Fact]
        public void EachFormHasItsOwnSignature()
        {
            const float r = 1000f;
            const float h = 400f;

            // Shield: a flat top. Even at half the radius more than 80 per cent is left.
            Assert.True(VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.5f * r, r, h) > 0.80f * h);
            // Strato: a straight line. Exactly half at half the radius.
            Assert.Equal(0.5f * h, VolcanoShape.ProfileAt(VolcanoForm.Strato, 0.5f * r, r, h), 2);
            // Dome: a quarter at 0.7071R.
            Assert.Equal(0.25f * h, VolcanoShape.ProfileAt(VolcanoForm.Dome, 0.70711f * r, r, h), 1);

            // "The dome is steep" comes not from the profile but from its **small radius**
            // (§C-9). At the default values the mean gradient must go
            // shield < strato < dome.
            float shield = VolcanoShape.DefaultHeightOf(VolcanoForm.Shield)
                         / VolcanoShape.DefaultRadiusOf(VolcanoForm.Shield);
            float strato = VolcanoShape.DefaultHeightOf(VolcanoForm.Strato)
                         / VolcanoShape.DefaultRadiusOf(VolcanoForm.Strato);
            float dome = VolcanoShape.DefaultHeightOf(VolcanoForm.Dome)
                       / VolcanoShape.DefaultRadiusOf(VolcanoForm.Dome);
            Assert.True(shield < strato, "shield must be the gentlest");
            Assert.True(strato < dome, "the dome must be the steepest");
        }

        [Fact]
        public void TheDomeRadiusIsNeverAllowedBelowTwoHundredAndFiftyMetres()
        {
            // ★ §C-9: 250 m is 8 raw cells on each side. Below that it is not a "mountain"
            //   but noise in the ground.
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.MinRadiusOf(VolcanoForm.Dome), 3);
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.RadiusFor(VolcanoForm.Dome, 10f), 3);
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.RadiusFor(VolcanoForm.Dome, -5f), 3);
            Assert.True(VolcanoShape.RadiusFor(VolcanoForm.Dome, 100000f)
                        <= VolcanoShape.MaxRadiusOf(VolcanoForm.Dome));
        }

        [Fact]
        public void TheSummitNeverReachesTheTerrainCeiling()
        {
            // ★ §C-10: hitting the ceiling raises no exception; the summit silently becomes
            //   a flat plateau. The greatest height feature no. 5 writes is exactly
            //   base + H (the crater rim is at H, and not 1 mm is written above it —— see
            //   the VolcanoCrater class doc).
            const float baseHeight = 900f;
            float h = VolcanoShape.HeightFor(VolcanoForm.Strato, 700f, baseHeight);
            Assert.True(baseHeight + h <= VolcanoShape.MaxTerrainMetres,
                "the summit would be clipped at " + (baseHeight + h));
            Assert.True(VolcanoShape.HeightWasLimitedByCeiling(VolcanoForm.Strato, 700f, baseHeight));

            // On flat ground at a sea level of 40 m, 700 m passes straight through
            // (the 983.98 m of headroom in §C-10).
            Assert.Equal(700f, VolcanoShape.HeightFor(VolcanoForm.Strato, 700f, 40f), 2);
            Assert.False(VolcanoShape.HeightWasLimitedByCeiling(VolcanoForm.Strato, 700f, 40f));
        }

        [Fact]
        public void RadiusAndHeightAreClampedToTheDeclaredRange()
        {
            // The .cgs is a public contract and can be edited by hand. Out-of-range values
            // are clamped rather than discarded.
            for (int i = 0; i < AllForms.Length; i++)
            {
                var f = AllForms[i];
                Assert.InRange(VolcanoShape.RadiusFor(f, -1f),
                               VolcanoShape.MinRadiusOf(f), VolcanoShape.MaxRadiusOf(f));
                Assert.InRange(VolcanoShape.RadiusFor(f, 999999f),
                               VolcanoShape.MinRadiusOf(f), VolcanoShape.MaxRadiusOf(f));
                Assert.InRange(VolcanoShape.HeightFor(f, -1f, 40f),
                               VolcanoShape.MinHeightOf(f), VolcanoShape.MaxHeightOf(f));
                Assert.InRange(VolcanoShape.HeightFor(f, 999999f, 40f),
                               VolcanoShape.MinHeightOf(f), VolcanoShape.MaxHeightOf(f));
            }
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                var f = AllForms[i];
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, float.NaN, 1000f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, float.NaN, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 1000f, float.NaN), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, -1f, 1000f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 0f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 1000f, 0f), 4);
            }
            Assert.Equal(0f, VolcanoShape.HeadroomMetres(float.NaN), 4);
        }

        [Fact]
        public void AnOutOfRangeSettingValueFallsBackToTheDefaultForm()
        {
            // The setting value is a public contract stored in the .cgs. The numbers are
            // never renumbered.
            Assert.Equal(VolcanoForm.Shield, VolcanoShape.FormOf(0));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(1));
            Assert.Equal(VolcanoForm.Dome, VolcanoShape.FormOf(2));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(-1));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(3));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(int.MaxValue));
        }

        [Fact]
        public void TheCraterIsSmallerAndShallowerThanTheMountain()
        {
            for (float h = 50f; h <= 700f; h += 25f)
            {
                Assert.True(VolcanoShape.CraterDepthOf(h) < h,
                    "the crater would punch through the mountain at H=" + h);
                Assert.True(VolcanoShape.CraterDepthOf(h) > 0f);
            }
            for (float r = 250f; r <= 3000f; r += 50f)
            {
                Assert.True(VolcanoShape.CraterRadiusOf(r) < r);
                // The crater rectangle must not span more than 128 cells (2048 m).
                Assert.True(VolcanoShape.CraterRadiusOf(r) <= 400f);
            }
            Assert.Equal(0f, VolcanoShape.CraterDepthOf(float.NaN), 4);
            Assert.Equal(0f, VolcanoShape.CraterRadiusOf(float.NaN), 4);

            // ★ On a low mountain the "45 % of the height" cap takes effect instead
            //   (it takes priority over the 10 m floor). Without this line a 15 m mountain
            //   gets a 10 m hole and the crater floor punches through to the original ground.
            Assert.True(VolcanoShape.CraterDepthOf(15f) <= 15f * 0.45f + 0.001f);
            Assert.True(VolcanoShape.CraterDepthOf(15f) > 0f);
        }
    }
}
