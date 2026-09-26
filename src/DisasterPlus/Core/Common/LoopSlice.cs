using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// A pure function that cuts a **seamless loop** out of a one-shot recording.
    /// Engine-free (no <c>UnityEngine</c>, no Cities API; works on both net35 and net8.0).
    ///
    /// ── Why it is needed (measured) ──────────────────────────────
    ///
    /// The bundled <c>erupting-volcano.wav</c> is, despite its name, **not a steady
    /// ambience**. Measure its RMS a second at a time and you get
    ///
    /// <code>
    /// 0.04 0.11 0.21 0.26 0.28 0.27 0.27 0.24 … 0.21 … 0.08 … 0.01 0.006 0.002 0.001
    /// └ onset ┘└──── the peak (3-13 s) ────┘└──── decaying into silence (36 s) ────┘
    /// </code>
    ///
    /// In other words, it is **a recording of one eruption**. Loop it as it stands and
    /// every 36 seconds it repeats "fall away to silence, then jump straight back to full
    /// volume". Removing the seam does not remove that pulsing — the only cure is to
    /// **loop the peak alone**.
    ///
    /// The "onset → peak → decay" arc the whole recording carries is not thrown away.
    /// **⑤ already has that arc of its own** (<c>VolcanoEruption.Envelope</c>), so the
    /// volume shape comes from there and this waveform supplies **texture only**. No
    /// giving one job to two places.
    ///
    /// ── How the seam is removed ─────────────────────────────────
    ///
    /// Take the loop body as <c>[start, start+length)</c> and mix into its **first
    /// <c>fade</c> frames** "the sound that would have carried on just past the end of the
    /// loop", <c>[start+length, start+length+fade)</c>:
    ///
    /// <code>
    /// out[i] = src[start+i]                                        (i >= fade)
    /// out[i] = src[start+i]·√t + src[start+length+i]·√(1−t)        (i &lt; fade, t = i/fade)
    /// </code>
    ///
    /// At the instant playback wraps from the end back to the start, the output begins at
    /// <c>src[start+length]</c> (i.e. the continuation of the end) and moves over to
    /// <c>src[start]</c> across <c>fade</c>. **The waveform never jumps, so there is no
    /// click.** The weights are <c>√</c> so that energy stays constant for uncorrelated
    /// noise-like material (do it linearly and the level dips at the seam).
    ///
    /// ★ <b>When it cannot cut a loop, it returns the original array as it stands.</b>
    ///   For someone who swapped in a short wav, handing back "a loop with a seam" is
    ///   always better than handing back silence.
    /// </summary>
    public static class LoopSlice
    {
        /// <summary>
        /// Cuts the loop. **It never throws.**
        /// </summary>
        /// <param name="samples">Interleaved samples.</param>
        /// <param name="channels">Channel count (1 or more).</param>
        /// <param name="startFrame">The frame number where the loop starts.</param>
        /// <param name="lengthFrames">The loop's length (frames).</param>
        /// <param name="fadeFrames">The crossfade length used at the seam (frames).</param>
        /// <returns>
        /// The interleaved array that was cut. If there is not enough material, or the
        /// arguments make no sense, <paramref name="samples"/> comes back as it stands
        /// (**it never returns null**).
        /// </returns>
        public static float[] Build(float[] samples, int channels,
                                    int startFrame, int lengthFrames, int fadeFrames)
        {
            if (samples == null) return new float[0];
            if (channels <= 0) return samples;
            if (startFrame < 0 || lengthFrames <= 0 || fadeFrames < 0) return samples;
            if (fadeFrames > lengthFrames) return samples;

            int totalFrames = samples.Length / channels;

            // The material mixed into the seam is taken from **past** the end of the loop.
            // If there is not that much, do not cut at all.
            long needed = (long)startFrame + lengthFrames + fadeFrames;
            if (needed > totalFrames) return samples;

            float[] output = new float[lengthFrames * channels];

            int from = startFrame * channels;
            Array.Copy(samples, from, output, 0, output.Length);

            if (fadeFrames == 0) return output;

            int tail = (startFrame + lengthFrames) * channels;
            for (int f = 0; f < fadeFrames; f++)
            {
                // t runs 0 → 1. At t=0 it is only "the continuation of the end", at t=1
                // only the head of the loop.
                double t = (f + 1) / (double)(fadeFrames + 1);
                float headWeight = (float)Math.Sqrt(t);
                float tailWeight = (float)Math.Sqrt(1.0 - t);

                int at = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    output[at + c] = output[at + c] * headWeight
                                     + samples[tail + at + c] * tailWeight;
                }
            }

            return output;
        }

        /// <summary>
        /// The version that takes seconds. If <paramref name="sampleRate"/> is 0 or less,
        /// it does not cut. **This is the entry point that actually gets used** (constants
        /// written in seconds read better).
        /// </summary>
        public static float[] Build(float[] samples, int channels, int sampleRate,
                                    float startSeconds, float lengthSeconds, float fadeSeconds)
        {
            if (samples == null) return new float[0];
            if (sampleRate <= 0) return samples;
            if (float.IsNaN(startSeconds) || float.IsNaN(lengthSeconds)
                || float.IsNaN(fadeSeconds))
            {
                return samples;
            }

            return Build(samples, channels,
                         (int)(startSeconds * sampleRate),
                         (int)(lengthSeconds * sampleRate),
                         (int)(fadeSeconds * sampleRate));
        }
    }
}
