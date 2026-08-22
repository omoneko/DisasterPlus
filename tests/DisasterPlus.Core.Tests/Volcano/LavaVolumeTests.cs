using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 所有者の依頼「噴火の規模によって流れ出るマグマの量も変えてください」。
    /// **設定 0 は 0 のまま**（切っている）と、**小さい火山でも 1 本は出る**の 2 つが要。
    /// </summary>
    public class LavaVolumeTests
    {
        private const int MaxFlows = 8;
        private const int BaseSteps = 128;

        [Fact]
        public void TheReferenceRadiusChangesNothing()
        {
            // 既定の設定・既定のスライダー位置では今までどおりでなければならない。
            Assert.Equal(1f, LavaVolume.Unit(LavaVolume.ReferenceRadiusMetres), 3);
            Assert.Equal(1f, LavaVolume.LengthFactor(LavaVolume.ReferenceRadiusMetres), 3);
            Assert.Equal(4, LavaVolume.FlowCount(4, MaxFlows, LavaVolume.ReferenceRadiusMetres));
            Assert.Equal(BaseSteps,
                         LavaVolume.StepBudget(BaseSteps, LavaVolume.ReferenceRadiusMetres));
        }

        [Fact]
        public void ABiggerVolcanoPoursMoreLava()
        {
            int small = LavaVolume.FlowCount(4, MaxFlows, 400f);
            int reference = LavaVolume.FlowCount(4, MaxFlows, 1200f);
            int large = LavaVolume.FlowCount(4, MaxFlows, 3000f);

            Assert.True(small < reference, small + " < " + reference);
            Assert.True(reference < large, reference + " < " + large);

            Assert.True(LavaVolume.StepBudget(BaseSteps, 400f)
                        < LavaVolume.StepBudget(BaseSteps, 3000f));
        }

        [Fact]
        public void TurningLavaOffStaysOff()
        {
            // 0 は「切っている」であって「いちばん小さい噴火」ではない。
            foreach (float r in new[] { 250f, 1200f, 3000f })
            {
                Assert.Equal(0, LavaVolume.FlowCount(0, MaxFlows, r));
            }
        }

        [Fact]
        public void TheSmallestVolcanoStillPoursOneFlow()
        {
            // 0 本にすると「溶岩の機能が壊れている」にしか見えない。
            Assert.True(LavaVolume.FlowCount(1, MaxFlows, 250f) >= 1);
            Assert.True(LavaVolume.FlowCount(2, MaxFlows, 250f) >= 1);
        }

        [Fact]
        public void TheImplementationLimitsAreNeverExceeded()
        {
            foreach (float r in new[] { 250f, 1200f, 3000f, 100000f })
            {
                Assert.InRange(LavaVolume.FlowCount(MaxFlows, MaxFlows, r), 1, MaxFlows);
                Assert.InRange(LavaVolume.StepBudget(BaseSteps, r), 1, BaseSteps);
            }
        }

        [Fact]
        public void TheUnitAndFactorStayInsideTheirBands()
        {
            foreach (float r in new[] { 1f, 250f, 1200f, 3000f, 50000f })
            {
                Assert.InRange(LavaVolume.Unit(r), LavaVolume.MinUnit, LavaVolume.MaxUnit);
                Assert.InRange(LavaVolume.LengthFactor(r),
                               LavaVolume.MinLengthFactor, LavaVolume.MaxLengthFactor);
            }
        }

        [Fact]
        public void BrokenInputFallsBackToTheConfiguredAmount()
        {
            Assert.Equal(1f, LavaVolume.Unit(float.NaN), 3);
            Assert.Equal(1f, LavaVolume.Unit(-5f), 3);
            Assert.Equal(4, LavaVolume.FlowCount(4, MaxFlows, float.NaN));
        }
    }
}
