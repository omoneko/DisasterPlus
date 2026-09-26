using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Builds the typhoon's vortex out of **our own white cloud puffs**.
    /// <b>Main thread only, every frame.</b>
    ///
    /// ── What was asked for (2026-08-22) ───────────────────────────
    ///
    /// &gt; About the typhoon cloud effect — I can still see something like smoke.
    /// &gt; Could you display the white cloud that MissileDisaster's mushroom cloud
    /// &gt; effect uses, in a spiral, up high?
    ///
    /// ── ★★ Stop "scattering" the puffs and start "placing" them ──────────
    ///
    /// The old implementation (<see cref="TyphoonCloudFx"/>) **scattered** vanilla
    /// particles through <c>ParticleEffect.RenderEffect</c>. A scattered particle belongs
    /// to vanilla's particle simulation: it is given a velocity at birth and then drifts —
    /// **that cannot hold a shape** (a vortex only looks like a vortex if it is re-placed
    /// every frame).
    ///
    /// Here we use <c>ParticleSystem</c> <b>purely as a renderer</b>:
    ///
    ///   - <c>emission.enabled = false</c> (the game creates not one particle)
    ///   - **we place** the particles ourselves every frame with <c>SetParticles</c>
    ///   - lifetimes are reset to the maximum every frame (we never let the simulation
    ///     age them)
    ///
    /// **This is the same trick as the missile mod's <c>MushroomCloudPuffsFx</c>.**
    /// That class's doc states the reason for this choice word for word —
    /// "A simulated particle gets a velocity at birth and drifts".
    ///
    /// ── The positions already exist ───────────────────────────────
    ///
    /// The shape of the vortex is already held by <see cref="VortexPuffLayout"/> (Core,
    /// with tests) — the three arms, the eyewall, the four height tiers, and each puff's
    /// size and density. The old implementation used it for "where to scatter".
    /// **We use the very same table for "where to place".** We are not reopening the
    /// argument about the shape.
    ///
    /// ── What changed (as a picture) ───────────────────────────────
    ///
    /// | | Old (scatter borrowed particles) | New (place our own) |
    /// |---|---|---|
    /// | Material | <c>Large Pool Steam</c> (opacity 0.34-0.41) | White cloud with an opaque core (0.997) |
    /// | Shape | Drifts and falls apart the moment it is scattered | Re-placed every frame, so it never falls apart |
    /// | Rotation | The puffs do not turn (only the scatter positions turn) | Each puff turns too |
    ///
    /// ── Do nothing in an environment where it cannot be drawn ─────────────
    ///
    /// If the material cannot be made, <see cref="Drawing"/> stays false and the caller
    /// **falls back to borrowed particles** (<see cref="TyphoonCloudFx"/>).
    /// We do not go quietly empty.
    /// </summary>
    public static class TyphoonVortexPuffFx
    {
        /// <summary>
        /// The puff colour (sunlit side). **Not pure white** — pure white looks as if it
        /// were glowing.
        ///
        /// ★★ Lowered from (242,244,248) on 2026-09-02 (the owner: "darker, greyer").
        ///   Typhoon cloud, cumulonimbus or not, is <b>lead-grey even on the sunlit
        ///   side</b>. Taking the white of a fair-weather thunderhead as the reference was
        ///   the mistake in the first place.
        /// </summary>
        private static readonly Color32 SunlitColor = new Color32(168, 174, 186, 255);

        /// <summary>
        /// The colour of the underside. Cloud is dark seen from below. Without a colour
        /// difference between top and bottom, **it looks like a flat disc pasted on the
        /// sky**.
        ///
        /// ★★ Lowered from (150,156,170) in the same way. The difference between top and
        ///   bottom (84 levels of brightness) is preserved — narrow the gap and the sense
        ///   of volume disappears and it is a disc again.
        /// </summary>
        private static readonly Color32 ShadedColor = new Color32(84, 90, 104, 255);

        /// <summary>The opacity of the densest puff. The texture has an opaque core, so
        /// close to 1 is fine.</summary>
        private const float MaxAlpha = 0.95f;

        /// <summary>The opacity of the thinnest puff (the tip of an outer arm).</summary>
        private const float MinAlpha = 0.42f;

        /// <summary>
        /// The multiplier on one puff's apparent size (applied to
        /// <c>CrowdPuff.SizeFraction</c>).
        ///
        /// ★ **Greater than 1.** Unless the puffs overlap each other it looks like a
        ///   scattering of dots rather than cloud (the 0.41 in
        ///   <see cref="VortexPuffCrowd"/>'s class doc is that state). The amount of
        ///   overlap was measured with "puff area ÷ vortex area" in
        ///   <c>tools/TyphoonPreview</c>.
        /// </summary>
        private const float PuffSizeGain = 1.0f;

        /// <summary>
        /// The ceiling on a puff's size relative to the screen
        /// (<c>ParticleSystemRenderer.maxParticleSize</c>). Left at the default of 0.5,
        /// **the cloud shrinks the moment you move in close**.
        /// </summary>
        private const float MaxScreenFraction = 4f;

        private static GameObject _object;
        private static ParticleSystem _system;
        private static ParticleSystem.Particle[] _buffer;
        private static bool _errorLogged;

        /// <summary>
        /// Seconds since this cloud was born. **This is what advances a parcel's life.**
        ///
        /// ★★ <b>This is where "it appears for an instant and vanishes" was fixed.</b>
        ///   (2026-08-22, reported by the owner.) Previously we placed
        ///   <c>VortexPuffCrowd</c>'s <b>static arrangement, determined by index alone</b>,
        ///   so once the cloud was placed it never changed again. Now the clock advances
        ///   every frame and parcels are born, drift and die
        ///   (<c>Core.Typhoon.TyphoonCloudParcels</c>).
        /// </summary>
        private static float _clockSeconds;

        /// <summary>
        /// This typhoon's seed. The point is that it <b>does not change during a single
        /// typhoon</b> (the doc on <see cref="Step"/>).
        /// </summary>
        private static uint _seed;

        /// <summary>How many puffs were placed on the most recent frame (diagnostics).
        /// 0 means "not drawing".</summary>
        public static int PuffsPlaced { get; private set; }

        /// <summary>Whether we drew on the most recent frame. If false the caller falls
        /// back.</summary>
        public static bool Drawing { get; private set; }

        /// <summary>
        /// **Main thread, every frame.**
        /// <paramref name="radiusMetres"/> is the vortex radius and
        /// <paramref name="spinDegrees"/> is how far the vortex has turned so far (pass
        /// the same value as <see cref="TyphoonCloudFx"/>).
        ///
        /// If it returns false **nothing has been drawn**, so the caller must fall back.
        /// </summary>
        public static bool Update(TyphoonSnapshot snapshot, float radiusMetres,
                                  float spinDegrees, float altitudeMetres,
                                  float thicknessMetres)
        {
            Drawing = false;
            PuffsPlaced = 0;

            try
            {
                return Step(snapshot, radiusMetres, spinDegrees, altitudeMetres, thicknessMetres);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon vortex cloud failed", e);
                }
                else
                {
                    Log.Diag("typhoonCloud", "vortex cloud failed: " + e.GetType().Name);
                }

                // ★ If it throws, pack up. **Do not leave a broken cloud on screen**
                //   (the caller falls back to the borrowed particles).
                Destroy();
                return false;
            }
        }

        private static bool Step(TyphoonSnapshot snapshot, float radiusMetres, float spinDegrees,
                                 float altitudeMetres, float thicknessMetres)
        {
            if (snapshot == null || !(radiusMetres > 0f)) return false;

            Material material = CloudParticleAssets.Cloud;
            if (material == null) return false;

            if (!EnsureSystem(material)) return false;

            Vec3 centre = snapshot.Centre;
            float spin = spinDegrees * 0.0174532925f;

            // ★ The clock advances here. spinDegrees stops while the game is paused, so
            //   to treat this the same way we use Time.deltaTime rather than the caller's
            //   step — although the caller has already made the pause decision, Update
            //   itself is still called on frames where things are stopped. **A slowly
            //   moving cloud is better than a stopped one** (the ash plume is treated the
            //   same way).
            _clockSeconds += Time.deltaTime;

            // ★ Fix the seed once per typhoon. A different slot number means a different
            //   typhoon.
            uint slotSeed = DeterministicRandom.Hash(snapshot.TyphoonId, 0x54595048u);
            if (slotSeed != _seed)
            {
                _seed = slotSeed;
                _clockSeconds = 0f;
            }

            // ★★ **The seed must not move.** (2026-08-22, in-game report "a typhoon cloud
            //    spinning at high speed appears for an instant".)
            //
            //    This used to hash the centre coordinates. **A typhoon moves**, so the
            //    seed changed every frame and <b>all 900 parcels jumped somewhere else
            //    every frame</b>. That is why the vortex looked as if it were spinning at
            //    high speed (⑤'s ash plume does not move, so the same code never showed
            //    the problem).
            //
            //    Seed it from the number of the disaster slot we hold. That has exactly
            //    the property we need: **unchanging through one typhoon, different for the
            //    next one**.
            uint seed = _seed;

            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                TyphoonParcel p = TyphoonCloudParcels.At(i, _clockSeconds, radiusMetres,
                                                         spin, seed);

                _buffer[i].position = new Vector3(
                    centre.X + p.X,
                    altitudeMetres + p.Y,
                    centre.Z + p.Z);

                // startSize is the **diameter**, so double the radius.
                _buffer[i].startSize = p.RadiusMetres * PuffSizeGain * 2f;
                _buffer[i].rotation = p.RotationDegrees;

                float alpha = MinAlpha + (MaxAlpha - MinAlpha) * Clamp01(p.Alpha);
                _buffer[i].startColor = Blend(SunlitColor, ShadedColor,
                                              1f - Clamp01(p.Brightness), alpha);

                // ★ Reset to the maximum every frame. **Never let the simulation age
                //   them** (this is what "use it purely as a renderer" in the class doc
                //   actually means).
                _buffer[i].remainingLifetime = 1000f;
                _buffer[i].startLifetime = 1000f;
            }

            _system.SetParticles(_buffer, TyphoonCloudParcels.Count);

            // ★★ **Move the GameObject to where the particles are.** (2026-08-22, in-game
            //    report "it disappears the moment lightning strikes".)
            //
            //    The particles are placed in world coordinates (simulationSpace = World),
            //    but <b>the GameObject was left sitting at the origin (0,0,0) the whole
            //    time</b>. Unity frustum-culls a <c>ParticleSystemRenderer</c> using
            //    <b>bounds based on the transform</b>, so **the moment the origin leaves
            //    the screen the whole system disappears**.
            //
            //    When lightning strikes the camera moves towards it (the game focuses on
            //    the disaster). At that point the origin left the view and the entire
            //    cloud vanished — the condition "the moment lightning strikes" was itself
            //    the clue.
            //
            //    ★ This is not a path that throws, so it is not wrapped in a try.
            _object.transform.position = new Vector3(centre.X, altitudeMetres, centre.Z);

            PuffsPlaced = TyphoonCloudParcels.Count;
            Drawing = true;
            return true;
        }

        /// <summary>
        /// Set up the <c>ParticleSystem</c> we use as a renderer. **One per city.**
        /// If it already exists and the material is alive, do nothing.
        /// </summary>
        private static bool EnsureSystem(Material material)
        {
            if (_object != null && _system != null && _buffer != null)
            {
                // ★★ The material is shared with ⑤ (the volcano) through
                //    CloudParticleAssets. Check fake-null every frame in case that side
                //    has destroyed it.
                var live = _system.GetComponent<ParticleSystemRenderer>();
                if (live != null && live.sharedMaterial == null) live.material = material;
                return true;
            }

            Destroy();

            var go = new GameObject("DisasterPlus_TyphoonVortexCloud");
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            // ★ Positions go in as **metres in world space**. Make them local and the
            //   GameObject would have to be moved every time the typhoon moves.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = TyphoonCloudParcels.Count;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;

            // ★★ **Let the game create not one particle.** We do the placing.
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // ★ The puffs are large, soft and overlapping, so without depth sorting the
            //   edges go dirty.
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = MaxScreenFraction;
            renderer.material = material;

            ps.Play();

            _object = go;
            _system = ps;
            _buffer = new ParticleSystem.Particle[TyphoonCloudParcels.Count];
            return true;
        }

        /// <summary>
        /// **Call on level unload and when the typhoon is gone.** Idempotent.
        ///
        /// The <c>GameObject</c> is ours, so we destroy it. The <c>Material</c> and
        /// <c>Texture2D</c> belong to <see cref="CloudParticleAssets"/>, so we **leave
        /// them alone** (that side destroys them itself on level unload).
        /// </summary>
        public static void Destroy()
        {
            if (_object != null) UnityEngine.Object.Destroy(_object);

            _object = null;
            _system = null;
            _buffer = null;
            _clockSeconds = 0f;
            _seed = 0u;
            Drawing = false;
            PuffsPlaced = 0;
        }

        private static Color32 Blend(Color32 top, Color32 bottom, float towardsBottom,
                                     float alpha)
        {
            float t = Clamp01(towardsBottom);
            return new Color32(
                (byte)(top.r + (bottom.r - top.r) * t),
                (byte)(top.g + (bottom.g - top.g) * t),
                (byte)(top.b + (bottom.b - top.b) * t),
                (byte)(255f * Clamp01(alpha)));
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
