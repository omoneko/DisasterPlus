using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// An immutable waveform snapshot for one observation point.
    /// Built by the sim thread; the main thread only reads it and never modifies it (the
    /// same discipline as <see cref="EarthquakeReading"/>).
    ///
    /// <see cref="Frames"/> / <see cref="Values"/> can be as long as the capacity.
    /// **Only the first <see cref="Count"/> entries are valid**; beyond that they are the
    /// zeros of the ring buffer's unused region. A zero there is indistinguishable from a
    /// displacement of 0 (i.e. not shaking), so always iterate by <see cref="Count"/> and
    /// never by the array's length.
    /// </summary>
    public class SeismographTrace
    {
        /// <summary>The building ID of the seismograph that became this observation point.</summary>
        public readonly ushort BuildingId;

        public readonly Vec3 Position;

        /// <summary>Horizontal distance from the epicentre to this observation point (m).</summary>
        public readonly float DistanceToEpicentre;

        public readonly uint[] Frames;
        public readonly float[] Values;

        /// <summary>
        /// The **synthetic seismogram**'s (<see cref="SeismogramModel"/>) value on the same
        /// frames. One-to-one with <see cref="Frames"/>, and likewise only the first
        /// <see cref="Count"/> entries are valid.
        ///
        /// **Null means "the model was not recorded", not "displacement 0".**
        /// When <c>ModSettings.EarthquakeSeismogram</c> is off this is null and the
        /// display draws no layer-2 line at all. Fill it with zeros instead and it means
        /// something else: "the model was running and it was not shaking".
        /// </summary>
        public readonly float[] ModelValues;

        /// <summary>
        /// The **volcanic tremor**'s (layer 3, <c>Core/Volcano/VolcanicTremor</c>) value on
        /// the same frames. One-to-one with <see cref="Frames"/>, and likewise only the
        /// first <see cref="Count"/> entries are valid.
        ///
        /// **Null means "not recorded", not "not shaking"** (exactly the same contract as
        /// <see cref="ModelValues"/>). While no volcano is shaking this is null and the
        /// display draws no layer-3 line at all.
        ///
        /// ★ This is <b>this mod's model</b>, not vanilla's formula (see
        ///   <c>Game/Volcano/VolcanoTremorTrace</c>'s class doc). Put its legend on the
        ///   same side as layer 2's.
        /// </summary>
        public readonly float[] TremorValues;

        /// <summary>
        /// Whether <see cref="Values"/> (layer 1) **means anything** — i.e. whether a
        /// vanilla earthquake is actually in progress.
        ///
        /// **When false, <see cref="Values"/> is all zeros, and that is the value meaning
        /// "there is no vanilla earthquake"** (not "it could not be read"). That is the
        /// state where only a volcano is shaking, and the layer-1 line is not drawn then.
        /// </summary>
        public readonly bool HasQuake;

        /// <summary>Horizontal distance from this point to the centre of ⑤'s affected area (m). 0 if there is no volcano.</summary>
        public readonly float DistanceToVolcano;

        /// <summary>The number of **valid** entries in <see cref="Frames"/> / <see cref="Values"/>.</summary>
        public readonly int Count;

        /// <summary>The peak amplitude (absolute value) among the samples held.</summary>
        public readonly float PeakAbsolute;

        /// <summary>The peak amplitude (absolute value) on the synthetic seismogram side. 0 if not recorded.</summary>
        public readonly float ModelPeakAbsolute;

        /// <summary>The peak amplitude (absolute value) on the volcanic tremor side. 0 if not recorded.</summary>
        public readonly float TremorPeakAbsolute;

        public SeismographTrace(ushort buildingId, Vec3 position, float distanceToEpicentre,
                                uint[] frames, float[] values, int count, float peakAbsolute,
                                float[] modelValues, float modelPeakAbsolute,
                                float[] tremorValues, float tremorPeakAbsolute,
                                bool hasQuake, float distanceToVolcano)
        {
            BuildingId = buildingId;
            Position = position;
            DistanceToEpicentre = distanceToEpicentre;
            Frames = frames;
            Values = values;
            Count = count;
            PeakAbsolute = peakAbsolute;
            ModelValues = modelValues;
            ModelPeakAbsolute = modelPeakAbsolute;
            TremorValues = tremorValues;
            TremorPeakAbsolute = tremorPeakAbsolute;
            HasQuake = hasQuake;
            DistanceToVolcano = distanceToVolcano;
        }

        /// <summary>Whether the synthetic seismogram line may be drawn (it was recorded and its length agrees with the count).</summary>
        public bool HasModel
        {
            get { return ModelValues != null && ModelValues.Length >= Count && Count > 0; }
        }

        /// <summary>Whether the volcanic tremor line may be drawn (it was recorded and its length agrees with the count).</summary>
        public bool HasTremor
        {
            get { return TremorValues != null && TremorValues.Length >= Count && Count > 0; }
        }

        /// <summary>The frame of the newest sample. 0 when empty.</summary>
        public uint NewestFrame
        {
            get { return Count <= 0 ? 0u : Frames[Count - 1]; }
        }
    }

    /// <summary>
    /// Observes and accumulates ground motion at the seismographs' positions.
    /// **Sim thread only.**
    ///
    /// ── What is being recorded (the boundary that stops us fabricating) ──────
    ///
    /// Vanilla's seismograph (<c>EarthquakeSensorAI</c>) **holds no time-series data
    /// whatsoever** (IL facts doc §C-1, ABSENT). Its only field is
    /// <c>m_detectionRange</c>, and it is a device that does nothing but scatter
    /// <c>EarthquakeCoverage</c> within that radius each tick. Waveforms, history, even
    /// the most recent shaking: **none of it exists** on the game's side. So the lines
    /// shown here are **not** "values measured by an in-game sensor".
    ///
    /// There is exactly one reason these lines may declare themselves layer 1 (measured
    /// out of vanilla): they are **vanilla's own shaking formula**
    /// (<c>EarthquakeAI.RenderInstance</c>, §A-7) evaluated at a different point. The
    /// same formula, the same constants and the same window that move the camera,
    /// evaluated at the seismograph's position instead of the camera's. That is a
    /// different evaluation of the same formula, not an approximation and not a
    /// simulation. **Write that difference in the UI too**
    /// (<c>Strings.EarthquakeWaveformNote</c>; design doc §3.5 requires it in both the
    /// design doc and the UI).
    ///
    /// **Never mix in Task 6's added shake.** <c>ShakeWaveform.IntensityFactor</c> is
    /// this mod's correction, not vanilla's formula. The waveform shows vanilla's formula
    /// as it stands.
    ///
    /// ── The cost of finding observation points ──────────────────────
    ///
    /// Finding seismographs means sweeping every slot of the building buffer. We sweep
    /// **once, when a new earthquake starts**, and remember up to
    /// <see cref="MaxObservationPoints"/> of them, nearest to the epicentre first. We do
    /// not sweep every tick.
    ///
    /// **There is exactly one exception: when there are zero observation points.** In
    /// that state the panel says "build a seismograph to record ground motion", and if
    /// you do as it says nothing happens until the next earthquake — a screen that does
    /// not behave the way the instructions directly above it said it would. In that one
    /// case we redo the sweep once every <see cref="RescanIntervalFrames"/> frames.
    /// **Once even one observation point has been found we never sweep again**, so in
    /// normal play the added cost is zero (the sweep only runs while an earthquake is
    /// happening in a city with no seismographs). Building a **second or later** one
    /// mid-earthquake is not reflected in that earthquake — that is left as a known
    /// limitation.
    ///
    /// ── Nothing is kept in the save (design doc §3.5) ──────────────────
    ///
    /// It is thrown away once there is no earthquake in progress, and on level unload
    /// (<see cref="Reset"/>).
    /// </summary>
    public static class SeismographRecorder
    {
        /// <summary>The cap on observation points. Only one graph is ever drawn, so there is no use for more.</summary>
        public const int MaxObservationPoints = 4;

        /// <summary>
        /// How many samples are held per observation point. Samples are taken **per
        /// frame** (one point per frame regardless of game speed; see
        /// <see cref="MaxSubSamplesPerTick"/>), so 512 is exactly the
        /// <see cref="PlotFrameWindow"/> frames of the display window.
        /// </summary>
        public const int Capacity = 512;

        /// <summary>The width of the display window (frames). Two periods of the envelope (§A-7, a 256-frame period).</summary>
        public const int PlotFrameWindow = 512;

        /// <summary>
        /// The minimum interval (frames) between rebuilds of the immutable snapshot.
        ///
        /// <see cref="Snapshot"/> is called every sim tick (from
        /// <c>EarthquakeReader.Read</c>). Building fresh <see cref="Capacity"/>-sized
        /// arrays every time would mean producing hundreds of KB of garbage per second
        /// for the whole duration of the shaking. **When nothing has changed we return
        /// the same reference as last time**, and even when it has, we rebuild no more
        /// often than this interval. A 512-frame window is drawn across 320 px, so 8
        /// frames is a discrepancy of under 5 px.
        /// </summary>
        private const int SnapshotIntervalFrames = 8;

        /// <summary>
        /// The interval (frames) for re-sweeping for seismographs, which only runs when
        /// there are zero observation points. 512 frames is roughly 10 seconds at speed 1
        /// — short enough that a seismograph you build shows up "before long", and long
        /// enough that the full-slot sweep (65,536 entries) is not felt.
        /// </summary>
        private const int RescanIntervalFrames = 512;

        /// <summary>
        /// The maximum number of samples filled in one sim tick. It matches the largest
        /// value of <c>FinalSimulationSpeed</c> (9, at game speed 3). The derivation of
        /// why one sample per tick is not enough is in
        /// <see cref="ShakeWaveform.FirstUnsampledFrame"/>'s doc.
        /// </summary>
        private const int MaxSubSamplesPerTick = 9;

        /// <summary>One observation point. The buffers are reused; a re-sweep overwrites only the identifying data.</summary>
        private class ObservationPoint
        {
            public ushort BuildingId;
            public Vec3 Position;
            public float DistanceToEpicentre;
            public readonly WaveformBuffer Buffer = new WaveformBuffer(Capacity);

            /// <summary>
            /// The synthetic seismogram (layer 2) side. **The same number of entries on
            /// the same frames as the vanilla side** — fill only one of them and the two
            /// lines' time axes come apart on the display side. While the setting is off,
            /// not a single entry goes in (<see cref="_modelRecorded"/>).
            /// </summary>
            public readonly WaveformBuffer ModelBuffer = new WaveformBuffer(Capacity);

            /// <summary>
            /// The volcanic tremor (layer 3) side. **The same number of entries on the
            /// same frames as the other two.** While no volcano is shaking, not a single
            /// entry goes in (<see cref="_tremorRecorded"/>).
            /// </summary>
            public readonly WaveformBuffer TremorBuffer = new WaveformBuffer(Capacity);

            /// <summary>Horizontal distance from this point to the centre of ⑤'s affected area (m). Re-measured every tick.</summary>
            public float DistanceToVolcano;
        }

        private static readonly ObservationPoint[] _points = CreatePoints();

        /// <summary>How many slots are filled, ordered nearest to the epicentre first.</summary>
        private static int _pointCount;

        // Scratch space used by the re-sweep. It is static solely to avoid allocating it
        // every time (sim thread only).
        private static readonly ushort[] _scanIds = new ushort[MaxObservationPoints];
        private static readonly Vector3[] _scanPositions = new Vector3[MaxObservationPoints];
        private static readonly float[] _scanDistances = new float[MaxObservationPoints];

        /// <summary>The ID of the earthquake currently being recorded. 0 means "not recording".</summary>
        private static ushort _quakeId;

        private static IList<SeismographTrace> _cached;
        private static uint _cachedAtFrame;
        private static bool _dirty;
        private static uint _lastSampleFrame;

        /// <summary>
        /// Whether <see cref="_lastSampleFrame"/> means anything. Frame 0 can genuinely
        /// occur, so "we have not taken a single sample yet" is not expressed as 0 (the
        /// internal version of the separate-zero-from-unread discipline this feature
        /// keeps in every other row).
        /// </summary>
        private static bool _hasLastSample;

        /// <summary>The frame of the last zero-observation-point re-sweep. 0 means "not yet".</summary>
        private static uint _lastRescanFrame;

        /// <summary>
        /// Whether the current buffers hold a synthetic seismogram (layer 2).
        ///
        /// It exists **for when the setting is toggled mid-run**. At the moment it is
        /// switched on, the vanilla side's buffer already holds hundreds of entries while
        /// the model side is empty, so putting the two lines side by side gives a picture
        /// with mismatched time axes. When the toggle is seen, **throw both away
        /// together** and start accumulating again in step.
        /// </summary>
        private static bool _modelRecorded;

        /// <summary>
        /// Whether the current buffers hold a volcanic tremor (layer 3).
        /// Needed for the same reason as <see cref="_modelRecorded"/> — if an eruption
        /// starts mid-run, the other two lines already hold hundreds of entries while
        /// this one is empty, so putting them side by side mismatches the time axes. When
        /// the toggle is seen, **throw all of them away together** and start accumulating
        /// again in step.
        /// </summary>
        private static bool _tremorRecorded;

        private static bool _scanErrorLogged;

        private static readonly IList<SeismographTrace> NoTraces =
            new List<SeismographTrace>(0).AsReadOnly();

        /// <summary>The shared empty list. Readers only.</summary>
        public static IList<SeismographTrace> EmptyTraceList
        {
            get { return NoTraces; }
        }

        /// <summary>
        /// Which earthquake is being recorded (an index into the disaster buffer). 0 means
        /// nothing is being recorded. **Read it from the sim thread**
        /// (<c>EarthquakeReader</c> puts it on the snapshot).
        /// </summary>
        public static ushort RecordingQuakeId
        {
            get { return _quakeId; }
        }

        /// <summary>On level load and unload. Carry nothing across from one city to the next.</summary>
        public static void Reset()
        {
            for (int i = 0; i < _points.Length; i++)
            {
                _points[i].BuildingId = 0;
                _points[i].Position = new Vec3(0f, 0f, 0f);
                _points[i].DistanceToEpicentre = 0f;
                _points[i].Buffer.Clear();
                _points[i].ModelBuffer.Clear();
                _points[i].TremorBuffer.Clear();
                _points[i].DistanceToVolcano = 0f;
            }
            _pointCount = 0;
            _modelRecorded = false;
            _tremorRecorded = false;
            _quakeId = 0;
            _cached = null;
            _cachedAtFrame = 0u;
            _dirty = false;
            _lastSampleFrame = 0u;
            _hasLastSample = false;
            _lastRescanFrame = 0u;
            // _scanErrorLogged is not reset. "It throws" is a fact about the game build
            // this DLL is referencing, not per-city state (the same judgement as
            // EarthquakeReader._readErrorLogged).
        }

        /// <summary>
        /// One sim tick's worth of sampling. **Sim thread only** (it touches the building
        /// buffers).
        ///
        /// **Call it below the pause guard.** This advances state, and a waveform that
        /// keeps extending while the game is paused would be a lie (no game time passes
        /// while paused, so no ground motion does either).
        /// </summary>
        public static void Sample(EarthquakeSnapshot snapshot, uint frame)
        {
            if (snapshot == null || !snapshot.Valid) return;

            // The ranking is funnelled through QuakeSelection (this used to hold a copy
            // of EarthquakeReader.SelectDamagingQuake that did not differ by a byte).
            var quake = QuakeSelection.SelectDamaging(snapshot.Quakes);

            // ★★ **Record even with no vanilla earthquake, as long as a volcano is
            //    shaking** (2026-08-22, at the owner's request: "volcanic earthquakes are
            //    not recorded on the seismograph"). This used to be "no earthquake means
            //    throw the observation points away too", so while only a volcano was
            //    shaking the seismograph stayed blank.
            bool tremor = VolcanoTremorTrace.Active;

            if (quake == null && !tremor)
            {
                // There is nothing shaking at all. As design doc §3.5 says, throw it away
                // here.
                if (_quakeId != 0 || _pointCount != 0) ClearAll();
                return;
            }

            if (quake != null && quake.DisasterId != _quakeId)
            {
                ClearAll();
                _quakeId = quake.DisasterId;
                _lastRescanFrame = frame;
                Rescan(quake.Epicentre.ToVec2());
            }
            else if (quake == null && _quakeId != 0)
            {
                // ★ Only the earthquake has ended. **Do not throw the observation points
                //   away** — the volcano is still shaking, and throwing them away here
                //   would break the seismogram and start it accumulating again from
                //   scratch. Layer 1 will be 0 from here on, and <c>HasQuake</c> declares
                //   false.
                _quakeId = 0;
            }

            if (_pointCount == 0)
            {
                // ★ The only re-sweep path (see the class doc). It runs only while the
                //    shaking continues in a city with no seismographs at all, and once
                //    even one is found it never runs again.
                if (frame - _lastRescanFrame >= RescanIntervalFrames)
                {
                    _lastRescanFrame = frame;
                    Rescan(quake != null
                           ? quake.Epicentre.ToVec2()
                           : VolcanoTremorTrace.Centre.ToVec2());
                }
                if (_pointCount == 0) return;
            }

            // ★ The distance to ⑤'s centre is **re-measured every tick**. With at most 4
            //   observation points the cost is negligible, and it is certain to be
            //   populated even for points reordered by an earthquake.
            if (tremor)
            {
                for (int i = 0; i < _pointCount; i++)
                {
                    _points[i].DistanceToVolcano =
                        VolcanoTremorTrace.DistanceFromCentre(_points[i].Position);
                }
            }

            // ★ Whether the vanilla earthquake side can actually be evaluated.
            //   m_activationFrame == 0 means "not yet decided", not "now" (the trap in
            //   §A-1). m_activeDuration is a prefab value, and without it we do not know
            //   the shaking window (§A-0) — hard-code a guess there and you get a
            //   waveform that keeps extending after the earthquake has ended.
            //   **If any one of them is missing, layer 1 is not evaluated. The volcano
            //   side is not stopped.**
            uint activeDuration = 0u;
            bool quakeUsable = false;
            if (quake != null && quake.ActivationScheduled && snapshot.Prefab.Resolved)
            {
                activeDuration = snapshot.Prefab.ActiveDuration;
                quakeUsable = activeDuration != 0u;
            }

            if (!quakeUsable && !tremor) return;

            // ★ Layer 2's synthetic seismogram. **With the setting off, not a single
            //   entry is accumulated** (it is off by default).
            //   **Nothing is accumulated with no vanilla earthquake either** — it is a
            //   model of one fault rupture, and volcanic tremor does not ride on it.
            bool wantModel = ModSettings.EarthquakeSeismogram.value && quakeUsable;

            // If the "record / do not record" of any of the three changes, **throw them
            // all away together**. Leaving one empty alongside the others gives a picture
            // with mismatched time axes.
            if (wantModel != _modelRecorded || tremor != _tremorRecorded)
            {
                for (int i = 0; i < _points.Length; i++)
                {
                    _points[i].Buffer.Clear();
                    _points[i].ModelBuffer.Clear();
                    _points[i].TremorBuffer.Clear();
                }
                _modelRecorded = wantModel;
                _tremorRecorded = tremor;
                _cached = null;
                _dirty = true;
                _hasLastSample = false;
            }

            // The seed comes from the earthquake itself (no frame number mixed in), so
            // **the same earthquake gives the same seismogram**. VanillaRandomizer is not
            // used — the synthetic seismogram is something this mod decides for itself.
            SeismogramModel model = wantModel
                ? SeismogramModel.For(SeismogramSeed(quake), activeDuration)
                : new SeismogramModel();

            uint first = ShakeWaveform.FirstUnsampledFrame(
                _lastSampleFrame, _hasLastSample, frame, MaxSubSamplesPerTick);

            bool wrote = false;
            for (uint f = first; f <= frame; f++)
            {
                // ★ One sample per tick is not enough. m_currentFrameIndex jumps by
                //    FinalSimulationSpeed (1/3/9), while the shaking's principal
                //    component is 0.63 rad/frame (a period of about 10 frames), so at
                //    speed 3 it aliases into a **spurious long-period wave** with a
                //    period of about 92 frames.
                //    DisplacementAt is a closed-form expression in e, so evaluating it on
                //    a skipped frame is exactly as "measured" as evaluating it once (see
                //    ShakeWaveform's doc).
                long e = 0L;
                bool quakeShaking = false;
                if (quakeUsable)
                {
                    e = (long)f - quake.ActivationFrame + ShakeWaveform.FrameOffset;
                    quakeShaking = ShakeWaveform.IsShaking(e, activeDuration);
                }

                // On a frame where nothing is shaking, add nothing at all (none of the
                // three lines).
                if (!quakeShaking && !_tremorRecorded) continue;

                // Do not add m_referenceTimer to t. That is a value for the main thread's
                // render interpolation and is not something the sim thread should read
                // (whole frames are enough here).
                float t = e;

                for (int i = 0; i < _pointCount; i++)
                {
                    var point = _points[i];

                    // ★ Vanilla's formula with the distance swapped from "from the
                    //    camera" to "from the hypocentre" (design doc §3.5). The formula,
                    //    the constants and the window are vanilla's as they stand.
                    //    ★ A 0 on a non-shaking frame is **the value meaning "there is no
                    //      vanilla earthquake"**, not "it could not be read"
                    //      (<c>HasQuake</c> declares which).
                    float value = quakeShaking
                        ? ShakeWaveform.DisplacementAt(point.DistanceToEpicentre, t)
                        : 0f;
                    point.Buffer.Add(f, value);

                    // ★ All three are evaluated **on the same frame at the same
                    //   observation point**. Throttle just one of them and its time axis
                    //   drifts away from the others.
                    if (_modelRecorded)
                    {
                        point.ModelBuffer.Add(
                            f, quakeShaking
                               ? model.DisplacementAt(point.DistanceToEpicentre, t)
                               : 0f);
                    }

                    if (_tremorRecorded)
                    {
                        point.TremorBuffer.Add(
                            f, VolcanoTremorTrace.DisplacementAt(point.DistanceToVolcano, f));
                    }
                }
                wrote = true;
            }

            if (!wrote) return;

            _lastSampleFrame = frame;
            _hasLastSample = true;
            _dirty = true;
        }

        /// <summary>
        /// The immutable snapshot handed to the main thread. **Ordered nearest to the
        /// epicentre first.** **Sim thread only** (call it from the same thread as
        /// <see cref="Sample"/>).
        ///
        /// When nothing has changed it returns **the same reference as last time**.
        /// Rebuilding the arrays every sim tick is the easiest waste this feature can
        /// produce, and on top of that the render side cannot tell apart updates finer
        /// than <see cref="SnapshotIntervalFrames"/>.
        /// </summary>
        public static IList<SeismographTrace> Snapshot()
        {
            if (_pointCount == 0) return NoTraces;

            if (_cached != null)
            {
                if (!_dirty) return _cached;
                if (_lastSampleFrame - _cachedAtFrame < SnapshotIntervalFrames) return _cached;
            }

            var traces = new List<SeismographTrace>(_pointCount);
            for (int i = 0; i < _pointCount; i++)
            {
                var point = _points[i];
                int count = point.Buffer.Count;

                var frames = new uint[count];
                var values = new float[count];
                int written = point.Buffer.CopyTo(frames, values);

                // ★ Null means "not recorded". Fill it with zeros and it turns into "it
                //   was not shaking".
                float[] modelValues = null;
                float modelPeak = 0f;
                if (_modelRecorded && point.ModelBuffer.Count == count)
                {
                    var modelFrames = new uint[count];
                    modelValues = new float[count];
                    point.ModelBuffer.CopyTo(modelFrames, modelValues);
                    modelPeak = point.ModelBuffer.PeakAbsolute;
                }

                // ★ Layer 3 follows the same contract (null means "not recorded").
                float[] tremorValues = null;
                float tremorPeak = 0f;
                if (_tremorRecorded && point.TremorBuffer.Count == count)
                {
                    var tremorFrames = new uint[count];
                    tremorValues = new float[count];
                    point.TremorBuffer.CopyTo(tremorFrames, tremorValues);
                    tremorPeak = point.TremorBuffer.PeakAbsolute;
                }

                traces.Add(new SeismographTrace(point.BuildingId, point.Position,
                                                point.DistanceToEpicentre,
                                                frames, values, written,
                                                point.Buffer.PeakAbsolute,
                                                modelValues, modelPeak,
                                                tremorValues, tremorPeak,
                                                _quakeId != 0, point.DistanceToVolcano));
            }

            _cached = traces.AsReadOnly();
            _cachedAtFrame = _lastSampleFrame;
            _dirty = false;
            return _cached;
        }

        /// <summary>
        /// The synthetic seismogram's seed. **It comes from the earthquake itself** (its
        /// ID and activation frame), so within one earthquake it gives the same shape
        /// however many times it is rebuilt, and a different earthquake gives a different
        /// shape. **Never mix in the frame number itself** — do so and you get a different
        /// seismogram on every tick.
        /// </summary>
        private static uint SeismogramSeed(EarthquakeReading quake)
        {
            return DeterministicRandom.Hash(quake.DisasterId, quake.ActivationFrame);
        }

        private static void ClearAll()
        {
            for (int i = 0; i < _points.Length; i++)
            {
                _points[i].Buffer.Clear();
                _points[i].ModelBuffer.Clear();
                _points[i].TremorBuffer.Clear();
                _points[i].DistanceToVolcano = 0f;
            }
            _pointCount = 0;
            _modelRecorded = false;
            _tremorRecorded = false;
            _quakeId = 0;
            _cached = null;
            _cachedAtFrame = 0u;
            _dirty = false;
            _lastSampleFrame = 0u;
            _hasLastSample = false;
        }

        /// <summary>
        /// Finds the seismographs and remembers up to <see cref="MaxObservationPoints"/>
        /// of them, nearest to the epicentre first. There are only two places it may be
        /// called from: **when a new earthquake starts**, and **when
        /// <see cref="RescanIntervalFrames"/> frames have passed with zero observation
        /// points** (it sweeps every slot, so it cannot run every tick; the background is
        /// in the class doc).
        ///
        /// There are only two conditions: <c>Created</c> being set, and
        /// <c>m_buildingAI is EarthquakeSensorAI</c>. Operating efficiency is not
        /// considered — that would be a matter for
        /// <c>ImmaterialResourceManager</c>, whereas this sweep is about finding out
        /// where the buildings called seismographs stand.
        /// </summary>
        /// <param name="origin">
        /// The origin for the nearest-first ordering. **The epicentre when there is a
        /// vanilla earthquake, and the centre of ⑤'s affected area otherwise** (we still
        /// need observation points when only a volcano is shaking).
        /// </param>
        private static void Rescan(Vec2 origin)
        {
            _pointCount = 0;

            try
            {
                var bm = BuildingManager.instance;
                if (bm == null) return;

                var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
                if (buildings == null) return;

                Vec2 epicentre = origin;
                int found = 0;

                // Index 0 is the reserved "invalid" slot, so start at 1.
                for (int i = 1; i < buildings.Length; i++)
                {
                    if ((buildings[i].m_flags & Building.Flags.Created) == Building.Flags.None) continue;

                    BuildingInfo info;
                    try
                    {
                        info = buildings[i].Info;
                    }
                    catch
                    {
                        continue;
                    }

                    // UnityEngine.Object's == overload also rejects a destroyed
                    // (fake-null) object.
                    if (info == null) continue;
                    if (!(info.m_buildingAI is EarthquakeSensorAI)) continue;

                    var p = buildings[i].m_position;
                    float distance = Mathf.Sqrt(epicentre.DistanceSquaredTo(new Vec2(p.x, p.z)));
                    found = InsertNearest(found, (ushort)i, p, distance);
                }

                for (int i = 0; i < found; i++)
                {
                    var point = _points[i];
                    point.BuildingId = _scanIds[i];
                    point.Position = new Vec3(_scanPositions[i].x, _scanPositions[i].y,
                                              _scanPositions[i].z);
                    point.DistanceToEpicentre = _scanDistances[i];
                    point.DistanceToVolcano = 0f;
                    // ClearAll() has already emptied these, but this is insurance against
                    // anyone later creating a path where swapping the observation points
                    // and the buffers' contents get out of step.
                    point.Buffer.Clear();
                    point.ModelBuffer.Clear();
                    point.TremorBuffer.Clear();
                }
                _pointCount = found;
            }
            catch (System.Exception e)
            {
                // This path is taken only once per earthquake, but Log.Error is not
                // throttled, so it follows the established shape (shout once, then Diag).
                if (!_scanErrorLogged)
                {
                    _scanErrorLogged = true;
                    Log.Error("earthquake sensor scan failed", e);
                }
                else
                {
                    Log.Diag("EqSensors", "sensor scan failed: " + e.GetType().Name);
                }
                _pointCount = 0;
            }
        }

        /// <summary>
        /// An insertion that keeps the sweep's results in ascending order of distance.
        /// Returns the count after insertion. Seismographs too far away to fit within the
        /// cap are dropped (the graph only ever draws the nearest one).
        /// </summary>
        private static int InsertNearest(int count, ushort id, Vector3 position, float distance)
        {
            int at = 0;
            while (at < count && _scanDistances[at] <= distance) at++;
            if (at >= MaxObservationPoints) return count;

            int last = count < MaxObservationPoints ? count : MaxObservationPoints - 1;
            for (int i = last; i > at; i--)
            {
                _scanIds[i] = _scanIds[i - 1];
                _scanPositions[i] = _scanPositions[i - 1];
                _scanDistances[i] = _scanDistances[i - 1];
            }

            _scanIds[at] = id;
            _scanPositions[at] = position;
            _scanDistances[at] = distance;

            return count < MaxObservationPoints ? count + 1 : MaxObservationPoints;
        }

        private static ObservationPoint[] CreatePoints()
        {
            var points = new ObservationPoint[MaxObservationPoints];
            for (int i = 0; i < points.Length; i++) points[i] = new ObservationPoint();
            return points;
        }
    }
}
