using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class FaultBandTests
    {
        // 角度 0 のとき dir = (-sin 0, cos 0) = (0, 1)、つまり断層は Z 軸に沿う。
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: 1000f, width: 100f);
        }

        [Fact]
        public void DirectionMatchesTheVanillaFormula()
        {
            var band = Sample();
            Assert.Equal(0f, band.Direction.X, 4);
            Assert.Equal(1f, band.Direction.Z, 4);

            var rotated = new FaultBand(new Vec2(0f, 0f), (float)(Math.PI / 2), 1000f, 100f);
            Assert.Equal(-1f, rotated.Direction.X, 3);
            Assert.Equal(0f, rotated.Direction.Z, 3);
        }

        [Fact]
        public void HalfWidthTapersToZeroAtTheEnds()
        {
            var band = Sample();
            // w = W * (1 - 4t²) なので、|t| = 0.5 でちょうど 0。
            Assert.Equal(200f, band.HalfWidthAt(0f), 3);      // 2 * W * (1-0)
            Assert.Equal(0f, band.HalfWidthAt(0.5f), 3);
            Assert.True(band.HalfWidthAt(0.4f) > 0f);
            Assert.True(band.HalfWidthAt(0.4f) < band.HalfWidthAt(0f));
        }

        [Fact]
        public void EpicentreIsInside()
        {
            Assert.True(Sample().Contains(new Vec2(0f, 0f)));
        }

        [Fact]
        public void BeyondTheRuptureRangeIsOutside()
        {
            var band = Sample();
            // 円盤の中心は t ∈ [-0.4, 0.4]。L = 1000 なので |z| > 400 は帯の外。
            Assert.False(band.Contains(new Vec2(0f, 450f)));
            Assert.True(band.Contains(new Vec2(0f, 350f)));
        }

        [Fact]
        public void AcrossTheFaultIsBoundedByTwiceTheTaperedWidth()
        {
            var band = Sample();
            Assert.True(band.Contains(new Vec2(190f, 0f)));    // 2w(0) = 200
            Assert.False(band.Contains(new Vec2(210f, 0f)));
        }

        [Fact]
        public void UnknownGeometryNeverClaimsContainment()
        {
            // プレハブが読めなかったとき（length = width = 0）は、
            // 「内側」とも「外側」とも断定しない。Contains は常に false、Known も false。
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            Assert.False(unknown.Known);
            Assert.False(unknown.Contains(new Vec2(0f, 0f)));
        }
    }
}
