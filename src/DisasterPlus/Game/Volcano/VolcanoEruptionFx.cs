using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// How the crater looks — <b>the plume, the flames and the fountain at the vent</b>, plus
    /// <b>the blast and the flying ejecta</b> (the substance of which is
    /// <see cref="VolcanoBlastFx"/>. **This type holds the one and only clock and hands it over
    /// to that one**). **Main thread only, every frame.**
    ///
    /// ── ★★ not one home-made polygon is emitted any more ──────────────────────────────────
    ///
    /// This used to emit the plume with its own <c>ParticleSystem</c> and its own
    /// <c>Material</c>. **In the live game not one particle was ever drawn** — because it was an
    /// environment where <c>Shader.Find</c> returned null for every name, including the built-in
    /// <c>"Standard"</c>.
    /// What is emitted now is **three of the game's own particle effects**, all of them
    /// <b>DLC-free</b> (Natural Disasters' explosions and meteors only exist for DLC owners, so
    /// they are never on the default path).
    ///
    /// <code>
    /// plume column  Factory Smoke              cloned, dark grey-brown / 30 particles / life 4-9s
    /// umbrella      Factory Smoke              **a second** clone, pale grey / 95 particles / life 18-34s
    /// flames        Fire Particles             **not cloned** (we want the same look as a building fire)
    /// ejecta        Medium Explosion Particles cloned, gravity turned downwards / particle size 6
    /// </code>
    ///
    /// ── ★★ the plume is a "column" (2026-08-22, live report ③) ────────────────────────────
    ///
    /// > The plume has turned into nothing but a pool of smoke. Please take the MissileMOD
    /// > mushroom-cloud method as a reference and make a realistic plume (not a mushroom cloud …).
    ///
    /// It used to be **one** <c>RenderEffect</c>, welling smoke up from a disc (radius 40–80 m)
    /// directly above the vent. The particles rise on their own initial velocity for 7–16 seconds
    /// and vanish, so what you get is <b>a lump of smoke hanging over the crater</b>. Neither a
    /// column nor an umbrella.
    ///
    /// Now the shape is decided by <c>Core/Volcano/EruptionColumn</c> (pure, with tests), and this
    /// code **merely wells up its 9 segments as cylinders via
    /// <c>SpawnArea(position, up, radius, height)</c>** (the same division of labour as the
    /// missile mod's <c>CloudPuffs</c>. The shape is not borrowed — theirs is a single puff,
    /// ours is a column continuously fed from the crater, and the physics differ).
    /// The three regions (gas-thrust, convective, umbrella) and the downwind lean are in that
    /// class's doc.
    ///
    /// **The total number of particles is the same as before.** The per-segment density is
    /// normalised by area (<c>EruptionColumn.MagnitudeFor</c>) and the weights sum to 1, so the
    /// whole column equals one of the old calls. All that goes up is the number of
    /// <c>RenderEffect</c> calls (1 → at most 9); the particle count per call actually falls.
    ///
    /// ── ★ the height datum is "the vent = the crater floor" (report ②) ────────────────────
    ///
    /// All four sit on <c>snapshot.VentWorld</c> (the crater floor plus a small lift).
    /// Put them on the summit (the crater rim) and **the whole lot floats in mid-air by the depth
    /// of the hollow**. The floor rises with the mountain, so use what the sim side re-sampled
    /// every tick, as is (<c>VolcanoEruption.SampleVent</c>).
    ///
    /// ── how the intensity is applied (magnitude is density, not size) ──────────────────────
    ///
    /// <c>ParticleEffect.RenderEffect</c>'s <c>magnitude</c> is **particle density**; what decides
    /// the apparent size is <c>SpawnArea</c>'s radius.
    /// How each of the two is built from the eruption strength <c>[0,1]</c> is in
    /// <see cref="EruptionEffectPlan"/> (Core, with tests).
    ///
    /// ── ★ fixed bug 1: magnitude was being collapsed into a probability ───────────────────
    ///
    /// It used to go through <c>BuildingProperties.m_fireEffect</c> (a composite
    /// <c>FireEffect</c>). In IL, <c>FireEffect.RenderEffect</c> is
    ///
    /// <code>
    /// probability = RoundToInt(magnitude * 100)   ← magnitude becomes a "probability"
    /// particlesPerSquare = timeDelta * 0.01f      ← magnitude does not enter this
    /// </code>
    ///
    /// and on top of that <c>EmitParticles</c> only ever uses **the first draw** of
    /// <c>new Randomizer(id.RawData)</c>. <c>default(InstanceID)</c> has <c>RawData == 0</c>, and
    /// the first draw of <c>new Randomizer(0).Int32(100)</c> is **always 7**.
    /// So the <c>0.25 + 0.75 * unit</c> ⑤ was passing made <c>7 &gt; 25…100</c> always false →
    /// **it was pinned at "emit" and the density was constant**.
    /// Which means the eruption's intensity never once reached the screen.
    /// **It now calls <c>ParticleEffect</c> directly**, so <c>magnitude</c> is plainly a density
    /// (<c>ParticleEffect</c> passes a constant 100 for <c>probability</c>, i.e. it always emits).
    ///
    /// ── ★ fixed bug 2: the time delta was <c>Time.deltaTime</c> ───────────────────────────
    ///
    /// Vanilla's <c>EffectManager.EndRenderingImpl</c> passes
    /// <c>SimulationManager.m_simulationTimeDelta</c> (measured in IL).
    /// With <c>Time.deltaTime</c> it **keeps erupting while paused, and the density does not
    /// change when you speed the game up**.
    /// It now goes through <see cref="VolcanoVanillaFx.EffectTimeDelta"/>.
    ///
    /// ── ★ what happens when something cannot be looked up ─────────────────────────────────
    ///
    /// **That one thing is simply not emitted.** No exception, one line of log, and the eruption
    /// carries on.
    /// Frames where the camera info cannot be obtained are skipped entirely
    /// (<c>ParticleEffect.RenderEffect</c> dereferences <c>cameraInfo</c> right at the top, so
    /// passing null gives an NRE).
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// At most 11 <c>RenderEffect</c> calls (9 plume segments + flames + ejecta).
    /// **The particle count per call is lower than before** — the per-segment density is
    /// normalised by area, so the whole column comes to the same amount as one of the old plume
    /// calls.
    /// <c>SpawnArea</c> / <c>InstanceID</c> / <c>Vector3</c> are all structs, and there is no heap
    /// allocation on the <c>EmitParticles</c> path (measured in IL).
    /// **Zero bytes of heap allocation.**
    /// The particle count is capped by the automatic throttling against <c>maxParticles</c>.
    ///
    /// ── this type is never called from the sim thread ──────────────────────────────────────
    ///
    /// All it reads is the immutable snapshot in <see cref="VolcanoHub"/>; it never touches
    /// <c>VolcanoEruption</c>'s internal state.
    /// </summary>
    public static class VolcanoEruptionFx
    {
        /// <summary>How far above the vent the foot of the plume column starts (m).</summary>
        private const float PlumeLiftMetres = 8f;

        /// <summary>
        /// The ratio used to estimate the height from the crater floor up to the rim **from the
        /// crater radius**.
        ///
        /// <c>VolcanoShape.CraterDepthOf</c> derives the depth from the mountain's height, but all
        /// that reaches here is **the crater radius** (what the snapshot carries is the affected
        /// range's radius and the final height, and the per-form ratios are both set to 0.12).
        /// Since the radius and the depth use the same ratio, it is fine to treat the rim height
        /// as proportional to the radius.
        /// **Overestimate it** — fall short and the smoke falls back into the bowl, while
        /// overshooting only ever amounts to "it comes out a little higher up".
        /// </summary>
        private const float CraterRimLiftRatio = 0.55f;

        /// <summary>From the crater floor to the rim (m).</summary>
        private static float CraterRimLiftMetres(float craterRadiusMetres)
        {
            if (float.IsNaN(craterRadiusMetres) || craterRadiusMetres <= 0f) return 0f;
            return craterRadiusMetres * CraterRimLiftRatio;
        }

        /// <summary>How far above the vent the flames sit (m).</summary>
        private const float FlameLiftMetres = 3f;

        /// <summary>How far above the vent the ejecta sit (m).</summary>
        private const float EjectaLiftMetres = 4f;

        /// <summary>The salt for drawing the wind direction (<see cref="DeterministicRandom"/>). **Decided from the location alone.**</summary>
        private const uint WindDirectionSalt = 0x57494E44u;

        /// <summary>The salt for drawing the wind speed.</summary>
        private const uint WindSpeedSalt = 0x57535044u;

        /// <summary>The lower bound of the wind speed that leans the plume (m/s). **A presentation value ⑤ chose** (not a meteorological measurement).</summary>
        private const float WindSpeedMinMetresPerSecond = 6f;

        /// <summary>As above, the upper bound.</summary>
        private const float WindSpeedMaxMetresPerSecond = 16f;

        /// <summary>
        /// The clock that ticks the ejecta's window (seconds). **It is the vanilla effect clock** —
        /// <c>EffectManager</c> itself adds <c>m_simulationTimeDelta</c> once per rendered frame,
        /// so ⑤ adds it the same way.
        /// It stops when paused and follows the game speed.
        /// </summary>
        private static float _clockSeconds;

        private static VolcanoVanillaFacts _facts;
        private static bool _renderErrorLogged;
        private static bool _cameraWarned;

        private static bool _plumeDrawn;
        private static bool _flameDrawn;
        private static bool _ejectaDrawn;

        /// <summary>Plume-column segments welled up this frame (for diagnostics. 0 means not one segment of the column is showing).</summary>
        private static int _plumeSegments;

        /// <summary>The height of the last column built (m. For diagnostics).</summary>
        private static float _plumeHeightMetres;

        /// <summary>
        /// Whether anything at all was emitted at the crater this frame.
        /// **It is read by the diagnostics (sim thread), so keep it as a <c>bool</c>** —
        /// do not compare a Unity reference with <c>== null</c> here.
        /// </summary>
        public static bool Drawing
        {
            get
            {
                return _plumeDrawn || _flameDrawn || _ejectaDrawn
                       || VolcanoBlastFx.BlocksDrawn > 0;
            }
        }

        /// <summary>Plume-column segments welled up this frame (for diagnostics).</summary>
        public static int PlumeSegments { get { return _plumeSegments; } }

        /// <summary>The height of the last plume column built (m, relative to the vent. For diagnostics).</summary>
        public static float PlumeHeightMetres { get { return _plumeHeightMetres; } }

        /// <summary>
        /// Which borrowings were actually gated on in the last frame (reported in the diagnostics
        /// and in the panel's refusal).
        ///
        /// ★ <c>DustResolved</c> is <b>not filled in here</b> (it is always false).
        ///   The pyroclastic lookalike belongs to <see cref="VolcanoPyroclasticFx"/> and its
        ///   success is decided separately. If you want to read it, read
        ///   <c>VolcanoPyroclasticFx.DustResolved</c>.
        /// </summary>
        public static VolcanoVanillaFacts Facts { get { return _facts; } }

        /// <summary>The one line reported in the diagnostics (**English**).</summary>
        public static string Detail { get { return VolcanoVanillaFx.Detail; } }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                _plumeDrawn = false;
                _flameDrawn = false;
                _ejectaDrawn = false;

                _plumeSegments = 0;

                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.Error("volcano eruption effects failed", e);
                }
            }
        }

        /// <summary>Whether the swarm of puffs was drawn in the last frame (for diagnostics).</summary>
        public static bool PuffsDrawn { get { return _puffsDrawn; } }

        private static bool _puffsDrawn;

        /// <summary>Say exactly once that the puffs could not be drawn and we fell back to the ash column.</summary>
        private static bool _puffFallbackLogged;

        /// <summary>
        /// **Main thread.** Call when turned off in the settings and on level unload.
        /// The borrowed effects themselves are held by <see cref="VolcanoVanillaFx"/>, so all that
        /// is folded away here is ⑤'s own clock. Idempotent.
        /// </summary>
        public static void Destroy()
        {
            VolcanoBlastFx.Reset();
            // ★ The swarm of puffs is **a GameObject we created**, so it is destroyed here
            //   (unlike the borrowed effects).
            VolcanoPlumePuffFx.Destroy();
            _puffsDrawn = false;
            _clockSeconds = 0f;
            _plumeDrawn = false;
            _flameDrawn = false;
            _ejectaDrawn = false;
            _plumeSegments = 0;
            _plumeHeightMetres = 0f;
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _puffsDrawn = false;
            _plumeDrawn = false;
            _flameDrawn = false;
            _ejectaDrawn = false;
            _plumeSegments = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.EruptionActive)
            {
                // Reset the clock **ourselves** on the frame the eruption ends. Never called from
                // the sim side.
                // ★ Discard the column height with it. Leave it and the diagnostics keep printing
                //   the unreadable line "a 1450 m column when only 0 segments are showing".
                _clockSeconds = 0f;
                _plumeHeightMetres = 0f;
                // ★ Discard the flying rocks too. Leave them and **rocks keep falling out of a
                //   sky where the eruption has finished.**
                VolcanoBlastFx.Reset();

                // ★★ **Fold the swarm of puffs away as well** (2026-08-22, live report
                //    "a bug where the smoke in the middle stays behind").
                //
                //    This ParticleSystem is <b>a renderer</b>, and it resets the particles'
                //    lifetime to 1000 seconds every frame (that class's doc). In other words
                //    <b>leave it in place and it never goes away</b> — unlike the game particles,
                //    it does not die off by itself.
                //    Skip this and the smoke stays stuck to the crater after the eruption ends.
                VolcanoPlumePuffFx.Destroy();
                return;
            }

            // ★ Count the inventory exactly once in the live game (the inventory in the facts doc
            //   is still PARTIAL).
            VolcanoVanillaFx.LogInventoryOnce();

            RenderManager.CameraInfo camera = VolcanoVanillaFx.CameraInfo();
            _facts = new VolcanoVanillaFacts(false, false, false, false, camera != null);

            if (camera == null)
            {
                if (!_cameraWarned)
                {
                    _cameraWarned = true;
                    // ★ This is on the per-frame path, so once only. Do not use Warn.
                    Log.Info("volcano eruption effects: the game is not reporting a camera "
                             + "this frame, so nothing is drawn at the crater; the eruption "
                             + "itself is unaffected");
                }
                return;
            }

            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt > 0f) _clockSeconds += dt;

            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            Vec3 vent = snapshot.VentWorld;

            float craterRadius = VolcanoShape.CraterRadiusOf(snapshot.Footprint.RadiusMetres);

            ParticleEffect ash = VolcanoVanillaFx.AshPlume();
            ParticleEffect umbrella = VolcanoVanillaFx.AshUmbrella();
            ParticleEffect flames = VolcanoVanillaFx.Flames();
            ParticleEffect ejecta = VolcanoVanillaFx.Ejecta();

            // ★ The fourth argument (the dust) is not filled in here. That belongs to
            //   VolcanoPyroclasticFx and never enters this gate (the doc of Facts).
            _facts = new VolcanoVanillaFacts(ash != null, flames != null, ejecta != null,
                                             false, true);

            // ★ On frames where dt == 0 (paused, or unreadable) emitting not one particle is the
            //   right thing. The particle count in continuous mode is proportional to timeDelta,
            //   so passing it through would come to 0 anyway.
            if (dt <= 0f) return;

            // ★ Build the column's shape once. The plume, the lightning and the swarm of puffs
            //   all look at **the same shape**.
            float windX, windZ;
            uint plumeSeed;
            EruptionColumn plume = BuildColumn(snapshot.Footprint.Centre, craterRadius, unit,
                                               out windX, out windZ, out plumeSeed);

            // ★★ **The swarm of cloud puffs** (the owner's request for "chaotic smoke rather than
            //    something geometric").
            //    It does **not replace** the game particles' ash column — the request was "in
            //    addition to the smoke effect", and their roles differ (that one is a fine haze,
            //    this one is big lumps).
            //    The eruption carries on even if it cannot be drawn (you just get the old picture).
            _puffsDrawn = VolcanoPlumePuffFx.Update(
                vent, craterRadius, plume.HeightMetres, unit, _clockSeconds,
                windX, windZ, plumeSeed);

            if (!_puffsDrawn && !_puffFallbackLogged)
            {
                _puffFallbackLogged = true;
                Log.Info("volcano plume: the cloud puffs are not drawing ("
                         + (VolcanoPlumePuffFx.LastFailure ?? "no reason given")
                         + "); falling back to the game's own ash particles");
            }

            // ★★ **The grey game-particle plume is no longer emitted** (2026-08-22, owner's
            //    instruction).
            //
            //    > The white plume effect is the better one, so please omit the existing grey
            //    > smoke effect.
            //
            //    Layered with the white swarm of puffs (VolcanoPlumePuffFx), the fine ash
            //    particles showed through the gaps between the puffs and it looked like
            //    **two plumes overlapping**.
            //    The grey has been moved into the colour of the puffs themselves (their AshColor).
            //
            //    ★ RenderColumn and the AshPlume/AshUmbrella clones are **kept**.
            //      The column's shape (EruptionColumn) is still needed for the plume height and
            //      for the lightning's path, and in an environment where not one puff can be
            //      drawn (the material cannot be looked up) falling back to this is the only way
            //      out.
            _plumeDrawn = _puffsDrawn
                ? false
                : RenderColumn(ash, umbrella, camera, vent, plume, craterRadius, dt);
            _flameDrawn = RenderFlames(flames, camera, vent, craterRadius, unit, dt);
            _ejectaDrawn = RenderEjecta(ejecta, camera, vent, craterRadius, unit, dt);

            // ★ The blast and the ejecta (the flying rocks). **Hand over this type's clock** —
            //   give that one a second clock and the moment it bursts drifts out of step with the
            //   fountain at the vent.
            VolcanoBlastFx.Update(camera, vent, snapshot.Footprint.Centre, snapshot.Footprint,
                                  craterRadius, unit, _clockSeconds, dt,
                                  snapshot.SupereruptionClimax, snapshot.RingFissureRadiusMetres);

            // ★★ The crater's magma pool, the light on the plume, and the lightning inside the
            //    plume. **Particles cannot do these**, so it is our own <c>DrawMesh</c>
            //    (the class doc of <see cref="VolcanoCraterFx"/>).
            //    The column's shape is passed in so that the lightning runs **through the plume**.
            VolcanoCraterFx.Update(snapshot, camera, plume.HeightMetres, plume);
        }

        /// <summary>
        /// This frame's column shape. **Built once, before drawing** — it used to be built inside
        /// <see cref="RenderColumn"/>, but the lightning (<see cref="VolcanoCraterFx"/>) has to
        /// look at **the same shape** (build it separately and the lightning alone runs through a
        /// column of a different width).
        /// </summary>
        private static EruptionColumn BuildColumn(Vec3 centre, float craterRadius, float unit)
        {
            float windX, windZ;
            uint seed;
            return BuildColumn(centre, craterRadius, unit, out windX, out windZ, out seed);
        }

        /// <summary>
        /// As above. **It also returns the wind and the seed** — so that
        /// <see cref="VolcanoPlumePuffFx"/> moves the puffs on the same wind and the same seed.
        /// Draw them separately and <b>the ash column and the swarm of puffs lean in different
        /// directions.</b>
        /// </summary>
        private static EruptionColumn BuildColumn(Vec3 centre, float craterRadius, float unit,
                                                  out float windX, out float windZ,
                                                  out uint seed)
        {
            seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float bearing = 2f * Mathf.PI * DeterministicRandom.Unit(seed, WindDirectionSalt)
                            + EruptionColumn.SwayAt(_clockSeconds);
            float windSpeed = WindSpeedMinMetresPerSecond
                              + (WindSpeedMaxMetresPerSecond - WindSpeedMinMetresPerSecond)
                                * DeterministicRandom.Unit(seed, WindSpeedSalt);

            windX = Mathf.Cos(bearing) * windSpeed;
            windZ = Mathf.Sin(bearing) * windSpeed;

            return new EruptionColumn(craterRadius, unit,
                                      Mathf.Cos(bearing), Mathf.Sin(bearing), windSpeed);
        }

        /// <summary>
        /// <b>The eruption column.</b> The 9 segments decided by
        /// <c>Core/Volcano/EruptionColumn</c> are welled up segment by segment as **cylinders**
        /// via <c>SpawnArea(position, up, radius, height)</c>.
        /// Pushed out every frame in **continuous mode** (<c>timeOffset &lt; 0</c>).
        ///
        /// ★ The umbrella segments are drawn with a separate clone (<c>AshUmbrella</c>) — pale,
        ///   with large, long-lived particles.
        ///   If it cannot be looked up, **the column's clone stands in** (the umbrella just comes
        ///   out denser; it does not disappear).
        ///
        /// ★ The wind is decided from the volcano's location (<see cref="DeterministicRandom"/>),
        ///   so **the same mountain always leans the same way**. Only the single sine of
        ///   <c>SwayAt</c> swings it slowly left and right (a 37-second period). The frame number
        ///   is never mixed in.
        /// </summary>
        private static bool RenderColumn(ParticleEffect column, ParticleEffect umbrella,
                                         RenderManager.CameraInfo camera, Vec3 vent,
                                         EruptionColumn plume, float craterRadius, float dt)
        {
            if (column == null && umbrella == null) return false;

            _plumeHeightMetres = plume.HeightMetres;

            float baseX = vent.X;
            // ★★ **Start it from the rim, not the crater floor** (2026-08-22, the owner's request
            //    "get rid of the smoke lingering in the crater and leave only the rising plume").
            //
            //    <c>VentWorld.Y</c> is **the crater floor** (it was lined up that way when the
            //    flames looked like they were floating). Welling smoke up just 8 m above that
            //    meant the smoke **pooled inside the crater bowl and struggled to escape upwards** —
            //    the result looks like "smoke pooled in the crater" rather than "a rising column".
            //    Lift it to the height of the rim and the smoke that wells up always gets out.
            //    From now on, what you see inside the bowl is the magma pool
            //    (<see cref="VolcanoCraterFx"/>).
            float baseY = vent.Y + CraterRimLiftMetres(craterRadius) + PlumeLiftMetres;
            float baseZ = vent.Z;

            int drawn = 0;
            for (int i = 0; i < plume.SegmentCount; i++)
            {
                EruptionColumnSegment segment = plume.SegmentAt(i);
                if (segment.Magnitude <= 0f) continue;

                ParticleEffect effect = segment.Umbrella
                    ? (umbrella != null ? umbrella : column)
                    : column;
                if (effect == null) continue;

                var area = new EffectInfo.SpawnArea(
                    new Vector3(baseX + segment.OffsetX,
                                baseY + segment.OffsetY,
                                baseZ + segment.OffsetZ),
                    Vector3.up,
                    segment.RadiusMetres,
                    segment.HalfHeightMetres);

                // An empty InstanceID is fine. ParticleEffect passes a constant 100 for
                // probability (i.e. it always emits), so it does not matter that the Randomizer
                // seed is pinned at 0.
                // The building-flag check is also shaped as "skip if building is 0" (measured in IL).
                effect.RenderEffect(default(InstanceID), area,
                                    new Vector3(segment.DriftX, segment.DriftY, segment.DriftZ),
                                    0f, segment.Magnitude,
                                    -1f,   // ★ continuous mode. The same shape as vanilla's sinkhole
                                    dt, camera);
                drawn++;
            }

            _plumeSegments = drawn;
            return drawn > 0;
        }

        /// <summary>The flames in the crater. <b>The game's own building-fire flames themselves.</b></summary>
        private static bool RenderFlames(ParticleEffect effect, RenderManager.CameraInfo camera,
                                         Vec3 vent, float craterRadius, float unit, float dt)
        {
            if (effect == null) return false;

            var area = new EffectInfo.SpawnArea(
                new Vector3(vent.X, vent.Y + FlameLiftMetres, vent.Z),
                Vector3.up,
                EruptionEffectPlan.FlameRadiusMetres(craterRadius, unit));

            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                EruptionEffectPlan.FlameMagnitude(unit), -1f, dt, camera);
            return true;
        }

        /// <summary>
        /// The ejecta. **Never called between bursts** (<c>magnitude</c> returns 0).
        ///
        /// <c>DispatchEffect</c> is not used because <c>EffectManager</c> runs what it queues
        /// **later**. If that ran after ⑤'s clones had been destroyed on level unload, vanilla
        /// would be touching destroyed objects.
        /// Make the window ourselves in continuous mode and the lifetime is entirely in our hands.
        /// </summary>
        private static bool RenderEjecta(ParticleEffect effect, RenderManager.CameraInfo camera,
                                         Vec3 vent, float craterRadius, float unit, float dt)
        {
            if (effect == null) return false;

            float period = EruptionEffectPlan.EjectaPeriodSeconds(unit);
            float phase = EruptionEffectPlan.BurstPhaseSeconds(_clockSeconds, period);
            float magnitude = EruptionEffectPlan.EjectaMagnitude(unit, phase);
            if (magnitude <= 0f) return false;

            var area = new EffectInfo.SpawnArea(
                new Vector3(vent.X, vent.Y + EjectaLiftMetres, vent.Z),
                Vector3.up,
                EruptionEffectPlan.EjectaRadiusMetres(craterRadius));

            effect.RenderEffect(default(InstanceID), area, Vector3.zero, 0f,
                                magnitude, -1f, dt, camera);
            return true;
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
