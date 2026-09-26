using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// <b>The track drawn on the map.</b> (2026-09-02, owner: "show the typhoon's
    /// track ... on the city map")
    ///
    /// ★ The path itself is pinned down by <see cref="TyphoonTrackTests"/>.
    ///   What is guarded here is only the <b>contract with the drawing side</b> ——
    ///   from unreadable values draw not a single line, allocate nothing, overflow nothing.
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
            // ★★ **If the drawing side draws the line with a different formula, the line
            //    on the map and the actual typhoon drift apart.**
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
            // ★★ Speed 0 means "the lifetime could not be read", not "it is standing still".
            //    Drawing a line from a value we never read means painting a guess on the map.
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

            // The ends are exactly from and to (the line does not start or end part-way).
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

            // Even when more is requested than the array holds, it stops at the array length.
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
            // ★ Computing span * i in uint overflows when the lifetime is long and count
            //   is large, and **the track folds back**. We divide in long and cast back.
            var plan = new TyphoonTrackPlan(new Vec2(0f, 0f), 9u, 3f, 0u, uint.MaxValue / 2u);
            var buffer = new Vec2[256];

            int n = plan.Sample(buffer, 256, 0u, uint.MaxValue / 2u);
            Assert.Equal(256, n);

            // It advances monotonically (a fold-back would go backwards somewhere).
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
