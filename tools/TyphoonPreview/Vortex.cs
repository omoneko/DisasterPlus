using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Tools.TyphoonPreview
{
    /// <summary>One particle (for rendering).</summary>
    internal struct Speck
    {
        public float X;
        public float Y;
        public float Z;
        public float SizeMetres;
        public float R;
        public float G;
        public float B;
        public float Alpha;
    }

    /// <summary>
    /// Expands the vortex laid out by <see cref="VortexPuffLayout"/> and
    /// <see cref="VortexCloudProfile"/> into a cloud of particles, **copied straight from the
    /// IL measurements of vanilla's <c>ParticleEffect.EmitParticles</c> (the effects
    /// measurement document, §B-4)**. The game is not launched.
    ///
    /// <code>
    /// position = puff centre + disc(radius disc) + up × [0, band)
    /// velocity = puff velocity + (up·cos a + sideways·sin a) × speed   a = spawn angle
    /// age      = uniform over [0, lifetime), i.e. the steady-state distribution
    /// count    ∝ max(100, π disc²) × magnitude × rateOverTime × lifetime
    /// </code>
    ///
    /// ★ In the game the automatic throttling of <c>maxParticles</c>
    ///   (<c>pps ×= 1 − fill²</c>) kicks in and **always caps it**, so here too the total
    ///   particle count per layer is fixed at <c>VortexCloudProfile.MaxParticles</c> and only
    ///   the distribution across puffs is decided by the ratio of <c>magnitude × disc²</c>
    ///   (i.e. the game's spawns per second) × lifetime.
    ///   This is the game's steady state.
    ///
    /// ★ The only randomness is <see cref="DeterministicRandom"/> (the same discipline as
    ///   Core). The same picture comes out every time.
    /// </summary>
    internal static class Vortex
    {
        /// <summary>The same rule as the game's
        /// <c>TyphoonCloudFx.VortexRadiusMetres</c>.</summary>
        internal const float VortexRadiusFactor = 1.35f;

        // ★ Keep this the same as the game side (TyphoonCloudFx.MaxVortexRadiusMetres).
        //   On 2026-08-22 it was raised from 6000 to 8640 (half the map's side) ——
        //   while it was clamped at 6000, the cloud stopped growing above intensity 111.
        internal const float MaxVortexRadiusMetres = 8640f;

        internal const float MinVortexRadiusMetres = 900f;

        /// <summary>Derives the vortex's outer radius (m) from the storm radius (m).</summary>
        internal static float RadiusOf(float stormRadiusMetres)
        {
            float r = stormRadiusMetres * VortexRadiusFactor;
            if (r > MaxVortexRadiusMetres) r = MaxVortexRadiusMetres;
            if (r < MinVortexRadiusMetres) r = MinVortexRadiusMetres;
            return r;
        }

        /// <summary><c>GravityModifier</c> multiplies this gravity (m/s²), as in Unity.</summary>
        private const float Gravity = 9.81f;

        /// <summary>The same value as the game's <c>TyphoonCloudFx</c>. **Do not let it
        /// drift.**</summary>
        internal const float ThicknessMetres = 2200f;

        internal const float SwirlMetresPerSecond = 34f;

        internal const float RadialMetresPerSecond = 20f;

        internal const float RiseMetresPerSecond = 16f;

        internal const float RateOverTime = 20f;

        internal const float ParticlesPerSecond = 700f;

        internal const float MinSizeMetres = 40f;

        internal const float MaxSizeMetres = 900f;

        /// <summary>
        /// Expands the whole vortex into particles. <paramref name="radius"/> is the gale
        /// radius (m). <paramref name="spinDegrees"/> is the vortex's rotation angle (the same
        /// as the game's <c>TyphoonCloud</c>).
        /// </summary>
        internal static Speck[] Build(float radius, float spinDegrees, uint seed)
        {
            var list = new System.Collections.Generic.List<Speck>();

            for (int layerIndex = 0; layerIndex < 3; layerIndex++)
            {
                var layer = (VortexCloudLayer)layerIndex;
                VortexCloudProfile profile = VortexCloudProfile.Of(layer);

                // The distribution across puffs (spawns per second x lifetime = the ratio of
                // the steady-state population).
                var share = new float[VortexPuffLayout.PuffCount];
                float total = 0f;
                for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
                {
                    VortexPuff puff = VortexPuffLayout.PuffAt(i);
                    if (puff.Layer != layer) continue;

                    float disc = puff.DiscFraction * radius;
                    float magnitude = VortexPuffLayout.MagnitudeFor(
                        disc, RateOverTime, ParticlesPerSecond, VortexPuffLayout.PuffCount);
                    float area = 3.14159265f * disc * disc;
                    if (area < 100f) area = 100f;

                    share[i] = area * magnitude * 0.01f * RateOverTime * puff.DensityFraction;
                    total += share[i];
                }
                if (!(total > 0f)) continue;

                float size = radius * profile.SizeFraction;
                if (size < MinSizeMetres) size = MinSizeMetres;
                if (size > MaxSizeMetres) size = MaxSizeMetres;

                float spin = spinDegrees * 0.0174532925f;
                uint draw = 0u;

                for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
                {
                    if (!(share[i] > 0f)) continue;
                    VortexPuff puff = VortexPuffLayout.PuffAt(i);

                    int count = (int)(profile.MaxParticles * (share[i] / total));
                    if (count <= 0) continue;

                    float a = puff.AngleRadians + spin;
                    float cos = (float)Math.Cos(a);
                    float sin = (float)Math.Sin(a);
                    float r = puff.RadiusFraction * radius;

                    float centreX = cos * r;
                    float centreY = puff.HeightFraction * ThicknessMetres;
                    float centreZ = sin * r;

                    float disc = puff.DiscFraction * radius;
                    float band = puff.BandFraction * ThicknessMetres;

                    float swirl = SwirlMetresPerSecond * puff.SwirlFraction;
                    float radial = RadialMetresPerSecond * puff.RadialFraction;
                    float driftX = -sin * swirl + cos * radial;
                    float driftY = RiseMetresPerSecond * puff.RiseFraction;
                    float driftZ = cos * swirl + sin * radial;

                    for (int n = 0; n < count; n++)
                    {
                        draw++;
                        float u1 = DeterministicRandom.Unit(seed, draw * 9u + 1u);
                        float u2 = DeterministicRandom.Unit(seed, draw * 9u + 2u);
                        float u3 = DeterministicRandom.Unit(seed, draw * 9u + 3u);
                        float u4 = DeterministicRandom.Unit(seed, draw * 9u + 4u);
                        float u5 = DeterministicRandom.Unit(seed, draw * 9u + 5u);
                        float u6 = DeterministicRandom.Unit(seed, draw * 9u + 6u);
                        float u7 = DeterministicRandom.Unit(seed, draw * 9u + 7u);
                        float u8 = DeterministicRandom.Unit(seed, draw * 9u + 8u);

                        // Spawn position: disc × [0, band) (the same as EmitParticles as
                        // measured from the IL).
                        double theta = 2.0 * Math.PI * u1;
                        float rr = (float)Math.Sqrt(u2) * disc;
                        float px = centreX + (float)Math.Cos(theta) * rr;
                        float py = centreY + u3 * band;
                        float pz = centreZ + (float)Math.Sin(theta) * rr;

                        // Initial velocity: the axis (up) tilted by the spawn angle.
                        float ang = (profile.SpawnAngleMinDegrees
                                     + (profile.SpawnAngleMaxDegrees
                                        - profile.SpawnAngleMinDegrees) * u4)
                                    * (float)(Math.PI / 180.0);
                        float speed = profile.SpeedMin
                                      + (profile.SpeedMax - profile.SpeedMin) * u5;
                        double side = 2.0 * Math.PI * u6;

                        float vy = (float)Math.Cos(ang) * speed + driftY;
                        float vx = (float)(Math.Sin(ang) * Math.Cos(side)) * speed + driftX;
                        float vz = (float)(Math.Sin(ang) * Math.Sin(side)) * speed + driftZ;

                        float life = profile.LifeMinSeconds
                                     + (profile.LifeMaxSeconds - profile.LifeMinSeconds) * u7;
                        float age = u8 * life;

                        px += vx * age;
                        py += vy * age - 0.5f * Gravity * profile.GravityModifier * age * age;
                        pz += vz * age;

                        // The colour is one point on a two-colour gradient (the same as
                        // ParticleSystem.MinMaxGradient).
                        float k = DeterministicRandom.Unit(seed + 1u, draw);
                        var speck = new Speck();
                        speck.X = px;
                        speck.Y = py;
                        speck.Z = pz;
                        speck.SizeMetres = size;
                        speck.R = profile.DarkRed + (profile.BrightRed - profile.DarkRed) * k;
                        speck.G = profile.DarkGreen
                                  + (profile.BrightGreen - profile.DarkGreen) * k;
                        speck.B = profile.DarkBlue + (profile.BrightBlue - profile.DarkBlue) * k;
                        speck.Alpha = profile.Alpha;
                        list.Add(speck);
                    }
                }
            }

            return list.ToArray();
        }
    }
}
