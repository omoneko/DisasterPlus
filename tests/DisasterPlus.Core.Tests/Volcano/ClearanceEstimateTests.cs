using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class ClearanceEstimateTests
    {
        [Fact]
        public void TheCellCountGrowsWithTheSquareOfTheRadius()
        {
            // The cells are 16 m square (§A-1). Double the radius and the cell count roughly
            // quadruples.
            int small = ClearanceEstimate.CellsInside(400f);
            int large = ClearanceEstimate.CellsInside(800f);
            Assert.True(large > small * 3, "expected roughly 4x, got " + large + " vs " + small);
            Assert.True(large < small * 5);
        }

        [Fact]
        public void TheFootprintAreaIsTheAreaOfTheCircle()
        {
            // The plan said Assert.Equal(..., -1), but xunit's precision overload passes the
            // value to Math.Round(value, digits), so a negative digit count is a run-time
            // exception. The intent ("it is the area of the circle") is kept, expressed as
            // an absolute tolerance.
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
            // "About 3 buildings" sounds like a fib. Below 10 we report the figure as it is.
            for (int i = 0; i <= 9; i++)
            {
                Assert.Equal(i, ClearanceEstimate.RoundedEstimate(i));
            }
        }

        [Fact]
        public void LargeCountsAreRoundedToTwoSignificantDigits()
        {
            // ★ Design doc §7.2: "state explicitly that it is an approximation". Report the
            //   exact number and the player reads it as "exactly that many will be destroyed".
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
