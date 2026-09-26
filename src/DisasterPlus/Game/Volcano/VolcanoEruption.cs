using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>The progression itself</b> of the summit eruption (the strength envelope and the vent's
    /// coordinates). **Sim thread only.**
    ///
    /// <code>
    /// [sim ] Tick()   only decides the eruption strength and the summit coordinates. It builds no Unity object
    /// [main]          the drawing is done by VolcanoEruptionFx / VolcanoPyroclasticFx (separate types)
    /// </code>
    ///
    /// ── ★ the drawing moved out of this type ──────────────────────────────────────────────
    ///
    /// This used to draw the plume with its own <c>ParticleSystem</c> and its own
    /// <c>Material</c>, with <c>BuildingProperties.m_fireEffect</c> layered on top.
    /// **In the live game not one particle was ever drawn** — because it was an environment where
    /// <c>Shader.Find</c> returned null for every name, including the built-in
    /// <c>"Standard"</c>.
    /// It now draws by borrowing <b>the game's own particle effects</b>. That set lives in
    /// <see cref="VolcanoEruptionFx"/> and <see cref="VolcanoPyroclasticFx"/>, and
    /// **this type holds not a single Unity object.**
    /// Choosing, cloning and cleaning up the borrowings all live in one place,
    /// <see cref="VolcanoVanillaFx"/>.
    ///
    /// ── the audio path (described to match the measurements) ───────────────────────────────
    ///
    /// <c>FireEffect.RenderEffect</c> never touches <c>m_soundEffect</c> (measured in IL).
    /// **The particle drawing path emits no sound.** ⑤'s eruption sound is a separate path, where
    /// <see cref="VolcanoEruptionAudio"/> feeds the bundled wav into the game's sound-effects
    /// group.
    ///
    /// ── the per-tick cost (sim) ────────────────────────────────────────────────────────────
    ///
    /// One <c>SampleDetailHeight</c> (4 reads + 3 <c>Lerp</c>s, §B-6) and about 20 floats.
    /// **Zero bytes of allocation.** The summit height is re-sampled every tick because the
    /// <c>UpdateArea</c> that carved the crater takes a few frames to reach
    /// <c>m_detailHeights</c>; read it just once and **the plume sticks to the height from before
    /// the crater was carved**.
    /// </summary>
    public static class VolcanoEruption
    {
        /// <summary>The in-game time an eruption lasts (minutes). **A presentation value ⑤ chose.**</summary>
        private const float TotalMinutes = 24f;

        /// <summary>The segment at which the strength is re-drawn (in-game minutes). **Never mix in the frame number.**</summary>
        private const float BurstMinutes = 2f;

        /// <summary>The fraction used for the rise (0 → 1 between 0 and this value).</summary>
        private const float RiseFraction = 0.08f;

        /// <summary>The fraction at which the decay starts (1 → 0 from here towards 1).</summary>
        private const float DecayFraction = 0.65f;

        /// <summary>The lower end of the strength jitter (each segment draws from <c>[Floor, 1]</c>).</summary>
        private const float JitterFloor = 0.62f;

        /// <summary>
        /// The floor on the strength while the mountain is growing (as a fraction of the sustain
        /// level).
        /// **The eruption does not start once the mountain is built; the eruption builds the
        /// mountain** (SimCity 4's ordering).
        /// The plume and the glow are emitted from the very start of the uplift, and the strength
        /// rises from here to 1 as the uplift progresses.
        /// </summary>
        private const float BuildFloor = 0.35f;

        /// <summary>
        /// How far above the crater floor the vent sits (m).
        /// **Not "above the summit"** (<see cref="SampleVent"/>).
        /// </summary>
        private const float VentLiftMetres = 6f;

        // ── the super-eruption's "great explosion" (2026-08-22, owner's report) ──────────────
        //
        // > When the caldera forms, doesn't the cone body drop a long way and explode massively…?
        //
        // Quite so, and the old ordering was "once the eruption has finished, the mountain quietly
        // sinks".
        // **In a real caldera formation, the roof falling is itself what causes the great
        // explosion** — the cone body drops as a single slab into the nearly-emptied magma chamber
        // and pushes the remaining magma out like a piston. The most violent moment in the
        // eruption's history is **during** the foundering, not after it.
        //
        // So in ⑤ the envelope is <b>pinned to the ceiling of the sustain phase</b> throughout the
        // foundering, and the segments (i.e. the explosions) are packed closer together. The decay
        // begins once the foundering has finished.

        /// <summary>
        /// The fraction of elapsed time after which the foundering may start. It is the point some
        /// way into the sustain phase, after a good deal has been ejected (i.e. when the magma
        /// chamber has started to empty). **A presentation value.**
        /// </summary>
        private const float ClimaxFraction = 0.40f;

        /// <summary>How much the segments are packed together during the great explosion.</summary>
        private const float ClimaxBurstScale = 0.4f;

        /// <summary>The floor on the strength jitter during the great explosion (keep it nearly pegged).</summary>
        private const float ClimaxJitterFloor = 0.9f;

        // ── sim-side state ──────────────────────────────────────────────────────────────────

        private static bool _started;
        private static bool _active;
        private static bool _finished;
        private static Vec3 _centre;
        private static Vec3 _vent;
        private static float _elapsedMinutes;

        /// <summary>
        /// The accumulated real time (in-game minutes). <see cref="_elapsedMinutes"/> is held at
        /// the entrance to the sustain phase while the mountain is growing, so
        /// **use this one for the jitter segments** — use the stopped one and the strength is
        /// never re-drawn while it is growing, and the plume looks completely frozen.
        /// </summary>
        private static float _clockMinutes;

        /// <summary>Whether the mountain is still growing (i.e. it is erupting while it uplifts).</summary>
        private static bool _building;

        /// <summary>Whether we are in the great explosion that accompanies the foundering (<see cref="BeginClimax"/>).</summary>
        private static bool _climax;
        private static float _intensity;
        private static int _burstIndex;
        private static int _bursts;
        private static uint _seed;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>Whether an eruption is in progress (**decided by sim and carried in the snapshot**).</summary>
        public static bool Active { get { return _active; } }

        /// <summary>Whether the eruption has finished. The cue for <see cref="VolcanoState"/> to move to the next phase.</summary>
        public static bool Finished { get { return _finished; } }

        /// <summary>
        /// Whether the mountain is still being piled up (i.e. it is erupting while it uplifts).
        /// **The same ordering as SimCity 4**: the eruption does not start once the mountain is
        /// built; the eruption builds the mountain.
        /// </summary>
        public static bool Building { get { return _building; } }

        /// <summary>The current eruption strength <c>[0,1]</c>. **A quantity ⑤ chose**, not a value from the game.</summary>
        public static float IntensityUnit { get { return _intensity; } }

        /// <summary>
        /// The vent's world coordinates. <c>Y</c> is <b>the crater floor</b> (plus a small lift).
        /// **Not the summit (the rim)** — put it on the rim and the flames, the plume and the
        /// ejecta all float above the hollow
        /// (2026-08-22, live report ②: "the flames in the crater look like they are floating").
        /// </summary>
        public static Vec3 VentWorld { get { return _vent; } }

        /// <summary>How many times the strength has been re-drawn so far (for diagnostics).</summary>
        public static int BurstsSoFar { get { return _bursts; } }

        /// <summary>
        /// Whether the magma chamber has started to empty and **it is time for the roof to fall**
        /// (<see cref="ClimaxFraction"/>). Only the super-eruption's <c>VolcanoState</c> reads it.
        /// </summary>
        public static bool ReadyForCollapse
        {
            get { return _active && !_building && !_finished
                         && _elapsedMinutes >= ClimaxFraction * TotalMinutes; }
        }

        /// <summary>Whether we are in the great explosion. Used by the display and the diagnostics.</summary>
        public static bool InClimax { get { return _climax; } }

        /// <summary>
        /// **The cone body has started to founder.** Hold the envelope at the sustain ceiling and
        /// pack the segments together.
        /// <b>The eruption does not end</b> until the foundering has finished — if it did, the
        /// plume would disappear at what should be the most violent moment.
        /// </summary>
        public static void BeginClimax()
        {
            _climax = true;
        }

        /// <summary>
        /// **It has finished falling.** Release the envelope and let it head into the decay.
        /// What is left between here and <see cref="TotalMinutes"/> is the end of the eruption.
        /// </summary>
        public static void EndClimax()
        {
            _climax = false;
        }

        /// <summary>The most recent failure (**English, for diagnostics**). null if there is none.</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// **Sim thread.** It only decides the plan for the ejecta and builds not a single Unity
        /// object. Call it only from <see cref="VolcanoState"/>'s phase branches.
        /// </summary>
        /// <param name="buildProgressUnit">
        /// Uplift progress [0,1]. **Below 1 means "the mountain is still growing"**, so the
        /// envelope is held at the entrance to the sustain phase and the strength rises with the
        /// progress.
        /// Pass 1 from phases where the uplift has finished.
        /// </param>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes,
                                float buildProgressUnit)
        {
            try
            {
                Step(footprint, deltaMinutes, buildProgressUnit);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the eruption tick threw " + e.GetType().Name;
                _active = false;
                // ★ Do not stall the phase on an exception. The eruption is presentation, and if
                //   it wedges here the player can never place a second volcano.
                _finished = true;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano eruption failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                             "volcano eruption failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes,
                                 float buildProgressUnit)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre)) Start(footprint);
            if (_finished) return;

            float build = Clamp01(float.IsNaN(buildProgressUnit) ? 1f : buildProgressUnit);
            _building = build < 1f;

            if (deltaMinutes > 0f)
            {
                _elapsedMinutes += deltaMinutes;
                _clockMinutes += deltaMinutes;
            }

            // ★★ **While the mountain is growing, hold the envelope at the entrance to the
            //    sustain phase.** Without that, the uplift (30 in-game minutes by default) is
            //    longer than the eruption (24 minutes), so by the time the mountain is finished
            //    the eruption is already over.
            //    Only the envelope is held; _clockMinutes keeps advancing (for the jitter).
            if (_building)
            {
                float sustainStart = RiseFraction * TotalMinutes;
                if (_elapsedMinutes > sustainStart) _elapsedMinutes = sustainStart;
            }

            // ★★ **Hold it the same way during the great explosion** (<see cref="BeginClimax"/>).
            //    The foundering waits on the clearing sweep, so it can run longer than the
            //    eruption (24 minutes).
            //    Without holding it, <b>the plume alone disappears while the mountain is
            //    falling</b>.
            if (_climax)
            {
                float decayStart = DecayFraction * TotalMinutes;
                if (_elapsedMinutes > decayStart) _elapsedMinutes = decayStart;
            }

            if (_elapsedMinutes >= TotalMinutes)
            {
                _active = false;
                _finished = true;
                _intensity = 0f;
                return;
            }

            // ★★ Re-sample the vent every tick. **The mountain is still growing and the crater
            //    floor is rising with it** (the class doc of VolcanoCrater), so reading it once
            //    leaves the flames behind, floating in mid-air (report ②).
            _vent = SampleVent(footprint);

            // ★ The segments come from elapsed in-game time. **Do not build it from
            //   frameIndex % N** (DAYTIME_FRAMES = 65536, one in-game minute ≒ 45.51 frames;
            //   firestorm appendix A-4).
            // ★ During the great explosion the segments are packed together (i.e. the explosions
            //   come one after another).
            float burstMinutes = _climax ? BurstMinutes * ClimaxBurstScale : BurstMinutes;
            int burst = (int)(_clockMinutes / burstMinutes);
            if (burst != _burstIndex)
            {
                _burstIndex = burst;
                _bursts++;
            }

            // ★ Do not mix the frame number into the randomness (the plan's "two random number
            //   generators"). Mix it in and the same eruption is re-drawn every tick, so the
            //   strength jumps about every frame.
            float floor = _climax ? ClimaxJitterFloor : JitterFloor;
            float jitter = floor
                           + (1f - floor)
                             * DeterministicRandom.Unit(_seed, (uint)_burstIndex);

            float envelope = Envelope(_elapsedMinutes / TotalMinutes);
            // While it is growing, build the strength up in step with the mountain's size
            // (a huge plume does not sit on a small mountain).
            if (_building) envelope *= BuildFloor + (1f - BuildFloor) * build;

            _intensity = Clamp01(envelope * jitter);
            _active = true;
        }

        /// <summary>
        /// The rise → sustain → decay envelope <c>[0,1]</c>.
        /// **It is not a physical quantity** (design doc §7.4 / item 5 of the plan's "the range of
        /// assertions we may make").
        /// </summary>
        private static float Envelope(float t)
        {
            if (float.IsNaN(t)) return 0f;
            if (t <= 0f) return 0f;
            if (t >= 1f) return 0f;
            if (t < RiseFraction) return t / RiseFraction;
            if (t > DecayFraction) return (1f - t) / (1f - DecayFraction);
            return 1f;
        }

        private static void Start(VolcanoFootprint footprint)
        {
            Reset();
            _started = true;
            _centre = footprint.Centre;
            _vent = SampleVent(footprint);

            // The seed is decided by the location. **The same spot gives the same eruption even
            // across cities** (DeterministicRandom is a stateless hash).
            _seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));
        }

        /// <summary>
        /// The world coordinates of the vent (= **the crater floor**). **Sim thread only**
        /// (<c>TerrainManager</c>).
        ///
        /// Simply reading the terrain height at the centre is enough — the crater is part of the
        /// height profile (<c>Core/Volcano/VolcanoCrater</c>), so **the centre cell already is the
        /// crater floor**. It is re-read every tick while it grows, so if the floor rises the vent
        /// rises with it.
        ///
        /// If it cannot be read, stand in with "the terrain height at survey time + the finished
        /// crater's floor" — **do not fabricate "plausible" coordinates out of a row of zeroes**
        /// (that would erupt inside the ground), and **do not stand in with the summit** (that is
        /// exactly the look of report ②).
        /// </summary>
        private static Vec3 SampleVent(VolcanoFootprint footprint)
        {
            float x = footprint.Centre.X;
            float z = footprint.Centre.Z;
            float fallback = footprint.GroundHeightMetres
                             + VolcanoCrater.FloorMetresAt(footprint.HeightMetres,
                                                           footprint.HeightMetres);

            float y = fallback;
            try
            {
                if (Singleton<TerrainManager>.exists)
                {
                    y = Singleton<TerrainManager>.instance
                            .SampleDetailHeight(new Vector3(x, 0f, z));
                }
            }
            catch
            {
                y = fallback;
            }

            if (float.IsNaN(y)) y = fallback;
            return new Vec3(x, y + VentLiftMetres, z);
        }

        /// <summary>
        /// Discard the sim-side state. **Call on level unload and when a new volcano starts.**
        /// <c>_errorLogged</c> is not reset (it is a fact about the build of the game, not
        /// per-city state).
        /// </summary>
        public static void Reset()
        {
            _climax = false;
            _started = false;
            _active = false;
            _finished = false;
            _centre = new Vec3(0f, 0f, 0f);
            _vent = new Vec3(0f, 0f, 0f);
            _elapsedMinutes = 0f;
            _clockMinutes = 0f;
            _building = false;
            _intensity = 0f;
            _burstIndex = 0;
            _bursts = 0;
            _seed = 0u;
            _lastFailure = null;
        }

        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcErupt",
                     "eruption " + (_active ? "active" : (_finished ? "finished" : "idle"))
                     + " intensity=" + _intensity.ToString("F2")
                     + " bursts=" + _bursts
                     + " elapsed=" + _elapsedMinutes.ToString("F0") + " min"
                     + " frame=" + frame);
        }

        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
