using System.Collections.Generic;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 走査の順序（第 2 層レビュー I1）。
    ///
    /// 直す前の実装は矩形を行優先で辿っていたので、**最初に見るセルが震央から
    /// いちばん遠い角**になり、1 回ぶんの上限が「いちばん壊れやすい建物」を
    /// 切り捨てていた。ここで固定するのは 3 つだけである:
    ///
    ///   1. 序数 0 は震央そのもの（<see cref="StartsAtTheCentre"/>）
    ///   2. リング（チェビシェフ距離）は序数について**非減少**
    ///      （<see cref="RingsNeverGoBackInwards"/>）—— これが「近い方を先に見る」の中身
    ///   3. <see cref="OutwardCellOrder.OrdinalCount"/> ぶん回せば、その半径の
    ///      正方形の全セルを**ちょうど 1 回ずつ**通る（重複も取りこぼしも無い）
    /// </summary>
    public class OutwardCellOrderTests
    {
        [Fact]
        public void StartsAtTheCentre()
        {
            int dx, dz;
            Assert.True(OutwardCellOrder.Offset(0, out dx, out dz));
            Assert.Equal(0, dx);
            Assert.Equal(0, dz);
        }

        [Fact]
        public void NegativeOrdinalIsRefusedAndYieldsTheCentre()
        {
            int dx = 7, dz = 7;
            Assert.False(OutwardCellOrder.Offset(-1, out dx, out dz));
            Assert.Equal(0, dx);
            Assert.Equal(0, dz);
        }

        /// <summary>
        /// **この機能の中身。** 序数が進むにつれてリングが内側へ戻らないこと。
        /// 行優先の実装はここで落ちる（最初の序数が最大リングになる）。
        /// </summary>
        [Fact]
        public void RingsNeverGoBackInwards()
        {
            int count = OutwardCellOrder.OrdinalCount(12);
            int previous = -1;

            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));

                int ring = OutwardCellOrder.RingOf(dx, dz);
                Assert.True(ring >= previous, "ring went back inwards at ordinal " + n);
                previous = ring;
            }

            // 最後の序数は必ず最外リングに居る。
            Assert.Equal(12, previous);
        }

        [Fact]
        public void CoversEverySquareCellExactlyOnce()
        {
            const int radius = 9;
            int count = OutwardCellOrder.OrdinalCount(radius);
            var seen = new HashSet<int>();

            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));
                Assert.True(dx >= -radius && dx <= radius);
                Assert.True(dz >= -radius && dz <= radius);

                // (dx, dz) を 1 つの整数へ畳む。半径は 9 なので衝突しない。
                Assert.True(seen.Add((dx + radius) * 1000 + (dz + radius)),
                            "ordinal " + n + " repeated a cell");
            }

            Assert.Equal(count, seen.Count);
            Assert.Equal((2 * radius + 1) * (2 * radius + 1), count);
        }

        /// <summary>
        /// リングの決定は整数平方根を使う。<c>(2r+1)²</c> の**直前と直後**が
        /// 境界なので、そこだけを名指しで固定する（double の Sqrt では 1 ずれうる）。
        /// </summary>
        [Fact]
        public void RingBoundariesAreExact()
        {
            for (int r = 1; r <= 60; r++)
            {
                int firstOfRing = (2 * r - 1) * (2 * r - 1);
                int lastOfRing = (2 * r + 1) * (2 * r + 1) - 1;

                int dx, dz;
                OutwardCellOrder.Offset(firstOfRing, out dx, out dz);
                Assert.Equal(r, OutwardCellOrder.RingOf(dx, dz));

                OutwardCellOrder.Offset(lastOfRing, out dx, out dz);
                Assert.Equal(r, OutwardCellOrder.RingOf(dx, dz));

                OutwardCellOrder.Offset(firstOfRing - 1, out dx, out dz);
                Assert.Equal(r - 1, OutwardCellOrder.RingOf(dx, dz));
            }
        }

        [Fact]
        public void OrdinalCountIsTheSquareOfTheOddSide()
        {
            Assert.Equal(1, OutwardCellOrder.OrdinalCount(0));
            Assert.Equal(1, OutwardCellOrder.OrdinalCount(-5));
            Assert.Equal(9, OutwardCellOrder.OrdinalCount(1));
            Assert.Equal(25, OutwardCellOrder.OrdinalCount(2));

            // 実際に使う最大（建物グリッド 270 辺）でも int に収まる。
            Assert.Equal(541 * 541, OutwardCellOrder.OrdinalCount(270));
        }

        /// <summary>
        /// 桁あふれを 0 や負にしない。あふれた値で <c>while (n &lt; count)</c> を
        /// 回すと走査が 1 セルも走らないという、いちばん見えにくい壊れ方をする。
        /// </summary>
        [Fact]
        public void OrdinalCountSaturatesInsteadOfOverflowing()
        {
            Assert.Equal(int.MaxValue, OutwardCellOrder.OrdinalCount(int.MaxValue));
            Assert.True(OutwardCellOrder.OrdinalCount(100000) > 0);
        }

        /// <summary>
        /// リングの半径は矩形の全セルを覆う。中心が矩形の**外**にあっても覆う
        /// （震央がマップ外でクランプされた場合）。
        /// </summary>
        [Fact]
        public void RingRadiusCoversTheWholeBox()
        {
            Assert.Equal(0, OutwardCellOrder.RingRadiusFor(5, 5, 5, 5, 5, 5));
            Assert.Equal(3, OutwardCellOrder.RingRadiusFor(5, 5, 2, 8, 4, 6));

            // 中心が矩形の外（左下の外）。いちばん遠い角までの距離になる。
            Assert.Equal(10, OutwardCellOrder.RingRadiusFor(0, 0, 3, 10, 1, 4));
        }

        /// <summary>
        /// 矩形とその中心から作った順序が、矩形の全セルを 1 回ずつ通ること。
        /// <c>LongPeriodDamage.Sweep</c> が実際に行っている「はみ出しは飛ばす」を
        /// そのまま写した検証である。
        /// </summary>
        [Fact]
        public void SweepingAClippedBoxVisitsEveryCellOnce()
        {
            const int minX = 3, maxX = 11, minZ = 0, maxZ = 5;
            const int centreX = 4, centreZ = 1;

            int count = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var seen = new HashSet<int>();
            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                Assert.True(seen.Add(x * 1000 + z), "cell visited twice at ordinal " + n);
            }

            Assert.Equal((maxX - minX + 1) * (maxZ - minZ + 1), seen.Count);
        }

        /// <summary>
        /// 打ち切りは**外側から**落ちる。1 回ぶんの上限で切ったとき、
        /// 見終わったセルはどれも、まだ見ていないセルより震央に近いか同じリングに居る。
        /// </summary>
        [Fact]
        public void TruncatingDropsTheOutermostCellsFirst()
        {
            const int budget = 30;
            int count = OutwardCellOrder.OrdinalCount(6);

            int lastVisitedRing = 0;
            for (int n = 0; n < budget; n++)
            {
                int dx, dz;
                OutwardCellOrder.Offset(n, out dx, out dz);
                lastVisitedRing = OutwardCellOrder.RingOf(dx, dz);
            }

            for (int n = budget; n < count; n++)
            {
                int dx, dz;
                OutwardCellOrder.Offset(n, out dx, out dz);
                Assert.True(OutwardCellOrder.RingOf(dx, dz) >= lastVisitedRing);
            }
        }
    }
}
