using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>What the vortex built out of cloud puffs is doing right now.</summary>
    public enum TyphoonCloudFxState
    {
        /// <summary>Not tried even once yet (or we are not in a city).</summary>
        Off,

        /// <summary>This environment has not one borrowable particle effect. **Fall back to
        /// the mesh.**</summary>
        NoEffect,

        /// <summary>Not drawing, because there is no typhoon. **Not a fault.**</summary>
        Idle,

        /// <summary>Emitting puffs every frame (**borrowed particles**).</summary>
        Emitting,

        /// <summary>
        /// Drawing with **our own white cloud puffs**
        /// (<see cref="TyphoonVortexPuffFx"/>). Since 2026-08-22 this is the default, and
        /// <see cref="Emitting"/> is the fallback.
        /// </summary>
        OwnPuffs,

        /// <summary>It threw and fell over. **Fall back to the mesh.**</summary>
        Failed,
    }

    /// <summary>
    /// Draws the typhoon's vortex as **a cumulonimbus built out of vanilla particle
    /// effects**. <b>Main thread only.</b>
    ///
    /// ── What the owner pointed out (2026-08-22), and why ──────────────────
    ///
    /// &gt; The cloud effect has turned into smoke and it looks very odd.
    /// &gt; Please rebuild it with a swirling cumulonimbus in mind.
    ///
    /// <b>There were two causes, the source material and the shape, and the material was
    /// the decisive one.</b>
    ///
    /// The old implementation put <c>Factory Smoke</c> at the head of the candidates.
    /// Extracting the particle materials' textures from the shipped assets
    /// (<c>sharedassets11.assets</c>) and measuring them:
    ///
    /// <code>
    /// Material       Texture           Average RGB      Appearance
    /// Smoke         smoke            ( 75,  78,  80)  Dark sooty round mass (with specks of ember)
    /// Steam         steam            (168, 184, 189)  Pale blue-white cotton. **Cloud itself**
    /// Water         water            (201, 222, 254)  White-blue spray
    /// Placement Dust placement-dust  (198, 165, 131)  Sand-coloured dust cloud
    /// IndustryDust  IndustryDust     ( 99,  94,  79)  Brown-grey dust
    /// </code>
    ///
    /// **<c>Smoke</c> is not "a cloud painted grey"; it is a picture of soot and embers.**
    /// <c>startColor</c> is applied multiplicatively, so multiplying by white leaves it
    /// dark, and arranged in any shape at all it can only look like smoke.
    /// **The complaint was correct, as a point about the source material.**
    ///
    /// ── Which is why we pick by <b>material</b>, not by name ───────────────
    ///
    /// The inventory in the effects measurement document is <b>PARTIAL</b> (an asset
    /// existing and the runtime API returning it are two different things). The old
    /// implementation, writing a list of names and trying them from the top, was betting
    /// on those names existing in this build. Now we
    ///
    /// 1. <b>actually enumerate</b> <c>EffectsWrapper.m_BuiltinEffects</c> and
    ///    <c>EffectCollection.Effects</c>,
    /// 2. read each <c>ParticleEffect</c>'s
    ///    <c>ParticleSystemRenderer.sharedMaterial.name</c>, and
    /// 3. <b>score by material name</b> and take whichever looks most like cloud
    ///    (<c>Steam</c> ≫ <c>Water</c> &gt; dust &gt; <c>Smoke</c>, with the additive
    ///     fire and explosion materials **excluded**).
    ///
    /// The list of names is **only ever used to break ties**. If the game is updated and
    /// the names change, the same picture comes out as long as the material is the same.
    /// Read <c>sharedMaterial</c> — <c>material</c> **clones the renderer's material and
    /// swaps it in** (damaging the shared state).
    ///
    /// ── The shape belongs to <see cref="VortexPuffLayout"/> and <see cref="VortexCloudProfile"/> ──
    ///
    /// Three layers — <b>the cloud base (dark and flat), the tower (bright and billowing,
    /// two tiers) and the anvil (the whitest and widest)</b> — with **a separate clone**
    /// for each layer. Colour and particle size are shared state on the
    /// <c>ParticleSystem</c> and cannot be changed per <c>RenderEffect</c> call (§B-4), so
    /// **the number of clones is exactly the number of units we want to vary the light and
    /// dark across**. The old implementation had one clone and a uniform grey.
    ///
    /// The scattering is <c>RenderEffect(..., timeOffset: -1f, ...)</c>, i.e.
    /// **continuous mode** (§B-3; the same shape as vanilla's
    /// <c>SinkholeAI.RenderInstance</c>), spawning <b>a cylinder</b> —
    /// <c>SpawnArea(position, up, disc radius, band height)</c> — one tier at a time.
    ///
    /// ── The eye stays a hole (the promise has changed owner) ──────────────
    ///
    /// The old doc said "the Game side is obliged to keep this promise. Break it and the
    /// eye fills in — no exception is raised and no test catches it". **Core keeps it
    /// now.** Both the disc radius and the particle size are declared by
    /// <see cref="VortexPuffLayout"/>, and the tests pin <c>EyeClearanceOf</c> for every
    /// puff.
    ///
    /// The one escape hatch left here is the lower clamp on the particle size
    /// (<see cref="MinSizeMetres"/>). **It only bites on the smallest vortex, and even
    /// there the eye does not fill in** (let us check the arithmetic): at the vortex's
    /// lower bound <see cref="MinVortexRadiusMetres"/> = 900 m the tower's particle size is
    /// 0.040 × 900 = 36 m and is lifted to 40 m — so a puff reaches 2 m further. The
    /// tower's clearance is (0.26 − 0.16 − 0.062 − 0.020) × 900 = 16.2 m, leaving 14.2 m.
    /// **If you raise the lower bound, recompute this.**
    ///
    /// ── The ceiling on per-frame work (three things decide it) ────────────
    ///
    /// 1. <c>RenderEffect</c> exactly <b><see cref="VortexPuffLayout.PuffCount"/>
    ///    times</b> (88; the old implementation did 30). The cost of one call is passing
    ///    <c>SpawnArea</c> by value plus two distance-culling tests, and **zero bytes are
    ///    allocated** (§E). The total number of particles fired is decided by 2. below and
    ///    does not depend on the number of calls, so all that grew is the constant cost of
    ///    the calls.
    /// 2. Newly spawned particles share out <b><see cref="ParticlesPerSecond"/> per
    ///    second</b> across all puffs (<see cref="VortexPuffLayout.MagnitudeFor"/> solves
    ///    §B-4's formula backwards; it depends on neither the frame rate nor the game
    ///    speed).
    /// 3. The total number of live particles is capped by <b>each clone's
    ///    <c>maxParticles</c></b> (2600 + 3600 + 1800 = 8000; the old implementation had
    ///    7000 in one system). Vanilla itself throttles with <c>pps ×= (1 - fill²)</c>
    ///    (§B-4).
    ///
    /// ── Give up quietly if we cannot get one ──────────────────────────────
    ///
    /// Both the enumeration and <c>FindEffect</c> **can come back empty** (unregistered, a
    /// game update, another mod). In that case we set
    /// <see cref="TyphoonCloudFxState.NoEffect"/>, write **one log line** and raise no
    /// exception. The caller (<see cref="TyphoonCloud"/>) falls back to the old mesh route.
    /// Not one of the typhoon's other elements stops.
    /// </summary>
    public static partial class TyphoonCloudFx
    {
        /// <summary>Particles spawned per second across the whole vortex. **A presentation
        /// value ④ chose.**</summary>
        private const float ParticlesPerSecond = 700f;

        /// <summary>
        /// The vortex's outer radius ÷ the storm radius.
        ///
        /// ★★ <b>The old implementation used the gale radius (2.2× the storm radius)
        ///   directly.</b> At intensity 128 that is 10.6 km, i.e. <b>21 km across</b>,
        ///   which is <b>bigger than the map is wide (17.3 km)</b>. It meant scattering the
        ///   same number of puffs over 4.8 times the area, so the puffs spread out
        ///   endlessly, looked like neither a vortex nor a cloud, and read as
        ///   **columns of smoke popping up here and there**. The owner's "it has turned
        ///   into smoke" was driven by this size as well as by the material
        ///   (<c>Smoke</c>).
        ///
        ///   Now we take the storm radius as the basis (the same formula as vanilla's
        ///   lightning scatter circle and hazard disc), capped by
        ///   <see cref="MaxVortexRadiusMetres"/>. **A vortex only looks like a vortex once
        ///   it fits on the map.**
        /// </summary>
        /// <remarks>
        /// ★★ 1.35 -> 2.70 (2026-08-29, the owner's instruction "please make the typhoon
        ///   cloud four times the size, 2x2").
        ///   <b>Four times the area = twice the radius</b> (2x2 means twice in each
        ///   direction).
        ///
        /// ★ The ceiling (<see cref="MaxVortexRadiusMetres"/> = half the map extent) is
        ///   unchanged. Once we hit it, the separate constraint "it must fit on the map"
        ///   wins.
        /// </remarks>
        /// <remarks>
        /// ★★ 2.70 -> 4.05 (2026-09-02, the owner: "make the default size bigger still").
        ///   <b>1.5× the radius = 2.25× the area</b>. The ceiling (half the map extent) is
        ///   unchanged, so strong typhoons still top out there as before, and
        ///   <b>what this affects is middling typhoons, i.e. "the default size"</b>.
        /// </remarks>
        private const float VortexRadiusFactor = 4.05f;

        /// <summary>The ceiling on the vortex's outer radius (m). Half the map extent is
        /// 8640 m, so 12 km across fits within 70% of the map.</summary>
        private const float MaxVortexRadiusMetres = 8640f;

        /// <summary>The floor for the same (m). Any smaller and a single puff fills the
        /// eye.</summary>
        /// <remarks>
        /// ★ The floor doubles too (900 -> 1800). Even a weak typhoon gets four times the
        ///   area.
        /// ★★ Then 1800 -> 2700 (2026-09-02, to match the 1.5× increase in radius).
        ///   The check in the class doc (particle size 0.040 × radius) assumes this floor,
        ///   so **if you lower it, recompute that.**
        /// </remarks>
        private const float MinVortexRadiusMetres = 2700f;

        /// <summary>The floor on particle size (m). Too small and it looks like nothing but
        /// dots. **It only bites on the smallest vortex, and even there the eye does not
        /// fill in** (the check in the class doc).</summary>
        private const float MinSizeMetres = 40f;

        /// <summary>The ceiling on particle size (m). Too large and it looks like a flat
        /// board, and the overlapping transparency is expensive.</summary>
        private const float MaxSizeMetres = 900f;

        /// <summary>The reference altitude of the cloud base (m). **Lower than the old
        /// implementation (900 m)** — a cumulonimbus has a low underside and grows upwards
        /// from there.</summary>
        /// <summary>
        /// The floor on the cloud base (m).
        ///
        /// ★★ Raised from 560 to 1200 on 2026-08-22 (in-game report "the cloud appears
        ///   lower than the lightning, which feels wrong. Could it be higher?").
        ///   Vanilla's lightning runs from the sky to the ground, so with the cloud below
        ///   it, <b>the bolt looks as if it comes down from above, straight through the
        ///   cloud</b>.
        /// </summary>
        private const float BaseAltitudeMetres = 1200f;

        /// <summary>
        /// <b>The origin of the lightning (the top of vanilla's bolt mesh, m).</b> 0 if we
        /// have not measured it yet.
        ///
        /// ★★ **Do not raise it by a guessed constant.** (2026-09-02, in-game report
        ///   "the typhoon cloud is at about half the altitude of the lightning's origin".)
        ///
        ///   Vanilla's bolt places <c>WeatherProperties.m_lightningMesh</c> <b>at the
        ///   strike point, at unit scale with only a rotation applied</b>
        ///   (<c>WeatherManager</c> IL_0231-0256). In other words <b>the height of the
        ///   origin is the mesh's own dimension</b>, and the number appears nowhere in the
        ///   code. Raising it from 560 to 1200 m last time was to fix "lower than the
        ///   lightning", but it was decided <b>without measuring how many metres the
        ///   lightning actually is</b>, so it stopped somewhere in between.
        ///
        ///   So we <b>read it at runtime</b>. <c>m_lightningMesh</c> is public, and
        ///   <c>bounds</c> is the mesh's local bounds, so it is directly the height of the
        ///   origin.
        ///
        /// ★ Re-read it per city (<see cref="Reset"/>). Unity objects become fake-null
        ///   across cities, so do not hold on to it in a static
        ///   (this project's "static cache trap").
        /// </summary>
        private static float _boltTopMetres;

        /// <summary>The value used when the lightning's height could not be measured (m).
        /// The same as the old cloud top.</summary>
        private const float BoltTopFallbackMetres = 3400f;

        /// <summary>
        /// The height of the lightning's origin (m). **Main thread.** Measured once and
        /// then remembered.
        /// </summary>
        private static float BoltTopMetres()
        {
            if (_boltTopMetres > 0f) return _boltTopMetres;

            try
            {
                if (Singleton<WeatherManager>.exists)
                {
                    WeatherProperties props = Singleton<WeatherManager>.instance.m_properties;
                    Mesh mesh = props != null ? props.m_lightningMesh : null;

                    if (mesh != null)
                    {
                        float top = mesh.bounds.max.y;
                        if (top > 1f)
                        {
                            _boltTopMetres = top;
                            Log.Info("typhoon: the vanilla lightning bolt is "
                                     + top.ToString("F0") + " m tall (mesh bounds "
                                     + mesh.bounds.min.y.ToString("F0") + " .. "
                                     + mesh.bounds.max.y.ToString("F0")
                                     + "). The storm cloud is placed so its base sits at "
                                     + "that height - a bolt has to come out of the cloud, "
                                     + "not fall through it.");
                            return _boltTopMetres;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("typhoon: could not measure the lightning bolt ("
                         + e.GetType().Name + "); using "
                         + BoltTopFallbackMetres.ToString("F0") + " m");
            }

            _boltTopMetres = BoltTopFallbackMetres;
            return _boltTopMetres;
        }

        /// <summary>
        /// The height of the cloud base (m). **Placed above both the top of the lightning
        /// and the terrain.**
        /// </summary>
        private static float CloudBaseMetres(float centreY)
        {
            float altitude = centreY + MinClearanceMetres;

            if (altitude < BaseAltitudeMetres) altitude = BaseAltitudeMetres;

            // ★★ With the cloud below the top of the lightning, **the bolt comes down from
            //    above, straight through the cloud**.
            float bolt = BoltTopMetres();
            if (altitude < bolt) altitude = bolt;

            return altitude;
        }

        /// <summary>The minimum clearance above the terrain height at the centre (m), so
        /// it does not get buried in a mountain on a mountainous map.</summary>
        /// <summary>
        /// The minimum lift above the ground (the terrain height at the typhoon's centre)
        /// in metres. 300 → 900 for the same reason as above.
        /// </summary>
        private const float MinClearanceMetres = 900f;

        /// <summary>The cloud's thickness (m). <c>HeightFraction</c> decides where within
        /// it each puff goes.
        /// **Raised from the old implementation's 320 m to 2200 m** — the vertical extent
        /// of a cumulonimbus was the heart of the complaint.
        /// The anvil's band reaches a little above that again, so the cloud top comes out at
        /// roughly 3000 m. Vanilla's tornado mesh is 2000 m tall, so this is not out of
        /// scale with the game.</summary>
        private const float ThicknessMetres = 2200f;

        /// <summary>How fast the cloud base flows around the vortex (m/s). Slower the
        /// higher the layer (<c>SwirlFraction</c>).</summary>
        private const float SwirlMetresPerSecond = 34f;

        /// <summary>The radial speed (m/s). The lower layers draw in, the anvil blows
        /// out.</summary>
        private const float RadialMetresPerSecond = 20f;

        /// <summary>The rising speed (m/s). Strongest inside the tower.</summary>
        private const float RiseMetresPerSecond = 16f;

        /// <summary>It must be visible from a distance (the sources only have 500-1000
        /// m).</summary>
        private const float VisibilityMetres = 12000f;

        /// <summary>How many frames to leave before looking for the effect again.
        /// Enumeration costs a full pass over a <c>Dictionary</c>, so we do not run it every
        /// frame.</summary>
        private const int LookupRetryFrames = 600;

        private static int _lookupMissCount;
        private static TyphoonCloudFxState _state = TyphoonCloudFxState.Off;
        private static int _lastRenderCalls;

        private static bool _errorLogged;

        public static TyphoonCloudFxState State { get { return _state; } }

        /// <summary>How many <c>RenderEffect</c> calls were made on the most recent
        /// frame.</summary>
        public static int LastRenderCalls { get { return _lastRenderCalls; } }

        /// <summary>The one line for the diagnostics (**in English**).</summary>
        public static string EffectDetail
        {
            get
            {
                if (!AnyClone())
                {
                    return "NONE (no vanilla particle effect could be borrowed)";
                }
                return "cloned \"" + (SourceName ?? "?") + "\" [material \""
                       + (SourceMaterial ?? "?") + "\"] into "
                       + CloneCount() + " layer(s): "
                       + VortexPuffLayout.PuffCount + " puffs/frame, "
                       + (int)ParticlesPerSecond + " particles/s, cap " + TotalParticleCap();
            }
        }

        /// <summary>
        /// The vortex's outer radius (m). **The fallback mesh route uses this one too** —
        /// decide it in two places and the puffs and the mesh end up different sizes.
        /// 0 if it could not be read (i.e. draw nothing; do not fill the sky from a guessed
        /// radius).
        /// </summary>
        public static float VortexRadiusMetres(TyphoonSnapshot snapshot)
        {
            if (snapshot == null) return 0f;

            float storm = snapshot.StormRadius;
            if (!(storm > 0f)) return 0f;

            float radius = storm * VortexRadiusFactor;
            if (float.IsNaN(radius)) return 0f;
            if (radius > MaxVortexRadiusMetres) radius = MaxVortexRadiusMetres;
            if (radius < MinVortexRadiusMetres) radius = MinVortexRadiusMetres;
            return radius;
        }

        /// <summary>
        /// **A side-effect-free query that only checks whether we can borrow.**
        /// <c>Assumptions</c> calls this — a verification predicate <b>has to be the very
        /// expression this feature actually gates on</b>, so rather than copying the
        /// candidate ordering over there, we share <see cref="Lookup"/> itself.
        /// It creates no clone and does not touch the thinning counter.
        /// Main thread only (<c>Assumptions.Run</c> is itself main thread only).
        /// </summary>
        public static bool CanBorrow(out string name)
        {
            try
            {
                string material;
                return Lookup(out name, out material) != null;
            }
            catch
            {
                name = null;
                return false;
            }
        }

        /// <summary>
        /// **Main thread, every frame.** true if we drew.
        /// On false the caller falls back to the old mesh route.
        ///
        /// <paramref name="spinDegrees"/> is the rotation angle <see cref="TyphoonCloud"/>
        /// holds (it does not advance while paused). Count it separately here and the
        /// vortex faces different ways on the fallback route and the main one.
        /// </summary>
        public static bool Update(TyphoonSnapshot snapshot, float spinDegrees)
        {
            try
            {
                return Step(snapshot, spinDegrees);
            }
            catch (System.Exception e)
            {
                _state = TyphoonCloudFxState.Failed;
                _lastRenderCalls = 0;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon cloud puffs failed", e);
                }

                // Do not keep hold of broken clones and go on throwing every frame.
                DestroyClones();
                TyphoonVortexPuffFx.Destroy();
                return false;
            }
        }

        private static bool Step(TyphoonSnapshot snapshot, float spinDegrees)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                // ★ Pack away our own puffs so no cloud is left in the sky with no typhoon.
                //   (The borrowed clones disappear on their own once we stop spawning.)
                TyphoonVortexPuffFx.Destroy();

                if (AnyClone()) _state = TyphoonCloudFxState.Idle;
                _lastRenderCalls = 0;
                return false;
            }

            float radius = VortexRadiusMetres(snapshot);
            if (!(radius > 0f))
            {
                _lastRenderCalls = 0;
                return false;
            }

            // ★★ **Draw with our own white cloud first** (2026-08-22, the owner's note
            //    "I can still see something like smoke").
            //
            //    Picking the source material for the borrowed particles had actually
            //    succeeded (the in-game log's <c>resolved: Large Pool Steam</c>), but
            //    **a picture of steam is far too thin to be cloud** — at an opacity of
            //    0.34-0.41 the background shows through
            //    (measurements in <see cref="CloudParticleAssets"/>'s class doc).
            //    On top of that a scattered particle belongs to vanilla's simulation and
            //    drifts, so it cannot hold the vortex's shape.
            //
            //    <see cref="TyphoonVortexPuffFx"/> uses the same
            //    <see cref="VortexPuffLayout"/> table and **re-places white cloud puffs
            //    with an opaque core every frame**.
            float altitudeMetres = CloudBaseMetres(snapshot.Centre.Y);

            // ★★ **The lightning inside the cloud.** Pass <b>the same radius, height and
            //    thickness</b> as the cloud — if they drift apart the lightning flashes
            //    outside the cloud (which is the very symptom we are trying to fix).
            //    Draw the lightning even if the cloud cannot be drawn (the sky may flash
            //    even over a picture that fell back to borrowed particles).
            TyphoonBoltFx.Update(snapshot, VanillaParticles.CameraInfo(),
                                 radius, altitudeMetres, ThicknessMetres);

            if (TyphoonVortexPuffFx.Update(snapshot, radius, spinDegrees,
                                           altitudeMetres, ThicknessMetres))
            {
                _state = TyphoonCloudFxState.OwnPuffs;
                _lastRenderCalls = 0;

                // ★ Do not keep hold of the borrowed ones. Show both and you get **a double
                //   cloud**.
                if (AnyClone()) DestroyClones();
                return true;
            }

            // ★ Everything below is the fallback. It is only reached in an environment
            //   where the alpha-blended shader cannot be resolved (in the game,
            //   <c>Custom/Particles/Alpha Blended</c> does resolve from the loaded
            //   materials).

            // ★ Look at the reference itself every frame. If it has been destroyed,
            //   fake-null makes it compare equal to null and it is rebuilt here (the
            //   second city's self-repair).
            if (!AnyClone() && !Acquire()) return false;

            var camera = VanillaParticles.CameraInfo();
            if (camera == null)
            {
                // ★ Passing null gives an NRE on the first line of
                //   ParticleEffect.RenderEffect (CheckRenderDistance / Intersect).
                //   **Draw nothing and wait quietly.**
                _lastRenderCalls = 0;
                return true;   // we do have the effect. Do not send it to the mesh fallback.
            }

            float timeDelta = VanillaParticles.TimeDelta();
            if (!(timeDelta > 0f))
            {
                // Paused, or speed 0. No new puffs spawn but the vortex remains (the
                // existing particles drift).
                _lastRenderCalls = 0;
                _state = TyphoonCloudFxState.Emitting;
                return true;
            }

            Emit(snapshot, radius, spinDegrees, timeDelta, camera);
            return true;
        }

        private static void Emit(TyphoonSnapshot snapshot, float radius, float spinDegrees,
                                 float timeDelta, RenderManager.CameraInfo camera)
        {
            Vec3 centre = snapshot.Centre;
            float altitude = CloudBaseMetres(centre.Y);

            // Match the particle size to the size of the vortex. **These are the clones,
            // not shared state**, so writing every frame is fine (MainModule is a struct;
            // zero bytes allocated).
            ApplySizes(radius);

            // ★ default(InstanceID)'s RawData is 0, which pins the Randomizer's seed at 0
            //   (§B-6). Calling ParticleEffect directly fixes probability at 100 so there
            //   is no real harm, but putting a non-zero in is good practice.
            InstanceID id = InstanceID.Empty;
            id.Disaster = snapshot.TyphoonId != 0 ? snapshot.TyphoonId : (ushort)1;

            float spin = spinDegrees * 0.0174532925f;
            int calls = 0;

            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff puff = VortexPuffLayout.PuffAt(i);

                ParticleEffect effect = CloneFor(puff.Layer);
                if (effect == null) continue;

                float a = puff.AngleRadians + spin;
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                float r = puff.RadiusFraction * radius;

                var position = new Vector3(centre.X + cos * r,
                                           altitude + puff.HeightFraction * ThicknessMetres,
                                           centre.Z + sin * r);

                // The secondary circulation. Tangential (the way the vortex turns) plus
                // radial (inwards low down, outwards high up) plus rising. **The same sense
                // as the vortex turns** (matching the sign of spin).
                float swirl = SwirlMetresPerSecond * puff.SwirlFraction;
                float radial = RadialMetresPerSecond * puff.RadialFraction;
                var velocity = new Vector3(-sin * swirl + cos * radial,
                                           RiseMetresPerSecond * puff.RiseFraction,
                                           cos * swirl + sin * radial);

                float discRadius = puff.DiscFraction * radius;
                if (!(discRadius > 0f)) continue;

                float band = puff.BandFraction * ThicknessMetres;

                // ★ Solve magnitude afresh for each disc. §B-4's formula acts on area, so
                //   unless the density is lowered in proportion to the widened disc, only
                //   the outside comes out dense.
                float magnitude = VortexPuffLayout.MagnitudeFor(discRadius, RateOverTime,
                                                                ParticlesPerSecond,
                                                                VortexPuffLayout.PuffCount);
                if (!(magnitude > 0f)) continue;

                // SpawnArea(pos, dir, radius, halfHeight) always falls into the
                // "point/disc" path (§B-2). halfHeight scatters **upwards only** over
                // [0, band) (§B-4), which meshes with HeightFraction being the bottom of
                // each tier.
                var area = new EffectInfo.SpawnArea(position, Vector3.up, discRadius, band);

                // timeOffset = -1f means **continuous mode** (§B-3).
                effect.RenderEffect(id, area, velocity, 0f,
                                    magnitude * puff.DensityFraction,
                                    -1f, timeDelta, camera);
                calls++;
            }

            _lastRenderCalls = calls;
            _state = calls > 0 ? TyphoonCloudFxState.Emitting : TyphoonCloudFxState.NoEffect;
        }

        /// <summary>
        /// **Call on level unload and when the cloud is switched off in the settings.**
        /// Main thread only. Idempotent.
        /// </summary>
        public static void Destroy()
        {
            DestroyClones();
            // ★ Destroy our own white cloud's GameObject and its assets (Material /
            //   Texture2D) ourselves too. None of them is a Component, so skip this and one
            //   more set is left behind every time you enter and leave a city.
            TyphoonVortexPuffFx.Destroy();
            // ★ The lightning's mesh, material and texture are ours too, so we destroy them
            //   ourselves.
            TyphoonBoltFx.Destroy();
            CloudParticleAssets.Destroy();
            _lookupMissCount = 0;
            _lastRenderCalls = 0;
            _state = TyphoonCloudFxState.Off;

            // ★★ Forget the lightning's height as well. <c>Mesh</c> is a Unity object, so it
            //    becomes fake-null across cities — do not hold on to it
            //    (this project's "static cache trap").
            _boltTopMetres = 0f;
            // ★ _unavailableLogged / _errorLogged are not reset (class doc).
        }
    }
}
