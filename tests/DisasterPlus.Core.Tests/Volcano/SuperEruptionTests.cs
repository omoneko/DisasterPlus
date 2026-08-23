using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 所有者の依頼（2026-08-22）「25.5 のときだけ、破局噴火の再現を…
    /// 地下のマグマ上昇による火山の形成 → 数万年かけた巨大なマグマだまりの成長 →
    /// 内圧限界による破局噴火（大爆発） → 地面の自重による大陥没とカルデラ形成」。
    /// </summary>
    public class SuperEruptionTests
    {
        private const float Radius = 1200f;
        private const float Height = 600f;

        [Fact]
        public void OnlyTheVeryTopOfTheSliderIsASupereruption()
        {
            // 「25.5 のときだけ」。24.9 では起きない。
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

            // ★ 山よりずっと広く、山よりずっと低い。
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

            // 縁の直前の変化がなだらかであること（折れ目 = 崖に見える）。
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
            Assert.True(depth > Height,
                        "the caldera floor (" + depth + ") does not get below the original ground");
        }

        [Fact]
        public void TheCalderaHasAFlatFloorNotACone()
        {
            // ★★ ここが「大きい火口」との違いである。
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
            // 壁が凸凹すると、落ちた塊ではなく削った穴に見える。
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
            // マップは一辺 17,280 m。半径も深さも桁で外れないこと。
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
