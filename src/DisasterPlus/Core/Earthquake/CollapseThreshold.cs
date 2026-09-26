namespace DisasterPlus.Core.Earthquake
{
    /// <summary>The two thresholds fixed for one building against this earthquake.
    /// Immutable.</summary>
    public struct BuildingThresholds
    {
        /// <summary>The collapse threshold. The first draw.</summary>
        public readonly int Collapse;

        /// <summary>The ignition threshold. The second draw.</summary>
        public readonly int Burn;

        public BuildingThresholds(int collapse, int burn)
        {
            Collapse = collapse;
            Burn = burn;
        }
    }

    /// <summary>
    /// **The centrepiece of this feature.** It reads ahead the very same thresholds vanilla
    /// is going to draw.
    ///
    /// §A-3 of the IL facts document, the building loop in
    /// DisasterHelpers.DestroyBuildings:
    ///   rnd  = new Randomizer(buildingID | (seed &lt;&lt; 16))   // seed is the disaster ID
    ///   fD   = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    ///   hitD = rnd.Int32(10000) &lt; fD * probability * 10000
    ///   hitB = rnd.Int32(10000) &lt; fB * probability * 10000
    ///
    /// **This seed depends on neither the frame nor the step.** It is constant for a
    /// (building, disaster) pair, so the five calls within the same step, and the next step
    /// too, all draw the same two values.
    /// Therefore, as far as the whole-quake disc is concerned, **whether a building falls is
    /// already decided the instant the earthquake starts**. The request's "collapses are
    /// probably random" was right in the sense that each building's threshold is a uniform
    /// random value, and wrong in the sense that there is no distance falloff.
    ///
    /// **The limit of what we can claim (callers must always state it alongside):**
    /// what we can produce here applies **only to the whole-quake disc (probability = 0.02,
    /// centred on the epicentre)**. The four discs along the fault have their positions
    /// re-drawn every step and, at probability = 1, all but certainly destroy whatever is
    /// there. Saying "this will not fall" about a building inside the fault band would be a
    /// confident, incorrect assertion. Use <c>FaultBand</c> (Task 4) to tell inside from
    /// outside, and state explicitly that the inside is judged separately.
    /// </summary>
    public static class CollapseThreshold
    {
        /// <summary>IL: rnd.Int32(10000). It is the UInt32 overload that gets
        /// called.</summary>
        public const uint Draws = 10000u;

        /// <summary>
        /// The whole-quake disc's probability (IL: ldc.r4 0.02). 2% at the epicentre.
        ///
        /// **This value is also Natural Disasters Renewal's fingerprint** (§E-2 of the IL
        /// facts document). NDR **replaces <c>DisasterHelpers.DestroyBuildings</c>
        /// entirely**, with a Prefix that returns false, and uses
        /// <c>probability == 0.02f</c> as the marker for a vanilla earthquake, swapping it
        /// for 0.04. So with NDR installed, any conclusion about collapse or ignition drawn
        /// from this constant is **neither vanilla's answer nor NDR's**.
        /// Set <c>damageModelReplaced</c> on <see cref="BuildingMargin.Evaluate"/> and
        /// withdraw the judgement altogether.
        /// </summary>
        public const float GlobalDiscProbability = 0.02f;

        /// <summary>
        /// IL: buildingID | (disasterID &lt;&lt; 16).
        /// Both are ushort, so we widen to int before combining them. Disaster IDs run 1-255
        /// so in practice it never goes negative, but if the top bit were set, the sign
        /// extension is handled by VanillaRandomizer's ctor (see its tests).
        /// </summary>
        public static int SeedFor(ushort buildingId, ushort disasterId)
        {
            return buildingId | (disasterId << 16);
        }

        /// <summary>
        /// The two thresholds fixed for this (building, disaster) pair.
        /// **The order of the draws matters** — the first is the collapse, the second the
        /// ignition. Do not swap them.
        /// </summary>
        public static BuildingThresholds For(ushort buildingId, ushort disasterId)
        {
            var rnd = new VanillaRandomizer(SeedFor(buildingId, disasterId));
            int collapse = rnd.Int32(Draws);
            int burn = rnd.Int32(Draws);
            return new BuildingThresholds(collapse, burn);
        }

        /// <summary>
        /// The IL's comparison itself:
        /// <c>threshold &lt; localFactor * probability * 10000</c>.
        /// The int is promoted to float for the comparison. Equality is not included.
        ///
        /// **This is a general comparison, whatever kind of disc it is.** Which disc's
        /// geometry localFactor (= the IL's fD) is computed from is the caller's
        /// responsibility; for the whole-quake disc, use <see cref="GlobalDiscHits"/>. The
        /// four fault discs have fD = <c>(2w - dist) / Max(1, 2w - w)</c>, a different
        /// formula from the whole-quake disc's <c>1 - d/R</c>, so mixing the two still
        /// produces numbers but they mean nothing.
        ///
        /// Note that for the four fault discs **there is no point calling this comparison at
        /// all**. <c>preRadius = w</c> culls <c>dist &gt;= w</c> first, and over what
        /// remains fD &gt; 1 and probability = 1, so the comparison is always true (see
        /// <see cref="FaultBand"/>'s doc).
        /// </summary>
        public static bool Hits(int threshold, float localFactor, float probability)
        {
            if (float.IsNaN(localFactor) || float.IsNaN(probability)) return false;
            return threshold < localFactor * probability * Draws;
        }

        /// <summary>
        /// The hit test for the whole-quake disc. <paramref name="localFactor"/> must be the
        /// s returned by <see cref="SeismicIntensity.At"/> (i.e. <c>1 - d/R</c>).
        /// This entry point exists so the caller does not get to choose the probability;
        /// always use it as a pair with <see cref="GlobalDiscCollapseDistance"/>.
        /// </summary>
        public static bool GlobalDiscHits(int threshold, float localFactor)
        {
            return Hits(threshold, localFactor, GlobalDiscProbability);
        }

        /// <summary>
        /// The epicentral distance at which this building starts to collapse **under the
        /// whole-quake disc**. Zero or below means it will not fall at any distance.
        ///
        ///   threshold &lt; (1 - d/R) * 0.02 * 10000
        ///   d &lt; R * (1 - threshold / (0.02 * 10000))
        ///
        /// This is not a "prophecy" but a **fact** derived from values vanilla has already
        /// decided. For the whole-quake disc only, though (see the limits in the class doc).
        ///
        /// ── Why it does not take probability as an argument (a deliberate design) ──────
        ///
        /// The signature used to be <c>CollapseDistance(threshold, intensity, probability)</c>,
        /// but it **hard-coded the ramp's denominator as <c>R = RadiusOf(intensity)</c>**. R
        /// is the whole-quake disc's geometry itself. The four fault discs have a
        /// <c>min = w, max = 2w</c> ramp around **a different centre** that is re-drawn every
        /// step, so passing <c>probability = 1</c> to that signature returned "a
        /// plausible-looking distance that means nothing". Writing the prohibition in the doc
        /// is not enough — someone will pass it one day. **We removed the argument to make it
        /// structurally impossible.** If you need the fault band's reach, use
        /// <see cref="FaultBand.PatchRadiusAt"/> (which is a different model).
        /// </summary>
        public static float GlobalDiscCollapseDistance(int threshold, byte intensity)
        {
            return GlobalDiscHitDistance(threshold, intensity);
        }

        /// <summary>
        /// The epicentral distance at which **ignition** starts. For the whole-quake disc
        /// <c>burnRadiusMin = 0, burnRadiusMax = R</c> (§A-3), so <c>fB = 1 - d/R</c>, which
        /// is **the same formula** as the collapse ramp. The only difference is the threshold
        /// drawn (the second draw).
        ///
        /// **Collapse takes priority.** The IL is <c>else if (hitB &amp;&amp; ...)</c>, so if
        /// the same building also hits on collapse we never reach this branch (the collapse
        /// side takes <c>burnAmount = Round(fB*255)</c> with it).
        /// The display side states that ordering alongside.
        /// </summary>
        public static float GlobalDiscBurnDistance(int threshold, byte intensity)
        {
            return GlobalDiscHitDistance(threshold, intensity);
        }

        /// <summary>
        /// For the whole-quake disc's ramp (<c>1 - d/R</c>) at probability 0.02, the
        /// epicentral distance at which threshold <paramref name="threshold"/> starts to
        /// hit.
        ///
        ///   threshold &lt; (1 - d/R) * 0.02 * 10000
        ///   d &lt; R * (1 - threshold / (0.02 * 10000))
        /// </summary>
        private static float GlobalDiscHitDistance(int threshold, byte intensity)
        {
            // A constant, so it is never 0 or NaN. No guard on the denominator is needed.
            const float denominator = GlobalDiscProbability * Draws;

            float d = SeismicIntensity.RadiusOf(intensity) * (1f - threshold / denominator);
            return d > 0f ? d : 0f;
        }
    }
}
