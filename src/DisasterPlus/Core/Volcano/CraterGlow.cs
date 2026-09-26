using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **The magma pool in the crater, and the light that reaches the plume from it.**
    /// <b>This is Core, so it never touches the engine</b> (pure functions only, holding no
    /// state at all).
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// > A magma pool in the crater (glowing like the lava) and light radiating into the
    /// > plume
    ///
    /// ── ★★ Do not make it flicker ───────────────────────────────
    ///
    /// <see cref="LavaGlow"/> already made this mistake once — scrolling the UVs made
    /// **the lava flow look like it was blinking** (the owner pointed it out).
    /// So here too the brightness is built from <b>smooth functions of time</b> alone, and
    /// the fastest component is <see cref="BreathHz"/> = 0.19 Hz (a period of just over 5
    /// seconds). **Do not add anything faster than this.**
    ///
    /// A magma pool stirs by convection; it does not blink. We add two sine waves with
    /// different periods so it does not sound repetitive (an incommensurable ratio).
    ///
    /// ── How the light carries ────────────────────────────────────
    ///
    /// The higher above the crater, the darker. Not an inverse square — a real plume is
    /// <b>a scattering medium</b>, so the light does not fall away abruptly with distance
    /// from the source, and at the same time <b>the plume's own thickness hides what is
    /// behind it</b>. With both in play the result comes out close to an exponential.
    /// <see cref="ReachFraction"/> is "what fraction of the column's height it reaches", and
    /// at that point it is <b>exactly 0</b> — let it glow faintly all the way up and the
    /// whole plume looks luminous, which ruins the night view.
    /// </summary>
    public static class CraterGlow
    {
        /// <summary>The floor on the magma pool's radius (as a ratio of the crater's
        /// radius).</summary>
        public const float PoolRadiusFloorRatio = 0.45f;

        /// <summary>The radius added by the eruption's strength (as a ratio of the crater's
        /// radius).</summary>
        public const float PoolRadiusGainRatio = 0.35f;

        /// <summary>The fastest component (Hz). **Do not add anything faster than
        /// this.**</summary>
        public const float BreathHz = 0.19f;

        /// <summary>The second swell (Hz). A value incommensurable with
        /// <see cref="BreathHz"/>.</summary>
        public const float Breath2Hz = 0.071f;

        /// <summary>The depth of the swell (0 for constant, 1 to fall all the way to
        /// 0).</summary>
        public const float BreathDepth = 0.22f;

        /// <summary>The brightness at eruption strength 0. **Never 0** (the crater is red
        /// before the eruption too).</summary>
        public const float MinBrightness = 0.35f;

        /// <summary>The brightness at eruption strength 1.</summary>
        public const float MaxBrightness = 1.0f;

        /// <summary>How high the light reaches (as a ratio of the column's height). At that
        /// point it is <b>exactly 0</b>.</summary>
        public const float ReachFraction = 0.34f;

        /// <summary>The light's strength directly above the crater (as a ratio of the magma
        /// pool's brightness).</summary>
        public const float LightAtVentRatio = 0.85f;

        /// <summary>How sharply the light falls away. The larger it is, the faster it goes
        /// dark.</summary>
        public const float LightDecay = 2.6f;

        /// <summary>
        /// The magma pool's radius (m). <paramref name="craterRadiusMetres"/> is the
        /// crater's radius. **Never larger than the crater** — if it looks like it is
        /// spilling over the rim, that is the lava flow's job.
        /// </summary>
        public static float PoolRadiusMetres(float craterRadiusMetres, float unit)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return 0f;

            float u = Clamp01(unit);
            float ratio = PoolRadiusFloorRatio + PoolRadiusGainRatio * u;
            if (ratio > 1f) ratio = 1f;
            return craterRadiusMetres * ratio;
        }

        /// <summary>
        /// The magma pool's brightness <c>[0,1]</c>. <paramref name="seconds"/> is ⑤'s
        /// effect clock. **Smooth functions of time only** (see the class doc).
        /// </summary>
        public static float PoolBrightness(float unit, float seconds)
        {
            float u = Clamp01(unit);
            float baseline = MinBrightness + (MaxBrightness - MinBrightness) * u;

            if (IsBad(seconds)) seconds = 0f;

            // Average the two swells. **With only one, the period is plainly visible.**
            double a = Math.Sin(6.2831853 * BreathHz * seconds);
            double b = Math.Sin(6.2831853 * Breath2Hz * seconds + 1.3);
            float wave = (float)((a * 0.6 + b * 0.4) * 0.5 + 0.5);   // [0,1]

            float envelope = 1f - BreathDepth + BreathDepth * wave;
            return Clamp01(baseline * envelope);
        }

        /// <summary>
        /// How much the magma's light brightens the plume <paramref name="heightMetres"/>
        /// above the crater, in <c>[0,1]</c>.
        ///
        /// <paramref name="plumeHeightMetres"/> is the plume column's full height, and above
        /// <c>ReachFraction</c> of it the result is <b>exactly 0</b>.
        /// </summary>
        public static float LightAt(float heightMetres, float plumeHeightMetres,
                                    float poolBrightness)
        {
            if (IsBad(heightMetres) || heightMetres < 0f) return 0f;
            if (IsBad(plumeHeightMetres) || plumeHeightMetres <= 0f) return 0f;

            float reach = plumeHeightMetres * ReachFraction;
            if (reach <= 0f || heightMetres >= reach) return 0f;

            float t = heightMetres / reach;

            // Fall off exponentially, then apply a window so it hits exactly 0 at the reach
            // (without the window it cuts off just short of reach and the boundary shows up
            // as a line).
            float decay = (float)Math.Exp(-LightDecay * t);
            float window = 1f - t * t;

            float k = Clamp01(poolBrightness) * LightAtVentRatio * decay * window;
            return Clamp01(k);
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
