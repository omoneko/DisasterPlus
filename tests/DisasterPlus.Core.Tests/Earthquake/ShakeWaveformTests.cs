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

        // ── 揺れの振幅そのもの（全体レビュー C3）────────────────────────

        [Fact]
        public void PeakAmplitudeIsTheVanillaAmpBeforeTheEnvelope()
        {
            // IL_0069: amp = 0.3f / (1f + dist * 0.001f)。包絡線の頂点で一致する。
            float t = ShakeWaveform.EnvelopePeriodFrames / 2f;
            foreach (float d in new[] { 0f, 250f, 1000f, 9000f })
            {
                Assert.Equal(ShakeWaveform.PeakAmplitudeAt(d), ShakeWaveform.AmplitudeAt(d, t), 4);
            }
        }

        [Fact]
        public void ShakingHasNoRadiusCutOff()
        {
            // **これが C3 の核心。** 全体円盤の R（強度 55 で 3100 m）を超えても
            // 揺れは 0 にならない。10 km で震央の 9% 前後。
            float epicentre = ShakeWaveform.PeakAmplitudeAt(0f);
            float tenKm = ShakeWaveform.PeakAmplitudeAt(10000f);
            Assert.True(tenKm > 0f, "vanilla's shaking never cuts off with distance");
            Assert.Equal(1f / 11f, tenKm / epicentre, 4);
        }

        // ── バーの満目盛り（全体レビュー I4）───────────────────────────

        [Fact]
        public void MaxDisplacementIsTwiceTheBaseAmplitude()
        {
            Assert.Equal(0.6f, ShakeWaveform.MaxDisplacement, 4);

            // 実際の変位がこれを超えないこと（超えると正規化が 1 で頭打ちになる）。
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
            // 符号は落とす（バーは大きさだけを見る）。
            Assert.Equal(0.5f, ShakeWaveform.NormalisedDisplacement(-0.3f), 4);
            // 上限で頭打ち。
            Assert.Equal(1f, ShakeWaveform.NormalisedDisplacement(99f), 4);
        }

        // ── サンプリング間隔（全体レビュー I5）───────────────────────────

        [Fact]
        public void FirstUnsampledFrameFillsTheGapLeftByTheSimulationSpeed()
        {
            // 速度 3 では m_currentFrameIndex が 9 ずつ飛ぶ。飛んだ 9 フレームを
            // 全部埋めないと、周期 10 フレームの主成分が偽の長周期波に折り返す。
            Assert.Equal(1001u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1009u, 9));
            // 速度 1（1 フレームずつ）なら、その 1 フレームだけ。
            Assert.Equal(1001u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1001u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameNeverExceedsTheSubSampleBudget()
        {
            // ポーズやセーブ跨ぎで大きく飛んだときは、窓の外まで遡らない。
            Assert.Equal(1992u, ShakeWaveform.FirstUnsampledFrame(10u, true, 2000u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameTakesOnlyTheCurrentFrameWithoutHistory()
        {
            // フレーム 0 は実在しうるので、「まだ 1 件も取っていない」を 0 で表さない。
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(0u, false, 500u, 9));
            // 同じフレームで 2 回呼ばれても遡らない（重複サンプルを作らない）。
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(500u, true, 500u, 9));
            Assert.Equal(500u, ShakeWaveform.FirstUnsampledFrame(900u, true, 500u, 9));
        }

        [Fact]
        public void FirstUnsampledFrameIsSafeNearFrameZero()
        {
            Assert.Equal(0u, ShakeWaveform.FirstUnsampledFrame(0u, false, 0u, 9));
            // frame 0 を既に取っているなら 1 から。予算より手前なので遡り制限は効かない。
            Assert.Equal(1u, ShakeWaveform.FirstUnsampledFrame(0u, true, 3u, 9));
            // 予算 1 なら常に現在フレームだけ（＝以前の挙動）。
            Assert.Equal(1009u, ShakeWaveform.FirstUnsampledFrame(1000u, true, 1009u, 1));
        }
    }
}
