using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Phase → volcanic-earthquake activity level <c>[0,1]</c>. **This one place holds the only
    /// mapping table.**
    ///
    /// ── why it was split out (2026-08-22) ──────────────────────────────────────────────────
    ///
    /// Two parties came to need this table:
    ///
    ///   1. <see cref="VolcanoTremorShake"/> — **main thread**, shakes the camera.
    ///      Its input is the <c>VolcanoSnapshot</c> (what sim published).
    ///   2. <see cref="VolcanoTremorTrace"/> — **sim thread**, records onto the seismograph
    ///      (the owner's request, "please also fix the volcanic earthquakes not being recorded on
    ///      the seismograph"). Its input is the raw sim-side state.
    ///
    /// If the two wrote their own <c>switch</c> statements, there would inevitably be
    /// **a phase fixed in only one of them** — giving the hardest kind of discrepancy to narrow
    /// down: the screen shakes but nothing appears in the record (or vice versa). So there is one
    /// mapping table.
    ///
    /// ── ★★ mind the direction of the cooling (a mix-up fixed on 2026-08-22) ───────────────
    ///
    /// In <c>VolcanoLava.CoolUnit</c>, <b>1 means "still hot" and 0 means "cooled out"</b>
    /// (its doc, and <c>LavaGlow.CoolFade</c>).
    /// But <c>VolcanicTremor.ActivityUnit</c>'s <c>coolUnit</c> has <b>1 meaning "cooled out"</b>
    /// — **the opposite direction**.
    ///
    /// The implementation was passing <c>VolcanoLava.CoolUnit</c> straight through, so the
    /// after-tremor came out **exactly backwards**: <b>0 while the lava was hot and 0.42 once it
    /// had cooled out</b>. Quietest right after the eruption, and starting to rumble once it had
    /// gone cold.
    /// It is corrected to <c>1 − coolUnit</c> here. **The conversion happens in this one place
    /// only.**
    /// </summary>
    public static class VolcanoTremorActivity
    {
        /// <summary>
        /// The current activity level <c>[0,1]</c>. Pass <paramref name="lavaCoolUnit"/> in
        /// <c>VolcanoLava.CoolUnit</c>'s direction (**1 = still hot**).
        /// </summary>
        public static float For(VolcanoPhase phase, float progressUnit,
                                float eruptionIntensityUnit, float lavaCoolUnit)
        {
            switch (phase)
            {
                // The clearing (destruction) and the uplift — the stages where magma is rising.
                // **It shakes before the eruption.**
                case VolcanoPhase.Clearing:
                case VolcanoPhase.Uplifting:
                    return VolcanicTremor.ActivityUnit(progressUnit, false, 0f, false, 0f);

                case VolcanoPhase.Erupting:
                    return VolcanicTremor.ActivityUnit(1f, true, eruptionIntensityUnit,
                                                       false, 0f);

                // From the end of the eruption until the lava has cooled out, the after-tremor
                // fades away.
                // ★ Line the directions up (class doc). 1 − CoolUnit is "how far it has cooled".
                case VolcanoPhase.Flowing:
                case VolcanoPhase.Cooling:
                    return VolcanicTremor.ActivityUnit(1f, false, 0f, true,
                                                       1f - Clamp01(lavaCoolUnit));

                default:
                    return 0f;
            }
        }

        /// <summary>How far the shaking carries (as a multiple of the mountain's radius). **Outside it is exactly 0.**</summary>
        public const float ReachRadiusFactor = 4.5f;

        /// <summary>
        /// The factor that converts the ground motion <c>[-1,1]</c> into **the same displacement
        /// unit as ②'s**.
        /// 0.7× ②'s vanilla theoretical maximum (<c>ShakeWaveform.MaxDisplacement</c> = 0.6) —
        /// a volcanic earthquake feels strong up close, but **it is not a main-shock fault
        /// earthquake**.
        ///
        /// ★★ <b>Both the camera (<see cref="VolcanoTremorShake"/>) and the seismogram
        /// (<see cref="VolcanoTremorTrace"/>) use this.</b>
        /// If only one of them used the raw <c>[-1,1]</c>, then — since the seismogram's vertical
        /// scale is shared across all three traces — you would get **a picture where the volcanic
        /// tremor alone is 1.7 times larger than vanilla's main shock**.
        /// </summary>
        public const float DisplacementGain = 0.7f * ShakeWaveform.MaxDisplacement;

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
