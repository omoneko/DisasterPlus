using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Pins down our copy of vanilla's shaking formula (IL facts document §A-7).
    ///
    /// **The single most important case is <see cref="IntensityFactorIsZeroAtTheVanillaDefault"/>.**
    /// That the added term is exactly 0 at intensity 55 is the only grounds for shipping this
    /// feature ON by default; break it and every player's screen changes while we still claim
    /// "behaves the same as vanilla".
    /// </summary>
    public class ShakeWaveformTests
    {
        [Fact]
        public void EnvelopeRateGivesA256FramePeriod()
        {
            // IL: 0.5 - 0.5*cos(t * 0.02454369). 2π / 0.02454369 = 256.0.
            double period = 2.0 * System.Math.PI / ShakeWaveform.EnvelopeRate;
            Assert.Equal(ShakeWaveform.EnvelopePeriodFrames, period, 1);
        }

        [Fact]
        public void EnvelopeIsZeroAtTheStartAndPeakInTheMiddle()
        {
            Assert.Equal(0f, ShakeWaveform.AmplitudeAt(0f, 0f), 4);
            Assert.Equal(0f, ShakeWaveform.AmplitudeAt(0f, ShakeWaveform.EnvelopePeriodFrames), 3);
            Assert.Equal(ShakeWaveform.BaseAmplitude,
                         ShakeWaveform.AmplitudeAt(0f, ShakeWaveform.EnvelopePeriodFrames / 2f), 3);
        }

        [Fact]
        public void AmplitudeFallsOffWithDistance()
        {
            float t = ShakeWaveform.EnvelopePeriodFrames / 2f;
            float near = ShakeWaveform.AmplitudeAt(0f, t);
            float mid = ShakeWaveform.AmplitudeAt(1000f, t);
            float far = ShakeWaveform.AmplitudeAt(5000f, t);
            Assert.True(near > mid && mid > far, "amplitude must decrease with distance");
            // 1 / (1 + 1000*0.001) = 0.5
            Assert.Equal(ShakeWaveform.BaseAmplitude * 0.5f, mid, 3);
        }

        [Fact]
        public void DisplacementIsBoundedByTwiceTheAmplitude()
        {
            for (float t = 0f; t < 2048f; t += 0.37f)
            {
                float amp = ShakeWaveform.AmplitudeAt(250f, t);
                float d = ShakeWaveform.DisplacementAt(250f, t);
                Assert.True(System.Math.Abs(d) <= 2f * amp + 1e-4f, "displacement out of envelope at t=" + t);
            }
        }

        [Fact]
        public void DisplacementActuallyOscillates()
        {
            // It is the sum of two sinusoids, so the sign changes many times. This pins down
            // that it has not been collapsed into a constant.
            int signChanges = 0;
            float prev = ShakeWaveform.DisplacementAt(0f, 128f);
            for (float t = 128.5f; t < 200f; t += 0.5f)
            {
                float d = ShakeWaveform.DisplacementAt(0f, t);
                if ((d < 0f) != (prev < 0f)) signChanges++;
                prev = d;
            }
            Assert.True(signChanges > 5, "too few sign changes: " + signChanges);
        }

        [Fact]
        public void IntensityFactorIsZeroAtTheVanillaDefault()
        {
            // At intensity 55 the added term is exactly 0 = behaviour completely identical
            // to vanilla. This is the very grounds for shipping it ON by default, so it must
            // always be pinned down.
            Assert.Equal(0f, ShakeWaveform.IntensityFactor(SeismicIntensity.VanillaDefaultIntensity), 5);
        }

        [Fact]
        public void IntensityFactorIsNegativeBelowTheDefaultAndClampedAbove()
        {
            Assert.True(ShakeWaveform.IntensityFactor(0) < 0f);
            Assert.Equal(-1f, ShakeWaveform.IntensityFactor(0), 4);
            Assert.Equal(ShakeWaveform.MaxIntensityFactor, ShakeWaveform.IntensityFactor(255), 4);
        }

        [Fact]
        public void ShakingWindowMatchesTheVanillaGuard()
        {
            // IL: return if e <= 0, return if e >= m_activeDuration.
            Assert.False(ShakeWaveform.IsShaking(0, 1000u));
            Assert.False(ShakeWaveform.IsShaking(-5, 1000u));
            Assert.True(ShakeWaveform.IsShaking(1, 1000u));
            Assert.True(ShakeWaveform.IsShaking(999, 1000u));
            Assert.False(ShakeWaveform.IsShaking(1000, 1000u));
            // Always false when m_activeDuration cannot be read (0).
            Assert.False(ShakeWaveform.IsShaking(1, 0u));
        }

        // ── The shaking amplitude itself (overall review C3) ────────────────────────

        [Fact]
        public void PeakAmplitudeIsTheVanillaAmpBeforeTheEnvelope()
        {
            // IL_0069: amp = 0.3f / (1f + dist * 0.001f). It matches at the peak of the envelope.
            float t = ShakeWaveform.EnvelopePeriodFrames / 2f;
            foreach (float d in new[] { 0f, 250f, 1000f, 9000f })
            {
                Assert.Equal(ShakeWaveform.PeakAmplitudeAt(d), ShakeWaveform.AmplitudeAt(d, t), 4);
            }
        }

        [Fact]
        public void ShakingHasNoRadiusCutOff()
        {
            // **This is the heart of C3.** Beyond the R of the overall disc (3100 m at
            // intensity 55) the shaking still does not become 0. At 10 km it is around 9%
            // of the epicentre.
            float epicentre = ShakeWaveform.PeakAmplitudeAt(0f);
            float tenKm = ShakeWaveform.PeakAmplitudeAt(10000f);
            Assert.True(tenKm > 0f, "vanilla's shaking never cuts off with distance");
            Assert.Equal(1f / 11f, tenKm / epicentre, 4);
        }

        // ── The full scale of the bar (overall review I4) ───────────────────────────

        [Fact]
        public void MaxDisplacementIsTwiceTheBaseAmplitude()
        {
            Assert.Equal(0.6f, ShakeWaveform.MaxDisplacement, 4);

            // The actual displacement must not exceed it (beyond it the normalisation caps at 1).
            for (float t = 0f; t < 1024f; t += 0.31f)
            {
                Assert.True(System.Math.Abs(ShakeWaveform.DisplacementAt(0f, t))
                            <= ShakeWaveform.MaxDisplacement + 1e-4f);
            }
        }

        [Fact]
        public void NormalisedDisplacementFillsTheBarOnlyAtTheTheoreticalMaximum()
        {
            Assert.Equal(0f, ShakeWaveform.NormalisedDisplacement(0f), 4);
            Assert.Equal(0.5f, ShakeWaveform.NormalisedDisplacement(0.3f), 4);
            Assert.Equal(1f, ShakeWaveform.NormalisedDisplacement(ShakeWaveform.MaxDisplacement), 4);
            // The sign is dropped (the bar only looks at the magnitude).
            Assert.Equal(0.5f, ShakeWaveform.NormalisedDisplacement(-0.3f), 4);
            // Capped at the upper limit.
            Assert.Equal(1f, ShakeWaveform.NormalisedDisplacement(99f), 4);
        }

        // ── The sampling interval (overall review I5) ───────────────────────────

        [Fact]
        public void FirstUnsampledFrameFillsTheGapLeftByTheSimulationSpeed()
        {
            // At speed 3 m_currentFrameIndex jumps by 9. Unless all 9 skipped frames are
            // filled in, the main component with its period of 10 frames aliases into a
            // spurious long-period wave.
            Assert.Equal(1001u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1009u, 9));
            // At speed 1 (one frame at a time) it is just that one frame.
            Assert.Equal(1001u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1001u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameNeverExceedsTheSubSampleBudget()
        {
            // After a big jump across a pause or a save, we do not go back beyond the window.
            Assert.Equal(1992u, ShakeWaveform.FirstUnsampledFrame(10u, true, 2000u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameTakesOnlyTheCurrentFrameWithoutHistory()
        {
            // Frame 0 can genuinely exist, so "nothing sampled yet" is not represented by 0.
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(0u, false, 500u, 9));
            // Called twice on the same frame it does not go back (it creates no duplicate sample).
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(500u, true, 500u, 9));
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(900u, true, 500u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameIsSafeNearFrameZero()
        {
            Assert.Equal(0u, ShakeWaveform.FirstUnsampledFrame(0u, false, 0u, 9));
            // If frame 0 has already been sampled, start from 1. It is within the budget,
            // so the look-back limit does not apply.
            Assert.Equal(1u, ShakeWaveform.FirstUnsampledFrame(0u, true, 3u, 9));
            // With a budget of 1 it is always just the current frame (= the previous behaviour).
            Assert.Equal(1009u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1009u, 1));
        }
    }
}
