namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The chance of collapse from wind. **This is physics this mod invented; vanilla has no
    /// corresponding quantity at all.**
    ///
    /// Vanilla has not one mechanism for wind destruction, and **there is not even a field
    /// that raises the wind speed** (§A-5 / §B5 of the IL facts document). The only readers
    /// of wind speed are wind turbine output, the sway of trees and roads, the scrolling of
    /// fog and clouds, and a hazard map correction — none of which destroys a building. Even
    /// <c>DisasterHelpers.AddWind</c> merely pushes citizens and vehicles about, unharmed.
    /// So what we have here is not "visualisation" but "invention".
    ///
    /// **There are no units.** wind is a coefficient in [0,1] and the return value is a
    /// chance per sweep. **It does not correspond to a real wind speed (m/s)** (design
    /// document §7.3 — the same reason ② did not claim to use the JMA seismic intensity
    /// scale). Callers must not display this value as m/s.
    ///
    /// Note that height is treated differently here than in ②'s <c>LongPeriodResponse</c>.
    /// That one did nothing when the height could not be read (because its premise is "the
    /// taller, the more damage"), but a typhoon blows single-storey houses away too. Here
    /// height is a **coefficient**: if it cannot be read we merely decline the bonus, and
    /// the building stays in scope. We are not guessing the height.
    ///
    /// There is no randomness here. The draw is done by the caller
    /// (<c>Game/Typhoon/TyphoonWind</c>) with <c>DeterministicRandom</c> — never with
    /// <c>VanillaRandomizer</c>. What is decided here is not a value vanilla draws but a
    /// judgement ④ invented.
    /// </summary>
    public static class WindDamageModel
    {
        /// <summary>
        /// Anything below this wind equivalent is ignored entirely. **We do not create
        /// "houses flying away at the edge of the gale zone".**
        /// </summary>
        public const float MinWind = 0.25f;

        /// <summary>
        /// The cap on the per-sweep collapse chance, before the height bonus is applied.
        ///
        /// Vanilla's whole-quake disc (earthquake) is 0.02 and ②'s long-period is 0.25, so
        /// we put this in between. The sweep runs once per 256 frames, which accumulates
        /// plenty over a typhoon's whole duration.
        /// **It is not a physical constant.** It is a number ④ chose.
        /// </summary>
        public const float MaxCollapseChance = 0.05f;

        /// <summary>The extra applied once the height reaches the ceiling (= a coefficient
        /// of 1.6).</summary>
        public const float HeightBonus = 0.6f;

        /// <summary>Up to here there is no height bonus (coefficient 1.0).</summary>
        public const float HeightFloorMetres = 10f;

        /// <summary>Above here it grows no further. **A ceiling so that a single skyscraper
        /// never reaches a chance of 1.0.**</summary>
        public const float HeightCeilingMetres = 70f;

        /// <summary>The maximum value of the strength slider (0-10).</summary>
        private const float MaxStrength = 10f;

        /// <summary>
        /// The coefficient derived from the building's height (m).
        ///
        /// If <paramref name="heightMetres"/> is 0 (i.e. <c>BuildingHeight.MetresOf</c>
        /// answered "could not read it"), it returns **1.0 (no bonus)**. That is not
        /// guessing the height — it is **declining to apply the bonus** (see the class doc).
        /// </summary>
        public static float HeightFactor(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= HeightFloorMetres) return 1f;
            if (heightMetres >= HeightCeilingMetres) return 1f + HeightBonus;

            float t = (heightMetres - HeightFloorMetres)
                      / (HeightCeilingMetres - HeightFloorMetres);
            return 1f + HeightBonus * t;
        }

        /// <summary>
        /// The collapse chance per sweep.
        ///
        /// <paramref name="strength"/> is the setting's 0-10. **At 0 it returns exactly 0**
        /// (the guarantee that the slider can switch it off completely). The <c>.cgs</c> is
        /// a public contract, so out-of-range values are clamped here too.
        ///
        /// Bad input (NaN, negative) returns 0. **We do not create "every building collapses
        /// because the wind speed is NaN".**
        /// </summary>
        public static float CollapseChance(float wind, float heightMetres, int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            if (float.IsNaN(wind) || wind <= MinWind) return 0f;
            if (wind > 1f) wind = 1f;

            // ★ A height of 0 means "unknown": we decline the bonus but keep the building in
            //   scope (see the class doc). A height of NaN is not "unknown" but **a broken
            //   reading**, so we reject that one. HeightFactor returning 1.0 for NaN is a
            //   meaningful default in its own right, but quietly rounding a broken value to
            //   the default and then flattening a building is another matter entirely.
            if (float.IsNaN(heightMetres) || heightMetres < 0f) return 0f;

            float excess = (wind - MinWind) / (1f - MinWind);
            float chance = excess * HeightFactor(heightMetres)
                           * (strength / MaxStrength) * MaxCollapseChance;

            // The cap is "the value with the height bonus fully applied". Make it
            // MaxCollapseChance instead and differences in height get crushed at the cap,
            // leaving TallerBuildingsCatchMoreWind meaningless.
            float ceiling = MaxCollapseChance * (1f + HeightBonus);
            if (float.IsNaN(chance) || chance <= 0f) return 0f;
            return chance > ceiling ? ceiling : chance;
        }
    }
}
