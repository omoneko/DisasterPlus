namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// Reinterprets the raw value of vanilla's intensity slider (0-255) as ⑤'s "multiplier
    /// on the size of the mountain".
    ///
    /// ── Why a multiplier ───────────────────────────────────
    ///
    /// A vanilla disaster happens via "pick a tile → intensity slider → click the map".
    /// The owner asked for ⑤ to have the same flow. But ⑤ has no quantity corresponding to
    /// "intensity" — what ⑤ has is <b>form, radius and final height</b>, and those live in
    /// the settings screen. So we read the slider as <b>a multiplier on the configured
    /// size</b>.
    ///
    ///   - <see cref="AnchorRaw"/> (55, displayed as 5.5; the game's own default) =
    ///     <b>the recommended size</b>
    ///   - Double the raw value and the mountain doubles (the radius and the final height
    ///     both grow by the same factor)
    ///
    /// ── ★★ The baseline is **the recommended value per form** (2026-08-22) ──────
    ///
    /// The baseline used to be the "radius" and "final height" sliders on the settings
    /// screen. The owner pointed out:
    ///
    /// > You've made the eruption radius and height adjustable on the Options screen, but
    /// > that makes the scale adjustment meaningless — please fix these at the recommended
    /// > settings.
    ///
    /// Quite right: **we were making the same quantity be decided by two knobs.** The
    /// baseline is now <c>VolcanoShape.DefaultRadiusOf</c> / <c>DefaultHeightOf</c> (the
    /// recommended values per form), and the size is decided by <b>a single slider and
    /// nothing else</b>.
    /// How many metres it actually comes out at is clamped at the end by the per-form band
    /// (<see cref="VolcanoShape.RadiusFor"/> / <see cref="VolcanoShape.HeightFor"/>), and
    /// **the post-clamp real size is stated by the affected-area line on the volcano tab and
    /// by the diagnostic dump** (the confirmation window was removed on 2026-08-21, so there
    /// is no longer a step that shows the real size beforehand).
    ///
    /// ── The band of multipliers ────────────────────────────────────
    ///
    /// Even at a raw value of 0 we never go below <see cref="MinScale"/>. A multiplier of 0
    /// means "build no mountain", not "a small mountain", and would create a control that
    /// does nothing when pressed. The top is cut at <see cref="MaxScale"/> (the per-form
    /// band will cut it anyway, but **we do not multiply straight through a NaN or a value
    /// orders of magnitude out**).
    /// </summary>
    public static class VolcanoSizeScale
    {
        /// <summary>The raw value corresponding to a multiplier of 1.0. 55, the same as the
        /// game's own default disaster intensity.</summary>
        public const int AnchorRaw = 55;

        /// <summary>
        /// **The raw value at the bottom** of vanilla's slider (displayed as 1.0).
        /// Vanilla's disaster panel cannot select anything below this.
        /// </summary>
        public const int MinRaw = 10;

        /// <summary>
        /// **The raw value at the top** once unlocked (displayed as 25.5).
        /// <c>IntensityUnlock</c> raises it from 100 to 255 (the intensity is a byte).
        /// </summary>
        public const int MaxRaw = 255;

        /// <summary>
        /// The multiplier at the smallest setting. **It is exactly the bottom of the
        /// slider** (<see cref="MinRaw"/> / <see cref="AnchorRaw"/> ≒ 0.18).
        /// We never make it 0 (= nothing happens).
        /// </summary>
        public const float MinScale = MinRaw / (float)AnchorRaw;

        /// <summary>
        /// The multiplier at the largest setting. **It is exactly the top of the slider**
        /// (<see cref="MaxRaw"/> / <see cref="AnchorRaw"/> ≒ 4.64).
        ///
        /// ★★ <b>Back when this was cut at 4, everything above 22.0 on the slider was
        ///   dead</b> (fixed on 2026-08-22). The per-form band (<c>VolcanoShape</c>) cuts it
        ///   anyway, so there is no point cutting it earlier here — all that does is create
        ///   a band where moving the slider changes nothing.
        /// </summary>
        public const float MaxScale = MaxRaw / (float)AnchorRaw;

        /// <summary>
        /// Raw value → multiplier. Out-of-range and negative values are clamped into the
        /// band (another mod could move the slider's upper limit, so we do not simply
        /// discard them).
        /// </summary>
        public static float ScaleFor(int raw)
        {
            if (raw <= 0) return MinScale;

            float scale = raw / (float)AnchorRaw;
            if (scale < MinScale) return MinScale;
            if (scale > MaxScale) return MaxScale;
            return scale;
        }

        /// <summary>
        /// Multiplier → raw value (the inverse map, for filling the slider's initial value).
        /// A round trip through <see cref="ScaleFor"/> leaves the value unchanged inside the
        /// band.
        /// </summary>
        public static int RawFor(float scale)
        {
            if (float.IsNaN(scale)) return AnchorRaw;
            if (scale < MinScale) scale = MinScale;
            if (scale > MaxScale) scale = MaxScale;

            int raw = (int)(scale * AnchorRaw + 0.5f);
            return raw < 1 ? 1 : raw;
        }

        /// <summary>
        /// Multiplies a value in metres by the multiplier. **NaN and negative inputs are not
        /// passed straight through** — the .cgs can be hand-edited, and if the product came
        /// out NaN the per-form clamp would drop it to the default and the cause would be
        /// impossible to find.
        /// </summary>
        public static float Apply(float metres, float scale)
        {
            if (float.IsNaN(metres) || float.IsNaN(scale)) return metres;
            if (metres <= 0f) return metres;

            if (scale < MinScale) scale = MinScale;
            if (scale > MaxScale) scale = MaxScale;
            return metres * scale;
        }
    }
}
