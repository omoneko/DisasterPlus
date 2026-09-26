using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Owner's request (2026-08-22): "the eruption sound... make it about twice as loud".
    /// The material already peaks at -1.55 dBFS, so **naively doubling it clips**.
    /// What is pinned down here are two points: "quiet samples are multiplied by exactly
    /// two, and the peak does not clip".
    /// </summary>
    public class LoudnessBoostTests
    {
        /// <summary>
        /// Measured from the bundled erupting-volcano.wav (peak 27412/32767, RMS 5330/32767).
        /// </summary>
        private const float MaterialPeak = 0.8366f;
        private const float MaterialRms = 0.1627f;

        [Fact]
        public void QuietSamplesAreMultipliedExactly()
        {
            // The RMS region is far below the knee, so it must be exactly gain times.
            Assert.Equal(MaterialRms * 2f, LoudnessBoost.Shape(MaterialRms, 2f), 5);
            Assert.Equal(-MaterialRms * 2f, LoudnessBoost.Shape(-MaterialRms, 2f), 5);

            // It is linear right up to just before the knee.
            float justBelow = LoudnessBoost.Threshold / 2f;
            Assert.Equal(LoudnessBoost.Threshold, LoudnessBoost.Shape(justBelow, 2f), 5);
        }

        [Fact]
        public void TheMaterialPeakStopsShortOfClipping()
        {
            float shaped = LoudnessBoost.Shape(MaterialPeak, 2f);

            Assert.True(shaped < 1f, "the peak must not reach full scale, got " + shaped);
            Assert.True(shaped > 0.95f, "the peak should still be loud, got " + shaped);
        }

        [Fact]
        public void NothingEverLeavesTheLegalRange()
        {
            // Even if the material is swapped for one that is already close to clipping,
            // the output always stays within [-1,1].
            foreach (float g in new[] { 1.5f, 2f, 4f, 100f })
            {
                for (int i = -20; i <= 20; i++)
                {
                    float x = i / 20f;
                    float y = LoudnessBoost.Shape(x, g);
                    Assert.InRange(y, -1f, 1f);
                }
            }
        }

        [Fact]
        public void TheSignIsAlwaysKept()
        {
            Assert.True(LoudnessBoost.Shape(0.9f, 2f) > 0f);
            Assert.True(LoudnessBoost.Shape(-0.9f, 2f) < 0f);
            Assert.Equal(0f, LoudnessBoost.Shape(0f, 2f), 6);
        }

        [Fact]
        public void LouderInputStaysLouderOutput()
        {
            // The ordering never swaps even inside the knee
            // (if it swapped, the waveform would be broken).
            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float y = LoudnessBoost.Shape(i / 100f, 2f);
                Assert.True(y >= previous, "must not go down at x=" + (i / 100f));
                previous = y;
            }
        }

        [Fact]
        public void AGainOfOneOrLessChangesNothing()
        {
            // This type has no "make it quieter" use case. It never silently shrinks.
            Assert.Equal(0.4f, LoudnessBoost.Shape(0.4f, 1f), 6);
            Assert.Equal(0.4f, LoudnessBoost.Shape(0.4f, 0.5f), 6);

            var samples = new[] { 0.4f, -0.4f };
            LoudnessBoost.Apply(samples, 1f);
            Assert.Equal(0.4f, samples[0], 6);
            Assert.Equal(-0.4f, samples[1], 6);
        }

        [Fact]
        public void BrokenSamplesBecomeSilenceInsteadOfPoisoningTheClip()
        {
            Assert.Equal(0f, LoudnessBoost.Shape(float.NaN, 2f), 6);
            Assert.Equal(0f, LoudnessBoost.Shape(float.PositiveInfinity, 2f), 6);
        }

        [Fact]
        public void ApplyWritesInPlaceAndReportsThePeak()
        {
            var samples = new[] { 0.1f, -0.2f, MaterialPeak, 0.05f };
            float peak = LoudnessBoost.Apply(samples, 2f);

            Assert.Equal(0.2f, samples[0], 5);
            Assert.Equal(-0.4f, samples[1], 5);
            Assert.Equal(peak, LoudnessBoost.PeakOf(samples), 5);
            Assert.True(peak < 1f);
        }

        [Fact]
        public void AnEmptyOrMissingBufferIsNotAnError()
        {
            Assert.Equal(0f, LoudnessBoost.Apply(null, 2f), 6);
            Assert.Equal(0f, LoudnessBoost.Apply(new float[0], 2f), 6);
            Assert.Equal(0f, LoudnessBoost.PeakOf(null), 6);
        }
    }
}
