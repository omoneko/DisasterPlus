using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The owner's request (2026-08-22): "I would like the lava flow to be a bit wider
    /// (scaled to the size of the eruption)".
    ///
    /// ★★ The most important thing is <b>that the ignition scan never exceeds the limit it
    ///    can count through</b>. Beyond that it is silently cut short by the per-step cell
    ///    cap and **buildings under the glowing lava do not catch fire**
    ///    (<c>LavaPath.SpreadHardMaxMetres</c>).
    /// </summary>
    public class LavaWidthTests
    {
        /// <summary>The implementation limits of the ignition scan (kept in step with the
        /// constants in <c>VolcanoLava.Ignite</c>).</summary>
        private const int MaxBuildingCellsPerStep = 25;
        private const int MaxTreeCellsPerStep = 64;

        [Fact]
        public void TheFlowIsWiderThanItUsedToBe()
        {
            // It used to be base 20 m / max 60 m. The request was **a bit wider**.
            Assert.True(LavaPath.SpreadBaseMetres > 20f);
            Assert.True(LavaPath.SpreadMaxMetres > 60f);
        }

        [Fact]
        public void TheReferenceVolcanoIsUnscaled()
        {
            // At the default settings and the default slider position the factor is 1
            // (the base width comes out unchanged).
            Assert.Equal(1f, LavaVolume.WidthFactor(LavaVolume.ReferenceRadiusMetres), 3);
            Assert.Equal(LavaPath.SpreadBaseMetres, LavaPath.SpreadRadiusFor(0f, 1f), 3);
        }

        [Fact]
        public void ABiggerEruptionPoursAWiderFlow()
        {
            float small = LavaPath.SpreadRadiusFor(500f, LavaVolume.WidthFactor(400f));
            float reference = LavaPath.SpreadRadiusFor(500f, LavaVolume.WidthFactor(1200f));
            float large = LavaPath.SpreadRadiusFor(500f, LavaVolume.WidthFactor(3000f));

            Assert.True(small < reference, small + " < " + reference);
            Assert.True(reference < large, reference + " < " + large);
        }

        [Fact]
        public void WidthGrowsMoreGentlyThanLength()
        {
            // Width registers as area to the eye, so scaling it at the same rate as the
            // length makes the flow wider than the mountain.
            float w = LavaVolume.WidthFactor(3000f);
            float l = LavaVolume.LengthFactor(3000f);
            Assert.True(w < l, "width " + w + " should grow more slowly than length " + l);
        }

        [Fact]
        public void TheIgniteScanCanAlwaysCoverTheBand()
        {
            // ★★ This is the structural guarantee. At any size and any distance, the
            //    rectangle of one step must fit within the scan's cell limit.
            foreach (float radius in new[] { 250f, 1200f, 3000f, 100000f })
            {
                float factor = LavaVolume.WidthFactor(radius);

                foreach (float travelled in new[] { 0f, 500f, 2000f, 10000f, 100000f })
                {
                    float r = LavaPath.SpreadRadiusFor(travelled, factor);

                    Assert.True(r <= LavaPath.SpreadHardMaxMetres,
                                "spread " + r + " exceeded the hard cap");

                    // The building grid is 64 m square, the tree grid 32 m square.
                    Assert.True(CellsPerAxis(r, 64f) * CellsPerAxis(r, 64f)
                                <= MaxBuildingCellsPerStep,
                                "building cells overflowed at r=" + r);
                    Assert.True(CellsPerAxis(r, 32f) * CellsPerAxis(r, 32f)
                                <= MaxTreeCellsPerStep,
                                "tree cells overflowed at r=" + r);
                }
            }
        }

        [Fact]
        public void TheOneArgumentFormStillMeansUnscaled()
        {
            // Do not change the meaning for existing callers (the preview tools and so on).
            foreach (float travelled in new[] { 0f, 500f, 5000f })
            {
                Assert.Equal(LavaPath.SpreadRadiusFor(travelled, 1f),
                             LavaPath.SpreadRadiusFor(travelled), 4);
            }
        }

        [Fact]
        public void BrokenInputNeverProducesAZeroWidthOrANaNBand()
        {
            foreach (float f in new[] { float.NaN, float.PositiveInfinity, 0f, -3f })
            {
                float r = LavaPath.SpreadRadiusFor(100f, f);
                Assert.False(float.IsNaN(r));
                Assert.True(r > 0f, "width collapsed to " + r);
                Assert.True(r <= LavaPath.SpreadHardMaxMetres);
            }

            Assert.Equal(1f, LavaVolume.WidthFactor(float.NaN), 3);
        }

        /// <summary>
        /// The number of cells a band of radius <paramref name="r"/> can span (on one axis).
        /// The scan runs <c>CellOf(p - r)</c> to <c>CellOf(p + r)</c>, so we count a number
        /// that **suffices wherever within a cell the centre happens to lie**.
        /// </summary>
        private static int CellsPerAxis(float r, float cellSize)
        {
            return (int)System.Math.Floor(2f * r / cellSize) + 1;
        }
    }
}
