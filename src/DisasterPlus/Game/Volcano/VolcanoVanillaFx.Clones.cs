using System;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="VolcanoVanillaFx"/> covering <b>"what gets changed on a borrowed
    /// prefab"</b>. **Main thread only.**
    ///
    /// Looking them up, their lifetime and the cleanup are in the main file. What is here is only
    /// <b>the table of numbers for one clone</b> and the check that the clone actually ended up
    /// usable.
    /// The split is not only about the 800-line limit — when you come to adjust the look, this is
    /// the only file you need to read.
    ///
    /// ── ★ only the clone may be changed here (§D-5) ───────────────────────────────────────
    ///
    /// <c>ParticleSystem.main.startColor</c> / <c>startSize</c> and
    /// <c>ParticleEffect.m_minLifeTime</c> are **shared state on that prefab**.
    /// Rewrite the original prefab and <b>every building fire and factory plume in the city goes
    /// with it</b>, and it lingers in memory (though not in the save). All three of these write
    /// **to clones**.
    /// The flames (<c>Fire Particles</c>) do not appear here —
    /// they are not cloned, and since not one byte of them is rewritten, sharing is safe.
    ///
    /// ── ★★ do not set <c>emission.rateOverTime</c> to 0 ──────────────────────────────────
    ///
    /// <c>ParticleEffect.CreateEffect</c> sets the inner clone's <c>emission.enabled</c> to
    /// <c>false</c>, but <c>EmitParticles</c> <b>goes on reading
    /// <c>emission.rateOverTime.constant</c> as a multiplier on the particle count</b>
    /// (measured in IL). Set it to 0 and not one particle is emitted. It is the easiest trap to
    /// fall into.
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>
        /// The clone for the plume <b>column</b>. Make it **tall, long-lived and visible from a
        /// distance**. <c>Factory Smoke</c>'s stock visibility distance is only 1000 m, and a
        /// volcanic plume should be visible from far away.
        ///
        /// ★★ <b>The initial speed has been dropped from 26-48 m/s to 3-11 m/s</b>
        ///   (2026-08-22, report ③).
        ///   The column's shape is now made by "where we well them up" (the 9 segments of
        ///   <c>Core/Volcano/EruptionColumn</c>), so if the particles themselves keep rising
        ///   **the umbrella never gets a ceiling** — stopping at the neutral buoyancy height and
        ///   spreading sideways is what an eruption column looks like.
        ///   The lifetime was also shortened from 7-16 s to 4-9 s. Particles welled up in a
        ///   segment die there, and by being re-welled every frame they make
        ///   <b>a column with a continuing supply</b>.
        ///
        /// The colour is a dark grey-brown. **Being denser and darker nearer the vent** is carried
        /// by the per-segment density (the lower segments have more weight), and the colour is
        /// fixed once here — <c>startColor</c> is shared state on the <c>ParticleSystem</c> side
        /// and cannot be changed per <c>RenderEffect</c> call (measured in IL, §B-4).
        /// </summary>
        private static GameObject CloneAsh(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoAsh");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            // ★ The table of numbers is in Core (EruptionAshProfile). tools/VolcanoPreview draws
            //   the plume column with the same numbers, so **do not hard-code them here**.
            Apply(effect, particles, EruptionAshProfile.Column, 10000f);

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.26f, 0.21f, 0.17f, 1f), new Color(0.08f, 0.07f, 0.065f, 1f));

            return Initialize(go, effect);
        }

        /// <summary>
        /// The clone for the plume column's <b>umbrella</b>. Pale, large, long-lived ash that
        /// spreads sideways at the neutral buoyancy height.
        ///
        /// It is kept separate from the column because <c>startColor</c> / <c>startSize</c> and
        /// the lifetime are **shared state** on the <c>ParticleSystem</c> side and cannot be
        /// changed per <c>RenderEffect</c> call (measured in IL, §B-4). The umbrella is
        ///
        /// <list type="bullet">
        /// <item><b>paler</b> than the column (thinly spread ash is bright against the sky)</item>
        /// <item>more than three times <b>larger</b> in particle size (filling a 500 m-class
        ///   surface with 30 particles takes size)</item>
        /// <item><b>long-lived</b> (18-34 s). It lingers and piles up into a flat surface</item>
        /// <item>emission angle 55-95°. It <b>spreads almost horizontally</b> —
        ///   <c>direction</c> is up, so this angle becomes the sideways initial velocity
        ///   directly</item>
        /// </list>
        ///
        /// ⑤ does not stop if it cannot be looked up (the column's clone stands in; see the doc of
        /// <c>AshUmbrella</c>).
        /// </summary>
        private static GameObject CloneAshUmbrella(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoUmbrella");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            Apply(effect, particles, EruptionAshProfile.Umbrella, 12000f);

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.44f, 0.42f, 0.41f, 1f), new Color(0.19f, 0.18f, 0.175f, 1f));

            return Initialize(go, effect);
        }

        /// <summary>
        /// The clone for the ejecta. Stock <c>Medium Explosion Particles</c> is a **fireball**
        /// with <c>gravityModifier = -1</c> (i.e. upwards) and a particle size of 60, so the
        /// gravity is put back downwards and the particles shrunk to give "debris falling in an
        /// arc".
        /// </summary>
        private static GameObject CloneEjecta(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoEjecta");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            effect.m_minLifeTime = 2.4f;
            effect.m_maxLifeTime = 4.2f;
            effect.m_minStartSpeed = 55f;
            effect.m_maxStartSpeed = 120f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 42f;
            effect.m_maxVisibilityDistance = 10000f;
            // ★ Stock is 1.0 seconds. We make the window ourselves in continuous mode, so drop it
            //   to 0.
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.86f, 0.42f, 1f), new Color(0.62f, 0.16f, 0.05f, 1f));
            main.startSize = 6f;
            main.gravityModifier = 1.6f;     // ★ It falls. Stock is -1 (upwards)
            main.maxParticles = 3000;

            var emission = particles.emission;
            emission.rateOverTime = 150f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// The clone for the pyroclastic lookalike. Make it **ash that spreads almost horizontally
        /// and settles slowly**.
        /// Stock <c>Collapse Particles</c> is already close, with an emission angle of 50–80°
        /// (nearly sideways), but its lifetime is short and its visibility distance is only
        /// 1000 m.
        /// </summary>
        private static GameObject CloneDust(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoPyroclast");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            effect.m_minLifeTime = 4.5f;
            effect.m_maxLifeTime = 9f;
            // ★ On a bezier band the speed **only goes into the upward component** (measured in
            //   IL). Pushing sideways is what RenderEffect's velocity argument is for, so keep
            //   this small.
            effect.m_minStartSpeed = 2f;
            effect.m_maxStartSpeed = 7f;
            effect.m_minSpawnAngle = 70f;
            effect.m_maxSpawnAngle = 95f;
            effect.m_maxVisibilityDistance = 10000f;
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.30f, 0.27f, 0.25f, 1f), new Color(0.13f, 0.12f, 0.11f, 1f));
            main.startSize = 28f;
            main.gravityModifier = 0.12f;    // settles slowly
            main.maxParticles = 6000;

            var emission = particles.emission;
            emission.rateOverTime = 26f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// Copy Core's table of numbers (<see cref="EruptionAshProfile"/>) onto the clone.
        ///
        /// ★ Always set <c>m_renderDuration</c> to 0. We run in continuous mode
        ///   (<c>timeOffset &lt; 0</c>), so anything other than 0 applies
        ///   <c>m_intensityCurve</c>'s phase gate.
        /// ★★ Do not set <c>rateOverTime</c> to 0. Even with <c>emission.enabled</c> false,
        ///   <c>EmitParticles</c> **goes on reading <c>rateOverTime.constant</c> as a multiplier
        ///   on the particle count** (measured in IL). Set it to 0 and not one particle is
        ///   emitted.
        /// </summary>
        private static void Apply(ParticleEffect effect, ParticleSystem particles,
                                  EruptionAshProfile profile, float visibilityMetres)
        {
            effect.m_minLifeTime = profile.LifeMinSeconds;
            effect.m_maxLifeTime = profile.LifeMaxSeconds;
            effect.m_minStartSpeed = profile.SpeedMin;
            effect.m_maxStartSpeed = profile.SpeedMax;
            effect.m_minSpawnAngle = profile.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = profile.SpawnAngleMaxDegrees;
            effect.m_maxVisibilityDistance = visibilityMetres;
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startSize = profile.SizeMetres;
            main.gravityModifier = profile.GravityModifier;
            main.maxParticles = profile.MaxParticles;

            var emission = particles.emission;
            emission.rateOverTime = profile.RateOverTime;
        }

        /// <summary>
        /// Clone one prefab. All the internal fields are <c>[NonSerialized]</c>, so the clone
        /// starts **uninitialised** and there is no accident of it sharing a
        /// <c>ParticleSystem</c> with the original prefab (measured in IL).
        /// </summary>
        private static GameObject CloneObject(ParticleEffect source, string name)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(source.gameObject);
                if (go == null) return null;

                go.name = name;
                // Do not let the game destroy it on level unload (we destroy it ourselves; see
                // Destroy()).
                UnityEngine.Object.DontDestroyOnLoad(go);

                // ★ The clone itself is "the template" and emits not one particle of its own
                //   (the particles well up in the inner clone that InitializeEffect creates).
                //   Leave it alone and the template puffs smoke at the map origin.
                var particles = go.GetComponent<ParticleSystem>();
                if (particles != null)
                {
                    var emission = particles.emission;
                    emission.enabled = false;
                    particles.Stop();
                    particles.Clear();
                }

                return go;
            }
            catch (Exception e)
            {
                Log.Info("volcano effects: \"" + name + "\" could not be cloned ("
                         + e.GetType().Name + "); Disaster + uses the game's own effect "
                         + "unchanged instead");
                return null;
            }
        }

        /// <summary>
        /// Call <c>InitializeEffect()</c> and confirm the particle system was actually built.
        /// **Hand a clone that was not built to the drawing path and you get an NRE inside
        /// <c>EmitParticles</c>** (that one dereferences <c>m_particleSystem</c> without a null
        /// check. Measured in IL).
        /// </summary>
        private static GameObject Initialize(GameObject go, ParticleEffect effect)
        {
            try
            {
                effect.InitializeEffect();
            }
            catch (Exception e)
            {
                Log.Info("volcano effects: \"" + go.name + "\" could not be initialised ("
                         + e.GetType().Name + ")");
                return Reject(go);
            }

            return Ready(effect) ? go : Reject(go);
        }

        /// <summary>
        /// Whether that <c>ParticleEffect</c>'s <c>m_particleSystem</c> has actually been built.
        ///
        /// ★★ Apply it to clones **and to shared prefabs**. <c>EffectCollection</c> only puts the
        ///   name into a dictionary and never calls <c>InitializeEffect()</c> (measured in IL), so
        ///   "it was looked up" and "it has a particle system" are different things. Hand an
        ///   uninitialised one to <c>RenderEffect</c> and you get an NRE inside
        ///   <c>EmitParticles</c>.
        /// </summary>
        private static bool Ready(ParticleEffect effect)
        {
            if (ParticleSystemField == null) return false;

            try
            {
                // ★ Cast down to ParticleSystem before comparing. Test != null while it is still
                //   an object and a Unity destroyed (fake-null) object reads as "present".
                var particles = ParticleSystemField.GetValue(effect) as ParticleSystem;
                return particles != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Throw away a half-built one. **Do not leave a half-finished clone behind.**</summary>
        private static GameObject Reject(GameObject go)
        {
            try
            {
                var effect = go.GetComponent<ParticleEffect>();
                if (effect != null) effect.ReleaseEffect();
            }
            catch
            {
                // Destroy the GameObject even if it cannot be released.
            }

            UnityEngine.Object.Destroy(go);
            return null;
        }
    }
}
