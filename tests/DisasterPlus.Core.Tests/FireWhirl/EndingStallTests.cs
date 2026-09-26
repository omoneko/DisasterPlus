using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class EndingStallTests
    {
        /// <summary>65536/1440, measured from vanilla. The game side passes
        /// FeatureHost.FramesPerMinute.</summary>
        private const float Fpm = EndingStall.VanillaFramesPerMinute;

        /// <summary>The in-game minutes a healthy teardown takes, exactly as measured
        /// from the IL (about 8.09).</summary>
        private static float HealthyTeardownMinutes
        {
            get { return EndingStall.TeardownFrames / Fpm; }
        }

        [Fact]
        public void JustEnteredEnding_NotStuck()
        {
            Assert.False(EndingStall.IsStuck(0f, 10, Fpm));
        }

        [Fact]
        public void HealthyTeardownIsAboutEightInGameMinutes()
        {
            // Vehicles only step once every 16 sim frames (VehicleManager.SimulationStepImpl).
            // 23 steps × 16 frames = 368 frames ≒ 8.09 in-game minutes.
            Assert.Equal(368, EndingStall.TeardownFrames);
            Assert.InRange(HealthyTeardownMinutes, 8.0f, 8.2f);
        }

        [Fact]
        public void HealthyTeardownIsNeverReportedStuck_AtAnyAllowedLifetime()
        {
            // This is the motivation for the fix. The slider allows 1 to 60 minutes, so
            // using only the factor of 2 as the threshold makes a healthy teardown come
            // out as STUCK on any setting of 4 minutes or less.
            for (int lifetime = 1; lifetime <= 60; lifetime++)
            {
                Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, lifetime, Fpm));
            }
        }

        [Fact]
        public void ShortLifetimeStillLeavesRoomForATeardown()
        {
            // The old implementation gave a 2 minute threshold at maxLifetime=1. A healthy
            // teardown (about 8.1 minutes) always produced a false report.
            Assert.False(EndingStall.IsStuck(3f, 1, Fpm));
            Assert.True(EndingStall.ThresholdMinutes(1, Fpm) > HealthyTeardownMinutes * 3f);
        }

        [Fact]
        public void FloorAppliesUntilTheLifetimeTermOvertakesIt()
        {
            float floor = EndingStall.MinimumMinutes(Fpm);
            Assert.InRange(floor, 32f, 33f);   // 368 / 45.51 * 4

            // On short settings the floor applies, so the threshold is the same
            // regardless of the configured value.
            Assert.Equal(floor, EndingStall.ThresholdMinutes(1, Fpm));
            Assert.Equal(floor, EndingStall.ThresholdMinutes(16, Fpm));

            // On long settings the doubling term overtakes the floor.
            Assert.Equal(120f, EndingStall.ThresholdMinutes(60, Fpm));
        }

        [Fact]
        public void ExactlyAtThreshold_NotStuck()
        {
            // Exactly at the threshold passes (we err on the side of avoiding
            // false positives).
            float t = EndingStall.ThresholdMinutes(10, Fpm);
            Assert.False(EndingStall.IsStuck(t, 10, Fpm));
        }

        [Fact]
        public void BeyondThreshold_IsStuck()
        {
            // "Never ending" goes on forever, so anything past the threshold is always caught.
            Assert.True(EndingStall.IsStuck(1000f, 10, Fpm));
            Assert.True(EndingStall.IsStuck(1000f, 60, Fpm));
        }

        [Fact]
        public void NonPositiveLifetime_FallsBackToTheMeasuredFloor()
        {
            // Even with a broken setting, the floor alone is enough to judge by.
            // The floor is 4 times a healthy teardown, so this is not a false positive.
            Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, 0, Fpm));
            Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, -5, Fpm));
            Assert.True(EndingStall.IsStuck(9999f, 0, Fpm));
            Assert.True(EndingStall.IsStuck(9999f, -5, Fpm));
        }

        [Fact]
        public void NonPositiveFramesPerMinute_FallsBackToTheVanillaRate()
        {
            // Even if an abnormal value comes from the game side, never divide by zero
            // or end up with an infinite threshold.
            Assert.Equal(EndingStall.MinimumMinutes(Fpm), EndingStall.MinimumMinutes(0f));
            Assert.Equal(EndingStall.MinimumMinutes(Fpm), EndingStall.MinimumMinutes(-1f));
        }

        [Fact]
        public void SlowerGameClockRaisesTheFloor()
        {
            // The floor is derived from a value measured in frames, so if the number of
            // frames per minute changes, the floor converted into minutes follows it.
            Assert.Equal(EndingStall.MinimumMinutes(Fpm) * 2f,
                         EndingStall.MinimumMinutes(Fpm / 2f), 3);
        }

        [Fact]
        public void CalibrationConstantsAreThoseMeasuredFromIl()
        {
            // Pins down the basis of the threshold as numbers. If you touch this,
            // read the IL again.
            Assert.Equal(16, EndingStall.FramesPerVehicleStep);
            Assert.Equal(23, EndingStall.TeardownVehicleSteps);
            Assert.Equal(4f, EndingStall.TeardownSafetyFactor);
            Assert.Equal(2f, EndingStall.LifetimeMultiplier);
        }
    }
}
