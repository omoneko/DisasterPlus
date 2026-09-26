using DisasterPlus.Core.Forecast;
using Xunit;

namespace DisasterPlus.Core.Tests.Forecast
{
    public class WindDirectionTests
    {
        [Fact]
        public void CardinalPoints()
        {
            Assert.Equal("N", WindDirection.LabelOf(0f));
            Assert.Equal("E", WindDirection.LabelOf(90f));
            Assert.Equal("S", WindDirection.LabelOf(180f));
            Assert.Equal("W", WindDirection.LabelOf(270f));
        }

        [Fact]
        public void IntercardinalPoints()
        {
            Assert.Equal("NE", WindDirection.LabelOf(45f));
            Assert.Equal("SW", WindDirection.LabelOf(225f));
        }

        [Fact]
        public void SecondaryIntercardinal()
        {
            Assert.Equal("NNE", WindDirection.LabelOf(22.5f));
            Assert.Equal("WNW", WindDirection.LabelOf(292.5f));
        }

        [Fact]
        public void WrapsAboveThreeSixty()
        {
            Assert.Equal("N", WindDirection.LabelOf(360f));
            Assert.Equal("E", WindDirection.LabelOf(450f));
            Assert.Equal("N", WindDirection.LabelOf(720f));
        }

        [Fact]
        public void HandlesNegativeAngles()
        {
            Assert.Equal("N", WindDirection.LabelOf(-360f));
            Assert.Equal("W", WindDirection.LabelOf(-90f));
            Assert.Equal("NNW", WindDirection.LabelOf(-22.5f));
        }

        [Fact]
        public void BoundariesRoundToNearestSector()
        {
            // The sector width is 22.5 degrees. At the 11.25 degree boundary it enters
            // the next sector.
            Assert.Equal("N", WindDirection.LabelOf(11.24f));
            Assert.Equal("NNE", WindDirection.LabelOf(11.26f));
        }

        [Fact]
        public void NaN_ReturnsUnknown()
        {
            Assert.Equal("?", WindDirection.LabelOf(float.NaN));
        }

        [Fact]
        public void AlwaysReturnsANonEmptyLabel()
        {
            for (float d = -720f; d <= 720f; d += 3.7f)
            {
                Assert.False(string.IsNullOrEmpty(WindDirection.LabelOf(d)));
            }
        }
    }
}
