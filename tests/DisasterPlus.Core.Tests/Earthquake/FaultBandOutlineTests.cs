using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 地図に描く断層帯の輪郭。**<see cref="FaultBand.Contains"/> と一致すること**が
    /// このファイルの全てである。
    ///
    /// 帯の縁は閉じた式で書けない（<c>Gap</c> は 4 次の区分多項式で単峰ですらない、
    /// 全体レビュー C1 追記）。だから輪郭は述語そのものを二分探索して測る。
    /// ここで別の近似式を持ち込むと、**パネルが「帯の内側」と言っている建物が
    /// 地図では帯の外に描かれる**という食い違いが起きる。
    /// </summary>
    public class FaultBandOutlineTests
    {
        private const float L = 1000f;
        private const float W = 100f;

        // 角度 0 のとき dir = (0, 1)、つまり断層は Z 軸に沿う（FaultBandTests と同じ）。
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: L, width: W);
        }

        private static FaultBandOutline Built()
        {
            var outline = new FaultBandOutline();
            outline.Rebuild(Sample());
            return outline;
        }

        [Fact]
        public void AnUnknownBandIsNotDrawnAtAll()
        {
            var outline = new FaultBandOutline();
            outline.Rebuild(default(FaultBand));

            // 「分からない」を「たぶんこのくらい」で描かない。プレハブ 4 値は
            // DLL に無く、実機でしか読めない（IL 事実文書 §A-0）。
            Assert.False(outline.Known);
            Assert.Equal(0f, outline.AlongExtent, 4);
            Assert.Equal(0f, outline.HalfWidthAt(0), 4);
            Assert.Equal(0f, outline.HalfWidthAt(FaultBandOutline.Segments / 2), 4);
        }

        [Fact]
        public void RebuildingOverAKnownOutlineClearsIt()
        {
            var outline = Built();
            Assert.True(outline.Known);

            outline.Rebuild(default(FaultBand));
            Assert.False(outline.Known);
            Assert.Equal(0f, outline.HalfWidthAt(FaultBandOutline.Segments / 2), 4);
        }

        [Fact]
        public void AlongExtentIsTheCentreLimitPlusTheReachThere()
        {
            var band = Sample();
            var outline = Built();

            // 円盤中心の上限 0.4L に、その位置での到達距離 w(0.4) が足される
            // （設計書 §3.1 の末尾。旧実装は両端をちょうど w ぶん取りこぼしていた）。
            Assert.Equal(FaultBand.MaxOffset * L + band.PatchRadiusAt(FaultBand.MaxOffset),
                         outline.AlongExtent, 3);
        }

        [Fact]
        public void TheOutlineSpansTheWholeExtentSymmetrically()
        {
            var outline = Built();
            Assert.Equal(-outline.AlongExtent, outline.AlongAt(0), 3);
            Assert.Equal(0f, outline.AlongAt(FaultBandOutline.Segments / 2), 3);
            Assert.Equal(outline.AlongExtent, outline.AlongAt(FaultBandOutline.Segments), 3);
        }

        [Fact]
        public void AtTheCentreTheHalfWidthIsTheReachPlusTheMeander()
        {
            var band = Sample();
            var outline = Built();

            // 中央（t = 0）では w = W、蛇行 0.5w を足して 1.5W。
            // FaultBand.HalfWidthAt と一致しなければならない。
            int middle = FaultBandOutline.Segments / 2;
            Assert.Equal(band.HalfWidthAt(0f), outline.HalfWidthAt(middle), 1);
            Assert.Equal(1.5f * W, outline.HalfWidthAt(middle), 1);
        }

        [Fact]
        public void TheBandTapersTowardBothEnds()
        {
            var outline = Built();
            int middle = FaultBandOutline.Segments / 2;

            for (int i = 1; i <= middle; i++)
            {
                Assert.True(outline.HalfWidthAt(i) > outline.HalfWidthAt(i - 1));
                Assert.True(outline.HalfWidthAt(FaultBandOutline.Segments - i)
                            > outline.HalfWidthAt(FaultBandOutline.Segments - i + 1));
            }

            // 端は幅がほとんど無い（円盤 1 個が最後に届く 1 点に近づく）。
            Assert.True(outline.HalfWidthAt(0) < 0.05f * W);
            Assert.True(outline.HalfWidthAt(FaultBandOutline.Segments) < 0.05f * W);
        }

        /// <summary>
        /// **このファイルの中心。** 測った輪郭のすぐ内側は <c>Contains</c> が真、
        /// すぐ外側は偽であること ＝ 描く形と判定する形が一致すること。
        /// </summary>
        [Fact]
        public void TheOutlineAgreesWithContainsOnBothSides()
        {
            var band = Sample();
            var outline = Built();
            const float Eps = 1f;

            for (int i = 0; i <= FaultBandOutline.Segments; i++)
            {
                float u = outline.AlongAt(i);
                float h = outline.HalfWidthAt(i);

                if (h > 2f * Eps)
                {
                    Assert.True(band.Contains(PointAt(band, u, h - Eps)),
                                "inside the drawn edge but Contains said no, sample " + i);
                }
                Assert.False(band.Contains(PointAt(band, u, h + Eps)),
                             "outside the drawn edge but Contains said yes, sample " + i);
            }
        }

        [Fact]
        public void TheOutlineIsSymmetricAboutTheFaultLine()
        {
            var band = Sample();
            var outline = Built();

            // 蛇行 sin(...) は ±0.5w の両側に等しく振れるので、帯は線対称になる。
            for (int i = 0; i <= FaultBandOutline.Segments; i++)
            {
                float u = outline.AlongAt(i);
                float h = outline.HalfWidthAt(i);
                if (h <= 2f) continue;
                Assert.True(band.Contains(PointAt(band, u, -(h - 1f))));
            }
        }

        [Fact]
        public void MatchesOnlyForTheGeometryItWasMeasuredFor()
        {
            var outline = Built();

            // L と W だけの関数なので、この 2 つが同じなら測り直さない
            // （震央の位置と m_angle には依存しない ＝ 局所座標で持っている）。
            Assert.True(outline.Matches(L, W));
            Assert.False(outline.Matches(L, W * 1.1f));
            Assert.False(outline.Matches(L * 1.1f, W));
            Assert.False(new FaultBandOutline().Matches(L, W));
        }

        private static Vec2 PointAt(FaultBand band, float along, float across)
        {
            return band.Centre
                   + band.Direction * along
                   + new Vec2(band.Direction.Z, -band.Direction.X) * across;
        }
    }
}
