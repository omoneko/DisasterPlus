using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlLifecycleTests
    {
        private static FireWhirlConfig Config(float maxLife = 10f, float grace = 1f)
        {
            var c = FireWhirlConfig.Defaults();
            c.MaxLifetimeMinutes = maxLife;
            c.ConditionGraceMinutes = grace;
            return c;
        }

        /// <summary>
        /// The default grace period must be comfortably longer than one full sweep of the
        /// burning-building scan.
        ///
        /// BurningBuildingScanner sweeps all 49152 slots in 8 ticks. At game speed 3 one
        /// tick = 9 sim frames, so in the worst case 72 frames' worth of results are "stale".
        /// One in-game minute = SimulationManager.DAYTIME_FRAMES(65536) / 1440 ≈ 45.51 frames.
        /// If the grace drops below that, a whirl dies on nothing but the stale results of a
        /// single sweep. (The old default of 1 minute looked effectively four times longer
        /// because it rested on the mistaken frame conversion of 262144.)
        /// </summary>
        [Fact]
        public void Defaults_GraceOutlastsOneFullScanSweep()
        {
            const float framesPerMinute = 65536f / 1440f;
            const int scanTicksPerSweep = 8;
            const int framesPerTickAtMaxSpeed = 9;

            float sweepMinutes = scanTicksPerSweep * framesPerTickAtMaxSpeed / framesPerMinute;
            float grace = FireWhirlConfig.Defaults().ConditionGraceMinutes;

            Assert.True(grace >= sweepMinutes * 1.5f,
                "default ConditionGraceMinutes (" + grace + ") must comfortably outlast one scan sweep ("
                + sweepMinutes + " in-game minutes at simulation speed 3)");
        }

        [Fact]
        public void FreshWhirl_Continues()
        {
            Assert.Equal(FireWhirlVerdict.Continue, FireWhirlLifecycle.Start().Evaluate(Config()));
        }

        [Fact]
        public void ConditionMet_ContinuesUpToMaxLifetime()
        {
            var life = FireWhirlLifecycle.Start();
            for (int i = 0; i < 9; i++) life = life.Advance(1f, true);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void MaxLifetime_AlwaysDissipates_EvenWhileFireRages()
        {
            // Regression test: even while the conditions keep being met, the absolute cap
            // must always cut it off. Without this the self-reinforcing loop of spreading
            // fire lets a whirl sit there for ever.
            var life = FireWhirlLifecycle.Start();
            for (int i = 0; i < 100; i++) life = life.Advance(1f, true);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void MaxLifetime_BoundaryIsInclusive()
        {
            var life = FireWhirlLifecycle.Start().Advance(10f, true);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(maxLife: 10f)));
        }

        [Fact]
        public void ConditionBroken_ShorterThanGrace_Continues()
        {
            var life = FireWhirlLifecycle.Start().Advance(0.5f, false);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void ConditionBroken_BeyondGrace_Dissipates()
        {
            var life = FireWhirlLifecycle.Start().Advance(1.5f, false);
            Assert.Equal(FireWhirlVerdict.Dissipate, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void ConditionRecovered_ResetsGraceCounter()
        {
            // If the fire weakens for a moment but comes straight back, the whirl survives.
            // It must not die on a flicker.
            var life = FireWhirlLifecycle.Start()
                .Advance(0.9f, false)
                .Advance(0.1f, true)
                .Advance(0.9f, false);
            Assert.Equal(FireWhirlVerdict.Continue, life.Evaluate(Config(grace: 1f)));
        }

        [Fact]
        public void Advance_AccumulatesElapsedRegardlessOfCondition()
        {
            var life = FireWhirlLifecycle.Start().Advance(2f, true).Advance(3f, false);
            Assert.Equal(5f, life.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_NegativeDelta_IsIgnored()
        {
            // Time can run backwards across a pause or a save/load. A negative value must
            // never extend the lifetime.
            var life = FireWhirlLifecycle.Start().Advance(5f, true).Advance(-3f, true);
            Assert.Equal(5f, life.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ZeroDelta_ChangesNothing()
        {
            // While paused the elapsed time is zero. Being called must not move the state.
            var life = FireWhirlLifecycle.Start().Advance(4f, false).Advance(0f, false);
            Assert.Equal(4f, life.ElapsedMinutes, 4);
            Assert.Equal(4f, life.ConditionBrokenMinutes, 4);
        }
    }
}
