using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// The owner's instruction: "line them up in the top left in the order WF button >
    /// ! button > D+ button". **We got it wrong twice in-game**, so both failures are
    /// pinned down here.
    /// </summary>
    public class TopRowClusterTests
    {
        private const float Gap = TopRowCluster.DefaultMaxGapPixels;

        /// <summary>The in-game layout: WF(150,36) and !(194,44), plus the vanilla UI in the
        /// top right.</summary>
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

            // The right edge of the ! button. **The vanilla UI in the top right (from 1500
            // on) is a separate cluster** —— this coming out as 1880 was the second in-game
            // failure (it overlapped the settings button).
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
            // An environment with no mods at all. The caller may start from the left edge.
            Assert.Equal(0f, TopRowCluster.RightEdge(new float[0], new float[0], 0, 0f, Gap), 2);
            Assert.Equal(0f, TopRowCluster.RightEdge(null, null, 3, 0f, Gap), 2);
        }

        [Fact]
        public void AFarAwayWidgetAloneIsNotACluster()
        {
            // Nothing on the left, only the vanilla UI in the top right. Start from the left edge.
            float edge = TopRowCluster.RightEdge(new[] { 1500f }, new[] { 1880f }, 1, 0f, Gap);
            Assert.Equal(0f, edge, 2);
        }

        [Fact]
        public void ButtonsSeparatedByExactlyTheGapStillJoin()
        {
            // The boundary is "they join if it is less than or equal".
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
            // A panel and its contents can both be picked up. It simply absorbs them and
            // does not stop.
            float edge = TopRowCluster.RightEdge(new[] { 8f, 12f, 20f },
                                                 new[] { 60f, 30f, 90f }, 3, 0f, Gap);
            Assert.Equal(90f, edge, 2);
        }

        [Fact]
        public void BrokenEntriesAreSkippedInsteadOfStoppingTheSearch()
        {
            float edge = TopRowCluster.RightEdge(
                new[] { 8f, float.NaN, 60f, 100f },
                new[] { 40f, 50f, 40f, 140f },   // the third entry has end <= start
                4, 0f, Gap);

            Assert.Equal(140f, edge, 2);
        }
    }
}
