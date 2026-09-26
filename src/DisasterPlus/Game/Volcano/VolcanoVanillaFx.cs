using System;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Whether the vanilla particle effects ⑤ borrows could actually be looked up in this
    /// environment.
    ///
    /// ★ <b>"Looked up" = drawable.</b> <see cref="VolcanoVanillaFx"/> <b>draws the original
    /// prefab as is</b> even when cloning fails, so each item's <c>bool</c> **is the drawing
    /// side's gate directly** (the clone is an improvement to the colour and the lifetime, not a
    /// precondition).
    /// <c>Assumptions</c>' predicate looks at these same values.
    /// </summary>
    public struct VolcanoVanillaFacts
    {
        /// <summary>Whether the grey smoke used for the plume was looked up.</summary>
        public readonly bool AshResolved;

        /// <summary>Whether the crater flames (<c>Fire Particles</c>) were looked up.</summary>
        public readonly bool FlameResolved;

        /// <summary>Whether the ejecta (<c>Medium Explosion Particles</c>) were looked up.</summary>
        public readonly bool EjectaResolved;

        /// <summary>Whether the pyroclastic lookalike's dust (<c>Collapse Particles</c>) was looked up.</summary>
        public readonly bool DustResolved;

        /// <summary>
        /// Whether <c>RenderManager.instance.CurrentCameraInfo</c> is currently non-null.
        /// <c>ParticleEffect.RenderEffect</c> calls <c>CheckRenderDistance</c> /
        /// <c>Intersect</c> right at the top, so **passing null gives an NRE** (measured in IL).
        /// </summary>
        public readonly bool CameraInfoResolved;

        public VolcanoVanillaFacts(bool ashResolved, bool flameResolved, bool ejectaResolved,
                                   bool dustResolved, bool cameraInfoResolved)
        {
            AshResolved = ashResolved;
            FlameResolved = flameResolved;
            EjectaResolved = ejectaResolved;
            DustResolved = dustResolved;
            CameraInfoResolved = cameraInfoResolved;
        }

        /// <summary>
        /// Whether all three parts of the eruption (plume, flames, ejecta) can be emitted.
        /// **It is the very expression <see cref="VolcanoEruptionFx"/> gates on.**
        /// </summary>
        public bool EruptionUsable
        {
            get { return CameraInfoResolved && AshResolved && FlameResolved && EjectaResolved; }
        }

        /// <summary>
        /// Whether the pyroclastic lookalike can be emitted. **The very gate of
        /// <see cref="VolcanoPyroclasticFx"/>.**
        /// Its success is decided independently of the three eruption parts, so the check is
        /// separate too.
        /// </summary>
        public bool PyroclasticUsable
        {
            get { return CameraInfoResolved && DustResolved; }
        }
    }

    /// <summary>
    /// The place that looks vanilla particle effects up by name, clones them where needed, and
    /// holds them. **Main thread only** (it touches <c>Object.Instantiate</c> and
    /// <c>ParticleSystem</c>).
    ///
    /// ── why the home-made mesh was abandoned ───────────────────────────────────────────────
    ///
    /// In live testing, <c>Shader.Find</c> **returned null for every name, including the built-in
    /// <c>"Standard"</c>**. Not one particle of the home-made-material plume was ever drawn.
    /// The vanilla particle effects, meanwhile, <b>are already loaded and already have a working
    /// material</b>. The conclusion is that what we should have borrowed was there all along.
    ///
    /// ★ "Borrow a Cities material and it goes invisible" (the trap ③ settled) is
    ///   <b>about a home-made <c>MeshRenderer</c> / <c>Graphics.DrawMesh</c></b>.
    ///   A particle's material hangs off the <c>ParticleSystemRenderer</c> and does not demand a
    ///   per-instance <c>MaterialPropertyBlock</c> (vanilla itself plugs a borrowed material in
    ///   at <c>EffectsWrapper.CreateParticleEffect</c>).
    ///   **So it does not apply here.** The lava surface (<see cref="VolcanoLavaFx"/>) is not
    ///   particles, so that one is right to stay on <see cref="ShaderPool"/>.
    ///
    /// ── how to look them up (there are two ways; both are public) ──────────────────────────
    ///
    /// <code>
    /// EffectManager.instance.m_EffectsWrapper.GetBuiltinEffect(name)  everything loaded
    /// EffectCollection.FindEffect(name)                               only the 186 registered
    /// </code>
    ///
    /// Of the four ⑤ uses, <c>Factory Smoke</c> is **not registered** in
    /// <c>EffectCollection</c> (`FindEffect` returns null). So the first port of call is
    /// <c>GetBuiltinEffect</c>, and <c>FindEffect</c> is the fallback.
    /// <c>GetBuiltinEffect</c> returns <c>System.Object</c>, so take it with <c>as</c>
    /// (measured in IL; getting the type wrong gives null rather than an exception).
    ///
    /// ── ★ what happens when something cannot be looked up ─────────────────────────────────
    ///
    /// **One line of log, and that one thing is simply not emitted.** No exception is thrown and
    /// the eruption carries on.
    /// The search is throttled to at most once every <see cref="RetryFrames"/> frames
    /// (<c>FindEffect</c> makes the game print a warning when it does not find something, so
    /// calling it every frame fills up output_log).
    ///
    /// ── ★ the clone is an improvement, not a precondition ─────────────────────────────────
    ///
    /// If cloning fails, **the original prefab is drawn as is**. The colour, the lifetime and the
    /// visibility distance all stay at vanilla's values, which is far better than drawing nothing.
    /// Thanks to this design, <see cref="ScanFacts"/>'s <c>bool</c> **is the drawing side's gate
    /// directly** (so we never create the shape, seen twice in this project, of "the check passed
    /// but the feature does not work").
    ///
    /// ── ★ do not rewrite the original prefab (§D-5) ───────────────────────────────────────
    ///
    /// <c>ParticleSystem.main.startColor</c> / <c>startSize</c> and
    /// <c>ParticleEffect.m_minLifeTime</c> are **shared state on that prefab**.
    /// Rewrite <c>Fire Particles</c> directly and **the colour of every building fire in the city
    /// changes**, and it lingers in memory (though not in the save). Always clone before changing
    /// a value.
    /// The flames (<see cref="Flames"/>) are not cloned — **because we want exactly that look** —
    /// and so not one byte of them is rewritten.
    ///
    /// ── ★ do not make the static cache an array (a bug ③ shipped) ─────────────────────────
    ///
    /// <c>UnityEngine.Object</c>'s <c>==</c> makes a destroyed object compare equal to null, but
    /// put it into a <c>static GameObject[]</c> and that self-repair stops working, so
    /// **it goes invisible in the second city with no message**. Here they are held as one
    /// reference each, and every time we test those references themselves with <c>== null</c>.
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>The source of the plume. A narrow rising jet; the column itself. **Base game.**</summary>
        internal const string AshName = "Factory Smoke";

        /// <summary>The substitute for when <see cref="AshName"/> cannot be looked up (whiter and fatter).</summary>
        internal const string AshAltName = "Factory Steam";

        /// <summary>The flames. Exactly the same thing as a building fire. **Base game.**</summary>
        internal const string FlameName = "Fire Particles";

        /// <summary>The ejecta. Initial speed 100–150 m/s, emission angle 0–80°. **Base game.**</summary>
        internal const string EjectaName = "Medium Explosion Particles";

        /// <summary>The pyroclastic lookalike's dust. The dust of a building collapse. **Base game.**</summary>
        internal const string DustName = "Collapse Particles";

        /// <summary>Frames to leave before searching again (the same throttling as <see cref="ShaderPool"/>).</summary>
        private const int RetryFrames = 300;

        // ── ★ not an array. One reference each ──────────────────────────────────────────────

        private static GameObject _ashObject;
        private static ParticleEffect _ashClone;

        /// <summary>
        /// The second clone, used for the plume column's **umbrella** (pale grey, large
        /// particles, long life).
        /// If it cannot be looked up, the column's clone stands in for it, so this one is
        /// <b>not a gate</b>.
        /// </summary>
        private static GameObject _umbrellaObject;
        private static ParticleEffect _umbrellaClone;
        private static GameObject _ejectaObject;
        private static ParticleEffect _ejectaClone;
        private static GameObject _dustObject;
        private static ParticleEffect _dustClone;

        /// <summary>
        /// The flames (<c>Fire Particles</c>). **The game's own prefab itself, not a clone.**
        /// ★ Not an array. Held as one reference and tested with <c>== null</c> every time.
        /// </summary>
        private static ParticleEffect _flame;

        private static int _ashMiss;
        private static int _ashAltMiss;

        // ★ The umbrella has **its own** throttle counter. Share it with the plume column and it
        //   is decremented twice in the same frame, effectively halving RetryFrames (which
        //   doubles the searching in an environment where it cannot be looked up).
        private static int _umbrellaMiss;
        private static int _umbrellaAltMiss;
        private static int _flameMiss;
        private static int _ejectaMiss;
        private static int _dustMiss;

        /// <summary>Cloning failed, so from now on use the original prefab as is.</summary>
        private static bool _ashCloneRefused;
        private static bool _umbrellaCloneRefused;
        private static bool _ejectaCloneRefused;
        private static bool _dustCloneRefused;

        /// <summary>Flags for saying "could not be looked up" exactly once per name.</summary>
        private static bool _ashMissLogged;
        private static bool _umbrellaRefusedLogged;
        private static bool _flameMissLogged;
        private static bool _ejectaMissLogged;
        private static bool _dustMissLogged;

        private static bool _inventoryLogged;

        // ── the cache the diagnostics read (**bools, ints and strings only**) ───────────────
        //
        // ★★ IDisasterFeature.WriteDiagnostics is **sim thread only**.
        //    Unity objects must not be touched from there — even a reference's == null is a
        //    comparison that drops into native code, and so is outside the main thread's
        //    contract.
        //    So what goes into the diagnostics is only these plain values, written by the main
        //    thread when it resolved something (this is what Detail / *ResolvedCached read).

        private static bool _ashOk;
        private static bool _flameOk;
        private static bool _ejectaOk;
        private static bool _dustOk;
        private static bool _ashCloned;
        private static bool _umbrellaCloned;
        private static bool _ejectaCloned;
        private static bool _dustCloned;

        /// <summary>The inventory measured most recently (reported on one diagnostic line).</summary>
        private static int _effectCount;
        private static int _particleMaterialCount;

        /// <summary>
        /// <c>ParticleEffect.m_particleSystem</c> (private, <c>[NonSerialized]</c>).
        /// Used to confirm **once, at clone time** that <c>InitializeEffect()</c> managed to build
        /// the real thing.
        /// <c>EmitParticles</c> dereferences this field without a null check, so handing a clone
        /// that was never built to the drawing path gives **an NRE inside vanilla**.
        /// </summary>
        private static readonly System.Reflection.FieldInfo ParticleSystemField =
            typeof(ParticleEffect).GetField("m_particleSystem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>The effect used for the plume. null if it cannot be looked up (**the caller quietly skips it**).</summary>
        internal static ParticleEffect AshPlume()
        {
            // ★ Note the resolution result here. The diagnostics (sim thread) cannot touch Unity
            //   objects, so they read this plain bool.
            ParticleEffect resolved = ResolveAsh();
            _ashOk = resolved != null;
            _ashCloned = _ashClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveAsh()
        {
            if (_ashClone != null) return _ashClone;

            // ★ Keep the throttle counters separate per name. Make it one and on the frame the
            //   first one misses the second is always "do not search yet", so the substitute is
            //   never tried.
            ParticleEffect source = Source(AshName, ref _ashMiss, ref _ashMissLogged);
            if (source == null) source = Source(AshAltName, ref _ashAltMiss, ref _ashMissLogged);
            if (source == null) return null;

            if (_ashCloneRefused) return Ready(source) ? source : null;

            // We got here because the clone's reference is dead (a different city, or destroyed).
            // Do not rebuild while leaving the husk behind.
            ReleaseAndDestroy(ref _ashObject, ref _ashClone);

            _ashObject = CloneAsh(source);
            _ashClone = _ashObject == null ? null : _ashObject.GetComponent<ParticleEffect>();
            if (_ashClone == null)
            {
                ReleaseAndDestroy(ref _ashObject, ref _ashClone);
                _ashCloneRefused = true;
                // ★ Do not hand an uninitialised prefab to the drawing path (the doc of Ready).
                return Ready(source) ? source : null;
            }

            return _ashClone;
        }

        /// <summary>
        /// The clone used for the plume column's **umbrella**. Returns <c>null</c> when it cannot
        /// be looked up or cloned — **it does not fall back to the original prefab**. The caller
        /// (<see cref="VolcanoEruptionFx"/>) then draws the umbrella with the column's clone,
        /// which looks better than standing in with a raw <c>Factory Smoke</c> (1 km visibility,
        /// 7 particles).
        /// </summary>
        internal static ParticleEffect AshUmbrella()
        {
            ParticleEffect resolved = ResolveUmbrella();
            _umbrellaCloned = _umbrellaClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveUmbrella()
        {
            if (_umbrellaClone != null) return _umbrellaClone;
            if (_umbrellaCloneRefused) return null;

            // ★ The source prefab is the same as the plume column's, but **the throttle counters
            //   are separate** (share them and they are decremented twice in the same frame,
            //   halving RetryFrames).
            //   Only the single "could not be looked up" line is per name, so sharing that is
            //   fine.
            ParticleEffect source = Source(AshName, ref _umbrellaMiss, ref _ashMissLogged);
            if (source == null)
            {
                source = Source(AshAltName, ref _umbrellaAltMiss, ref _ashMissLogged);
            }
            if (source == null) return null;

            ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);

            _umbrellaObject = CloneAshUmbrella(source);
            _umbrellaClone = _umbrellaObject == null
                ? null : _umbrellaObject.GetComponent<ParticleEffect>();
            if (_umbrellaClone == null)
            {
                ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);
                _umbrellaCloneRefused = true;

                // ★ **Do not give up silently.** Say it in one line (this is on the per-frame
                //   path, so do not use Warn. The eruption carries on as it is and the umbrella
                //   is drawn with the column's clone).
                if (!_umbrellaRefusedLogged)
                {
                    _umbrellaRefusedLogged = true;
                    Log.Info("volcano effects: the ash umbrella could not be cloned in this "
                             + "environment; Disaster + draws the umbrella with the column's "
                             + "own clone instead (it looks denser) and the eruption carries on");
                }
                return null;
            }

            return _umbrellaClone;
        }

        /// <summary>
        /// The crater flames. **Not cloned** — we want exactly the same look as a vanilla building
        /// fire, and since not one byte is rewritten, sharing is safe (§D-5 in the class doc).
        ///
        /// ★★ **The first port of call is <c>BuildingProperties.m_fireEffect.m_particleEffect</c>.**
        ///   <c>BuildingManager.InitializeProperties</c> calls
        ///   <c>m_fireEffect.InitializeEffect()</c>, and <c>FireEffect.CreateEffect</c> calls
        ///   <c>m_particleEffect.InitializeEffect()</c> inside it (measured in IL), so
        ///   **the one that arrives by this path is guaranteed to be initialised**. A name lookup
        ///   ought to hit the same object, but nothing guarantees it.
        ///
        /// ★ Do not hand an uninitialised prefab to the drawing path. <c>EmitParticles</c>
        ///   dereferences <c>m_particleSystem</c> without a null check (measured in IL), so you
        ///   get **an NRE inside vanilla**.
        /// </summary>
        internal static ParticleEffect Flames()
        {
            ParticleEffect resolved = ResolveFlames();
            _flameOk = resolved != null;
            return resolved;
        }

        private static ParticleEffect ResolveFlames()
        {
            if (_flame != null) return _flame;

            if (_flameMiss > 0)
            {
                _flameMiss--;
                return null;
            }

            ParticleEffect found = BuildingFireParticles();
            if (found == null) found = Lookup(FlameName);

            if (found == null || !Ready(found))
            {
                _flameMiss = RetryFrames;
                if (!_flameMissLogged)
                {
                    _flameMissLogged = true;
                    Log.Info("volcano effects: the game's own fire particles could not be "
                             + "reached in this environment; the crater has no flames and the "
                             + "eruption carries on");
                }
                return null;
            }

            _flame = found;
            return _flame;
        }

        /// <summary>
        /// The particles of <c>BuildingProperties.m_fireEffect</c> (a <c>FireEffect</c>).
        /// **The only path where initialisation is guaranteed** (the doc of <see cref="Flames"/>).
        /// </summary>
        private static ParticleEffect BuildingFireParticles()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;

                var properties = Singleton<BuildingManager>.instance.m_properties;
                if (properties == null) return null;

                // m_fireEffect's declared type is EffectInfo, so cast down with as (measured in IL).
                var fire = properties.m_fireEffect as FireEffect;
                return fire == null ? null : fire.m_particleEffect;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The ejecta. Returns **a clone with the gravity turned downwards** (the original points up, for a fireball).</summary>
        internal static ParticleEffect Ejecta()
        {
            // ★ Note the resolution result here. The diagnostics (sim thread) cannot touch Unity
            //   objects, so they read this plain bool.
            ParticleEffect resolved = ResolveEjecta();
            _ejectaOk = resolved != null;
            _ejectaCloned = _ejectaClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveEjecta()
        {
            if (_ejectaClone != null) return _ejectaClone;

            ParticleEffect source = Source(EjectaName, ref _ejectaMiss, ref _ejectaMissLogged);
            if (source == null) return null;

            if (_ejectaCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);

            _ejectaObject = CloneEjecta(source);
            _ejectaClone = _ejectaObject == null
                ? null : _ejectaObject.GetComponent<ParticleEffect>();
            if (_ejectaClone == null)
            {
                ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);
                _ejectaCloneRefused = true;
                // ★ Do not hand an uninitialised prefab to the drawing path (the doc of Ready).
                return Ready(source) ? source : null;
            }

            return _ejectaClone;
        }

        /// <summary>The pyroclastic lookalike's dust. Returns **a clone that spreads almost horizontally**.</summary>
        internal static ParticleEffect PyroclasticDust()
        {
            // ★ Note the resolution result here. The diagnostics (sim thread) cannot touch Unity
            //   objects, so they read this plain bool.
            ParticleEffect resolved = ResolveDust();
            _dustOk = resolved != null;
            _dustCloned = _dustClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveDust()
        {
            if (_dustClone != null) return _dustClone;

            ParticleEffect source = Source(DustName, ref _dustMiss, ref _dustMissLogged);
            if (source == null) return null;

            if (_dustCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _dustObject, ref _dustClone);

            _dustObject = CloneDust(source);
            _dustClone = _dustObject == null ? null : _dustObject.GetComponent<ParticleEffect>();
            if (_dustClone == null)
            {
                ReleaseAndDestroy(ref _dustObject, ref _dustClone);
                _dustCloneRefused = true;
                // ★ Do not hand an uninitialised prefab to the drawing path (the doc of Ready).
                return Ready(source) ? source : null;
            }

            return _dustClone;
        }

        /// <summary>
        /// The camera info that can be drawn with in this environment right now.
        /// **Never pass null to <c>RenderEffect</c>.**
        /// </summary>
        internal static RenderManager.CameraInfo CameraInfo()
        {
            try
            {
                return Singleton<RenderManager>.exists
                    ? Singleton<RenderManager>.instance.CurrentCameraInfo : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The time delta vanilla is using for this frame.
        /// <b>It is not <c>Time.deltaTime</c>.</b> <c>EffectManager.EndRenderingImpl</c> passes
        /// <c>SimulationManager.m_simulationTimeDelta</c> (measured in IL), and using that one
        /// **follows pausing and speed changes directly**.
        /// Returns 0 if it cannot be read — **0 means "emit not one particle this frame"; it is
        /// neither an exception nor a fabrication.**
        /// </summary>
        internal static float EffectTimeDelta()
        {
            try
            {
                if (!Singleton<SimulationManager>.exists) return 0f;
                float dt = Singleton<SimulationManager>.instance.m_simulationTimeDelta;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) return 0f;
                return dt;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// **Main thread only.** Probe what can be looked up.
        /// Both <c>Assumptions</c> and the drawing side use this, and
        /// **what the drawing side gates on is this struct's <c>bool</c> itself**.
        /// </summary>
        internal static VolcanoVanillaFacts ScanFacts()
        {
            LogInventoryOnce();

            return new VolcanoVanillaFacts(
                AshPlume() != null,
                Flames() != null,
                Ejecta() != null,
                PyroclasticDust() != null,
                CameraInfo() != null);
        }

        /// <summary>
        /// The one line reported in the diagnostics (**English**). It is the only clue if a future
        /// game update makes everything silently stop appearing, so always state
        /// **what was looked up and what could be cloned**.
        ///
        /// ★★ **Do not run a resolution from here.** The diagnostics are assembled from the sim
        ///   thread (<c>FeatureHost.BuildReport</c>), so not only <c>Object.Instantiate</c> but
        ///   even a Unity reference comparison must not be stepped on. All that is read is the
        ///   plain values the main thread wrote.
        /// </summary>
        internal static string Detail
        {
            get
            {
                return "builtin effects=" + (_effectCount > 0 ? _effectCount.ToString() : "?")
                       + ", particle materials="
                       + (_particleMaterialCount > 0 ? _particleMaterialCount.ToString() : "?")
                       + "; ash=" + State(_ashOk, _ashCloned, _ashCloneRefused)
                       + ", umbrella=" + (_umbrellaCloned ? "cloned"
                            : (_umbrellaCloneRefused ? "shares the column clone" : "not yet"))
                       + ", flames=" + (_flameOk ? "shared" : "MISSING")
                       + ", ejecta=" + State(_ejectaOk, _ejectaCloned, _ejectaCloneRefused)
                       + ", dust=" + State(_dustOk, _dustCloned, _dustCloneRefused)
                       + ", " + BlastDetail;
            }
        }

        /// <summary>
        /// Whether the dust was looked up most recently. **A plain read for diagnostics only**; it
        /// never runs a resolution (the same reason as <see cref="Detail"/>).
        /// </summary>
        internal static bool DustResolvedCached { get { return _dustOk; } }

        /// <summary>
        /// **Count the inventory exactly once in the live game** (the inventory in the facts doc
        /// is still PARTIAL).
        /// <c>GetEffectList()</c> **allocates an array** via <c>Keys.ToArray()</c>, so it is only
        /// called here. <see cref="_inventoryLogged"/> is not set until the count succeeds
        /// (partway through loading, the dictionary may still be empty).
        /// </summary>
        internal static void LogInventoryOnce()
        {
            if (_inventoryLogged) return;

            try
            {
                if (!Singleton<EffectManager>.exists) return;
                var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                if (wrapper == null || wrapper.m_BuiltinEffects == null) return;

                int count = wrapper.m_BuiltinEffects.Count;
                if (count <= 0) return;

                _effectCount = count;
                _particleMaterialCount = wrapper.m_BuiltinParticleMaterials != null
                    ? wrapper.m_BuiltinParticleMaterials.Count : 0;
                _inventoryLogged = true;

                Log.Info("volcano effects: " + count + " builtin effect(s), "
                         + _particleMaterialCount + " particle material(s); "
                         // ★ If this one line says MISSING, ⑤ uses not a single borrowed effect.
                         //   That is because there would be no way of confirming whether one was
                         //   looked up, and handing one to RenderEffect unconfirmed gives an NRE
                         //   inside vanilla.
                         + "ParticleEffect.m_particleSystem="
                         + (ParticleSystemField != null ? "ok" : "MISSING") + "; "
                         + AshName + "=" + Probe(wrapper, AshName)
                         + ", " + FlameName + "=" + Probe(wrapper, FlameName)
                         + ", " + EjectaName + "=" + Probe(wrapper, EjectaName)
                         + ", " + DustName + "=" + Probe(wrapper, DustName));
            }
            catch (Exception e)
            {
                // The eruption still appears even if the count fails. **Say it once and then keep
                // quiet.**
                _inventoryLogged = true;
                Log.Info("volcano effects: the builtin effect inventory could not be read ("
                         + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// **Main thread. Call on level unload.** Idempotent.
        ///
        /// ★ Call <c>ReleaseEffect()</c> first. The inner clones that
        ///   <c>ParticleEffect.CreateEffect</c> builds hang under the
        ///   <b>"Particle Effects" root (<c>DontDestroyOnLoad</c>)</b> and do not go down with our
        ///   <c>GameObject</c>.
        ///   Skip it and **one more set of particle systems is left behind every time the player
        ///   enters and leaves a city.**
        /// </summary>
        internal static void Destroy()
        {
            ReleaseAndDestroy(ref _ashObject, ref _ashClone);
            ReleaseAndDestroy(ref _umbrellaObject, ref _umbrellaClone);
            ReleaseAndDestroy(ref _ejectaObject, ref _ejectaClone);
            ReleaseAndDestroy(ref _dustObject, ref _dustClone);

            // ★ Clean up after the blast and the ejecta (VolcanoVanillaFx.Blast.cs).
            DestroyBlast();

            // ★ The flames are **only borrowed**, so neither Release nor Destroy is called on
            //   them (release something we did not initialise and you stop the game's building
            //   fires).
            //   Just drop the reference.
            _flame = null;

            _ashCloneRefused = false;
            _umbrellaCloneRefused = false;
            _ejectaCloneRefused = false;
            _dustCloneRefused = false;
            _ashMiss = 0;
            _ashAltMiss = 0;
            _umbrellaMiss = 0;
            _umbrellaAltMiss = 0;
            _flameMiss = 0;
            _ejectaMiss = 0;
            _dustMiss = 0;
            _inventoryLogged = false;
            _effectCount = 0;
            _particleMaterialCount = 0;
            _ashOk = false;
            _flameOk = false;
            _ejectaOk = false;
            _dustOk = false;
            _ashCloned = false;
            _umbrellaCloned = false;
            _ejectaCloned = false;
            _dustCloned = false;
            // ★ _ashMissLogged and friends are not reset (they are facts about the build of the
            //   game, not per-city state. The same call as the other types in ④ and ⑤).
        }

        private static void ReleaseAndDestroy(ref GameObject go, ref ParticleEffect clone)
        {
            try
            {
                if (clone != null) clone.ReleaseEffect();
            }
            catch
            {
                // Carry on even if it cannot be released. The GameObject is destroyed below
                // regardless.
            }

            if (go != null) UnityEngine.Object.Destroy(go);
            go = null;
            clone = null;
        }

        /// <summary>
        /// Look a vanilla prefab up by name. **null if it cannot be looked up** (no exception is
        /// thrown).
        /// While it is not found, at most once every <see cref="RetryFrames"/> frames.
        /// </summary>
        private static ParticleEffect Source(string name, ref int missCount, ref bool logged)
        {
            if (missCount > 0)
            {
                missCount--;
                return null;
            }

            ParticleEffect found = Lookup(name);

            if (found == null)
            {
                missCount = RetryFrames;
                if (!logged)
                {
                    logged = true;
                    // ★ Log.Warn is not throttled. This is on the per-frame path, so Info once
                    //   only. **The eruption carries on even with a piece missing.**
                    Log.Info("volcano effects: the game's particle effect \"" + name
                             + "\" could not be looked up in this environment; Disaster + "
                             + "leaves that piece of the eruption out and carries on");
                }
            }

            return found;
        }

        /// <summary>
        /// Just looks a vanilla prefab up by name. **A pure probe with no throttling and no log.**
        /// </summary>
        private static ParticleEffect Lookup(string name)
        {
            try
            {
                if (Singleton<EffectManager>.exists)
                {
                    var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                    if (wrapper != null)
                    {
                        // ★ The return type is System.Object (measured in IL). Take it with as, so
                        //   a type mismatch comes out as null too.
                        var byName = wrapper.GetBuiltinEffect(name) as ParticleEffect;
                        if (byName != null) return byName;
                    }
                }

                // ★ The fallback. Factory Smoke is **not registered** in EffectCollection so it
                //   cannot be looked up this way, but Fire Particles and the like can.
                return EffectCollection.FindEffect(name) as ParticleEffect;
            }
            catch
            {
                return null;
            }
        }


        private static string Probe(EffectsWrapper wrapper, string name)
        {
            try
            {
                return wrapper.GetBuiltinEffect(name) != null ? "ok" : "MISSING";
            }
            catch
            {
                return "MISSING";
            }
        }

        private static string State(bool resolved, bool cloned, bool refused)
        {
            if (!resolved) return "MISSING";
            if (cloned) return "cloned";
            return refused ? "shared (clone refused)" : "shared";
        }
    }
}
