using System.Collections.Generic;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The four tuning values baked into the <c>EarthquakeAI</c> prefab.
    ///
    /// **The actual numbers do not exist anywhere in the DLL** (IL facts doc §A-0). They
    /// are serialised prefab values, so IL disassembly cannot see them, and reading
    /// <c>sharedassets</c> with UnityPy also fails because the type tree cannot be read.
    /// So **the only way to get them is to read them at runtime from
    /// <c>DisasterManager.FindDisasterInfo&lt;EarthquakeAI&gt;()</c> and print them in the
    /// diagnostic dump**, which is Task 3's main purpose. Every later duration design in
    /// ② rests on these four.
    ///
    /// It is a struct so that caching it does not drag in Unity's fake-null
    /// self-repair problem (it holds only floats and uints, so no <c>DisasterInfo</c>
    /// reference is kept). The default value has <see cref="Resolved"/> == false, i.e.
    /// "not read yet / no longer readable".
    /// </summary>
    public struct EarthquakePrefabFacts
    {
        /// <summary>Whether the four values were actually read. When false every other field is 0 and means nothing.</summary>
        public readonly bool Resolved;

        public readonly float CrackLength;
        public readonly float CrackWidth;
        public readonly uint EmergingDuration;
        public readonly uint ActiveDuration;

        public EarthquakePrefabFacts(float crackLength, float crackWidth,
                                     uint emergingDuration, uint activeDuration)
        {
            Resolved = true;
            CrackLength = crackLength;
            CrackWidth = crackWidth;
            EmergingDuration = emergingDuration;
            ActiveDuration = activeDuration;
        }
    }

    /// <summary>
    /// An immutable snapshot built on the sim thread and read on the main thread.
    /// Same discipline as ①'s <see cref="WeatherSnapshot"/>: **once built, never modified**.
    ///
    /// </summary>
    public class EarthquakeSnapshot
    {
        /// <summary>
        /// The empty list shared by every snapshot when there are no earthquakes at all.
        /// It exists solely to avoid allocating an empty list on every sim tick.
        ///
        /// Wrapping it in a <c>ReadOnlyCollection</c> is deliberate. Hand out a plain
        /// <c>List</c> as an <c>IList</c> and <c>snapshot.Quakes.Add(...)</c> both
        /// compiles and runs, so **one place dirtying the shared empty list breaks every
        /// snapshot from then on**. Wrapped as it is here, that misuse throws immediately,
        /// the very first time.
        /// </summary>
        private static readonly IList<EarthquakeReading> NoQuakes =
            new List<EarthquakeReading>(0).AsReadOnly();

        /// <summary>
        /// The earthquakes alive at this instant (Created and not Deleted).
        /// **Do not modify it after construction** (<see cref="EarthquakeReader"/> wraps
        /// it read-only before handing it over). Break that and the sim thread ends up
        /// writing to it while the main thread is enumerating it, which turns into an
        /// exception with no stack trace.
        /// </summary>
        public readonly IList<EarthquakeReading> Quakes;

        public readonly EarthquakePrefabFacts Prefab;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>. The reference the display uses for time remaining.</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// The sim thread's time of day (0 to 23.99963).
        /// It is <c>m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR</c>, not
        /// <c>m_currentDayTimeHour</c> (§F-1; the details are in
        /// <see cref="EarthquakeReader"/>).
        /// </summary>
        public readonly float HourOfDay;

        /// <summary>
        /// <c>SimulationManager.m_enableDayNight</c>.
        ///
        /// **When it is false, <see cref="HourOfDay"/> is pinned at 12.0 forever**
        /// (the sim thread resets <c>m_dayTimeOffsetFrames</c> every frame, §F-1).
        /// That is neither a bug nor an inconsistency in the game but a legitimate player
        /// setting, so it is not a FAIL in the assumption checks. But **the display must
        /// not hide the fact** (show the bare number 12.0 and a clock stuck at noon looks
        /// like a value that was successfully read).
        /// </summary>
        public readonly bool DayNightEnabled;

        /// <summary>Whether the read succeeded. When false the display shows "cannot be read".</summary>
        public readonly bool Valid;

        /// <summary>
        /// The headroom of the one building that was under the cursor position the main
        /// thread published (built on the sim thread by <see cref="BuildingProbe"/>).
        ///
        /// When no building was found, or the cursor was invalid, this is
        /// <see cref="BuildingMargin.None"/> with <c>HasBuilding == false</c>.
        /// **The main thread only draws it; it never touches the building buffers.**
        /// </summary>
        public readonly BuildingMargin CursorBuilding;

        /// <summary>
        /// Which earthquake <see cref="CursorBuilding"/> was judged against (an index into
        /// the disaster buffer).
        ///
        /// **0 means "we never looked", not "there was no building"** — either the cursor
        /// was invalid (panel closed, mouse over the UI, or off the terrain), or there was
        /// no earthquake running a destruction pass (Active / Emerging). "We looked and
        /// there was no building" is expressed by <c>CursorQuakeId != 0</c> together with
        /// <c>CursorBuilding.HasBuilding == false</c>. Conflate the two and the panel says
        /// "there is no building" while the cursor is sitting on one.
        ///
        /// **The display must always state this.** Several earthquakes can run at once
        /// (§E-1). Show the judgement for one of them and say nothing else, and the
        /// reader never learns that nothing has been said about the others — which makes
        /// it a confidently wrong number.
        /// </summary>
        public readonly ushort CursorQuakeId;

        /// <summary>
        /// **How** the sweep for the building under the cursor ended
        /// (<see cref="BuildingProbeOutcome"/>).
        ///
        /// <see cref="CursorQuakeId"/> alone cannot distinguish "we looked and there was
        /// no building" from "we tried to look and failed". The first is a measurement,
        /// the second a failed read, and giving them the same wording makes the failed
        /// read come out wearing the face of a measurement.
        /// </summary>
        public readonly BuildingProbeOutcome CursorProbe;

        /// <summary>
        /// The height of <see cref="CursorBuilding"/> (m). **For layer 2 (long-period
        /// ground motion) only.**
        ///
        /// **0 means "could not be read", not "short"**
        /// (<see cref="BuildingHeight.MetresOf"/>). The display must not conflate the two
        /// — conflate them and, in an environment where the height cannot be read, you
        /// get the **entirely unfounded** assertion "this building is short, so it is not
        /// affected by long-period motion".
        ///
        /// Vanilla does not use this quantity for the shaking or the damage at all
        /// (§A-7 / §A-3). So any row that uses it is necessarily layer 2.
        /// </summary>
        public readonly float CursorBuildingHeight;

        /// <summary>
        /// The raw <c>ImmaterialResourceManager.Resource.EarthquakeCoverage</c> at the
        /// cursor position. Meaningless when <see cref="CursorCoverageValid"/> is false.
        ///
        /// **This is not what the lead time is based on.** Vanilla's lead time uses the
        /// single coverage value **at the epicentre**
        /// (<see cref="EarthquakeReading.CoverageAtEpicentre"/>, §A-2); the value at the
        /// cursor exists purely so the player can check whether a seismograph reaches
        /// this spot. Mix the two up and derive the lead time from the cursor position,
        /// and every decision about where to build seismographs goes wrong.
        /// </summary>
        public readonly int CursorCoverage;

        /// <summary>
        /// Whether the coverage at the cursor was actually read.
        ///
        /// Kept separate for the same reason as
        /// <see cref="EarthquakeReading.CoverageKnown"/>. **A coverage of 0 is a
        /// meaningful measurement — "no seismograph reaches here"** — and it is the
        /// headline explanation of this whole feature, so it must never be collapsed into
        /// the same 0 as a failed read.
        ///
        /// There are two ways it becomes false: the main thread has not yet published a
        /// valid cursor position (panel closed, mouse over the UI, or off the terrain),
        /// or <c>ImmaterialResourceManager</c> could not be read. The display tells the
        /// two apart by cross-checking against its own knowledge of whether the cursor is
        /// currently over terrain.
        /// </summary>
        public readonly bool CursorCoverageValid;

        /// <summary>
        /// Ground-motion waveforms observed at the seismograph positions. **Ordered by
        /// distance from the epicentre** (nearest first).
        ///
        /// These are <b>not values measured by an in-game sensor</b> —
        /// <c>EarthquakeSensorAI</c> holds no time-series data whatsoever (§C-1). They are
        /// what this mod accumulated by evaluating **vanilla's own shaking formula**
        /// (§A-7) at each seismograph's position (see <see cref="SeismographRecorder"/>'s
        /// class doc).
        ///
        /// There are two ways this comes out empty, and **the display must tell them
        /// apart**:
        ///   - <see cref="WaveformQuakeId"/> == 0 … nothing is being recorded at all (no earthquake)
        ///   - <see cref="WaveformQuakeId"/> != 0 … there is an earthquake to record, but zero seismographs
        ///
        /// On top of that, "there are stations but zero samples" (before the main shock,
        /// while the shaking window has not opened yet) is expressed by
        /// <c>SeismographTrace.Count == 0</c>. **An empty graph and a flat graph mean
        /// different things**, so zero samples must never be drawn as "displacement 0".
        /// </summary>
        public readonly IList<SeismographTrace> Traces;

        /// <summary>
        /// Which earthquake <see cref="Traces"/> records (an index into the disaster
        /// buffer). **0 means "nothing is being recorded"** — there is no earthquake in
        /// progress (Emerging|Active).
        /// </summary>
        public readonly ushort WaveformQuakeId;

        /// <summary>
        /// **Layer 2.** Where the tsunami chain from an undersea epicentre currently
        /// stands (<see cref="TsunamiChain"/>). This is not a quantity vanilla computes
        /// but **the state of behaviour this mod invented**, so the display must always
        /// show it as a layer-2 row.
        ///
        /// It exists so the main thread never reads <see cref="TsunamiChain"/>'s static
        /// state directly. <c>EarthquakeReader.Read()</c> runs **before**
        /// <c>TsunamiChain.Tick()</c>, so what lands here is up to one tick old (the same
        /// kind of lag as <see cref="CursorBuilding"/> already has).
        /// </summary>
        public readonly TsunamiChainState TsunamiState;

        /// <summary>
        /// The frame at which the scheduled tsunami comes due. Only meaningful when
        /// <see cref="TsunamiState"/> is <see cref="TsunamiChainState.Scheduled"/>.
        /// </summary>
        public readonly uint TsunamiDueFrame;

        /// <summary>
        /// The earthquake the tsunami chain is watching (an index into the disaster
        /// buffer). 0 means it is watching nothing.
        /// **The display must state this** — several earthquakes can run at once (§E-1),
        /// so saying nothing about which one the chain came from leaves the reader
        /// unaware that nothing has been said about the others.
        /// </summary>
        public readonly ushort TsunamiQuakeId;

        /// <summary>
        /// **Layer 2.** Whether the most recent long-period sweep was cut off at its
        /// per-sweep cap (<c>LongPeriodDamage.LastCapped</c>).
        ///
        /// The sweep works outwards from the epicentre (<c>OutwardCellOrder</c>), so even
        /// when it is cut off the area around the epicentre has certainly been evaluated.
        /// But **the outer area has not**, so printing nothing but "extra collapse risk
        /// N.N%" for a distant building means showing a probability that has not yet been
        /// drawn in this sweep, wearing the face of a settled figure (layer-2 review I1).
        /// The display must always state this as a caveat.
        ///
        /// As with <see cref="TsunamiState"/>, <c>EarthquakeReader.Read()</c> runs
        /// **before** <c>LongPeriodDamage.Apply()</c>, so what lands here is up to one
        /// tick old.
        /// </summary>
        public readonly bool LongPeriodCapped;

        public EarthquakeSnapshot(IList<EarthquakeReading> quakes, EarthquakePrefabFacts prefab,
                                  uint currentFrame, float hourOfDay, bool dayNightEnabled,
                                  BuildingMargin cursorBuilding, ushort cursorQuakeId,
                                  BuildingProbeOutcome cursorProbe, float cursorBuildingHeight,
                                  int cursorCoverage, bool cursorCoverageValid,
                                  IList<SeismographTrace> traces, ushort waveformQuakeId,
                                  TsunamiChainState tsunamiState, uint tsunamiDueFrame,
                                  ushort tsunamiQuakeId, bool longPeriodCapped,
                                  bool valid)
        {
            LongPeriodCapped = longPeriodCapped;
            TsunamiState = tsunamiState;
            TsunamiDueFrame = tsunamiDueFrame;
            TsunamiQuakeId = tsunamiQuakeId;
            Traces = traces == null ? SeismographRecorder.EmptyTraceList : traces;
            WaveformQuakeId = waveformQuakeId;
            Quakes = quakes == null ? NoQuakes : quakes;
            Prefab = prefab;
            CurrentFrame = currentFrame;
            HourOfDay = hourOfDay;
            DayNightEnabled = dayNightEnabled;
            CursorBuilding = cursorBuilding;
            CursorQuakeId = cursorQuakeId;
            CursorProbe = cursorProbe;
            CursorBuildingHeight = cursorBuildingHeight;
            CursorCoverage = cursorCoverage;
            CursorCoverageValid = cursorCoverageValid;
            Valid = valid;
        }

        public static EarthquakeSnapshot Invalid()
        {
            return new EarthquakeSnapshot(NoQuakes, new EarthquakePrefabFacts(), 0u, 0f, false,
                                          BuildingMargin.None(), 0,
                                          BuildingProbeOutcome.NotProbed, 0f, 0, false,
                                          SeismographRecorder.EmptyTraceList, 0,
                                          TsunamiChainState.Idle, 0u, 0, false, false);
        }

        /// <summary>The shared empty list used when there are no earthquakes. Readers only.</summary>
        public static IList<EarthquakeReading> EmptyQuakeList
        {
            get { return NoQuakes; }
        }
    }
}
