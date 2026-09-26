using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The three tuning values burnt into the <c>ThunderStormAI</c> prefab.
    ///
    /// ★ **This once also carried <c>VortexAI</c>'s three values** (the accompanying
    /// tornado's destruction radii and the vortex vehicle's <c>m_maxSpeed</c>). The
    /// accompanying tornado was retired and tornado-grade damage is now produced by
    /// <c>TyphoonGust</c> itself, so **do not keep values here that not one place reads
    /// any more** — an unused number sitting in the diagnostics gets read by the next
    /// person as "this is having an effect".
    ///
    /// **The actual values of these six do not exist in the DLL** (IL facts document §A-0
    /// and §B-1, both judged PARTIAL). They are prefab serialised values, so IL
    /// disassembly cannot see them, and **reading them at runtime through
    /// <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> and putting them in the
    /// diagnostics dump is the only way to obtain them** — which is the main purpose of
    /// Task 2. Everything ④ does later — duration, number of strikes, destruction radius,
    /// travel speed — rides on top of them.
    ///
    /// It is a struct so that caching it brings none of Unity's fake-null self-repair
    /// problem with it (holding nothing but floats and uints, it never has to keep a
    /// reference to a <c>DisasterInfo</c> or a <c>VehicleInfo</c>). Both defaults are
    /// false, i.e. "not read yet / no longer readable".
    /// </summary>
    public struct TyphoonPrefabFacts
    {
        /// <summary>Whether the storm's three values could be read. When false the three
        /// below are 0 and mean nothing.</summary>
        public readonly bool StormResolved;

        /// <summary><c>ThunderStormAI.m_radius</c>. The basis for the lightning scatter
        /// radius and the hazard disc (§A-1 / §A-2).</summary>
        public readonly float StormRadius;

        /// <summary><c>ThunderStormAI.m_emergingDuration</c> (frames).</summary>
        public readonly uint EmergingDuration;

        /// <summary>
        /// <c>ThunderStormAI.m_activeDuration</c> (frames).
        /// **A typhoon cannot outlive this** (<c>IsStillActive</c>, §A-1).
        /// The travel speed can only be derived from this value
        /// (<c>TyphoonTrack.SpeedFor</c>).
        /// </summary>
        public readonly uint ActiveDuration;

        public TyphoonPrefabFacts(bool stormResolved, float stormRadius,
                                  uint emergingDuration, uint activeDuration)
        {
            StormResolved = stormResolved;
            StormRadius = stormRadius;
            EmergingDuration = emergingDuration;
            ActiveDuration = activeDuration;
        }

        /// <summary>
        /// Whether we may raise a typhoon at all.
        ///
        /// With a radius of 0 both the storm radius and the gale radius come out 0
        /// (<c>TyphoonProfile.StormRadiusOf</c>), and with a duration of 0 the speed comes
        /// out 0 (<c>TyphoonTrack.SpeedFor</c>).
        /// **Both are places where "never substitute a guessed value" is guaranteed
        /// structurally**, so when this is false the caller raises no typhoon and puts the
        /// reason in the diagnostics (design doc §6).
        /// </summary>
        public bool Usable
        {
            get { return StormResolved && StormRadius > 0f && ActiveDuration > 0u; }
        }
    }

    /// <summary>
    /// An immutable snapshot built on the sim thread and read on the main thread.
    /// Same discipline as ①'s <c>WeatherSnapshot</c> and ②'s
    /// <see cref="EarthquakeSnapshot"/>: **once built, never rewritten**.
    ///
    /// **T3 onwards add fields. Always append them at the end of the ctor** (so existing
    /// call sites do not all have to be changed).
    ///
    /// ④'s display convention: of the values here, **only <see cref="Rain"/> /
    /// <see cref="Cloud"/> / <see cref="Fog"/> / <see cref="WindDirectionDegrees"/> are
    /// vanilla measurements**; everything else is a quantity this mod decided
    /// (design doc §1.2 / §7).
    /// </summary>
    public class TyphoonSnapshot
    {
        /// <summary>Whether the read succeeded. If false the display side shows "cannot be
        /// read".</summary>
        public readonly bool Valid;

        public readonly TyphoonPrefabFacts Prefab;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>.</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// <c>WeatherManager.m_currentRain</c>. **One of the two values in ④ allowed to
        /// claim <c>[measured]</c>.**
        /// When <see cref="WeatherReadable"/> is false this value is meaningless (do not
        /// mix it up with 0).
        /// </summary>
        public readonly float Rain;

        /// <summary><c>WeatherManager.m_currentCloud</c>. The other
        /// <c>[measured]</c>.</summary>
        public readonly float Cloud;

        /// <summary><c>WeatherManager.m_currentFog</c>. Diagnostics only (not shown on the
        /// panel).</summary>
        public readonly float Fog;

        /// <summary><c>WeatherManager.m_windDirection</c> (degrees, normalised to
        /// -180 to 180).</summary>
        public readonly float WindDirectionDegrees;

        /// <summary>
        /// <c>WeatherManager.m_enableWeather</c>.
        ///
        /// **false is not a fault but a legitimate player setting.** Note though that in
        /// such an environment both rain and cloud are crushed to 0 unless
        /// <c>m_forceWeatherOn</c> is written every tick (§A-4), so T4's weather driving
        /// looks at this value and behaves differently. The display side must not hide it.
        /// </summary>
        public readonly bool WeatherEnabled;

        /// <summary>
        /// Whether the four weather values could actually be read.
        /// **The flag that keeps a 0 we failed to read apart from a 0 that really was 0**
        /// (a discipline ① and ② established over and over).
        /// </summary>
        public readonly bool WeatherReadable;

        // ── T3: the state of the typhoon itself ─────────────────────────────
        //
        // **This is the previous tick's state.** TyphoonReader.Read() runs at the top of
        // TyphoonFeature.OnSimulationTick (i.e. before TyphoonController.Tick), so what
        // goes in here is the value from one tick ago. It makes no visible difference on
        // the panel, but **sim-side code must not use this snapshot as "the current
        // state"** (the sim side reads TyphoonController's statics directly).

        /// <summary>Whether ④'s typhoon is running.</summary>
        public readonly bool Active;

        /// <summary>The index of the disaster slot ④ holds. Only meaningful when
        /// <see cref="Active"/>.</summary>
        public readonly ushort TyphoonId;

        /// <summary>The true centre, **before clamping** (it can be off the map).</summary>
        public readonly Vec3 Centre;

        /// <summary>Heading (rad, [0, 2π)).</summary>
        public readonly float HeadingRadians;

        /// <summary>The current intensity (0-255). Not the configured maximum, but the
        /// value after the envelope and the landfall decay.</summary>
        public readonly byte Intensity;

        public readonly float StormRadius;
        public readonly float GaleRadius;

        /// <summary>
        /// The values needed to draw the track <b>on into the future</b>
        /// (<see cref="TyphoonTrackPlan"/>). The forecast panel's "track" uses this. Do
        /// not draw it if <c>Usable</c> is false.
        /// </summary>
        public readonly TyphoonTrackPlan Track;

        public readonly TyphoonPhase Phase;
        public readonly uint ElapsedFrames;
        public readonly uint TotalFrames;
        public readonly bool OverLand;

        /// <summary>
        /// Whether a landfall forecast exists. **false means "it will pass out at sea",
        /// not "in 0 minutes".** Do not mix it up with 0 (a discipline ① and ②
        /// established over and over).
        /// </summary>
        public readonly bool LandfallKnown;

        /// <summary>In-game minutes to landfall. Only meaningful when
        /// <see cref="LandfallKnown"/>.</summary>
        public readonly float MinutesToLandfall;

        /// <summary>
        /// Why a typhoon could not be raised, or was let go of (English, for diagnostics;
        /// null if there is none).
        /// **This is the only means of telling "it could not be raised" from "nothing is
        /// happening".**
        /// </summary>
        public readonly string Refusal;

        // ── T4: the **target** weather values ④ is writing ──────────────────
        //
        // ★ These are a different thing from Rain / Cloud above (WeatherManager's
        //   m_currentRain / m_currentCloud). Those are vanilla measurements and the only
        //   two values allowed to claim [measured]; these are the targets ④ writes every
        //   tick, and are this mod's quantities.
        //   **Do not confuse the two sets on the display side.**

        /// <summary>Whether ④ is driving the weather.</summary>
        public readonly bool WeatherDriving;

        /// <summary>The <c>m_targetRain</c> ④ wrote.</summary>
        public readonly float DrivenRain;

        /// <summary>The <c>m_targetCloud</c> ④ wrote.</summary>
        public readonly float DrivenCloud;

        /// <summary>The <c>m_targetDirection</c> ④ wrote (degrees, 0 = +Z /
        /// 90 = +X).</summary>
        public readonly float DrivenDirectionDegrees;

        // ── T6: lightning ───────────────────────────────────────────
        //
        // All four are **quantities ④ counted or estimated**, not values the game
        // publishes (<c>m_lightningQueue</c> is private and cannot be read. IL facts
        // document §A-3). So the display side must not put <c>[measured]</c> on them.

        /// <summary>How many ④ has in the queue (they drop at scheduled + 45
        /// frames).</summary>
        public readonly int LightningInFlight;

        /// <summary>The cumulative count ④ queued during this typhoon. It counts from 0
        /// again for each typhoon.</summary>
        public readonly int LightningTotal;

        /// <summary>
        /// The cumulative count the game threw away during this typhoon. **Anything other
        /// than 0 means we are hitting the ceiling of 20**, i.e. the host storm's and
        /// other mods' lightning is being wiped out too.
        /// </summary>
        public readonly int LightningRejected;

        /// <summary>
        /// The slots kept free for the host vanilla thunderstorm (the
        /// <c>LightningBudget.VanillaMaxStrikes</c> estimate). The higher the intensity
        /// the larger it gets, and the smaller ④'s share.
        /// </summary>
        public readonly int LightningVanillaReserve;

        // ── T7: wind damage ─────────────────────────────────────────
        //
        // All five are **quantities ④ counted**. Vanilla has no wind destruction
        // mechanism at all (§A-5 / §B5), and there is no corresponding tally on the game's
        // side. So the display side must not put <c>[measured]</c> on them.

        /// <summary>How many wind sweeps have run so far (cumulative for the
        /// session).</summary>
        public readonly int WindPasses;

        /// <summary>How many buildings collapsed in the most recent sweep.</summary>
        public readonly int WindLastCollapsed;

        /// <summary>Buildings collapsed, cumulative for the session.</summary>
        public readonly int WindTotalCollapsed;

        /// <summary>How many buildings the most recent sweep examined (those that passed
        /// the candidate mask and were inside the gale radius).</summary>
        public readonly int WindLastScanned;

        /// <summary>
        /// How many buildings **vanilla refused by design** in the most recent sweep.
        /// **Non-zero is normal** — disaster response facilities are not destroyed by a
        /// typhoon (§F-2).
        /// </summary>
        public readonly int WindLastRefused;

        /// <summary>Whether the most recent sweep was cut short at its ceiling (the outer
        /// rim has not been checked yet).</summary>
        public readonly bool WindLastCapped;

        /// <summary>
        /// How many buildings the most recent sweep could not read a height for.
        /// **Unlike ②, they are not excluded from consideration** (they merely forgo the
        /// height bonus. <c>WindDamageModel</c>'s doc).
        /// </summary>
        public readonly int WindLastUnknownHeight;

        // ── T8: river flooding ──────────────────────────────────────
        //
        // These are ④'s quantities too. Vanilla has no flood disaster (§D-1), and there is
        // no value on the game's side publishing "how far the river has risen".

        /// <summary>
        /// The flooding state. **<see cref="TyphoonFloodState.NoSources"/> is not a
        /// fault** (design doc §7.4). The display side must give the reason.
        /// </summary>
        public readonly TyphoonFloodState FloodState;

        /// <summary>
        /// The number of <c>TYPE_NATURAL</c> water sources across the whole map.
        /// **Map-dependent and unknown** (§D-4). 0 means "this map has no natural water
        /// source feeding a river", which is not an anomaly.
        /// </summary>
        public readonly int FloodNaturalSources;

        /// <summary>How many water sources ④ is currently raising.</summary>
        public readonly int FloodTouched;

        /// <summary>The rise applied at the centre in the most recent sweep (m).</summary>
        public readonly float FloodPeakRiseMetres;

        // ── Tornado-grade local damage (patches) ────────────────────────────
        //
        // All four are quantities ④ counted. **Not one actual tornado is created**, so
        // there is no corresponding tally on the game's side either.

        /// <summary>How many patches are alive now. **0 means "there are none right now"
        /// and is not a fault.**</summary>
        public readonly int GustActive;

        /// <summary>How many buildings the patches knocked down in the most recent
        /// sweep.</summary>
        public readonly int GustLastCollapsed;

        /// <summary>How many buildings the patches knocked down, cumulative for the
        /// session.</summary>
        public readonly int GustTotalCollapsed;

        /// <summary>
        /// How many buildings **vanilla refused by design** for the patches in the most
        /// recent sweep.
        /// **Non-zero is normal** — disaster response facilities are not destroyed by
        /// tornadoes either.
        /// </summary>
        public readonly int GustLastRefused;

        public TyphoonSnapshot(bool valid, TyphoonPrefabFacts prefab, uint currentFrame,
                               float rain, float cloud, float fog, float windDirectionDegrees,
                               bool weatherEnabled, bool weatherReadable,
                               bool active, ushort typhoonId, Vec3 centre, float headingRadians,
                               byte intensity, float stormRadius, float galeRadius,
                               TyphoonTrackPlan track,
                               TyphoonPhase phase, uint elapsedFrames, uint totalFrames,
                               bool overLand, bool landfallKnown, float minutesToLandfall,
                               string refusal,
                               bool weatherDriving, float drivenRain, float drivenCloud,
                               float drivenDirectionDegrees,
                               int lightningInFlight, int lightningTotal,
                               int lightningRejected, int lightningVanillaReserve,
                               int windPasses, int windLastCollapsed, int windTotalCollapsed,
                               int windLastScanned, int windLastRefused, bool windLastCapped,
                               int windLastUnknownHeight,
                               TyphoonFloodState floodState, int floodNaturalSources,
                               int floodTouched, float floodPeakRiseMetres,
                               int gustActive, int gustLastCollapsed,
                               int gustTotalCollapsed, int gustLastRefused)
        {
            GustActive = gustActive;
            GustLastCollapsed = gustLastCollapsed;
            GustTotalCollapsed = gustTotalCollapsed;
            GustLastRefused = gustLastRefused;
            FloodState = floodState;
            FloodNaturalSources = floodNaturalSources;
            FloodTouched = floodTouched;
            FloodPeakRiseMetres = floodPeakRiseMetres;
            WindPasses = windPasses;
            WindLastCollapsed = windLastCollapsed;
            WindTotalCollapsed = windTotalCollapsed;
            WindLastScanned = windLastScanned;
            WindLastRefused = windLastRefused;
            WindLastCapped = windLastCapped;
            WindLastUnknownHeight = windLastUnknownHeight;
            LightningInFlight = lightningInFlight;
            LightningTotal = lightningTotal;
            LightningRejected = lightningRejected;
            LightningVanillaReserve = lightningVanillaReserve;
            WeatherDriving = weatherDriving;
            DrivenRain = drivenRain;
            DrivenCloud = drivenCloud;
            DrivenDirectionDegrees = drivenDirectionDegrees;
            Valid = valid;
            Prefab = prefab;
            CurrentFrame = currentFrame;
            Rain = rain;
            Cloud = cloud;
            Fog = fog;
            WindDirectionDegrees = windDirectionDegrees;
            WeatherEnabled = weatherEnabled;
            WeatherReadable = weatherReadable;
            Active = active;
            TyphoonId = typhoonId;
            Centre = centre;
            HeadingRadians = headingRadians;
            Intensity = intensity;
            StormRadius = stormRadius;
            GaleRadius = galeRadius;
            Track = track;
            Phase = phase;
            ElapsedFrames = elapsedFrames;
            TotalFrames = totalFrames;
            OverLand = overLand;
            LandfallKnown = landfallKnown;
            MinutesToLandfall = minutesToLandfall;
            Refusal = refusal;
        }

        public static TyphoonSnapshot Invalid()
        {
            return new TyphoonSnapshot(false, new TyphoonPrefabFacts(), 0u,
                                       0f, 0f, 0f, 0f, false, false,
                                       false, 0, new Vec3(0f, 0f, 0f), 0f,
                                       0, 0f, 0f, TyphoonTrackPlan.None,
                                       TyphoonPhase.Idle, 0u, 0u,
                                       false, false, 0f, null,
                                       false, 0f, 0f, 0f,
                                       0, 0, 0, 0,
                                       0, 0, 0, 0, 0, false, 0,
                                       TyphoonFloodState.Idle, 0, 0, 0f,
                                       0, 0, 0, 0);
        }
    }
}
