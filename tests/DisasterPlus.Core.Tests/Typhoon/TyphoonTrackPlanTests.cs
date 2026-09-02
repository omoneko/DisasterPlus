using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// <b>地図に引く進路。</b>（2026-09-02、所有者「台風の進路…を都市マップ上に示す」）
    ///
    /// ★ 経路そのものは <see cref="TyphoonTrackTests"/> が固定している。
    ///   ここが守るのは<b>描画側との約束</b>だけ ——
    ///   読めない値では 1 本も引かないこと、確保しないこと、溢れないこと。
    /// </summary>
    public class TyphoonTrackPlanTests
    {
        private const uint Lifetime = 8192u;

        private static TyphoonTrackPlan Plan(Vec2 origin, uint seed)
        {
            float speed = TyphoonTrack.SpeedFor(Lifetime);
            uint approach = TyphoonTrack.ApproachFramesFor(origin, seed, speed, Lifetime);
            return new TyphoonTrackPlan(origin, seed, speed, approach, Lifetime);
        }

        [Fact]
        public void It_agrees_with_the_track_it_came_from()
        {
            // ★★ **描画が別の式で線を引いたら、地図の線と実際の台風がずれる。**
            var origin = new Vec2(1000f, -500f);
            var plan = Plan(origin, 42u);

            for (uint t = 0u; t < Lifetime; t += 512u)
            {
                var expected = TyphoonTrack.CentreAt(origin, 42u, t,
                                                     plan.Speed, plan.ApproachFrames);
                var actual = plan.CentreAt(t);

                Assert.Equal(expected.X, actual.X, 3);
                Assert.Equal(expected.Z, actual.Z, 3);

                Assert.Equal(TyphoonTrack.HeadingAt(origin, 42u, t, plan.Speed,
                                                    plan.ApproachFrames),
                             plan.HeadingAt(t), 3);
            }
        }

        [Fact]
        public void An_unreadable_prefab_draws_nothing()
        {
            // ★★ 速度 0 は「持続時間が読めなかった」であって「止まっている」ではない。
            //    読めていない値から線を引いたら、推測を地図に描くことになる。
            var dead = new TyphoonTrackPlan(new Vec2(0f, 0f), 1u, 0f, 0u, Lifetime);
            Assert.False(dead.Usable);
            Assert.Equal(0, dead.Sample(new Vec2[16], 16, 0u, Lifetime));

            var noLife = new TyphoonTrackPlan(new Vec2(0f, 0f), 1u, 5f, 0u, 0u);
            Assert.False(noLife.Usable);
            Assert.Equal(0, noLife.Sample(new Vec2[16], 16, 0u, 0u));

            Assert.False(TyphoonTrackPlan.None.Usable);
        }

        [Fact]
        public void It_fills_the_buffer_it_is_given()
        {
            var plan = Plan(new Vec2(0f, 0f), 7u);
            var buffer = new Vec2[64];

            int n = plan.Sample(buffer, 40, 0u, Lifetime);
            Assert.Equal(40, n);

            // 端がちょうど from と to であること（線が途中で始まったり終わったりしない）。
            var first = plan.CentreAt(0u);
            var last = plan.CentreAt(Lifetime);
            Assert.Equal(first.X, buffer[0].X, 3);
            Assert.Equal(last.X, buffer[39].X, 3);
        }

        [Fact]
        public void It_never_writes_past_the_buffer()
        {
            var plan = Plan(new Vec2(0f, 0f), 3u);
            var small = new Vec2[8];

            // 要求が配列より大きくても、配列の長さで止まること。
            Assert.Equal(8, plan.Sample(small, 999, 0u, Lifetime));

            Assert.Equal(0, plan.Sample(null, 8, 0u, Lifetime));
            Assert.Equal(0, plan.Sample(small, 0, 0u, Lifetime));
        }

        [Fact]
        public void A_single_sample_is_the_start_point()
        {
            var plan = Plan(new Vec2(0f, 0f), 5u);
            var one = new Vec2[1];

            Assert.Equal(1, plan.Sample(one, 1, 2000u, Lifetime));
            Assert.Equal(plan.CentreAt(2000u).X, one[0].X, 3);
        }

        [Fact]
        public void A_long_span_does_not_overflow()
        {
            // ★ span * i を uint のまま計算すると、寿命が長く count が大きいときに
            //   溢れて**経路が折り返す**。long で割ってから戻している。
            var plan = new TyphoonTrackPlan(new Vec2(0f, 0f), 9u, 3f, 0u, uint.MaxValue / 2u);
            var buffer = new Vec2[256];

            int n = plan.Sample(buffer, 256, 0u, uint.MaxValue / 2u);
            Assert.Equal(256, n);

            // 単調に進んでいること（折り返していたら、どこかで戻る）。
            uint previous = 0u;
            for (int i = 1; i < n; i++)
            {
                uint at = (uint)((long)(uint.MaxValue / 2u) * i / (n - 1));
                Assert.True(at >= previous, "sample " + i + " went backwards");
                previous = at;
            }
        }

        [Fact]
        public void Backwards_ranges_draw_nothing()
        {
            var plan = Plan(new Vec2(0f, 0f), 11u);
            Assert.Equal(0, plan.Sample(new Vec2[8], 8, 5000u, 1000u));
        }
    }
}
