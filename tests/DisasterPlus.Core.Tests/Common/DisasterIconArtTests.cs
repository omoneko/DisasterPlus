using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Owner's request (2026-08-22): "I would like the volcano and typhoon tab icons
    /// made into illustrations."
    ///
    /// ★★ Whether the artwork is any good cannot be decided by a test (look at it with
    ///    <c>tools/IconPreview</c>). What is pinned here are the <b>conditions for a
    ///    shape that still reads when made small</b> —— for either picture, if these
    ///    break at the real size of 70 px it stops making any sense.
    /// </summary>
    public class DisasterIconArtTests
    {
        /// <summary>
        /// Scan at close to the real size (look at the picture that actually appears,
        /// not a blown-up one).
        /// </summary>
        private const int Steps = 70;

        [Fact]
        public void BothIconsActuallyDrawSomething()
        {
            Assert.True(Coverage(true) > 0.15f, "the volcano icon is nearly empty");
            Assert.True(Coverage(false) > 0.40f, "the typhoon icon is nearly empty");
        }

        [Fact]
        public void NeitherIconFillsTheWholeSquare()
        {
            // Painting right out to the edge makes it look like a "square plate" rather
            // than standing out from the tile background.
            Assert.True(Coverage(true) < 0.75f, "the volcano icon is a solid block");
            Assert.True(Coverage(false) < 0.85f, "the typhoon icon is a solid block");
        }

        [Fact]
        public void TheVolcanoHasGroundAtTheBottomAndSkyAtTheTopCorners()
        {
            // The mountain goes at the bottom, the plume up the middle. **The top
            // corners must be left empty.**
            Assert.True(DisasterIconArt.Volcano(0.5f, 0.05f).A > 0, "no mountain at the bottom");
            Assert.Equal(0, DisasterIconArt.Volcano(0.04f, 0.96f).A);
            Assert.Equal(0, DisasterIconArt.Volcano(0.96f, 0.96f).A);
        }

        [Fact]
        public void TheVolcanoShowsLava()
        {
            // If not a single pixel of lava shows, it is just a mountain.
            int hot = 0;
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    IconPixel p = DisasterIconArt.Volcano((x + 0.5f) / Steps, (y + 0.5f) / Steps);
                    if (p.A > 0 && p.R > 200 && p.G < 200 && p.B < 120) hot++;
                }
            }

            Assert.True(hot > Steps, hot + " lava pixels is not enough to read as a volcano");
        }

        [Fact]
        public void TheTyphoonIsRoundAndKeepsItsEyeOpen()
        {
            // The corners are transparent (it is round).
            Assert.Equal(0, DisasterIconArt.Typhoon(0.02f, 0.02f).A);
            Assert.Equal(0, DisasterIconArt.Typhoon(0.98f, 0.98f).A);

            // The centre is the sea, not cloud (the eye). **Filled in with cloud, it
            // does not look like a typhoon.**
            IconPixel eye = DisasterIconArt.Typhoon(0.5f, 0.5f);
            Assert.True(eye.A > 0, "the eye should still be part of the disc");
            Assert.True(eye.B > eye.R, "the eye should be the dark sea, not white cloud");
        }

        [Fact]
        public void TheTyphoonActuallySpirals()
        {
            // Going once round at the same radius, cloud and sea must **swap over
            // several times**. If they do not swap, it is just a ring, not a spiral.
            int flips = 0;
            bool wasCloud = false;

            for (int i = 0; i <= 360; i++)
            {
                double a = i * System.Math.PI / 180.0;
                float u = 0.5f + 0.30f * (float)System.Math.Cos(a);
                float v = 0.5f + 0.30f * (float)System.Math.Sin(a);

                IconPixel p = DisasterIconArt.Typhoon(u, v);
                bool cloud = p.A > 0 && p.R > 150;

                if (i > 0 && cloud != wasCloud) flips++;
                wasCloud = cloud;
            }

            Assert.True(flips >= 4, "only " + flips + " cloud/sea transitions; that is not a spiral");
        }

        [Fact]
        public void EveryDrawnPixelIsFullyOpaque()
        {
            // Do not produce half-transparent pixels. The tile background can be either
            // light or dark, so a half-transparent one **changes colour depending on
            // the background**.
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    float u = (x + 0.5f) / Steps;
                    float v = (y + 0.5f) / Steps;

                    foreach (IconPixel p in new[]
                             {
                                 DisasterIconArt.Volcano(u, v),
                                 DisasterIconArt.Typhoon(u, v),
                             })
                    {
                        Assert.True(p.A == 0 || p.A == 255, "half-transparent pixel at " + u + "," + v);
                    }
                }
            }
        }

        [Fact]
        public void BrokenInputDrawsNothingInsteadOfThrowing()
        {
            Assert.Equal(0, DisasterIconArt.Volcano(float.NaN, 0.5f).A);
            Assert.Equal(0, DisasterIconArt.Typhoon(0.5f, float.PositiveInfinity).A);
        }

        private static float Coverage(bool volcano)
        {
            int drawn = 0;
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    float u = (x + 0.5f) / Steps;
                    float v = (y + 0.5f) / Steps;
                    IconPixel p = volcano
                        ? DisasterIconArt.Volcano(u, v)
                        : DisasterIconArt.Typhoon(u, v);
                    if (p.A > 0) drawn++;
                }
            }
            return drawn / (float)(Steps * Steps);
        }
    }
}
