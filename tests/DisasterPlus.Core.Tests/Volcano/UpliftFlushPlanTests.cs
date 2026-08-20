using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 「どの矩形をいつ流すか」を固定する。
    ///
    /// **いちばん重要なのは 2 件:**
    ///   - <see cref="EveryPassStaysUnderBothLimits"/> —— 渡す矩形が
    ///     128 セル（切り捨て）と 10000 セル（途中フラッシュ）の両方を必ず下回ること。
    ///     ここが崩れると、はみ出した地形が**例外も出さずに更新されないまま残る**。
    ///   - <see cref="NothingIsEverLeftUnflushed"/> —— 書いた範囲が必ず全部流れること。
    ///     漏れると、山の一部が 1 tick 前の高さのまま永久に固まる。
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

            // TileSplit.Margin ぶん広げた矩形がそのまま返る（呼び出し側で足し直さない）。
            Assert.Equal(500 - TileSplit.Margin, minX);
            Assert.Equal(500 - TileSplit.Margin, minZ);
            Assert.Equal(540 + TileSplit.Margin, maxX);
            Assert.Equal(540 + TileSplit.Margin, maxZ);

            // 1 回で出し切ったので溜まりは残らない ＝ 次の tick も 1 回で出せる。
            Assert.False(plan.HasPending);
            Assert.Equal(1, plan.TileCount);
            Assert.Equal(0, plan.Cursor);
        }

        [Fact]
        public void ABigChangeGoesOutOneTileAtATime()
        {
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            // 151x151（既定の成層火山のフットプリント）は 1 回では出せない。
            Assert.True(plan.Next(true, 465, 465, 615, 615, out minX, out minZ, out maxX, out maxZ));
            Assert.True(plan.HasPending);
            Assert.Equal(4, plan.TileCount);

            // 追加の変更が無ければ、残りの枚数で出し切って空になる。
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
            // 隆起そのものと同じ形（山頂から外へ広がる円盤）で回し、
            // 返ってきた全部の矩形が両方の閾値を下回ることを見る。
            var plan = new UpliftFlushPlan();
            const int centre = 540;

            for (int step = 1; step <= 80; step++)
            {
                int r = step;   // セル単位の前線
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
            // 変わったセルが必ずいつか流れること。**union を上書きに変えるとここが落ちる。**
            const int size = 200;
            const int origin = 440;

            var written = new bool[size * size];
            var shown = new bool[size * size];
            var plan = new UpliftFlushPlan();

            const int centre = 540;

            // 広がる円盤を 60 tick ぶん。
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

            // 書き終えたあと、溜まりが空になるまで流し切る（⑤が火口を彫る前にやること）。
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
            // 総当たりの途中で変更範囲が縮んでも、溜まっていた範囲は消えない。
            var plan = new UpliftFlushPlan();
            int minX, minZ, maxX, maxZ;

            Assert.True(plan.Next(true, 465, 465, 615, 615, out minX, out minZ, out maxX, out maxZ));
            Assert.Equal(4, plan.TileCount);

            // 次の tick は中央の小さな矩形しか変わらなかった。**それでも 4 枚は残る。**
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

            // min > max（呼び出し側の数え損ね）。溜めない。
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
