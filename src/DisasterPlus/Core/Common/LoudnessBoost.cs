using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Makes a waveform louder **without replacing the source material**. <b>This is Core,
    /// so it touches the engine not at all</b> (neither <c>UnityEngine</c> nor
    /// <c>Mathf</c> appears here).
    ///
    /// ── Why it is needed (2026-08-22, the owner's request: "the eruption is too quiet") ────
    ///
    /// > Also the eruption sound is too quiet. Please make it about twice as loud.
    ///
    /// **There are three places volume gets multiplied, and two of them are not ours.**
    /// ⑤'s sound goes through <c>AudioManager.EffectGroup</c> (IL findings doc §H-23), and
    /// the final volume is
    ///
    /// <code>
    /// m_targetVolume = info.m_volume * volume * m_cachedVolume
    ///                                           ↑ the player's sound-effects slider
    /// source.volume ← m_targetVolume * (1 - slot number in use / m_maxActiveCount)
    /// </code>
    ///
    /// <c>volume</c> is already pushed all the way up to <c>[0.35, 1.0]</c> by the eruption
    /// strength, and **whether <c>AudioSource.volume</c> accepts a value above 1 depends on
    /// Unity's implementation and cannot be measured from this mod** (it is a native call,
    /// so it does not show up in the IL). Do not bet a doubling on something you cannot
    /// verify.
    ///
    /// ── So make the waveform itself louder. **Except the material has no headroom left** ───
    ///
    /// Measuring the bundled <c>erupting-volcano.wav</c>:
    ///
    /// | Quantity | Value |
    /// |---|---|
    /// | Peak | 27412 / 32767 = 0.837 (**-1.55 dBFS**) |
    /// | RMS | 5330 / 32767 = 0.163 (-15.8 dBFS) |
    ///
    /// **The peak is already within a factor of 1.2 of the ceiling**, so simply doubling it
    /// would clip (0.837 × 2 = 1.674 runs off the end of <c>[-1,1]</c>).
    /// The RMS, on the other hand, is a full 14 dB below the peak — this is material whose
    /// **average is small while only its peaks are large**. So "hold the peaks back a
    /// little and double everything else straightforwardly" is the right way to make this
    /// particular material louder.
    ///
    /// ── What it does ────────────────────────────────
    ///
    /// Up to <see cref="Threshold"/> it is <b>exactly <paramref name="gain"/>×</b>, and
    /// above that it is squashed by an exponential knee asymptotic to 1 (see
    /// <see cref="Apply"/>).
    ///
    /// <code>
    /// |y| = gain * |x|
    /// |y| &lt;= t : unchanged          ← material with RMS 0.163 goes almost entirely through here
    /// |y| &gt;  t : t + (1-t)(1 - e^-((|y|-t)/(1-t)))
    /// </code>
    ///
    /// Measured (gain = 2, t = 0.7): 0.163 (RMS) → 0.326, exactly double, and the peak
    /// 0.837 → 0.988, which **does not clip**. In other words <b>the perceived loudness is
    /// close to doubled and only the very largest peaks are squashed</b>.
    ///
    /// ★ <b>The source file is not altered by a single byte.</b> This only multiplies the
    ///   <c>float[]</c> after loading, so the wav the owner supplied stays as it is (and
    ///   swapping it out cannot leave the previous processing applied twice).
    /// </summary>
    public static class LoudnessBoost
    {
        /// <summary>
        /// Up to here it is **exactly gain×**. Above it, the knee takes over.
        /// It must satisfy 0 &lt; t &lt; 1 (set it to 1 and the knee disappears and it clips).
        /// </summary>
        public const float Threshold = 0.7f;

        /// <summary>
        /// Multiplies the waveform (interleaved, in <c>[-1,1]</c>) by
        /// <paramref name="gain"/>. **The array is rewritten in place** (36 seconds × 2
        /// channels is 3.2 M elements, and making a second one wastes 12 MB).
        ///
        /// Does nothing if <paramref name="samples"/> is null.
        /// **Does nothing** if <paramref name="gain"/> is 1 or less, NaN or ∞ (this type
        /// has no "make it quieter" use; if you want that, use a different type).
        ///
        /// Returns the peak after processing (for diagnostics). 0 for an empty array.
        /// </summary>
        public static float Apply(float[] samples, float gain)
        {
            if (samples == null || samples.Length == 0) return 0f;
            if (IsBad(gain) || gain <= 1f) return PeakOf(samples);

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = Shape(samples[i], gain);
                samples[i] = v;

                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }
            return peak;
        }

        /// <summary>
        /// One sample's worth. Linear up to <see cref="Threshold"/>, asymptotic to 1 above
        /// it. **The sign is always preserved** (lose it and the waveform becomes something
        /// else entirely).
        /// Abnormal input gives 0 (silence) — this is the only place we fill with 0, and it
        /// means "that one sample was corrupt", not "it could not be read".
        /// </summary>
        public static float Shape(float sample, float gain)
        {
            if (IsBad(sample)) return 0f;
            if (IsBad(gain) || gain <= 1f) return Clamp(sample);

            float sign = sample < 0f ? -1f : 1f;
            float magnitude = sample < 0f ? -sample : sample;

            float y = magnitude * gain;
            if (y <= Threshold) return sign * y;

            // Above t, the remaining (1 - t) of range is used to approach 1 asymptotically.
            const float Knee = 1f - Threshold;
            float over = (y - Threshold) / Knee;
            float shaped = Threshold + Knee * (1f - (float)Math.Exp(-over));
            return sign * Clamp(shaped);
        }

        /// <summary>The current peak (the largest absolute value).</summary>
        public static float PeakOf(float[] samples)
        {
            if (samples == null) return 0f;

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i];
                if (IsBad(v)) continue;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }
            return peak;
        }

        private static float Clamp(float v)
        {
            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
