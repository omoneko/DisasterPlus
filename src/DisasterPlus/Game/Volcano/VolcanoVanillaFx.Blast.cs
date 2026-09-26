using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="VolcanoVanillaFx"/> covering <b>the blast (a one-shot) and the
    /// ejecta (the flying rocks)</b>.
    /// **Main thread only.**
    ///
    /// ── ★★ the blast alone is "not cloned" (the one way it differs from the other four) ────
    ///
    /// The blast is a one-shot queued with <c>EffectManager.DispatchEffect</c>. That
    /// **only queues it; vanilla does the actual drawing on a later frame** (IL fact §C).
    /// So if ⑤ were to <c>DispatchEffect</c> a clone and then <c>Destroy</c> that clone on level
    /// unload immediately afterwards, **vanilla would be touching a destroyed object.**
    /// So what is queued is only <b>the game's own prefab (<c>Medium Explosion Particles</c>
    /// itself)</b>. That one belongs to the game and ⑤ will never destroy it.
    ///
    /// ★ Not one byte of it is rewritten, so sharing is safe (the discipline of §D-5).
    ///   Its <c>m_renderDuration</c> is 1.0 seconds, so **queuing it once is enough: it decays
    ///   away along <c>m_intensityCurve</c>** — which is the right answer for a one-shot.
    ///
    /// ── the flying rocks are cloned (we want them to leave a trail) ────────────────────────
    ///
    /// The rocks' positions are decided by <c>Core/Volcano/EjectaBallistics</c>, and this code
    /// **wells a small ball of particles up at that one point every frame** (continuous mode).
    /// Stock <c>Medium Explosion Particles</c> scatters in all directions at 100–150 m/s, so it
    /// reads as **a burst rather than a ball**. Clone it, drop the initial speed, shorten the
    /// lifetime and apply weak gravity to make it <b>a trail</b>.
    /// </summary>
    internal static partial class VolcanoVanillaFx
    {
        /// <summary>The flying rocks' trail. Built from the same prefab as the blast. **Base game.**</summary>
        internal const string BlockName = EjectaName;

        private static int _blastMiss;
        private static bool _blastMissLogged;
        private static bool _blastOk;

        // ★ Not an array. One reference each.
        private static GameObject _blockObject;
        private static ParticleEffect _blockClone;
        private static int _blockMiss;
        private static bool _blockMissLogged;
        private static bool _blockCloneRefused;
        private static bool _blockOk;
        private static bool _blockCloned;

        /// <summary>
        /// The blast (the one-shot queued with <c>DispatchEffect</c>). **The game's own prefab
        /// itself**, not a clone (class doc). null if it cannot be looked up —
        /// **that one thing is simply not emitted, and the eruption carries on.**
        /// </summary>
        internal static ParticleEffect BlastOneShot()
        {
            ParticleEffect source = Source(EjectaName, ref _blastMiss, ref _blastMissLogged);

            // ★ Do not hand an uninitialised prefab to the drawing path (the doc of Ready).
            //   Even via DispatchEffect, what runs at the end is the same EmitParticles.
            ParticleEffect resolved = source != null && Ready(source) ? source : null;
            _blastOk = resolved != null;
            return resolved;
        }

        /// <summary>The trail of a rock in flight. **A clone** (the original prefab if it cannot be cloned, and null if that fails too).</summary>
        internal static ParticleEffect FlyingBlock()
        {
            ParticleEffect resolved = ResolveBlock();
            _blockOk = resolved != null;
            _blockCloned = _blockClone != null;
            return resolved;
        }

        private static ParticleEffect ResolveBlock()
        {
            if (_blockClone != null) return _blockClone;

            // ★ Keep the throttle counter separate from both the blast's and the ejecta's
            //   (share them and they are decremented several times in the same frame, effectively
            //   dividing RetryFrames down).
            ParticleEffect source = Source(BlockName, ref _blockMiss, ref _blockMissLogged);
            if (source == null) return null;

            if (_blockCloneRefused) return Ready(source) ? source : null;

            ReleaseAndDestroy(ref _blockObject, ref _blockClone);

            _blockObject = CloneFlyingBlock(source);
            _blockClone = _blockObject == null
                ? null : _blockObject.GetComponent<ParticleEffect>();
            if (_blockClone == null)
            {
                ReleaseAndDestroy(ref _blockObject, ref _blockClone);
                _blockCloneRefused = true;
                return Ready(source) ? source : null;
            }

            return _blockClone;
        }

        /// <summary>
        /// The clone for the flying rocks. Make it **a small hot ball that stays where it is** —
        /// ⑤ does the moving (re-welling it up every frame at the position
        /// <c>Core/Volcano/EjectaBallistics</c> produced), so the particles themselves do not need
        /// to fly.
        ///
        /// ★ Do not set <c>emission.rateOverTime</c> to 0 (the class doc of Clones).
        /// </summary>
        private static GameObject CloneFlyingBlock(ParticleEffect source)
        {
            GameObject go = CloneObject(source, "DisasterPlus_VolcanoFlyingBlock");
            if (go == null) return null;

            var effect = go.GetComponent<ParticleEffect>();
            var particles = go.GetComponent<ParticleSystem>();
            if (effect == null || particles == null) return Reject(go);

            // The trail. It disappears in under a second, so it lingers briefly where it was
            // welled up and reads as a line.
            effect.m_minLifeTime = 0.35f;
            effect.m_maxLifeTime = 0.95f;
            effect.m_minStartSpeed = 1.5f;
            effect.m_maxStartSpeed = 7f;
            effect.m_minSpawnAngle = 0f;
            effect.m_maxSpawnAngle = 180f;
            effect.m_maxVisibilityDistance = 10000f;
            // ★ Stock is 1.0 seconds. We make the window ourselves in continuous mode, so drop it
            //   to 0.
            effect.m_renderDuration = 0f;

            var main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.72f, 0.28f, 1f), new Color(0.30f, 0.13f, 0.09f, 1f));
            main.startSize = 9f;
            main.gravityModifier = 0.35f;
            main.maxParticles = 2000;

            var emission = particles.emission;
            emission.rateOverTime = 90f;

            return Initialize(go, effect);
        }

        /// <summary>
        /// Cleaning up after the blast and the ejecta. **Called from
        /// <see cref="VolcanoVanillaFx.Destroy"/>.**
        /// The blast side is only borrowed, so we do not even hold a reference (all that happens
        /// is resetting the counting flags).
        /// </summary>
        private static void DestroyBlast()
        {
            ReleaseAndDestroy(ref _blockObject, ref _blockClone);
            _blockCloneRefused = false;
            _blockMiss = 0;
            _blastMiss = 0;
            _blastOk = false;
            _blockOk = false;
            _blockCloned = false;
            // ★ The *MissLogged flags are not reset (they are facts about the build of the game).
        }

        /// <summary>Part of the one line reported in the diagnostics (**English**). Only plain values read from the sim thread.</summary>
        internal static string BlastDetail
        {
            get
            {
                return "blast=" + (_blastOk ? "shared" : "MISSING")
                       + ", flying blocks=" + State(_blockOk, _blockCloned, _blockCloneRefused);
            }
        }
    }
}
