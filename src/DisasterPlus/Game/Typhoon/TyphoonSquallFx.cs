using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>What the driving rain is doing right now.</summary>
    public enum TyphoonSquallState
    {
        /// <summary>Switched off in the settings (or we are not in a city yet).</summary>
        Off,

        /// <summary>This environment has no borrowable particle effect. **Not a fault**
        /// (the rain still falls).</summary>
        NoEffect,

        /// <summary>Not drawing, because there is no typhoon.</summary>
        Idle,

        /// <summary>The camera is outside the gale radius. **Not a fault** — it is not
        /// blowing there.</summary>
        OutsideStorm,

        /// <summary>Emitting spray every frame.</summary>
        Emitting,

        /// <summary>It threw and fell over.</summary>
        Failed,
    }

    /// <summary>
    /// Shows <b>the storm</b> at ground level. <b>Main thread only.</b>
    ///
    /// ── What the owner pointed out (2026-08-22) ───────────────────────────
    ///
    /// &gt; I would like the storm itself reproduced.
    ///
    /// All ④ had been able to show as "the strength of the storm" so far was the two
    /// values <c>m_targetRain</c> / <c>m_targetCloud</c>. Both are **settings for the
    /// whole sky**, and vanilla's rain falls straight down, obeying neither the wind
    /// direction nor the typhoon's position. To a player on the ground, a typhoon at its
    /// peak looks exactly like ordinary rain.
    ///
    /// ★★ <b>The rainfall cannot be raised any further.</b> At the peak
    ///   <c>m_targetRain</c> is already 1.0, and on top of that
    ///   <c>m_currentRain &gt; 0.8</c> is the boundary at which the game creates a
    ///   thunderstorm disaster of its own (IL facts document §A-3). ④'s lightning budget
    ///   is balanced just short of that (design doc §4.2). **This feature does not touch
    ///   a single byte of the rainfall.**
    ///
    /// What it adds instead is <b>spray driven sideways</b>. Unlike vanilla's rain it
    /// drifts downwind via the <c>velocity</c> argument, and for the first time it looks
    /// as though the wind is blowing.
    ///
    /// ── ★ Place it around the camera (not at the typhoon's centre) ───────
    ///
    /// The vortex (<see cref="TyphoonCloudFx"/>) is placed at the typhoon's centre. This
    /// is different — it is <b>there to show "it is raging where I am"</b>, so it goes
    /// below <c>RenderManager.CurrentCameraInfo.m_position</c>. Spray whirling 5 km away
    /// does nothing at all on screen.
    ///
    /// The strength is decided by <c>SquallLayout.StrengthOf</c> from
    /// <c>TyphoonProfile.WindAt</c> (the distance between the camera and the typhoon's
    /// centre). Outside the gale radius **not one particle is emitted**
    /// (<see cref="TyphoonSquallState.OutsideStorm"/>; not a fault).
    ///
    /// ── The source material ───────────────────────────────────────
    ///
    /// <see cref="VanillaParticles"/> enumerates what is available and takes, in
    /// preference to anything else, the particle material <c>Water</c> (the shipped
    /// <c>water</c> texture is white-blue spray with an average RGB of (201, 222, 254)).
    /// Why we **do not look it up by name** is in that doc.
    /// The source has a spawn angle of only 1 degree and a visibility distance of
    /// 500-2000 m, so we **clone it** and rebuild it as almost horizontal (66-104 degrees)
    /// with a visibility of 3000 m.
    ///
    /// ── The work done per frame ───────────────────────────────────
    ///
    /// <c>RenderEffect</c> exactly <c>SquallLayout.PatchCount</c> (9) times, newly spawned
    /// particles at <c>SquallLayout.ParticlesPerSecond</c> (900) per second, and live
    /// particles capped at <c>SquallLayout.MaxParticles</c> (2400).
    /// **Zero bytes of heap allocation.**
    ///
    /// ── How it stops ─────────────────────────────────────────
    ///
    /// This type is **never once called from the sim thread** (the same as
    /// <see cref="TyphoonCloud"/>). On the frame the typhoon ends,
    /// <see cref="TyphoonFeature.OnMainThreadUpdate"/> hands it a snapshot with
    /// <c>Active == false</c> and it stops drawing itself. Particles that already spawned
    /// die of old age (1.9 seconds at most).
    /// Do not add this type to <c>TyphoonController.Forget</c>'s cleanup list.
    /// </summary>
    public static class TyphoonSquallFx
    {
        private const int LookupRetryFrames = 600;

        private static GameObject _object;
        private static ParticleEffect _effect;
        private static ParticleSystem _particles;

        private static string _sourceName;
        private static string _sourceMaterial;

        private static int _lookupMissCount;
        private static TyphoonSquallState _state = TyphoonSquallState.Off;
        private static int _lastRenderCalls;
        private static float _lastStrength;

        private static bool _unavailableLogged;
        private static bool _errorLogged;

        public static TyphoonSquallState State { get { return _state; } }

        /// <summary>How many <c>RenderEffect</c> calls were made on the most recent
        /// frame.</summary>
        public static int LastRenderCalls { get { return _lastRenderCalls; } }

        /// <summary>The most recently measured spray strength [0, 1] (where the camera
        /// is).</summary>
        public static float LastStrength { get { return _lastStrength; } }

        /// <summary>The one line for the diagnostics (**in English**).</summary>
        public static string Detail
        {
            get
            {
                if (_effect == null)
                {
                    return "NONE (no vanilla water particle effect could be borrowed)";
                }
                return "cloned \"" + (_sourceName ?? "?") + "\" [material \""
                       + (_sourceMaterial ?? "?") + "\"], " + SquallLayout.PatchCount
                       + " patches/frame around the camera, "
                       + (int)SquallLayout.ParticlesPerSecond + " particles/s, cap "
                       + SquallLayout.MaxParticles;
            }
        }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                _state = TyphoonSquallState.Failed;
                _lastRenderCalls = 0;
                _lastStrength = 0f;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon driving rain failed", e);
                }

                // Do not keep hold of a broken clone and go on throwing every frame.
                DestroyClone();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                if (_effect != null) _state = TyphoonSquallState.Idle;
                _lastRenderCalls = 0;
                _lastStrength = 0f;
                return;
            }

            var camera = VanillaParticles.CameraInfo();
            if (camera == null)
            {
                // ★ Passing null to RenderEffect gives an NRE on its first line. **Draw
                //   nothing and wait.**
                _lastRenderCalls = 0;
                return;
            }

            Vector3 eye = camera.m_position;
            Vec3 centre = snapshot.Centre;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.Prefab.StormRadius);
            float strength = SquallLayout.StrengthOf(wind);
            _lastStrength = strength;

            if (!(strength > 0f))
            {
                // The camera is outside the gale radius. **Not a fault.**
                _lastRenderCalls = 0;
                if (_effect != null) _state = TyphoonSquallState.OutsideStorm;
                return;
            }

            // ★ Look at the reference itself every frame. If it has been destroyed,
            //   fake-null makes it compare equal to null and it is rebuilt here (the
            //   second city's self-repair).
            if (_effect == null && !Acquire()) return;

            float timeDelta = VanillaParticles.TimeDelta();
            if (!(timeDelta > 0f))
            {
                // Paused, or speed 0. No new particles spawn, but the ones already spawned
                // drift on.
                _lastRenderCalls = 0;
                _state = TyphoonSquallState.Emitting;
                return;
            }

            Emit(snapshot, eye, dx, dz, strength, timeDelta, camera);
        }

        private static void Emit(TyphoonSnapshot snapshot, Vector3 eye, float dx, float dz,
                                 float strength, float timeDelta,
                                 RenderManager.CameraInfo camera)
        {
            // The wind is the typhoon's secondary circulation (tangential plus inflow).
            // **The same sense as the vortex turns.**
            float wx, wz;
            SquallLayout.WindDirection(dx, dz, out wx, out wz);

            float drift = SquallLayout.DriftMetresPerSecond * strength;
            var velocity = new Vector3(wx * drift, 0f, wz * drift);

            InstanceID id = InstanceID.Empty;
            id.Disaster = snapshot.TyphoonId != 0 ? snapshot.TyphoonId : (ushort)1;

            // ★★ **Scatter from the ground.** At first we used the camera height as the
            //   reference, but then the spray became a sheet up in the air that never
            //   reached the ground (spotted in tools/TyphoonPreview's squall image).
            //   <c>SampleDetailHeight</c> is read-only and safe from either thread
            //   (TerrainHeightSampler's class doc). We sample it once per frame.
            float ground = TerrainHeightSampler.Instance.SampleHeight(eye.x, eye.z);
            if (float.IsNaN(ground))
            {
                // The terrain cannot be read. **Do not scatter in mid-air at a guessed
                // height.**
                _lastRenderCalls = 0;
                return;
            }

            // The higher the camera the wider we scatter (otherwise all you get is a small
            // smudge in the middle of the screen).
            float spread = SquallLayout.SpreadFor(eye.y - ground);

            int calls = 0;
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch patch = SquallLayout.PatchAt(i);

                var position = new Vector3(
                    eye.x + patch.OffsetXFraction * spread,
                    ground + patch.HeightFraction * SquallLayout.HeightMetres,
                    eye.z + patch.OffsetZFraction * spread);

                float disc = patch.DiscFraction * spread;
                if (!(disc > 0f)) continue;

                float band = patch.BandFraction * SquallLayout.HeightMetres;

                float magnitude = ParticleBudget.MagnitudeFor(
                    disc, SquallLayout.RateOverTime,
                    SquallLayout.ParticlesPerSecond * strength, SquallLayout.PatchCount);
                if (!(magnitude > 0f)) continue;

                var area = new EffectInfo.SpawnArea(position, Vector3.up, disc, band);

                // timeOffset = -1f means **continuous mode** (§B-3).
                _effect.RenderEffect(id, area, velocity, 0f,
                                     magnitude * patch.DensityFraction,
                                     -1f, timeDelta, camera);
                calls++;
            }

            _lastRenderCalls = calls;
            _state = calls > 0 ? TyphoonSquallState.Emitting : TyphoonSquallState.NoEffect;
        }

        /// <summary>Borrow, clone and initialise. false if we could not get one (**it does
        /// not throw**).</summary>
        private static bool Acquire()
        {
            if (_lookupMissCount > 0)
            {
                _lookupMissCount--;
                return false;
            }

            string name;
            string material;
            ParticleEffect source = VanillaParticles.Pick(SprayMaterials, SprayNames,
                                                          out name, out material);
            if (source == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;

                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    // ★ **Info, not Warn.** Even with no spray the rain still falls, and
                    //   not one of the typhoon's other elements stops.
                    Log.Info("typhoon driving rain: this build exposes no borrowable water "
                             + "particle effect, so the storm has no wind-driven spray. "
                             + "The rain, the wind damage and the vortex are unaffected.");
                }
                return false;
            }

            _object = Clone(source);
            if (_object == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;
                return false;
            }

            _effect = _object.GetComponent<ParticleEffect>();
            _particles = _object.GetComponent<ParticleSystem>();
            if (_effect == null)
            {
                DestroyClone();
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonSquallState.NoEffect;
                return false;
            }

            _sourceName = name;
            _sourceMaterial = material;

            Log.Info("typhoon driving rain: borrowed \"" + name + "\" (particle material \""
                     + material + "\") for the wind-driven spray: " + SquallLayout.PatchCount
                     + " patches/frame, " + (int)SquallLayout.ParticlesPerSecond
                     + " particles/s, cap " + SquallLayout.MaxParticles);
            return true;
        }

        /// <summary>
        /// The particle materials we want, best first. **<c>Water</c> is the one we are
        /// after** — the shipped <c>water</c> texture is white-blue spray with an average
        /// RGB of (201, 222, 254). <c>Steam</c> ends up looking like cloud, so it is
        /// second choice.
        /// </summary>
        private static readonly string[] SprayMaterials =
        {
            "Water", "Snow", "Steam", "Placement Dust",
        };

        /// <summary>The name order, used only to break ties in the ordering. **Not the
        /// order we draw in.**</summary>
        private static readonly string[] SprayNames =
        {
            "Fire Copter Water Particles",
            "Fireman Water",
            "Snowplow Particles",
        };

        /// <summary>
        /// Clone it and dress it up as sideways spray. **It does not touch a single byte
        /// of shared state** (§D-5) — rewrite the source in place and every fire engine in
        /// the city hoses storm-coloured water.
        /// The table of numbers lives in <see cref="SquallLayout"/> (Core), so **do not
        /// write them inline here**.
        /// </summary>
        private static GameObject Clone(ParticleEffect source)
        {
            GameObject go = VanillaParticles.Clone(source, "DisasterPlus_TyphoonSquall");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var ps = go.GetComponent<ParticleSystem>();
            if (effect == null || ps == null) return VanillaParticles.Reject(go);

            effect.m_maxVisibilityDistance = SquallLayout.VisibilityMetres;
            effect.m_minLifeTime = SquallLayout.LifeMinSeconds;
            effect.m_maxLifeTime = SquallLayout.LifeMaxSeconds;
            effect.m_minStartSpeed = SquallLayout.SpeedMin;
            effect.m_maxStartSpeed = SquallLayout.SpeedMax;
            effect.m_minSpawnAngle = SquallLayout.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = SquallLayout.SpawnAngleMaxDegrees;
            effect.m_renderDuration = 0f;      // used in continuous mode (§B-3)
            effect.m_extraRadius = 0f;

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(SquallLayout.BrightRed, SquallLayout.BrightGreen,
                          SquallLayout.BrightBlue, SquallLayout.Alpha),
                new Color(SquallLayout.DarkRed, SquallLayout.DarkGreen,
                          SquallLayout.DarkBlue, SquallLayout.Alpha));
            main.startSize = SquallLayout.SizeMetres;
            main.gravityModifier = SquallLayout.GravityModifier;
            main.maxParticles = SquallLayout.MaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = SquallLayout.RateOverTime;

            return VanillaParticles.Initialize(go, effect);
        }

        /// <summary>
        /// **Call on level unload and when it is switched off in the settings.** Main
        /// thread only. Idempotent.
        /// </summary>
        public static void Destroy()
        {
            DestroyClone();
            _lookupMissCount = 0;
            _lastRenderCalls = 0;
            _lastStrength = 0f;
            _state = TyphoonSquallState.Off;
            // ★ _unavailableLogged / _errorLogged are not reset (they are facts about the
            //   game build).
        }

        private static void DestroyClone()
        {
            VanillaParticles.Release(ref _object, ref _effect, ref _particles);
            _sourceName = null;
            _sourceMaterial = null;
        }
    }
}
