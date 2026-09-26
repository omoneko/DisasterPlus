using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class LavaRibbonTests
    {
        private static bool BuildStraight(int count, out Vec3[] v, out float[] uv, out int[] tri)
        {
            Vec2[] pts = new Vec2[count];
            float[] widths = new float[count];
            for (int i = 0; i < count; i++)
            {
                pts[i] = new Vec2(i * 12f, 0f);
                widths[i] = 20f;
            }
            return LavaRibbon.Build(pts, count, widths, 0.5f, out v, out uv, out tri);
        }

        [Fact]
        public void TheVertexAndTriangleCountsMatchTheDeclaredFormulas()
        {
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(BuildStraight(10, out v, out uv, out tri));
            Assert.Equal(LavaRibbon.VertexCountFor(10), v.Length);
            Assert.Equal(LavaRibbon.TriangleIndexCountFor(10), tri.Length);
            Assert.Equal(v.Length * 2, uv.Length);
        }

        [Fact]
        public void EveryTriangleIndexIsInsideTheVertexArray()
        {
            // An out-of-range index raises no exception on the Unity side; it quietly breaks
            // the rendering.
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(BuildStraight(24, out v, out uv, out tri));
            Assert.Equal(0, tri.Length % 3);
            for (int i = 0; i < tri.Length; i++)
            {
                Assert.InRange(tri[i], 0, v.Length - 1);
            }
        }

        [Fact]
        public void TheRibbonIsAsWideAsItWasAsked()
        {
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(BuildStraight(4, out v, out uv, out tri));
            // The first two vertices are the left and right of the first point. The polyline
            // runs along X, so Z is ±10.
            float span = System.Math.Abs(v[0].Z - v[1].Z);
            Assert.Equal(20f, span, 2);
        }

        [Fact]
        public void UvsAreInsideTheUnitSquare()
        {
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(BuildStraight(16, out v, out uv, out tri));
            for (int i = 0; i < uv.Length; i++)
            {
                Assert.InRange(uv[i], 0f, 1f);
            }
        }

        [Fact]
        public void TheGeometryIsDeterministicAndHasNoNaN()
        {
            // Never cause "the shape of the lava changed after reloading the city".
            Vec3[] a; float[] uvA; int[] triA;
            Vec3[] b; float[] uvB; int[] triB;
            Assert.True(BuildStraight(20, out a, out uvA, out triA));
            Assert.True(BuildStraight(20, out b, out uvB, out triB));
            for (int i = 0; i < a.Length; i++)
            {
                Assert.False(float.IsNaN(a[i].X) || float.IsNaN(a[i].Y) || float.IsNaN(a[i].Z),
                    "NaN vertex at " + i);
                Assert.Equal(a[i].X, b[i].X, 5);
                Assert.Equal(a[i].Y, b[i].Y, 5);
                Assert.Equal(a[i].Z, b[i].Z, 5);
            }
        }

        [Fact]
        public void TooFewPointsProduceNothingRatherThanADegenerateMesh()
        {
            Vec3[] v; float[] uv; int[] tri;
            Vec2[] pts = new Vec2[] { new Vec2(0f, 0f) };
            float[] widths = new float[] { 20f };
            Assert.False(LavaRibbon.Build(pts, 1, widths, 0.5f, out v, out uv, out tri));
            Assert.False(LavaRibbon.Build(pts, 0, widths, 0.5f, out v, out uv, out tri));
            Assert.False(LavaRibbon.Build(null, 4, widths, 0.5f, out v, out uv, out tri));
            Assert.False(LavaRibbon.Build(pts, 4, null, 0.5f, out v, out uv, out tri));
        }

        [Fact]
        public void RepeatedPointsDoNotProduceNaNNormals()
        {
            // When the lava stops, the same point repeats. It must not break when the
            // direction becomes 0.
            Vec2[] pts = new Vec2[] { new Vec2(0f, 0f), new Vec2(0f, 0f), new Vec2(12f, 0f) };
            float[] widths = new float[] { 20f, 20f, 20f };
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(LavaRibbon.Build(pts, 3, widths, 0.5f, out v, out uv, out tri));
            for (int i = 0; i < v.Length; i++)
            {
                Assert.False(float.IsNaN(v[i].X) || float.IsNaN(v[i].Z), "NaN vertex at " + i);
            }
        }

        [Fact]
        public void ThePointBudgetIsFiniteAndTheWidthHasAFloor()
        {
            Assert.InRange(LavaRibbon.MaxPoints, 2, 4096);
            Assert.True(LavaRibbon.MinWidthMetres > 0f);

            // A point count over the limit is cut at the limit (the arrays never grow without
            // bound).
            int over = LavaRibbon.MaxPoints + 50;
            Vec2[] pts = new Vec2[over];
            float[] widths = new float[over];
            for (int i = 0; i < over; i++) { pts[i] = new Vec2(i * 3f, 0f); widths[i] = 0f; }
            Vec3[] v; float[] uv; int[] tri;
            Assert.True(LavaRibbon.Build(pts, over, widths, 0.5f, out v, out uv, out tri));
            Assert.True(v.Length <= LavaRibbon.VertexCountFor(LavaRibbon.MaxPoints));
        }
    }
}
