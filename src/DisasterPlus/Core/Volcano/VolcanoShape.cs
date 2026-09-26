namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// A volcano's form. **The values are a public contract saved into the settings file
    /// (.cgs), so do not renumber them.**
    /// Swap the numbers around and settings already saved silently turn into a different form.
    /// </summary>
    public enum VolcanoForm
    {
        /// <summary>A shield volcano. Broad at the foot and flat at the summit (default R=2000
        /// m / H=200 m, mean gradient 1:10).</summary>
        Shield = 0,

        /// <summary>A stratovolcano. A straight cone (default R=1200 m / H=600 m, mean
        /// gradient 1:2). **⑤'s default.**</summary>
        Strato = 1,

        /// <summary>A lava dome. Small and steep (default R=350 m / H=300 m, mean gradient
        /// 1:1.17).</summary>
        Dome = 2,
    }

    /// <summary>
    /// The height profiles of the three forms. **Every number here was invented by this mod.**
    /// Vanilla has no such phenomenon as a volcano, and there is not one prefab, material or
    /// shader for lava, magma or melt — not even a single entry in the DLL's string heap
    /// (IL findings doc §B-5).
    ///
    /// The one exception is <b>the shield volcano's profile</b>, which is **a straight copy**
    /// of the shape DisasterHelpers.MakeCrater(pos, R, -H, raiseEdges:false) actually draws
    /// (§C-8's measured formulas 1 - (d/0.837R)^4, and 4(u-1.197)^2 beyond 0.73R).
    ///
    /// **So why not call MakeCrater?** Because MakeCrater calls
    /// TerrainModify.RefreshAllModifications() at its top (§C-8 IL_0006), so **every call
    /// forces one full terrain flush**. Called every tick during a staged uplift, that
    /// disables vanilla's batching optimisation every tick (design conclusion 3 in §A-1).
    /// The shape is the same; only the cost differs. **Do not revert to it saying "one
    /// MakeCrater call will do".**
    ///
    /// ★ The summit crater dropped <c>MakeCrater</c> too, on 2026-08-22 (in-game report ①,
    /// "it would be better if the crater were generated as a depression from the start").
    /// The crater is returned by <see cref="VolcanoCrater"/> as **part of the height
    /// profile**, so it grows along with the mountain. ⑤ no longer calls
    /// <c>MakeCrater</c> from anywhere.
    ///
    /// There is no gradient clamp, no erosion and no smoothing pass (§C-9), so a steep cone
    /// does not collapse. The only two constraints are "a 16 m grid" and "1/64 m
    /// quantisation". Which is why **the lava dome's radius is never taken below 250 m
    /// (8 raw cells each side)**.
    /// Below that it looks like noise in the ground rather than a mountain.
    ///
    /// The height ceiling is 65535/64 = 1023.984375 m, and **going past it raises no
    /// exception; the summit silently becomes a flat plateau** (§C-10).
    /// <see cref="HeightFor"/> subtracts the starting terrain height first, and
    /// <see cref="HeightWasLimitedByCeiling"/> separately reports "whether it was cut".
    /// The caller must show that to the player up front.
    /// </summary>
    public static class VolcanoShape
    {
        /// <summary>Raw units per metre (<c>RawHeights</c> is <c>raw/64</c> metres,
        /// §C-8).</summary>
        public const float RawUnitsPerMetre = 64f;

        /// <summary>The metre value of one raw unit (= 0.015625 m). **Any change smaller than
        /// this is lost to rounding.**</summary>
        public const float MetresPerRawUnit = 1f / 64f;

        /// <summary>The representable ceiling on terrain height (= 65535/64 m, §C-10). Going
        /// past it raises no exception.</summary>
        public const float MaxTerrainMetres = 1023.984375f;

        /// <summary>The side of a raw cell (m). §C-8's <c>cell = 16</c>.</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>The lava dome's minimum radius (m). 8 raw cells each side. §C-9.</summary>
        public const float MinDomeRadiusMetres = 250f;

        /// <summary>The shield's falloff reference (§C-8's <c>radius * 0.837</c>).</summary>
        public const float ShieldFalloff = 0.837f;

        /// <summary>The boundary of the shield's inner branch (§C-8's
        /// <c>d &lt; radius * 0.73</c>).</summary>
        public const float ShieldInnerFraction = 0.73f;

        /// <summary>The offset in the shield's outer branch (§C-8's <c>u - 1.197</c>).</summary>
        public const float ShieldOuterOffset = 1.197f;

        /// <summary>The crater radius as a fraction (of the mountain's radius). Bounded above
        /// and below.</summary>
        private const float CraterRadiusFraction = 0.12f;

        /// <summary>The crater depth as a fraction (of the mountain's height). Bounded above
        /// and below.</summary>
        private const float CraterDepthFraction = 0.12f;

        /// <summary>
        /// The fraction of the mountain's height the crater's depth must never exceed.
        /// **It is there so a low mountain is not punched through**; without it,
        /// <see cref="MinCraterDepthMetres"/> (10 m) puts a 10 m hole in a 15 m mountain and
        /// the crater floor drops all the way to the original ground.
        /// </summary>
        private const float MaxCraterDepthOfHeight = 0.45f;

        private const float MinCraterRadiusMetres = 40f;
        private const float MaxCraterRadiusMetres = 400f;
        private const float MinCraterDepthMetres = 10f;
        private const float MaxCraterDepthMetres = 60f;

        /// <summary>
        /// From the setting value (the int saved in .cgs) to a form. **Out-of-range values
        /// fall to the default <see cref="VolcanoForm.Strato"/>** (the settings file can be
        /// edited by hand).
        /// </summary>
        public static VolcanoForm FormOf(int settingValue)
        {
            switch (settingValue)
            {
                case 0: return VolcanoForm.Shield;
                case 2: return VolcanoForm.Dome;
                default: return VolcanoForm.Strato;
            }
        }

        /// <summary>The default radius (m) for each form.</summary>
        public static float DefaultRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 2000f;
                case VolcanoForm.Dome: return 350f;
                default: return 1200f;
            }
        }

        /// <summary>The minimum radius (m) for each form. Only the dome uses
        /// <see cref="MinDomeRadiusMetres"/> (§C-9).</summary>
        public static float MinRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 800f;
                case VolcanoForm.Dome: return MinDomeRadiusMetres;
                default: return 400f;
            }
        }

        /// <summary>
        /// The maximum radius (m) for each form.
        ///
        /// ── ★★ Made to work right to the top of the slider (2026-08-22) ─────────
        ///
        /// The in-game report:
        ///
        /// &gt; I think the volcano's 25.5 scale is too small.
        ///
        /// Quite right, and **the band was topping out before the slider did.**
        /// The top of the multiplier is <c>VolcanoSizeScale.MaxScale</c> (255/55 ≈ 4.64), so
        /// a stratovolcano is asked for 1200 × 4.64 = 5568 m. That was being cut at 2000 m,
        /// so <b>above a displayed 9.2 the slider moved and the radius did not change by a
        /// single metre</b>.
        ///
        /// The current ceilings are "the recommended value × the top multiplier", rounded.
        /// They are then held down by <see cref="AbsoluteMaxRadiusMetres"/> — the uplift
        /// holds two <c>ushort[]</c>s covering the affected rectangle (proportional to
        /// area), so allowing a map half-side (8640 m) would put a single volcano into tens
        /// of megabytes.
        /// </summary>
        public static float MaxRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                // Recommended 2000 × 4.64 = 9280 → held down to 6000 by the memory cost ceiling.
                case VolcanoForm.Shield: return AbsoluteMaxRadiusMetres;
                // Recommended 350 × 4.64 = 1624.
                case VolcanoForm.Dome: return 1650f;
                // Recommended 1200 × 4.64 = 5568.
                default: return 5600f;
            }
        }

        /// <summary>
        /// The radius (m) no form ever exceeds. **It is a cost ceiling, not a statement
        /// about shape.**
        /// The uplift's backup array is 279 KB at a 3 km radius (proportional to area), so
        /// about 1.1 MB at 6 km. If you raise this, fix that table too.
        /// </summary>
        public const float AbsoluteMaxRadiusMetres = 6000f;

        /// <summary>The default height (m) for each form.</summary>
        public static float DefaultHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 200f;
                case VolcanoForm.Dome: return 300f;
                default: return 600f;
            }
        }

        /// <summary>The minimum height (m) for each form.</summary>
        public static float MinHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 50f;
                default: return 100f;
            }
        }

        /// <summary>
        /// The maximum height (m) for each form. Raised to "the recommended value × the top
        /// multiplier" for the same reason as <see cref="MaxRadiusOf"/> (2026-08-22).
        ///
        /// ★★ <b>The game's terrain ceiling is 1024 m</b>
        ///   (see <c>UpliftSchedule.CeilingMetres</c>: <c>ushort</c> × 0.015625 m).
        ///   That is a ceiling on <b>height above sea level</b>, so raising a 700 m mountain
        ///   on land 400 m up gets its summit shaved off — and in that case
        ///   <c>VolcanoState</c> always says "cut by the ceiling". **A mod cannot raise it.**
        ///   So there is no point putting this above 1024.
        /// </summary>
        public static float MaxHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                // Recommended 200 × 4.64 = 928. A shield volcano stays "broad and low"
                // (the radius goes out to 6 km, so the proportion is not broken).
                case VolcanoForm.Shield: return 930f;
                // Recommended 300 × 4.64 = 1392 → held down by the ceiling.
                case VolcanoForm.Dome: return AbsoluteMaxHeightMetres;
                // Recommended 600 × 4.64 = 2784 → held down by the ceiling.
                default: return AbsoluteMaxHeightMetres;
            }
        }

        /// <summary>
        /// The height (m) no form ever exceeds. Placed a little below the game's terrain
        /// ceiling (1024 m) — put it exactly there and, even on land at sea level, one raw
        /// unit of the summit touches the ceiling and gets reported as "cut".
        /// </summary>
        public const float AbsoluteMaxHeightMetres = 1000f;

        /// <summary>
        /// Clamps the requested radius into the form's band. NaN gives the default.
        /// **Do not silently discard it** — .cgs is a public contract and can be edited by hand.
        /// </summary>
        public static float RadiusFor(VolcanoForm form, float requestedRadiusMetres)
        {
            if (float.IsNaN(requestedRadiusMetres)) return DefaultRadiusOf(form);
            return Clamp(requestedRadiusMetres, MinRadiusOf(form), MaxRadiusOf(form));
        }

        /// <summary>
        /// The headroom (m) from the starting terrain height to the ceiling. NaN gives 0.
        /// It never goes negative.
        /// </summary>
        public static float HeadroomMetres(float baseHeightMetres)
        {
            if (float.IsNaN(baseHeightMetres)) return 0f;
            float headroom = MaxTerrainMetres - baseHeightMetres;
            return headroom > 0f ? headroom : 0f;
        }

        /// <summary>
        /// Clamps the requested height into the form's band and then **cuts it further by
        /// the ceiling (§C-10)**.
        ///
        /// ★ This used to subtract the crater rim's headroom too
        ///   (<c>CraterRimHeadroomOf</c>). <c>MakeCrater(raiseEdges:true)</c> raised an
        ///   annular rim <b>above the summit</b>, and when that broke through the ceiling
        ///   the rim alone went silently flat.
        ///   Now that the crater has been folded into the height profile (see
        ///   <see cref="VolcanoCrater"/>), **the greatest height ⑤ writes is exactly
        ///   <c>base + H</c>** — the crater rim is H itself, and nothing is written a single
        ///   millimetre above it.
        ///   So there is nothing left to subtract.
        ///
        /// At high elevations <c>limit</c> can fall below <see cref="MinHeightOf"/>.
        /// In that case it does not get raised to the minimum; **the small value is
        /// returned as it is** — so that the caller can look at
        /// <see cref="HeightWasLimitedByCeiling"/> and refuse up front with "a volcano
        /// cannot be made here".
        /// </summary>
        public static float HeightFor(VolcanoForm form, float requestedHeightMetres,
                                      float baseHeightMetres)
        {
            float h = float.IsNaN(requestedHeightMetres)
                ? DefaultHeightOf(form)
                : Clamp(requestedHeightMetres, MinHeightOf(form), MaxHeightOf(form));

            float limit = HeadroomMetres(baseHeightMetres);
            if (limit < h) h = limit;
            return Clamp(h, 0f, MaxHeightOf(form));
        }

        /// <summary>
        /// Whether the ceiling makes the mountain lower than requested. The way to avoid
        /// **silently building a lower mountain**.
        /// </summary>
        public static bool HeightWasLimitedByCeiling(VolcanoForm form, float requestedHeightMetres,
                                                     float baseHeightMetres)
        {
            float requested = float.IsNaN(requestedHeightMetres)
                ? DefaultHeightOf(form)
                : Clamp(requestedHeightMetres, MinHeightOf(form), MaxHeightOf(form));
            return HeightFor(form, requestedHeightMetres, baseHeightMetres) < requested;
        }

        /// <summary>
        /// The **rise above the terrain** (m) at a point <paramref name="distanceMetres"/>
        /// from the centre.
        ///
        /// **Outside the radius it returns exactly 0.** Leave even a millimetre and
        /// <c>UpdateArea</c>'s rectangle grows without limit. Abnormal input (NaN, a
        /// negative distance, R&lt;=0, H&lt;=0) also gives 0.
        /// </summary>
        public static float ProfileAt(VolcanoForm form, float distanceMetres,
                                      float radiusMetres, float heightMetres)
        {
            if (float.IsNaN(distanceMetres) || float.IsNaN(radiusMetres) || float.IsNaN(heightMetres)) return 0f;
            if (distanceMetres < 0f || radiusMetres <= 0f || heightMetres <= 0f) return 0f;
            if (distanceMetres >= radiusMetres) return 0f;

            switch (form)
            {
                case VolcanoForm.Shield:
                    return ShieldProfile(distanceMetres, radiusMetres, heightMetres);

                case VolcanoForm.Dome:
                {
                    float t = distanceMetres / radiusMetres;
                    float k = 1f - t * t;
                    return heightMetres * k * k;
                }

                default:
                    return heightMetres * (1f - distanceMetres / radiusMetres);
            }
        }

        /// <summary>
        /// §C-8's measured formula for <c>MakeCrater(pos, R, -H, raiseEdges:false)</c>, exactly.
        /// Inside, <c>H(1 - u^4)</c>; beyond 0.73R, <c>4H(u - 1.197)^2</c> (u = d / 0.837R).
        /// </summary>
        private static float ShieldProfile(float distanceMetres, float radiusMetres,
                                           float heightMetres)
        {
            float u = distanceMetres / (ShieldFalloff * radiusMetres);
            if (distanceMetres < ShieldInnerFraction * radiusMetres)
            {
                float u2 = u * u;
                return heightMetres * (1f - u2 * u2);
            }

            float w = u - ShieldOuterOffset;
            return heightMetres * 4f * w * w;
        }

        /// <summary>The summit crater's radius (m). **Capped at 400 m** —
        /// so that one crater's rectangle does not span 128 cells (2048 m).</summary>
        public static float CraterRadiusOf(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return 0f;
            return Clamp(radiusMetres * CraterRadiusFraction,
                         MinCraterRadiusMetres, MaxCraterRadiusMetres);
        }

        /// <summary>
        /// The summit crater's depth (m). **There are two tiers of cap so it does not punch
        /// through the mountain** — the absolute <see cref="MaxCraterDepthMetres"/>, and
        /// <see cref="MaxCraterDepthOfHeight"/> relative to the mountain's height. Without
        /// the latter, a mountain that could only be raised 15 m right under the ceiling
        /// gets a 10 m hole in it.
        /// </summary>
        public static float CraterDepthOf(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0f;

            float depth = Clamp(heightMetres * CraterDepthFraction,
                                MinCraterDepthMetres, MaxCraterDepthMetres);

            float limit = heightMetres * MaxCraterDepthOfHeight;
            return depth < limit ? depth : limit;
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
