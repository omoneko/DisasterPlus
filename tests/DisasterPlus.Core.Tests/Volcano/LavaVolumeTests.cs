using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The owner's request: "please also vary the amount of magma that flows out with the
    /// scale of the eruption".
    /// The two essentials are that **a setting of 0 stays 0** (it is switched off) and that
    /// **even a small volcano produces one flow**.
    /// </summary>
    public class LavaVolumeTests
    {
        private const int MaxFlows = 8;
        private const int BaseSteps = 128;

        [Fact]
        public void TheReferenceRadiusChangesNothing()
        {
            // With the default settings and the default slider position, the behaviour must
            // be exactly as it has been.
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
            // 0 means "switched off", not "the smallest eruption".
            foreach (float r in new[] { 250f, 1200f, 3000f })
            {
                Assert.Equal(0, LavaVolume.FlowCount(0, MaxFlows, r));
            }
        }

        [Fact]
        public void TheSmallestVolcanoStillPoursOneFlow()
        {
            // Dropping to zero flows can only look like "the lava feature is broken".
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
