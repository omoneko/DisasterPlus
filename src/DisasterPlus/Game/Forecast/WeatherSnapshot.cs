using DisasterPlus.Core.Forecast;

namespace DisasterPlus.Game
{
    /// <summary>
    /// An immutable snapshot built on the sim thread and read on the main thread.
    /// Once built it is never written to again.
    /// </summary>
    public class WeatherSnapshot
    {
        public readonly ForecastReading Temperature;
        public readonly ForecastReading Rain;
        public readonly ForecastReading Cloud;
        public readonly ForecastReading Fog;
        public readonly float WindDegrees;
        public readonly float DisasterProbability;
        public readonly int DisasterCooldown;

        /// <summary>
        /// The number of thunderstorms that are currently both "located" (Located) and
        /// under way (Emerging|Active).
        ///
        /// Why we carry this (the most important finding of the full review, confirmed by
        /// reading the IL): vanilla's hazard map is **not a static risk surface**.
        /// <c>ThunderStormAI.UpdateHazardMap</c> and <c>TornadoAI.UpdateHazardMap</c> both
        /// open with the same two instruction blocks, a gate:
        ///   <c>(m_flags &amp; 4096) == 0 -&gt; return</c> (4096 = DisasterData.Flags.Located)
        ///   <c>(m_flags &amp; 12)   == 0 -&gt; return</c> (12 = Emerging|Active)
        /// and neither one looks at the terrain or the buildings at all (checked by
        /// sweeping the whole IL). Only if it gets past the gate does it paint a disc
        /// around m_targetPosition whose size comes from the radius and the intensity.
        /// On top of that, <c>DisasterManager.UpdateTexture</c> fills all 256x256 cells
        /// with zero every time before writing in only the disasters that passed the gate.
        ///
        /// So what the grid holds is "the predicted damage area of storms the radar has
        /// located and that are under way", and if there is not a single such storm
        /// **every cell is 0**.
        ///
        /// Without this count, a player who owns the DLC but has not built a weather radar
        /// presses "show on map", puts the cursor anywhere in the city and reads
        /// "Lightning: 0". <c>SampleAt</c> returns ok=true, because the submode does match
        /// and the grid really is there — it is just that its contents are all zero. The
        /// player concludes "there is no lightning risk anywhere", when the truth is "no
        /// storm is detected right now". That is exactly the sort of confidently wrong
        /// number this feature was written to prevent.
        ///
        /// When <see cref="DisasterInfoAvailable"/> is false this value is meaningless
        /// (we could not read it, so it stays 0). Callers must always check that first.
        /// </summary>
        public readonly int LocatedLightningStorms;

        /// <summary>
        /// The number of tornadoes that are currently both located and under way.
        /// The meaning and the caveats are the same as for
        /// <see cref="LocatedLightningStorms"/>.
        ///
        /// A note from reading the IL: <c>TornadoAI.SimulationStep</c> calls
        /// <c>DisasterManager.DetectDisaster(disasterID, located: false)</c> itself
        /// (IL_0038 is <c>ldc.i4.0</c>). <c>ThunderStormAI</c> never calls
        /// <c>DetectDisaster</c> at all. The only callers that pass <c>located: true</c>
        /// are <c>ProduceGoods</c> on <c>WeatherRadarAI</c> / <c>SpaceRadarAI</c> /
        /// <c>TsunamiBuoyAI</c>, <c>FirewatchTowerAI.NearObjectInFire</c>,
        /// <c>SinkholeAI.SimulationStep</c>, and <c>DisasterWrapper.DetectDisaster</c>
        /// (the scripting API for mods and scenarios) — and of those, the only thing that
        /// can set Located on a thunderstorm or a tornado is **the weather radar
        /// (WeatherRadarAI)**.
        /// </summary>
        public readonly int LocatedTornadoes;

        /// <summary>
        /// Whether DisasterProbability / DisasterCooldown / LocatedLightningStorms /
        /// LocatedTornadoes were actually read from DisasterManager.
        ///
        /// Review finding (feedback from the coordinator): when DisasterManager was absent,
        /// DisasterProbability used to be returned silently as 0f. That is a
        /// **fabricated zero** — outwardly indistinguishable from a genuine reading of
        /// "0% probability" — and it is the same kind of confidently wrong number as
        /// putting a hazard figure under the label of a type that is not on display. This
        /// flag lets the caller tell "could not read it (unknown)" apart from "read it and
        /// it was 0". When it is false the panel leaves the probability line out entirely
        /// (it does not print 0.0%).
        ///
        /// The same reasoning applies to the located-storm counts. Stating flatly that
        /// "no storm is detected" while this is false would itself be an assertion that
        /// hides the fact that we could not read anything, so the panel must fall back to
        /// the generic "unknown".
        /// </summary>
        public readonly bool DisasterInfoAvailable;

        /// <summary>Whether the read succeeded. If false the panel says "cannot
        /// read".</summary>
        public readonly bool Valid;

        public WeatherSnapshot(ForecastReading temperature, ForecastReading rain,
                               ForecastReading cloud, ForecastReading fog,
                               float windDegrees,
                               float disasterProbability, int disasterCooldown,
                               int locatedLightningStorms, int locatedTornadoes,
                               bool disasterInfoAvailable, bool valid)
        {
            Temperature = temperature;
            Rain = rain;
            Cloud = cloud;
            Fog = fog;
            WindDegrees = windDegrees;
            DisasterProbability = disasterProbability;
            DisasterCooldown = disasterCooldown;
            LocatedLightningStorms = locatedLightningStorms;
            LocatedTornadoes = locatedTornadoes;
            DisasterInfoAvailable = disasterInfoAvailable;
            Valid = valid;
        }

        public static WeatherSnapshot Invalid()
        {
            var zero = new ForecastReading(0f, 0f, TrendMath.DefaultDeadband);
            return new WeatherSnapshot(zero, zero, zero, zero, 0f, 0f, 0, 0, 0, false, false);
        }
    }
}
