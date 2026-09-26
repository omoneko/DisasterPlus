using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **Decides how much lava flows out, from the eruption's scale.**
    /// <b>This is Core, so it never touches the engine.</b>
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// > Please also vary the amount of magma that flows out with the eruption's scale.
    ///
    /// Until then the lava's **flow count and length were both fixed by the settings**, and
    /// whether the slider was at 1.0 or at 25.5 the same number of flows ran the same
    /// distance. Changing only the mountain's size while the lava stays put means **the
    /// bigger the volcano, the less noticeable the lava**.
    ///
    /// ── What counts as "scale" ─────────────────────────────
    ///
    /// <b>The mountain's radius.</b> ⑤'s only size knob is the single intensity slider
    /// (<see cref="VolcanoSizeScale"/>), and that grows the radius and the final height by
    /// the same factor. The eruption's strength (<c>EruptionIntensityUnit</c>) is
    /// <b>not used</b> — that is a quantity that fluctuates from one phase of the eruption
    /// to the next, and deciding the flow count from it would mean **the number of flows
    /// going up and down during the eruption** (which would mean deleting flows that are
    /// already running). Unless the scale is something <b>fixed the moment it is placed and
    /// never moving again</b>, the lava side cannot hold state.
    ///
    /// ── The ratio ───────────────────────────────────────
    ///
    /// Neither the count nor the length is made <b>proportional to the radius</b>. The
    /// radius spans 250 m to 3,000 m, a factor of 12; make it proportional and a small
    /// volcano produces not a single lava flow while a large one pins to <c>MaxFlows</c> and
    /// the distinction disappears. <see cref="Unit"/> is <b>the square root of the ratio to
    /// the reference radius</b>, which flattens a 12× spread of radii into 3.5×.
    /// </summary>
    public static class LavaVolume
    {
        /// <summary>
        /// The radius (m) treated as scale 1.0. Set to the same value as the recommended
        /// figure for a stratovolcano (<c>VolcanoShape.DefaultRadiusOf(Strato)</c>) — so
        /// that **with the default settings and the slider in its default position, the flow
        /// count is what it always was**.
        /// </summary>
        public const float ReferenceRadiusMetres = 1200f;

        /// <summary>The floor on the scale. Never 0 (0 means "produce no lava", not a small
        /// eruption).</summary>
        public const float MinUnit = 0.45f;

        /// <summary>The ceiling on the scale.</summary>
        public const float MaxUnit = 2.0f;

        /// <summary>
        /// The floor on the flow-length multiplier. **It does go below 1** — lava from a
        /// small volcano running kilometres past its foot is the same as the scale having no
        /// effect at all.
        /// </summary>
        public const float MinLengthFactor = 0.40f;

        /// <summary>The ceiling on the flow-length multiplier.</summary>
        public const float MaxLengthFactor = 1.8f;

        /// <summary>
        /// Radius → scale, in <c>[MinUnit, MaxUnit]</c>.
        /// If <paramref name="radiusMetres"/> is bad, returns 1 (= exactly as configured).
        /// </summary>
        public static float Unit(float radiusMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 1f;

            float ratio = radiusMetres / ReferenceRadiusMetres;
            float unit = (float)Math.Sqrt(ratio);

            if (unit < MinUnit) return MinUnit;
            if (unit > MaxUnit) return MaxUnit;
            return unit;
        }

        /// <summary>
        /// The number of flows actually produced. <paramref name="configuredFlows"/> is the
        /// configured count and <paramref name="maxFlows"/> is the implementation's cap.
        ///
        /// ★★ <b>If the setting is 0, return 0.</b> 0 means "lava is switched off
        ///   entirely", not "the smallest eruption" — the scale must never bring it back up
        ///   to 1.
        ///
        /// ★ If the setting is 1 or more, <b>always return at least 1</b>. Returning 0 for a
        ///   small volcano looks to the player like nothing but "the lava feature is
        ///   broken".
        /// </summary>
        public static int FlowCount(int configuredFlows, int maxFlows, float radiusMetres)
        {
            if (configuredFlows <= 0) return 0;
            if (maxFlows <= 0) return 0;

            int wanted = (int)(configuredFlows * Unit(radiusMetres) + 0.5f);
            if (wanted < 1) wanted = 1;
            if (wanted > configuredFlows && wanted > maxFlows) wanted = maxFlows;
            if (wanted > maxFlows) wanted = maxFlows;
            return wanted;
        }

        /// <summary>
        /// The multiplier applied to a flow's length, in
        /// <c>[MinLengthFactor, MaxLengthFactor]</c>.
        /// It responds more strongly than the count does (the count is an integer and so
        /// steps coarsely, and **the length reads as "the amount changed" more readily**).
        /// </summary>
        public static float LengthFactor(float radiusMetres)
        {
            float unit = Unit(radiusMetres);

            // 1.0 at scale 1, swinging a little harder than the scale in both directions.
            float factor = 1f + (unit - 1f) * 1.35f;

            if (factor < MinLengthFactor) return MinLengthFactor;
            if (factor > MaxLengthFactor) return MaxLengthFactor;
            return factor;
        }

        /// <summary>The floor on the flow-width multiplier.</summary>
        public const float MinWidthFactor = 0.60f;

        /// <summary>The ceiling on the flow-width multiplier.</summary>
        public const float MaxWidthFactor = 1.55f;

        /// <summary>
        /// The multiplier applied to a flow's **width** (2026-08-22, at the owner's request:
        /// "I'd like the lava flows a bit wider, please — scaled to the eruption").
        ///
        /// It responds **more weakly** than the length (<see cref="LengthFactor"/>) does.
        /// Width registers as area, so swinging it by the same ratio as the length would
        /// make a large volcano's lava wider than the mountain. <c>Unit^0.75</c> pulls it
        /// towards 1.
        ///
        /// ★ **Exactly 1** at the reference radius. With the default settings and the
        ///   slider in its default position, the base width
        ///   (<c>LavaPath.SpreadBaseMetres</c>) comes out unchanged.
        /// </summary>
        public static float WidthFactor(float radiusMetres)
        {
            float factor = (float)Math.Pow(Unit(radiusMetres), 0.75);

            if (IsBad(factor)) return 1f;
            if (factor < MinWidthFactor) return MinWidthFactor;
            if (factor > MaxWidthFactor) return MaxWidthFactor;
            return factor;
        }

        /// <summary>
        /// How many steps one flow may walk. <paramref name="baseSteps"/> is the
        /// implementation's cap (<c>LavaPath.MaxSteps</c>), and we **never exceed it** (it
        /// is what the array lengths are sized by).
        /// </summary>
        public static int StepBudget(int baseSteps, float radiusMetres)
        {
            if (baseSteps <= 0) return 0;

            int steps = (int)(baseSteps * LengthFactor(radiusMetres) + 0.5f);
            if (steps < 1) steps = 1;
            if (steps > baseSteps) steps = baseSteps;
            return steps;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
