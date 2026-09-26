namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The water-level maths for river flooding. Decides how far to lift
    /// <c>WaterSource.m_target</c>.
    ///
    /// ── The units come from the game. The rise is ④'s invention ─────────────────────
    ///
    /// <c>WaterSource.m_target</c> is an **absolute water level, a UInt16 in units of
    /// 1/64 m** (IL findings doc §D-4; sea level 40 m = 2560). <see cref="UnitsPerMetre"/>
    /// and <see cref="MaxTargetUnits"/> are facts about the game, not numbers ④ chose.
    /// **Mistake this for metres and your intended 2 m rise becomes 128 m.**
    ///
    /// The formulas in <see cref="RiseMetresOf"/> and <see cref="RiseAt"/>, on the other
    /// hand, are **entirely ④'s invention**. Vanilla has no flood disaster at all (§D-1;
    /// <c>GenericFloodAI</c> is an empty class with zero fields and zero methods), so there
    /// is no existing quantity for "how far a river rises in a typhoon" to refer to.
    ///
    /// ── Saturate (wrap-around is the worst way for this to break) ───────────────────
    ///
    /// <see cref="RaisedTarget"/> computes in <c>int</c>, clamps with saturation, and
    /// **only then casts to <c>(ushort)</c>**. The moment it goes past 65535, a
    /// <c>m_target</c> that wraps round to near 0 flips the self-regulating spring and
    /// **drains the river dry** (the suction-side loop in §D-4).
    ///
    /// **It never lowers.** A negative rise or a NaN returns the original value untouched.
    /// Lower it and the river runs dry.
    /// </summary>
    public static class FloodTarget
    {
        /// <summary>
        /// How many <c>m_target</c> units make a metre. **A fact about the game** (§D-4).
        /// The same scale as <c>TerrainManager.RawHeights</c>.
        /// </summary>
        public const int UnitsPerMetre = 64;

        /// <summary>The ceiling on <c>m_target</c> (a UInt16). **A fact about the game.**</summary>
        public const ushort MaxTargetUnits = 65535;

        /// <summary>
        /// How far the strongest typhoon lifts the water level at most (m). **A number ④ chose.**
        /// 6 m is about where "the river in the valley floor clearly bursts its banks, but
        /// it does not reach the neighbourhoods up the hill", and in any case cells with
        /// <c>natural &amp;&amp; terrain >= m_target</c> are skipped (§D-4), so water never
        /// gets onto the hilltops in the first place.
        /// </summary>
        public const float MaxRiseMetres = 6f;

        /// <summary>
        /// At or below this much rain, not one millimetre of rise. **A number ④ chose.**
        /// A river bursting its banks because it was merely brushed by the typhoon's outer
        /// edge would be absurd.
        /// </summary>
        public const float MinRainForRise = 0.5f;

        /// <summary>The top of the strength slider (0-10).</summary>
        private const float MaxStrength = 10f;

        /// <summary>The maximum intensity (a byte).</summary>
        private const float MaxIntensity = 255f;

        /// <summary>
        /// The rise (m) at the typhoon's centre, from the intensity, the rainfall and the
        /// strength setting. **This is a formula ④ invented** (see the class doc).
        ///
        /// If <paramref name="strength"/> is 0 or less the result is exactly 0 (the slider
        /// switches it off completely). Likewise 0 if <paramref name="rain"/> is at or
        /// below <see cref="MinRainForRise"/>.
        /// </summary>
        public static float RiseMetresOf(byte intensity, float rain, int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            // NaN falls out on the !(rain > MinRainForRise) side.
            if (!(rain > MinRainForRise)) return 0f;
            if (rain > 1f) rain = 1f;

            float rainFactor = (rain - MinRainForRise) / (1f - MinRainForRise);
            float rise = (intensity / MaxIntensity) * rainFactor
                         * (strength / MaxStrength) * MaxRiseMetres;

            if (float.IsNaN(rise) || rise <= 0f) return 0f;
            return rise > MaxRiseMetres ? MaxRiseMetres : rise;
        }

        /// <summary>
        /// The rise (m) for a water source <paramref name="distanceToCentre"/> m from the
        /// typhoon's centre. <paramref name="peakRise"/> at the centre, falling linearly to
        /// exactly 0 at the edge of the gale radius.
        ///
        /// If <paramref name="galeRadius"/> is 0 (i.e. the prefab radius could not be read)
        /// it **returns 0**. Do not flood rivers off a guessed radius (design doc §6).
        /// </summary>
        public static float RiseAt(float distanceToCentre, float galeRadius, float peakRise)
        {
            if (!(galeRadius > 0f)) return 0f;
            if (!(peakRise > 0f)) return 0f;
            if (!(distanceToCentre >= 0f) || distanceToCentre >= galeRadius) return 0f;

            float rise = peakRise * (1f - distanceToCentre / galeRadius);
            return rise > 0f ? rise : 0f;
        }

        /// <summary>
        /// The lifted <c>m_target</c>. **It saturates; it does not wrap** (see the class doc).
        /// A rise of 0 or less, or NaN, returns the original value untouched (never lower).
        /// </summary>
        public static ushort RaisedTarget(ushort original, float riseMetres)
        {
            if (!(riseMetres > 0f)) return original;

            // ★ Compute in int, clamp with saturation, and only then cast to (ushort).
            //   Cast first and the moment it goes past 65535 it wraps round to near 0,
            //   flipping the self-regulating spring and draining the river dry.
            int units = (int)(riseMetres * UnitsPerMetre + 0.5f);
            if (units <= 0) return original;

            long raised = (long)original + units;
            return raised >= MaxTargetUnits ? MaxTargetUnits : (ushort)raised;
        }
    }
}
