using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class LightningBudgetTests
    {
        [Fact]
        public void TheQueueCapacityIsTheNumberTheGameActuallyUses()
        {
            // The ldc.i4.s 20 of §A-3. Raise this on our own and we walk straight into the
            // state where requests over the limit are silently discarded.
            Assert.Equal(20, LightningBudget.QueueCapacity);
            Assert.True(LightningBudget.MinFreeSlots > 0,
                "some slots must always be left for the game itself");
        }

        [Fact]
        public void TheRampRisesAndFallsLikeTheVanillaFormula()
        {
            // c = min(100, (f-act)>>3, (act+dur-f)>>3) then (c*i + 50)/100
            const uint act = 1000u, dur = 8000u;
            int early = LightningBudget.VanillaRampCount(act + 8u, act, dur, 255);
            int mid = LightningBudget.VanillaRampCount(act + dur / 2u, act, dur, 255);
            int late = LightningBudget.VanillaRampCount(act + dur - 8u, act, dur, 255);

            Assert.True(early < mid, "the ramp must rise");
            Assert.True(late < mid, "the ramp must fall");
            Assert.InRange(mid, 0, 255);
        }

        [Fact]
        public void TheRampIsZeroOutsideTheActiveWindow()
        {
            const uint act = 1000u, dur = 8000u;
            Assert.Equal(0, LightningBudget.VanillaRampCount(act - 1u, act, dur, 255));
            Assert.Equal(0, LightningBudget.VanillaRampCount(act + dur + 1u, act, dur, 255));
            // 0 when the duration cannot be read (i.e. vanilla's share cannot be estimated).
            Assert.Equal(0, LightningBudget.VanillaRampCount(act + 100u, act, 0u, 255));
        }

        [Fact]
        public void TheVanillaReserveIsTheUpperBoundOfTheDrawNotTheAverage()
        {
            // Take the **upper bound** of n = r.Int32(max(1, c/20), max(1, 1 + c/10)).
            // Estimate with the mean instead and an unlucky step goes just over 20.
            Assert.Equal(1, LightningBudget.VanillaMaxStrikes(0));
            Assert.Equal(1 + 255 / 10, LightningBudget.VanillaMaxStrikes(255));
            Assert.True(LightningBudget.VanillaMaxStrikes(255)
                        > LightningBudget.VanillaMaxStrikes(50));
        }

        [Fact]
        public void TheAllowanceNeverLetsTheQueueReachCapacity()
        {
            for (int inFlight = 0; inFlight <= 30; inFlight++)
            {
                for (int reserve = 0; reserve <= 30; reserve++)
                {
                    int allowance = LightningBudget.Allowance(inFlight, reserve);
                    Assert.True(allowance >= 0, "the allowance must never be negative");
                    Assert.True(inFlight + reserve + allowance
                                <= LightningBudget.QueueCapacity - LightningBudget.MinFreeSlots
                                   || allowance == 0,
                        "inFlight=" + inFlight + " reserve=" + reserve
                        + " allowance=" + allowance + " overruns the queue");
                }
            }
        }

        [Fact]
        public void AnEmptyQueueAlwaysLeavesRoomForAtLeastOneStrike()
        {
            // If this becomes 0, the queue empties while rain > 0.8 and the game starts
            // creating thunderstorm disasters of its own accord (§A-3).
            Assert.True(LightningBudget.Allowance(0, LightningBudget.VanillaMaxStrikes(0)) >= 1);
        }

        [Fact]
        public void AboveAKnownIntensityTheHostStormTakesTheWholeBudget()
        {
            // Overall review I4. **This threshold lies inside the range of intensities that
            // can be set** (the slider goes 10–255), so a player can silently lose T6's
            // eyewall scatter. To stop the constant drifting away from the formula, both
            // sides are built from the formula and pinned down.
            const uint act = 1000u, dur = 80000u;

            // A frame that lands on the peak of the ramp (far enough in that c caps at 100).
            uint peak = act + dur / 2u;

            int justBelow = LightningBudget.VanillaMaxStrikes(
                LightningBudget.VanillaRampCount(
                    peak, act, dur, (byte)(LightningBudget.IntensityWithNoShareAtPeak - 1)));
            int atThreshold = LightningBudget.VanillaMaxStrikes(
                LightningBudget.VanillaRampCount(
                    peak, act, dur, (byte)LightningBudget.IntensityWithNoShareAtPeak));

            Assert.False(LightningBudget.YieldsCompletely(justBelow));
            Assert.True(LightningBudget.YieldsCompletely(atThreshold));

            // "A share of 0" is a share of 0 —— neither negative nor an error.
            Assert.Equal(0, LightningBudget.Allowance(0, atThreshold));
            Assert.True(LightningBudget.Allowance(0, justBelow) >= 1);
        }

        [Fact]
        public void YieldingCompletelyIgnoresTheModsOwnStock()
        {
            // The property that keeps two states apart: being temporarily at 0 because of
            // our own stock (which recovers on the next tick), and being at 0 purely
            // because of the host's share (which does not recover until the intensity is
            // lowered). The display side rests on this distinction.
            Assert.Equal(0, LightningBudget.Allowance(18, 0));
            Assert.False(LightningBudget.YieldsCompletely(0));
        }

        [Fact]
        public void TheEarliestFrameMatchesTheGamesFloor()
        {
            // §A-3: startFrame = Max(startFrame, currentFrameIndex + 15)
            Assert.Equal(1015u, LightningBudget.EarliestFrame(1000u));
        }

        [Fact]
        public void StrikesExpireExactlyWhenTheGameDropsThem()
        {
            // §A-3: ReleaseInstance happens when sf + 45 < m_currentFrameIndex.
            Assert.False(LightningBudget.HasExpired(1000u, 1045u));
            Assert.True(LightningBudget.HasExpired(1000u, 1046u));
            // A scheduled strike that has not come round yet has not expired.
            Assert.False(LightningBudget.HasExpired(2000u, 1000u));
        }
    }
}
