using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class BarrenSpreadTrackerTests
    {
        [Fact]
        public void FreshTracker_IsNotTripped()
        {
            var t = new BarrenSpreadTracker(3);
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void TripsExactlyOnceAtThreshold()
        {
            var t = new BarrenSpreadTracker(3);
            Assert.False(t.Record(5, 0));
            Assert.False(t.Record(5, 0));
            Assert.True(t.Record(5, 0));    // ここで初めて閾値
            Assert.False(t.Record(5, 0));   // 以後はもう報告しない（ログを 1 回に留める）
            Assert.True(t.Tripped);
        }

        [Fact]
        public void AnyIgnitionResetsEverything()
        {
            var t = new BarrenSpreadTracker(3);
            t.Record(5, 0);
            t.Record(5, 0);
            Assert.Equal(2, t.Streak);

            Assert.False(t.Record(5, 1));
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void IgnitionAfterTrippingClearsTheBadge()
        {
            var t = new BarrenSpreadTracker(2);
            t.Record(1, 0);
            Assert.True(t.Record(1, 0));
            Assert.True(t.Tripped);

            t.Record(1, 3);
            Assert.False(t.Tripped);
            Assert.Equal(0, t.Streak);

            // 一度回復したら、次の閾値到達はまた報告される。
            t.Record(1, 0);
            Assert.True(t.Record(1, 0));
        }

        [Fact]
        public void PassesWithNoAttemptAreNotEvidence()
        {
            // 「燃やす対象がそもそも無かった」回は証拠にならない。
            // 積み増しもしないが、それまでの証拠を消しもしない。
            var t = new BarrenSpreadTracker(3);
            t.Record(2, 0);
            Assert.False(t.Record(0, 0));
            Assert.Equal(1, t.Streak);
            Assert.False(t.Record(0, 0));
            Assert.Equal(1, t.Streak);

            t.Record(2, 0);
            Assert.True(t.Record(2, 0));
        }

        [Fact]
        public void ResetClearsState()
        {
            var t = new BarrenSpreadTracker(2);
            t.Record(1, 0);
            t.Record(1, 0);
            Assert.True(t.Tripped);

            t.Reset();
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void ThresholdIsClampedToAtLeastOne()
        {
            var t = new BarrenSpreadTracker(0);
            Assert.Equal(1, t.Threshold);
            Assert.True(t.Record(1, 0));
        }

        [Fact]
        public void DefaultThresholdIsEight()
        {
            // 既定値の根拠（旋風 1 基の寿命およそ 28 パスの約 1/4）を固定する。
            Assert.Equal(8, BarrenSpreadTracker.DefaultThreshold);
            Assert.Equal(8, new BarrenSpreadTracker().Threshold);
        }

        [Fact]
        public void DefaultTracker_TripsOnlyAfterEightBarrenPasses()
        {
            var t = new BarrenSpreadTracker();
            for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold - 1; i++)
            {
                Assert.False(t.Record(3, 0));
            }
            Assert.True(t.Record(3, 0));
        }
    }
}
