using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WarningLeadTimeTests
    {
        [Fact]
        public void NoCoverage_IsTheBaseLeadTime()
        {
            // IL 事実文書 §A-2: loc2 = Min(cov,100) * 6437 / 100 + 1755
            Assert.Equal(WarningLeadTime.BaseFrames, WarningLeadTime.FramesFor(0));
        }

        [Fact]
        public void FullCoverage_IsExactlyThreeInGameHours()
        {
            // 1755 + 6437 = 8192 = 65536 / 8 = ちょうど 3.0 ゲーム内時間。
            Assert.Equal(8192, WarningLeadTime.FramesFor(100));
        }

        [Fact]
        public void CoverageIsClampedAtOneHundred()
        {
            // Min(coverage, 100) はバニラ側にある。255 でも 100 と同じ。
            Assert.Equal(WarningLeadTime.FramesFor(100), WarningLeadTime.FramesFor(255));
            Assert.Equal(100, WarningLeadTime.ClampCoverage(255));
            Assert.Equal(0, WarningLeadTime.ClampCoverage(-5));
        }

        [Fact]
        public void UsesIntegerDivisionLikeTheGame()
        {
            // IL は div.un（整数除算）。6437 * 1 / 100 = 64（64.37 ではない）。
            Assert.Equal(WarningLeadTime.BaseFrames + 64, WarningLeadTime.FramesFor(1));
            Assert.Equal(WarningLeadTime.BaseFrames + 643, WarningLeadTime.FramesFor(10));
        }

        [Fact]
        public void LeadTimeIsMonotonic()
        {
            int prev = -1;
            for (int c = 0; c <= 120; c++)
            {
                int f = WarningLeadTime.FramesFor(c);
                Assert.True(f >= prev, "lead time decreased at coverage " + c);
                prev = f;
            }
        }

        [Fact]
        public void MinutesMatchTheKnownFigures()
        {
            // 1 ゲーム内分 = 65536 / 1440 ≒ 45.51 フレーム（火災旋風設計書 付録 A-4）。
            // 定数を直書きせず、呼び出し側から渡す。
            const float framesPerMinute = 65536f / 1440f;
            Assert.Equal(38.6f, WarningLeadTime.MinutesFor(0, framesPerMinute), 1);
            Assert.Equal(180.0f, WarningLeadTime.MinutesFor(100, framesPerMinute), 1);
        }

        [Fact]
        public void MinutesAreZeroWhenTheConversionIsUnusable()
        {
            // FeatureHost.FramesPerMinute が取れない環境（SimulationManager が
            // 居ない起動直後）で「0 分後に警報」ではなく「換算できない」に倒すのは
            // 呼び出し側の責任だが、ここで NaN や無限大を作らないことは保証する。
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, 0f), 4);
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, -1f), 4);
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, float.NaN), 4);
        }
    }
}
