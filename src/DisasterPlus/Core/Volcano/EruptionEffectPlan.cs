namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// Decides, from the strength, the quantities handed to the eruption's visuals
    /// (<b>the particle density and the radius of the circle they spawn in</b>).
    /// **Pure, engine-free functions only.** No Unity types, no game types, no randomness.
    ///
    /// ── Why it lives in Core ─────────────────────────────────
    ///
    /// The <c>magnitude</c> passed to vanilla's <c>ParticleEffect.RenderEffect</c> is
    /// **the particle density**, not a size. The number of particles spawned per frame is
    ///
    /// <code>
    /// count = max(100, PI * r^2) * (timeDelta * magnitude * 0.01) * rateOverTime
    /// </code>
    ///
    /// (measured from the IL). So the two knobs that make "visual strength" are
    /// <c>magnitude</c> and <c>r</c>, and both are **just mappings between numbers**. Scatter
    /// the mappings across the Game side and you cannot verify a single line without
    /// launching the game, so we collect them here and pin them down with tests.
    ///
    /// ── The basis for the ranges ────────────────────────────────────────
    ///
    /// The <c>magnitude &lt;= 1</c> range is <b>the convention for building fires</b>
    /// (<c>m_fireIntensity / 255</c>) and does not apply when calling
    /// <c>ParticleEffect</c> directly.
    /// Vanilla itself takes sinkholes up to 2.0, and making <c>Factory Smoke</c> — whose
    /// <c>rateOverTime</c> is only 15 — look like a column takes two digits (at radius 30 m
    /// and 60 fps, <c>magnitude = 1</c> gives only 5 particles a frame).
    /// **So every number here is a presentation value ⑤ chose**, not a physical quantity.
    /// </summary>
    public static class EruptionEffectPlan
    {
        // ── The plume (the ash column) ──────────────────────────────────

        /// <summary>The floor on the plume's density (at strength 0).</summary>
        public const float PlumeMagnitudeMin = 14f;

        /// <summary>The ceiling on the plume's density (at strength 1).</summary>
        public const float PlumeMagnitudeMax = 78f;

        /// <summary>The floor on the radius of the circle the plume spawns in (as a ratio of
        /// the crater's radius).</summary>
        public const float PlumeRadiusFloorRatio = 0.30f;

        /// <summary>The radius added by the strength (as a ratio of the crater's
        /// radius).</summary>
        public const float PlumeRadiusGainRatio = 0.28f;

        // ── The flames (the crater and the lava's edges) ─────────────────────

        /// <summary>The floor on the flames' density. <c>Fire Particles</c> has a high
        /// <c>rateOverTime</c> of 200, so far smaller numbers suffice than for the
        /// plume.</summary>
        public const float FlameMagnitudeMin = 1.6f;

        /// <summary>The ceiling on the flames' density.</summary>
        public const float FlameMagnitudeMax = 9f;

        /// <summary>The floor on the radius of the circle the flames spawn in (as a ratio of
        /// the crater's radius).</summary>
        public const float FlameRadiusFloorRatio = 0.18f;

        /// <summary>The flame radius added by the strength (as a ratio of the crater's
        /// radius).</summary>
        public const float FlameRadiusGainRatio = 0.22f;

        // ── Ejecta ───────────────────────────────────────

        /// <summary>How long one burst of ejecta is thrown up for (seconds).</summary>
        public const float EjectaBurstSeconds = 0.55f;

        /// <summary>The longest interval between ejecta (i.e. at its weakest;
        /// seconds).</summary>
        public const float EjectaPeriodMaxSeconds = 7.5f;

        /// <summary>The shortest interval between ejecta (i.e. at its strongest;
        /// seconds).</summary>
        public const float EjectaPeriodMinSeconds = 1.6f;

        /// <summary>The floor on one ejecta burst's density.</summary>
        public const float EjectaMagnitudeMin = 6f;

        /// <summary>The ceiling on one ejecta burst's density.</summary>
        public const float EjectaMagnitudeMax = 34f;

        /// <summary>The radius of the circle the ejecta spawn in (as a ratio of the crater's
        /// radius).</summary>
        public const float EjectaRadiusRatio = 0.22f;

        // ── The blast and the ejecta's impacts (2026-08-22, at the owner's request:
        //    "explosions + ejecta") ────────

        /// <summary>
        /// The floor on one blast's density. It is **a one-shot through
        /// <c>DispatchEffect</c>**, so it does not need two digits the way the plume does in
        /// its continuous mode
        /// (<c>Medium Explosion Particles</c> has an <c>m_renderDuration</c> of 1.0 second,
        /// and one push is enough for it to decay along <c>m_intensityCurve</c> and
        /// disappear).
        /// </summary>
        public const float BlastMagnitudeMin = 1.4f;

        /// <summary>The ceiling on one blast's density.</summary>
        public const float BlastMagnitudeMax = 5.5f;

        /// <summary>The floor on the radius of the circle the blast spawns in (as a ratio of
        /// the crater's radius).</summary>
        public const float BlastRadiusFloorRatio = 0.35f;

        /// <summary>The blast radius added by the strength (same units).</summary>
        public const float BlastRadiusGainRatio = 0.45f;

        /// <summary>The density of the trail attached to one flying rock (this value for a
        /// large rock).</summary>
        public const float BlockTrailMagnitudeMax = 1.2f;

        /// <summary>The floor on the same (the smallest rock).</summary>
        public const float BlockTrailMagnitudeMin = 0.4f;

        /// <summary>The radius of the circle a flying rock spawns in (m). **The size of one
        /// rock.**</summary>
        public const float BlockTrailRadiusMetres = 9f;

        /// <summary>How long the dust cloud from an impact lasts (seconds).</summary>
        public const float ImpactSeconds = 0.9f;

        /// <summary>The density of an impact's dust cloud (this value for a large
        /// rock).</summary>
        public const float ImpactMagnitudeMax = 2.6f;

        /// <summary>The spread of an impact's dust cloud (m, for a large rock).</summary>
        public const float ImpactRadiusMetresMax = 34f;

        /// <summary>The blast's density.</summary>
        public static float BlastMagnitude(float unit)
        {
            return Lerp(BlastMagnitudeMin, BlastMagnitudeMax, Clamp01(unit));
        }

        /// <summary>The radius (m) of the circle the blast spawns in.</summary>
        public static float BlastRadiusMetres(float craterRadiusMetres, float unit)
        {
            return RadiusFrom(craterRadiusMetres, BlastRadiusFloorRatio,
                              BlastRadiusGainRatio, unit);
        }

        /// <summary>The density of a flying rock's trail. <paramref name="sizeUnit"/> is the
        /// rock's size.</summary>
        public static float BlockTrailMagnitude(float sizeUnit)
        {
            return Lerp(BlockTrailMagnitudeMin, BlockTrailMagnitudeMax, Clamp01(sizeUnit));
        }

        /// <summary>
        /// The density of an impact's dust cloud. Once <paramref name="ageSeconds"/> passes
        /// <see cref="ImpactSeconds"/> it returns <b>0</b>, so the caller need only draw
        /// while it is <c>&gt; 0</c>.
        /// </summary>
        public static float ImpactMagnitude(float sizeUnit, float ageSeconds)
        {
            if (IsBad(ageSeconds) || ageSeconds < 0f) return 0f;
            if (ageSeconds >= ImpactSeconds) return 0f;

            // A fast rise and a slow fade (how a dust cloud looks).
            float w = ageSeconds / ImpactSeconds;
            float shape = w < 0.15f ? w / 0.15f : (1f - w) / 0.85f;
            if (shape < 0f) shape = 0f;

            return ImpactMagnitudeMax * (0.4f + 0.6f * Clamp01(sizeUnit)) * shape;
        }

        /// <summary>The spread (m) of an impact's dust cloud. The larger the rock, the
        /// wider.</summary>
        public static float ImpactRadiusMetres(float sizeUnit)
        {
            float r = ImpactRadiusMetresMax * (0.35f + 0.65f * Clamp01(sizeUnit));
            return r < MinRadiusMetres ? MinRadiusMetres : r;
        }

        /// <summary>
        /// A radius below this is treated as "the crater could not be read" and not used (m).
        /// Pass 0 and particles still spawn, thanks to <c>max(100, PI r^2)</c>, so
        /// **letting a 0 through unchanged would make it erupt from a single point.**
        /// </summary>
        public const float MinRadiusMetres = 4f;

        /// <summary>The plume's density. <paramref name="unit"/> is the strength
        /// <c>[0,1]</c>.</summary>
        public static float PlumeMagnitude(float unit)
        {
            return Lerp(PlumeMagnitudeMin, PlumeMagnitudeMax, Clamp01(unit));
        }

        /// <summary>The radius (m) of the circle the plume spawns in.</summary>
        public static float PlumeRadiusMetres(float craterRadiusMetres, float unit)
        {
            return RadiusFrom(craterRadiusMetres, PlumeRadiusFloorRatio,
                              PlumeRadiusGainRatio, unit);
        }

        /// <summary>The flames' density.</summary>
        public static float FlameMagnitude(float unit)
        {
            return Lerp(FlameMagnitudeMin, FlameMagnitudeMax, Clamp01(unit));
        }

        /// <summary>The radius (m) of the circle the flames spawn in.</summary>
        public static float FlameRadiusMetres(float craterRadiusMetres, float unit)
        {
            return RadiusFrom(craterRadiusMetres, FlameRadiusFloorRatio,
                              FlameRadiusGainRatio, unit);
        }

        /// <summary>The radius (m) of the circle the ejecta spawn in. **Not varied by the
        /// strength** (it is the width of the crater's mouth).</summary>
        public static float EjectaRadiusMetres(float craterRadiusMetres)
        {
            return RadiusFrom(craterRadiusMetres, EjectaRadiusRatio, 0f, 0f);
        }

        /// <summary>
        /// The interval between ejecta (seconds). The stronger, the shorter.
        /// </summary>
        public static float EjectaPeriodSeconds(float unit)
        {
            return Lerp(EjectaPeriodMaxSeconds, EjectaPeriodMinSeconds, Clamp01(unit));
        }

        /// <summary>
        /// The phase (seconds, in <c>[0, period)</c>) obtained by folding the clock by the
        /// interval.
        /// **Neither a negative clock nor an interval of zero or below lets a NaN out.**
        /// </summary>
        public static float BurstPhaseSeconds(float clockSeconds, float periodSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;
            if (IsBad(periodSeconds) || periodSeconds <= 0f) return 0f;

            float t = clockSeconds - (float)System.Math.Floor(clockSeconds / periodSeconds)
                                     * periodSeconds;
            if (IsBad(t) || t < 0f) return 0f;
            if (t >= periodSeconds) return 0f;
            return t;
        }

        /// <summary>
        /// The ejecta's density. <b>It returns 0 while nothing is being thrown</b>, so the
        /// caller need only call <c>RenderEffect</c> while it is <c>&gt; 0</c>.
        ///
        /// The window is a hump (rising then falling) because with a rectangular switch you
        /// get a single frame of dense particles and **it looks like it is blinking**.
        /// </summary>
        public static float EjectaMagnitude(float unit, float phaseSeconds)
        {
            float u = Clamp01(unit);
            if (IsBad(phaseSeconds) || phaseSeconds < 0f) return 0f;
            if (phaseSeconds >= EjectaBurstSeconds) return 0f;

            // A 0 → 1 → 0 hump, peaking in the middle of the window.
            float w = phaseSeconds / EjectaBurstSeconds;
            float shape = 1f - System.Math.Abs(w * 2f - 1f);
            if (shape < 0f) shape = 0f;

            return Lerp(EjectaMagnitudeMin, EjectaMagnitudeMax, u) * shape;
        }

        private static float RadiusFrom(float craterRadiusMetres, float floorRatio,
                                        float gainRatio, float unit)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return MinRadiusMetres;

            float r = craterRadiusMetres * (floorRatio + gainRatio * Clamp01(unit));
            if (IsBad(r) || r < MinRadiusMetres) return MinRadiusMetres;
            return r;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
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
