using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Pins down "which rectangle gets flushed when".
    ///
    /// **The two most important ones are:**
    ///   - <see cref="EveryPassStaysUnderBothLimits"/> —— the rectangle handed over must
    ///     always stay under both 128 cells (truncation) and 10000 cells (mid-frame flush).
    ///     If that breaks down, the terrain that overflowed **stays un-updated without so
    ///     much as an exception**.
    ///   - <see cref="NothingIsEverLeftUnflushed"/> —— every range written must eventually
    ///     be flushed. Miss one and part of the mountain freezes forever at the height it
    ///     had one tick earlier.
    /// </summary>
    public class UpliftFlushPlanTests
    {
        [Fact]
        public void NothingToFlushReturnsFalse()
        {
            var plan = new UpliftFlushPlan();
            int a, b, c, d;
            Assert.False(plan.Next(false, 0, 0, 0, 0, out a, out b, out c, out d));
            Assert.False(plan.HasPending);
            Assert.Equal(0, a);
            Assert.Equal(0, b);
            Assert.Equal(0, c);
            Assert.Equal(0, d);
        }

        [Fact]
        public void ASmallChangeGoesOutInOnePass()
        {
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            Assert.True(plan.Next(true, 500, 500, 540, 540, out minX, out minZ, out maxX, out maxZ));

            // The rectangle comes back already widened by TileSplit.Margin (the caller must
            // not add it again).
            Assert.Equal(500 - TileSplit.Margin, minX);
            Assert.Equal(500 - TileSplit.Margin, minZ);
            Assert.Equal(540 + TileSplit.Margin, maxX);
            Assert.Equal(540 + TileSplit.Margin, maxZ);

            // It went out in a single pass, so nothing is left pending = the next tick can
            // also go out in one pass.
            Assert.False(plan.HasPending);
            Assert.Equal(1, plan.TileCount);
            Assert.Equal(0, plan.Cursor);
        }

        [Fact]
        public void ABigChangeGoesOutOneTileAtATime()
        {
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            // 151x151 (the default stratovolcano footprint) cannot go out in one pass.
            Assert.True(plan.Next(true, 465, 465, 615, 615, out minX, out minZ, out maxX, out maxZ));
            Assert.True(plan.HasPending);
            Assert.Equal(4, plan.TileCount);

            // With no further changes it drains over the remaining tiles and empties.
            for (int i = 1; i < 4; i++)
            {
                Assert.True(plan.Next(false, 0, 0, 0, 0, out minX, out minZ, out maxX, out maxZ));
            }
            Assert.False(plan.HasPending);

            Assert.False(plan.Next(false, 0, 0, 0, 0, out minX, out minZ, out maxX, out maxZ));
        }

        [Fact]
        public void EveryPassStaysUnderBothLimits()
        {
            // Drive it with the same shape as the uplift itself (a disc spreading outwards
            // from the summit) and check that every rectangle returned stays under both
            // thresholds.
            var plan = new UpliftFlushPlan();
            const int centre = 540;

            for (int step = 1; step <= 80; step++)
            {
                int r = step;   // the front, in cells
                int minX, minZ, maxX, maxZ;
                if (!plan.Next(true, centre - r, centre - r, centre + r, centre + r,
                               out minX, out minZ, out maxX, out maxZ))
                {
                    continue;
                }

                int width = maxX - minX + 1;
                int depth = maxZ - minZ + 1;

                Assert.True(width <= TileSplit.MaxPassedSide, "width " + width);
                Assert.True(depth <= TileSplit.MaxPassedSide, "depth " + depth);
                Assert.True(width < 128 && depth < 128, "a side reached the truncation limit");
                Assert.True(width * depth <= TileSplit.MaxPassedCells, "cells " + (width * depth));
                Assert.True(width * depth < 10000, "the pass would force a mid-frame flush");
            }
        }

        [Fact]
        public void NothingIsEverLeftUnflushed()
        {
            // Every changed cell must be flushed at some point. **Turn the union into an
            // overwrite and this one fails.**
            const int size = 200;
            const int origin = 440;

            var written = new bool[size * size];
            var shown = new bool[size * size];
            var plan = new UpliftFlushPlan();

            const int centre = 540;

            // A spreading disc over 60 ticks.
            for (int step = 1; step <= 60; step++)
            {
                int r = step + 20;
                int dMinX = centre - r, dMinZ = centre - r, dMaxX = centre + r, dMaxZ = centre + r;

                for (int z = dMinZ; z <= dMaxZ; z++)
                {
                    for (int x = dMinX; x <= dMaxX; x++)
                    {
                        written[(z - origin) * size + (x - origin)] = true;
                    }
                }

                int minX, minZ, maxX, maxZ;
                if (plan.Next(true, dMinX, dMinZ, dMaxX, dMaxZ,
                              out minX, out minZ, out maxX, out maxZ))
                {
                    Mark(shown, size, origin, minX, minZ, maxX, maxZ);
                }
            }

            // Once the writing is done, drain until nothing is pending (this is what ⑤ must
            // do before it carves the crater).
            int guard = 0;
            while (plan.HasPending && guard++ < 32)
            {
                int minX, minZ, maxX, maxZ;
                if (plan.Next(false, 0, 0, 0, 0, out minX, out minZ, out maxX, out maxZ))
                {
                    Mark(shown, size, origin, minX, minZ, maxX, maxZ);
                }
            }

            Assert.False(plan.HasPending, "the plan never drained");

            for (int i = 0; i < written.Length; i++)
            {
                if (written[i] && !shown[i])
                {
                    Assert.Fail("a written cell was never flushed (index " + i + ")");
                }
            }
        }

        [Fact]
        public void AShrinkingChangeDoesNotDropTheRestOfTheCycle()
        {
            // Even if the changed range shrinks partway through the round-robin, the range
            // already accumulated is not lost.
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            Assert.True(plan.Next(true, 465, 465, 615, 615, out minX, out minZ, out maxX, out maxZ));
            Assert.Equal(4, plan.TileCount);

            // On the next tick only a small rectangle in the middle changed. **The four
            // tiles still remain.**
            Assert.True(plan.Next(true, 530, 530, 550, 550, out minX, out minZ, out maxX, out maxZ));
            Assert.True(plan.HasPending);
            Assert.Equal(4, plan.TileCount);
        }

        [Fact]
        public void ResetForgetsEverything()
        {
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            plan.Next(true, 465, 465, 615, 615, out minX, out minZ, out maxX, out maxZ);
            Assert.True(plan.HasPending);

            plan.Reset();
            Assert.False(plan.HasPending);
            Assert.Equal(1, plan.TileCount);
            Assert.Equal(0, plan.Cursor);
            Assert.False(plan.Next(false, 0, 0, 0, 0, out minX, out minZ, out maxX, out maxZ));
        }

        [Fact]
        public void ABrokenRectIsIgnoredRatherThanThrowing()
        {
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            // min > max (the caller miscounted). Do not accumulate it.
            Assert.False(plan.Next(true, 600, 600, 500, 500, out minX, out minZ, out maxX, out maxZ));
            Assert.False(plan.HasPending);
        }

        private static void Mark(bool[] shown, int size, int origin,
                                 int minX, int minZ, int maxX, int maxZ)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                int zi = z - origin;
                if (zi < 0 || zi >= size) continue;
                for (int x = minX; x <= maxX; x++)
                {
                    int xi = x - origin;
                    if (xi < 0 || xi >= size) continue;
                    shown[zi * size + xi] = true;
                }
            }
        }
    }
}
