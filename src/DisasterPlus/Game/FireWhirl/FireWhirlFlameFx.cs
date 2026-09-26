using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Wraps the vortex in flames. Call only from the main thread (it creates Unity
    /// objects).
    ///
    /// We do not borrow CS's <b>materials</b>. CS's shaders demand per-instance data the
    /// engine supplies, so putting one on a plain Renderer draws nothing at all or comes
    /// out pure black. <b>What we borrow is <u>only the shader</u></b>, fetched by
    /// <c>ShaderPool</c>; we always build the <c>Material</c> ourselves (the difference
    /// between the two is in the class doc on <see cref="ShaderPool"/>).
    ///
    /// ★★ <b>If not one shader resolves, we do not draw.</b> In an in-game test
    ///   <c>Shader.Find</c> **returned null for everything, including the built-in
    ///   <c>"Standard"</c>**. This code used to call <c>new Material(s)</c> unchecked
    ///   after a three-step chain ending in <c>?? Shader.Find("Standard")</c>, so
    ///   <c>Internal_CreateWithShader</c> threw a <c>NullReferenceException</c> — and
    ///   since this is a per-frame path, **the same exception appeared on 6,938 lines**.
    ///   ④'s <c>TyphoonCloud</c> and ⑤'s <c>VolcanoLavaFx</c> had a "if it does not
    ///   resolve, do not draw" path from the start and could report a clean FAIL, so ③ is
    ///   brought into the same shape.
    ///
    /// We do not use DispatchEffect because magnitude is nothing but particle density and
    /// the size can only be changed through the EffectInfo.SpawnArea radius. A whirl
    /// varies widely in scale, from 40 to 220 m, so a ParticleSystem of our own is the
    /// more straightforward route.
    ///
    /// Unity 5.6's VelocityOverLifetimeModule has no orbitalX/Y/Z (they were added in
    /// 2018 and later). Instead we rotate the emitter's own Transform every frame and let
    /// that rotation redraw the local-space velocity vector every frame, which gives the
    /// core of the whirl (a rotating emitter plus a local-space VelocityOverLifetime was
    /// the pseudo-orbital technique commonly used in the 5.x generation).
    /// </summary>
    public static class FireWhirlFlameFx
    {
        private static readonly Dictionary<ushort, GameObject> _objects = new Dictionary<ushort, GameObject>();

        // ★ Do not make this an array. Hold a single reference so Unity's fake-null
        //   self-repair still works (§4.8).
        private static Material _flameMaterial;

        // Sync() is called every frame, so the collections it uses are allocated once and
        // reused. Each is Clear()ed before use, so nothing is carried over between calls.
        private static readonly HashSet<ushort> _aliveScratch = new HashSet<ushort>();
        private static readonly List<ushort> _staleScratch = new List<ushort>();

        /// <summary>The vortex's apparent rotation speed (degrees per second). A purely
        /// presentational value, there to give the whirl a core.</summary>
        private const float SpinDegreesPerSecond = 200f;

        /// <summary>
        /// How many frames to leave before looking for a shader again (the same throttle
        /// as ④'s <c>TyphoonCloud</c>). <see cref="FlameMaterial"/> is called every frame
        /// for as long as the material cannot be built, so written naively the search
        /// would run every frame for the whole session.
        /// </summary>
        private const int ShaderRetryFrames = 300;

        /// <summary>How many frames are left before the next search, after one did not
        /// resolve.</summary>
        private static int _shaderMissCount;

        /// <summary>The facts about the most recently resolved shader (**if none was
        /// obtained, <c>Usable</c> is false**).</summary>
        private static ShaderPick _pick;

        /// <summary>Whether we have already sounded <c>Log.Warn</c> once about the shader
        /// not resolving. **Not reset by <see cref="Clear"/>** (it is a fact about the
        /// build of the game, not per-city state — the same judgement as ④ and ⑤).</summary>
        private static bool _shaderWarned;

        /// <summary>
        /// The one line we put into the diagnostics (**in English**). It is **the only
        /// diagnostic output about ③'s appearance**. <c>Assumptions</c> gets the same
        /// answer straight from <see cref="ShaderPool"/>, so do not grow a second outlet
        /// here for "is it a particle system?" (with two outlets for the same fact, one of
        /// them always goes stale).
        /// </summary>
        public static string ShaderDetail
        {
            get
            {
                return _pick.Usable
                    ? _pick.Describe()
                    : "NONE (no shader resolved; the flames are not drawn)";
            }
        }

        /// <summary>Creates, updates and destroys the effects to match the registry's
        /// contents.</summary>
        public static void Sync()
        {
            var views = FireWhirlRegistry.Snapshot();
            float dt = Time.deltaTime;

            // ★ Resolve when we first need to create something, and **only once in this
            //   frame**. Calling it on every pass through the loop would decrement the
            //   throttle counter once per vortex.
            Material flame = null;
            bool flameAsked = false;

            _aliveScratch.Clear();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                _aliveScratch.Add(v.DisasterId);

                GameObject go;
                if (!_objects.TryGetValue(v.DisasterId, out go) || go == null)
                {
                    // go == null also covers Unity's fake null (destroyed across a city
                    // change).
                    if (!flameAsked)
                    {
                        flameAsked = true;
                        flame = FlameMaterial();
                    }

                    // ★★ This is the "do not draw" path. **Never go on to Create with a
                    //   null.** Doing so has new Material(null) throw a
                    //   NullReferenceException, and since this is a per-frame path it
                    //   buries the log (see the class doc). The whirl itself — the
                    //   pinning, the spread, the lifetime — works without this.
                    if (flame == null) continue;

                    go = Create(v.DisasterId, flame);
                    _objects[v.DisasterId] = go;
                }

                go.transform.position = new Vector3(v.Center.X, v.Center.Y, v.Center.Z);
                // The rotation that makes it look like a swirl. Combined with Configure's
                // VelocityOverLifetime (Local), the tangential velocity vector is redrawn
                // every frame, which reads as the core of a whirl.
                go.transform.Rotate(Vector3.up, SpinDegreesPerSecond * dt, Space.World);
                Configure(go, v.Radius);
            }

            // Tidy away the effects of whirls that have gone.
            _staleScratch.Clear();
            foreach (var kv in _objects)
            {
                if (!_aliveScratch.Contains(kv.Key)) _staleScratch.Add(kv.Key);
            }
            for (int i = 0; i < _staleScratch.Count; i++)
            {
                if (_objects[_staleScratch[i]] != null) Object.Destroy(_objects[_staleScratch[i]]);
                _objects.Remove(_staleScratch[i]);
            }
        }

        /// <summary>
        /// The flame material. **Returns null if it cannot be resolved** (i.e. do not
        /// draw). The caller must always check for null.
        /// </summary>
        private static Material FlameMaterial()
        {
            if (_flameMaterial != null) return _flameMaterial;

            // ★ Do not go looking every frame (the same throttle as ④'s TyphoonCloud).
            if (_shaderMissCount > 0)
            {
                _shaderMissCount--;
                return null;
            }

            // ★ There are setups where Shader.Find fails across the board, so if we
            //   cannot look one up by name we borrow **just the shader** off an
            //   already-loaded Material (ShaderPool).
            _pick = ShaderPool.Resolve(ShaderPreference.Additive);

            if (!_pick.Usable)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    // ★ Log.Warn is not throttled. This is a per-frame path, so we sound
                    //   it once and stay quiet afterwards (this is the very path that
                    //   produced 6,938 lines).
                    Log.Warn("fire whirl: no usable shader resolved; the flames are not drawn "
                             + "(Disaster + does not borrow a Cities material - that renders "
                             + "invisible or black on a hand-rolled renderer). The fire whirl "
                             + "itself still spins, stays pinned and still spreads fire");
                }
                _shaderMissCount = ShaderRetryFrames;
                return null;
            }

            // Do not write Material.color. The colour is decided by main.startColor (in
            // Create). Material.color touches _Color, whereas Particles/Additive tints
            // through _TintColor, so putting a colour in here would do nothing at all.
            // Leave a dead line that reads as though it works and sooner or later
            // somebody "fixes" it to _TintColor and the look changes for no reason.
            var m = new Material(_pick.Shader);
            m.name = "DisasterPlus_FireWhirlFlame";

            // ★ The catch-all for when we have fallen all the way to Standard. Without
            //   making it transparent the flames come out as **a swarm of opaque square
            //   slabs**. Do not apply it to a shader borrowed from elsewhere (_Mode and
            //   _SrcBlend are Standard's contract).
            if (_pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            _flameMaterial = m;
            return _flameMaterial;
        }

        private static GameObject Create(ushort disasterId, Material flame)
        {
            var go = new GameObject("DisasterPlus_FireWhirl_" + disasterId);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.2f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.25f, 0.05f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Rise while swirling. The speeds themselves are put in by Configure,
            // according to the radius.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = flame;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return go;
        }

        /// <summary>Varies the shape and the amount with the radius. A whirl's size ranges
        /// from 40 to 220 m.</summary>
        private static void Configure(GameObject go, float radius)
        {
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return;

            var main = ps.main;
            main.startSize = radius * 0.20f;
            main.startSpeed = radius * 0.25f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius * 0.35f;
            shape.angle = 12f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 40f + radius * 1.5f;

            // A local-space tangential (x) plus rising (y) velocity. The emitter itself is
            // rotated every frame by Sync() at SpinDegreesPerSecond, so in world space
            // this local vector keeps turning around a circle, which reads as a whirl.
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(radius * 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(radius * 0.30f);
        }

        /// <summary>
        /// Always call this on level unload.
        /// Leave destroyed Unity objects sitting in a static collection and the flames
        /// silently stop appearing in the second city.
        /// </summary>
        public static void Clear()
        {
            foreach (var kv in _objects)
            {
                if (kv.Value != null) Object.Destroy(kv.Value);
            }
            _objects.Clear();

            if (_flameMaterial != null) Object.Destroy(_flameMaterial);
            _flameMaterial = null;

            _shaderMissCount = 0;
            // ★ _pick and _shaderWarned are not reset. They are facts about the build of
            //   the game, not per-city state (the same as ④ and ⑤). All _pick holds is a
            //   Shader reference; the Material is destroyed above.
        }
    }
}
