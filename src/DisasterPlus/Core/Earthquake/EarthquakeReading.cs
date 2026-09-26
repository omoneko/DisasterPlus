using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// An immutable set of readings for one earthquake in progress.
    /// It plays the same role as ①'s <c>ForecastReading</c>: the sim thread builds it and
    /// the main thread reads it.
    ///
    /// **Nothing but "values that were read" goes in here.** No estimates, no predictions
    /// (those are the display side's and the second layer's job). The one exception is
    /// <see cref="Radius"/>, which is vanilla's own formula determined uniquely by
    /// <see cref="Intensity"/>, so it is a computed property to avoid holding the radius in
    /// two places.
    /// </summary>
    public class EarthquakeReading
    {
        /// <summary>The index into the disaster buffer. Used as the identifier in
        /// diagnostics and logs.</summary>
        public readonly ushort DisasterId;

        /// <summary>The epicentre (<c>DisasterData.m_targetPosition</c>).</summary>
        public readonly Vec3 Epicentre;

        /// <summary>The fault's orientation (<c>DisasterData.m_angle</c>, radians).</summary>
        public readonly float AngleRadians;

        /// <summary><c>DisasterData.m_intensity</c>. Vanilla's random spawns use 10-100;
        /// this mod has unlocked it up to 255.</summary>
        public readonly byte Intensity;

        public readonly EarthquakePhase Phase;

        /// <summary>
        /// Whether it has been located. The only thing that sets this for an earthquake is
        /// <c>EarthquakeCoverage != 0</c> at the epicentre — that is, **the seismometer and
        /// nothing else** (§A-2 / §C-2 of the IL facts document).
        /// If false, this earthquake is never painted onto the hazard map at all (§A-6).
        /// </summary>
        public readonly bool Located;

        public readonly uint StartFrame;

        /// <summary>
        /// The scheduled (or observed) activation frame.
        /// **0 means "not decided yet", not "now".** Always check
        /// <see cref="ActivationScheduled"/> first.
        /// </summary>
        public readonly uint ActivationFrame;

        /// <summary>
        /// <c>m_activationFrame != 0</c>. Exactly the trap in §A-1.
        ///
        /// For an earthquake without <c>SelfTrigger(64)</c> set,
        /// <c>EarthquakeAI.StartDisaster</c> returns immediately, so
        /// <c>m_activationFrame</c> stays 0, <c>IsStillEmerging</c> always returns true via
        /// <c>m_activationFrame == 0</c>, and it **freezes in Emerging forever**. Feed that
        /// 0 straight into a time calculation and you get numbers like "4,739 years to go",
        /// so the display side must always branch here.
        /// </summary>
        public readonly bool ActivationScheduled;

        /// <summary>
        /// The **raw value** of
        /// <c>ImmaterialResourceManager.Resource.EarthquakeCoverage</c> at the epicentre.
        /// Vanilla's warning formula uses <c>Min(coverage, 100)</c>, but we do not clamp
        /// here (so that diagnostics can show both the raw value and the displayed one; the
        /// clamping is done by Task 7's <c>WarningLeadTime</c>).
        ///
        /// When <see cref="CoverageKnown"/> is false this value is meaningless.
        /// </summary>
        public readonly int CoverageAtEpicentre;

        /// <summary>
        /// Whether the coverage could actually be read.
        ///
        /// This field is not in the plan's list of types, but it is added for the same
        /// reason as ①'s <c>WeatherSnapshot.DisasterInfoAvailable</c>. A coverage of 0 is
        /// **a meaningful measurement** — "there is no seismometer" — and returning the same
        /// 0 when the read fails would make the two indistinguishable from the outside: an
        /// invented zero. "The hazard map is empty because you have no seismometer" is the
        /// single most important explanation this feature produces, so we must not hide the
        /// fact that the evidence behind it could not be read.
        /// </summary>
        public readonly bool CoverageKnown;

        /// <summary>
        /// This earthquake's fault length <c>L = m_crackLength * (0.5 + intensity*0.005)</c>
        /// (§A-3). 0 (= unknown) when the prefab could not be resolved.
        /// </summary>
        public readonly float CrackLength;

        /// <summary>Likewise the fault width
        /// <c>W = m_crackWidth * (0.5 + intensity*0.005)</c>. 0 if unknown.</summary>
        public readonly float CrackWidth;

        public EarthquakeReading(ushort disasterId, Vec3 epicentre, float angleRadians,
                                 byte intensity, EarthquakePhase phase, bool located,
                                 uint startFrame, uint activationFrame, bool activationScheduled,
                                 int coverageAtEpicentre, bool coverageKnown,
                                 float crackLength, float crackWidth)
        {
            DisasterId = disasterId;
            Epicentre = epicentre;
            AngleRadians = angleRadians;
            Intensity = intensity;
            Phase = phase;
            Located = located;
            StartFrame = startFrame;
            ActivationFrame = activationFrame;
            ActivationScheduled = activationScheduled;
            CoverageAtEpicentre = coverageAtEpicentre;
            CoverageKnown = coverageKnown;
            CrackLength = crackLength;
            CrackWidth = crackWidth;
        }

        /// <summary>The whole-quake disc radius R. Simply returns
        /// <see cref="SeismicIntensity.RadiusOf"/>.</summary>
        public float Radius
        {
            get { return SeismicIntensity.RadiusOf(Intensity); }
        }
    }
}
