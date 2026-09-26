using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// In-game report (2026-08-22): "the typhoon cloud effect appears for only an
    /// instant and then disappears ... I want a huge typhoon cloud to rotate slowly
    /// overhead and pass across as much of the map as possible ...".
    ///
    /// ★★ <b>The cause was that the bearing had nothing to do with the start point.</b>
    ///    Point near the edge of the map, draw a seed that faces outwards, and the
    ///    typhoon leaves the map within a few hundred metres and is caught by
    ///    <c>TyphoonController.Stop()</c> —— <b>the cloud appears for an instant and
    ///    vanishes.</b> The host vanilla thunderstorm runs on a different lifetime, so
    ///    only that one is left behind (the report's "from then on it is just a plain
    ///    thunderstorm").
    /// </summary>
    public class TyphoonBearingTests
    {
        /// <summary>Measured from ThunderStormAI.</summary>
        private const uint HostDuration = 8192u;

        private static float Speed
        {
            get { return TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration)); }
        }

        private static uint Lifetime
        {
            get { return TyphoonTrack.LifetimeFramesFor(HostDuration); }
        }

        /// <summary>A few points right at the edge of the map.
        /// **This is the placement that breaks most easily.**</summary>
        private static Vec2[] EdgePoints()
        {
            const float e = TyphoonTrack.MapHalfExtent * 0.92f;
            return new[]
            {
                new Vec2(e, 0f), new Vec2(-e, 0f), new Vec2(0f, e), new Vec2(0f, -e),
                new Vec2(e, e), new Vec2(-e, e), new Vec2(e, -e), new Vec2(-e, -e),
            };
        }

        [Fact]
        public void ATyphoonPlacedAtTheEdgeHeadsInland()
        {
            // ★★ For every seed, placing it at the edge must head **inwards**.
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 40; seed++)
                {
                    float bearing = TyphoonTrack.BearingFrom(origin, seed);

                    // Dot product of the heading unit vector with the unit vector
                    // pointing at the centre.
                    float dx = (float)System.Math.Cos(bearing);
                    float dz = (float)System.Math.Sin(bearing);

                    float len = (float)System.Math.Sqrt(origin.X * origin.X
                                                        + origin.Z * origin.Z);
                    float tx = -origin.X / len;
                    float tz = -origin.Z / len;

                    float dot = dx * tx + dz * tz;

                    // spread is 0.62 rad, so it never goes below cos(0.62) = 0.81.
                    Assert.True(dot > 0.8f,
                                "seed " + seed + " at (" + origin.X + "," + origin.Z
                                + ") heads away from the map (dot=" + dot + ")");
                }
            }
        }

        [Fact]
        public void ATyphoonPlacedAtTheEdgeStaysOverTheMapForMostOfItsLife()
        {
            // ★★ This is the request itself —— "pass overhead ... as much as possible".
            //    This used to come out as 0 for plenty of seeds.
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 20; seed++)
                {
                    int inside = 0;
                    const int Samples = 64;

                    for (int i = 0; i < Samples; i++)
                    {
                        uint elapsed = (uint)((long)Lifetime * i / Samples);
                        Vec2 centre = TyphoonTrack.CentreAt(origin, seed, elapsed, Speed);
                        if (TyphoonTrack.IsInsideMap(centre)) inside++;
                    }

                    Assert.True(inside > Samples / 2,
                                "seed " + seed + " at (" + origin.X + "," + origin.Z
                                + ") spends only " + inside + "/" + Samples
                                + " of its life over the map");
                }
            }
        }

        [Fact]
        public void APointNearTheCentreStillGetsAVariedHeading()
        {
            // ★ Right by the centre, "the direction towards the centre" is undefined.
            //   It is decided by the seed alone, so the bearings must be spread out.
            var origin = new Vec2(0f, 0f);
            var seen = new System.Collections.Generic.HashSet<int>();

            for (uint seed = 1; seed < 60; seed++)
            {
                seen.Add((int)(TyphoonTrack.BearingFrom(origin, seed) * 4f));
            }

            Assert.True(seen.Count > 8,
                        "every typhoon from the centre heads the same way (" + seen.Count + ")");
        }

        [Fact]
        public void TheSamePlaceAndSeedAlwaysGivesTheSameTrack()
        {
            // It must be reproducible within the same save (design document §4.1).
            var origin = new Vec2(3000f, -2000f);
            for (uint seed = 1; seed < 20; seed++)
            {
                Assert.Equal(TyphoonTrack.BearingFrom(origin, seed),
                             TyphoonTrack.BearingFrom(origin, seed), 5);
            }
        }

        [Fact]
        public void TheBearingIsAlwaysAUsableAngle()
        {
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 30; seed++)
                {
                    float b = TyphoonTrack.BearingFrom(origin, seed);
                    Assert.False(float.IsNaN(b));
                    Assert.InRange(b, 0f, 6.28318531f);
                }
            }
        }
    }
}
