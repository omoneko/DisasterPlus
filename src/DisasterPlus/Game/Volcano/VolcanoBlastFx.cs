using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The blast and the ejecta.** The crater <b>goes off with a bang</b> at each segment and
    /// blocks arc out onto the flanks and the lower slopes.
    /// **Main thread only, every frame.**
    /// <see cref="VolcanoEruptionFx"/> calls it along with its own clock (do not keep two clocks).
    ///
    /// ── what changed (2026-08-22, the owner's request) ─────────────────────────────────────
    ///
    /// > I'd also like a blast + ejecta animation implemented for the eruption.
    ///
    /// The "ejecta" up to now just kept welling particles up out of a circle above the crater;
    /// **there was no moment of detonation and no rock flying and landing** (that one is the
    /// fountain at the vent. It has been left in place — remove it and the crater goes too quiet).
    /// What this adds is two things:
    ///
    /// <code>
    /// blast   EffectManager.DispatchEffect(Medium Explosion Particles, the crater, …)
    ///         ★ A one-shot. m_renderDuration is 1.0 s, so queuing it once is enough; it decays away
    /// ejecta  Along the parabola decided by Core/Volcano/EjectaBallistics, re-well a small hot
    ///         ball every frame with RenderEffect (at most 16 per blast, with 48 slots)
    ///         → once it lands on the flank or the lower slope, emit dust there for 0.9 s
    /// </code>
    ///
    /// ── "pulsing", not "a single bang" ─────────────────────────────────────────────────────
    ///
    /// A real eruption pulses rather than running continuously. The segment comes from
    /// <c>EruptionEffectPlan.EjectaPeriodSeconds</c> (shorter the stronger it is; 7.5 → 1.6 s),
    /// which is **the same formula as the fountain at the vent** —
    /// use a different formula and the moment of detonation drifts out of step with the fountain's
    /// peaks, and it reads as two unrelated effects.
    ///
    /// ★ On top of that, each blast is split into several bursts, offset via
    ///   <c>DispatchEffect</c>'s <c>startFrame</c> (IL fact §C: firing can be delayed until
    ///   <c>m_startFrame</c>). One burst ends with a "pop", but offset bursts look like a
    ///   **ba-da-boom**. The count comes from <c>Core.Volcano.BlastCluster.CountFor</c>, which
    ///   varies it with the mountain's size and whether it is a great explosion.
    ///
    /// ── ★ what is passed to <c>DispatchEffect</c> is not a clone ──────────────────────────
    ///
    /// That **only queues it; the drawing happens on a later frame**. Queue ⑤'s clone and then
    /// destroy the clone on level unload right afterwards, and vanilla touches a destroyed object.
    /// So what is queued is only <c>VolcanoVanillaFx.BlastOneShot()</c>
    /// (= **the game's own prefab itself**). The flying rocks' trails are on
    /// <c>RenderEffect</c>'s continuous mode, so their lifetime is entirely in our hands
    /// (the same call as in <see cref="VolcanoEruptionFx"/>).
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// One <c>RenderEffect</c> per live rock (48 slots; the rocks in flight and the 0.9 s of dust
    /// after landing share the same slots).
    /// One rock's particles are one ball's worth (radius 9 m = the lower side of
    /// <c>max(100, πr²)</c>), so it is far lighter than a single plume-column segment.
    /// **The ballistics are solved once per blast and baked into an array.**
    /// The arrays are fixed-length and pre-built, so <b>per-frame allocation is zero bytes</b>.
    /// **<c>EjectaBlock</c> is a struct, so this array does not fall into Unity's fake-null trap.**
    /// </summary>
    public static class VolcanoBlastFx
    {
        // ★★ BlastPulses / BlastPulseFrames / BlastPulseShrink were retired on 2026-08-22.
        //
        //    They were "queue three bursts at the same place, 6 frames apart, shrinking each
        //    time". Because what was offset landed in **the same place**, it got denser but never
        //    wider — part of the cause of the live report "the explosion effect isn't to scale;
        //    the blast during a super-eruption in particular is far too feeble".
        //
        //    Now <c>Core.Volcano.BlastCluster</c> decides **the place, the size and the delay**,
        //    and varies the count itself with the mountain's size and whether it is a great
        //    explosion.
        //    The constants are not kept — keep them and someone reads "so it is still three".

        /// <summary>How far above the crater floor the blast and the rocks start (m).</summary>
        private const float LaunchLiftMetres = 4f;

        /// <summary>The salt that mixes the eruption's index into the seed.</summary>
        private const uint BlastSalt = 0x424C5354u;

        // ── state (**all structs and plain values. It holds not one Unity reference**) ──────

        /// <summary>
        /// How many rocks may be in the air at once.
        ///
        /// ★★ <b>One blast's worth (16) is not enough.</b> The longest trajectory runs over 30
        /// seconds, while the interval between blasts is only 1.6 seconds at full strength —
        /// with an array sized for one blast, **the previous rocks vanish in mid-air every time
        /// the next blast comes**.
        /// Keep three blasts' worth and not one rock vanishes even in the tightest case.
        /// </summary>
        private const int MaxLiveBlocks = EjectaBallistics.MaxBlocksPerBlast * 3;

        private static readonly EjectaBlock[] _blocks = new EjectaBlock[MaxLiveBlocks];

        /// <summary>The launch time of each slot (in seconds of ⑤'s effect clock).</summary>
        private static readonly float[] _launchedAt = new float[MaxLiveBlocks];

        /// <summary>The index of the last segment that detonated. <see cref="NoBlast"/> means "not once yet".</summary>
        private static int _lastBlastIndex = NoBlast;

        private const int NoBlast = int.MinValue;

        private static int _blastsSoFar;
        private static int _drawnLastFrame;
        private static bool _dispatchFailedLogged;

        /// <summary>How many bursts the most recent blast was split into (for diagnostics).</summary>
        private static int _burstsLastBlast;

        /// <summary>As above (the entry point read from outside).</summary>
        public static int BurstsLastBlast { get { return _burstsLastBlast; } }

        /// <summary>How many detonations there have been so far (for diagnostics).</summary>
        public static int BlastsSoFar { get { return _blastsSoFar; } }

        /// <summary>The number of rocks drawn this frame (for diagnostics).</summary>
        public static int BlocksDrawn { get { return _drawnLastFrame; } }

        /// <summary>
        /// **Main thread.** Call on the frame the eruption ends, and on level unload.
        /// Cleaning up the borrowings is <see cref="VolcanoVanillaFx"/>'s job, so all that is
        /// folded away here is ⑤'s own plan. Idempotent.
        /// </summary>
        public static void Reset()
        {
            for (int i = 0; i < _blocks.Length; i++)
            {
                _blocks[i] = default(EjectaBlock);
                _launchedAt[i] = 0f;
            }
            _lastBlastIndex = NoBlast;
            _blastsSoFar = 0;
            _drawnLastFrame = 0;
        }

        /// <summary>
        /// **Main thread, every frame.**
        /// <paramref name="clockSeconds"/> is ⑤'s effect clock (it stops when paused and follows
        /// the game speed. <see cref="VolcanoEruptionFx"/> holds it).
        /// </summary>
        /// <param name="climax">
        /// Whether this is the caldera-forming great explosion (<c>VolcanoEruption.InClimax</c>).
        /// </param>
        /// <param name="ringRadiusMetres">
        /// The radius of the ring of fissures (m). **0 means the central vent only.** It only has
        /// meaning during a super-eruption.
        /// </param>
        public static void Update(RenderManager.CameraInfo camera, Vec3 vent, Vec3 centre,
                                  VolcanoFootprint footprint, float craterRadiusMetres,
                                  float unit, float clockSeconds, float dt,
                                  bool climax, float ringRadiusMetres)
        {
            _drawnLastFrame = 0;
            if (camera == null || dt <= 0f) return;
            if (!footprint.Valid) return;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float period = EruptionEffectPlan.EjectaPeriodSeconds(unit);
            if (period > 0f)
            {
                int blastIndex = (int)(clockSeconds / period);
                if (blastIndex != _lastBlastIndex)
                {
                    _lastBlastIndex = blastIndex;
                    _blastsSoFar++;
                    Detonate(seed, blastIndex, vent, footprint, craterRadiusMetres,
                             unit, clockSeconds, climax, ringRadiusMetres);
                }
            }

            RenderBlocks(camera, vent, clockSeconds, dt);
        }

        /// <summary>
        /// The moment of detonation. **Queues the one-shot as the offset bursts
        /// <c>Core.Volcano.BlastCluster</c> lays out** and, at the same time, solves the
        /// rocks' ballistics and bakes them into the array.
        /// </summary>
        private static void Detonate(uint seed, int blastIndex, Vec3 vent,
                                     VolcanoFootprint footprint, float craterRadiusMetres,
                                     float unit, float clockSeconds,
                                     bool climax, float ringRadiusMetres)
        {
            DispatchBlast(seed + (uint)blastIndex * 977u, vent, footprint,
                          craterRadiusMetres, unit, climax, ringRadiusMetres);

            // ★ How many metres the crater floor is above the ground (the spot the mountain was
            //   placed on). VolcanoFootprint.GroundHeightMetres is the terrain height at survey
            //   time.
            float ventAboveBase = vent.Y - footprint.GroundHeightMetres;
            if (float.IsNaN(ventAboveBase) || ventAboveBase < 0f) ventAboveBase = 0f;

            int count = EjectaBallistics.BlocksPerBlast(unit);
            uint blastSeed = DeterministicRandom.Hash(seed, BlastSalt);

            for (int i = 0; i < count; i++)
            {
                EjectaBlock block = EjectaBallistics.Plan(
                    blastSeed, blastIndex, i, unit, footprint.Form,
                    footprint.RadiusMetres, footprint.HeightMetres, ventAboveBase);

                if (!block.Valid) continue;

                int slot = FreeSlot(clockSeconds);
                _blocks[slot] = block;
                _launchedAt[slot] = clockSeconds;
            }
        }

        /// <summary>
        /// A free slot. **If there is none, overwrite the oldest slot** — what disappears then is
        /// the rock most likely to have already landed.
        /// </summary>
        private static int FreeSlot(float clockSeconds)
        {
            int oldest = 0;
            float oldestAge = float.MinValue;

            for (int i = 0; i < _blocks.Length; i++)
            {
                EjectaBlock b = _blocks[i];
                if (!b.Valid) return i;

                float age = clockSeconds - _launchedAt[i];
                if (age >= b.FlightSeconds + EruptionEffectPlan.ImpactSeconds) return i;
                if (age > oldestAge) { oldestAge = age; oldest = i; }
            }

            return oldest;
        }

        /// <summary>
        /// Queue the game's own explosion with <c>DispatchEffect</c>.
        /// **If it cannot be looked up, leave one line and do nothing** (the rocks still fly).
        /// </summary>
        private static void DispatchBlast(uint seed, Vec3 vent, VolcanoFootprint footprint,
                                          float craterRadiusMetres, float unit,
                                          bool climax, float ringRadiusMetres)
        {
            ParticleEffect blast = VolcanoVanillaFx.BlastOneShot();
            if (blast == null) return;

            try
            {
                if (!Singleton<EffectManager>.exists) return;
                EffectManager effects = Singleton<EffectManager>.instance;

                uint startFrame = 0u;
                if (Singleton<SimulationManager>.exists)
                {
                    startFrame = Singleton<SimulationManager>.instance.m_referenceFrameIndex;
                }

                // ★★ **One burst does not make it bigger** (the class doc of
                //    <see cref="BlastCluster"/>).
                //    Widen <c>SpawnArea</c>'s radius and the particles do not get bigger; the
                //    same-sized particles just scatter more thinly. The only approach is to
                //    increase the count and layer them with offsets, and that count is decided by
                //    <b>the mountain's size</b> and <b>whether it is a great explosion</b>.
                float sizeUnit = BlastCluster.SizeUnitOf(
                    footprint.RadiusMetres,
                    VolcanoShape.DefaultRadiusOf(footprint.Form),
                    VolcanoShape.MaxRadiusOf(footprint.Form));

                int count = BlastCluster.CountFor(unit, sizeUnit, climax);
                _burstsLastBlast = count;

                var origin = new Vector3(vent.X, vent.Y + LaunchLiftMetres, vent.Z);

                for (int i = 0; i < count; i++)
                {
                    BlastBurst burst = BlastCluster.For(i, count, unit, sizeUnit, climax,
                                                        craterRadiusMetres, ringRadiusMetres,
                                                        seed);

                    var position = new Vector3(origin.x + burst.OffsetX,
                                               origin.y + burst.OffsetY,
                                               origin.z + burst.OffsetZ);
                    // ★★ **Use the four-argument one.** (2026-08-22, the owner's report
                    //    "the effect looks flat") The three-argument SpawnArea writes
                    //    m_halfHeight = 0 (confirmed at IL_006F-0075), so the particles only well
                    //    up in **a disc of zero thickness**.
                    var area = new EffectInfo.SpawnArea(position, Vector3.up,
                                                        burst.RadiusMetres,
                                                        burst.HalfHeightMetres);

                    // ★ audioGroup may be null. ParticleEffect's RequirePlay() is false, so not
                    //   one entry is queued on the audio side (IL fact §C).
                    effects.DispatchEffect(blast, default(InstanceID), area,
                                           Vector3.zero, 0f, burst.Magnitude, null,
                                           startFrame + (uint)burst.DelayFrames, false);
                }
            }
            catch (System.Exception e)
            {
                // ★ This is a per-segment path (once every few seconds). Even so, say it once
                //   only.
                if (!_dispatchFailedLogged)
                {
                    _dispatchFailedLogged = true;
                    Log.Info("volcano blast: DispatchEffect refused ("
                             + e.GetType().Name + "); the eruption keeps its plume, "
                             + "flames and flying blocks");
                }
            }
        }

        /// <summary>The rocks in flight, and the dust where they landed. **Make the window ourselves in continuous mode.**</summary>
        private static void RenderBlocks(RenderManager.CameraInfo camera, Vec3 vent,
                                         float clockSeconds, float dt)
        {
            ParticleEffect trail = VolcanoVanillaFx.FlyingBlock();
            ParticleEffect dust = VolcanoVanillaFx.PyroclasticDust();
            if (trail == null && dust == null) return;

            float baseY = vent.Y + LaunchLiftMetres;

            for (int i = 0; i < _blocks.Length; i++)
            {
                EjectaBlock block = _blocks[i];
                if (!block.Valid) continue;

                float age = clockSeconds - _launchedAt[i];
                if (age < 0f) continue;

                // ★ Free a slot **on the spot** once the rock has landed and its dust has gone.
                //   Without freeing it, a long-running eruption fills every slot and new rocks
                //   delete old ones in mid-air.
                if (age >= block.FlightSeconds + EruptionEffectPlan.ImpactSeconds)
                {
                    _blocks[i] = default(EjectaBlock);
                    continue;
                }

                if (age < block.FlightSeconds)
                {
                    if (trail == null) continue;

                    float dx, dy, dz;
                    EjectaBallistics.OffsetAt(block, age, out dx, out dy, out dz);

                    var area = new EffectInfo.SpawnArea(
                        new Vector3(vent.X + dx, baseY + dy, vent.Z + dz),
                        Vector3.up,
                        EruptionEffectPlan.BlockTrailRadiusMetres);

                    trail.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                       EruptionEffectPlan.BlockTrailMagnitude(block.SizeUnit),
                                       -1f, dt, camera);
                    _drawnLastFrame++;
                    continue;
                }

                // Impact. Emit a short burst of dust **where it landed**.
                if (dust == null) continue;

                float impactMagnitude = EruptionEffectPlan.ImpactMagnitude(
                    block.SizeUnit, age - block.FlightSeconds);
                if (impactMagnitude <= 0f) continue;

                float ix, iy, iz;
                EjectaBallistics.OffsetAt(block, block.FlightSeconds, out ix, out iy, out iz);

                var impact = new EffectInfo.SpawnArea(
                    new Vector3(vent.X + ix, baseY + iy, vent.Z + iz),
                    Vector3.up,
                    EruptionEffectPlan.ImpactRadiusMetres(block.SizeUnit));

                dust.RenderEffect(default(InstanceID), impact, Vector3.zero, 0f,
                                  impactMagnitude, -1f, dt, camera);
                _drawnLastFrame++;
            }
        }
    }
}
