using System.Collections.Generic;
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Regression test for GridVote's cell key packing ((long)cx &lt;&lt; 32 ^ (uint)cz).
    ///
    /// CS map coordinates are centred on the origin, so half of them are negative. If
    /// the cell division or the key packing breaks down at negative coordinates, it
    /// breaks in the way that "firestorms only fail to appear in the bottom-left half of
    /// the map", which is hard to notice even in the real game unless it is tested.
    /// </summary>
    public class GridVoteTests
    {
        private static List<int> Near(GridVote g, Vec2 at, float radius)
        {
            var into = new List<int>();
            g.CollectNear(at, radius, into);
            return into;
        }

        [Fact]
        public void CollectNear_FindsPointsAtNegativeCoordinates()
        {
            var g = new GridVote(100f);
            g.Add(0, new Vec2(-1000f, -1000f));
            g.Add(1, new Vec2(-1050f, -980f));

            var found = Near(g, new Vec2(-1000f, -1000f), 100f);
            Assert.Contains(0, found);
            Assert.Contains(1, found);
        }

        [Fact]
        public void CollectNear_DoesNotMixDistantNegativeAndPositiveCells()
        {
            var g = new GridVote(100f);
            g.Add(0, new Vec2(-1000f, -1000f));
            g.Add(1, new Vec2(1000f, 1000f));

            Assert.Equal(new[] { 0 }, Near(g, new Vec2(-1000f, -1000f), 100f));
            Assert.Equal(new[] { 1 }, Near(g, new Vec2(1000f, 1000f), 100f));
        }

        [Fact]
        public void CollectNear_WorksAcrossTheOrigin()
        {
            // Four quadrants straddling the origin. Cell indices use floor, so they
            // scatter across (-1,-1) / (-1,0) / (0,-1) / (0,0).
            var g = new GridVote(100f);
            g.Add(0, new Vec2(-10f, -10f));
            g.Add(1, new Vec2(-10f, 10f));
            g.Add(2, new Vec2(10f, -10f));
            g.Add(3, new Vec2(10f, 10f));

            var found = Near(g, new Vec2(0f, 0f), 100f);
            found.Sort();
            Assert.Equal(new[] { 0, 1, 2, 3 }, found);
        }

        [Fact]
        public void KeyPacking_DoesNotCollideBetweenMirroredCells()
        {
            // (cx, cz) must not be folded onto the same key as (cz, cx) or as a
            // sign-flipped variant. If they were folded, buildings at unrelated
            // locations would land in the same cell, giving false detections.
            var g = new GridVote(100f);
            g.Add(0, new Vec2(-250f, 350f));   // cell (-3,  3)
            g.Add(1, new Vec2(350f, -250f));   // cell ( 3, -3)
            g.Add(2, new Vec2(-250f, -250f));  // cell (-3, -3)
            g.Add(3, new Vec2(350f, 350f));    // cell ( 3,  3)

            // With radius 0, span = 0 and only the cell itself is looked up.
            Assert.Equal(new[] { 0 }, Near(g, new Vec2(-250f, 350f), 0f));
            Assert.Equal(new[] { 1 }, Near(g, new Vec2(350f, -250f), 0f));
            Assert.Equal(new[] { 2 }, Near(g, new Vec2(-250f, -250f), 0f));
            Assert.Equal(new[] { 3 }, Near(g, new Vec2(350f, 350f), 0f));
        }
    }
}
