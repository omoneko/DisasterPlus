using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class EndingStallTests
    {
        /// <summary>バニラ実測の 65536/1440。ゲーム側は FeatureHost.FramesPerMinute を渡す。</summary>
        private const float Fpm = EndingStall.VanillaFramesPerMinute;

        /// <summary>IL 実測どおりの、正常な解体にかかるゲーム内分（約 8.09）。</summary>
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
            // 車両は 16 sim フレームに 1 回しかステップしない（VehicleManager.SimulationStepImpl）。
            // 23 ステップ × 16 フレーム = 368 フレーム ≒ 8.09 ゲーム内分。
            Assert.Equal(368, EndingStall.TeardownFrames);
            Assert.InRange(HealthyTeardownMinutes, 8.0f, 8.2f);
        }

        [Fact]
        public void HealthyTeardownIsNeverReportedStuck_AtAnyAllowedLifetime()
        {
            // これがこの修正の動機。スライダーは 1〜60 分を許すので、
            // 倍率 2 だけを閾値にすると 4 分以下の設定で正常な解体が STUCK になる。
            for (int lifetime = 1; lifetime <= 60; lifetime++)
            {
                Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, lifetime, Fpm));
            }
        }

        [Fact]
        public void ShortLifetimeStillLeavesRoomForATeardown()
        {
            // 旧実装は maxLifetime=1 のとき閾値 2 分。正常な解体（約 8.1 分）で必ず誤報した。
            Assert.False(EndingStall.IsStuck(3f, 1, Fpm));
            Assert.True(EndingStall.ThresholdMinutes(1, Fpm) > HealthyTeardownMinutes * 3f);
        }

        [Fact]
        public void FloorAppliesUntilTheLifetimeTermOvertakesIt()
        {
            float floor = EndingStall.MinimumMinutes(Fpm);
            Assert.InRange(floor, 32f, 33f);   // 368 / 45.51 * 4

            // 短い設定では下限が効き、設定値によらず同じ閾値になる。
            Assert.Equal(floor, EndingStall.ThresholdMinutes(1, Fpm));
            Assert.Equal(floor, EndingStall.ThresholdMinutes(16, Fpm));

            // 長い設定では 2 倍の項が下限を追い越す。
            Assert.Equal(120f, EndingStall.ThresholdMinutes(60, Fpm));
        }

        [Fact]
        public void ExactlyAtThreshold_NotStuck()
        {
            // 閾値ちょうどは通す（誤検知を避ける側に倒す）。
            float t = EndingStall.ThresholdMinutes(10, Fpm);
            Assert.False(EndingStall.IsStuck(t, 10, Fpm));
        }

        [Fact]
        public void BeyondThreshold_IsStuck()
        {
            // 「終わらない」は永久に続くので、閾値を越えれば必ず捕まる。
            Assert.True(EndingStall.IsStuck(1000f, 10, Fpm));
            Assert.True(EndingStall.IsStuck(1000f, 60, Fpm));
        }

        [Fact]
        public void NonPositiveLifetime_FallsBackToTheMeasuredFloor()
        {
            // 設定が壊れていても、下限だけで判定できる。
            // 下限は正常な解体の 4 倍あるので、これで誤検知にはならない。
            Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, 0, Fpm));
            Assert.False(EndingStall.IsStuck(HealthyTeardownMinutes, -5, Fpm));
            Assert.True(EndingStall.IsStuck(9999f, 0, Fpm));
            Assert.True(EndingStall.IsStuck(9999f, -5, Fpm));
        }

        [Fact]
        public void NonPositiveFramesPerMinute_FallsBackToTheVanillaRate()
        {
            // ゲーム側から異常な値が来ても 0 除算や無限大の閾値にしない。
            Assert.Equal(EndingStall.MinimumMinutes(Fpm), EndingStall.MinimumMinutes(0f));
            Assert.Equal(EndingStall.MinimumMinutes(Fpm), EndingStall.MinimumMinutes(-1f));
        }

        [Fact]
        public void SlowerGameClockRaisesTheFloor()
        {
            // 下限はフレーム数で測った実測値から導くので、
            // 1 分あたりのフレーム数が変われば分に直した下限も追従する。
            Assert.Equal(EndingStall.MinimumMinutes(Fpm) * 2f,
                         EndingStall.MinimumMinutes(Fpm / 2f), 3);
        }

        [Fact]
        public void CalibrationConstantsAreThoseMeasuredFromIl()
        {
            // 閾値の根拠を数値として固定する。ここを触るなら IL を読み直すこと。
            Assert.Equal(16, EndingStall.FramesPerVehicleStep);
            Assert.Equal(23, EndingStall.TeardownVehicleSteps);
            Assert.Equal(4f, EndingStall.TeardownSafetyFactor);
            Assert.Equal(2f, EndingStall.LifetimeMultiplier);
        }
    }
}
