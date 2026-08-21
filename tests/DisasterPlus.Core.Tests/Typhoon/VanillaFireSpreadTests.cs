using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 実機で出た <c>DivideByZeroException</c> の再現と、④の対処の固定。
    /// 割り算そのものはバニラの中にあるので、ここで固定できるのは
    /// **「どの経過フレームで 0 になるか」と「④が書く活性化フレームは安全か」**である。
    /// </summary>
    public class VanillaFireSpreadTests
    {
        [Fact]
        public void TheVanillaDivisorIsZeroForExactlyOneThousandTwentyFourFrames()
        {
            // 窓の中は全部 0（＝例外）。
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
            // 窓のすぐ内側と、すぐ外側。**両側**を固定する。
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
            // Thunderstorm プレハブの m_emergingDuration は 8192（実測）。
            // StartDisaster の既定値のままだと、台風を起こした瞬間がちょうど窓の左端。
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

            // 台風の寿命ぶん（8192 フレーム）を全部歩いても除数は下限を割らない。
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
            // m_activationFrame == 0 は「予定が無い」の意味なので、そのまま書かない。
            Assert.Equal(1u, VanillaFireSpread.SafeActivationFrame(0u));

            // その 1 フレームだけ elapsed は -1 になるが、除数は 7 であって 0 ではない。
            Assert.Equal(-1, VanillaFireSpread.ElapsedOf(0u, 1u));
            Assert.Equal(7, VanillaFireSpread.DivisorOf(-1));
            Assert.False(VanillaFireSpread.DividesByZero(-1));
        }

        [Fact]
        public void ElapsedIsSignedSoAFutureActivationDoesNotLookLikeAHugePast()
        {
            // uint のまま引くと 4294959104 になり、「危険な窓」が見えなくなる。
            Assert.Equal(-8192, VanillaFireSpread.ElapsedOf(0u, 8192u));
        }
    }
}
