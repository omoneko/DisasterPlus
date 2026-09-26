namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// A fixed-length ring buffer holding the ground-motion samples for one station.
    ///
    /// ── Why the mod keeps them itself ────────────────────────────
    ///
    /// <c>EarthquakeSensorAI</c> holds **no time-series data whatsoever** (IL findings doc
    /// §C-1, ABSENT). Its only field is <c>m_detectionRange</c>, and all it does is spread
    /// an immune-system-like resource called <c>EarthquakeCoverage</c> within that radius
    /// every tick. Waveforms, history, how hard it shook a moment ago — **none of it exists
    /// on the game's side**. So the only answer to "there is a seismometer and yet I cannot
    /// see a waveform" is for **this mod to observe and store it itself**.
    ///
    /// The values stored are not fabricated — they are <c>ShakeWaveform</c> (i.e. vanilla's
    /// own formula from <c>EarthquakeAI.RenderInstance</c>, §A-7) evaluated at the
    /// station's position.
    ///
    /// ── The writing side and the reading side are on different threads ──────────────────
    ///
    /// This class is not itself thread-safe. Writing happens on the **sim thread**
    /// (<c>SeismographRecorder.Sample</c>) and drawing on the **main thread**, and the
    /// handover uses the snapshot-then-render pattern carried on from ①
    /// (<c>EarthquakeHub</c>'s single <c>lock</c>). The main thread must never read this
    /// buffer directly.
    ///
    /// ── Never, ever read the unused region ──────────────────────────
    ///
    /// The arrays are allocated at full capacity, and the elements not yet written are 0.
    /// **0 is indistinguishable from "not shaking".** Read into the unused region and you
    /// draw a confident straight line on top of data that does not exist. Every read is
    /// bounded by <see cref="Count"/> and walks in order from <see cref="OldestIndex"/>.
    ///
    /// ── Not kept in the save (design doc §3.5) ──────────────────────────
    ///
    /// Thrown away when the earthquake ends. It is not worth adding to the save format, and
    /// this project has already been badly bitten once by a bug rooted in save ordering.
    /// </summary>
    public class WaveformBuffer
    {
        private readonly uint[] _frames;
        private readonly float[] _values;

        /// <summary>Where the next write goes.</summary>
        private int _head;

        /// <summary>How many are actually held (&lt;= <see cref="Capacity"/>).</summary>
        private int _count;

        /// <summary>
        /// A <paramref name="capacity"/> below 1 is rounded up to 1.
        /// Make a zero-length array and <see cref="Add"/> is certain to fall over — and an
        /// <c>IndexOutOfRangeException</c> on the sim thread comes out as a popup with no
        /// stack trace.
        /// </summary>
        public WaveformBuffer(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _frames = new uint[capacity];
            _values = new float[capacity];
        }

        public int Capacity { get { return _frames.Length; } }

        public int Count { get { return _count; } }

        /// <summary>
        /// Adds one sample. **No allocation per item** (this is a path taken every sim tick).
        /// When full, the oldest one is dropped.
        /// </summary>
        public void Add(uint frame, float value)
        {
            _frames[_head] = frame;
            _values[_head] = value;

            _head++;
            if (_head >= _frames.Length) _head = 0;
            if (_count < _frames.Length) _count++;
        }

        /// <summary>
        /// Throws away the contents. The arrays are not zero-filled — every read is bounded
        /// by <see cref="Count"/>, so the values left behind are visible to nobody.
        /// </summary>
        public void Clear()
        {
            _count = 0;
            _head = 0;
        }

        /// <summary>
        /// Packs the samples oldest-first and returns **how many were actually written**.
        /// If the arrays passed in are shorter than the number held, it goes with the
        /// shorter (it never writes past the length the caller provided).
        /// Returns 0 if an array is null (rather than throwing).
        /// </summary>
        public int CopyTo(uint[] frames, float[] values)
        {
            if (frames == null || values == null) return 0;

            int max = frames.Length < values.Length ? frames.Length : values.Length;
            if (max > _count) max = _count;

            int start = OldestIndex;
            for (int i = 0; i < max; i++)
            {
                int index = start + i;
                if (index >= _frames.Length) index -= _frames.Length;
                frames[i] = _frames[index];
                values[i] = _values[index];
            }
            return max;
        }

        /// <summary>The frame of the oldest sample held. 0 if empty.</summary>
        public uint OldestFrame
        {
            get { return _count == 0 ? 0u : _frames[OldestIndex]; }
        }

        /// <summary>The frame of the newest sample held. 0 if empty.</summary>
        public uint NewestFrame
        {
            get
            {
                if (_count == 0) return 0u;
                int index = _head - 1;
                if (index < 0) index += _frames.Length;
                return _frames[index];
            }
        }

        /// <summary>
        /// The largest amplitude (absolute value) **among the samples held right now**.
        /// 0 if empty.
        ///
        /// Do not update a maximum in <see cref="Add"/> and hold on to it. Do that and you
        /// keep reporting the amplitude of a sample that has already fallen out of the
        /// buffer as "the current peak amplitude" — a value that is not present in the
        /// waveform being displayed.
        /// NaN is ignored naturally, since every comparison against it is false.
        /// </summary>
        public float PeakAbsolute
        {
            get
            {
                float peak = 0f;
                int start = OldestIndex;
                for (int i = 0; i < _count; i++)
                {
                    int index = start + i;
                    if (index >= _frames.Length) index -= _frames.Length;

                    float v = _values[index];
                    if (v < 0f) v = -v;
                    if (v > peak) peak = v;
                }
                return peak;
            }
        }

        /// <summary>
        /// The index of the oldest sample. **Until it fills up it has not wrapped once**, so
        /// it is always 0; once full, the next write position is the oldest as it stands.
        /// </summary>
        private int OldestIndex
        {
            get { return _count < _frames.Length ? 0 : _head; }
        }
    }
}
