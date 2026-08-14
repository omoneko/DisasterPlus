using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 断層帯の幾何。**全体レビュー(C1) で 33% 広すぎることが判明して作り直した。**
    ///
    /// 誤りの中身: 到達距離を <c>destructionRadiusMax = 2w</c> としていた。実際の
    /// 呼び出しは <c>preRadius: w</c>（§A-3 の呼び出し一覧）で、
    /// <c>DisasterHelpers.DestroyBuildings</c> は <c>if (dist &gt;= preRadius) continue;</c>
    /// を**種の生成よりランプの計算より前**に置いている（IL_0130 の <c>bge.un</c>）。
    /// 2w が効くのは fD の分子だけで、<c>dist &lt; w</c> では fD &gt; 1 かつ
    /// probability = 1 なので比較は無条件に真 —— つまり 2w はどこにも現れない。
    ///
    /// このファイルのテストは**導出を書く**（生の数値を 1 つ置いて固定しない）。
    /// 前回の版は 200 という数値そのものを固定していたので、誤ったモデルを
    /// 「テストで守られている」状態にしてしまっていた。
    /// </summary>
    public class FaultBandTests
    {
        private const float L = 1000f;
        private const float W = 100f;

        // 角度 0 のとき dir = (-sin 0, cos 0) = (0, 1)、つまり断層は Z 軸に沿う。
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: L, width: W);
        }

        [Fact]
        public void DirectionMatchesTheVanillaFormula()
        {
            var band = Sample();
            Assert.Equal(0f, band.Direction.X, 4);
            Assert.Equal(1f, band.Direction.Z, 4);

            var rotated = new FaultBand(new Vec2(0f, 0f), (float)(Math.PI / 2), L, W);
            Assert.Equal(-1f, rotated.Direction.X, 3);
            Assert.Equal(0f, rotated.Direction.Z, 3);
        }

        [Fact]
        public void PatchRadiusIsTheTaperedWidth()
        {
            var band = Sample();
            // IL_01D6–01EA: w = W * (1 - 4t²)。|t| = 0.5 でちょうど 0。
            Assert.Equal(W, band.PatchRadiusAt(0f), 3);
            Assert.Equal(W * (1f - 4f * 0.4f * 0.4f), band.PatchRadiusAt(0.4f), 3);
            Assert.Equal(0f, band.PatchRadiusAt(0.5f), 3);
        }

        [Fact]
        public void HalfWidthIsTheReachPlusTheMeander()
        {
            var band = Sample();

            // 直交方向の包絡線 = 円盤の到達距離 w（= preRadius）
            //                  + 蛇行が中心をずらせる幅 0.5w（s ∈ [-1,1] に対し s*w*0.5）
            //                  = 1.5w
            foreach (float t in new[] { 0f, 0.2f, 0.4f })
            {
                float w = band.PatchRadiusAt(t);
                Assert.Equal(w + 0.5f * w, band.HalfWidthAt(t), 3);
            }

            Assert.Equal(0f, band.HalfWidthAt(0.5f), 3);
            Assert.True(band.HalfWidthAt(0.4f) < band.HalfWidthAt(0f));
        }

        [Fact]
        public void HalfWidthIsNotTheOldTwoWModel()
        {
            // 回帰テスト。以前は 2w * taper を返しており、震央では 200 だった。
            var band = Sample();
            Assert.Equal(1.5f * W, band.HalfWidthAt(0f), 3);
            Assert.NotEqual(2f * W, band.HalfWidthAt(0f), 3);
        }

        [Fact]
        public void EpicentreIsInside()
        {
            Assert.True(Sample().Contains(new Vec2(0f, 0f)));
        }

        [Fact]
        public void AcrossTheFaultIsBoundedByOnePointFiveW()
        {
            var band = Sample();
            float limit = band.HalfWidthAt(0f);   // = 1.5 * W = 150

            Assert.True(band.Contains(new Vec2(limit - 5f, 0f)));
            Assert.False(band.Contains(new Vec2(limit + 5f, 0f)));

            // 旧モデル（2w = 200）の縁は、今は帯の外である。
            Assert.False(band.Contains(new Vec2(190f, 0f)));
        }

        [Fact]
        public void AlongTheFaultReachesPastTheLastPatchCentreByItsRadius()
        {
            var band = Sample();

            // 円盤中心は t ∈ [-0.4, 0.4]、つまり |z| ≦ 0.4L = 400 までしか置けない。
            // だがその円盤は自分の半径 w(0.4) ぶん先まで壊す。以前の Contains は
            // |t| > 0.4 を無条件に外側としていたので、両端をちょうど w ぶん取りこぼしていた。
            float endW = band.PatchRadiusAt(FaultBand.MaxOffset);   // = 36
            float lastCentre = FaultBand.MaxOffset * L;             // = 400

            Assert.True(band.Contains(new Vec2(0f, lastCentre + endW * 0.5f)));
            Assert.True(band.Contains(new Vec2(0f, -(lastCentre + endW * 0.5f))));
            Assert.False(band.Contains(new Vec2(0f, lastCentre + endW * 2f)));
        }

        [Fact]
        public void AFatterDiscFurtherAlongTheFaultStillCounts()
        {
            // **独立レビューが見つけた欠陥の回帰テスト。**
            // Contains は最初、点にいちばん近い 1 つの t だけで円との交差を見ていた。
            // w は t とともに細るので、沿走方向の残差を最小にする t が到達距離を
            // 最大にする t とは限らない —— もっと中央寄りの太い円盤が届くことがある。
            //
            //   t = 0.30 … w = 64.00、残差 0、直交の余り 99 - 32.00 = 67.00 → 外
            //   t = 0.28 … w = 68.64、残差 20、直交の余り 99 - 34.32 = 64.68 → **内**
            //              20² + 64.68² = 4583 < 68.64² = 4712
            //
            // 誤りの向きが最悪だった: 帯の内側の建物を外側と言い、その建物について
            // 全体円盤の「倒壊しません」を名乗ることになる。
            var band = Sample();
            Assert.True(band.Contains(new Vec2(99f, 300f)));

            // その t = 0.28 の円盤が届くこと自体を、独立に確かめる。
            float w = band.PatchRadiusAt(0.28f);
            double da = 300.0 - 0.28 * L;
            double dacross = 99.0 - 0.5 * w;
            Assert.True(da * da + dacross * dacross <= (double)w * w);
        }

        [Fact]
        public void ContainsAgreesWithABruteForceSweepOverAllPatchPositions()
        {
            // Contains の走査（96 分割 + 24 回の細分）が、素朴な全数探索と一致すること。
            // 真の境界のすぐ近く（±1 m）は判定がぶれうるので、そこは除いて比べる。
            var band = Sample();

            for (float along = -520f; along <= 520f; along += 13f)
            {
                for (float across = 0f; across <= 170f; across += 3f)
                {
                    double best = BruteForceGap(along, across);
                    if (System.Math.Abs(best) < 200.0) continue;   // 境界の近傍は除く

                    Assert.Equal(best <= 0.0, band.Contains(new Vec2(across, along)));
                }
            }
        }

        /// <summary>
        /// 素朴な全数探索。円盤中心の沿走位置を 1/20000 刻みで舐めて、
        /// はみ出しの最小値を返す（0 以下なら届く）。
        /// </summary>
        private static double BruteForceGap(double along, double across)
        {
            const int Steps = 20000;
            double half = FaultBand.MaxOffset * L;
            double best = double.MaxValue;

            for (int i = 0; i <= Steps; i++)
            {
                double u = -half + 2.0 * half * i / Steps;
                double ratio = u / L;
                double w = W * (1.0 - 4.0 * ratio * ratio);
                if (w <= 0.0) continue;

                double da = along - u;
                double dacross = across - 0.5 * w;
                if (dacross < 0.0) dacross = 0.0;

                double gap = da * da + dacross * dacross - w * w;
                if (gap < best) best = gap;
            }

            return best;
        }

        [Fact]
        public void PastTheEndTheCrossSectionShrinks()
        {
            var band = Sample();
            float endW = band.PatchRadiusAt(FaultBand.MaxOffset);
            float lastCentre = FaultBand.MaxOffset * L;

            // 端の円盤の縁ぎりぎりでは、直交方向に使える余裕がほとんど残らない。
            // （円との交差なので、沿走方向に使い切ると直交方向は 0 に近づく）
            Assert.False(band.Contains(
                new Vec2(band.HalfWidthAt(FaultBand.MaxOffset), lastCentre + endW * 0.95f)));
        }

        [Fact]
        public void UnknownGeometryNeverClaimsContainment()
        {
            // プレハブが読めなかったとき（length = width = 0）は、
            // 「内側」とも「外側」とも断定しない。Contains は常に false、Known も false。
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            Assert.False(unknown.Known);
            Assert.False(unknown.Contains(new Vec2(0f, 0f)));
            Assert.Equal(0f, unknown.HalfWidthAt(0f), 4);
            Assert.Equal(0f, unknown.PatchRadiusAt(0f), 4);
        }
    }
}
