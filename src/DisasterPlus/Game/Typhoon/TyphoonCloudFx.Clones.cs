using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="TyphoonCloudFx"/> covering <b>what we borrow and what we
    /// change on it</b>. **Main thread only.**
    ///
    /// How the puffs are scattered lives in the main body. All that is here is <b>what we
    /// want from the source material</b> and <b>writing the numbers for one clone</b>.
    /// The split is not only for the 800-line limit — when you come to tune the look,
    /// this file and <c>Core/Typhoon/VortexCloudProfile</c> are the only two you need to
    /// read.
    ///
    /// Enumeration, scoring, cloning, initialisation and cleanup all belong to
    /// <see cref="VanillaParticles"/> (shared with <c>TyphoonSquallFx</c>). The reason we
    /// **pick by material rather than by name**, and the average RGB measured from the
    /// shipped assets, are in that doc.
    /// </summary>
    public static partial class TyphoonCloudFx
    {
        /// <summary>The <c>emission.rateOverTime</c> we pin on the clone.
        /// **It must not be 0** (at 0 not a single particle comes out; the trap in §D-2).
        /// Each borrowed source carries a different value (9.67 to 200), so we level it
        /// here to fix the input to
        /// <see cref="VortexPuffLayout.MagnitudeFor"/>.</summary>
        private const float RateOverTime = 20f;

        /// <summary>
        /// The particle materials we want, best first. **<c>Steam</c> is the one we are
        /// after** — the shipped <c>steam</c> texture is pale blue-white cotton with an
        /// average RGB of (168, 184, 189), which is cloud itself. <c>Smoke</c> is a
        /// picture of soot at (75, 78, 80), so it goes **last** (the old implementation
        /// had it as first choice).
        /// </summary>
        private static readonly string[] CloudMaterials =
        {
            "Steam", "Water", "Snow", "Placement Dust", "IndustryDust", "Smoke",
        };

        /// <summary>
        /// The name order, used only to break ties in the ordering. **Not the order we
        /// draw in.** <c>Large Pool Steam</c> is at the front because its raw initial
        /// speed of 0.1-0.2 m/s is the closest thing to a "big cloud that does not move"
        /// (§A-6).
        /// </summary>
        private static readonly string[] CloudNames =
        {
            "Large Pool Steam",
            "Pool Steam",
            "Factory Steam",
            "Fire Copter Water Particles",
            "Collapse Particles",
        };

        // ★ Hold them one reference at a time (not in an array). UnityEngine.Object's ==
        //   makes a destroyed object compare equal to null, but **that does not apply to
        //   comparing the array reference** — a static array stays non-null while holding
        //   destroyed contents, and goes silently invisible in the second city (③ fire
        //   whirl §4.8).
        private static GameObject _deckObject;
        private static ParticleEffect _deckEffect;
        private static ParticleSystem _deckParticles;

        private static GameObject _towerObject;
        private static ParticleEffect _towerEffect;
        private static ParticleSystem _towerParticles;

        private static GameObject _canopyObject;
        private static ParticleEffect _canopyEffect;
        private static ParticleSystem _canopyParticles;

        private static string _sourceName;
        private static string _sourceMaterial;

        /// <summary>Whether we have named the unavailability once. **Not reset by
        /// <see cref="Destroy"/>** (it is a fact about the game build, not per-city
        /// state).</summary>
        private static bool _unavailableLogged;

        internal static string SourceName { get { return _sourceName; } }

        internal static string SourceMaterial { get { return _sourceMaterial; } }

        /// <summary>The clone for a layer. **Look at the reference itself** (fake-null
        /// self-repair).</summary>
        private static ParticleEffect CloneFor(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return _deckEffect != null ? _deckEffect : null;
            if (layer == VortexCloudLayer.Canopy)
            {
                // ★ If the anvil could not be made, stand in with the tower's clone (the
                //   canopy just takes the tower's colour; it does not disappear). The
                //   same decision as ⑤'s umbrella.
                if (_canopyEffect != null) return _canopyEffect;
                return _towerEffect != null ? _towerEffect : null;
            }
            return _towerEffect != null ? _towerEffect : null;
        }

        private static bool AnyClone()
        {
            return _deckEffect != null || _towerEffect != null || _canopyEffect != null;
        }

        private static int CloneCount()
        {
            int n = 0;
            if (_deckEffect != null) n++;
            if (_towerEffect != null) n++;
            if (_canopyEffect != null) n++;
            return n;
        }

        private static int TotalParticleCap()
        {
            int cap = 0;
            if (_deckEffect != null) cap += VortexCloudProfile.Deck.MaxParticles;
            if (_towerEffect != null) cap += VortexCloudProfile.Tower.MaxParticles;
            if (_canopyEffect != null) cap += VortexCloudProfile.Canopy.MaxParticles;
            return cap;
        }

        /// <summary>
        /// Only checks whether it can be borrowed (the formula shared with
        /// <c>Assumptions</c>). No side effects.
        /// </summary>
        private static ParticleEffect Lookup(out string name, out string material)
        {
            return VanillaParticles.Pick(CloudMaterials, CloudNames, out name, out material);
        }

        /// <summary>
        /// Borrow, clone three times and initialise. false if not one could be made
        /// (**it does not throw**).
        /// </summary>
        private static bool Acquire()
        {
            // ★ Do not go looking every frame. Enumeration costs a full pass over the
            //   dictionary.
            if (_lookupMissCount > 0)
            {
                _lookupMissCount--;
                return false;
            }

            string name;
            string material;
            ParticleEffect source = Lookup(out name, out material);
            if (source == null)
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;

                if (!_unavailableLogged)
                {
                    _unavailableLogged = true;
                    Log.Warn("typhoon cloud: this build exposes no borrowable particle effect "
                             + "for the vortex (the mod enumerated the game's builtin effects "
                             + "and its effect collection and found none with a usable "
                             + "particle material); falling back to the mod's own spiral mesh. "
                             + "Everything else about the typhoon is unaffected.");
                }
                return false;
            }

            _sourceName = name;
            _sourceMaterial = material;

            _deckObject = BuildClone(source, VortexCloudLayer.Deck, "DisasterPlus_TyphoonDeck");
            _deckEffect = ComponentOf(_deckObject, out _deckParticles);

            _towerObject = BuildClone(source, VortexCloudLayer.Tower, "DisasterPlus_TyphoonTower");
            _towerEffect = ComponentOf(_towerObject, out _towerParticles);

            _canopyObject = BuildClone(source, VortexCloudLayer.Canopy,
                                       "DisasterPlus_TyphoonCanopy");
            _canopyEffect = ComponentOf(_canopyObject, out _canopyParticles);

            if (!AnyClone())
            {
                _lookupMissCount = LookupRetryFrames;
                _state = TyphoonCloudFxState.NoEffect;
                return false;
            }

            Log.Info("typhoon cloud: borrowed \"" + name + "\" (particle material \"" + material
                     + "\") for the vortex and cloned it into " + CloneCount()
                     + " cumulonimbus layer(s): " + VortexPuffLayout.PuffCount
                     + " puffs/frame, " + (int)ParticlesPerSecond + " particles/s, cap "
                     + TotalParticleCap());
            return true;
        }

        private static ParticleEffect ComponentOf(GameObject go, out ParticleSystem particles)
        {
            particles = null;
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            if (effect == null) return null;

            particles = go.GetComponent<ParticleSystem>();
            return effect;
        }

        /// <summary>
        /// Clone one layer and dress it up as a cumulonimbus. **It does not touch a single
        /// byte of shared state** (§D-5). The table of numbers lives in
        /// <see cref="VortexCloudProfile"/> (Core) — <c>tools/TyphoonPreview</c> draws the
        /// vortex from the same figures, so **do not write them inline here**.
        /// </summary>
        private static GameObject BuildClone(ParticleEffect source, VortexCloudLayer layer,
                                             string name)
        {
            GameObject go = VanillaParticles.Clone(source, name);
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var ps = go.GetComponent<ParticleSystem>();
            if (effect == null || ps == null) return VanillaParticles.Reject(go);

            VortexCloudProfile profile = VortexCloudProfile.Of(layer);

            effect.m_maxVisibilityDistance = VisibilityMetres;
            effect.m_minLifeTime = profile.LifeMinSeconds;
            effect.m_maxLifeTime = profile.LifeMaxSeconds;
            effect.m_minStartSpeed = profile.SpeedMin;
            effect.m_maxStartSpeed = profile.SpeedMax;
            effect.m_minSpawnAngle = profile.SpawnAngleMinDegrees;
            effect.m_maxSpawnAngle = profile.SpawnAngleMaxDegrees;
            effect.m_renderDuration = 0f;      // used in continuous mode (§B-3)
            effect.m_extraRadius = 0f;         // some sources add 2-9 m of their own accord

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(profile.BrightRed, profile.BrightGreen, profile.BrightBlue,
                          profile.Alpha),
                new Color(profile.DarkRed, profile.DarkGreen, profile.DarkBlue, profile.Alpha));
            main.startSize = MinSizeMetres;    // ApplySizes puts the real value in each frame
            main.gravityModifier = profile.GravityModifier;
            main.maxParticles = profile.MaxParticles;

            var emission = ps.emission;
            emission.rateOverTime = profile.RateOverTime;

            return VanillaParticles.Initialize(go, effect);
        }

        /// <summary>
        /// Match the particle size to the size of the vortex. **These are our clones, so
        /// writing every frame is fine** (<c>MainModule</c> is a struct; zero bytes
        /// allocated).
        /// </summary>
        private static void ApplySizes(float radius)
        {
            ApplySize(_deckParticles, radius, VortexCloudProfile.Deck.SizeFraction);
            ApplySize(_towerParticles, radius, VortexCloudProfile.Tower.SizeFraction);
            ApplySize(_canopyParticles, radius, VortexCloudProfile.Canopy.SizeFraction);
        }

        private static void ApplySize(ParticleSystem particles, float radius, float fraction)
        {
            // ★ Look at the reference itself. If it has been destroyed, fake-null makes it
            //   compare equal to null.
            if (particles == null) return;

            float size = radius * fraction;
            if (float.IsNaN(size)) return;
            if (size < MinSizeMetres) size = MinSizeMetres;
            if (size > MaxSizeMetres) size = MaxSizeMetres;

            var main = particles.main;
            main.startSize = size;
        }

        private static void DestroyClones()
        {
            VanillaParticles.Release(ref _deckObject, ref _deckEffect, ref _deckParticles);
            VanillaParticles.Release(ref _towerObject, ref _towerEffect, ref _towerParticles);
            VanillaParticles.Release(ref _canopyObject, ref _canopyEffect, ref _canopyParticles);
            _sourceName = null;
            _sourceMaterial = null;
        }
    }
}
