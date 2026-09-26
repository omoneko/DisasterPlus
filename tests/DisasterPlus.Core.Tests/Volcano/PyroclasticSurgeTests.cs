using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The fan of ash (in-game report no. 5: "a pyroclastic flow should spread much further
    /// over the lower slopes, not just along the lava flow").
    ///
    /// Five things are pinned down here:
    ///   1. the lobes are **spread out** around the vent (they do not pile onto the lava)
    ///   2. they **widen** as they descend
    ///   3. they are pulled towards the valley **only as they near the foot**
    ///      (at the source they cross the ridges)
    ///   4. the fan appears even when there is not a single lava flow
    ///   5. the total number of particles does not grow
    ///      (it is normalised by the area of the band)
    /// </summary>
    public class PyroclasticSurgeTests
    {
        private static readonly uint Seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));

        private static readonly Vec2 Vent = new Vec2(100f, -50f);

        [Fact]
        public void TheLobesAreSpreadAroundTheVent()
        {
            var seen = new float[PyroclasticSurge.LobeCount];
            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                seen[i] = PyroclasticSurge.LobeAzimuth(Seed, i, PyroclasticSurge.LobeCount);
            }

            // Neighbours never swap places (i.e. the jitter never exceeds half the even spacing).
            float step = (float)(2.0 * Math.PI / PyroclasticSurge.LobeCount);
            for (int i = 1; i < seen.Length; i++)
            {
                float gap = seen[i] - seen[i - 1];
                Assert.InRange(gap, step * 0.3f, step * 1.7f);
            }

            // An out-of-range index does not break it (guards the outside of the caller's loop).
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, -3, 5)));
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, 99, 5)));
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, 0, 0)));
        }

        [Fact]
        public void TheBandWidensAsItDescends()
        {
            float near = PyroclasticSurge.HalfWidthMetres(0f);
            float mid = PyroclasticSurge.HalfWidthMetres(600f);
            float far = PyroclasticSurge.HalfWidthMetres(100000f);

            Assert.Equal(PyroclasticSurge.HalfWidthBaseMetres, near, 3);
            Assert.True(mid > near * 2f, "the surge should fan out as it descends");
            Assert.Equal(PyroclasticSurge.HalfWidthMaxMetres, far, 3);

            Assert.Equal(PyroclasticSurge.HalfWidthBaseMetres,
                         PyroclasticSurge.HalfWidthMetres(float.NaN), 3);
        }

        [Fact]
        public void TheValleyPullsOnlyNearTheFoot()
        {
            // ★ At the source it crosses the ridges; at the foot it gathers into the valley.
            Assert.Equal(0f, PyroclasticSurge.ChannelPullAt(0f), 4);
            Assert.True(PyroclasticSurge.ChannelPullAt(0.25f) < 0.1f);
            Assert.Equal(PyroclasticSurge.ChannelPullMax, PyroclasticSurge.ChannelPullAt(1f), 4);

            // **It never exceeds a half** —— beyond that we are back to "it only flows over
            // the lava".
            Assert.True(PyroclasticSurge.ChannelPullMax <= 0.5f);

            for (float t = 0f; t <= 1.2f; t += 0.05f)
            {
                Assert.InRange(PyroclasticSurge.ChannelPullAt(t), 0f,
                               PyroclasticSurge.ChannelPullMax);
            }
            Assert.Equal(0f, PyroclasticSurge.ChannelPullAt(float.NaN), 4);
        }

        [Fact]
        public void TheLobeLeavesTheVentOnItsOwnBearingAndTurnsTowardTheValley()
        {
            const float reach = 900f;
            const float baseAzimuth = 0f;                 // leaves towards +X
            float channel = (float)(Math.PI * 0.5);       // the valley is at +Z

            Vec2 near = PyroclasticSurge.PointAt(Vent, baseAzimuth, channel - baseAzimuth,
                                                 reach, 60f);
            Vec2 far = PyroclasticSurge.PointAt(Vent, baseAzimuth, channel - baseAzimuth,
                                                reach, reach);

            // Right next to the source it is almost the lobe's own bearing
            // (it has not turned towards the valley).
            float nearAngle = (float)Math.Atan2(near.Z - Vent.Z, near.X - Vent.X);
            Assert.InRange(nearAngle, -0.6f, 0.6f);

            // At the foot it has swung round towards the valley (but never becomes the
            // valley itself).
            float farAngle = (float)Math.Atan2(far.Z - Vent.Z, far.X - Vent.X);
            Assert.True(farAngle > nearAngle + 0.2f, "the lobe never turned toward the valley");
            Assert.True(farAngle < channel - 0.2f, "the lobe collapsed onto the lava path");
        }

        [Fact]
        public void TheFanIsThereEvenWhenNoLavaIsFlowing()
        {
            bool found;
            float azimuth = 1.2f;
            Assert.Equal(azimuth, PyroclasticSurge.NearestChannel(azimuth, null, 0, out found), 4);
            Assert.False(found);

            Assert.Equal(azimuth,
                         PyroclasticSurge.NearestChannel(azimuth, new float[0], 0, out found), 4);
            Assert.False(found);

            // The lobes themselves still appear (they are simply not pulled to a valley).
            Vec2 a, b, c, d;
            Assert.True(PyroclasticSurge.TryLobe(Vent, azimuth, azimuth, 900f, 400f,
                                                 out a, out b, out c, out d));
            Assert.False(Bad(a.X) || Bad(a.Z) || Bad(d.X) || Bad(d.Z));
        }

        [Fact]
        public void TheNearestValleyIsFoundAcrossTheWrapAround()
        {
            bool found;
            var bearings = new float[] { 3.0f, 0.5f };

            // The nearest to -3.1 is 3.0 (it straddles +π and −π). The difference is 0.18,
            // smaller than the difference from 0.5.
            float result = PyroclasticSurge.NearestChannel(-3.1f, bearings, 2, out found);
            Assert.True(found);
            Assert.Equal(-3.1f + -0.1831853f, result, 3);

            // It does not fall over when a NaN is mixed in.
            var dirty = new float[] { float.NaN, 0.4f };
            result = PyroclasticSurge.NearestChannel(0.5f, dirty, 2, out found);
            Assert.True(found);
            Assert.Equal(0.4f, result, 3);
        }

        [Fact]
        public void TheHeadRunsDownTheLobeAndStartsOver()
        {
            const float path = 900f;
            float cycle = PyroclasticSurge.CycleSeconds(path);

            Assert.Equal(0f, PyroclasticSurge.HeadMetres(0f, path), 2);
            Assert.Equal(0f, PyroclasticSurge.HeadMetres(cycle, path), 1);
            Assert.True(PyroclasticSurge.HeadMetres(cycle * 0.5f, path) > 0f);

            Assert.True(PyroclasticSurge.CycleSeconds(0f) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(float.NaN) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(-100f) > 0f);

            // Each lobe is offset in phase (the five do not march in formation).
            float previous = -1f;
            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                float phase = PyroclasticSurge.LobePhaseSeconds(i, PyroclasticSurge.LobeCount,
                                                                path);
                Assert.InRange(phase, 0f, cycle);
                Assert.True(phase > previous);
                previous = phase;
            }
        }

        [Fact]
        public void TheParticleBudgetDoesNotGrowWithTheFan()
        {
            // ★ The particle count of a Bezier band is 2 x halfWidth x path length x pps
            //   (IL §B-5). Even after widening 90 -> 260 m and going from 2 -> 5 lobes,
            //   the "area x magnitude" of the whole fan must not exceed that of the
            //   previous two bands.
            const float path = 1200f;
            float worst = 0f;

            for (float head = 0f; head <= path; head += 25f)
            {
                float half = PyroclasticSurge.HalfWidthMetres(head);
                float m = PyroclasticSurge.Magnitude(1f, head, path, half);
                float area = 2f * half * PyroclasticSurge.BandLengthMetres
                             * PyroclasticSurge.LobeCount;
                float budget = area * m;
                if (budget > worst) worst = budget;
            }

            float before = PyroclasticSurge.ReferenceAreaSquareMetres
                           * PyroclasticSurge.MagnitudeMax;
            Assert.True(worst <= before * 1.01f,
                "the fan emits more particles than the two old bands did");
        }

        [Fact]
        public void NothingIsDrawnBeforeTheHeadHasLeftTheVentOrAfterItHasGone()
        {
            const float path = 900f;

            // While not 1 mm of the band sits on the path it is 0 (i.e. the caller draws nothing).
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, 0f, path, 50f), 4);

            // On a mountain whose path is too short we emit none at all
            // (otherwise a ball of ash sits on the summit).
            Assert.Equal(0f, PyroclasticSurge.Magnitude(
                1f, 40f, PyroclasticSurge.MinPathMetres - 1f, 50f), 4);

            Vec2 a, b, c, d;
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, 40f, 20f,
                                                  out a, out b, out c, out d));
            Assert.Equal(Vent.X, a.X, 3);
        }

        [Fact]
        public void TheReachGrowsWithTheEruptionAndVariesByLobe()
        {
            const float radius = 1200f;

            float weak = PyroclasticSurge.ReachMetres(radius, 0f, Seed, 0);
            float strong = PyroclasticSurge.ReachMetres(radius, 1f, Seed, 0);
            Assert.True(strong > weak);
            Assert.InRange(strong, radius * 0.7f, radius * 1.2f);

            // Each lobe differs (if they were all the same the edge of the fan would be a
            // perfect circle).
            float first = PyroclasticSurge.ReachMetres(radius, 1f, Seed, 0);
            bool differs = false;
            for (int i = 1; i < PyroclasticSurge.LobeCount; i++)
            {
                if (Math.Abs(PyroclasticSurge.ReachMetres(radius, 1f, Seed, i) - first) > 1f)
                {
                    differs = true;
                }
            }
            Assert.True(differs, "every lobe reaches exactly as far as the others");

            Assert.Equal(0f, PyroclasticSurge.ReachMetres(float.NaN, 1f, Seed, 0), 4);
            Assert.Equal(0f, PyroclasticSurge.ReachMetres(0f, 1f, Seed, 0), 4);
        }

        [Fact]
        public void GarbageInputIsNeverNaN()
        {
            Vec2 bad = new Vec2(float.NaN, 0f);
            Vec2 a, b, c, d;

            Assert.False(PyroclasticSurge.TryLobe(bad, 0f, 0f, 900f, 400f,
                                                  out a, out b, out c, out d));
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, float.NaN, 400f,
                                                  out a, out b, out c, out d));
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, 900f, float.NaN,
                                                  out a, out b, out c, out d));

            // When the strength cannot be read we get **the thinnest band** (not 0).
            // Same treatment as EruptionEffectPlan: "cannot read" must not become "emit nothing".
            Assert.True(PyroclasticSurge.Magnitude(float.NaN, 300f, 900f, 50f) >= 0f);
            Assert.False(Bad(PyroclasticSurge.Magnitude(float.NaN, 300f, 900f, 50f)));
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, 100f, float.NaN, 50f), 4);
            Assert.True(PyroclasticSurge.Magnitude(1f, 300f, 900f, float.NaN) >= 0f);

            Vec2 p = PyroclasticSurge.PointAt(Vent, 0f, 0f, 900f, float.NaN);
            Assert.Equal(Vent.X, p.X, 3);
        }

        [Fact]
        public void TheLavaBearingPointsWhereTheFlowWent()
        {
            var points = new[]
            {
                new Vec2(100f, -50f), new Vec2(160f, -50f), new Vec2(240f, -50f),
            };

            float bearing;
            Assert.True(PyroclasticSurge.TryBearing(points, 0, points.Length, Vent, out bearing));
            Assert.Equal(0f, bearing, 2);   // it flowed towards +X

            // False when there are too few points or they are broken
            // (**never invent a bearing by guessing**).
            Assert.False(PyroclasticSurge.TryBearing(null, 0, 3, Vent, out bearing));
            Assert.False(PyroclasticSurge.TryBearing(points, 0, 1, Vent, out bearing));
            Assert.False(PyroclasticSurge.TryBearing(
                new[] { Vent, Vent }, 0, 2, Vent, out bearing));
        }

        private static bool Bad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
