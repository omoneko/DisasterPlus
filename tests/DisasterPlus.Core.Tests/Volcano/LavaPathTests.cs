using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class LavaPathTests
    {
        [Fact]
        public void AStepMovesExactlyTheStepLength()
        {
            Vec2 next;
            Assert.True(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), 12f, out next));
            Assert.Equal(12f, next.X, 3);
            Assert.Equal(0f, next.Z, 3);
        }

        [Fact]
        public void TheDirectionIsNormalisedBeforeTheStepIsTaken()
        {
            // Even if the caller passes a non-unit vector, the step length does not change.
            Vec2 a, b;
            LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(3f, 4f), 10f, out a);
            LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(30f, 40f), 10f, out b);
            Assert.Equal(a.X, b.X, 3);
            Assert.Equal(a.Z, b.Z, 3);
            float len = (float)System.Math.Sqrt(a.X * a.X + a.Z * a.Z);
            Assert.Equal(10f, len, 3);
        }

        [Fact]
        public void AFlatOrPooledSpotStopsTheFlowInsteadOfDrifting()
        {
            // Carrying on "forward anyway" over flat ground makes the lava sail past the
            // hollow and keep running to the edge of the map. Stopping and pooling is right.
            Vec2 next;
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(0f, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f),
                new Vec2(LavaPath.MinSlope * 0.4f, 0f), 12f, out next));
        }

        [Fact]
        public void GarbageInputStopsTheFlowInsteadOfProducingNaN()
        {
            // "Lava at a NaN position" silently breaks both the drawing and the grid scan.
            Vec2 next;
            Assert.False(LavaPath.NextPosition(new Vec2(float.NaN, 0f), new Vec2(1f, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(float.NaN, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), float.NaN, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), 0f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), -12f, out next));
        }

        [Fact]
        public void TheInitialDirectionsAreSpreadAroundTheCrater()
        {
            // If they all set off the same way, only one side of the volcano gets covered.
            const int n = 4;
            Vec2[] dirs = new Vec2[n];
            for (int i = 0; i < n; i++) dirs[i] = LavaPath.InitialDirection(7u, i, n);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float dot = dirs[i].X * dirs[j].X + dirs[i].Z * dirs[j].Z;
                    Assert.True(dot < 0.9f,
                        "flows " + i + " and " + j + " point almost the same way (dot=" + dot + ")");
                }
            }
        }

        [Fact]
        public void TheInitialDirectionsAreUnitLengthAndDeterministic()
        {
            for (int i = 0; i < 8; i++)
            {
                Vec2 a = LavaPath.InitialDirection(11u, i, 8);
                Vec2 b = LavaPath.InitialDirection(11u, i, 8);
                Assert.Equal(a.X, b.X, 5);
                Assert.Equal(a.Z, b.Z, 5);
                float len = (float)System.Math.Sqrt(a.X * a.X + a.Z * a.Z);
                Assert.Equal(1f, len, 3);
            }
        }

        [Fact]
        public void DifferentSeedsGiveDifferentFans()
        {
            Vec2 a = LavaPath.InitialDirection(1u, 0, 4);
            Vec2 b = LavaPath.InitialDirection(2u, 0, 4);
            Assert.True(a.X != b.X || a.Z != b.Z, "the fan does not depend on the seed");
        }

        [Fact]
        public void DegenerateFlowCountsAreHandled()
        {
            Vec2 one = LavaPath.InitialDirection(3u, 0, 1);
            float len = (float)System.Math.Sqrt(one.X * one.X + one.Z * one.Z);
            Assert.Equal(1f, len, 3);
            // Even an out-of-range index returns a unit vector and never produces NaN.
            Vec2 bad = LavaPath.InitialDirection(3u, 9, 4);
            Assert.False(float.IsNaN(bad.X) || float.IsNaN(bad.Z));
            Vec2 zero = LavaPath.InitialDirection(3u, 0, 0);
            Assert.False(float.IsNaN(zero.X) || float.IsNaN(zero.Z));
        }

        [Fact]
        public void TheFlowWidensAsItTravelsButIsCapped()
        {
            float near = LavaPath.SpreadRadiusFor(0f);
            float mid = LavaPath.SpreadRadiusFor(1000f);
            float far = LavaPath.SpreadRadiusFor(100000f);
            Assert.Equal(LavaPath.SpreadBaseMetres, near, 3);
            Assert.True(mid > near);
            Assert.Equal(LavaPath.SpreadMaxMetres, far, 3);
            Assert.Equal(LavaPath.SpreadBaseMetres, LavaPath.SpreadRadiusFor(float.NaN), 3);
            Assert.Equal(LavaPath.SpreadBaseMetres, LavaPath.SpreadRadiusFor(-5f), 3);
        }

        [Fact]
        public void TheVentSitsOutsideTheCraterRim()
        {
            // ★ Vent right on top of the rim and the downhill direction points into the
            //   crater, so the lava pools in the hollow. It must always sit outside both
            //   the "ratio" and the "absolute" clearance.
            for (float crater = 40f; crater <= 400f; crater += 20f)
            {
                float vent = LavaPath.VentRadiusMetres(crater);
                Assert.True(vent >= crater * LavaPath.VentRimClearanceFactor - 0.001f);
                Assert.True(vent >= crater + LavaPath.VentRimClearanceMetres - 0.001f);
                // Even so, not so far out that it leaves the mountain (even the smallest
                // mountain has a radius of 250 m).
                Assert.True(vent < 250f || crater > 180f);
            }

            // Even when it cannot be read, do not vent from the centre (never return 0).
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(0f), 3);
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(float.NaN), 3);
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(-1f), 3);
        }

        [Fact]
        public void TheStepBudgetIsFiniteAndDeclared()
        {
            // Do not create "lava that never stops". The cap declares itself as a constant.
            Assert.InRange(LavaPath.MaxSteps, 1, 4096);
            Assert.True(LavaPath.StepMetres > 0f);
            Assert.True(LavaPath.MinSlope > 0f);
            Assert.True(LavaPath.SpreadMaxMetres > LavaPath.SpreadBaseMetres);
        }
    }
}
