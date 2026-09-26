using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// "Why is no fire whirl appearing right now", boiled down to a single value.
    ///
    /// ── Why it is needed ────────────────────────────────────────
    ///
    /// ③ has **no route other than spontaneous formation** (the manual placement tool was
    /// removed). So "nothing appears" is the default state, and playtesting produced an
    /// actual report of getting nothing but the single line
    ///
    ///   DIAG fireWhirl: burning=0 active=0
    ///
    /// with no way to tell "is it broken, or is there simply not enough fire yet".
    /// **A diagnostic that cannot separate those two is not a diagnostic.**
    ///
    /// This is filled in by <see
    /// cref="FireWhirlDetector.Detect(System.Collections.Generic.IList{BurningBuilding},
    /// FireWhirlConfig, System.Collections.Generic.IList{Vec2}, out FireWhirlProspect)"/>
    /// **within the same single pass as the decision itself**. Count it again in a separate
    /// pass and the diagnostic disagrees with the real decision — which is the worst kind
    /// of diagnostic there is.
    ///
    /// An engine-free value type. The wording of <see cref="Describe"/> is pinned by unit tests.
    /// </summary>
    public struct FireWhirlProspect
    {
        /// <summary>The total number of buildings burning in the most recently completed
        /// sweep.</summary>
        public readonly int BurningCount;

        /// <summary>
        /// **The size of the densest cluster** (within the detection radius, counting
        /// itself). If it has not reached <see cref="RequiredCount"/>, that is "the reason
        /// nothing appears".
        /// </summary>
        public readonly int DensestCount;

        /// <summary>The centroid of the densest cluster. Meaningless when
        /// <see cref="DensestCount"/> is 0.</summary>
        public readonly Vec2 DensestCentre;

        /// <summary>The detection radius (m). The thresholds travel with it so that a single
        /// diagnostic line makes sense on its own.</summary>
        public readonly float RadiusMetres;

        /// <summary>The number of buildings required.</summary>
        public readonly int RequiredCount;

        /// <summary>
        /// The number of candidates that met the conditions but were discarded for being
        /// too close to a live fire whirl or to a spot still on cooldown. **The one and
        /// only explanation for "there is plenty of fire and still nothing appears".**
        /// </summary>
        public readonly int SuppressedCount;

        /// <summary>The number of candidates actually accepted this tick.</summary>
        public readonly int AcceptedCount;

        public FireWhirlProspect(int burningCount, int densestCount, Vec2 densestCentre,
                                 float radiusMetres, int requiredCount,
                                 int suppressedCount, int acceptedCount)
        {
            BurningCount = burningCount;
            DensestCount = densestCount;
            DensestCentre = densestCentre;
            RadiusMetres = radiusMetres;
            RequiredCount = requiredCount;
            SuppressedCount = suppressedCount;
            AcceptedCount = acceptedCount;
        }

        /// <summary>How many more buildings are needed. 0 if there are enough.</summary>
        public int Shortfall
        {
            get
            {
                int missing = RequiredCount - DensestCount;
                return missing > 0 ? missing : 0;
            }
        }

        /// <summary>
        /// One line of diagnostics (in English). **It always distinguishes "the conditions
        /// are not met" from "it is broken".**
        ///
        /// This function never says "it is broken" — whoever knows whether something is
        /// broken is the caller (prefab resolution, or detecting that the spread came up
        /// empty). All this can speak to are <b>the facts on the conditions side</b>, and
        /// stating those outright is what pushes "the conditions are met and still nothing
        /// happens" out to the rest of the diagnostics.
        /// </summary>
        public string Describe()
        {
            if (BurningCount <= 0)
            {
                return "nothing is on fire; a fire whirl needs " + RequiredCount
                     + " buildings burning within " + Format(RadiusMetres) + " m of each other";
            }

            if (DensestCount < RequiredCount)
            {
                return BurningCount + " burning, but the densest group is only " + DensestCount
                     + " within " + Format(RadiusMetres) + " m (need " + RequiredCount
                     + "; " + Shortfall + " more, or a wider radius / lower count in the settings)";
            }

            if (AcceptedCount <= 0 && SuppressedCount > 0)
            {
                return "the fire is dense enough (" + DensestCount + " within "
                     + Format(RadiusMetres) + " m), but " + SuppressedCount
                     + " candidate(s) were too close to a live fire whirl or a spot still cooling down";
            }

            if (AcceptedCount <= 0)
            {
                // Dense enough, and not rejected by the separation rule either. If it is
                // stuck past this point, the cause is not on the conditions side (it is the
                // prefab, or a failed spawn). **Say so outright.**
                return "the fire is dense enough (" + DensestCount + " within "
                     + Format(RadiusMetres) + " m) and nothing suppressed it; "
                     + "if no fire whirl appears, the cause is not the fire conditions";
            }

            return AcceptedCount + " spawn point(s) met the conditions this pass ("
                 + DensestCount + " within " + Format(RadiusMetres) + " m)";
        }

        private static string Format(float metres)
        {
            return metres.ToString("F0");
        }
    }
}
