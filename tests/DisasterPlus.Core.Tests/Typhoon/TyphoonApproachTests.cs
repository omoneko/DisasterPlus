using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// <b>台風はマップ端から来て、クリック地点で最盛期を迎え、通過して去る。</b>
    /// （2026-09-02、所有者の依頼）
    ///
    /// ★ 経路そのものは変えていない。同じ円弧の<b>どこを t = 0 と呼ぶか</b>を
    ///   <c>ApproachFramesFor</c> ぶんずらしただけである。
    /// </summary>
    public class TyphoonApproachTests
    {
        private const uint Lifetime = 8192u;

        private static float Speed()
        {
            return TyphoonTrack.SpeedFor(Lifetime);
        }

        [Fact]
        public void It_arrives_at_the_clicked_point()
        {
            // ★★ **これがこの機能の定義である。** クリック地点は出発点ではなく到達点。
            var click = new Vec2(1000f, -2000f);
            float speed = Speed();
            uint approach = TyphoonTrack.ApproachFramesFor(click, 12345u, speed, Lifetime);

            var at = TyphoonTrack.CentreAt(click, 12345u, approach, speed, approach);

            Assert.InRange(at.X - click.X, -1f, 1f);
            Assert.InRange(at.Z - click.Z, -1f, 1f);
        }

        [Fact]
        public void It_starts_outside_the_map()
        {
            // ★★ **これが「マップ端で発生して」の中身である。**
            //    いちばん厳しいのはマップ中央 —— どの端からも半辺ぶん離れている。
            //    そこで外へ出られるだけの道のりを <c>NominalPathLength</c> が取っている。
            var click = new Vec2(0f, 0f);
            float speed = Speed();

            for (uint seed = 1u; seed < 40u; seed++)
            {
                uint approach = TyphoonTrack.ApproachFramesFor(click, seed, speed, Lifetime);
                var start = TyphoonTrack.CentreAt(click, seed, 0u, speed, approach);

                Assert.False(TyphoonTrack.IsInsideMap(start),
                    "seed " + seed + " starts inside the map at ("
                    + start.X + "," + start.Z + ") after backing up "
                    + approach + " frames");
            }
        }

        [Fact]
        public void It_still_starts_outside_from_a_corner()
        {
            // 端の近くを指されたら、戻る距離は短くて済む（頭打ちにも当たらない）。
            var click = new Vec2(7000f, -7000f);
            float speed = Speed();

            for (uint seed = 1u; seed < 20u; seed++)
            {
                uint approach = TyphoonTrack.ApproachFramesFor(click, seed, speed, Lifetime);
                var start = TyphoonTrack.CentreAt(click, seed, 0u, speed, approach);

                Assert.False(TyphoonTrack.IsInsideMap(start),
                    "seed " + seed + " starts inside at (" + start.X + "," + start.Z + ")");
            }
        }

        [Fact]
        public void It_keeps_going_after_it_arrives()
        {
            var click = new Vec2(0f, 0f);
            float speed = Speed();
            uint approach = TyphoonTrack.ApproachFramesFor(click, 7u, speed, Lifetime);

            var atArrival = TyphoonTrack.CentreAt(click, 7u, approach, speed, approach);
            var later = TyphoonTrack.CentreAt(click, 7u, approach + 2000u, speed, approach);

            float moved = Distance(atArrival, later);

            // 2000 フレームぶん進んでいること（円弧なので直線距離はやや短い）。
            Assert.True(moved > speed * 2000f * 0.7f,
                "it should carry on past the clicked point, moved " + moved + " m");
        }

        [Fact]
        public void The_approach_never_eats_more_than_half_the_life()
        {
            // ★ 端から遠い地点を指しても、頭打ちに当たるだけで暴走しない。
            //   （最盛期は寿命の真ん中なので、そこを越えて接近に使うと
            //     衰えてから到達することになる。）
            var click = new Vec2(0f, 0f);
            uint approach = TyphoonTrack.ApproachFramesFor(click, 3u, Speed(), Lifetime);

            // ★ 強度の台形が平らな 25〜75% の中に着く。**その外だと弱い台風になる。**
            Assert.InRange(approach,
                           (uint)(Lifetime * TyphoonTrack.MinApproachFraction),
                           (uint)(Lifetime * TyphoonTrack.ApproachFraction));
        }

        [Fact]
        public void A_dead_storm_asks_for_no_approach()
        {
            // 速度 0 は「m_activeDuration が読めなかった」の意味（SpeedFor の doc）。
            Assert.Equal(0u, TyphoonTrack.ApproachFramesFor(new Vec2(0f, 0f), 1u, 0f, Lifetime));
            Assert.Equal(0u, TyphoonTrack.ApproachFramesFor(new Vec2(0f, 0f), 1u, 1f, 0u));
        }

        [Fact]
        public void Zero_approach_is_the_old_behaviour()
        {
            // ★ 5 引数版に 0 を渡したら 4 引数版と同じ ——
            //   既存のテストと診断がそこに乗っている。
            var click = new Vec2(500f, 500f);
            float speed = Speed();

            for (uint t = 0u; t < 4000u; t += 500u)
            {
                var a = TyphoonTrack.CentreAt(click, 9u, t, speed);
                var b = TyphoonTrack.CentreAt(click, 9u, t, speed, 0u);
                Assert.Equal(a.X, b.X, 3);
                Assert.Equal(a.Z, b.Z, 3);

                Assert.Equal(TyphoonTrack.HeadingAt(click, 9u, t, speed),
                             TyphoonTrack.HeadingAt(click, 9u, t, speed, 0u), 3);
            }
        }

        [Fact]
        public void The_heading_matches_the_direction_it_is_actually_moving()
        {
            // ★★ 位置だけずらして向きを忘れると、**進んでいる向きと表示が食い違う**。
            var click = new Vec2(0f, 0f);
            float speed = Speed();
            uint approach = TyphoonTrack.ApproachFramesFor(click, 21u, speed, Lifetime);

            var a = TyphoonTrack.CentreAt(click, 21u, 1000u, speed, approach);
            var b = TyphoonTrack.CentreAt(click, 21u, 1010u, speed, approach);

            float moveAngle = (float)System.Math.Atan2(b.Z - a.Z, b.X - a.X);
            float reported = TyphoonTrack.HeadingAt(click, 21u, 1005u, speed, approach);

            float diff = System.Math.Abs(Wrap(moveAngle - reported));
            Assert.True(diff < 0.05f,
                "heading " + reported + " vs actual " + moveAngle);
        }

        private static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Wrap(float radians)
        {
            const float twoPi = 6.28318531f;
            while (radians > 3.14159265f) radians -= twoPi;
            while (radians < -3.14159265f) radians += twoPi;
            return radians;
        }
    }
}
