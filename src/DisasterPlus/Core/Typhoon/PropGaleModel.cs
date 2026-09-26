using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// Whether a gale carries off a prop (a sign, a road sign, a parasol, a street light…).
    /// **Pure engine-free functions only.**
    ///
    /// ── The owner's request (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; …to make the gale and the heavy rain destroy small buildings and signage props
    /// &gt; around the city, and cause local flooding.
    ///
    /// The buildings and the flooding already exist (<c>TyphoonWind.Gale</c> /
    /// <c>TyphoonFlood</c>). <b>Props were the one thing where nothing was being destroyed
    /// at all</b>, so this adds them.
    ///
    /// ── ★★ What gets destroyed and what does not ────────────────────────────────
    ///
    /// What breaks is <b>whatever is small and light</b>. The test is
    /// <see cref="FragilityOf"/> — derived from the prop's <b>size</b>.
    ///
    /// <list type="bullet">
    /// <item>Signs, road signs, bins, parasols (up to ~3 m) … carried off</item>
    /// <item>Street lights, telegraph poles (up to ~8 m) … snap only in a strong wind</item>
    /// <item>Anything bigger (water towers, chimneys and so on) … never carried off</item>
    /// </list>
    ///
    /// ★★ <b>Trees are out of scope.</b> Trees belong to <c>TreeManager</c> and are not in
    ///   <c>PropManager</c>. Doing fallen trees would need a different type
    ///   — **do not come away thinking trees are covered here.**
    ///
    /// ★★ <b>What you carry off cannot be put back.</b> <c>PropManager.ReleaseProp</c>
    ///   cannot be undone, so the thresholds are deliberately <b>stingy</b>.
    ///   "Every sign in the city vanishes when a typhoon arrives" is a kind of breakage
    ///   that cannot be repaired.
    /// </summary>
    public static class PropGaleModel
    {
        /// <summary>Below this wind, nothing is carried off at all (m/s).</summary>
        public const float MinWindMetresPerSecond = 24f;

        /// <summary>At this wind speed, the most fragile things are certain to go (m/s).</summary>
        public const float FullWindMetresPerSecond = 62f;

        /// <summary>Up to this size, a prop counts as "the most fragile" (m). The size of a
        /// sign or a road sign.</summary>
        public const float FragileSizeMetres = 3f;

        /// <summary>Anything larger than this size is never carried off (m).</summary>
        public const float SturdySizeMetres = 9f;

        /// <summary>
        /// The cap on the fraction carried off in one pass. **Never 1** —
        /// at 1, every sign in the area disappears the instant the gale radius reaches it.
        /// </summary>
        public const float MaxTakeRatio = 0.55f;

        /// <summary>
        /// How fragile a prop is, <c>[0,1]</c>. 1 is a sign, 0 is "never carried off".
        /// <paramref name="sizeMetres"/> is the representative size of its collision bounds.
        /// </summary>
        public static float FragilityOf(float sizeMetres)
        {
            if (IsBad(sizeMetres) || sizeMetres <= 0f) return 0f;
            if (sizeMetres <= FragileSizeMetres) return 1f;
            if (sizeMetres >= SturdySizeMetres) return 0f;

            return 1f - (sizeMetres - FragileSizeMetres)
                        / (SturdySizeMetres - FragileSizeMetres);
        }

        /// <summary>
        /// The probability of being carried off at this wind and this fragility,
        /// <c>[0, <see cref="MaxTakeRatio"/>]</c>.
        /// </summary>
        public static float TakeChance(float windMetresPerSecond, float fragility)
        {
            float f = Clamp01(fragility);
            if (f <= 0f) return 0f;

            if (IsBad(windMetresPerSecond)) return 0f;
            if (windMetresPerSecond <= MinWindMetresPerSecond) return 0f;

            float w = (windMetresPerSecond - MinWindMetresPerSecond)
                      / (FullWindMetresPerSecond - MinWindMetresPerSecond);
            w = Clamp01(w);

            // Wind does not act linearly (force goes as the square of the speed).
            return MaxTakeRatio * w * w * f;
        }

        /// <summary>
        /// Does this prop get carried off? **Deterministic** —
        /// with the same <paramref name="propId"/> and <paramref name="round"/> you get the
        /// same answer however many times you call it (this mod's discipline about random
        /// numbers).
        /// </summary>
        /// <param name="round">
        /// Which pass this is. **Do not pass the same value every time** — do that and a
        /// sign that survived once will never be carried off again (however much the wind
        /// picks up).
        /// </param>
        public static bool Takes(ushort propId, uint round, float windMetresPerSecond,
                                 float fragility, uint seed)
        {
            float chance = TakeChance(windMetresPerSecond, fragility);
            if (chance <= 0f) return false;

            return DeterministicRandom.Unit(seed, (uint)propId * 31u + round * 7919u) < chance;
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
