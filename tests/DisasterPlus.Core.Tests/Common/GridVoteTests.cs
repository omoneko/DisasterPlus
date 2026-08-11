using System.Collections.Generic;
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// GridVote のセルキー詰め（(long)cx &lt;&lt; 32 ^ (uint)cz）の回帰テスト。
    ///
    /// CS のマップ座標は原点が中心なので、半分は負である。負の座標でセル分割や
    /// キー詰めが破綻すると「マップの左下半分だけ火災旋風が出ない」という、
    /// テストしていなければ実機でも気づきにくい壊れ方をする。
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
            // 原点をまたぐ 4 象限。セル番号は floor なので (-1,-1) / (-1,0) / (0,-1) / (0,0) に散る。
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
            // (cx, cz) と (cz, cx) や符号違いが同じキーに畳まれないこと。
            // 畳まれると無関係な地点の建物が同じセルに入り、誤検出になる。
            var g = new GridVote(100f);
            g.Add(0, new Vec2(-250f, 350f));   // cell (-3,  3)
            g.Add(1, new Vec2(350f, -250f));   // cell ( 3, -3)
            g.Add(2, new Vec2(-250f, -250f));  // cell (-3, -3)
            g.Add(3, new Vec2(350f, 350f));    // cell ( 3,  3)

            // 半径 0 なら span = 0 で自分のセルだけを引く。
            Assert.Equal(new[] { 0 }, Near(g, new Vec2(-250f, 350f), 0f));
            Assert.Equal(new[] { 1 }, Near(g, new Vec2(350f, -250f), 0f));
            Assert.Equal(new[] { 2 }, Near(g, new Vec2(-250f, -250f), 0f));
            Assert.Equal(new[] { 3 }, Near(g, new Vec2(350f, 350f), 0f));
        }
    }
}
