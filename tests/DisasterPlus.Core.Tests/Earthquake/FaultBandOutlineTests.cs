using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// The outline of the fault band drawn on the map. **Agreeing with
    /// <see cref="FaultBand.Contains"/>** is the whole point of this file.
    ///
    /// The edge of the band cannot be written in closed form (<c>Gap</c> is a piecewise
    /// quartic and is not even unimodal, per the addendum to overall review C1). So the
    /// outline is measured by binary-searching the predicate itself. Bring in a different
    /// approximation here and you get a mismatch where **a building the panel calls
    /// "inside the band" is drawn outside the band on the map**.
    /// </summary>
    public class FaultBandOutlineTests
    {
        private const float L = 1000f;
        private const float W = 100f;

        // At angle 0, dir = (0, 1), i.e. the fault runs along the Z axis
        // (same as FaultBandTests).
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: L, width: W);
        }

        private static FaultBandOutline Built()
        {
            var outline = new FaultBandOutline();
            outline.Rebuild(Sample());
            return outline;
        }

        [Fact]
        public void AnUnknownBandIsNotDrawnAtAll()
        {
            var outline = new FaultBandOutline();
            outline.Rebuild(default(FaultBand));

            // Do not draw "unknown" as "probably about this much". The four prefab values
            // are not in the DLL and can only be read in-game (IL facts doc §A-0).
            Assert.False(outline.Known);
            Assert.Equal(0f, outline.AlongExtent, 4);
            Assert.Equal(0f, outline.HalfWidthAt(0), 4);
            Assert.Equal(0f, outline.HalfWidthAt(FaultBandOutline.Segments / 2), 4);
        }

        [Fact]
        public void RebuildingOverAKnownOutlineClearsIt()
        {
            var outline = Built();
            Assert.True(outline.Known);

            outline.Rebuild(default(FaultBand));
            Assert.False(outline.Known);
            Assert.Equal(0f, outline.HalfWidthAt(FaultBandOutline.Segments / 2), 4);
        }

        [Fact]
        public void AlongExtentIsTheCentreLimitPlusTheReachThere()
        {
            var band = Sample();
            var outline = Built();

            // The reach at that position, w(0.4), is added to the 0.4L limit on the disc
            // centre (end of design doc §3.1. The old implementation dropped exactly w at
            // each end).
            Assert.Equal(FaultBand.MaxOffset * L + band.PatchRadiusAt(FaultBand.MaxOffset),
                         outline.AlongExtent, 3);
        }

        [Fact]
        public void TheOutlineSpansTheWholeExtentSymmetrically()
        {
            var outline = Built();
            Assert.Equal(-outline.AlongExtent, outline.AlongAt(0), 3);
            Assert.Equal(0f, outline.AlongAt(FaultBandOutline.Segments / 2), 3);
            Assert.Equal(outline.AlongExtent, outline.AlongAt(FaultBandOutline.Segments), 3);
        }

        [Fact]
        public void AtTheCentreTheHalfWidthIsTheReachPlusTheMeander()
        {
            var band = Sample();
            var outline = Built();

            // At the centre (t = 0), w = W, plus the meander of 0.5w gives 1.5W.
            // It must agree with FaultBand.HalfWidthAt.
            int middle = FaultBandOutline.Segments / 2;
            Assert.Equal(band.HalfWidthAt(0f), outline.HalfWidthAt(middle), 1);
            Assert.Equal(1.5f * W, outline.HalfWidthAt(middle), 1);
        }

        [Fact]
        public void TheBandTapersTowardBothEnds()
        {
            var outline = Built();
            int middle = FaultBandOutline.Segments / 2;

            for (int i = 1; i <= middle; i++)
            {
                Assert.True(outline.HalfWidthAt(i) > outline.HalfWidthAt(i - 1));
                Assert.True(outline.HalfWidthAt(FaultBandOutline.Segments - i)
                            > outline.HalfWidthAt(FaultBandOutline.Segments - i + 1));
            }

            // At the ends there is almost no width (it approaches the single point that the
            // last disc reaches).
            Assert.True(outline.HalfWidthAt(0) < 0.05f * W);
            Assert.True(outline.HalfWidthAt(FaultBandOutline.Segments) < 0.05f * W);
        }

        /// <summary>
        /// **The centre of this file.** Just inside the measured outline <c>Contains</c>
        /// must be true and just outside it false = the shape drawn and the shape tested
        /// against agree.
        /// </summary>
        [Fact]
        public void TheOutlineAgreesWithContainsOnBothSides()
        {
            var band = Sample();
            var outline = Built();
            const float Eps = 1f;

            for (int i = 0; i <= FaultBandOutline.Segments; i++)
            {
                float u = outline.AlongAt(i);
                float h = outline.HalfWidthAt(i);

                if (h > 2f * Eps)
                {
                    Assert.True(band.Contains(PointAt(band, u, h - Eps)),
                                "inside the drawn edge but Contains said no, sample " + i);
                }
                Assert.False(band.Contains(PointAt(band, u, h + Eps)),
                             "outside the drawn edge but Contains said yes, sample " + i);
            }
        }

        [Fact]
        public void TheOutlineIsSymmetricAboutTheFaultLine()
        {
            var band = Sample();
            var outline = Built();

            // The meander sin(...) swings equally to either side of ±0.5w, so the band is
            // symmetric about the line.
            for (int i = 0; i <= FaultBandOutline.Segments; i++)
            {
                float u = outline.AlongAt(i);
                float h = outline.HalfWidthAt(i);
                if (h <= 2f) continue;
                Assert.True(band.Contains(PointAt(band, u, -(h - 1f))));
            }
        }

        [Fact]
        public void MatchesOnlyForTheGeometryItWasMeasuredFor()
        {
            var outline = Built();

            // It is a function of L and W alone, so if those two are the same we do not
            // re-measure (it does not depend on the epicentre position or m_angle = it is
            // held in local coordinates).
            Assert.True(outline.Matches(L, W));
            Assert.False(outline.Matches(L, W * 1.1f));
            Assert.False(outline.Matches(L * 1.1f, W));
            Assert.False(new FaultBandOutline().Matches(L, W));
        }

        private static Vec2 PointAt(FaultBand band, float along, float across)
        {
            return band.Centre
                   + band.Direction * along
                   + new Vec2(band.Direction.Z, -band.Direction.X) * across;
        }
    }
}
