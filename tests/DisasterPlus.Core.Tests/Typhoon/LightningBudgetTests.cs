using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class LightningBudgetTests
    {
        [Fact]
        public void TheQueueCapacityIsTheNumberTheGameActuallyUses()
        {
            // §A-3 の ldc.i4.s 20。ここを勝手に増やすと、超過した要求が
            // 黙って捨てられる状態に自分から突っ込むことになる。
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
            // 持続時間が読めていなければ 0（＝バニラのぶんを見積もれない）。
            Assert.Equal(0, LightningBudget.VanillaRampCount(act + 100u, act, 0u, 255));
        }

        [Fact]
        public void TheVanillaReserveIsTheUpperBoundOfTheDrawNotTheAverage()
        {
            // n = r.Int32(max(1, c/20), max(1, 1 + c/10)) の**上限**を取る。
            // 平均で見積もると、運が悪い step にちょうど 20 を超える。
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
            // ここが 0 になると、雨 > 0.8 でキューが空になり、
            // ゲームが勝手に雷雨災害を作り始める（§A-3）。
            Assert.True(LightningBudget.Allowance(0, LightningBudget.VanillaMaxStrikes(0)) >= 1);
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
            // §A-3: sf + 45 < m_currentFrameIndex で ReleaseInstance される。
            Assert.False(LightningBudget.HasExpired(1000u, 1045u));
            Assert.True(LightningBudget.HasExpired(1000u, 1046u));
            // まだ来ていない予定は期限切れではない。
            Assert.False(LightningBudget.HasExpired(2000u, 1000u));
        }
    }
}
