using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class ClearanceEstimateTests
    {
        [Fact]
        public void TheCellCountGrowsWithTheSquareOfTheRadius()
        {
            // セルは 16 m 角（§A-1）。半径を 2 倍にすればセルは約 4 倍。
            int small = ClearanceEstimate.CellsInside(400f);
            int large = ClearanceEstimate.CellsInside(800f);
            Assert.True(large > small * 3, "expected roughly 4x, got " + large + " vs " + small);
            Assert.True(large < small * 5);
        }

        [Fact]
        public void TheFootprintAreaIsTheAreaOfTheCircle()
        {
            // 計画は Assert.Equal(..., -1) と書いていたが、xunit の precision 版は
            // Math.Round(value, digits) に渡すので負の桁数は実行時例外になる。
            // 意図（「円の面積であること」）はそのままに、絶対誤差で書く。
            float area = ClearanceEstimate.FootprintAreaSquareMetres(1000f);
            Assert.InRange(area, 3141592.6f - 1f, 3141592.6f + 1f);
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            Assert.Equal(0, ClearanceEstimate.CellsInside(float.NaN));
            Assert.Equal(0, ClearanceEstimate.CellsInside(-1f));
            Assert.Equal(0, ClearanceEstimate.CellsInside(0f));
            Assert.Equal(0f, ClearanceEstimate.FootprintAreaSquareMetres(float.NaN), 4);
            Assert.Equal(0f, ClearanceEstimate.FootprintAreaSquareMetres(-1f), 4);
        }

        [Fact]
        public void SmallCountsAreShownExactly()
        {
            // 「およそ 3 棟」は嘘くさい。10 未満はそのまま出す。
            for (int i = 0; i <= 9; i++)
            {
                Assert.Equal(i, ClearanceEstimate.RoundedEstimate(i));
            }
        }

        [Fact]
        public void LargeCountsAreRoundedToTwoSignificantDigits()
        {
            // ★ 設計書 §7.2:「概数であることも明示する」。実数をそのまま出すと
            //   プレイヤーは「ぴったりその数だけ壊れる」と読む。
            Assert.Equal(240, ClearanceEstimate.RoundedEstimate(243));
            Assert.Equal(240, ClearanceEstimate.RoundedEstimate(238));
            Assert.Equal(1200, ClearanceEstimate.RoundedEstimate(1234));
            Assert.Equal(10, ClearanceEstimate.RoundedEstimate(10));
            Assert.Equal(99, ClearanceEstimate.RoundedEstimate(99));
        }

        [Fact]
        public void TheEstimateIsNeverNegativeAndNeverDecreases()
        {
            Assert.Equal(0, ClearanceEstimate.RoundedEstimate(-5));
            int previous = -1;
            for (int i = 0; i < 5000; i += 7)
            {
                int r = ClearanceEstimate.RoundedEstimate(i);
                Assert.True(r >= previous, "the estimate went backwards at " + i);
                Assert.True(r >= 0);
                previous = r;
            }
        }
    }
}
