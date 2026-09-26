namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// How much seismometer coverage extends the warning lead time.
    ///
    /// §A-2 of the IL facts document, the Emerging branch of
    /// <c>EarthquakeAI.SimulationStep</c>:
    /// <code>
    ///   CheckLocalResource(EarthquakeCoverage, m_targetPosition, out coverage)
    ///   coverage = Mathf.Min(coverage, 100)
    ///   lead     = coverage * 6437 / 100 + 1755      // integer division (div.un)
    ///   if (currentFrame + lead &gt;= m_activationFrame) DetectDisaster(id, located: coverage != 0)
    /// </code>
    ///
    /// At coverage 0 that is 1,755 frames (about 38.6 in-game minutes), at 100 it is 8,192
    /// frames (= 65536/8 = **exactly 3.0 in-game hours**).
    ///
    /// **Coverage is read at the epicentre** (<c>m_targetPosition</c>). Not at the
    /// seismometer's position, and not at the cursor's. A seismometer whose effective range
    /// does not reach the epicentre contributes nothing to that earthquake (§A-2 / §C-2).
    ///
    /// **The seismometer has a second effect.** Since <c>located</c> is decided by
    /// <c>coverage != 0</c>, without a seismometer the earthquake is never drawn on the
    /// hazard map at all (the gate in §A-6). That side is handled by
    /// <see cref="DisasterPhases.PaintsHazardMap(bool, EarthquakePhase)"/>.
    /// **The seismometer itself never calls <c>DetectDisaster</c>** (§C-2) — the caller is
    /// <c>EarthquakeAI.SimulationStep</c>, and all the seismometer does is scatter
    /// resource 22 within its radius. Do not read the causality backwards.
    ///
    /// This is built from **vanilla's formula and constants alone** (the first layer). There
    /// is not one piece of new physics in it.
    /// </summary>
    public static class WarningLeadTime
    {
        /// <summary>The lead time at coverage 0. The IL literal <c>ldc.i4 1755</c>.</summary>
        public const int BaseFrames = 1755;

        /// <summary>What gets added on at coverage 100. The IL literal
        /// <c>ldc.i4 6437</c>.</summary>
        public const int BonusFrames = 6437;

        /// <summary>The 100 in <c>Mathf.Min(coverage, 100)</c>.</summary>
        public const int MaxCoverage = 100;

        /// <summary>
        /// Vanilla's <c>Mathf.Min(coverage, 100)</c>. Negative values never occur in the
        /// game, but we clamp at 0 so that a broken reading cannot produce a negative lead
        /// time.
        /// </summary>
        public static int ClampCoverage(int coverage)
        {
            if (coverage < 0) return 0;
            if (coverage > MaxCoverage) return MaxCoverage;
            return coverage;
        }

        /// <summary>
        /// Coverage (the raw value is fine; we clamp inside) → lead time (frames).
        ///
        /// **It matters that this is integer division.** Write it in float and the rounding
        /// drifts from the game's, and you end up displaying a lead time the game does not
        /// actually use (for example, coverage 1 is 1,819, not 1,819.37).
        /// </summary>
        public static int FramesFor(int coverage)
        {
            return ClampCoverage(coverage) * BonusFrames / MaxCoverage + BaseFrames;
        }

        /// <summary>
        /// The lead time in in-game minutes.
        ///
        /// <paramref name="framesPerMinute"/> comes from the caller (on the Game side,
        /// <c>FeatureHost.FramesPerMinute</c>). **Never hard-code the constant** — we have
        /// form here: hard-coding it in ③ put every duration out by a factor of four.
        ///
        /// Returns 0 when the conversion cannot be done. Callers must not display that as
        /// "0 minutes"; they should drop the whole line (it is a guard against leaking NaN
        /// or infinity out of here, not a meaningful value of "0 minutes").
        /// </summary>
        public static float MinutesFor(int coverage, float framesPerMinute)
        {
            // NaN makes every comparison false, so reject it explicitly.
            if (float.IsNaN(framesPerMinute) || framesPerMinute <= 0f) return 0f;
            return FramesFor(coverage) / framesPerMinute;
        }
    }
}
