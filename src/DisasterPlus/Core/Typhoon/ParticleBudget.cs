namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// A pure function that does one thing: <b>solve vanilla's particle-count formula
    /// backwards from "how many particles per second do we want"</b>.
    /// **Engine-free** (used by both ④'s vortex and its rainstorm).
    ///
    /// The formula from §B-4 of the measured-effects document:
    ///
    /// <code>
    /// count = max(100, π·r²) × (timeDelta × magnitude × 0.01 × rateOverTime)
    /// </code>
    ///
    /// <c>timeDelta</c> cancels on both sides, so the <c>magnitude</c> returned
    /// <b>depends on neither the frame rate nor the game speed</b>.
    ///
    /// ★ Solve it again for every disc. The formula scales with area, so unless you drop
    ///   <c>magnitude</c> as much as you widen the disc, only the big discs come out dense.
    ///
    /// Bad input (zero or below, NaN) returns 0 — we do not want to produce "the sky fills
    /// up because the particle count is NaN".
    /// </summary>
    public static class ParticleBudget
    {
        /// <param name="discRadius">The disc radius (m) of one emission (one
        /// <c>RenderEffect</c> call).</param>
        /// <param name="rateOverTime">The effect's own
        /// <c>emission.rateOverTime.constant</c>.</param>
        /// <param name="particlesPerSecond">How many particles per second we want **in
        /// total**.</param>
        /// <param name="emitterCount">How many <c>RenderEffect</c> calls we fire per
        /// frame.</param>
        public static float MagnitudeFor(float discRadius, float rateOverTime,
                                         float particlesPerSecond, int emitterCount)
        {
            if (!(discRadius > 0f) || !(rateOverTime > 0f)) return 0f;
            if (!(particlesPerSecond > 0f) || emitterCount <= 0) return 0f;

            float area = 3.14159265f * discRadius * discRadius;
            if (area < 100f) area = 100f;      // the max(100, πr²) from §B-4

            float perEmitter = particlesPerSecond / emitterCount;
            float magnitude = perEmitter / (area * 0.01f * rateOverTime);

            if (float.IsNaN(magnitude) || magnitude <= 0f) return 0f;
            return magnitude;
        }
    }
}
