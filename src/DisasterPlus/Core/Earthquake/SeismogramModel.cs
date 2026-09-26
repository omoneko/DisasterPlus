using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// **Synthetic ground motion with a P wave, an S wave and a coda. This is a model of
    /// this mod's own, not a measurement.**
    ///
    /// ── Why it is needed (request ②, "the shaking and the waveform are not realistic") ─────
    ///
    /// <see cref="ShakeWaveform"/> copies vanilla's own formula (§A-7) as it stands —
    /// <c>(sin(t·0.63) + sin(t·0.17)) × amplitude</c>, i.e. **two fixed sinusoids**.
    /// Their two periods are incommensurate so it does not repeat exactly, but the visible
    /// texture comes round in a few tens of frames, and **there is no arrival, no onset and
    /// no decay**. Looked at as a seismometer record, that is not ground motion but merely
    /// a continuous oscillation. The player's report is correct.
    ///
    /// ── What was built ──────────────────────────────────────
    ///
    /// Only the three most recognisable properties a real seismogram has:
    ///
    ///   1. **The P wave arrival** — small, and higher in frequency
    ///   2. **The S wave arrival** — later than the P, far larger, and lower in frequency
    ///   3. **The coda** — decaying exponentially after the S, with an amplitude that
    ///      wanders irregularly
    ///
    /// And **the gap between the arrival times widens with the hypocentral distance**. That
    /// is the most legible property of a seismogram (the preliminary tremor duration), and
    /// it is also the only handle for showing ②'s headline feature, the intensity
    /// distribution with distance, from the waveform side.
    ///
    /// ── Do not lie about where the numbers come from ─────────────────────────────
    ///
    ///   - <see cref="VpOverVs"/> = √3 is **real physics** (the ratio of P to S wave speed
    ///     in a Poisson solid). The ratio alone is genuine.
    ///   - **The absolute speeds are not.** The real crust has Vp ≈ 6 km/s, so across a CS
    ///     city (a little over 10 km on the diagonal) S-P would be under a second and would
    ///     not open by a single pixel on screen. What is fixed here is "at the reference
    ///     distance <see cref="ReferenceDistanceMetres"/> the P arrives at
    ///     <see cref="PArrivalFractionAtReference"/> of the way into the window".
    ///     **That is a scale chosen so it can be shown; it is neither measured nor real.**
    ///   - The window (<paramref name="windowFrames"/>) uses vanilla's
    ///     <c>m_activeDuration</c> as it stands. When it cannot be read, the caller takes
    ///     not one sample (the same discipline as <see cref="ShakeWaveform.IsShaking"/>).
    ///
    /// ── Do not alias (preventing a recurrence of whole-mod review I5) ──────────────────
    ///
    /// The record is one point per sim frame (<c>SeismographRecorder</c> backfills the
    /// frames skipped within a tick). So the sample interval is one frame and the Nyquist
    /// period is two frames. The fastest component used here is the P wave's
    /// <see cref="BasePRate"/> = 0.90 rad/frame (a period of ≈7.0 frames), and the seed of
    /// the wander is on a <see cref="NoiseSegmentFrames"/> = 11 frame grid.
    /// **Both get six or more points per cycle.** Even at speed 3 (1 tick = 9 frames) the
    /// backfilling is enough.
    /// <b>Do not add any component faster than this</b> — the instant you do, a fake
    /// long-period wave indistinguishable from the second layer's long-period ground motion
    /// appears on the graph.
    ///
    /// ── Same earthquake, same waveform (it uses <see cref="DeterministicRandom"/> alone) ───────
    ///
    /// All the per-earthquake variation comes out of that earthquake's seed.
    /// <c>VanillaRandomizer</c> is **not used** — that exists solely to "predict the values
    /// vanilla is about to draw", and the synthetic seismogram is something this mod decides
    /// for itself (see the doc on <c>DeterministicRandom</c>).
    /// The frame number is not mixed into the seed, so evaluating the same earthquake at the
    /// same time always gives the same value.
    ///
    /// <see cref="DisplacementAt"/> is a **closed-form expression** in t (it holds no state).
    /// Backfilling a skipped frame afterwards gives the same value as evaluating it once on
    /// that frame.
    /// </summary>
    public struct SeismogramModel
    {
        /// <summary>
        /// The ratio of P to S wave speed. **This alone is real physics** (√3 in a Poisson
        /// solid). The absolute speed is fixed on the
        /// <see cref="ReferenceDistanceMetres"/> side.
        /// </summary>
        public const float VpOverVs = 1.7320508f;

        /// <summary>The reference distance (m) that sets the scale of the arrival times. **A
        /// value chosen so it can be shown.**</summary>
        public const float ReferenceDistanceMetres = 4000f;

        /// <summary>Where the P arrives at the reference distance (as a fraction of the window
        /// length). **A value chosen so it can be shown.**</summary>
        public const float PArrivalFractionAtReference = 0.12f;

        /// <summary>
        /// The S arrival is capped here (as a fraction of the window length).
        /// Past this there is nowhere left to put the coda, and the record at a distant
        /// station becomes a picture of nothing but "the window closing the instant the S
        /// arrives".
        /// **Beyond the cap, increasing the distance no longer widens S-P.**
        /// </summary>
        public const float MaxSArrivalFraction = 0.55f;

        /// <summary>The P wave's amplitude (taking the S wave as 1). Small.</summary>
        public const float PAmplitude = 0.22f;

        /// <summary>The P wave's carrier (rad/frame, a period of ≈7.0 frames). **This is the
        /// fastest component.**</summary>
        public const float BasePRate = 0.90f;

        /// <summary>The S wave's carrier (rad/frame, a period of ≈21 frames). Lower than the
        /// P.</summary>
        public const float BaseSRate = 0.30f;

        /// <summary>The wander's grid (frames). **It must be comfortably longer than two
        /// frames** (see the class doc).</summary>
        public const int NoiseSegmentFrames = 11;

        /// <summary>The floor the wander can drop the amplitude to. At 0 the wave appears to
        /// cut out completely.</summary>
        public const float NoiseFloor = 0.55f;

        /// <summary>The number of frames used for the S wave's onset. **Not 0** (a
        /// discontinuity shows up to both ear and eye).</summary>
        public const float SRiseFrames = 3f;

        /// <summary>The number of frames used for the P wave's onset.</summary>
        public const float PRiseFrames = 1.5f;

        /// <summary>The coda's decay time constant (as a fraction of the window length).</summary>
        public const float CodaFraction = 0.30f;

        /// <summary>The stretch over which it is brought to 0 at the end of the window (as a
        /// fraction of the window length).</summary>
        public const float TaperFraction = 0.08f;

        /// <summary>
        /// The overall gain. **It exists to bring the S wave's peak amplitude to the same
        /// order as vanilla's peak amplitude.**
        ///
        /// The three carriers are divided by the sum of their weights (see
        /// <see cref="ShapeAt"/>), so the bare shape only actually reaches about 0.6
        /// against a theoretical maximum of 1. Swap it in as it stands and you get
        /// **shaking weaker than vanilla's**, i.e. "we made it realistic and it lost its
        /// impact". It clips at ±1 only when all three line up at once, and that is exactly
        /// the shape of a recorder pegging its needle; the proportion of clipping measured
        /// offline shows up in <c>tools/WaveformPreview</c>'s measurements.
        /// </summary>
        public const float Gain = 1.4f;

        /// <summary>The floor on the window this type can handle (frames). Below it, no
        /// waveform is produced.</summary>
        public const int MinWindowFrames = 32;

        // ── Values decided per earthquake from the seed ──────────────────────────────

        private float _window;
        private float _pArrivalScale;
        private float _pRate;
        private float _sRate0;
        private float _sRate1;
        private float _sRate2;
        private float _sPhase0;
        private float _sPhase1;
        private float _sPhase2;
        private float _codaFrames;
        private uint _seed;
        private bool _valid;

        /// <summary>Whether the window length was readable and a waveform may be
        /// produced.</summary>
        public bool Valid { get { return _valid; } }

        /// <summary>This earthquake's shaking window (frames). <c>m_activeDuration</c>
        /// itself.</summary>
        public float WindowFrames { get { return _window; } }

        /// <summary>
        /// Builds the shape for one earthquake from the seed. **Build it once per tick or
        /// frame, not per sample** (<see cref="DisplacementAt"/> holds no state, so
        /// rebuilding gives the same values).
        ///
        /// <paramref name="windowFrames"/> is <c>m_activeDuration</c>.
        /// At 0, or at an extremely short value, <see cref="Valid"/> comes out false and
        /// <see cref="DisplacementAt"/> returns 0 — fix the arrival times without knowing
        /// the window and you get a waveform that goes on after the earthquake has ended.
        /// </summary>
        public static SeismogramModel For(uint seed, uint windowFrames)
        {
            SeismogramModel m = new SeismogramModel();

            if (windowFrames < MinWindowFrames) return m;

            m._seed = seed;
            m._window = windowFrames;
            m._valid = true;

            // Variation in the apparent velocity (±15%). At the same distance, the
            // preliminary tremor duration differs from earthquake to earthquake.
            float velocityJitter = 0.85f + 0.30f * DeterministicRandom.Unit(seed, 1u);
            m._pArrivalScale = PArrivalFractionAtReference * m._window * velocityJitter
                               / ReferenceDistanceMetres;

            // The carrier is shaken by ±10% only. **Do not raise the upper end** (see the
            // class doc on aliasing).
            m._pRate = BasePRate * (0.92f + 0.16f * DeterministicRandom.Unit(seed, 2u));

            // The S is three incommensurate components. With one you are back to "the same
            // waveform going on and on".
            float sJitter = 0.90f + 0.20f * DeterministicRandom.Unit(seed, 3u);
            m._sRate0 = BaseSRate * sJitter;
            m._sRate1 = BaseSRate * sJitter * 1.618f;   // Golden ratio: keeps the set incommensurate
            m._sRate2 = BaseSRate * sJitter * 0.577f;

            m._sPhase0 = 6.2831853f * DeterministicRandom.Unit(seed, 4u);
            m._sPhase1 = 6.2831853f * DeterministicRandom.Unit(seed, 5u);
            m._sPhase2 = 6.2831853f * DeterministicRandom.Unit(seed, 6u);

            m._codaFrames = CodaFraction * m._window
                            * (0.75f + 0.50f * DeterministicRandom.Unit(seed, 7u));
            if (m._codaFrames < 1f) m._codaFrames = 1f;

            return m;
        }

        /// <summary>
        /// The P wave arrival (frames from the head of the window, <c>e = 0</c>).
        /// Proportional to distance. 0 if <see cref="Valid"/> is false.
        /// </summary>
        public float PArrivalFrames(float distanceMetres)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || distanceMetres < 0f) distanceMetres = 0f;

            float p = distanceMetres * _pArrivalScale;
            float maxP = MaxSArrivalFraction * _window / VpOverVs;
            return p > maxP ? maxP : p;
        }

        /// <summary>
        /// The S wave arrival (as above). <c>P × √3</c>. **Directly above the hypocentre it
        /// arrives with the P** (at distance 0, S-P is 0 too) — that is not an
        /// approximation, it is simply how it is.
        /// </summary>
        public float SArrivalFrames(float distanceMetres)
        {
            return PArrivalFrames(distanceMetres) * VpOverVs;
        }

        /// <summary>
        /// The preliminary tremor duration (S-P, frames). **Widening with distance** is this
        /// model's headline.
        /// It stops widening past the <see cref="MaxSArrivalFraction"/> cap.
        /// </summary>
        public float SMinusPFrames(float distanceMetres)
        {
            return PArrivalFrames(distanceMetres) * (VpOverVs - 1f);
        }

        /// <summary>
        /// The signed displacement. <paramref name="t"/> is <c>e</c> (frames, fractions
        /// included).
        ///
        /// The amplitude baseline is twice
        /// <see cref="ShakeWaveform.PeakAmplitudeAt"/>, the same as vanilla (i.e.
        /// <see cref="ShakeWaveform.MaxDisplacement"/>), so **full scale is shared with
        /// vanilla's waveform**. Drawn side by side, the vertical scales line up.
        ///
        /// 0 outside the window, when <see cref="Valid"/> is false, and for NaN.
        /// </summary>
        public float DisplacementAt(float distanceMetres, float t)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || float.IsNaN(t)) return 0f;
            if (t <= 0f || t >= _window) return 0f;

            float shape = ShapeAt(distanceMetres, t);
            if (shape == 0f) return 0f;

            // Twice PeakAmplitudeAt = exactly MaxDisplacement (0.60) at distance 0.
            return shape * 2f * ShakeWaveform.PeakAmplitudeAt(distanceMetres);
        }

        /// <summary>
        /// The shape with the distance taken out (the absolute value is always at most 1).
        /// The tests look at that bound directly.
        /// </summary>
        public float ShapeAt(float distanceMetres, float t)
        {
            if (!_valid) return 0f;
            if (float.IsNaN(distanceMetres) || float.IsNaN(t)) return 0f;
            if (t <= 0f || t >= _window) return 0f;

            float tP = PArrivalFrames(distanceMetres);
            float tS = tP * VpOverVs;

            float value = 0f;

            // ── The P wave: small, fast, and gone before the S arrives ──────────────
            if (t >= tP)
            {
                float dt = t - tP;
                float tauP = 0.35f * (tS - tP);
                if (tauP < 4f) tauP = 4f;

                float envelope = Rise(dt, PRiseFrames) * Decay(dt, tauP);
                value += PAmplitude * envelope
                         * (float)System.Math.Sin(t * _pRate);
            }

            // ── The S wave and the coda: large, slow, decaying irregularly ────────────
            if (t >= tS)
            {
                float dt = t - tS;
                float envelope = Rise(dt, SRiseFrames) * Decay(dt, _codaFrames);

                // The wander. **This one place is the only thing making the coda's
                // amplitude irregular.** Wobbling the carrier frequency would eat into the
                // aliasing margin (see the class doc).
                envelope *= NoiseFloor + (1f - NoiseFloor) * Wobble(t);

                float carrier =
                    (float)(System.Math.Sin(t * _sRate0 + _sPhase0)
                            + 0.55 * System.Math.Sin(t * _sRate1 + _sPhase1)
                            + 0.40 * System.Math.Sin(t * _sRate2 + _sPhase2))
                    / 1.95f;

                value += envelope * carrier;
            }

            value *= Gain;

            // Bring it to 0 at the end of the window. Without that the camera jolts the
            // instant the window closes.
            float taper = TaperFraction * _window;
            if (taper > 0f)
            {
                float left = _window - t;
                if (left < taper) value *= left / taper;
            }

            if (value > 1f) return 1f;
            if (value < -1f) return -1f;
            return value;
        }

        /// <summary>The onset [0,1]. Rises linearly up to <paramref name="frames"/>.</summary>
        private static float Rise(float dt, float frames)
        {
            if (frames <= 0f) return 1f;
            if (dt >= frames) return 1f;
            return dt <= 0f ? 0f : dt / frames;
        }

        /// <summary>Exponential decay <c>exp(-dt/tau)</c>. Do not let <paramref name="tau"/>
        /// go below 1.</summary>
        private static float Decay(float dt, float tau)
        {
            if (tau < 1f) tau = 1f;
            float x = dt / tau;
            // Past eight time constants (amplitude 1/3000) 0 will do. Saves calling exp.
            if (x > 8f) return 0f;
            return (float)System.Math.Exp(-x);
        }

        /// <summary>
        /// An irregular envelope in [0,1] (value noise). Random numbers every
        /// <see cref="NoiseSegmentFrames"/> frames, joined with a smoothstep.
        /// **Do not mix t's fractional part or the frame number itself into the random
        /// draw** — mix them in and evaluating the same time twice gives different values,
        /// and it stops being a closed-form expression.
        /// </summary>
        private float Wobble(float t)
        {
            float scaled = t / NoiseSegmentFrames;
            int cell = (int)scaled;
            if (scaled < 0f) cell = 0;

            float f = scaled - cell;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;

            float a = DeterministicRandom.Unit(_seed, unchecked((uint)cell) + 0x51ED2701u);
            float b = DeterministicRandom.Unit(_seed, unchecked((uint)(cell + 1)) + 0x51ED2701u);

            float s = f * f * (3f - 2f * f);
            return a + (b - a) * s;
        }
    }
}
