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
            Assert.True(t.Record(5, 0));    // the threshold is reached here for the first time
            Assert.False(t.Record(5, 0));   // it does not report again (keeping the log to one entry)
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

            // Once it has recovered, the next time the threshold is reached it reports again.
            t.Record(1, 0);
            Assert.True(t.Record(1, 0));
        }

        [Fact]
        public void PassesWithNoAttemptAreNotEvidence()
        {
            // A pass where "there was nothing to burn in the first place" is not evidence.
            // It neither adds to the count nor erases the evidence gathered so far.
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
        public void DefaultThresholdIsAWholeWhirlLifetime()
        {
            // Pins down the grounds for the default (1 pass ≈ 0.35 in-game minutes × 24
            // ≈ 8.4 minutes ≈ the lifetime of one whirl). Back when it was 8, that was
            // 2.8 in-game minutes = under 3 seconds of real time, and since the evidence
            // also included "buildings vanilla refuses by design", a healthy city always
            // tripped it.
            Assert.Equal(24, BarrenSpreadTracker.DefaultThreshold);
            Assert.Equal(24, new BarrenSpreadTracker().Threshold);
        }

        [Fact]
        public void DefaultTracker_TripsOnlyAtTheDefaultThreshold()
        {
            var t = new BarrenSpreadTracker();
            for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold - 1; i++)
            {
                Assert.False(t.Record(3, 0));
            }
            Assert.True(t.Record(3, 0));
        }

        [Fact]
        public void RefusedByDesignCandidatesAreNotEvidence()
        {
            // The motivation for this fix. The steady state on the site of a successful fire
            // whirl is "buildings still burning (excluded by Select) plus burnt-out rubble
            // (always refused)", and back when the rubble was counted as attempted, the
            // evidence piled up without end in a healthy city.
            // FireWhirlDamage.CanBurn now keeps those out of attempted, so from the caller's
            // point of view all that happens is an endless run of "passes with 0 attempts".
            var t = new BarrenSpreadTracker();
            for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold * 4; i++)
            {
                Assert.False(t.Record(0, 0));
            }
            Assert.Equal(0, t.Streak);
            Assert.False(t.Tripped);
        }

        [Fact]
        public void OccasionalIgnitionKeepsTheDetectorQuietForever()
        {
            // The healthy case. If even one building ignites before the threshold is reached,
            // the evidence is discarded every time.
            var t = new BarrenSpreadTracker();
            for (int round = 0; round < 10; round++)
            {
                for (int i = 0; i < BarrenSpreadTracker.DefaultThreshold - 1; i++)
                {
                    Assert.False(t.Record(3, 0));
                }
                Assert.False(t.Record(3, 1));
                Assert.Equal(0, t.Streak);
            }
            Assert.False(t.Tripped);
        }
    }
}
