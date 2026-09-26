using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The owner's request: "a magma pool in the crater (glowing like lava) and light
    /// radiating into the eruption plume".
    ///
    /// ★★ The most important thing is that it **must not strobe**. <c>LavaGlow</c> once
    ///    made the lava flow flicker with UV scrolling and was called out for it.
    /// </summary>
    public class CraterGlowTests
    {
        [Fact]
        public void ThePoolNeverStrobes()
        {
            // Ten seconds' worth at 60 fps. **The per-frame change must never become a
            // visible step.**
            const float Step = 1f / 60f;
            float previous = CraterGlow.PoolBrightness(1f, 0f);
            float worst = 0f;

            for (int i = 1; i <= 600; i++)
            {
                float now = CraterGlow.PoolBrightness(1f, i * Step);
                float delta = now > previous ? now - previous : previous - now;
                if (delta > worst) worst = delta;
                previous = now;
            }

            // The fastest component is 0.19 Hz. A single frame's change should not even
            // reach 1%.
            Assert.True(worst < 0.01f, "per-frame change was " + worst);
        }

        [Fact]
        public void TheCraterIsAlreadyRedBeforeTheEruptionPeaks()
        {
            // It does not go out even at strength 0. **The crater is red before the
            // eruption starts.**
            for (int i = 0; i < 40; i++)
            {
                Assert.True(CraterGlow.PoolBrightness(0f, i * 0.37f) > 0f);
            }
        }

        [Fact]
        public void AStrongerEruptionIsBrighterAndWider()
        {
            Assert.True(CraterGlow.PoolBrightness(1f, 3f) > CraterGlow.PoolBrightness(0.1f, 3f));
            Assert.True(CraterGlow.PoolRadiusMetres(100f, 1f)
                        > CraterGlow.PoolRadiusMetres(100f, 0f));
        }

        [Fact]
        public void ThePoolNeverSpillsOverTheCraterRim()
        {
            // Looking as though it spills over the rim is the lava flow's job, not the
            // pool's.
            foreach (float u in new[] { 0f, 0.5f, 1f, 5f })
            {
                Assert.True(CraterGlow.PoolRadiusMetres(100f, u) <= 100f);
            }
        }

        [Fact]
        public void TheLightFadesToExactlyZeroBeforeTheTopOfThePlume()
        {
            const float Height = 1000f;
            float reach = Height * CraterGlow.ReachFraction;

            Assert.True(CraterGlow.LightAt(0f, Height, 1f) > 0f);
            Assert.Equal(0f, CraterGlow.LightAt(reach, Height, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(Height, Height, 1f), 5);
        }

        [Fact]
        public void TheLightOnlyGetsDarkerAsItGoesUp()
        {
            const float Height = 1000f;
            float previous = float.MaxValue;

            for (int i = 0; i <= 100; i++)
            {
                float y = Height * (i / 100f);
                float k = CraterGlow.LightAt(y, Height, 1f);
                Assert.True(k <= previous + 1e-5f, "went back up at y=" + y);
                previous = k;
            }
        }

        [Fact]
        public void EverythingStaysInsideZeroToOne()
        {
            foreach (float u in new[] { -1f, 0f, 0.5f, 1f, 9f, float.NaN })
            {
                Assert.InRange(CraterGlow.PoolBrightness(u, 7f), 0f, 1f);
                Assert.InRange(CraterGlow.LightAt(50f, 900f, u), 0f, 1f);
            }
        }

        [Fact]
        public void BrokenInputDoesNotLightAnything()
        {
            Assert.Equal(0f, CraterGlow.PoolRadiusMetres(float.NaN, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(float.NaN, 900f, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(50f, 0f, 1f), 5);
        }
    }
}
