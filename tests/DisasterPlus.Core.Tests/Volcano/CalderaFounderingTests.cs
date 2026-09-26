using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// In-game report (2026-08-22): "when a caldera forms, surely the mountain edifice
    /// drops away massively and there is a huge explosion...? Please reproduce the correct
    /// phenomenon for the formation."
    ///
    /// ★★ <b>What is pinned here is that no mountain is left behind.</b>
    ///    The first implementation subtracted the depth from the current ground, so
    ///    cone +1000 m − depth 900 m = <b>a 100 m stump was left at the summit</b>.
    ///    That is not a "collapse"; it is a picture of "a trench dug around the mountain".
    ///
    ///    The cross-section is built from the real cone
    ///    (<see cref="VolcanoCrater.ProfileAt"/>) —— if you inspect it with a
    ///    "plausible-looking mountain" of your own making, this would pass even when it
    ///    disagrees with the actual shape.
    /// </summary>
    public class CalderaFounderingTests
    {
        private const float ConeRadius = 2928f;
        private const float ConeHeight = 1000f;
        private const float Ground = 120f;

        private static readonly VolcanoRelief Flat =
            VolcanoRelief.For(VolcanoForm.Strato, 12345u, 0f);

        private static float CalderaRadius
        {
            get { return SuperEruption.CalderaRadiusMetres(ConeRadius); }
        }

        private static float CalderaDepth
        {
            get { return SuperEruption.CalderaDepthMetres(ConeHeight); }
        }

        /// <summary>The ground before the eruption (m). A cone sitting on flat land.</summary>
        private static float BaseAt(float distance)
        {
            return Ground + VolcanoCrater.ProfileAt(Flat, distance, 0f, ConeRadius, ConeHeight);
        }

        /// <summary>The ground after it has finished dropping (m).</summary>
        private static float FinalAt(float distance)
        {
            float bowl = SuperEruption.BowlProfileAt(distance, CalderaRadius, CalderaDepth);
            return BaseAt(distance) + SuperEruption.FounderDropAt(bowl, BaseAt(distance), Ground);
        }

        [Fact]
        public void TheCalderaIsWiderThanTheMountainItSwallows()
        {
            Assert.True(CalderaRadius > ConeRadius,
                        "the caldera (" + CalderaRadius + " m) does not reach past the cone ("
                        + ConeRadius + " m)");
        }

        [Fact]
        public void NoStumpOfTheMountainIsLeftBehind()
        {
            // ★★ This is exactly the owner's point. Nowhere within the area the mountain
            //    occupied may anything remain higher than the original ground.
            for (int i = 0; i <= 200; i++)
            {
                float d = ConeRadius * i / 200f;
                float after = FinalAt(d);

                Assert.True(after < Ground,
                            "a stump of the mountain is left at " + d + " m: the ground is "
                            + after + " m, the original ground was " + Ground + " m");
            }
        }

        [Fact]
        public void TheFloorIsFlatAndBelowTheOriginalGround()
        {
            float floorEdge = CalderaRadius * SuperEruption.FloorFraction * 0.9f;

            float centre = FinalAt(0f);
            float mid = FinalAt(floorEdge);

            Assert.Equal(centre, mid, 2);
            Assert.Equal(Ground - CalderaDepth, centre, 2);
        }

        [Fact]
        public void TheSummitFallsFurtherThanTheCalderaIsDeep()
        {
            // The summit falls by "the depth plus the height of the mountain". If it only
            // falls by the depth, we are back to the subtraction.
            float fall = BaseAt(0f) - FinalAt(0f);

            Assert.True(fall > CalderaDepth + ConeHeight * 0.5f,
                        "the summit only fell " + fall + " m; the caldera is "
                        + CalderaDepth + " m deep and the mountain was " + ConeHeight + " m tall");
        }

        [Fact]
        public void NothingOutsideTheCalderaMoves()
        {
            foreach (float d in new[] { CalderaRadius, CalderaRadius * 1.5f, CalderaRadius * 4f })
            {
                Assert.Equal(BaseAt(d), FinalAt(d), 3);
            }
        }

        [Fact]
        public void TheGroundOnlyEverGoesDown()
        {
            // There must not be a single path by which the collapse raises the ground.
            for (int i = 0; i <= 400; i++)
            {
                float d = CalderaRadius * 1.2f * i / 400f;
                Assert.True(FinalAt(d) <= BaseAt(d) + 1e-3f,
                            "the collapse raised the ground at " + d + " m");
            }
        }

        [Fact]
        public void ASlopingSiteNeverGetsItsRimPushedUp()
        {
            // The reference height is a single point at the centre, so the real ground at the
            // outer rim can be lower than it. Raising that "up to the target" would make the
            // rim bulge ——> it must be clamped at 0.
            float lowOutskirt = Ground - 60f;
            float bowlAtRim = SuperEruption.BowlProfileAt(CalderaRadius * 0.98f,
                                                          CalderaRadius, CalderaDepth);

            Assert.Equal(0f, SuperEruption.FounderDropAt(bowlAtRim, lowOutskirt, Ground), 4);
        }

        [Fact]
        public void BrokenInputMovesNoGround()
        {
            Assert.Equal(0f, SuperEruption.FounderDropAt(float.NaN, 100f, 100f), 4);
            Assert.Equal(0f, SuperEruption.FounderDropAt(-100f, float.NaN, 100f), 4);
            Assert.Equal(0f, SuperEruption.FounderDropAt(-100f, 100f, float.NaN), 4);
        }
    }
}
