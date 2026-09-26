namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// **Vanilla's own shaking formula** (§A-7 of the IL facts document,
    /// <c>EarthquakeAI.RenderInstance</c>). Nothing invented: this is a straight copy of what
    /// the game computes every frame.
    ///
    ///   amp = 0.3 / (1 + distance * 0.001)
    ///   amp *= 0.5 - 0.5 * cos(t * 0.02454369)      // 2π/0.02454369 = a 256-frame period
    ///   s   = (sin(t * 0.63) + sin(t * 0.17)) * amp
    ///
    /// There are only two components, 0.63 rad/frame (a period of ≒10 frames) and
    /// 0.17 rad/frame (≒37 frames), and **there is no long-period component** (the
    /// 256-frame one is the amplitude envelope, not ground motion). Long-period ground
    /// motion is added separately by Task 10 — that is the second layer, this is the first.
    ///
    /// **m_intensity does not appear in the amplitude at all.** That is the gap this mod
    /// corrects, and <see cref="IntensityFactor"/> returns the correction factor.
    ///
    /// ── What `distance` means changes with the caller (design document §3.5) ─────
    ///
    ///   - The camera shake correction (<c>CameraShakeBooster</c>, Task 6) uses
    ///     **the distance from the camera**. The same thing vanilla measures with
    ///     <c>camera.transform.InverseTransformPoint</c> (§A-7 IL_003F), and we match it
    ///     right down to scaling <c>z</c> by 0.25 before taking the length. We are
    ///     **adding** to vanilla's wave, so unless we evaluate at the same point the shape
    ///     falls apart.
    ///   - The waveform graph (Task 8) uses **the distance from the hypocentre to the
    ///     observation point**.
    ///
    /// **These are two evaluations of the same formula, not an approximation.** It is
    /// vanilla's formula itself, merely evaluated at a different point. Write that
    /// distinction into the UI as well (design document §3.5). Confuse the two and you get a
    /// lie: a waveform graph that claims to be "ground shaking" while actually drawing the
    /// camera's movement.
    ///
    /// Core cannot use <c>UnityEngine.Mathf</c>, so we compute in <c>System.Math</c>
    /// (double) and narrow to float. The lowest bits can differ from vanilla's
    /// <c>Mathf.Sin</c> (float), which we accept because this is a quantity for display and
    /// presentation, not a prediction.
    /// </summary>
    public static class ShakeWaveform
    {
        public const float BaseAmplitude = 0.3f;
        public const float DistanceFalloff = 0.001f;
        public const float EnvelopeRate = 0.02454369f;
        public const int EnvelopePeriodFrames = 256;
        public const float FastRate = 0.63f;
        public const float SlowRate = 0.17f;

        /// <summary>IL: <c>e = m_referenceFrameIndex - m_activationFrame + 128</c>.</summary>
        public const int FrameOffset = 128;

        /// <summary>
        /// The cap on the intensity factor we may add. At intensity 255 the raw value would
        /// be (255/55 - 1) = 3.64, giving 4.64× vanilla in total. A screen you cannot use is
        /// not drama, it is a defect.
        /// </summary>
        public const float MaxIntensityFactor = 2f;

        /// <summary>
        /// Vanilla's display window. <paramref name="activeDuration"/> is the prefab value,
        /// and **when it could not be read (0) we return false** — shake without knowing the
        /// window and the shaking carries on after the earthquake has ended.
        ///
        /// This is a straight inversion of the IL's
        /// <c>if (e &lt;= 0) return; if (e &gt;= m_activeDuration) return;</c>. Do not
        /// rewrite the comparison operators to be "more readable".
        /// </summary>
        public static bool IsShaking(long elapsedPlusOffset, uint activeDuration)
        {
            if (activeDuration == 0u) return false;
            return elapsedPlusOffset > 0 && elapsedPlusOffset < activeDuration;
        }

        /// <summary>
        /// The theoretical upper bound on the displacement's absolute value:
        /// <see cref="BaseAmplitude"/> times <c>|sin + sin| ≦ 2</c>. It is 0.6, reached at
        /// distance 0 at the peak of the envelope.
        ///
        /// This is the **full-scale value** when showing the waveform's peak amplitude as a
        /// bar. Borrow the local factor s's scale (0-1) instead and you draw a value that
        /// only ever reaches 0.6 on a 0-1 scale, so it does not line up visually with the
        /// s bar next to it.
        /// </summary>
        public const float MaxDisplacement = 2f * BaseAmplitude;

        /// <summary>
        /// The amplitude **before** the envelope is applied
        /// (IL_0069: <c>amp = 0.3f / (1f + v.magnitude * 0.001f)</c>).
        ///
        /// **This is vanilla's "shaking" itself, and there is no cut-off by radius** (§A-7).
        /// The whole-quake disc's <c>R = 2000 + 20i</c> is the range of the collapse and
        /// ignition tests, not the range of the shaking. Even 10 km away it keeps shaking at
        /// 9% of the epicentre's amplitude.
        ///
        /// Vanilla measures this distance **from the camera**. When the caller passes the
        /// distance from the epicentre, that is another evaluation of the same formula, not
        /// an approximation (design document §3.5).
        /// Always write that distinction into the UI.
        /// </summary>
        public static float PeakAmplitudeAt(float distance)
        {
            if (float.IsNaN(distance)) return 0f;
            if (distance < 0f) distance = 0f;
            return BaseAmplitude / (1f + distance * DistanceFalloff);
        }

        /// <summary>
        /// Maps a displacement (or its maximum) onto a 0-1 scale, with
        /// <see cref="MaxDisplacement"/> as full scale. Use it only as the input to a bar
        /// display, and **print the pre-normalisation value as the number itself**.
        /// </summary>
        public static float NormalisedDisplacement(float value)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < 0f) value = -value;
            float n = value / MaxDisplacement;
            return n > 1f ? 1f : n;
        }

        /// <summary>
        /// Returns **the first frame to fill in this time**, given the last sampled frame
        /// and the current one.
        ///
        /// ── Why one sample per tick is not enough ──────────────────────
        ///
        /// <c>SimulationManager.m_currentFrameIndex</c> advances by
        /// <c>FinalSimulationSpeed</c> (1/3/9 at game speeds 1/2/3) per sim tick. The
        /// shaking's main component, meanwhile, is 0.63 rad/frame (a period of ≒10 frames),
        /// so **taking a single point every 9 frames turns it into a spurious wave with a
        /// period of ≒92 frames** (aliasing). Worse, what that looks like is precisely
        /// "long-period ground motion", and §A-7 establishes that **vanilla has no
        /// long-period component**. In other words the first layer's graph would be drawing
        /// a phenomenon only the second layer is allowed to add.
        ///
        /// <see cref="DisplacementAt"/> is a closed form in e, so evaluating it at each
        /// frame within the tick is exactly as much a "measurement" as evaluating it once.
        /// Fill in the skipped frames and we satisfy the sampling theorem (1-frame spacing
        /// against a 10-frame period).
        ///
        /// 9 would do for <paramref name="maxSubSamples"/> at game speed 3, but the frame
        /// can jump a long way across a save or a pause, so we use it as a cap (anything
        /// beyond the jump is discarded rather than filled in — accumulating it would only
        /// put it outside the window anyway).
        /// </summary>
        public static uint FirstUnsampledFrame(uint lastSampledFrame, bool hasLastSample,
                                               uint currentFrame, int maxSubSamples)
        {
            if (maxSubSamples < 1) maxSubSamples = 1;

            uint oldest = currentFrame >= (uint)(maxSubSamples - 1)
                ? currentFrame - (uint)(maxSubSamples - 1)
                : 0u;

            if (!hasLastSample || lastSampledFrame >= currentFrame) return currentFrame;

            uint next = lastSampledFrame + 1u;
            return next < oldest ? oldest : next;
        }

        /// <summary>The amplitude with the envelope included. <paramref name="t"/> is the
        /// frame (fractional values allowed).</summary>
        public static float AmplitudeAt(float distance, float t)
        {
            if (float.IsNaN(distance) || float.IsNaN(t)) return 0f;

            float amp = PeakAmplitudeAt(distance);
            amp *= 0.5f - 0.5f * (float)System.Math.Cos(t * EnvelopeRate);
            return amp < 0f ? 0f : amp;
        }

        /// <summary>The signed displacement. Vanilla's <c>s</c> itself.</summary>
        public static float DisplacementAt(float distance, float t)
        {
            float amp = AmplitudeAt(distance, t);
            if (amp <= 0f) return 0f;
            return (float)(System.Math.Sin(t * FastRate) + System.Math.Sin(t * SlowRate)) * amp;
        }

        /// <summary>
        /// The **additional** factor applied to vanilla's shaking (it never suppresses).
        ///
        /// At intensity 55 (<c>DisasterManager.CreateDisaster</c>'s default) it is exactly
        /// 0, and the total then matches vanilla perfectly. **That 0 is the sole
        /// justification for having this feature on by default**, so whenever the formula is
        /// rewritten "equivalently", always confirm that <c>intensity == 55</c> still yields
        /// exactly 0f (55f / 55f is exactly 1.0f in IEEE754, and subtracting 1f from it
        /// gives exactly 0f).
        /// </summary>
        public static float IntensityFactor(byte intensity)
        {
            float f = (float)intensity / SeismicIntensity.VanillaDefaultIntensity - 1f;
            if (f > MaxIntensityFactor) return MaxIntensityFactor;
            if (f < -1f) return -1f;
            return f;
        }
    }
}
