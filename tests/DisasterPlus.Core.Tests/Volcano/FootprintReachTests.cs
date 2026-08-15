using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 全体レビュー I4（中点が範囲の外にある放射状の幹線道路が取り除かれず、
    /// 完成した山の中に平らな溝が残る）を固定するテスト。
    /// </summary>
    public class FootprintReachTests
    {
        private static readonly Vec2 Centre = new Vec2(0f, 0f);

        [Fact]
        public void AMidpointInsideTheCircleTouches()
        {
            // 中点だけで判定していた頃も通っていた形。回帰で落とさないこと。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-500f, 0f), new Vec2(50f, 0f), new Vec2(600f, 0f)));
        }

        [Fact]
        public void ARadialRoadWhoseMidpointIsOutsideStillTouches()
        {
            // ★ これが I4 そのもの。中点も両端も円の外だが、道路は円を貫いている。
            //   中点 1 点の判定では false になり、その道路は永久に残る。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-800f, 0f), new Vec2(400f, 0f), new Vec2(1600f, 0f)));
        }

        [Fact]
        public void AnEndNodeInsideTheCircleTouchesEvenWhenTheMidpointIsFarAway()
        {
            // 曲がった道路（ベジェの中点がノードの中点から離れる場合）の形。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(20f, 20f), new Vec2(900f, 900f), new Vec2(1800f, 1800f)));
        }

        [Fact]
        public void ARoadThatNeverComesNearIsNotTouched()
        {
            Assert.False(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(500f, 500f), new Vec2(600f, 500f), new Vec2(700f, 500f)));
        }

        [Fact]
        public void TheBoundaryIsInclusive()
        {
            // ちょうど半径の上に乗っている道路は「掛かっている」。
            // 走査側と数える側で同じ述語を使うので、境界の向きも 1 つに決めておく。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-500f, 100f), new Vec2(0f, 100f), new Vec2(500f, 100f)));
            Assert.False(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-500f, 100.5f), new Vec2(0f, 100.5f), new Vec2(500f, 100.5f)));
        }

        [Fact]
        public void ANonPositiveOrNaNRadiusNeverTouches()
        {
            Assert.False(FootprintReach.CircleTouchesPolyline(
                Centre, 0f, Centre, Centre, Centre));
            Assert.False(FootprintReach.CircleTouchesPolyline(
                Centre, -1f, Centre, Centre, Centre));
            Assert.False(FootprintReach.CircleTouchesPolyline(
                Centre, float.NaN, Centre, Centre, Centre));
        }

        [Fact]
        public void AnUnreadablePointDoesNotThrowAwayTheWholeRoad()
        {
            var nan = new Vec2(float.NaN, float.NaN);

            // 中点が読めなくても両端で判定する。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f, new Vec2(-500f, 0f), nan, new Vec2(500f, 0f)));

            // 1 点しか読めなければその点で判定する。
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f, nan, new Vec2(10f, 10f), nan));

            // 1 点も読めなければ false（推測で壊さない）。
            Assert.False(FootprintReach.CircleTouchesPolyline(Centre, 100f, nan, nan, nan));
        }

        [Fact]
        public void TheDistanceToASegmentClampsToTheEndPoints()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(10f, 0f);

            // 線分の内側に落ちる点は垂線の足まで。
            Assert.Equal(9f, FootprintReach.DistanceSquaredToSegment(new Vec2(5f, 3f), a, b), 3);
            // 線分の外側の点は端点まで（無限直線ではない）。
            Assert.Equal(25f, FootprintReach.DistanceSquaredToSegment(new Vec2(-5f, 0f), a, b), 3);
            Assert.Equal(25f, FootprintReach.DistanceSquaredToSegment(new Vec2(15f, 0f), a, b), 3);
        }

        [Fact]
        public void ADegenerateSegmentIsJustTheDistanceToThePoint()
        {
            var a = new Vec2(3f, 4f);
            Assert.Equal(25f, FootprintReach.DistanceSquaredToSegment(new Vec2(0f, 0f), a, a), 3);
        }
    }
}
