using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class VortexCloudProfileTests
    {
        [Fact]
        public void TheProfileAndTheLayoutAgreeOnParticleSize()
        {
            // ★★ If these drift apart **the eye fills in.** VortexPuffLayout computes the
            //   eye's clearance from SizeFractionOf, while the Game side decides startSize
            //   from VortexCloudProfile's SizeFraction. **They must be the same number.**
            for (int i = 0; i < 3; i++)
            {
                var layer = (VortexCloudLayer)i;
                Assert.Equal(VortexPuffLayout.SizeFractionOf(layer),
                             VortexCloudProfile.Of(layer).SizeFraction, 6);
            }
        }

        [Fact]
        public void TheCloudIsBrightOnTopAndDarkUnderneath()
        {
            // A thunderhead exactly. Reverse this and it looks like a lump of smoke however
            // it is arranged (the old implementation put a uniform grey into a single copy).
            float deck = Luma(VortexCloudProfile.Deck);
            float tower = Luma(VortexCloudProfile.Tower);
            float canopy = Luma(VortexCloudProfile.Canopy);

            Assert.True(deck < tower, "the cloud base must be darker than the towers");
            Assert.True(tower < canopy, "the canopy must be the brightest layer");
            // Too small a difference and it is only ever "greys of different brightness".
            Assert.True(canopy - deck > 0.35f);
        }

        [Fact]
        public void NoLayerEmitsZeroParticles()
        {
            // ★★ Set rateOverTime to 0 and not one particle appears (EmitParticles keeps
            //   reading it as a multiplier even when emission.enabled is false).
            //   It is the easiest trap of all to fall into.
            for (int i = 0; i < 3; i++)
            {
                VortexCloudProfile p = VortexCloudProfile.Of((VortexCloudLayer)i);
                Assert.True(p.RateOverTime > 0f);
                Assert.True(p.MaxParticles > 0);
                Assert.True(p.LifeMinSeconds > 0f);
                Assert.True(p.LifeMaxSeconds >= p.LifeMinSeconds);
                Assert.InRange(p.Alpha, 0.05f, 1f);
            }
        }

        [Fact]
        public void TheCanopySpreadsSidewaysAndTheTowersRise()
        {
            // The canopy spreads sideways at the neutral buoyancy height
            // (its emission angle is nearly horizontal).
            Assert.True(VortexCloudProfile.Canopy.SpawnAngleMinDegrees >= 60f);
            // The towers rise (a narrow emission angle).
            Assert.True(VortexCloudProfile.Tower.SpawnAngleMaxDegrees <= 45f);
            // Only the towers float slightly (negative gravity).
            // The cloud base and the canopy linger.
            Assert.True(VortexCloudProfile.Tower.GravityModifier < 0f);
            // The canopy lives the longest (it lingers and becomes a flat ceiling).
            Assert.True(VortexCloudProfile.Canopy.LifeMaxSeconds
                        > VortexCloudProfile.Tower.LifeMaxSeconds);
        }

        private static float Luma(VortexCloudProfile p)
        {
            float bright = 0.299f * p.BrightRed + 0.587f * p.BrightGreen + 0.114f * p.BrightBlue;
            float dark = 0.299f * p.DarkRed + 0.587f * p.DarkGreen + 0.114f * p.DarkBlue;
            return (bright + dark) * 0.5f;
        }
    }
}
