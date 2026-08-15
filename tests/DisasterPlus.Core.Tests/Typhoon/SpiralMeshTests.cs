using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class SpiralMeshTests
    {
        private static void BuildInto(float inner, float outer, float height,
                                      out Vec3[] v, out float[] uv, out int[] tri)
        {
            v = new Vec3[SpiralMesh.VertexCount];
            uv = new float[SpiralMesh.VertexCount * 2];
            tri = new int[SpiralMesh.TriangleIndexCount];
            SpiralMesh.Build(inner, outer, height, v, uv, tri);
        }

        [Fact]
        public void TheCountsAreConsistentWithEachOther()
        {
            Assert.True(SpiralMesh.VertexCount > 0);
            Assert.Equal(0, SpiralMesh.TriangleIndexCount % 3);
            // 竜巻の 16250 頂点より 1 桁小さいこと。台風の雲は上空に 1 枚あればよい。
            Assert.True(SpiralMesh.VertexCount < 16250);
        }

        [Fact]
        public void EveryTriangleIndexPointsAtARealVertex()
        {
            // ★ 範囲外の索引は Unity 側で例外にならず、**何も描画されない**か
            //   ジオメトリが壊れるだけで済むことがある。ここで固定する。
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            for (int i = 0; i < tri.Length; i++)
            {
                Assert.InRange(tri[i], 0, v.Length - 1);
            }
        }

        [Fact]
        public void NoTriangleIsDegenerate()
        {
            // 3 頂点のうち 2 つが同じ索引だと面積 0 の三角形になり、
            // 「頂点はあるのに何も見えない」という最も調べにくい形になる。
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                Assert.False(tri[i] == tri[i + 1] || tri[i + 1] == tri[i + 2]
                             || tri[i] == tri[i + 2],
                    "degenerate triangle at index " + i);
            }
        }

        [Fact]
        public void ThereIsAHoleInTheMiddleForTheEye()
        {
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            for (int i = 0; i < v.Length; i++)
            {
                float r = (float)System.Math.Sqrt(v[i].X * v[i].X + v[i].Z * v[i].Z);
                Assert.True(r >= 200f - 0.5f, "vertex " + i + " is inside the eye (r=" + r + ")");
            }
        }

        [Fact]
        public void EveryVertexStaysInsideTheOuterRadius()
        {
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            for (int i = 0; i < v.Length; i++)
            {
                float r = (float)System.Math.Sqrt(v[i].X * v[i].X + v[i].Z * v[i].Z);
                Assert.True(r <= 2000f + 0.5f, "vertex " + i + " is outside (r=" + r + ")");
            }
        }

        [Fact]
        public void TheMeshIsFlatEnoughToBeACloudLayer()
        {
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            for (int i = 0; i < v.Length; i++)
            {
                Assert.InRange(v[i].Y, -0.5f, 120.5f);
            }
        }

        [Fact]
        public void UvsAreInsideTheUnitSquare()
        {
            Vec3[] v; float[] uv; int[] tri;
            BuildInto(200f, 2000f, 120f, out v, out uv, out tri);
            Assert.Equal(v.Length * 2, uv.Length);
            for (int i = 0; i < uv.Length; i++)
            {
                Assert.InRange(uv[i], 0f, 1f);
            }
        }

        [Fact]
        public void TheGeometryIsDeterministicAndHasNoNaN()
        {
            // 「都市を読み直したら雲の形が変わった」を起こさない。
            Vec3[] a; float[] uvA; int[] triA;
            Vec3[] b; float[] uvB; int[] triB;
            BuildInto(200f, 2000f, 120f, out a, out uvA, out triA);
            BuildInto(200f, 2000f, 120f, out b, out uvB, out triB);

            for (int i = 0; i < a.Length; i++)
            {
                Assert.False(float.IsNaN(a[i].X) || float.IsNaN(a[i].Y) || float.IsNaN(a[i].Z),
                    "NaN vertex at " + i);
                Assert.Equal(a[i].X, b[i].X, 5);
                Assert.Equal(a[i].Y, b[i].Y, 5);
                Assert.Equal(a[i].Z, b[i].Z, 5);
            }
        }
    }
}
