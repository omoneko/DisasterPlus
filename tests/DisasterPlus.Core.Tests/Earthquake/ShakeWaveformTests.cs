using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// バニラの揺れの式（IL 事実文書 §A-7）の写しを固定する。
    ///
    /// **いちばん重要なのは <see cref="IntensityFactorIsZeroAtTheVanillaDefault"/> の 1 件。**
    /// 強度 55 で追加分が厳密に 0 になることが、この機能を既定 ON にしてよい唯一の根拠で、
    /// そこが崩れると「バニラと同じ挙動」を名乗ったまま全プレイヤーの画面が変わる。
    /// </summary>
    public class ShakeWaveformTests
    {
        [Fact]
        public void EnvelopeRateGivesA256FramePeriod()
        {
            // IL: 0.5 - 0.5*cos(t * 0.02454369)。2π / 0.02454369 = 256.0。
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
            // 2 本の正弦の合成なので符号が何度も変わる。定数化していないことを固定する。
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
            // 強度 55 で追加分がちょうど 0 = バニラと完全に同一の挙動。
            // 既定 ON にしてよい根拠そのものなので、必ず固定する。
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
            // IL: e <= 0 なら return、e >= m_activeDuration なら return。
            Assert.False(ShakeWaveform.IsShaking(0, 1000u));
            Assert.False(ShakeWaveform.IsShaking(-5, 1000u));
            Assert.True(ShakeWaveform.IsShaking(1, 1000u));
            Assert.True(ShakeWaveform.IsShaking(999, 1000u));
            Assert.False(ShakeWaveform.IsShaking(1000, 1000u));
            // m_activeDuration が読めていない（0）ときは常に false。
            Assert.False(ShakeWaveform.IsShaking(1, 0u));
        }
    }
}
