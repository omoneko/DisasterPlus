using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>One burst's worth of explosion. Maps onto a single <c>DispatchEffect</c>
    /// call.</summary>
    public struct BlastBurst
    {
        /// <summary>Horizontal offset from the vent (m).</summary>
        public readonly float OffsetX;

        /// <summary>Height above the vent (m).</summary>
        public readonly float OffsetY;

        /// <summary>Horizontal offset from the vent (m).</summary>
        public readonly float OffsetZ;

        /// <summary>The <c>SpawnArea</c>'s radius (m).</summary>
        public readonly float RadiusMetres;

        /// <summary>
        /// The <c>SpawnArea</c>'s <b>vertical extent</b> (m).
        ///
        /// ★★ <b>It must not be 0.</b> (2026-08-22, the owner's report that "the effect
        ///   looks flat".) The three-argument
        ///   <c>SpawnArea(position, direction, radius)</c> writes
        ///   <c>m_halfHeight = 0</c> (confirmed at IL_006F-0075).
        ///   Particles spawn in "the disc + up × [0, halfHeight)", so at 0 they only spawn
        ///   in <b>a disc of zero thickness</b> — which is what "flat" actually was.
        ///   Use the four-argument version and pass this in.
        /// </summary>
        public readonly float HalfHeightMetres;

        /// <summary><c>DispatchEffect</c>'s <c>magnitude</c> (i.e. the particle density).</summary>
        public readonly float Magnitude;

        /// <summary>
        /// How many frames to delay before queueing it. **Do not fire them all at once** —
        /// fired at once it looks like "one flat disc" rather than one large explosion.
        /// </summary>
        public readonly int DelayFrames;

        public BlastBurst(float offsetX, float offsetY, float offsetZ,
                          float radiusMetres, float halfHeightMetres,
                          float magnitude, int delayFrames)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RadiusMetres = radiusMetres;
            HalfHeightMetres = halfHeightMetres;
            Magnitude = magnitude;
            DelayFrames = delayFrames;
        }
    }

    /// <summary>
    /// Decides <b>how many <c>DispatchEffect</c> bursts one explosion is split into</b>.
    /// **Pure engine-free functions only.**
    ///
    /// ── Why one burst will not do (2026-08-22, the owner's report) ──────────────
    ///
    /// &gt; The explosion effect is not to scale — the explosion during a caldera-forming
    /// &gt; eruption in particular is far too feeble.
    ///
    /// It used to call <c>DispatchEffect</c> <b>once</b> and widen the radius alone, as
    /// <c>crater radius × (0.35 + 0.45·strength)</c>. But <b>widening the
    /// <c>SpawnArea</c>'s radius does not make the particles bigger</b> —
    /// the particle count for one frame is
    /// <c>max(100, π r²) × magnitude × 0.01 × rateOverTime</c>, proportional to area, but
    /// the size of one <c>Medium Explosion Particles</c> particle is fixed on the prefab
    /// side (the same story as the measured note about <c>ShaderPool</c>).
    /// So doubling the radius merely <b>scatters the same-sized particles more thinly</b>,
    /// giving "a sparse explosion" rather than "a large explosion". **The report was
    /// right.**
    ///
    /// There is only one way to make it look big — <b>increase the number and stagger them
    /// so they overlap</b>. A real volcanic explosion is not a single sphere either, but a
    /// collection of blobs bursting out of the conduit one after another.
    ///
    /// ── ★★ A caldera-forming eruption erupts from a "ring fissure" ──────────────────────
    ///
    /// The eruption of a caldera-forming episode blows up <b>along the ring fault at the
    /// edge of the collapsing roof, not from a central crater</b> (a ring-fissure
    /// eruption). This is also the essence of "the caldera-forming explosion is feeble" —
    /// light up only the single central point and no explosion is visible anywhere in a
    /// 5 km radius caldera.
    /// Pass <c>ringRadiusMetres</c> to <see cref="For"/> and some of the bursts are
    /// distributed along that ring.
    ///
    /// ── The size is decided on two axes ─────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>Strength</b> (<c>unit</c>) … the eruption's envelope. As before</item>
    /// <item><b>The mountain's size</b> (<c>sizeUnit</c>) … <b>this was the missing one.</b>
    ///   Put the slider at 25.5 and only the crater radius changed; the number of bursts
    ///   stayed the same</item>
    /// </list>
    /// </summary>
    public static class BlastCluster
    {
        /// <summary>The floor on how many bursts one explosion queues (smallest volcano,
        /// weakest moment).</summary>
        public const int MinBursts = 3;

        /// <summary>The ceiling on the same. **Do not raise it past this** —
        /// every <c>DispatchEffect</c> call consumes the game's effect queue.</summary>
        public const int MaxBursts = 40;

        /// <summary>How far the mountain's size raises the burst count (as a multiple).</summary>
        public const float SizeCountGain = 3.4f;

        /// <summary>How much further the burst count is multiplied in a great explosion (a
        /// caldera-forming episode).</summary>
        public const float ClimaxCountGain = 2.6f;

        /// <summary>How much the density of one burst is multiplied in a great explosion.</summary>
        public const float ClimaxMagnitudeGain = 1.8f;

        /// <summary>
        /// How far the blobs scatter (as a fraction of the crater radius).
        ///
        /// ★★ <b>1.15 was too wide</b> (2026-08-22, the owner's report that "the position
        ///   looks slightly off"). On a volcano with a crater radius of 351 m that is up to
        ///   404 m — i.e. <b>the explosions were scattering outside the crater</b>. Instead
        ///   of one large explosion it looks like scattered bursts going off around the
        ///   crater. Keep it inside the crater.
        /// </summary>
        public const float SpreadRatio = 0.55f;

        /// <summary>
        /// How high the blobs stack (as a fraction of the crater radius). **Do not scatter
        /// sideways only.**
        ///
        /// ★★ As above. At 1.9, with a crater radius of 351 m, explosions were floating
        ///   <b>667 m above the vent</b>. That height would be fine for an eruption column,
        ///   but an explosion happens at the crater.
        /// </summary>
        public const float RiseRatio = 0.7f;

        /// <summary>
        /// The vertical extent of one burst (as a fraction of that burst's radius).
        /// **The closer to 1, the more it reads as a sphere.** 0 is a disc (see
        /// <c>BlastBurst.HalfHeightMetres</c>).
        /// </summary>
        public const float HalfHeightRatio = 1.25f;

        /// <summary>The floor on one burst's <c>SpawnArea</c> radius (as a fraction of the
        /// crater radius).</summary>
        public const float BurstRadiusMinRatio = 0.22f;

        /// <summary>The ceiling on the same.</summary>
        public const float BurstRadiusMaxRatio = 0.62f;

        /// <summary>How many frames until all the bursts are out.</summary>
        public const int SpreadFrames = 26;

        /// <summary>
        /// The fraction of bursts distributed to the ring (i.e. the caldera's edge). It only
        /// means anything for a great explosion.
        /// Not 1 — the central conduit is erupting at the same time.
        /// </summary>
        public const float RingShare = 0.55f;

        /// <summary>How far the bursts along the ring scatter from it (as a fraction of the
        /// ring's radius).</summary>
        public const float RingJitterRatio = 0.10f;

        /// <summary>
        /// How many bursts this one explosion is split into.
        /// </summary>
        /// <param name="sizeUnit">
        /// The mountain's size <c>[0,1]</c>. 0 at the recommended size, 1 at the top of the slider.
        /// </param>
        /// <param name="climax">Whether this is the great explosion of a caldera-forming
        /// episode.</param>
        public static int CountFor(float unit, float sizeUnit, bool climax)
        {
            float u = Clamp01(unit);
            float s = Clamp01(sizeUnit);

            float n = MinBursts * (1f + SizeCountGain * s) * (0.45f + 0.55f * u);
            if (climax) n *= ClimaxCountGain;

            int count = (int)(n + 0.5f);
            if (count < MinBursts) count = MinBursts;
            if (count > MaxBursts) count = MaxBursts;
            return count;
        }

        /// <summary>
        /// Burst number <paramref name="index"/>. <paramref name="index"/> is in
        /// <c>[0, <see cref="CountFor"/>)</c>.
        /// </summary>
        /// <param name="craterRadiusMetres">The crater radius (m). The baseline for how far
        /// the blobs scatter.</param>
        /// <param name="ringRadiusMetres">
        /// The ring fissure's radius (m). **0 means do not use a ring** (an ordinary eruption).
        /// </param>
        /// <param name="seed">This explosion's seed.</param>
        public static BlastBurst For(int index, int count, float unit, float sizeUnit,
                                     bool climax, float craterRadiusMetres,
                                     float ringRadiusMetres, uint seed)
        {
            if (index < 0) index = 0;
            if (count < 1) count = 1;
            if (index >= count) index = count - 1;

            float u = Clamp01(unit);
            float crater = IsBad(craterRadiusMetres) || craterRadiusMetres < MinRadius
                ? MinRadius
                : craterRadiusMetres;

            uint draw = (uint)index * 7u + 1u;
            float a = DeterministicRandom.Unit(seed, draw) * 6.2831853f;
            float rr = DeterministicRandom.Unit(seed, draw + 1u);
            float rise = DeterministicRandom.Unit(seed, draw + 2u);
            float sizePick = DeterministicRandom.Unit(seed, draw + 3u);
            float delayPick = DeterministicRandom.Unit(seed, draw + 4u);

            // ★★ The ring fissure (caldera-forming eruptions only). **Some fraction of the
            //    bursts is distributed along the ring.**
            bool onRing = climax
                          && !IsBad(ringRadiusMetres)
                          && ringRadiusMetres > crater
                          && DeterministicRandom.Unit(seed, draw + 5u) < RingShare;

            float distance;
            if (onRing)
            {
                float jitter = (rr * 2f - 1f) * RingJitterRatio * ringRadiusMetres;
                distance = ringRadiusMetres + jitter;
            }
            else
            {
                // Uniform over the disc (hence the sqrt). Do not let them bunch up at the centre.
                distance = (float)Math.Sqrt(rr) * SpreadRatio * crater;
            }

            float offsetX = (float)Math.Cos(a) * distance;
            float offsetZ = (float)Math.Sin(a) * distance;

            // How high they stack. **The ring's bursts are low** (they blow out sideways
            // from the edge).
            float offsetY = rise * RiseRatio * crater * (onRing ? 0.35f : 1f);

            float radius = crater * (BurstRadiusMinRatio
                                     + (BurstRadiusMaxRatio - BurstRadiusMinRatio) * sizePick);
            if (radius < MinRadius) radius = MinRadius;

            float magnitude = EruptionEffectPlan.BlastMagnitude(u);
            // ★ Do not thin out each burst to compensate for having more of them — thinning
            //   cancels out exactly the point of adding them (this is where it differs from
            //   the ash plume, which normalises by area. That one is continuous; this is a
            //   one-shot).
            if (climax) magnitude *= ClimaxMagnitudeGain;

            int delay = (int)(delayPick * SpreadFrames);
            if (delay < 0) delay = 0;
            if (delay > SpreadFrames) delay = SpreadFrames;

            return new BlastBurst(offsetX, offsetY, offsetZ, radius,
                                  radius * HalfHeightRatio, magnitude, delay);
        }

        /// <summary>
        /// The mountain's size <c>[0,1]</c>. 0 at the recommended size
        /// (<c>VolcanoShape.DefaultRadiusOf</c>), 1 at the top of that form's band.
        /// **Measured from the real dimensions, not from the slider itself** — each form
        /// has a different band, so the slider position alone does not determine the size.
        /// </summary>
        public static float SizeUnitOf(float radiusMetres, float defaultRadiusMetres,
                                       float maxRadiusMetres)
        {
            if (IsBad(radiusMetres) || IsBad(defaultRadiusMetres) || IsBad(maxRadiusMetres)) return 0f;
            if (maxRadiusMetres <= defaultRadiusMetres) return 0f;
            if (radiusMetres <= defaultRadiusMetres) return 0f;

            return Clamp01((radiusMetres - defaultRadiusMetres)
                           / (maxRadiusMetres - defaultRadiusMetres));
        }

        private const float MinRadius = EruptionEffectPlan.MinRadiusMetres;

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
