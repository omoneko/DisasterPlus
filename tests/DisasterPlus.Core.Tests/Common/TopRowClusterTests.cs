using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// 所有者の指示「WF ボタン ＞ ！ボタン ＞ D＋ボタンの順に左上のところに並ぶように」。
    /// **実機で 2 回外した**ので、両方の外し方をここで固定する。
    /// </summary>
    public class TopRowClusterTests
    {
        private const float Gap = TopRowCluster.DefaultMaxGapPixels;

        /// <summary>実機の並び: WF(150,36) と !(194,44)、そして右上のバニラ UI。</summary>
        private static void Layout(out float[] starts, out float[] ends)
        {
            starts = new[] { 8f, 64f, 150f, 194f, 1500f, 1700f };
            ends = new[] { 40f, 96f, 186f, 238f, 1700f, 1880f };
        }

        [Fact]
        public void TheClusterEndsAfterTheLastModButtonNotAtTheScreenEdge()
        {
            float[] s, e;
            Layout(out s, out e);

            float edge = TopRowCluster.RightEdge(s, e, s.Length, 0f, Gap);

            // ！ボタンの右端。**右上のバニラ UI（1500 以降）は別の一団である** ——
            // ここが 1880 になったのが実機 2 回目の壊れ方（設定ボタンと重なった）。
            Assert.Equal(238f, edge, 2);
        }

        [Fact]
        public void TheOrderOfTheInputDoesNotMatter()
        {
            float[] s, e;
            Layout(out s, out e);
            float forward = TopRowCluster.RightEdge(s, e, s.Length, 0f, Gap);

            var rs = new[] { 1700f, 194f, 8f, 1500f, 150f, 64f };
            var re = new[] { 1880f, 238f, 40f, 1700f, 186f, 96f };
            Assert.Equal(forward, TopRowCluster.RightEdge(rs, re, rs.Length, 0f, Gap), 2);
        }

        [Fact]
        public void AnEmptyTopRowReportsNoCluster()
        {
            // MOD が 1 つも居ない環境。呼び出し側は左端から始めてよい。
            Assert.Equal(0f, TopRowCluster.RightEdge(new float[0], new float[0], 0, 0f, Gap), 2);
            Assert.Equal(0f, TopRowCluster.RightEdge(null, null, 3, 0f, Gap), 2);
        }

        [Fact]
        public void AFarAwayWidgetAloneIsNotACluster()
        {
            // 左には何も無く、右上にだけバニラの UI が居る。左端から始めさせる。
            float edge = TopRowCluster.RightEdge(new[] { 1500f }, new[] { 1880f }, 1, 0f, Gap);
            Assert.Equal(0f, edge, 2);
        }

        [Fact]
        public void ButtonsSeparatedByExactlyTheGapStillJoin()
        {
            // 境界は「以下なら繋がる」。
            float edge = TopRowCluster.RightEdge(new[] { 8f, 40f + Gap },
                                                 new[] { 40f, 40f + Gap + 32f }, 2, 0f, Gap);
            Assert.Equal(40f + Gap + 32f, edge, 2);
        }

        [Fact]
        public void OneGapWiderThanTheLimitEndsTheCluster()
        {
            float edge = TopRowCluster.RightEdge(new[] { 8f, 40f + Gap + 1f },
                                                 new[] { 40f, 40f + Gap + 33f }, 2, 0f, Gap);
            Assert.Equal(40f, edge, 2);
        }

        [Fact]
        public void OverlappingWidgetsDoNotBreakTheWalk()
        {
            // パネルとその中身が両方拾われることがある。飲み込むだけで止まらない。
            float edge = TopRowCluster.RightEdge(new[] { 8f, 12f, 20f },
                                                 new[] { 60f, 30f, 90f }, 3, 0f, Gap);
            Assert.Equal(90f, edge, 2);
        }

        [Fact]
        public void BrokenEntriesAreSkippedInsteadOfStoppingTheSearch()
        {
            float edge = TopRowCluster.RightEdge(
                new[] { 8f, float.NaN, 60f, 100f },
                new[] { 40f, 50f, 40f, 140f },   // 3 件目は end <= start
                4, 0f, Gap);

            Assert.Equal(140f, edge, 2);
        }
    }
}
