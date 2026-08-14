using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class EndingStallTests
    {
        [Fact]
        public void JustEnteredEnding_NotStuck()
        {
            Assert.False(EndingStall.IsStuck(0f, 10));
        }

        [Fact]
        public void WithinLimit_NotStuck()
        {
            // 正常な終了はゲーム内 1 分にも満たない。
            Assert.False(EndingStall.IsStuck(0.5f, 10));
            Assert.False(EndingStall.IsStuck(9f, 10));
        }

        [Fact]
        public void ExactlyAtThreshold_NotStuck()
        {
            // 閾値ちょうどは通す（誤検知を避ける側に倒す）。
            Assert.False(EndingStall.IsStuck(20f, 10));
        }

        [Fact]
        public void BeyondThreshold_IsStuck()
        {
            Assert.True(EndingStall.IsStuck(20.1f, 10));
            Assert.True(EndingStall.IsStuck(1000f, 10));
        }

        [Fact]
        public void ThresholdFollowsConfiguredLifetime()
        {
            // 設定を短くすれば検出も早くなる。
            Assert.True(EndingStall.IsStuck(3f, 1));
            Assert.False(EndingStall.IsStuck(3f, 60));
        }

        [Fact]
        public void NonPositiveLifetime_NeverStuck()
        {
            // 設定が壊れているときに全件を stuck と報告しない。
            Assert.False(EndingStall.IsStuck(9999f, 0));
            Assert.False(EndingStall.IsStuck(9999f, -5));
        }

        [Fact]
        public void MultiplierIsTwo()
        {
            // 閾値の根拠（正常値の 20 倍以上待つ）を数値として固定する。
            Assert.Equal(2f, EndingStall.LifetimeMultiplier);
        }
    }
}
