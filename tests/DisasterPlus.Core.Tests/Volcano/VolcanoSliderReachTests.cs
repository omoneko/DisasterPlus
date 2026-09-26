using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Report from the game (2026-08-22): "the volcano's 25.5 scale feels too small to me".
    ///
    /// ★★ <b>The cause was not the slider but the band.</b> The top of the multiplier was
    ///    reaching 255/55 ≒ 4.64 correctly, but the maximum in <see cref="VolcanoShape"/>
    ///    capped out first, so <b>above a displayed 9.2 the radius did not change by a
    ///    single metre however far the slider was moved</b>.
    ///
    ///    What is pinned here is "at any position on the slider, moving it changes the
    ///    size" —— so that a band where pushing does nothing is never built again.
    /// </summary>
    public class VolcanoSliderReachTests
    {
        private static readonly VolcanoForm[] AllForms =
        {
            VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome,
        };

        private static float RadiusAt(VolcanoForm form, int raw)
        {
            return VolcanoShape.RadiusFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form),
                                             VolcanoSizeScale.ScaleFor(raw)));
        }

        private static float HeightAt(VolcanoForm form, int raw)
        {
            // Stand it on land at 0 m above sea level (the widest possible headroom).
            return VolcanoShape.HeightFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form),
                                             VolcanoSizeScale.ScaleFor(raw)), 0f);
        }

        [Fact]
        public void TheTopOfTheSliderIsMuchBiggerThanTheDefault()
        {
            foreach (VolcanoForm form in AllForms)
            {
                float atDefault = RadiusAt(form, VolcanoSizeScale.AnchorRaw);
                float atTop = RadiusAt(form, VolcanoSizeScale.MaxRaw);

                // Before the band took effect there was only 2000/1200 = 1.67x.
                Assert.True(atTop >= atDefault * 2.5f,
                            form + " only grows from " + atDefault + " m to " + atTop + " m");
            }
        }

        [Fact]
        public void TheTopOfTheSliderIsTallerThanTheDefault()
        {
            foreach (VolcanoForm form in AllForms)
            {
                float atDefault = HeightAt(form, VolcanoSizeScale.AnchorRaw);
                float atTop = HeightAt(form, VolcanoSizeScale.MaxRaw);

                // A stratovolcano only had 700/600 = 1.17x. **That is what "too small" was.**
                //
                // ★★ <b>Height cannot be stretched by the full multiplier (4.64x).</b>
                //   The game's terrain ceiling is 1024 m above sea level and **a mod cannot
                //   raise it** (<c>UpliftSchedule.CeilingMetres</c>). The recommended 600 m
                //   for a stratovolcano times 4.64 is 2784 m —— a mountain that cannot exist.
                //   So all that is demanded here is that the top is clearly higher than the
                //   recommended value, and **the radius is what really carries "it got
                //   bigger"** (see the volume check below).
                Assert.True(atTop >= atDefault * 1.5f,
                            form + " only grows from " + atDefault + " m to " + atTop + " m");
            }
        }

        [Fact]
        public void TheTopOfTheSliderIsAnOrderOfMagnitudeMoreMountain()
        {
            // ★ What answers "too small" is the <b>volume</b> (r² h).
            //   Before the band was raised, a stratovolcano had only
            //   2000² × 700 / (1200² × 600) = 3.2x.
            foreach (VolcanoForm form in AllForms)
            {
                float rd = RadiusAt(form, VolcanoSizeScale.AnchorRaw);
                float hd = HeightAt(form, VolcanoSizeScale.AnchorRaw);
                float rt = RadiusAt(form, VolcanoSizeScale.MaxRaw);
                float ht = HeightAt(form, VolcanoSizeScale.MaxRaw);

                float ratio = (rt * rt * ht) / (rd * rd * hd);
                Assert.True(ratio >= 10f,
                            form + " at the top of the slider is only " + ratio
                            + "x the recommended volcano");
            }
        }

        [Fact]
        public void SomethingStillChangesAtTheTopOfTheSlider()
        {
            // ★★ **Never build a band where pushing changes nothing.**
            //
            //    One of the two saturating first is unavoidable —— the stratovolcano's
            //    height stops at the ceiling (1024 m) and the shield volcano's radius stops
            //    at the cost limit (6 km). What is forbidden is <b>both stopping at once</b>:
            //    the moment that happens, all the player can see is "the slider is broken".
            foreach (VolcanoForm form in AllForms)
            {
                bool radiusMoves = RadiusAt(form, VolcanoSizeScale.MaxRaw - 20)
                                   < RadiusAt(form, VolcanoSizeScale.MaxRaw);
                bool heightMoves = HeightAt(form, VolcanoSizeScale.MaxRaw - 20)
                                   < HeightAt(form, VolcanoSizeScale.MaxRaw);

                Assert.True(radiusMoves || heightMoves,
                            form + " stops changing entirely before the top of the slider");
            }
        }

        [Fact]
        public void NothingReachesTheGamesTerrainCeiling()
        {
            // The ceiling (1024 m) cannot be raised from a mod. If the band went past it,
            // "it does not reach the height you set" would become the default behaviour.
            foreach (VolcanoForm form in AllForms)
            {
                Assert.True(VolcanoShape.MaxHeightOf(form) < UpliftSchedule.CeilingMetres,
                            form + " can be asked for a summit above the terrain ceiling");
            }
        }

        [Fact]
        public void TheSupereruptionStaysInsideTheCostCeiling()
        {
            // Even for the largest mountain, the inflation and caldera rectangles must not
            // exceed the cost limit.
            foreach (VolcanoForm form in AllForms)
            {
                float biggest = VolcanoShape.MaxRadiusOf(form);
                Assert.True(SuperEruption.InflationRadiusMetres(biggest)
                            <= SuperEruption.MaxRadiusMetres);
                Assert.True(SuperEruption.CalderaRadiusMetres(biggest)
                            <= SuperEruption.MaxRadiusMetres);
            }
        }

        [Fact]
        public void OnlyTheVeryTopOfTheSliderGetsASupereruption()
        {
            // Drop the slider by one notch and it must go back to an ordinary eruption
            // (the owner's request: "only at 25.5").
            Assert.True(SuperEruption.IsSuper(VolcanoSizeScale.MaxRaw));
            Assert.False(SuperEruption.IsSuper(VolcanoSizeScale.MaxRaw - 1));
            Assert.False(SuperEruption.IsSuper(VolcanoSizeScale.AnchorRaw));
        }
    }
}
