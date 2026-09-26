using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class VolcanoSizeScaleTests
    {
        [Fact]
        public void TheVanillaDefaultMeansExactlyTheConfiguredSize()
        {
            // At 55 (the default intensity of the game's own disasters) the factor is 1.0.
            // Let this drift and a player who never touched the slider builds a mountain
            // that differs from the settings.
            Assert.Equal(1f, VolcanoSizeScale.ScaleFor(VolcanoSizeScale.AnchorRaw), 4);
            Assert.Equal(1200f, VolcanoSizeScale.Apply(1200f, VolcanoSizeScale.ScaleFor(55)), 3);
        }

        [Fact]
        public void DoublingTheRawValueDoublesTheMountain()
        {
            Assert.Equal(2f, VolcanoSizeScale.ScaleFor(110), 4);
            Assert.Equal(2400f, VolcanoSizeScale.Apply(1200f, VolcanoSizeScale.ScaleFor(110)), 3);
        }

        [Fact]
        public void ZeroIsNotAZeroSizedVolcano()
        {
            // A factor of 0 is not "a small mountain" but "nothing happens when you press it".
            Assert.Equal(VolcanoSizeScale.MinScale, VolcanoSizeScale.ScaleFor(0), 4);
            Assert.Equal(VolcanoSizeScale.MinScale, VolcanoSizeScale.ScaleFor(-30), 4);
        }

        [Fact]
        public void TheTopOfTheRangeIsCapped()
        {
            Assert.Equal(VolcanoSizeScale.MaxScale, VolcanoSizeScale.ScaleFor(255), 4);
            Assert.Equal(VolcanoSizeScale.MaxScale, VolcanoSizeScale.ScaleFor(100000), 4);
        }

        [Fact]
        public void RawAndScaleRoundTripInsideTheBand()
        {
            for (int raw = 20; raw <= 200; raw += 5)
            {
                float scale = VolcanoSizeScale.ScaleFor(raw);
                if (scale <= VolcanoSizeScale.MinScale || scale >= VolcanoSizeScale.MaxScale) continue;
                Assert.Equal(raw, VolcanoSizeScale.RawFor(scale));
            }
        }

        [Fact]
        public void GarbageInputDoesNotBecomeNaNMetres()
        {
            // A NaN factor falls back to "do not multiply" (the metres pass through unchanged).
            Assert.Equal(1200f, VolcanoSizeScale.Apply(1200f, float.NaN), 3);
            // NaN metres are returned as NaN (we do not invent a default here).
            Assert.True(float.IsNaN(VolcanoSizeScale.Apply(float.NaN, 2f)));
            Assert.Equal(VolcanoSizeScale.AnchorRaw, VolcanoSizeScale.RawFor(float.NaN));
        }

        [Fact]
        public void TheScaleNeverProducesANonPositiveSize()
        {
            Assert.True(VolcanoSizeScale.Apply(1200f, VolcanoSizeScale.ScaleFor(0)) > 0f);
            Assert.True(VolcanoSizeScale.Apply(50f, VolcanoSizeScale.ScaleFor(1)) > 0f);
        }
    }
}
