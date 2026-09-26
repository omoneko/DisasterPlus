using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>What the cloud is doing right now.</summary>
    public enum TyphoonCloudState
    {
        /// <summary>Switched off in the settings (or we are not in a city yet).</summary>
        Off,

        /// <summary>Not drawing, because there is no typhoon. **Not a fault.**</summary>
        NotBuilt,

        /// <summary>
        /// **The main route.** The vortex is built from cloud puffs borrowed from vanilla's
        /// particle effects (<see cref="TyphoonCloudFx"/>).
        /// </summary>
        Puffs,

        /// <summary>The fallback route. Drawing our own spiral mesh every frame.</summary>
        Drawing,

        /// <summary>Building the mesh or the material failed.</summary>
        BuildFailed,

        /// <summary><c>Shader.Find</c> resolved nothing at all.</summary>
        ShaderMissing,
    }

    /// <summary>
    /// The typhoon's huge rotating cloud. <b>Main thread only.</b>
    ///
    /// ── Why we build it from scratch (there is nothing off the shelf) ─────
    ///
    /// <c>DisasterInfo.m_effect</c> **does not exist as a field at all**. Around the
    /// disaster code, only two fields are of type <c>EffectInfo</c> —
    /// <c>DisasterProperties.m_mediumExplosion</c> and <c>MeteorAI.m_impactEffect</c> —
    /// and **neither a cloud nor a vortex is prefabbed** (IL facts document §C-1). And
    /// vanilla's clouds are a noise shader pasted on a sky dome of radius 6400 km, fixed at
    /// <c>(0, m_HorizonOffset, 0)</c> and **with no world coordinates**.
    /// <c>m_Coverage</c> is overwritten every frame with
    /// <c>m_MaxCoverage × SampleCloudCoverage()</c> (§C-2). So compositing "a cloud vortex
    /// at the typhoon's position" is **impossible in principle**. ④ builds its own mesh,
    /// makes its own material, and draws it itself.
    ///
    /// The sky dome is drawn at infinity, so ④'s cloud **always appears in front of it.
    /// There is no depth breakage** (§C-2).
    ///
    /// ── Trap 1: do not borrow CS materials (③ settled this) ──────────────
    ///
    /// CS's shaders demand per-instance data supplied by the engine
    /// (<c>VortexAI.RenderExtraStuff</c> packs <c>ID_TyreMatrix</c> /
    /// <c>ID_TyrePosition</c> / <c>ID_LightState</c> / <c>ID_Color</c> into
    /// <c>VehicleManager.m_materialBlock</c> before its <c>DrawMesh</c>. §C-1). Put one of
    /// those into a hand-rolled <c>DrawMesh</c> and **nothing is drawn, or it comes out
    /// black**. So if we borrow anything it is the mesh only, and we build the material
    /// ourselves (fire whirl §4.9; the same shape as ③'s
    /// <c>FireWhirlFlameFx.FlameMaterial</c>).
    ///
    /// ★ That shader is fetched by <see cref="ShaderPool"/>. An environment was found in
    ///   the game where <c>Shader.Find</c> **returns null even for the built-in
    ///   <c>"Standard"</c>**, so when lookup by name fails we borrow <b>the shader
    ///   only</b> from an already-loaded <c>Material</c> (we do not borrow the
    ///   <c>Material</c> instance. The difference between the two is in that class's doc).
    ///
    /// **If no shader resolves at all we draw nothing**
    /// (<see cref="TyphoonCloudState.ShaderMissing"/>).
    /// So that a future game update cannot make it silently invisible, there is one
    /// matching check in <c>Assumptions</c> as well.
    ///
    /// ── Trap 2: do not make the static cache a <c>Mesh[]</c> (③ settled this) ──
    ///
    /// <c>UnityEngine.Object</c> overloads <c>==</c> so that a destroyed object compares
    /// equal to null, but **that does not apply to comparing the array reference**. A
    /// <c>static Mesh[]</c> stays non-null while holding destroyed meshes and
    /// **goes invisible in the second city, silently and with no log** (fire whirl §4.8).
    /// Here we hold the <c>Mesh</c> and the <c>Material</c> as **one reference each** and
    /// check that reference itself with <c>== null</c> every frame.
    ///
    /// ── We <c>Object.Destroy</c> the <c>Mesh</c> and <c>Material</c> ourselves ──
    ///
    /// Neither is a <c>Component</c>, so destroying a <c>GameObject</c> does not take them
    /// with it (the same shape of leak ② hit with <c>WaveformView</c>'s
    /// <c>Texture2D</c>). <see cref="Destroy"/> destroys both explicitly.
    ///
    /// ── This type is never once called from the sim thread ────────────────
    ///
    /// **That is what makes T9 (the cloud) independent of ④'s other elements.**
    /// Do not add this type to <c>TyphoonController.Forget</c>'s cleanup list — the moment
    /// you do, the cloud becomes a dependency of the typhoon proper and can no longer be
    /// switched off. <c>TyphoonFeature.OnMainThreadUpdate</c> calls
    /// <see cref="Update"/> every frame, and **it cleans up after itself on the frame
    /// where the snapshot goes <c>Active == false</c>**.
    ///
    /// ── ★ This is no longer the main route (the cloud is built from puffs) ──
    ///
    /// Following the owner's note "build the current huge vortex out of cloud", the vortex
    /// is now built by <see cref="TyphoonCloudFx"/> from **cloud puffs borrowed from
    /// vanilla's particle effects**. The mesh route in this file is <b>kept as a
    /// fallback</b>:
    ///
    /// - Even in an environment with not one borrowable particle effect (a game update,
    ///   another mod), it is better to be able to read where the vortex is.
    /// - The <see cref="ShaderPool"/> route **has not once been exercised in the game**, so
    ///   deleting it here would mean deleting not "something we know does not work" but
    ///   "something we do not know whether it works".
    ///
    /// What the fallback looks like is, as the paragraph below says, **a single flat spiral
    /// of roughly 900 m radius — a symbol of a vortex** — not a cloud covering the sky. A
    /// real typhoon's cloud spreads over tens of kilometres. Weighing down the whole sky is
    /// <see cref="ApplyVanillaBoost"/>'s job, and that too does not work in every
    /// environment. **Do not be surprised by a screenshot and say "it is smaller than I
    /// expected".** The decision is in design doc §4.5 and the in-game checklist as well.
    ///
    /// ── The per-frame cost ─────────────────────────────────────
    ///
    /// **One** <c>Graphics.DrawMesh</c> (2304 vertices / 4560 triangles, casting and
    /// receiving no shadows), one <c>Matrix4x4.TRS</c>, and — when the vanilla cloud boost
    /// is enabled — writing three floats. **Zero bytes of heap allocation**
    /// (<c>Matrix4x4</c> / <c>Quaternion</c> / <c>Vector3</c> are all structs, and the
    /// mesh, material and texture are built once per city).
    /// </summary>
    public static class TyphoonCloud
    {
        /// <summary>Turn it slowly (degrees per second). 6 deg/s = one turn in 60 seconds.
        /// **A presentation value ④ chose.**</summary>
        /// <summary>
        /// How fast the vortex turns (degrees per second).
        ///
        /// ★★ Lowered from 6 to 2.2 on 2026-08-22 (the owner's instruction "a slower
        ///   rotation would be fine"). 6 deg/s is one turn in 60 seconds, **more than a
        ///   hundred times faster than a real typhoon** — a satellite image only looks like
        ///   a turning vortex because it is hours of footage sped up.
        ///   At 2.2 one turn takes 164 seconds, which is still fast for the game's flow of
        ///   time but falls within what reads as "slowly swirling".
        /// </summary>
        private const float SpinDegreesPerSecond = 2.2f;

        /// <summary>The cloud's altitude (m). The sky dome is at infinity, so it always
        /// appears in front of it (§C-2).</summary>
        private const float CloudAltitudeMetres = 900f;

        /// <summary>The minimum clearance above the terrain height at the centre (m), so it
        /// does not get buried in a mountain on a mountainous map.</summary>
        private const float MinClearanceMetres = 300f;

        /// <summary>The layer used for drawing. 0 = Default, which is in every camera's
        /// culling mask.</summary>
        private const int CloudLayer = 0;

        // The mesh is built once at a fixed size, and the size is varied through the
        // matrix's scale.
        private const float MeshInnerRadius = 120f;
        private const float MeshOuterRadius = 1000f;
        private const float MeshHeightMetres = 140f;

        /// <summary>The maximum multiplier on the vanilla sky's cloud cover (at intensity
        /// 255).</summary>
        private const float BoostMaxCoverage = 1.4f;

        /// <summary>The maximum multiplier on the vanilla sky clouds' flow speed.</summary>
        private const float BoostWindForce = 2.5f;

        /// <summary>The maximum multiplier on the vanilla sky clouds' rate of
        /// deformation.</summary>
        private const float BoostEvolutionSpeed = 2f;

        /// <summary>How many frames to leave before looking for the vanilla sky's cloud
        /// settings again. <c>Resources.FindObjectsOfTypeAll</c> involves an allocation and
        /// a full scan, so we do not run it every frame.</summary>
        private const int BoostRetryFrames = 300;

        /// <summary>How many frames to leave before looking for the shader again
        /// (<see cref="_shaderMissCount"/>). The same thinning as
        /// <see cref="BoostRetryFrames"/>, for the same reason.</summary>
        private const int ShaderRetryFrames = 300;

        // ★ Not an array (trap 2). One reference each, so fake-null self-repair works.
        private static Mesh _mesh;
        private static Material _material;

        /// <summary>The alpha that fades out the ribbon's edges (<c>CloudBandAlpha</c>).
        /// Like <c>Mesh</c> / <c>Material</c> it is not a <c>Component</c>, so
        /// <see cref="Destroy"/> calls <c>Object.Destroy</c> on it itself.</summary>
        private static Texture2D _texture;

        /// <summary>
        /// When no shader resolves, how many frames are left before we go and ask
        /// <see cref="ShaderPool"/> again (whole-project review).
        ///
        /// <c>BuildMaterial</c> is called **every frame** as long as the material cannot be
        /// built, so written naively the resolution attempt runs every frame for the whole
        /// session (<see cref="ShaderPool"/> has thinning of its own on the scan, but this
        /// side keeps its own too).
        /// The <c>Log.Warn</c> was already latched to fire once, but **the search itself had
        /// no such thinning** — what <see cref="ApplyVanillaBoost"/> in this same file
        /// already does with <see cref="BoostRetryFrames"/> had not been copied across here.
        /// </summary>
        private static int _shaderMissCount;

        private static TyphoonCloudState _state = TyphoonCloudState.Off;
        private static float _spinDegrees;
        private static int _lastDrawCalls;
        private static float _lastRadius;

        private static DayNightDynamicCloudsProperties _clouds;
        private static int _boostMissCount;
        private static bool _boostApplied;
        private static float _originalMaxCoverage;
        private static float _originalWindForce;
        private static float _originalEvolutionSpeed;

        /// <summary>Whether we have named once that the vanilla sky's cloud settings are
        /// absent in this environment.</summary>
        private static bool _boostUnavailableLogged;

        /// <summary>Whether the shader failing to resolve has been sounded once through
        /// <c>Log.Warn</c>. **Not reset by <see cref="Destroy"/>** (it is a fact about the
        /// game build, not per-city state).</summary>
        private static bool _shaderWarned;

        /// <summary>The facts about the shader most recently resolved (**<c>Usable</c> is
        /// false if we could not get one**).
        /// Not reset by <see cref="Destroy"/> — for the same reason as
        /// <see cref="_shaderWarned"/>, it is a fact about the game build. All it holds is a
        /// <c>Shader</c> reference; the <c>Material</c> is destroyed by
        /// <see cref="Destroy"/>.</summary>
        private static ShaderPick _pick;

        private static bool _errorLogged;

        public static TyphoonCloudState State { get { return _state; } }

        /// <summary>
        /// The one line for the diagnostics (**in English**). <c>Assumptions</c> gets the
        /// same answer directly from <see cref="ShaderPool"/>, so do not grow a second
        /// accessor here for "is it a particle system" (with two accessors onto the same
        /// fact, one of them goes stale).
        /// </summary>
        public static string ShaderDetail
        {
            get
            {
                return _pick.Usable
                    ? _pick.Describe()
                    : "NONE (no shader resolved; the cloud is not drawn)";
            }
        }

        /// <summary>Whether the vanilla sky's clouds are currently being boosted.</summary>
        public static bool VanillaBoostApplied { get { return _boostApplied; } }

        /// <summary>How many <c>DrawMesh</c> calls were made on the most recent frame
        /// (0 or 1).</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        /// <summary>The outer radius of the cloud most recently drawn (m). For
        /// diagnostics.</summary>
        public static float LastRadiusMetres { get { return _lastRadius; } }

        /// <summary>
        /// **Main thread, every frame.**
        /// It does not matter if <paramref name="snapshot"/> is stale — nobody notices a
        /// cloud sitting at where the centre was one frame ago.
        /// </summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                _state = TyphoonCloudState.BuildFailed;
                _lastDrawCalls = 0;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon cloud failed", e);
                }

                // Give back what we touched. Do not stay holding the vanilla sky's settings
                // on a frame where something threw.
                ReleaseVanillaBoost();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                // ★ Clean up **ourselves** on the frame the typhoon ends. The sim side's
                //   TyphoonController never once calls the cloud (class doc).
                ReleaseVanillaBoost();

                // ★★ **Tell the puff side "there is no typhoon" too.** Without that,
                //    TyphoonCloudFx's state and its RenderEffect count stay stuck at their
                //    last values, and **the diagnostics read as "still drawing"**.
                //    That side drops to Idle and reports 0 on a frame that is not Active
                //    (particles already spawned drift for their lifetime and disappear,
                //    which is the correct look).
                TyphoonCloudFx.Update(snapshot, _spinDegrees);

                _state = TyphoonCloudState.NotBuilt;
                _lastDrawCalls = 0;
                _lastRadius = 0f;
                return;
            }

            // ★ Take the size from **the same single source** as the puff side
            //   (TyphoonCloudFx.VortexRadiusMetres). Decide it in two places and the vortex
            //   jumps in size when we fall back.
            float radius = TyphoonCloudFx.VortexRadiusMetres(snapshot);
            if (!(radius > 0f))
            {
                ReleaseVanillaBoost();
                _state = TyphoonCloudState.NotBuilt;
                _lastDrawCalls = 0;
                _lastRadius = 0f;
                return;
            }

            // ★ Do not turn it while paused (whole-project review). This is a per-frame
            //   main-thread route, so Time.deltaTime keeps advancing through a pause —
            //   the cloud alone was turning over a city where nothing was moving.
            //   SimulationManager.SimulationPaused is a bool property and may be read from
            //   the main thread (treated the same way as ②'s CameraShakeBooster).
            //   **The puffs and the mesh use the same angle**, so the orientation does not
            //   jump when we fall back.
            if (!SimulationIsPaused())
            {
                _spinDegrees += SpinDegreesPerSecond * Time.deltaTime;
                if (_spinDegrees >= 360f) _spinDegrees -= 360f;
            }

            // ★★ **The main route is the puffs** (class doc). If the vortex can be built
            //    from borrowed cloud puffs, we draw not one mesh — and **do not even build
            //    one** — because showing both makes a disc show through inside the puffs,
            //    and because going looking for a shader we will not use every frame fills
            //    the in-game log.
            if (TyphoonCloudFx.Update(snapshot, _spinDegrees))
            {
                _state = TyphoonCloudState.Puffs;
                _lastDrawCalls = 0;
                _lastRadius = radius;
                ApplyVanillaBoost(snapshot.Intensity);
                return;
            }

            // ── Everything below is the fallback route (our own mesh) ────────────

            // ★ Trap 2: look at the reference itself every frame. If it has been destroyed,
            //   Unity's fake-null makes it compare equal to null and it is rebuilt here
            //   (the second city's self-repair).
            if (_mesh == null) _mesh = BuildMesh();
            if (_material == null) _material = BuildMaterial();
            if (_mesh == null || _material == null)
            {
                _lastDrawCalls = 0;
                return;
            }

            Vec3 centre = snapshot.Centre;
            float altitude = centre.Y + MinClearanceMetres;
            if (altitude < CloudAltitudeMetres) altitude = CloudAltitudeMetres;

            float scale = radius / MeshOuterRadius;

            // Everything below is a struct. **Zero bytes of heap allocation** (class doc).
            var position = new Vector3(centre.X, altitude, centre.Z);
            var rotation = Quaternion.AngleAxis(_spinDegrees, Vector3.up);
            var matrix = Matrix4x4.TRS(position, rotation, new Vector3(scale, 1f, scale));

            // ★ Cast and receive no shadows (whole-project review). The four-argument
            //   overload forwards castShadows: true / receiveShadows: true, which would put
            //   **a translucent vortex 900 m up (4560 triangles) into the shadow pass** and
            //   could drop a spiral shadow on the city. The cloud is presentation, not an
            //   occluder.
            //   Leave camera as null (i.e. all cameras) — CS draws the world in the map
            //   editor and photo mode as well as through the in-game camera, so narrowing it
            //   to one makes the cloud disappear there.
            Graphics.DrawMesh(_mesh, matrix, _material, CloudLayer,
                              null,     // camera: all cameras
                              0,        // submeshIndex
                              null,     // MaterialPropertyBlock
                              false,    // castShadows
                              false);   // receiveShadows

            _state = TyphoonCloudState.Drawing;
            _lastDrawCalls = 1;
            _lastRadius = radius;

            ApplyVanillaBoost(snapshot.Intensity);
        }

        // ── The mesh and the material ──────────────────────────────────────

        /// <summary>
        /// Build a <c>Mesh</c> from <c>SpiralMesh</c> (pure data in Core).
        /// It runs once per city. **This is not a per-frame route.**
        /// </summary>
        private static Mesh BuildMesh()
        {
            var source = new Vec3[SpiralMesh.VertexCount];
            var uvSource = new float[SpiralMesh.VertexCount * 2];
            var triangles = new int[SpiralMesh.TriangleIndexCount];
            SpiralMesh.Build(MeshInnerRadius, MeshOuterRadius, MeshHeightMetres,
                             source, uvSource, triangles);

            var vertices = new Vector3[source.Length];
            var uvs = new Vector2[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                vertices[i] = new Vector3(source[i].X, source[i].Y, source[i].Z);
                uvs[i] = new Vector2(uvSource[i * 2], uvSource[i * 2 + 1]);
            }

            var mesh = new Mesh();
            mesh.name = "DisasterPlus_TyphoonCloud";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// **Do not borrow a CS material** (trap 1). We want to draw it translucently, so we
        /// aim first for a particle alpha blend and fall back in order if there is none.
        /// Only when we fall all the way to <c>Standard</c> do we set transparency up by
        /// hand — the default <c>Standard</c> is opaque, so **it would put an opaque grey
        /// disc over the city**.
        /// </summary>
        private static Material BuildMaterial()
        {
            // ★ Do not go looking every frame (the doc on _shaderMissCount).
            if (_shaderMissCount > 0)
            {
                _shaderMissCount--;
                _state = TyphoonCloudState.ShaderMissing;
                return null;
            }

            // ★ There are environments where Shader.Find fails entirely, so when lookup by
            //   name fails we borrow **the shader only** from an already-loaded Material
            //   (ShaderPool).
            _pick = ShaderPool.Resolve(ShaderPreference.AlphaBlended);
            if (!_pick.Usable)
            {
                // ★ Log.Warn is not throttled. This is **a per-frame route** (it retries
                //   every frame as long as the material cannot be built), so we sound it
                //   once and then stay quiet.
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    Log.Warn("typhoon cloud: no usable shader resolved; the cloud is not drawn "
                             + "(Disaster + does not borrow a Cities material - that renders "
                             + "invisible or black in a hand-rolled DrawMesh)");
                }
                _shaderMissCount = ShaderRetryFrames;
                _state = TyphoonCloudState.ShaderMissing;
                return null;
            }

            var m = new Material(_pick.Shader);
            m.name = "DisasterPlus_TyphoonCloud";

            // ★ The texture that fades out the ribbon's edges. SpiralMesh was emitting UVs
            //   and yet _MainTex had never once been assigned (whole-project review), i.e.
            //   the UVs were dead data and we were getting a hard-edged flat fill.
            //   **It carries no colour** (white × tint = tint). If it cannot be built we
            //   simply do not assign it and fall back to exactly the old appearance.
            if (_texture == null) _texture = BuildTexture();
            if (_texture != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _texture);

            // The storm cloud's colour. A particle shader's tint is _TintColor, Standard's
            // is _Color (the distinction ③ settled. FireWhirlFlameFx's doc). **Do not write
            // the one that has no effect and feel satisfied** — only set properties that
            // actually exist.
            var tint = new Color(0.32f, 0.34f, 0.38f, 0.5f);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            // ★ Only switch to transparency when we fell all the way to Standard. Do not
            //   apply it to some other borrowed shader (_Mode / _SrcBlend are Standard's
            //   contract).
            if (_pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>
        /// Build the one alpha texture that fades out the ribbon's edges. Once per city.
        ///
        /// **The RGB is white** (the colour lives in the material's tint.
        /// <c>CloudBandAlpha</c>'s doc). We do not use <c>TextureFormat.Alpha8</c> because a
        /// particle shader multiplies the RGB as well, which in some environments would make
        /// it **pure black**. If it cannot be built we return null and the caller does not
        /// assign <c>_MainTex</c> (i.e. the appearance stays as it was).
        /// </summary>
        private static Texture2D BuildTexture()
        {
            try
            {
                int size = DisasterPlus.Core.Typhoon.CloudBandAlpha.Size;
                var alpha = new byte[size * size];
                DisasterPlus.Core.Typhoon.CloudBandAlpha.Build(alpha);

                var pixels = new Color32[alpha.Length];
                for (int i = 0; i < alpha.Length; i++)
                {
                    pixels[i] = new Color32(255, 255, 255, alpha[i]);
                }

                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                t.name = "DisasterPlus_TyphoonCloudBand";
                // u covers one span along an arm and v one span across the ribbon, so
                // neither repeats. Clamp also keeps the 0 at the edge from wrapping round.
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                t.SetPixels32(pixels);
                t.Apply(false, false);
                return t;
            }
            catch
            {
                // Giving up here still leaves a cloud (just with harder edges). This is not
                // a per-frame route, but we log nothing — failing does not degrade the
                // feature.
                return null;
            }
        }

        /// <summary>
        /// Whether the simulation is stopped. If it cannot be read we come down on "not
        /// stopped" (not because a cloud turning through a pause does less harm than one
        /// that never turns, but to avoid the cloud standing still for ever in an
        /// environment where it cannot be read).
        /// </summary>
        private static bool SimulationIsPaused()
        {
            try
            {
                if (!ColossalFramework.Singleton<SimulationManager>.exists) return false;
                return ColossalFramework.Singleton<SimulationManager>.instance.SimulationPaused;
            }
            catch
            {
                return false;
            }
        }

        // ── Boosting the vanilla sky's clouds (give up quietly if absent) ──────

        /// <summary>
        /// Raise three values on <c>DayNightDynamicCloudsProperties</c> to make the whole
        /// sky heavier and faster. These three are the only ones that are not overwritten
        /// (<c>m_Coverage</c> is crushed every frame by
        /// <c>m_MaxCoverage × SampleCloudCoverage()</c>, so writing it is pointless. §C-2).
        ///
        /// **This type being absent in this environment is legitimate** (DLC and graphics
        /// settings. PARTIAL). If it is absent we put one line in the diagnostics and give
        /// up — ④'s own cloud does not depend on it.
        /// Do not put it in <c>Assumptions</c> (making it a FAIL would cry wolf).
        /// </summary>
        private static void ApplyVanillaBoost(byte intensity)
        {
            if (!ModSettings.TyphoonVanillaCloudBoost.value)
            {
                ReleaseVanillaBoost();
                return;
            }

            // ★ One reference. If it has been destroyed, fake-null makes it compare equal to
            //   null and it is looked up again.
            //
            // ★★ But **do not go looking every frame.** SceneObjects.FindInScene calls
            //    Resources.FindObjectsOfTypeAll<T>(), which **allocates an array and sweeps
            //    every object**. In an environment where this type does not exist
            //    (legitimate. §C-2) it is never found, so written naively an allocation and
            //    a full sweep run every frame for the whole of the typhoon.
            //    We apply the same thinning as TyphoonReader's prefab scan.
            if (_clouds == null)
            {
                if (_boostMissCount > 0)
                {
                    _boostMissCount--;
                    _boostApplied = false;
                    return;
                }
                _clouds = SceneObjects.FindInScene<DayNightDynamicCloudsProperties>();
                if (_clouds == null) _boostMissCount = BoostRetryFrames;
            }

            if (_clouds == null)
            {
                if (!_boostUnavailableLogged)
                {
                    _boostUnavailableLogged = true;
                    Log.Info("typhoon cloud: DayNightDynamicCloudsProperties is not present in "
                             + "this environment; only Disaster +'s own cloud is drawn "
                             + "(this is normal on some DLC/graphics settings)");
                }
                _boostApplied = false;
                return;
            }

            if (!_boostApplied)
            {
                // ★ Take down the original values **once only**. Re-read them every frame
                //   and we would memorise the values we wrote as "the originals" and grow
                //   exponentially.
                _originalMaxCoverage = _clouds.m_MaxCoverage;
                _originalWindForce = _clouds.m_WindForce;
                _originalEvolutionSpeed = _clouds.m_EvolutionSpeed;
                _boostApplied = true;
            }

            // The closer (stronger) the typhoon, the heavier and faster. At intensity 0 it
            // is unchanged.
            float t = intensity / 255f;
            float coverage = _originalMaxCoverage * (1f + (BoostMaxCoverage - 1f) * t);
            if (coverage > 1f) coverage = 1f;

            _clouds.m_MaxCoverage = coverage;
            _clouds.m_WindForce = _originalWindForce * (1f + (BoostWindForce - 1f) * t);
            _clouds.m_EvolutionSpeed = _originalEvolutionSpeed * (1f + (BoostEvolutionSpeed - 1f) * t);
        }

        /// <summary>
        /// Put the vanilla sky's cloud settings back. **Idempotent.** Called on the frame
        /// the typhoon ends, on the frame the setting is switched off, and from
        /// <see cref="Destroy"/>.
        /// </summary>
        public static void ReleaseVanillaBoost()
        {
            if (!_boostApplied) return;
            _boostApplied = false;

            if (_clouds == null) return;   // it went with the city. There is nothing to restore

            _clouds.m_MaxCoverage = _originalMaxCoverage;
            _clouds.m_WindForce = _originalWindForce;
            _clouds.m_EvolutionSpeed = _originalEvolutionSpeed;
        }

        /// <summary>
        /// **Call on level unload and when the cloud is switched off in the settings.**
        /// Main thread only.
        ///
        /// Neither <c>Mesh</c> nor <c>Material</c> is a <c>Component</c>, so destroying a
        /// <c>GameObject</c> does not take them with it. **We <c>Object.Destroy</c> them
        /// ourselves** (class doc). It is idempotent.
        /// </summary>
        public static void Destroy()
        {
            ReleaseVanillaBoost();

            // ★ Always pack away the puff clones (the GameObject and the ParticleEffect
            //   inside it) too. They are created with DontDestroyOnLoad, so left alone they
            //   survive across cities.
            TyphoonCloudFx.Destroy();

            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = null;

            if (_material != null) Object.Destroy(_material);
            _material = null;

            // ★ Texture2D is not a Component either (the same as Mesh / Material; the same
            //   shape of leak ② hit with WaveformView's Texture2D).
            if (_texture != null) Object.Destroy(_texture);
            _texture = null;

            _clouds = null;
            _boostMissCount = 0;
            _shaderMissCount = 0;
            _state = TyphoonCloudState.Off;
            _spinDegrees = 0f;
            _lastDrawCalls = 0;
            _lastRadius = 0f;
            _boostUnavailableLogged = false;
            // ★ _errorLogged is not reset (it is a fact about the game build, not per-city
            //    state. The same decision as ④'s other types).
        }
    }
}
