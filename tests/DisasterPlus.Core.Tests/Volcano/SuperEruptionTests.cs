using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Owner's request (2026-08-22): "only at 25.5, reproduce a supereruption...
    /// a volcano formed by magma rising underground -> the growth of a huge magma
    /// chamber over tens of thousands of years -> a supereruption (a great explosion)
    /// as the internal pressure reaches its limit -> a great subsidence under the
    /// ground's own weight, forming a caldera".
    /// </summary>
    public class SuperEruptionTests
    {
        private const float Radius = 1200f;
        private const float Height = 600f;

        [Fact]
        public void OnlyTheVeryTopOfTheSliderIsASupereruption()
        {
            // "Only at 25.5". It does not happen at 24.9.
            Assert.True(SuperEruption.IsSuper(255));
            Assert.False(SuperEruption.IsSuper(254));
            Assert.False(SuperEruption.IsSuper(100));
            Assert.False(SuperEruption.IsSuper(55));
        }

        [Fact]
        public void TheBulgeIsWideAndLowNotJustATallerMountain()
        {
            float reach = SuperEruption.InflationRadiusMetres(Radius);
            float rise = SuperEruption.InflationHeightMetres(Height);

            // ★ Far wider than the mountain, and far lower than the mountain.
            Assert.True(reach > Radius * 2f, "the bulge only reached " + reach);
            Assert.True(rise < Height * 0.5f, "the bulge rose " + rise + ", that is a mountain");
            Assert.True(rise > 0f);
        }

        [Fact]
        public void TheBulgeDiesOutSmoothlyAtItsEdge()
        {
            float reach = SuperEruption.InflationRadiusMetres(Radius);
            float rise = SuperEruption.InflationHeightMetres(Height);

            Assert.Equal(rise, SuperEruption.InflationAt(0f, reach, rise), 3);
            Assert.Equal(0f, SuperEruption.InflationAt(reach, reach, rise), 4);
            Assert.Equal(0f, SuperEruption.InflationAt(reach * 2f, reach, rise), 4);

            // The change just before the edge must be gentle (a kink looks like a cliff).
            float a = SuperEruption.InflationAt(reach * 0.95f, reach, rise);
            float b = SuperEruption.InflationAt(reach * 0.99f, reach, rise);
            Assert.True(a - b < rise * 0.02f, "the bulge edge is a step: " + (a - b));
        }

        [Fact]
        public void TheCalderaSwallowsTheWholeMountainAndGoesDeeper()
        {
            float cr = SuperEruption.CalderaRadiusMetres(Radius);
            float depth = SuperEruption.CalderaDepthMetres(Height);

            Assert.True(cr > Radius, "the caldera " + cr + " is smaller than the cone " + Radius);

            // ★★ <b>"Deeper than the mountain's height" is not a requirement.</b>
            //    (this was corrected on 2026-08-22)
            //
            //    This used to require depth &gt; Height. That requirement pushed
            //    <c>CalderaDepthFactor</c> to 1.35 (capped at 900 m), and
            //    <b>the floor always dropped 800 m below sea level (40 m)</b> ——
            //    which is precisely the cause of the owner's question, "why does the
            //    elevation inside the caldera always end up below sea level?".
            //
            //    What drops is the <b>edifice</b>; the surrounding land does not sink
            //    along with it. There are only two requirements: "definitely below the
            //    surrounding ground" and "not too deep".
            Assert.True(depth > 0f, "the caldera does not go below the original ground at all");
            Assert.True(depth < Height,
                        "the caldera floor drops " + depth + " m for a " + Height
                        + " m mountain; that sinks the whole landscape, not the edifice");
        }

        [Fact]
        public void TheCalderaHasAFlatFloorNotACone()
        {
            // ★★ This is what makes it different from "a big crater".
            float cr = SuperEruption.CalderaRadiusMetres(Radius);
            float depth = SuperEruption.CalderaDepthMetres(Height);

            float centre = SuperEruption.BowlProfileAt(0f, cr, depth);
            float mid = SuperEruption.BowlProfileAt(cr * SuperEruption.FloorFraction * 0.9f,
                                                    cr, depth);

            Assert.Equal(centre, mid, 3);
            Assert.Equal(-depth, centre, 3);
        }

        [Fact]
        public void TheCalderaIsAlwaysADepressionAndStopsAtItsRim()
        {
            float cr = SuperEruption.CalderaRadiusMetres(Radius);
            float depth = SuperEruption.CalderaDepthMetres(Height);

            for (int i = 0; i <= 100; i++)
            {
                float d = cr * i / 100f;
                float p = SuperEruption.BowlProfileAt(d, cr, depth);
                Assert.True(p <= 0f, "the caldera rose at " + d);
                Assert.True(p >= -depth - 0.001f, "the caldera went past its depth at " + d);
            }

            Assert.Equal(0f, SuperEruption.BowlProfileAt(cr, cr, depth), 4);
            Assert.Equal(0f, SuperEruption.BowlProfileAt(cr * 3f, cr, depth), 4);
        }

        [Fact]
        public void TheCalderaWallOnlyGetsShallowerOutwards()
        {
            // A bumpy wall looks like a hole that was gouged out, not a block that dropped.
            float cr = SuperEruption.CalderaRadiusMetres(Radius);
            float depth = SuperEruption.CalderaDepthMetres(Height);

            float previous = float.MinValue;
            for (int i = 0; i <= 200; i++)
            {
                float p = SuperEruption.BowlProfileAt(cr * i / 200f, cr, depth);
                Assert.True(p >= previous - 1e-4f, "the wall dipped again at " + (cr * i / 200f));
                previous = p;
            }
        }

        [Fact]
        public void NothingEverExceedsWhatTheMapCanHold()
        {
            // The map is 17,280 m on a side. Neither the radius nor the depth may be
            // out by an order of magnitude.
            foreach (float r in new[] { 400f, 2000f, 6000f, 100000f })
            {
                Assert.True(SuperEruption.CalderaRadiusMetres(r) <= SuperEruption.MaxRadiusMetres);
                Assert.True(SuperEruption.InflationRadiusMetres(r) <= SuperEruption.MaxRadiusMetres);
            }

            foreach (float h in new[] { 50f, 700f, 5000f })
            {
                Assert.InRange(SuperEruption.CalderaDepthMetres(h),
                               SuperEruption.MinDepthMetres, SuperEruption.MaxDepthMetres);
            }
        }

        [Fact]
        public void BrokenInputMovesNoGround()
        {
            Assert.Equal(0f, SuperEruption.InflationAt(float.NaN, 100f, 10f), 4);
            Assert.Equal(0f, SuperEruption.BowlProfileAt(10f, float.NaN, 100f), 4);
            Assert.Equal(0f, SuperEruption.BowlProfileAt(10f, 100f, 0f), 4);
            Assert.Equal(0f, SuperEruption.CalderaRadiusMetres(float.NaN), 4);
        }
    }
}
