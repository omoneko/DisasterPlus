using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>One blob of cloud (for drawing). Its position is **relative to the typhoon's
    /// eye** (m).</summary>
    public struct TyphoonParcel
    {
        public readonly float X;

        /// <summary>Height above the cloud base (m).</summary>
        public readonly float Y;

        public readonly float Z;

        /// <summary>The blob's radius (m). The drawing side must convert it to a
        /// diameter.</summary>
        public readonly float RadiusMetres;

        /// <summary>Opacity <c>[0,1]</c>. It is 0 at birth and at the end.</summary>
        public readonly float Alpha;

        /// <summary>Brightness <c>[0,1]</c>. 0 is the shadow at the cloud base, 1 the sunlit
        /// cloud top.</summary>
        public readonly float Brightness;

        /// <summary>Rotation angle (degrees).</summary>
        public readonly float RotationDegrees;

        public TyphoonParcel(float x, float y, float z, float radiusMetres,
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
    /// Moves the typhoon's cloud as <b>a crowd of parcels</b>. **Pure engine-free functions only.**
    ///
    /// ── The owner's instruction (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; The typhoon's cloud effect only appears for a moment and then vanishes. The
    /// &gt; volcano's eruption cloud has the right texture, so please build the typhoon's
    /// &gt; cloud by applying the eruption-cloud effect.
    ///
    /// ── ★★ Built the same way as <see cref="Volcano.PlumeParcels"/> ───────────────────
    ///
    /// It takes the same road the ash plume went down:
    ///
    /// <list type="bullet">
    /// <item>Each parcel <b>has a life of its own</b> (born → moves → thins out and
    ///   disappears). Overlap hundreds of lives with staggered phases and the crowd becomes
    ///   chaotic by itself</item>
    /// <item>Stack <see cref="TurbulenceOctaves"/> eddies of differing period. With one you
    ///   get a tidy wave and the eye follows the repetition</item>
    /// <item>What it draws is <b>a <c>ParticleSystem</c> with emission stopped</b>; we place
    ///   the particles ourselves every frame with <c>SetParticles</c></item>
    /// </list>
    ///
    /// ★★ <b>Why this fixes "only appears for a moment and then vanishes".</b>
    ///   The previous <c>VortexPuffCrowd</c> was <b>a static arrangement decided by the
    ///   indices alone</b>, and the only movement was the rotation the caller added. So it
    ///   amounted to <b>the cloud itself never being updated once placed</b>, and once the
    ///   borrowed vanilla particles (which die naturally at the end of their lifetime) were
    ///   gone, nothing remained. Here <b>the positions change every frame</b>, so the cloud
    ///   does not disappear as long as the re-placing does not stop.
    ///
    /// ── The shape is the typhoon's, not the ash plume's ────────────────────────
    ///
    /// What is borrowed is <b>the method</b>; the shape is a separate matter:
    ///
    /// <code>
    /// ash plume: born at the crater, rises, spreads sideways at the umbrella (a vertical column)
    /// typhoon:   born on the outside, **spirals inwards and is drawn into the eye**,
    ///            rises at the eyewall, and is spat outwards from the cloud top (a horizontal vortex)
    /// </code>
    ///
    /// Inside <see cref="EyeFraction"/>, <b>no cloud is placed</b> —
    /// the eye of a typhoon really is clear. Fill it in and it looks like a plain disc.
    /// </summary>
    public static class TyphoonCloudParcels
    {
        /// <summary>The total number of parcels. **Measure the in-game draw cost before
        /// raising it.**</summary>
        public const int Count = 900;

        /// <summary>How long one parcel lasts from birth to disappearance (seconds). **A
        /// typhoon is slower than an ash plume.**</summary>
        public const float LifeSeconds = 64f;

        /// <summary>
        /// The eye's radius (as a fraction of the outer radius). **No cloud is placed here.**
        ///
        /// ★★ At 0.10 it filled in (2026-08-22, spotted in tools/TyphoonParcelPreview:
        ///   "eye coverage 100%"). One parcel's radius was about the same as the eye's
        ///   radius, so a parcel placed on the eyewall <b>spilled straight into the eye</b>.
        ///   Test against <b>the parcel's inner edge</b> (see <see cref="At"/>).
        /// </summary>
        public const float EyeFraction = 0.20f;

        /// <summary>The eyewall's radius (the densest and tallest ring) (as above).</summary>
        public const float EyewallFraction = 0.28f;

        /// <summary>How far it moves inwards over its lifetime (as a fraction of the outer
        /// radius).</summary>
        public const float InflowFraction = 0.34f;

        /// <summary>The angle swept over its lifetime (radians). **This sets how many turns
        /// the spiral makes.**</summary>
        public const float SwirlRadians = 2.6f;

        /// <summary>How much faster the inside turns (at 0 it is rigid rotation, i.e. it looks
        /// like a turning plate).</summary>
        public const float DifferentialSpin = 1.7f;

        /// <summary>
        /// The number of spiral rainbands.
        ///
        /// ★★ <b>Without these it does not look like a typhoon.</b> (2026-08-22, spotted by
        ///   drawing it offline.) Place the parcels uniformly in angle and, however good the
        ///   texture is, all you get is <b>a plain round blob</b>. A real typhoon is made of
        ///   the eyewall around the eye and several arms winding outwards from it.
        /// </summary>
        public const int ArmCount = 4;

        /// <summary>
        /// How tightly the arms wind. It is the coefficient in the logarithmic spiral
        /// <c>θ = θ0 + Tightness × ln(r)</c>; the larger it is, the tighter the winding.
        /// </summary>
        public const float SpiralTightness = 2.6f;

        /// <summary>The scatter away from an arm (radians). **At 0 you get a thin line.**</summary>
        public const float ArmScatterRadians = 0.22f;

        /// <summary>The fraction of parcels that belong to no arm and fill the gaps between
        /// them.</summary>
        public const float StrayShare = 0.08f;

        /// <summary>The cloud's thickness (as a fraction of the outer radius).</summary>
        public const float ThicknessFraction = 0.16f;

        /// <summary>How much higher the eyewall rises than the rest (as a fraction of the
        /// thickness).</summary>
        public const float EyewallLiftFraction = 0.85f;

        /// <summary>One parcel's radius (as a fraction of the outer radius).</summary>
        public const float ParcelRadiusFraction = 0.075f;

        /// <summary>The spread of sizes between parcels (as a proportion of the fraction
        /// above).</summary>
        public const float ParcelRadiusSpread = 0.6f;

        /// <summary>How many octaves of turbulence are stacked.</summary>
        public const int TurbulenceOctaves = 3;

        /// <summary>The size of the turbulence (as a fraction of a parcel's radius).</summary>
        public const float TurbulenceRatio = 0.8f;

        /// <summary>The period of the slowest eddy (seconds).</summary>
        public const float TurbulenceBaseSeconds = 23f;

        /// <summary>The fraction of the lifetime spent thickening up after birth.</summary>
        public const float FadeInFraction = 0.08f;

        /// <summary>The fraction of the lifetime at which it starts thinning out.</summary>
        public const float FadeOutFraction = 0.72f;

        /// <summary>The opacity at its densest.</summary>
        public const float PeakAlpha = 0.9f;

        /// <summary>
        /// The state of one parcel. <paramref name="index"/> is in
        /// <c>[0, <see cref="Count"/>)</c>.
        /// </summary>
        /// <param name="timeSeconds">Seconds, increasing continuously.</param>
        /// <param name="radiusMetres">The vortex's outer radius (m).</param>
        /// <param name="spinRadians">The whole vortex's rotation angle (accumulated by the
        /// caller).</param>
        /// <param name="seed">This typhoon's seed.</param>
        public static TyphoonParcel At(int index, float timeSeconds, float radiusMetres,
                                       float spinRadians, uint seed)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;

            float radius = IsBad(radiusMetres) || radiusMetres <= 0f ? 1000f : radiusMetres;
            float t = IsBad(timeSeconds) ? 0f : timeSeconds;
            float spin = IsBad(spinRadians) ? 0f : spinRadians;

            uint draw = (uint)index * 13u + 5u;

            // ★★ Stagger the phase (same as the ash plume. This is the doorway to chaos).
            float phase = DeterministicRandom.Unit(seed, draw);
            float age = Frac(t / LifeSeconds + phase) * LifeSeconds;
            float w = age / LifeSeconds;

            // ── The radius it is born at. The outside needs more parcels (more area), so sqrt ──
            float birth = EyewallFraction
                          + (1f - EyewallFraction)
                            * (float)Math.Sqrt(DeterministicRandom.Unit(seed, draw + 1u));

            // ── The spiral: it moves inwards over its lifetime. **It never enters the eye.** ───
            float fraction = birth - InflowFraction * w;
            if (fraction < EyeFraction) fraction = EyeFraction;

            // ── The parcel's own size (needed before the arm placement) ──────────────
            float sizePick = DeterministicRandom.Unit(seed, draw + 4u);
            float parcel = radius * ParcelRadiusFraction
                           * (1f - ParcelRadiusSpread * 0.5f + ParcelRadiusSpread * sizePick);

            // ★★ **Protect the eye using the parcel's inner edge.** Test against the
            //    distance to its centre and a parcel placed on the eyewall spills into the
            //    eye (which is what "the eye fills in" actually was).
            float minDistance = EyeFraction * radius + parcel;
            float distance = fraction * radius;
            if (distance < minDistance) distance = minDistance;
            fraction = radius > 0f ? distance / radius : fraction;

            // ── ★★ The spiral rainbands ────────────────────────────────
            //
            //    The logarithmic spiral θ = θ0 + Tightness × ln(r). Assign parcels to one of
            //    the arms and scatter them slightly from it. **Placed uniformly you get a
            //    plain round blob.**
            float armPick = DeterministicRandom.Unit(seed, draw + 9u);
            float scatter = DeterministicRandom.Unit(seed, draw + 10u) * 2f - 1f;

            float baseAngle;
            if (armPick < StrayShare)
            {
                // The share that fills the gaps between the arms. **Put them all on arms and
                // the outline goes hard.**
                baseAngle = DeterministicRandom.Unit(seed, draw + 2u) * 6.2831853f;
            }
            else
            {
                int arm = (int)(DeterministicRandom.Unit(seed, draw + 11u) * ArmCount);
                if (arm >= ArmCount) arm = ArmCount - 1;

                baseAngle = arm * (6.2831853f / ArmCount)
                            + SpiralTightness * (float)Math.Log(fraction > 1e-3f
                                                                ? fraction : 1e-3f)
                            + scatter * ArmScatterRadians;
            }

            // ★★ **Do not add the vortex's rotation from the age.** (2026-08-22, spotted by
            //    drawing it offline.) While <c>SwirlRadians × w × the faster-inside
            //    factor</c> was being added here, each parcel turned by up to 8 extra
            //    radians, and <b>the arms got neatly painted over into a plain ring</b>.
            //
            //    In a logarithmic spiral <b>the rotation comes out of moving inwards
            //    itself</b> — since <c>θ = θ0 + Tightness × ln(r)</c>, θ moves as r gets
            //    smaller. A parcel flows across the "pattern" that the arms are; the pattern
            //    does not turn wholesale of its own accord.
            float angle = baseAngle + spin;
            float x = (float)Math.Cos(angle) * distance;
            float z = (float)Math.Sin(angle) * distance;

            // ── Height. The eyewall is the tallest, thinning and dropping towards the rim ───
            float thickness = ThicknessFraction * radius;
            float wallness = Bell(fraction, EyewallFraction, EyewallFraction * 1.6f);
            float band = DeterministicRandom.Unit(seed, draw + 3u);
            float y = band * thickness * (0.35f + 0.65f * (1f - fraction))
                      + wallness * EyewallLiftFraction * thickness;

            // ── ★★ Turbulence. **This is what separates "a flat sheet" from "cloud".** ──────
            float scale = parcel * TurbulenceRatio;
            for (int o = 0; o < TurbulenceOctaves; o++)
            {
                float period = TurbulenceBaseSeconds / (1 << o);
                float amp = scale / (1 << o);
                uint os = seed + (uint)(o * 6373);

                float px = DeterministicRandom.Unit(os, draw + 5u) * 6.2831853f;
                float py = DeterministicRandom.Unit(os, draw + 6u) * 6.2831853f;
                float pz = DeterministicRandom.Unit(os, draw + 7u) * 6.2831853f;

                float k = 6.2831853f / period;
                x += (float)Math.Sin(k * age + px) * amp;
                y += (float)Math.Sin(k * age + py) * amp * 0.45f;
                z += (float)Math.Sin(k * age + pz) * amp;
            }

            // ★★ **Protecting the eye happens "after" the turbulence.** (2026-08-22, caught
            //    by a test.) Protect it first and the turbulence that follows pushes parcels
            //    back into the eye (they were getting 683 m into an eye of radius 840 m).
            //    Push them outwards here — **outwards, not in towards the centre**.
            float finalDistance = (float)Math.Sqrt(x * x + z * z);
            float keepOut = EyeFraction * radius + parcel;
            if (finalDistance < keepOut)
            {
                if (finalDistance > 1e-3f)
                {
                    float push = keepOut / finalDistance;
                    x *= push;
                    z *= push;
                }
                else
                {
                    // Exactly at the centre. There is no direction, so send it out along the
                    // angle it was born at.
                    x = (float)Math.Cos(angle) * keepOut;
                    z = (float)Math.Sin(angle) * keepOut;
                }
            }

            // ── Density ─────────────────────────────────────────
            float alpha;
            if (w < FadeInFraction) alpha = w / FadeInFraction;
            else if (w > FadeOutFraction) alpha = (1f - w) / (1f - FadeOutFraction);
            else alpha = 1f;

            // ★ The eyewall is the densest and the outer arms are thin.
            alpha *= PeakAlpha * (0.55f + 0.45f * (1f - fraction) + 0.3f * wallness);
            if (alpha > 1f) alpha = 1f;

            // ── Brightness. The higher it is, the more sun it catches and the whiter it is ───
            float brightness = Clamp01(y / (thickness * 1.4f));

            float rotation = DeterministicRandom.Unit(seed, draw + 8u) * 360f
                             + angle * 57.29578f;

            return new TyphoonParcel(x, y, z, parcel, Clamp01(alpha), brightness, rotation);
        }

        /// <summary>
        /// How close <paramref name="at"/> is to <paramref name="centre"/>, <c>[0,1]</c>.
        /// A bell that reaches 0 at <paramref name="width"/>.
        /// </summary>
        private static float Bell(float at, float centre, float width)
        {
            if (width <= 0f) return 0f;
            float d = at - centre;
            if (d < 0f) d = -d;
            if (d >= width) return 0f;

            float k = d / width;
            return 0.5f * (1f + (float)Math.Cos(Math.PI * k));
        }

        private static float Frac(float v)
        {
            if (IsBad(v)) return 0f;
            float f = v - (float)Math.Floor(v);
            return f < 0f ? f + 1f : f;
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
