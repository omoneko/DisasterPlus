using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>One cloud parcel (for rendering). The position is **relative to the vent**
    /// (m).</summary>
    public struct PlumeParcel
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        /// <summary>The parcel's radius (m). The rendering side must convert to a
        /// diameter.</summary>
        public readonly float RadiusMetres;

        /// <summary>Opacity <c>[0,1]</c>. It is 0 at birth and at the end.</summary>
        public readonly float Alpha;

        /// <summary>Brightness <c>[0,1]</c>. 0 is the black near the vent, 1 the white of the
        /// umbrella.</summary>
        public readonly float Brightness;

        /// <summary>The rotation angle (degrees). Each parcel turns slowly.</summary>
        public readonly float RotationDegrees;

        public PlumeParcel(float x, float y, float z, float radiusMetres,
                           float alpha, float brightness, float rotationDegrees)
        {
            X = x;
            Y = y;
            Z = z;
            RadiusMetres = radiusMetres;
            Alpha = alpha;
            Brightness = brightness;
            RotationDegrees = rotationDegrees;
        }
    }

    /// <summary>
    /// Animates the plume as <b>a swarm of parcels</b>. **Pure, engine-free functions only.**
    ///
    /// ── Why it was rebuilt (2026-08-22, the owner's observation) ─────────────────
    ///
    /// &gt; The plume animation still isn't realistic enough either. Rather than something
    /// &gt; geometric, I'd like a more natural, chaotic smoke animation. On top of the smoke
    /// &gt; effect, please also use parts of the nuclear mushroom cloud (MissileDisaster)
    /// &gt; effect to make it realistic.
    ///
    /// <see cref="EruptionColumn"/> splits the column into <b>9 stacked discs</b> and spawns
    /// the game's particle effects at them. As a cross-section (the textbook's three
    /// regions) that is correct, but <b>the discs do not move</b> — the spawn points are
    /// fixed, so from a distance it looks like "9 stacked plates". **Geometric, exactly as
    /// observed.**
    ///
    /// ── ★★ What changed: Eulerian → Lagrangian ──────────────────
    ///
    /// <list type="bullet">
    /// <item><b>Before</b> (Eulerian) … 9 frames fixed in space, with smoke passing through
    ///   them</item>
    /// <item><b>Now</b> (Lagrangian) … <b>we follow the parcels themselves.</b>
    ///   Each one is born at the crater, rises, swells, is carried by the wind, thins out
    ///   and disappears</item>
    /// </list>
    ///
    /// Once parcels have individual lives, the swarm becomes chaotic of its own accord —
    /// **we are not adding "jitter" with random numbers.** Hundreds of lives at different
    /// phases overlapping give you that look where new lobes keep welling up and collapsing
    /// along the plume's edge.
    ///
    /// ── What we use from MissileDisaster is <b>the technique</b> ────────────────
    ///
    /// The one thing we borrow from the mushroom cloud's <c>MushroomCloudPuffsFx</c> is
    /// "<b>use a <c>ParticleSystem</c> with emission switched off as a renderer, and place
    /// the particles ourselves every frame with <c>SetParticles</c></b>"
    /// (the same as ④'s <c>TyphoonVortexPuffFx</c> — a road we have been down once already).
    /// That frees us from the game particle prefab's constraint (a fixed particle size) and
    /// lets us **draw parcels hundreds of metres across**.
    ///
    /// ★★ <b>We do not borrow the shape.</b> A nuclear cloud is a single bubble (a thin stem
    ///   plus a round cap), whereas a volcano is a column with a continuous supply (see
    ///   <see cref="EruptionColumn"/>'s class doc).
    ///   What this copies is <see cref="EruptionColumn"/>'s cross-section.
    ///
    /// ── The life of one parcel ─────────────────────────────────────────
    ///
    /// <code>
    /// age = 0            born inside the crater (radius = crater × VentRadiusFactor)
    ///   ↓ the rise decelerates (RiseDecayPower: gas thrust → buoyancy → neutral)
    ///   ↓ the radius grows by entrainment (EntrainmentSlope)
    ///   ↓ it leans downwind (BendPower: stronger the higher it is = wind shear)
    ///   ↓ it is churned by eddies (TurbulenceOctaves — **this is what the chaos actually is**)
    /// age = life         it has spread fully sideways at the umbrella height, thins out and disappears
    /// </code>
    ///
    /// A parcel's phase is determined by the seed, so **the same eruption looks the same
    /// however many times you run it** (<see cref="DeterministicRandom"/> alone — this mod's
    /// discipline about randomness).
    /// </summary>
    public static class PlumeParcels
    {
        /// <summary>The total number of parcels. **Measure the rendering cost on real
        /// hardware before raising it.**</summary>
        public const int Count = 560;

        /// <summary>How long one parcel lives from birth to disappearance (seconds).</summary>
        public const float LifeSeconds = 26f;

        /// <summary>
        /// How many times <b>the column's radius at that point</b> one parcel's radius is.
        ///
        /// ★★ <b>Do not base it on the crater's radius.</b> (Noticed on 2026-08-22 by
        ///   rendering it offline.) The column gets fatter as it rises, so basing it on the
        ///   crater makes <b>parcels bigger than the column at its foot and mere grains of
        ///   sand at the umbrella</b>.
        ///   Make it proportional to the column's width at each height and the "graininess"
        ///   looks the same everywhere.
        /// </summary>
        public const float ParcelRadiusRatio = 0.34f;

        /// <summary>The spread of sizes between parcels (as a ratio of
        /// <see cref="ParcelRadiusRatio"/>).</summary>
        public const float ParcelRadiusSpread = 0.55f;

        /// <summary>How much they swell as they age (per second, as a ratio).</summary>
        public const float GrowthPerSecond = 0.022f;

        /// <summary>
        /// How many layers of turbulence we stack. **One is not enough** (it comes out as a
        /// clean wave). At three, eddies of differing periods interlock and the eye can no
        /// longer follow the repetition.
        /// </summary>
        public const int TurbulenceOctaves = 3;

        /// <summary>
        /// The size of the turbulence (as a ratio of the column's radius at that point).
        /// **Make it proportional to the column's width** — add it as an absolute value and
        /// only the narrow foot thrashes about.
        /// </summary>
        public const float TurbulenceRatio = 0.30f;

        /// <summary>The period of the slowest eddy (seconds).</summary>
        public const float TurbulenceBaseSeconds = 9f;

        /// <summary>The cap on how fast a parcel turns (degrees per second).</summary>
        public const float SpinDegreesPerSecond = 11f;

        /// <summary>The fraction of a parcel's life it takes to reach full opacity from
        /// birth.</summary>
        public const float FadeInFraction = 0.06f;

        /// <summary>The fraction of its life at which it starts to thin out.</summary>
        public const float FadeOutFraction = 0.62f;

        /// <summary>The opacity at its densest.</summary>
        public const float PeakAlpha = 0.82f;

        /// <summary>
        /// The state of one parcel. <paramref name="index"/> is
        /// <c>[0, <see cref="Count"/>)</c>.
        /// </summary>
        /// <param name="timeSeconds">Seconds since the eruption began (a continuously
        /// increasing value).</param>
        /// <param name="ventRadiusMetres">The crater's radius (m).</param>
        /// <param name="columnHeightMetres">The column's height (m; the same as
        /// <see cref="EruptionColumn"/>'s).</param>
        /// <param name="windX">The wind's direction and speed (m/s).</param>
        /// <param name="windZ">The same.</param>
        /// <param name="seed">This volcano's seed.</param>
        public static PlumeParcel At(int index, float timeSeconds, float ventRadiusMetres,
                                     float columnHeightMetres, float windX, float windZ,
                                     uint seed)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;

            float vent = Sane(ventRadiusMetres, 8f);
            float height = Sane(columnHeightMetres, EruptionColumn.HeightMinMetres);
            float t = IsBad(timeSeconds) ? 0f : timeSeconds;
            float wx = IsBad(windX) ? 0f : windX;
            float wz = IsBad(windZ) ? 0f : windZ;

            uint draw = (uint)index * 11u + 3u;

            // ★★ **Staggering the phases is the way into the chaos.** Have them all born at
            //    the same moment and the whole swarm pulses in unison (which looks
            //    "geometric" if anything does).
            float phase = DeterministicRandom.Unit(seed, draw);
            float age = Frac(t / LifeSeconds + phase) * LifeSeconds;
            float w = age / LifeSeconds;

            // ── The rise (decelerating) ──────────────────────────────
            // Of the form w^(1/RiseDecayPower): fast low down, easing off higher up.
            float climb = Pow(w, 1f / EruptionColumn.RiseDecayPower);
            float y = climb * height;

            // ── Where in the column's cross-section it sits ──────────────────────
            //
            // ★★ **Draw only a direction and a "fraction of the way out from the axis".**
            //    The actual distance comes from multiplying by
            //    <see cref="ColumnRadiusAt"/> (the column's radius at that height).
            //    Originally we placed them as "a point inside the crater × the widening
            //    factor × the umbrella's spread factor", but that <b>applied the spread
            //    twice</b>, so we got a blob filling the screen rather than a column
            //    (noticed in tools/PlumePreview).
            //    The fraction out from the axis never changes over a parcel's life — a parcel
            //    spreads out together with the column; it does not travel across it.
            float birthAngle = DeterministicRandom.Unit(seed, draw + 1u) * 6.2831853f;
            float axisFraction = (float)Math.Sqrt(DeterministicRandom.Unit(seed, draw + 2u));

            float columnRadius = ColumnRadiusAt(climb, vent, height);

            float x = (float)Math.Cos(birthAngle) * axisFraction * columnRadius;
            float z = (float)Math.Sin(birthAngle) * axisFraction * columnRadius;

            // ── Leaning downwind (stronger the higher it is = wind shear) ─────────────
            float bend = Pow(climb, EruptionColumn.BendPower)
                         * EruptionColumn.BendFactor * height
                         / EruptionColumn.ReferenceWindMetresPerSecond;
            x += wx * bend;
            z += wz * bend;

            // ── ★★ The turbulence. **This is what "chaotic smoke" actually is.** ────
            //    We stack three eddies of differing periods. Each parcel has its own seed,
            //    so neighbouring parcels twist in different directions.
            float scale = columnRadius * TurbulenceRatio;
            for (int o = 0; o < TurbulenceOctaves; o++)
            {
                float period = TurbulenceBaseSeconds / (1 << o);
                float amp = scale / (1 << o);
                uint os = seed + (uint)(o * 7919);

                float px = DeterministicRandom.Unit(os, draw + 3u) * 6.2831853f;
                float py = DeterministicRandom.Unit(os, draw + 4u) * 6.2831853f;
                float pz = DeterministicRandom.Unit(os, draw + 5u) * 6.2831853f;

                float k = 6.2831853f / period;
                x += (float)Math.Sin(k * age + px) * amp;
                y += (float)Math.Sin(k * age + py) * amp * 0.5f;
                z += (float)Math.Sin(k * age + pz) * amp;
            }

            // ── The parcel's own size (**proportional to the column's width there**) ────
            float sizePick = DeterministicRandom.Unit(seed, draw + 8u);
            float radius = columnRadius * ParcelRadiusRatio
                           * (1f - ParcelRadiusSpread * 0.5f + ParcelRadiusSpread * sizePick)
                           * (1f + GrowthPerSecond * age);

            // ── The density (0 at birth and at the end) ──────────────────────
            float alpha;
            if (w < FadeInFraction) alpha = w / FadeInFraction;
            else if (w > FadeOutFraction) alpha = (1f - w) / (1f - FadeOutFraction);
            else alpha = 1f;
            alpha *= PeakAlpha;

            // ── Brightness. Black near the vent, white up top where the sun catches it ────
            float brightness = Clamp01(climb * 1.25f);

            float spin = (DeterministicRandom.Unit(seed, draw + 6u) * 2f - 1f)
                         * SpinDegreesPerSecond * age
                         + DeterministicRandom.Unit(seed, draw + 7u) * 360f;

            return new PlumeParcel(x, y, z, radius, Clamp01(alpha), brightness, spin);
        }

        /// <summary>
        /// The column's radius (m) at a given height. <paramref name="climbUnit"/> is
        /// <c>height / column height</c>. It matches <see cref="EruptionColumn"/>'s
        /// cross-section — **if the two representations claim different widths, the ash
        /// column and the swarm of parcels look misaligned.**
        /// </summary>
        public static float ColumnRadiusAt(float climbUnit, float ventRadiusMetres,
                                           float columnHeightMetres)
        {
            float c = Clamp01(climbUnit);
            float vent = Sane(ventRadiusMetres, 8f);
            float height = Sane(columnHeightMetres, EruptionColumn.HeightMinMetres);

            float gasTop = EruptionColumn.GasThrustFraction;
            float base0 = vent * EruptionColumn.VentRadiusFactor;
            float gasTopRadius = vent * EruptionColumn.GasTopRadiusFactor;

            if (c <= gasTop)
            {
                float k = gasTop > 0f ? c / gasTop : 0f;
                return base0 + (gasTopRadius - base0) * k;
            }

            // The convective region: it widens by EntrainmentSlope for every metre of rise.
            float rise = (c - gasTop) * height;
            float r = gasTopRadius + rise * EruptionColumn.EntrainmentSlope;

            // ★★ **The umbrella is spread exactly once, here.** Multiply on the caller's
            //    side as well and it gets squared, giving a blob instead of a column (see
            //    the doc on how this class places things).
            if (c > EruptionColumn.UmbrellaBaseFraction)
            {
                float umbrella = (c - EruptionColumn.UmbrellaBaseFraction)
                                 / (1f - EruptionColumn.UmbrellaBaseFraction);
                r *= 1f + (EruptionColumn.UmbrellaSpread - 1f) * umbrella;
            }

            return r;
        }

        private static float Frac(float v)
        {
            if (IsBad(v)) return 0f;
            float f = v - (float)Math.Floor(v);
            return f < 0f ? f + 1f : f;
        }

        private static float Pow(float v, float p)
        {
            if (v <= 0f) return 0f;
            return (float)Math.Pow(v, p);
        }

        private static float Sane(float v, float fallback)
        {
            return IsBad(v) || v <= 0f ? fallback : v;
        }

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
