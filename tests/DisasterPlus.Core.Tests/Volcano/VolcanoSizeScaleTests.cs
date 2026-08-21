using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class VolcanoSizeScaleTests
    {
        [Fact]
        public void TheVanillaDefaultMeansExactlyTheConfiguredSize()
        {
            // 55（ゲーム自身の災害の既定強度）で倍率 1.0。ここがずれると、
            // スライダーに触っていないプレイヤーが設定と違う山を建てることになる。
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
            // 0 倍は「小さい山」ではなく「押しても何も起きない」である。
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
            // NaN の倍率は「掛けない」に倒す（メートルはそのまま）。
            Assert.Equal(1200f, VolcanoSizeScale.Apply(1200f, float.NaN), 3);
            // NaN のメートルは NaN のまま返す（ここで既定値を発明しない）。
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
