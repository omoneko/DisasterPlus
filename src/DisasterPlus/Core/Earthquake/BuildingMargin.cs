using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>The conclusion for this building against this earthquake.</summary>
    public enum CollapseVerdict
    {
        /// <summary>The fault's geometry could not be read, so no verdict can be
        /// given.</summary>
        Unknown,

        WillCollapse,

        /// <summary>It does not collapse **under the whole-quake disc**. Only claim this
        /// outside the fault band.</summary>
        Survives,

        /// <summary>Inside the fault band. The destruction discs judge it separately, so we
        /// do not assert survival.</summary>
        InsideFaultZone,

        AlreadyDown,

        /// <summary>The epicentral distance is R or more. Vanilla does not even run the
        /// test.</summary>
        OutOfRange,

        /// <summary>
        /// The destruction code itself has been replaced by another mod.
        ///
        /// Natural Disasters Renewal **replaces <c>DisasterHelpers.DestroyBuildings</c>
        /// entirely, with a Prefix that returns false**, and uses
        /// <c>probability == 0.02f</c> as the marker for a vanilla earthquake, then uses
        /// 0.04 (§E-2 of the IL facts document). In other words, in that environment the
        /// 0.02 ramp this mod reads is **a formula nobody is executing**.
        ///
        /// Saying "it will not fall" here would mean asserting, while claiming to have
        /// measured it, the opposite of what happens to a building that will in fact fall
        /// (at intensity 55 a building with tD = 300 falls out to 775 m under NDR).
        /// **We withdraw the verdict, not the numbers.**
        /// </summary>
        DamageModelReplaced,
    }

    /// <summary>
    /// The "margin" for one building. **It is not a prophecy.**
    ///
    /// For each building vanilla draws two fixed thresholds from
    /// new Randomizer(buildingID | (disasterID &lt;&lt; 16)) and destroys it when the local
    /// factor × probability exceeds them (§A-3 of the IL facts document). That seed depends
    /// on neither the frame nor the step, so by rebuilding the same seed **we can know here,
    /// in advance, the values vanilla is about to draw**.
    /// And since the whole-quake disc's epicentre does not move, **whether a building falls
    /// is already decided the instant the earthquake starts**. That is the answer to the
    /// request's "collapses are probably random".
    ///
    /// **The limits (callers must always state them alongside):** what we can answer here is
    /// only about the whole-quake disc (probability = 0.02, centred on the epicentre). The
    /// four discs along the fault have their positions re-drawn every step and destroy at
    /// probability = 1, so inside the band we must not say "it will not fall".
    /// See <see cref="CollapseVerdict.InsideFaultZone"/>.
    ///
    /// **Never restate "unknown" as "outside".** The fault's geometry rides on the prefab's
    /// m_crackLength / m_crackWidth, and their actual values are not in the DLL (§A-0).
    /// When they cannot be read, <see cref="FaultBand.Known"/> is false and this returns
    /// <see cref="CollapseVerdict.Unknown"/>. **We can still give the thresholds and the
    /// distances; it is only the verdict we cannot give** — without knowing inside from
    /// outside the band, there is no basis for claiming the whole-quake disc's conclusion as
    /// this building's conclusion.
    ///
    /// **The same treatment applies when another mod has replaced the destruction code.**
    /// Natural Disasters Renewal replaces <c>DisasterHelpers.DestroyBuildings</c> wholesale
    /// (§E-2), and then the 0.02 ramp we read here is **not being executed anywhere**. Set
    /// <c>damageModelReplaced</c> and the verdict becomes
    /// <see cref="CollapseVerdict.DamageModelReplaced"/>, with the distances withheld too.
    /// ②'s policy that "this mod does not go through <c>DisasterHelpers</c>" is **about the
    /// side that writes damage** and **has no effect whatsoever on the side that reads it**.
    /// </summary>
    public struct BuildingMargin
    {
        public readonly ushort BuildingId;

        /// <summary>The horizontal distance from the epicentre.</summary>
        public readonly float Distance;

        /// <summary>The whole-quake disc's local factor s (= vanilla's fD).</summary>
        public readonly float LocalFactor;

        public readonly int CollapseThresholdValue;
        public readonly int BurnThresholdValue;

        /// <summary>
        /// Inside this distance the building collapses under the whole-quake disc. 0 means it
        /// does not collapse at any distance.
        /// **When <see cref="Verdict"/> is
        /// <see cref="CollapseVerdict.DamageModelReplaced"/> this holds 0** — a distance
        /// derived from vanilla's 0.02 is a number nobody is using in that environment.
        /// </summary>
        public readonly float CollapseWithin;

        /// <summary>
        /// Inside this distance the building catches fire under the whole-quake disc. 0 means
        /// it does not catch fire at any distance.
        /// The same ramp and the same probability as the collapse; only the threshold differs
        /// (§A-3).
        /// </summary>
        public readonly float BurnWithin;

        public readonly CollapseVerdict Verdict;

        /// <summary>
        /// **The ignition verdict.** The answer to "fires from the shaking", which the request
        /// named explicitly, and the raw material (the second draw) was in
        /// <see cref="BurnThresholdValue"/> from the start.
        ///
        /// We reuse <see cref="CollapseVerdict"/> because the structure of the branching is
        /// exactly the same as for collapse (<see cref="CollapseVerdict.WillCollapse"/> here
        /// means "the second draw hits" = it catches fire). The display side should read it
        /// with ignition wording.
        ///
        /// **Collapse takes priority.** The IL is <c>else if (hitB &amp;&amp; ...)</c>, so if
        /// the same building also hits on collapse we never get here. The display side states
        /// that ordering alongside too.
        /// </summary>
        public readonly CollapseVerdict BurnVerdict;

        private BuildingMargin(ushort buildingId, float distance, float localFactor,
                               int collapseThreshold, int burnThreshold,
                               float collapseWithin, float burnWithin,
                               CollapseVerdict verdict, CollapseVerdict burnVerdict)
        {
            BuildingId = buildingId;
            Distance = distance;
            LocalFactor = localFactor;
            CollapseThresholdValue = collapseThreshold;
            BurnThresholdValue = burnThreshold;
            CollapseWithin = collapseWithin;
            BurnWithin = burnWithin;
            Verdict = verdict;
            BurnVerdict = burnVerdict;
        }

        /// <summary>There is no building under the cursor, or no earthquake.</summary>
        public static BuildingMargin None()
        {
            return new BuildingMargin(0, 0f, 0f, 0, 0, 0f, 0f,
                                      CollapseVerdict.Unknown, CollapseVerdict.Unknown);
        }

        /// <summary>
        /// Whether a building has been identified. The only way to tell this apart from
        /// <see cref="None"/>. Building ID 0 is CS's empty slot, so it never collides with a
        /// real building.
        /// </summary>
        public bool HasBuilding { get { return BuildingId != 0; } }

        /// <summary>
        /// <paramref name="damageModelReplaced"/> means "<c>DisasterHelpers.DestroyBuildings</c>
        /// has been entirely replaced by another mod" (in practice, Natural Disasters
        /// Renewal, §E-2).
        /// **When it is true this function gives no conclusion at all.** Giving one would
        /// mean asserting, while claiming to have measured it, the answer of a formula nobody
        /// is executing.
        /// </summary>
        public static BuildingMargin Evaluate(ushort buildingId, ushort disasterId,
                                              Vec2 buildingPos, Vec2 epicentre,
                                              byte intensity, FaultBand band, bool alreadyDown,
                                              bool damageModelReplaced)
        {
            float dx = buildingPos.X - epicentre.X;
            float dz = buildingPos.Z - epicentre.Z;
            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

            var thresholds = CollapseThreshold.For(buildingId, disasterId);
            float local = SeismicIntensity.At(distance, intensity);

            // ★ If another mod has replaced the destruction code, give neither distances nor
            //    a verdict. Do not check this before AlreadyDown — "it is already down" is
            //    the building's current state, not a prediction of what is about to happen,
            //    so it is correct no matter which mod is computing the destruction.
            if (!alreadyDown && damageModelReplaced)
            {
                return new BuildingMargin(buildingId, distance, local,
                                          thresholds.Collapse, thresholds.Burn, 0f, 0f,
                                          CollapseVerdict.DamageModelReplaced,
                                          CollapseVerdict.DamageModelReplaced);
            }

            // Use the entry point that leaves no room to pass a probability. The four fault
            // discs use a different ramp (min = w, max = 2w, and their centres are re-drawn
            // every step), so this distance only means anything for the whole-quake disc.
            float within = CollapseThreshold.GlobalDiscCollapseDistance(
                thresholds.Collapse, intensity);
            float burnWithin = CollapseThreshold.GlobalDiscBurnDistance(
                thresholds.Burn, intensity);

            // Inside-or-outside the band gives the same answer for collapse and ignition, so
            // we only ask once (FaultBand.Contains scans the whole range of u, so it is not
            // a cheap call).
            bool insideBand = band.Known && band.Contains(buildingPos);

            var verdict = VerdictFor(thresholds.Collapse, distance, local,
                                     intensity, band.Known, insideBand, alreadyDown);
            var burnVerdict = VerdictFor(thresholds.Burn, distance, local,
                                         intensity, band.Known, insideBand, alreadyDown);

            return new BuildingMargin(buildingId, distance, local,
                                      thresholds.Collapse, thresholds.Burn,
                                      within, burnWithin, verdict, burnVerdict);
        }

        /// <summary>
        /// The conclusion for one threshold. Shared between collapse and ignition because
        /// **the structure of the branching is the same** (the whole-quake disc's fD and fB
        /// are both <c>1 - d/R</c>, and the probability is the same 0.02).
        /// The order of the branches is meaningful, so do not rearrange them.
        /// </summary>
        private static CollapseVerdict VerdictFor(int threshold, float distance, float local,
                                                  byte intensity, bool bandKnown, bool insideBand,
                                                  bool alreadyDown)
        {
            if (alreadyDown) return CollapseVerdict.AlreadyDown;

            // Outside the first-pass cull by preRadius. Vanilla does not even draw a random
            // number.
            if (!SeismicIntensity.IsInside(distance, intensity)) return CollapseVerdict.OutOfRange;

            // We do not know inside from outside the band, so we assert neither survival nor
            // collapse.
            if (!bandKnown) return CollapseVerdict.Unknown;

            // ★ The very fact that this branch sits ahead of the only route to Survives is
            //    what guarantees "never tell a building inside the fault band that it will
            //    not fall". Do not move it below the two branches under it. Inside the fault
            //    band the probability = 1 destruction discs judge it separately, and the
            //    whole-quake disc's thresholds say nothing at all about that judgement.
            if (insideBand) return CollapseVerdict.InsideFaultZone;

            return CollapseThreshold.GlobalDiscHits(threshold, local)
                ? CollapseVerdict.WillCollapse
                : CollapseVerdict.Survives;
        }
    }
}
