using System;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The eruption column (in-game report no. 3: "the eruption plume has become nothing
    /// but a puddle of smoke").
    ///
    /// The five things pinned down here are what make it **an eruption column rather than
    /// a mushroom cloud**:
    ///   1. the column **stacks up without a break** from the vent to the umbrella
    ///      (the segments line up with no gaps)
    ///   2. it **thickens** with height (entrainment). The umbrella is far wider still
    ///   3. the rise **stops** at the neutral buoyancy height (0 at the umbrella)
    ///   4. it **bends** downwind, and the higher it is the more strongly it bends
    ///   5. the total number of particles is the same as one of the old plumes
    ///      (it is normalised by area)
    /// </summary>
    public class EruptionColumnTests
    {
        private const float Vent = 144f;

        private static EruptionColumn Default(float unit)
        {
            return new EruptionColumn(Vent, unit, 1f, 0f,
                                      EruptionColumn.ReferenceWindMetresPerSecond);
        }

        [Fact]
        public void TheColumnIsContinuousFromTheVentToTheUmbrella()
        {
            EruptionColumn c = Default(1f);
            Assert.Equal(EruptionColumn.MaxSegments, c.SegmentCount);

            float expected = 0f;
            for (int i = 0; i < EruptionColumn.ColumnSegments; i++)
            {
                EruptionColumnSegment s = c.SegmentAt(i);
                Assert.False(s.Umbrella);
                Assert.Equal(expected, s.OffsetY, 2);
                Assert.True(s.HalfHeightMetres > 0f, "segment " + i + " has no height");
                expected += s.HalfHeightMetres;
            }

            // The top face of the last column segment is exactly the base of the umbrella.
            Assert.Equal(c.UmbrellaBaseMetres, expected, 1);

            // The top face of the umbrella reaches exactly the height of the column.
            EruptionColumnSegment core = c.SegmentAt(EruptionColumn.ColumnSegments);
            Assert.True(core.Umbrella);
            Assert.Equal(c.HeightMetres, core.OffsetY + core.HalfHeightMetres, 1);
        }

        [Fact]
        public void TheColumnWidensWithHeightAndTheUmbrellaIsWiderStill()
        {
            EruptionColumn c = Default(1f);

            float previous = 0f;
            for (float z = 0f; z <= c.UmbrellaBaseMetres; z += 20f)
            {
                float r = c.RadiusAt(z);
                Assert.True(r >= previous - 0.001f, "the column got narrower at " + z);
                previous = r;
            }

            float top = c.RadiusAt(c.UmbrellaBaseMetres);
            Assert.True(c.UmbrellaRadiusMetres > top * 2f,
                "the umbrella is not much wider than the column");

            // At the vent it does not erupt across the whole crater (it rises from inside
            // the mouth).
            Assert.True(c.RadiusAt(0f) < Vent);
        }

        [Fact]
        public void TheRiseStopsAtTheNeutralBuoyancyHeight()
        {
            EruptionColumn c = Default(1f);

            Assert.True(c.RiseAt(0f) > c.RiseAt(c.UmbrellaBaseMetres * 0.5f));
            Assert.True(c.RiseAt(c.UmbrellaBaseMetres * 0.5f) > 0f);
            Assert.Equal(0f, c.RiseAt(c.UmbrellaBaseMetres), 4);
            Assert.Equal(0f, c.RiseAt(c.HeightMetres), 4);

            // The body of the umbrella does not rise. The two downwind ones sink, because
            // ash is falling out of them.
            Assert.Equal(0f, c.SegmentAt(EruptionColumn.ColumnSegments).DriftY, 4);
            Assert.True(c.SegmentAt(EruptionColumn.ColumnSegments + 1).DriftY < 0f);
            Assert.True(c.SegmentAt(EruptionColumn.ColumnSegments + 2).DriftY
                        < c.SegmentAt(EruptionColumn.ColumnSegments + 1).DriftY);
        }

        [Fact]
        public void TheColumnBendsDownwindAndTheUmbrellaTrailsFurther()
        {
            EruptionColumn c = Default(1f);

            // The wind is towards +X. The higher it is the more strongly it bends.
            Assert.Equal(0f, c.BendAt(0f), 4);
            Assert.True(c.BendAt(c.HeightMetres * 0.5f) > 0f);
            Assert.True(c.BendAt(c.HeightMetres) > c.BendAt(c.HeightMetres * 0.5f) * 2f);

            float lower = c.SegmentAt(0).OffsetX;
            float upper = c.SegmentAt(EruptionColumn.ColumnSegments - 1).OffsetX;
            Assert.True(upper > lower);

            // The downwind part of the umbrella is further out and lower than the body
            // (i.e. the side where ash falls from the underside).
            EruptionColumnSegment core = c.SegmentAt(EruptionColumn.ColumnSegments);
            EruptionColumnSegment tail = c.SegmentAt(EruptionColumn.ColumnSegments + 2);
            Assert.True(tail.OffsetX > core.OffsetX);
            Assert.True(tail.OffsetY < core.OffsetY);
            Assert.True(tail.Magnitude * tail.RadiusMetres * tail.RadiusMetres
                        < core.Magnitude * core.RadiusMetres * core.RadiusMetres,
                "the fallout tail should be thinner than the umbrella itself");

            // With no wind it does not bend by even 1 mm.
            var calm = new EruptionColumn(Vent, 1f, 0f, 0f, 0f);
            Assert.Equal(0f, calm.BendMetres, 4);
            for (int i = 0; i < calm.SegmentCount; i++)
            {
                Assert.Equal(0f, calm.SegmentAt(i).DriftX, 4);
                Assert.Equal(0f, calm.SegmentAt(i).DriftZ, 4);
            }
        }

        [Fact]
        public void TheParticleBudgetIsTheSameAsTheOldSinglePlume()
        {
            // ★ The particle count is max(100, PI r^2) x dt x magnitude x 0.01 x rate
            //   (IL §B-4), so r^2 x magnitude is proportional to "the particle count of that
            //   segment". The sum over all segments must match one of the old plumes
            //   (refRadius^2 x PlumeMagnitude).
            for (float unit = 0f; unit <= 1.0001f; unit += 0.25f)
            {
                EruptionColumn c = Default(unit);

                float total = 0f;
                for (int i = 0; i < c.SegmentCount; i++)
                {
                    EruptionColumnSegment s = c.SegmentAt(i);
                    total += s.Magnitude * s.RadiusMetres * s.RadiusMetres;
                }

                float refRadius = EruptionEffectPlan.PlumeRadiusMetres(Vent, unit);
                float budget = refRadius * refRadius * EruptionEffectPlan.PlumeMagnitude(unit);

                Assert.InRange(total, budget * 0.98f, budget * 1.02f);
            }
        }

        [Fact]
        public void TheColumnGrowsWithTheEruptionIntensity()
        {
            EruptionColumn weak = Default(0f);
            EruptionColumn strong = Default(1f);

            Assert.True(strong.HeightMetres > weak.HeightMetres * 2f);
            Assert.True(strong.UmbrellaRadiusMetres > weak.UmbrellaRadiusMetres);
            Assert.True(strong.BendMetres > weak.BendMetres);

            // The bigger the crater the taller the column (a huge column does not sit on a
            // small mountain).
            var small = new EruptionColumn(40f, 1f, 1f, 0f, 12f);
            Assert.True(small.HeightMetres < strong.HeightMetres);
        }

        [Fact]
        public void TheSwayIsOneSlowSineAndNothingElse()
        {
            // **A sway, not a flicker.** The amplitude is exactly as declared and it returns
            // precisely at the period.
            Assert.Equal(0f, EruptionColumn.SwayAt(0f), 4);
            Assert.Equal(0f, EruptionColumn.SwayAt(EruptionColumn.SwaySeconds), 3);
            Assert.Equal(EruptionColumn.SwayRadians,
                         EruptionColumn.SwayAt(EruptionColumn.SwaySeconds * 0.25f), 3);

            for (float t = 0f; t < 200f; t += 0.37f)
            {
                float v = EruptionColumn.SwayAt(t);
                Assert.InRange(v, -EruptionColumn.SwayRadians - 0.001f,
                               EruptionColumn.SwayRadians + 0.001f);
            }

            Assert.Equal(0f, EruptionColumn.SwayAt(float.NaN), 4);
        }

        [Fact]
        public void GarbageInputIsNeverNaN()
        {
            var columns = new[]
            {
                new EruptionColumn(float.NaN, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(0f, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(-10f, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(Vent, float.NaN, 1f, 0f, 10f),
                new EruptionColumn(Vent, 9f, 1f, 0f, 10f),
                new EruptionColumn(Vent, -9f, 1f, 0f, 10f),
                new EruptionColumn(Vent, 0.5f, float.NaN, float.NaN, 10f),
                new EruptionColumn(Vent, 0.5f, 0f, 0f, float.NaN),
                new EruptionColumn(Vent, 0.5f, 1f, 0f, -50f),
                new EruptionColumn(Vent, 0.5f, 1f, 0f, 9999f),
            };

            for (int c = 0; c < columns.Length; c++)
            {
                EruptionColumn column = columns[c];
                Assert.False(Bad(column.HeightMetres));
                Assert.False(Bad(column.UmbrellaRadiusMetres));
                Assert.False(Bad(column.BendMetres));

                for (int i = -2; i < column.SegmentCount + 2; i++)
                {
                    EruptionColumnSegment s = column.SegmentAt(i);
                    Assert.False(Bad(s.OffsetX) || Bad(s.OffsetY) || Bad(s.OffsetZ),
                                 "segment " + i + " of column " + c + " has a bad offset");
                    Assert.False(Bad(s.RadiusMetres) || Bad(s.HalfHeightMetres));
                    Assert.False(Bad(s.DriftX) || Bad(s.DriftY) || Bad(s.DriftZ));
                    Assert.False(Bad(s.Magnitude));

                    Assert.True(s.RadiusMetres >= EruptionColumn.MinRadiusMetres);
                    Assert.True(s.HalfHeightMetres >= 0f);
                    Assert.True(s.Magnitude >= 0f);

                    // Out of range is "a segment with density 0" —— neither an exception nor
                    // an invention.
                    if (i < 0 || i >= column.SegmentCount) Assert.Equal(0f, s.Magnitude, 4);
                }
            }
        }

        private static bool Bad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
