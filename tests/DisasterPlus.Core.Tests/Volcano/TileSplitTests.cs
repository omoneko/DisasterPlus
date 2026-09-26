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
            // ★ Trap 3. Anything beyond 128×128 raw cells is **silently truncated rather
            //   than split into tiles**, and that part is left un-updated (§A-1, IL_026C).
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
            // ★ The second half of trap 3. A single request of more than 10000 cells ignores
            //   the nesting and flushes immediately (§A-1 IL_0399). What we measure is
            //   **the rectangle we pass in**, not the tile we have in mind.
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
            // Miss a patch of coverage and that strip alone is left un-updated
            // (it shows up immediately).
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
            // The same ±2 as MakeCrack / MakeCrater. If SmoothSample picks up a stale value
            // at the boundary, a step appears along the seam between tiles.
            int minX, minZ, maxX, maxZ;
            SplitRange(1500f, out minX, out minZ, out maxX, out maxZ);
            int count = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            Assert.True(count >= 4);

            int a0, b0, c0, d0, a1, b1, c1, d1;
            TileSplit.TileAt(0, minX, minZ, maxX, maxZ, out a0, out b0, out c0, out d0);
            TileSplit.TileAt(1, minX, minZ, maxX, maxZ, out a1, out b1, out c1, out d1);

            // Tiles 0 and 1 are adjacent along X (they are laid out in row-major order).
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
            // Even for a volcano placed in the corner of the map the indices stay inside the
            // array (an array of 1081²).
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
            // radius 3000 m = diameter 375 cells → 4x4 = 16 tiles
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

        [Fact]
        public void SinglePassIsAllowedOnlyUpToTheCoreTileSide()
        {
            Assert.True(TileSplit.FitsSinglePass(500, 500, 500, 500));
            Assert.True(TileSplit.FitsSinglePass(500, 500,
                                                500 + TileSplit.CoreTileSide - 1,
                                                500 + TileSplit.CoreTileSide - 1));

            // One cell bigger and it is not allowed. **Permit "it roughly fits" and the
            // overhang is left un-updated without so much as an exception**
            // (the truncation of §A-1).
            Assert.False(TileSplit.FitsSinglePass(500, 500,
                                                  500 + TileSplit.CoreTileSide,
                                                  500 + TileSplit.CoreTileSide - 1));
            Assert.False(TileSplit.FitsSinglePass(500, 500,
                                                  500 + TileSplit.CoreTileSide - 1,
                                                  500 + TileSplit.CoreTileSide));
            Assert.False(TileSplit.FitsSinglePass(10, 10, 5, 5));
        }

        [Fact]
        public void ASinglePassRectStaysUnderBothLimits()
        {
            for (int side = 1; side <= TileSplit.CoreTileSide; side++)
            {
                int minX = 400, minZ = 400;
                int maxX = minX + side - 1, maxZ = minZ + side - 1;
                Assert.True(TileSplit.FitsSinglePass(minX, minZ, maxX, maxZ));

                int a, b, c, d;
                Assert.True(TileSplit.ExpandForPass(minX, minZ, maxX, maxZ,
                                                    out a, out b, out c, out d));

                int width = c - a + 1;
                int depth = d - b + 1;
                Assert.Equal(side + 2 * TileSplit.Margin, width);
                Assert.True(width < 128 && depth < 128);
                Assert.True(width * depth < 10000);
                Assert.True(width <= TileSplit.MaxPassedSide);
            }
        }

        [Fact]
        public void ExpandingAtTheMapEdgeClampsInsteadOfWrapping()
        {
            int a, b, c, d;
            Assert.True(TileSplit.ExpandForPass(0, 0, 4, 4, out a, out b, out c, out d));
            Assert.Equal(0, a);
            Assert.Equal(0, b);
            Assert.Equal(6, c);
            Assert.Equal(6, d);

            Assert.True(TileSplit.ExpandForPass(TileSplit.RawResolution - 4,
                                                TileSplit.RawResolution - 4,
                                                TileSplit.RawResolution,
                                                TileSplit.RawResolution,
                                                out a, out b, out c, out d));
            Assert.Equal(TileSplit.RawResolution, c);
            Assert.Equal(TileSplit.RawResolution, d);

            Assert.False(TileSplit.ExpandForPass(10, 10, 5, 5, out a, out b, out c, out d));
        }
    }
}
