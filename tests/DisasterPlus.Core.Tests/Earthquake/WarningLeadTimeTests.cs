using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WarningLeadTimeTests
    {
        [Fact]
        public void NoCoverage_IsTheBaseLeadTime()
        {
            // IL facts document §A-2: loc2 = Min(cov,100) * 6437 / 100 + 1755
            Assert.Equal(WarningLeadTime.BaseFrames, WarningLeadTime.FramesFor(0));
        }

        [Fact]
        public void FullCoverage_IsExactlyThreeInGameHours()
        {
            // 1755 + 6437 = 8192 = 65536 / 8 = exactly 3.0 in-game hours.
            Assert.Equal(8192, WarningLeadTime.FramesFor(100));
        }

        [Fact]
        public void CoverageIsClampedAtOneHundred()
        {
            // Min(coverage, 100) lives on the vanilla side. 255 behaves just like 100.
            Assert.Equal(WarningLeadTime.FramesFor(100), WarningLeadTime.FramesFor(255));
            Assert.Equal(100, WarningLeadTime.ClampCoverage(255));
            Assert.Equal(0, WarningLeadTime.ClampCoverage(-5));
        }

        [Fact]
        public void UsesIntegerDivisionLikeTheGame()
        {
            // The IL uses div.un (integer division). 6437 * 1 / 100 = 64 (not 64.37).
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
            // 1 in-game minute = 65536 / 1440 ≒ 45.51 frames (firestorm design
            // document, appendix A-4).
            // The constant is not hard-coded; the caller passes it in.
            const float framesPerMinute = 65536f / 1440f;
            Assert.Equal(38.6f, WarningLeadTime.MinutesFor(0, framesPerMinute), 1);
            Assert.Equal(180.0f, WarningLeadTime.MinutesFor(100, framesPerMinute), 1);
        }

        [Fact]
        public void MinutesAreZeroWhenTheConversionIsUnusable()
        {
            // In an environment where FeatureHost.FramesPerMinute cannot be obtained
            // (just after start-up, when there is no SimulationManager), falling back to
            // "cannot convert" rather than "warning in 0 minutes" is the caller's
            // responsibility, but we do guarantee here that no NaN or infinity is made.
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, 0f), 4);
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, -1f), 4);
            Assert.Equal(0f, WarningLeadTime.MinutesFor(100, float.NaN), 4);
        }
    }
}
