using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class TileSplitTests
    {
        private static void SplitRange(float radius,
                                       out int minX, out int minZ, out int maxX, out int maxZ)
        {
            Assert.True(TileSplit.CellRangeFor(0f, 0f, radius, out minX, out minZ, out maxX, out maxZ));
        }

        [Fact]
        public void NoTileIsWideEnoughToBeTruncatedAtOneHundredAndTwentyEight()
        {
            // ★ 罠 3。128×128 raw セルを超えた分は**タイル分割されず無言で切り捨てられ**、
            //   その部分は更新されないまま残る（§A-1、IL_026C）。
            int minX, minZ, maxX, maxZ;
            SplitRange(3000f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            Assert.True(count > 1, "a 3 km volcano must be split into more than one tile");

            for (int i = 0; i < count; i++)
            {
                int a, b, c, d;
                Assert.True(TileSplit.TileAt(i, minX, minZ, maxX, maxZ, out a, out b, out c, out d));
                Assert.True(c - a + 1 <= TileSplit.MaxPassedSide, "tile " + i + " is too wide");
                Assert.True(d - b + 1 <= TileSplit.MaxPassedSide, "tile " + i + " is too tall");
                Assert.True(TileSplit.MaxPassedSide < 128);
            }
        }

        [Fact]
        public void NoTileIsLargeEnoughToForceAMidFrameFlush()
        {
            // ★ 罠 3 の後半。10000 セル超の単発要求は入れ子を無視して即フラッシュする
            //   （§A-1 IL_0399）。測るのは**渡す矩形**であって内心のタイルではない。
            int minX, minZ, maxX, maxZ;
            SplitRange(3000f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            for (int i = 0; i < count; i++)
            {
                int a, b, c, d;
                TileSplit.TileAt(i, minX, minZ, maxX, maxZ, out a, out b, out c, out d);
                int cells = (c - a + 1) * (d - b + 1);
                Assert.True(cells <= TileSplit.MaxPassedCells, "tile " + i + " holds " + cells + " cells");
                Assert.True(TileSplit.MaxPassedCells <= 10000);
            }
        }

        [Fact]
        public void TheTilesTogetherCoverTheWholeRectangle()
        {
            // 覆い漏れがあると、その帯だけ更新されないまま残る（見た目にすぐ出る）。
            int minX, minZ, maxX, maxZ;
            SplitRange(1500f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);

            int w = maxX - minX + 1;
            int h = maxZ - minZ + 1;
            bool[] covered = new bool[w * h];

            for (int i = 0; i < count; i++)
            {
                int a, b, c, d;
                TileSplit.TileAt(i, minX, minZ, maxX, maxZ, out a, out b, out c, out d);
                for (int z = b; z <= d; z++)
                {
                    for (int x = a; x <= c; x++)
                    {
                        if (x < minX || x > maxX || z < minZ || z > maxZ) continue;
                        covered[(z - minZ) * w + (x - minX)] = true;
                    }
                }
            }

            for (int i = 0; i < covered.Length; i++)
            {
                Assert.True(covered[i], "cell " + i + " was never updated");
            }
        }

        [Fact]
        public void NeighbouringTilesOverlapByTheMargin()
        {
            // MakeCrack / MakeCrater と同じ ±2。境目で SmoothSample が古い値を
            // 掴むと、タイルの継ぎ目に段差が出る。
            int minX, minZ, maxX, maxZ;
            SplitRange(1500f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            Assert.True(count >= 4);

            int a0, b0, c0, d0, a1, b1, c1, d1;
            TileSplit.TileAt(0, minX, minZ, maxX, maxZ, out a0, out b0, out c0, out d0);
            TileSplit.TileAt(1, minX, minZ, maxX, maxZ, out a1, out b1, out c1, out d1);

            // タイル 0 と 1 は X 方向に隣接する（行優先で並べる）。
            Assert.True(c0 >= a1, "tile 0 and tile 1 do not overlap in X");
            Assert.True(c0 - a1 + 1 >= 2 * TileSplit.Margin,
                "the overlap is thinner than the margin: " + (c0 - a1 + 1));
        }

        [Fact]
        public void ASingleCellRectangleStillProducesOneTile()
        {
            Assert.Equal(1, TileSplit.TileCountFor(500, 500, 500, 500));
            int a, b, c, d;
            Assert.True(TileSplit.TileAt(0, 500, 500, 500, 500, out a, out b, out c, out d));
            Assert.True(a <= 500 && c >= 500 && b <= 500 && d >= 500);
        }

        [Fact]
        public void TheCellRangeIsClampedToTheMap()
        {
            // マップの隅に置かれた火山でも添字が配列の外に出ない（1081² の配列）。
            int minX, minZ, maxX, maxZ;
            Assert.True(TileSplit.CellRangeFor(-8600f, -8600f, 2000f,
                                               out minX, out minZ, out maxX, out maxZ));
            Assert.InRange(minX, 0, TileSplit.RawResolution);
            Assert.InRange(minZ, 0, TileSplit.RawResolution);
            Assert.InRange(maxX, 0, TileSplit.RawResolution);
            Assert.InRange(maxZ, 0, TileSplit.RawResolution);
            Assert.True(minX <= maxX && minZ <= maxZ);

            int a, b, c, d;
            Assert.False(TileSplit.CellRangeFor(float.NaN, 0f, 500f, out a, out b, out c, out d));
            Assert.False(TileSplit.CellRangeFor(0f, 0f, 0f, out a, out b, out c, out d));
            Assert.False(TileSplit.CellRangeFor(0f, 0f, -1f, out a, out b, out c, out d));
        }

        [Fact]
        public void AThreeKilometreVolcanoProducesAFiniteNumberOfTiles()
        {
            int minX, minZ, maxX, maxZ;
            SplitRange(3000f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            // 半径 3000 m = 直径 375 セル → 4x4 = 16 枚
            Assert.InRange(count, 2, 64);
        }

        [Fact]
        public void AnOutOfRangeTileIndexIsRefused()
        {
            int minX, minZ, maxX, maxZ;
            SplitRange(1500f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);

            int a, b, c, d;
            Assert.False(TileSplit.TileAt(-1, minX, minZ, maxX, maxZ, out a, out b, out c, out d));
            Assert.False(TileSplit.TileAt(count, minX, minZ, maxX, maxZ, out a, out b, out c, out d));
            Assert.Equal(0, TileSplit.TileCountFor(10, 10, 5, 5));
        }
    }
}
