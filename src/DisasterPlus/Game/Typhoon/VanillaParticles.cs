using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The common parts of ④ <b>borrowing vanilla particle effects</b>.
    /// **Main thread only.**
    ///
    /// ── ★ Pick by material, not by name ──────────────────────────────────
    ///
    /// The inventory in the effects measurement document is marked <b>PARTIAL</b> (an
    /// asset existing and the runtime API returning it are two different things). Writing
    /// a list of names and trying them from the top is a bet on those names existing in
    /// this build.
    ///
    /// Here we do not bet. We <b>actually enumerate</b>
    /// <c>EffectsWrapper.m_BuiltinEffects</c> (i.e. the result of
    /// <c>Resources.FindObjectsOfTypeAll&lt;EffectInfo&gt;()</c>, "everything loaded into
    /// this build") and <c>EffectCollection.Effects</c> (the 186 registered ones), read
    /// the <c>ParticleSystemRenderer.sharedMaterial.name</c> each <c>ParticleEffect</c> is
    /// really drawing with, and <b>score them by the material preference the caller passed
    /// in</b>.
    ///
    /// Average RGB measured from the textures extracted from the shipped assets:
    ///
    /// <code>
    /// Material       Texture         Average RGB      Appearance
    /// Steam          steam           (168, 184, 189)  Pale blue-white cotton. **Cloud**
    /// Water          water           (201, 222, 254)  White-blue spray. **Rain / spray**
    /// Placement Dust placement-dust  (198, 165, 131)  Sand-coloured dust cloud
    /// IndustryDust   IndustryDust    ( 99,  94,  79)  Brown-grey dust
    /// Smoke          smoke           ( 75,  78,  80)  Dark sooty mass (with specks of ember)
    /// </code>
    ///
    /// <c>startColor</c> is applied multiplicatively, so <b>if the source is soot, it looks
    /// like smoke whatever colour you multiply by.</b> That was the direct cause of the
    /// owner's "the cloud effect has turned into smoke".
    ///
    /// The list of names (<c>namePreference</c>) is <b>only ever used to break ties</b>.
    /// If the game is updated and the names change, the same picture comes out as long as
    /// the material is the same.
    ///
    /// ★ Read <c>sharedMaterial</c>. <c>material</c> **clones the renderer's material and
    ///   swaps it in**, which both damages the game's shared state and leaks.
    ///
    /// ── ★ Clone before touching (§D-5) ───────────────────────────────────
    ///
    /// A <c>ParticleSystem</c>'s colour, particle size, lifetime and visibility distance
    /// are **the effect's shared state**. Rewrite the source in place and every bit of
    /// steam and spray in the city is dragged along with it — and it stays in memory, not
    /// in the save. <see cref="Clone"/> does an <c>Object.Instantiate</c> before returning.
    ///
    /// ★★ The cloned <c>GameObject</c> **goes into the active scene**, so left as it is
    ///   its own <c>ParticleSystem</c> keeps emitting at the world origin.
    ///   <see cref="Clone"/> sets <c>emission.enabled = false</c> before returning
    ///   (<c>InitializeEffect()</c> **clones that state one level further down**, so the
    ///   same setting is passed to the inner one too. Measured from the IL).
    ///   <c>playOnAwake</c> is **left alone** — the inner clone is built so that
    ///   <c>ParticleEffect.Update</c> looks at <c>isPaused</c> and calls <c>Play()</c>
    ///   again, so handing out a stopped state leaves the particles motionless.
    ///
    /// ⑤ the volcano has a borrowing of the same shape (<c>VolcanoVanillaFx</c>). **They
    /// have not been merged** — that one deals with an inventory that assumes lookup by
    /// name (things like <c>Fire Particles</c> where it has to be that one specific
    /// effect), whereas this one picks "whichever looks most like cloud". Put two
    /// different ways of choosing into one type and neither intention can be read any
    /// more.
    /// </summary>
    internal static class VanillaParticles
    {
        /// <summary>
        /// **Glowing things with additive blending
        /// (<c>Custom/Particles/Additive (Soft)</c>).** They can be used for neither cloud
        /// nor rain (the sky catches fire), so we drop them before scoring.
        /// </summary>
        private static readonly string[] Rejected =
        {
            "Fire", "FireRocket", "Explosion", "RocketLaunch", "SmokeRocket",
            "FireworksIngame", "FireworksTrail",
        };

        /// <summary>
        /// <c>ParticleEffect.m_particleSystem</c> (a <c>[NonSerialized]</c> private).
        /// **Pass an uninitialised clone to <c>RenderEffect</c> and you get an NRE inside
        /// <c>EmitParticles</c>**, so we check with this.
        /// </summary>
        private static readonly System.Reflection.FieldInfo ParticleSystemField =
            typeof(ParticleEffect).GetField("m_particleSystem",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        private static readonly EffectInfo[] EmptyEffects = new EffectInfo[0];

        /// <summary>
        /// Enumerate the inventory and return the one particle effect closest to
        /// <paramref name="materialPreference"/>. **It does not throw** (null if nothing
        /// could be got).
        ///
        /// **It does allocate** (enumerating the dictionary, and the <c>IEnumerable</c>
        /// from <c>EffectCollection.Effects</c>), but it is called once per city.
        /// </summary>
        /// <param name="materialPreference">Material names, best first. The first is the
        /// best.</param>
        /// <param name="namePreference">Names used to break ties. null if there are
        /// none.</param>
        internal static ParticleEffect Pick(string[] materialPreference, string[] namePreference,
                                            out string name, out string material)
        {
            name = null;
            material = null;

            ParticleEffect best = null;
            int bestScore = 0;
            int bestRank = int.MaxValue;

            Dictionary<string, EffectInfo> builtin = BuiltinEffects();
            if (builtin != null)
            {
                foreach (KeyValuePair<string, EffectInfo> pair in builtin)
                {
                    Consider(pair.Key, pair.Value, materialPreference, namePreference,
                             ref best, ref bestScore, ref bestRank, ref name, ref material);
                }
            }

            foreach (EffectInfo info in RegisteredEffects())
            {
                if (info == null) continue;
                Consider(info.name, info, materialPreference, namePreference,
                         ref best, ref bestScore, ref bestRank, ref name, ref material);
            }

            if (best == null)
            {
                // Last resort: the building-collapse dust the game itself holds.
                Consider("BuildingProperties.m_collapseEffect", BuildingCollapseEffect(),
                         materialPreference, namePreference,
                         ref best, ref bestScore, ref bestRank, ref name, ref material);
            }

            return best;
        }

        /// <summary>Score one candidate and decide whether it replaces the current
        /// best.</summary>
        private static void Consider(string candidateName, EffectInfo info,
                                     string[] materialPreference, string[] namePreference,
                                     ref ParticleEffect best, ref int bestScore, ref int bestRank,
                                     ref string name, ref string material)
        {
            ParticleEffect particle = Extract(info);
            if (particle == null) return;

            string mat = MaterialNameOf(particle);
            int score = ScoreOf(mat, materialPreference);
            if (score <= 0) return;

            int rank = RankOf(candidateName, namePreference);

            // Take the higher score. On a tie, the name preference; still tied, ordinal
            // order — **so that every build picks the same thing** (which makes in-game
            // reports readable).
            if (score < bestScore) return;
            if (score == bestScore)
            {
                if (rank > bestRank) return;
                if (rank == bestRank && best != null
                    && string.CompareOrdinal(candidateName, name) >= 0) return;
            }

            best = particle;
            bestScore = score;
            bestRank = rank;
            name = candidateName;
            material = mat;
        }

        /// <summary>
        /// Scoring a material name. **Higher is better.** 0 means "do not use".
        /// A material not on the list scores 1 — **it always loses to one that is on the
        /// list**, but it is better than nothing (insurance for an environment where a
        /// game update has changed all the names).
        /// </summary>
        private static int ScoreOf(string material, string[] preference)
        {
            if (string.IsNullOrEmpty(material)) return 0;

            for (int i = 0; i < Rejected.Length; i++)
            {
                if (Rejected[i] == material) return 0;
            }

            if (preference != null)
            {
                for (int i = 0; i < preference.Length; i++)
                {
                    if (preference[i] == material) return preference.Length - i + 1;
                }
            }

            return 1;
        }

        private static int RankOf(string candidateName, string[] namePreference)
        {
            if (candidateName == null || namePreference == null) return int.MaxValue - 1;

            for (int i = 0; i < namePreference.Length; i++)
            {
                if (namePreference[i] == candidateName) return i;
            }
            return int.MaxValue - 1;
        }

        /// <summary>
        /// The name of the material that particle effect is really drawing with.
        /// **Read <c>sharedMaterial</c>** (class doc).
        /// </summary>
        internal static string MaterialNameOf(ParticleEffect effect)
        {
            try
            {
                var go = effect.gameObject;
                if (go == null) return null;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) return null;

                var mat = renderer.sharedMaterial;
                if (mat == null) return null;

                // Unity appends " (Instance)" to a cloned material. Look at the plain
                // name.
                string n = mat.name;
                if (n == null) return null;

                int at = n.IndexOf(" (Instance)");
                return at > 0 ? n.Substring(0, at) : n;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, EffectInfo> BuiltinEffects()
        {
            try
            {
                if (!Singleton<EffectManager>.exists) return null;
                var wrapper = Singleton<EffectManager>.instance.m_EffectsWrapper;
                return wrapper != null ? wrapper.m_BuiltinEffects : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<EffectInfo> RegisteredEffects()
        {
            try
            {
                IEnumerable<EffectInfo> effects = EffectCollection.Effects;
                if (effects != null) return effects;
            }
            catch
            {
                // Even where this cannot be enumerated, m_BuiltinEffects above is enough.
            }
            return EmptyEffects;
        }

        private static EffectInfo BuildingCollapseEffect()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;
                var properties = Singleton<BuildingManager>.instance.m_properties;
                return properties != null ? properties.m_collapseEffect : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get the particles out of an <c>EffectInfo</c>. <c>MultiEffect</c> (i.e. a bundle
        /// of particles and sound, like <c>Collapse Effect</c>) and <c>FireEffect</c> hold
        /// a <c>ParticleEffect</c> inside them (§B-7).
        /// **Do not grab the sound one** — <c>SoundEffect.RenderEffect</c> is not
        /// overridden and calling it does nothing.
        /// </summary>
        private static ParticleEffect Extract(EffectInfo info)
        {
            if (info == null) return null;

            var direct = info as ParticleEffect;
            if (direct != null) return direct;

            var fire = info as FireEffect;
            if (fire != null) return fire.m_particleEffect;

            var multi = info as MultiEffect;
            if (multi != null && multi.m_effects != null)
            {
                for (int i = 0; i < multi.m_effects.Length; i++)
                {
                    var child = multi.m_effects[i].m_effect as ParticleEffect;
                    if (child != null) return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Clone one prefab. Every internal field is <c>[NonSerialized]</c>, so the clone
        /// starts in an **uninitialised state** and the accident of sharing a
        /// <c>ParticleSystem</c> with the original prefab cannot happen (measured from the
        /// IL, §D-1).
        /// **<c>InitializeEffect()</c> has not been called yet** (the caller writes its
        /// numbers and then calls <see cref="Initialize"/>).
        /// </summary>
        internal static GameObject Clone(ParticleEffect source, string name)
        {
            try
            {
                var sourceObject = source.gameObject;
                if (sourceObject == null) return null;

                var go = Object.Instantiate(sourceObject) as GameObject;
                if (go == null) return null;

                go.name = name;
                Object.DontDestroyOnLoad(go);

                // ★★ Stop the outer emission **before InitializeEffect** (class doc).
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                    ps.Stop();
                    ps.Clear();
                }

                return go;
            }
            catch (System.Exception e)
            {
                Log.Info("typhoon effects: \"" + name + "\" could not be cloned ("
                         + e.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// Call <c>InitializeEffect()</c> and check the particle system really came out.
        /// If it did not, throw the clone away and return null.
        /// </summary>
        internal static GameObject Initialize(GameObject go, ParticleEffect effect)
        {
            try
            {
                effect.InitializeEffect();
            }
            catch (System.Exception e)
            {
                Log.Info("typhoon effects: \"" + go.name + "\" could not be initialised ("
                         + e.GetType().Name + ")");
                return Reject(go);
            }

            return Ready(effect) ? go : Reject(go);
        }

        private static bool Ready(ParticleEffect effect)
        {
            if (ParticleSystemField == null) return false;

            try
            {
                // ★ Cast down to ParticleSystem before comparing. Testing != null while it
                //   is still an object reads Unity's destroyed (fake-null) objects as
                //   "present".
                var particles = ParticleSystemField.GetValue(effect) as ParticleSystem;
                return particles != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Throw away a half-built one. **Leave no half-finished clone
        /// behind.**</summary>
        internal static GameObject Reject(GameObject go)
        {
            try
            {
                var effect = go.GetComponent<ParticleEffect>();
                if (effect != null) effect.ReleaseEffect();
            }
            catch
            {
                // Even if it cannot be released, destroy the GameObject.
            }

            Object.Destroy(go);
            return null;
        }

        /// <summary>
        /// Let go of one clone. **Idempotent.** It also packs away the inner clone (the one
        /// <c>ParticleEffect.CreateEffect</c> made).
        /// </summary>
        internal static void Release(ref GameObject go, ref ParticleEffect effect,
                                     ref ParticleSystem particles)
        {
            if (effect != null)
            {
                try
                {
                    effect.ReleaseEffect();
                }
                catch
                {
                    // Even if it cannot be packed away, always destroy the outer one.
                }
            }
            effect = null;
            particles = null;

            if (go != null) Object.Destroy(go);
            go = null;
        }

        /// <summary>
        /// The camera info we can draw with in this environment right now. **Do not pass
        /// null to <c>RenderEffect</c>** (it gives an NRE on the first line of
        /// <c>ParticleEffect.RenderEffect</c>. §H-18).
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
        /// <b>It is not <c>Time.deltaTime</c>.</b>
        /// <c>EffectManager.EndRenderingImpl</c> passes
        /// <c>SimulationManager.m_simulationTimeDelta</c> (measured from the IL, §B-3),
        /// and using this one **follows pausing and speed changes automatically**.
        /// If it cannot be read it returns 0 — **0 means "emit no particles this frame",
        /// which is neither an exception nor a fabrication.**
        /// </summary>
        internal static float TimeDelta()
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
    }
}
