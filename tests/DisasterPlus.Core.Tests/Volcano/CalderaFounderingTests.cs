using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 実機報告（2026-08-22）「カルデラ形成時は、山体が大きく落ち込んで大爆発する
    /// んじゃないでしょうか…？ 生成の際の正しい現象を再現してください」。
    ///
    /// ★★ <b>ここが固定するのは「山が残らない」ことである。</b>
    ///    最初の実装は今の地面から深さぶんを引いていたので、
    ///    円錐 +1000 m − 深さ 900 m ＝ <b>山頂に 100 m の切り株が残った</b>。
    ///    それは「陥没」ではなく「山のまわりに溝を掘った」絵である。
    ///
    ///    断面は本物の円錐（<see cref="VolcanoCrater.ProfileAt"/>）から作る ——
    ///    自分で作った「それらしい山」で検査すると、実際の形と食い違ったときに
    ///    ここが通ってしまう。
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

        /// <summary>噴火前の地面（m）。平らな土地に円錐が乗っている。</summary>
        private static float BaseAt(float distance)
        {
            return Ground + VolcanoCrater.ProfileAt(Flat, distance, 0f, ConeRadius, ConeHeight);
        }

        /// <summary>落ち切ったあとの地面（m）。</summary>
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
            // ★★ これが所有者の指摘そのもの。山の在った範囲のどこにも、
            //    元の地面より高いところが残っていないこと。
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
            // 山頂は「深さ ＋ 山の高さ」ぶん落ちる。深さだけしか落ちないなら、
            // それは引き算に戻っている。
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
            // 陥没が地面を持ち上げる経路が 1 本も無いこと。
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
            // 起点の高さは中心の 1 点なので、外縁の実地面がそれより低いことがある。
            // そこを「目標まで上げる」と縁が盛り上がる ——> 0 に切っていること。
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
