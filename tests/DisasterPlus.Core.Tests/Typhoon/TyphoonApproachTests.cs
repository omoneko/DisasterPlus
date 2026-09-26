using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// <b>The typhoon comes in from the edge of the map, reaches its peak at the clicked
    /// point, and passes on by.</b> (2026-09-02, the owner's request)
    ///
    /// ★ The track itself is unchanged. All we did was shift <b>which point on the same
    ///   arc we call t = 0</b> by <c>ApproachFramesFor</c>.
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
            // ★★ **This is the definition of the feature.** The clicked point is not where
            //    it starts but where it arrives.
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
            // ★★ **This is the substance of "it forms at the edge of the map".**
            //    The hardest case is the centre of the map —— half a side away from every
            //    edge. <c>NominalPathLength</c> reserves enough track to get out from there.
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
            // When a point near the edge is clicked, the distance to back up is short
            // (and it never hits the cap).
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

            // It must have travelled 2000 frames' worth (the straight-line distance is a
            // little shorter, since the track is an arc).
            Assert.True(moved > speed * 2000f * 0.7f,
                "it should carry on past the clicked point, moved " + moved + " m");
        }

        [Fact]
        public void The_approach_never_eats_more_than_half_the_life()
        {
            // ★ Clicking a point far from the edge only hits the cap; it never runs away.
            //   (The peak is at the middle of the lifetime, so spending more than that on
            //    the approach would mean arriving after it has already weakened.)
            var click = new Vec2(0f, 0f);
            uint approach = TyphoonTrack.ApproachFramesFor(click, 3u, Speed(), Lifetime);

            // ★ It lands within the 25–75% where the intensity trapezium is flat.
            //   **Outside that it becomes a weak typhoon.**
            Assert.InRange(approach,
                           (uint)(Lifetime * TyphoonTrack.MinApproachFraction),
                           (uint)(Lifetime * TyphoonTrack.ApproachFraction));
        }

        [Fact]
        public void A_dead_storm_asks_for_no_approach()
        {
            // A speed of 0 means "m_activeDuration could not be read" (see the SpeedFor doc).
            Assert.Equal(0u, TyphoonTrack.ApproachFramesFor(new Vec2(0f, 0f), 1u, 0f, Lifetime));
            Assert.Equal(0u, TyphoonTrack.ApproachFramesFor(new Vec2(0f, 0f), 1u, 1f, 0u));
        }

        [Fact]
        public void Zero_approach_is_the_old_behaviour()
        {
            // ★ Passing 0 to the 5-argument version must be identical to the 4-argument one
            //   —— the existing tests and the diagnostics rest on that.
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
            // ★★ Shift the position but forget the heading and **the direction it is moving
            //    disagrees with the direction displayed**.
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
