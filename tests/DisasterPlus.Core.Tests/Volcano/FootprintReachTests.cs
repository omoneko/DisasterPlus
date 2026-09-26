using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Tests pinning down overall review I4 (a radial trunk road whose midpoint lies outside
    /// the area was not removed, leaving a flat trench inside the finished mountain).
    /// </summary>
    public class FootprintReachTests
    {
        private static readonly Vec2 Centre = new Vec2(0f, 0f);

        [Fact]
        public void AMidpointInsideTheCircleTouches()
        {
            // A shape that already passed back when only the midpoint was tested.
            // Do not lose it to a regression.
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-500f, 0f), new Vec2(50f, 0f), new Vec2(600f, 0f)));
        }

        [Fact]
        public void ARadialRoadWhoseMidpointIsOutsideStillTouches()
        {
            // ★ This is I4 itself. The midpoint and both ends are outside the circle, yet
            //   the road runs straight through it. A midpoint-only test returns false and
            //   that road stays for ever.
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f,
                new Vec2(-800f, 0f), new Vec2(400f, 0f), new Vec2(1600f, 0f)));
        }

        [Fact]
        public void AnEndNodeInsideTheCircleTouchesEvenWhenTheMidpointIsFarAway()
        {
            // The shape of a curved road (where the Bezier midpoint is far from the midpoint
            // of the nodes).
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
            // A road lying exactly on the radius counts as "touching".
            // The scanning side and the counting side use the same predicate, so the
            // direction of the boundary is settled once and for all.
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

            // Even when the midpoint cannot be read, the two ends are still tested.
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f, new Vec2(-500f, 0f), nan, new Vec2(500f, 0f)));

            // If only one point can be read, the test is made on that point.
            Assert.True(FootprintReach.CircleTouchesPolyline(
                Centre, 100f, nan, new Vec2(10f, 10f), nan));

            // If no point at all can be read, false (never demolish on a guess).
            Assert.False(FootprintReach.CircleTouchesPolyline(Centre, 100f, nan, nan, nan));
        }

        [Fact]
        public void TheDistanceToASegmentClampsToTheEndPoints()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(10f, 0f);

            // A point that falls within the segment measures to the foot of the perpendicular.
            Assert.Equal(9f, FootprintReach.DistanceSquaredToSegment(new Vec2(5f, 3f), a, b), 3);
            // A point beyond the segment measures to the end point (not to an infinite line).
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
