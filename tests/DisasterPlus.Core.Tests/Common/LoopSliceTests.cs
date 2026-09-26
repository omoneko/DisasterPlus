using System;
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Pins down <see cref="LoopSlice"/>.
    ///
    /// Three things are protected:
    /// 1. the frames of the requested window come out verbatim (outside the crossfade region)
    /// 2. **the waveform does not jump** at the seam (no click)
    /// 3. for input that cannot be cut, **the original array is returned as it is**
    ///    (never silence)
    /// </summary>
    public class LoopSliceTests
    {
        private static float[] Ramp(int frames, int channels)
        {
            var s = new float[frames * channels];
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < channels; c++) s[f * channels + c] = f + c * 0.5f;
            }
            return s;
        }

        [Fact]
        public void TheWindowOutsideTheCrossfadeIsCopiedVerbatim()
        {
            float[] src = Ramp(100, 1);
            float[] loop = LoopSlice.Build(src, 1, 10, 50, 5);

            Assert.Equal(50, loop.Length);
            for (int i = 5; i < 50; i++) Assert.Equal(10f + i, loop[i], 4);
        }

        [Fact]
        public void BothChannelsAreKeptInStep()
        {
            float[] src = Ramp(100, 2);
            float[] loop = LoopSlice.Build(src, 2, 10, 40, 4);

            Assert.Equal(80, loop.Length);
            for (int f = 4; f < 40; f++)
            {
                Assert.Equal(10f + f, loop[f * 2], 4);
                Assert.Equal(10f + f + 0.5f, loop[f * 2 + 1], 4);
            }
        }

        [Fact]
        public void TheSeamDoesNotJump()
        {
            // ★ This is the reason the class exists. The step from the end of the loop back
            //   to its start must be of about the same size as a one-sample step inside the
            //   window.
            float[] src = Ramp(200, 1);
            float[] loop = LoopSlice.Build(src, 1, 20, 100, 10);

            float insideStep = Math.Abs(loop[60] - loop[59]);
            float seamStep = Math.Abs(loop[0] - loop[loop.Length - 1]);

            // The material is a ramp, so a plain cut would give a step of 100 (the window
            // length). With the crossfade working it stays within a few samples' worth.
            Assert.True(seamStep < insideStep * 5f,
                        "the loop seam jumps by " + seamStep + " against an in-window step of "
                        + insideStep);
        }

        [Fact]
        public void TheCrossfadeStartsFromTheMaterialThatFollowedTheLoopEnd()
        {
            // Unless the first sample is roughly src[start+length] (i.e. the continuation
            // of the end), the waveform jumps the moment the loop wraps round.
            float[] src = Ramp(300, 1);
            float[] loop = LoopSlice.Build(src, 1, 50, 100, 20);

            float continuation = src[150];      // start(50) + length(100)
            Assert.True(Math.Abs(loop[0] - continuation) < Math.Abs(loop[0] - src[50]),
                        "the first frame must lean on the continuation, not on the window head");
        }

        [Fact]
        public void AZeroLengthCrossfadeStillCopiesTheWindow()
        {
            float[] src = Ramp(100, 1);
            float[] loop = LoopSlice.Build(src, 1, 10, 20, 0);

            Assert.Equal(20, loop.Length);
            for (int i = 0; i < 20; i++) Assert.Equal(10f + i, loop[i], 4);
        }

        // ── When it cannot be cut, leave it alone (**never silence**) ──────────────────

        [Fact]
        public void AFileTooShortForTheWindowIsReturnedUnchanged()
        {
            float[] src = Ramp(40, 2);
            float[] loop = LoopSlice.Build(src, 2, 10, 30, 5);
            Assert.Same(src, loop);
        }

        [Fact]
        public void ThereMustBeMaterialAfterTheLoopEndForTheCrossfade()
        {
            // A file with exactly the window's worth of material. There is nothing to mix
            // in, so it is not cut.
            float[] src = Ramp(50, 1);
            Assert.Same(src, LoopSlice.Build(src, 1, 0, 50, 5));
            // Just 5 extra frames are enough for it to be cut.
            float[] longer = Ramp(55, 1);
            Assert.Equal(50, LoopSlice.Build(longer, 1, 0, 50, 5).Length);
        }

        [Fact]
        public void NonsenseArgumentsReturnTheInputRatherThanThrowing()
        {
            float[] src = Ramp(100, 1);
            Assert.Same(src, LoopSlice.Build(src, 0, 0, 10, 1));
            Assert.Same(src, LoopSlice.Build(src, 1, -1, 10, 1));
            Assert.Same(src, LoopSlice.Build(src, 1, 0, 0, 1));
            Assert.Same(src, LoopSlice.Build(src, 1, 0, 10, -1));
            Assert.Same(src, LoopSlice.Build(src, 1, 0, 10, 20));
        }

        [Fact]
        public void NullSamplesGiveAnEmptyArrayNotAnException()
        {
            Assert.Empty(LoopSlice.Build(null, 2, 0, 10, 1));
            Assert.Empty(LoopSlice.Build(null, 2, 44100, 0f, 1f, 0.1f));
        }

        [Fact]
        public void TheSecondsOverloadLandsOnTheSameFrames()
        {
            float[] src = Ramp(44100, 1);
            float[] bySeconds = LoopSlice.Build(src, 1, 4410, 0.1f, 0.5f, 0.05f);
            float[] byFrames = LoopSlice.Build(src, 1, 441, 2205, 220);

            Assert.Equal(byFrames.Length, bySeconds.Length);
            for (int i = 0; i < byFrames.Length; i++) Assert.Equal(byFrames[i], bySeconds[i], 4);
        }

        [Fact]
        public void AZeroSampleRateLeavesTheInputAlone()
        {
            float[] src = Ramp(100, 1);
            Assert.Same(src, LoopSlice.Build(src, 1, 0, 0.1f, 0.5f, 0.05f));
            Assert.Same(src, LoopSlice.Build(src, 1, 44100, float.NaN, 0.5f, 0.05f));
        }

        [Fact]
        public void TheCrossfadeKeepsTheLevelRoughlyConstant()
        {
            // The mix is equal-power (√), so the level must not dip at the seam even for
            // uncorrelated material. Between two DC 1.0 sources, √t + √(1-t) >= 1, so it
            // never falls below 1.
            var src = new float[400];
            for (int i = 0; i < src.Length; i++) src[i] = 1f;

            float[] loop = LoopSlice.Build(src, 1, 0, 200, 40);
            for (int i = 0; i < 40; i++)
            {
                Assert.True(loop[i] >= 0.99f,
                            "frame " + i + " dipped to " + loop[i] + " across the crossfade");
            }
        }
    }
}
