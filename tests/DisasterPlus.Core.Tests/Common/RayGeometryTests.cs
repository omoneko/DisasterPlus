using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class RayGeometryTests
    {
        private class FlatGround : IHeightSampler
        {
            private readonly float _h;
            public FlatGround(float h) { _h = h; }
            public float SampleHeight(float x, float z) { return _h; }
        }

        /// <summary>X が増えるほど高くなる斜面。</summary>
        private class Slope : IHeightSampler
        {
            public float SampleHeight(float x, float z) { return x * 0.5f; }
        }

        private static Vec3 Down45()
        {
            float k = (float)(1.0 / System.Math.Sqrt(2.0));
            return new Vec3(k, -k, 0f);
        }

        [Fact]
        public void StraightDown_HitsFlatGroundAtSampledHeight()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(100f, 500f, 200f), new Vec3(0f, -1f, 0f),
                new FlatGround(80f), 2000f, out hit);

            Assert.True(ok);
            Assert.Equal(100f, hit.X, 1);
            Assert.Equal(200f, hit.Z, 1);
            Assert.Equal(80f, hit.Y, 1);
        }

        [Fact]
        public void Diagonal_HitsFlatGround_AtGeometricallyCorrectPoint()
        {
            // 高さ 100 から 45 度で降りる → 水平に 100 進んだ地点で高さ 0 に到達
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(0f, 100f, 0f), Down45(), new FlatGround(0f), 2000f, out hit);

            Assert.True(ok);
            Assert.Equal(100f, hit.X, 0);
            Assert.Equal(0f, hit.Y, 0);
        }

        [Fact]
        public void RayPointingUp_Misses()
        {
            Vec3 hit;
            Assert.False(RayGeometry.IntersectTerrain(
                new Vec3(0f, 100f, 0f), new Vec3(0f, 1f, 0f),
                new FlatGround(0f), 2000f, out hit));
        }

        [Fact]
        public void ShortMaxDistance_Misses()
        {
            Vec3 hit;
            Assert.False(RayGeometry.IntersectTerrain(
                new Vec3(0f, 1000f, 0f), new Vec3(0f, -1f, 0f),
                new FlatGround(0f), 50f, out hit));
        }

        [Fact]
        public void OriginAlreadyBelowGround_HitsImmediately()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(10f, -5f, 10f), new Vec3(0f, -1f, 0f),
                new FlatGround(0f), 500f, out hit);
            Assert.True(ok);
        }

        [Fact]
        public void Slope_HitIsOnTheSurface()
        {
            Vec3 hit;
            bool ok = RayGeometry.IntersectTerrain(
                new Vec3(0f, 400f, 0f), Down45(), new Slope(), 4000f, out hit);

            Assert.True(ok);
            // 収束した点で「レイの高さ ≒ 地形の高さ」になっていること
            Assert.Equal(new Slope().SampleHeight(hit.X, hit.Z), hit.Y, 0);
        }

        [Fact]
        public void IsDeterministic()
        {
            Vec3 a, b;
            RayGeometry.IntersectTerrain(new Vec3(5f, 300f, 5f), Down45(), new Slope(), 3000f, out a);
            RayGeometry.IntersectTerrain(new Vec3(5f, 300f, 5f), Down45(), new Slope(), 3000f, out b);
            Assert.Equal(a.X, b.X, 5);
            Assert.Equal(a.Y, b.Y, 5);
            Assert.Equal(a.Z, b.Z, 5);
        }
    }
}
