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
            // 回帰テスト: 条件を満たし続けても絶対上限で必ず打ち切られること。
            // これが無いと延焼拡大の自己強化ループで旋風が永久に居座る。
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
            // 火が一瞬弱まってもすぐ戻れば存続する。ちらつきで消えないこと。
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
            // ポーズやセーブロードで時間が巻き戻ることがある。負値で寿命が伸びてはいけない。
            var life = FireWhirlLifecycle.Start().Advance(5f, true).Advance(-3f, true);
            Assert.Equal(5f, life.ElapsedMinutes, 4);
        }

        [Fact]
        public void Advance_ZeroDelta_ChangesNothing()
        {
            // ポーズ中は経過ゼロ。呼ばれても状態が動かないこと。
            var life = FireWhirlLifecycle.Start().Advance(4f, false).Advance(0f, false);
            Assert.Equal(4f, life.ElapsedMinutes, 4);
            Assert.Equal(4f, life.ConditionBrokenMinutes, 4);
        }
    }
}
