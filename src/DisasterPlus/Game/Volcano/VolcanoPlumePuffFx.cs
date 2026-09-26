using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Draws the plume as <b>a swarm of cloud puffs</b>. **Main thread only** (Unity objects).
    ///
    /// ── the owner's request (2026-08-22) ───────────────────────────────────────────────────
    ///
    /// &gt; The plume animation still isn't realistic. I'd like a more natural, chaotic smoke
    /// &gt; animation rather than something geometric. As well as the smoke effect, please also
    /// &gt; use part of the nuclear mushroom cloud (MissileDisaster) effect to make it realistic.
    ///
    /// ── ★★ what was borrowed from MissileDisaster is "how to draw" ────────────────────────
    ///
    /// It uses the same technique as the mushroom cloud's <c>MushroomCloudPuffsFx</c> —
    /// <b>use a <c>ParticleSystem</c> with emission turned off purely as a renderer, and place the
    /// particles ourselves every frame with <c>SetParticles</c></b>.
    /// A road already travelled in ④'s <c>TyphoonVortexPuffFx</c>.
    ///
    /// This is needed because with the game's particle prefabs **you cannot change the size of an
    /// individual particle** (widen <c>SpawnArea</c>'s radius and the particles do not get bigger;
    /// the same-sized particles just scatter more thinly. The same story is in the class doc of
    /// <c>BlastCluster</c>).
    /// Place them ourselves and the puffs can be drawn hundreds of metres across.
    ///
    /// ★★ <b>The shape is not borrowed.</b> The positions come from <see cref="PlumeParcels"/> —
    ///   this is a volcanic plume column with a continuing supply, not a nuclear one-shot puff.
    ///
    /// ── ★★ this became the <b>only</b> thing drawing the plume (2026-08-22) ───────────────
    ///
    /// At first it was layered over the game particles' ash column (the 9 segments of
    /// <see cref="EruptionColumn"/>). The owner's instruction stopped that:
    ///
    /// &gt; The white plume effect is the better one, so please omit the existing grey smoke
    /// &gt; effect. Instead, add a little grey into the white plume.
    ///
    /// Layered, the fine ash particles showed through the gaps between the puffs and it looked
    /// like <b>two plumes overlapping</b>. The grey has been moved into this one's colour
    /// (<c>AshColor</c> and <c>AshMixSpread</c>).
    ///
    /// ★ <c>VolcanoEruptionFx.RenderColumn</c> and the ash clones **have not been deleted**.
    ///   The column's shape (<see cref="EruptionColumn"/>) is still needed for the plume height
    ///   and the lightning's path, and in <b>an environment where not one puff can be drawn</b>
    ///   (the material cannot be looked up) falling back to it is the only way out.
    ///   So that the two representations claim the same thickness,
    ///   <see cref="PlumeParcels.ColumnRadiusAt"/> uses <see cref="EruptionColumn"/>'s constants
    ///   as they are.
    ///
    /// ── fold it away if it falls over ──────────────────────────────────────────────────────
    ///
    /// If <see cref="Update"/> returns false, **nothing was drawn**.
    /// The caller can fall back to the game-particles-only picture (which is the old picture).
    /// </summary>
    public static class VolcanoPlumePuffFx
    {
        /// <summary>
        /// The colour near the vent. **Ash is dark.** Pure white looks like steam.
        ///
        /// ★ Brightened slightly on 2026-08-22. Since the game particles' grey plume was stopped
        ///   (the owner's instruction), <b>this one now has to carry the grey</b>.
        ///   Left pitch black, the base looks like a lump of soot.
        /// </summary>
        private static readonly Color32 AshColor = new Color32(96, 91, 88, 255);

        /// <summary>The umbrella's colour. Sunlit ash-white. **Not pure white** (it would look like it is glowing).</summary>
        private static readonly Color32 SunlitColor = new Color32(226, 226, 230, 255);

        /// <summary>
        /// The spread of the per-puff grey mixing.
        ///
        /// ── ★★ why it varies per puff (2026-08-22, the owner's instruction) ────────────────
        ///
        /// &gt; The white plume effect is the better one, so please omit the existing grey smoke
        /// &gt; effect. Instead, add a little grey into the white plume.
        ///
        /// Decide white → grey by height alone and **every puff at the same height comes out the
        /// same colour**, which reads as neat stripes (i.e. back to "geometric" again). Shift each
        /// puff towards grey by ± this spread and you get the colour of a real plume: darker ash
        /// puffs mixed in among white cloud.
        /// </summary>
        private const float AshMixSpread = 0.34f;

        /// <summary>
        /// The cap on a particle's size relative to the screen
        /// (<c>ParticleSystemRenderer.maxParticleSize</c>).
        /// Leave it at the default 0.5 and **the plume shrinks the moment you get close**.
        /// </summary>
        private const float MaxScreenFraction = 6f;

        /// <summary>
        /// The factor applied to a puff's apparent size. **Greater than 1** —
        /// unless the puffs overlap each other it reads as dots rather than smoke
        /// (if <c>tools/PlumePreview</c>'s cover drops below 30 %, suspect this).
        /// </summary>
        private const float PuffSizeGain = 1.15f;

        private static GameObject _object;
        private static ParticleSystem _system;
        private static ParticleSystem.Particle[] _buffer;
        private static bool _errorLogged;

        /// <summary>The number of puffs placed in the last frame (for diagnostics). 0 means "not drawing".</summary>
        public static int PuffsPlaced { get; private set; }

        /// <summary>Whether it drew in the last frame. If false, the caller falls back.</summary>
        public static bool Drawing { get; private set; }

        /// <summary>The most recent failure (for diagnostics). **Do not fail to draw silently.**</summary>
        public static string LastFailure { get; private set; }

        /// <summary>
        /// **Main thread, every frame.**
        /// </summary>
        /// <param name="vent">The vent's world coordinates (<c>VolcanoSnapshot.Vent</c>).</param>
        /// <param name="ventRadiusMetres">The crater's radius (m).</param>
        /// <param name="columnHeightMetres">The column's height (m).</param>
        /// <param name="intensityUnit">The eruption strength <c>[0,1]</c>. It affects the thinness and the count.</param>
        /// <param name="timeSeconds">Seconds since the eruption began (**a continuously increasing value**).</param>
        public static bool Update(Vec3 vent, float ventRadiusMetres, float columnHeightMetres,
                                  float intensityUnit, float timeSeconds,
                                  float windX, float windZ, uint seed)
        {
            Drawing = false;
            PuffsPlaced = 0;

            try
            {
                return Step(vent, ventRadiusMetres, columnHeightMetres, intensityUnit,
                            timeSeconds, windX, windZ, seed);
            }
            catch (Exception e)
            {
                LastFailure = "the plume puffs threw " + e.GetType().Name;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano plume puffs failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcPlume",
                             "plume puffs failed: " + e.GetType().Name);
                }

                // ★ Fold it away if it falls over. **Do not leave a broken plume on screen.**
                Destroy();
                return false;
            }
        }

        private static bool Step(Vec3 vent, float ventRadiusMetres, float columnHeightMetres,
                                 float intensityUnit, float timeSeconds,
                                 float windX, float windZ, uint seed)
        {
            if (!(ventRadiusMetres > 0f) || !(columnHeightMetres > 0f))
            {
                LastFailure = "the vent radius or the column height is not usable";
                return false;
            }

            Material material = CloudParticleAssets.Cloud;
            if (material == null)
            {
                LastFailure = "no cloud material (" + (CloudParticleAssets.Detail ?? "no detail")
                              + ")";
                return false;
            }

            if (!EnsureSystem(material)) return false;

            float unit = Clamp01(intensityUnit);

            // ★★ **For a weak eruption, cut the number of puffs itself.** Merely making them
            //    thinner reads as "a thin plume of the same size", not "a weak eruption".
            int count = (int)(PlumeParcels.Count * (0.35f + 0.65f * unit));
            if (count < 1) count = 1;
            if (count > PlumeParcels.Count) count = PlumeParcels.Count;

            for (int i = 0; i < count; i++)
            {
                PlumeParcel p = PlumeParcels.At(i, timeSeconds, ventRadiusMetres,
                                                columnHeightMetres, windX, windZ, seed);

                _buffer[i].position = new Vector3(vent.X + p.X, vent.Y + p.Y, vent.Z + p.Z);

                // startSize is a **diameter**, so double the radius.
                _buffer[i].startSize = p.RadiusMetres * PuffSizeGain * 2f;
                _buffer[i].rotation = p.RotationDegrees;

                // ★ The strength affects the density too. A weak eruption's plume is see-through.
                float alpha = p.Alpha * (0.45f + 0.55f * unit);

                // ★ The brightness is decided by height (ash at the bottom, white at the top), but
                //   a per-puff scatter is added on top. Without it, everything at the same height
                //   comes out the same colour.
                float mix = p.Brightness
                            - AshMixSpread * 0.5f
                            + AshMixSpread * DeterministicRandom.Unit(seed, (uint)i * 13u + 5u);

                _buffer[i].startColor = Blend(SunlitColor, AshColor, mix, alpha);

                // ★ Reset to the cap every frame. **Do not let the simulation age them**
                //   (this is the substance of "use it purely as a renderer" in the class doc).
                _buffer[i].remainingLifetime = 1000f;
                _buffer[i].startLifetime = 1000f;
            }

            _system.SetParticles(_buffer, count);

            // ★★ **Move the GameObject to where the particles are.** (2026-08-22, live report
            //    "it disappears the moment lightning strikes")
            //
            //    The particles are placed in world coordinates (simulationSpace = World), but
            //    <b>the GameObject was left sitting at the origin (0,0,0) the whole time</b>.
            //    Unity frustum-culls a <c>ParticleSystemRenderer</c> by <b>bounds based on the
            //    transform</b>, so **the moment the origin leaves the screen the whole system
            //    disappears**.
            //
            //    Found in ④ (there it vanished when the camera moved in on the lightning). ⑤ is
            //    usually looking at the crater, so it just surfaces less often — **it is the same
            //    hole**.
            //
            //    ★ This is not a path that throws, so it is not wrapped in a try.
            _object.transform.position = new Vector3(vent.X, vent.Y, vent.Z);

            PuffsPlaced = count;
            Drawing = true;
            LastFailure = null;
            return true;
        }

        /// <summary>
        /// Prepare the <c>ParticleSystem</c> that acts as the renderer. **One per city.**
        /// </summary>
        private static bool EnsureSystem(Material material)
        {
            if (_object != null && _system != null && _buffer != null)
            {
                // ★★ **The material is shared with ④** (<see cref="CloudParticleAssets"/>).
                //    <c>TyphoonCloudFx.Destroy</c> destroys it, so when a typhoon ends or the
                //    clouds are turned off in the settings, <b>the material vanishes from under us
                //    in the middle of an eruption.</b>
                //    At that point <c>renderer.material</c> becomes a Unity fake-null and the
                //    plume goes silently invisible — the GameObject is still alive, so unless we
                //    check here, nobody can notice.
                var live = _system.GetComponent<ParticleSystemRenderer>();
                if (live != null && live.sharedMaterial == null)
                {
                    live.material = material;
                }
                return true;
            }

            Destroy();

            var go = new GameObject("DisasterPlus_VolcanoPlumePuffs");
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            // ★ Positions go in as **world coordinates in metres**.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = PlumeParcels.Count;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;

            // ★★ **Do not let the game spawn a single particle.** We place them.
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // ★ These are large, soft, overlapping puffs, so without depth sorting the edges come
            //   out dirty.
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = MaxScreenFraction;
            renderer.material = material;

            ps.Play();

            _object = go;
            _system = ps;
            _buffer = new ParticleSystem.Particle[PlumeParcels.Count];
            return true;
        }

        /// <summary>
        /// **Call on level unload and when the eruption ends.** Idempotent.
        ///
        /// The <c>GameObject</c> is ours, so it is destroyed. The <c>Material</c> and
        /// <c>Texture2D</c> belong to <see cref="CloudParticleAssets"/>, so **do not touch them**.
        /// </summary>
        public static void Destroy()
        {
            if (_object != null) UnityEngine.Object.Destroy(_object);

            _object = null;
            _system = null;
            _buffer = null;
            Drawing = false;
            PuffsPlaced = 0;
        }

        private static Color32 Blend(Color32 sunlit, Color32 ash, float brightness, float alpha)
        {
            float t = Clamp01(brightness);
            float a = Clamp01(alpha) * 255f;

            return new Color32(
                (byte)(ash.r + (sunlit.r - ash.r) * t),
                (byte)(ash.g + (sunlit.g - ash.g) * t),
                (byte)(ash.b + (sunlit.b - ash.b) * t),
                (byte)a);
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
