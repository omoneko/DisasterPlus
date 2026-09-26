namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The collapse probability inside a local damage area (a patch). **This too is physics
    /// this mod invented.**
    ///
    /// Two things separate it from <see cref="WindDamageModel"/> (the typhoon's overall
    /// wind damage):
    ///
    /// 1. **The magnitude is different.** That one is at most 8% per sweep; this one is at
    ///    most <see cref="MaxCollapseChance"/> = 70% per encounter. "Only a narrow strip
    ///    gets wrecked as though a tornado went through" is exactly what was asked for.
    /// 2. **It does not look at building height.** A tornado flattens a bungalow and a
    ///    tower block alike. Vary it by height and you no longer have "tornado damage",
    ///    you have "strong wind damage".
    ///
    /// The draw is done by the caller (<c>Game/Typhoon/TyphoonGust</c>) as
    /// <c>DeterministicRandom.Unit(patch seed, building ID)</c>, and **the frame is not
    /// mixed in**. So the roll for one building is fixed once per patch, and the building
    /// only falls when the patch comes close enough to push the probability up — that is
    /// the implementation of "the track the patch left behind is what is wrecked".
    /// Mix the frame in and the same building is re-drawn every tick, so its chance climbs
    /// without limit.
    ///
    /// **There are no units here.** What comes back is a probability, not a wind speed (m/s).
    /// </summary>
    public static class GustDamageModel
    {
        /// <summary>The maximum probability at the patch centre (at strength 10 and
        /// destructiveness 1.0).</summary>
        public const float MaxCollapseChance = 0.70f;

        /// <summary>The top of the strength slider (0-10).</summary>
        private const float MaxStrength = 10f;

        /// <summary>
        /// The collapse probability.
        ///
        /// <paramref name="distanceFraction"/> is distance from the patch centre ÷ patch
        /// radius (0 at the centre, 1 at the rim). **1 or above gives 0** (outside the
        /// patch not one building falls).
        ///
        /// <paramref name="strengthFraction"/> is this individual patch's destructiveness
        /// [0, 1] (produced by <c>GustPatchPlan.Patch</c>).
        ///
        /// <paramref name="strength"/> is the setting, 0-10. **At 0 it returns exactly 0**
        /// (the guarantee that the slider can switch it off completely). <c>.cgs</c> is a
        /// public contract, so out-of-range values get clamped here too.
        ///
        /// Broken input (NaN, negative) gives 0. **Do not build a "NaN distance flattens
        /// every building".**
        /// </summary>
        public static float CollapseChance(float distanceFraction, float strengthFraction,
                                           int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            // NaN falls out on the !(x >= 0) side.
            if (!(distanceFraction >= 0f) || distanceFraction >= 1f) return 0f;
            if (float.IsNaN(strengthFraction) || strengthFraction <= 0f) return 0f;
            if (strengthFraction > 1f) strengthFraction = 1f;

            // 1 at the centre, 0 at the rim. Squared so the rim drops away quickly
            // — do not build a "half of it still falls at the rim".
            float falloff = 1f - distanceFraction;
            falloff *= falloff;

            float chance = MaxCollapseChance * falloff * strengthFraction
                           * (strength / MaxStrength);

            if (float.IsNaN(chance) || chance <= 0f) return 0f;
            return chance > MaxCollapseChance ? MaxCollapseChance : chance;
        }
    }
}
