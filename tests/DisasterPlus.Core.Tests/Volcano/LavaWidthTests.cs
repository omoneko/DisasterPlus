using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 所有者の依頼（2026-08-22）「溶岩流の太さを、もう少し太くしてほしいです
    /// （噴火規模に合わせて）」。
    ///
    /// ★★ いちばん大事なのは<b>着火の走査が数え切れる上限を超えないこと</b>である。
    ///    超えると 1 歩あたりのセル上限で黙って打ち切られ、
    ///    **光っている溶岩の下の建物が燃えない**（<c>LavaPath.SpreadHardMaxMetres</c>）。
    /// </summary>
    public class LavaWidthTests
    {
        /// <summary>着火の走査の実装上限（<c>VolcanoLava.Ignite</c> の定数と揃えてある）。</summary>
        private const int MaxBuildingCellsPerStep = 25;
        private const int MaxTreeCellsPerStep = 64;

        [Fact]
        public void TheFlowIsWiderThanItUsedToBe()
        {
            // 以前は base 20 m / max 60 m だった。**もう少し太く**が依頼である。
            Assert.True(LavaPath.SpreadBaseMetres > 20f);
            Assert.True(LavaPath.SpreadMaxMetres > 60f);
        }

        [Fact]
        public void TheReferenceVolcanoIsUnscaled()
        {
            // 既定の設定・既定のスライダー位置では倍率 1（太さの基準値がそのまま出る）。
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
            // 太さは面積として目に入るので、長さと同じ比で振ると山より太くなる。
            float w = LavaVolume.WidthFactor(3000f);
            float l = LavaVolume.LengthFactor(3000f);
            Assert.True(w < l, "width " + w + " should grow more slowly than length " + l);
        }

        [Fact]
        public void TheIgniteScanCanAlwaysCoverTheBand()
        {
            // ★★ ここが構造の担保である。どんな規模・どんな距離でも、
            //    1 歩の矩形が走査の上限セル数に収まっていること。
            foreach (float radius in new[] { 250f, 1200f, 3000f, 100000f })
            {
                float factor = LavaVolume.WidthFactor(radius);

                foreach (float travelled in new[] { 0f, 500f, 2000f, 10000f, 100000f })
                {
                    float r = LavaPath.SpreadRadiusFor(travelled, factor);

                    Assert.True(r <= LavaPath.SpreadHardMaxMetres,
                                "spread " + r + " exceeded the hard cap");

                    // 建物グリッドは 64 m 角、樹木グリッドは 32 m 角。
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
            // 既存の呼び出し元（プレビュー道具など）の意味を変えない。
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
        /// 半径 <paramref name="r"/> の帯が跨ぎうるセル数（1 軸）。
        /// 走査は <c>CellOf(p - r)</c> 〜 <c>CellOf(p + r)</c> なので、
        /// **中心がセルのどこにあっても足りる**数を数える。
        /// </summary>
        private static int CellsPerAxis(float r, float cellSize)
        {
            return (int)System.Math.Floor(2f * r / cellSize) + 1;
        }
    }
}
