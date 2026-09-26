using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// Reproduces the <c>DivideByZeroException</c> seen in-game and pins down feature no. 4's
    /// fix. The division itself lives inside vanilla, so all we can pin down here is
    /// **"at which elapsed frames it becomes 0" and "whether the activation frame feature
    /// no. 4 writes is safe"**.
    /// </summary>
    public class VanillaFireSpreadTests
    {
        [Fact]
        public void TheVanillaDivisorIsZeroForExactlyOneThousandTwentyFourFrames()
        {
            // Everything inside the window is 0 (i.e. an exception).
            for (int e = VanillaFireSpread.UnsafeElapsedFirst;
                 e <= VanillaFireSpread.UnsafeElapsedLast; e++)
            {
                Assert.True(VanillaFireSpread.DividesByZero(e),
                            "elapsed " + e + " must divide by zero");
            }

            Assert.Equal(1024,
                VanillaFireSpread.UnsafeElapsedLast - VanillaFireSpread.UnsafeElapsedFirst + 1);
        }

        [Fact]
        public void JustOutsideTheWindowTheDivisorIsNonZeroOnBothSides()
        {
            // Just inside the window and just outside it. **Both sides** are pinned down.
            Assert.True(VanillaFireSpread.DividesByZero(VanillaFireSpread.UnsafeElapsedFirst));
            Assert.True(VanillaFireSpread.DividesByZero(VanillaFireSpread.UnsafeElapsedLast));

            Assert.False(VanillaFireSpread.DividesByZero(VanillaFireSpread.UnsafeElapsedFirst - 1));
            Assert.False(VanillaFireSpread.DividesByZero(VanillaFireSpread.UnsafeElapsedLast + 1));

            Assert.Equal(-1, VanillaFireSpread.DivisorOf(VanillaFireSpread.UnsafeElapsedFirst - 1));
            Assert.Equal(1, VanillaFireSpread.DivisorOf(VanillaFireSpread.UnsafeElapsedLast + 1));
        }

        [Fact]
        public void ThunderstormsEmergingDurationLandsExactlyOnTheWindow()
        {
            // The m_emergingDuration of the Thunderstorm prefab is 8192 (measured).
            // Left at StartDisaster's default, the moment the typhoon is raised lands
            // exactly on the left edge of the window.
            const uint start = 1000u;
            const uint emergingDuration = 8192u;
            uint vanillaActivation = start + emergingDuration;

            int elapsed = VanillaFireSpread.ElapsedOf(start, vanillaActivation);
            Assert.Equal(-8192, elapsed);
            Assert.True(VanillaFireSpread.DividesByZero(elapsed));
            Assert.False(VanillaFireSpread.IsSafeActivation(start, vanillaActivation));
        }

        [Fact]
        public void PullingTheActivationFrameToTheStartFrameRemovesTheWindow()
        {
            const uint start = 1000u;
            uint activation = VanillaFireSpread.SafeActivationFrame(start);

            Assert.Equal(start, activation);
            Assert.True(VanillaFireSpread.IsSafeActivation(start, activation));

            // Walking through the typhoon's whole lifetime (8192 frames), the divisor never
            // drops below the floor.
            for (uint f = start; f <= start + 8192u; f++)
            {
                int elapsed = VanillaFireSpread.ElapsedOf(f, activation);
                Assert.True(elapsed >= 0);
                Assert.True(VanillaFireSpread.DivisorOf(elapsed) >= VanillaFireSpread.MinSafeDivisor);
                Assert.False(VanillaFireSpread.DividesByZero(elapsed));
            }
        }

        [Fact]
        public void FrameZeroIsRaisedToOneBecauseZeroMeansNoScheduleAtAll()
        {
            // m_activationFrame == 0 means "there is no schedule at all", so we never write
            // it as it stands.
            Assert.Equal(1u, VanillaFireSpread.SafeActivationFrame(0u));

            // For that one frame elapsed is -1, but the divisor is 7, not 0.
            Assert.Equal(-1, VanillaFireSpread.ElapsedOf(0u, 1u));
            Assert.Equal(7, VanillaFireSpread.DivisorOf(-1));
            Assert.False(VanillaFireSpread.DividesByZero(-1));
        }

        [Fact]
        public void ElapsedIsSignedSoAFutureActivationDoesNotLookLikeAHugePast()
        {
            // Subtract as uint and you get 4294959104, which hides the "dangerous window".
            Assert.Equal(-8192, VanillaFireSpread.ElapsedOf(0u, 8192u));
        }
    }
}
