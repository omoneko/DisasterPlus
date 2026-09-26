using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Tools.TyphoonPreview
{
    /// <summary>
    /// Expands the <b>driving rain</b> (<see cref="SquallLayout"/>) into particles, copied
    /// straight from the IL measurements of <c>ParticleEffect.EmitParticles</c> (§B-4).
    /// The game is not launched. The maths is the same as <see cref="Vortex"/>.
    ///
    /// ★ There is only one difference from the game: the automatic throttling of
    ///   <c>maxParticles</c> is reproduced here as "fix the total at
    ///   <c>SquallLayout.MaxParticles</c> and use only the ratio between patches".
    ///   The steady-state picture matches with that.
    /// </summary>
    internal static class Squall
    {
        private const float Gravity = 9.81f;

        /// <summary>
        /// The spray at a camera height of <paramref name="cameraHeight"/> m and a squall
        /// strength of <paramref name="strength"/> [0,1]. The origin is the ground directly
        /// below the camera.
        /// </summary>
        internal static Speck[] Build(float cameraHeight, float strength, uint seed)
        {
            var list = new System.Collections.Generic.List<Speck>();
            if (!(strength > 0f)) return list.ToArray();

            // The wind blows towards +X (so the picture shows it coming in from the side). In
            // the game it is the typhoon's tangential component plus the inflow.
            float drift = SquallLayout.DriftMetresPerSecond * strength;
            float driftX = drift;
            float driftZ = 0f;

            // ★ The same rule as the game. The origin is the **ground** directly below the
            //   camera (y = 0).
            float spread = SquallLayout.SpreadFor(cameraHeight);

            // The distribution across patches (spawns per second x lifetime = the ratio of the
            // steady-state population).
            var share = new float[SquallLayout.PatchCount];
            float total = 0f;
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch patch = SquallLayout.PatchAt(i);
                float disc = patch.DiscFraction * spread;
                float magnitude = ParticleBudget.MagnitudeFor(
                    disc, SquallLayout.RateOverTime,
                    SquallLayout.ParticlesPerSecond * strength, SquallLayout.PatchCount);
                float area = 3.14159265f * disc * disc;
                if (area < 100f) area = 100f;

                share[i] = area * magnitude * 0.01f * SquallLayout.RateOverTime
                           * patch.DensityFraction;
                total += share[i];
            }
            if (!(total > 0f)) return list.ToArray();

            uint draw = 0u;
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                if (!(share[i] > 0f)) continue;
                SquallPatch patch = SquallLayout.PatchAt(i);

                float centreX = patch.OffsetXFraction * spread;
                float centreZ = patch.OffsetZFraction * spread;
                float centreY = patch.HeightFraction * SquallLayout.HeightMetres;
                float disc = patch.DiscFraction * spread;
                float band = patch.BandFraction * SquallLayout.HeightMetres;

                int count = (int)(SquallLayout.MaxParticles * (share[i] / total));
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

                    double theta = 2.0 * Math.PI * u1;
                    float rr = (float)Math.Sqrt(u2) * disc;
                    float px = centreX + (float)Math.Cos(theta) * rr;
                    float py = centreY + u3 * band;
                    float pz = centreZ + (float)Math.Sin(theta) * rr;

                    float ang = (SquallLayout.SpawnAngleMinDegrees
                                 + (SquallLayout.SpawnAngleMaxDegrees
                                    - SquallLayout.SpawnAngleMinDegrees) * u4)
                                * (float)(Math.PI / 180.0);
                    float speed = SquallLayout.SpeedMin
                                  + (SquallLayout.SpeedMax - SquallLayout.SpeedMin) * u5;
                    double side = 2.0 * Math.PI * u6;

                    float vy = (float)Math.Cos(ang) * speed;
                    float vx = (float)(Math.Sin(ang) * Math.Cos(side)) * speed + driftX;
                    float vz = (float)(Math.Sin(ang) * Math.Sin(side)) * speed + driftZ;

                    float life = SquallLayout.LifeMinSeconds
                                 + (SquallLayout.LifeMaxSeconds
                                    - SquallLayout.LifeMinSeconds) * u7;
                    float age = u8 * life;

                    px += vx * age;
                    py += vy * age - 0.5f * Gravity * SquallLayout.GravityModifier * age * age;
                    pz += vz * age;

                    float k = DeterministicRandom.Unit(seed + 1u, draw);
                    var speck = new Speck();
                    speck.X = px;
                    speck.Y = py;
                    speck.Z = pz;
                    speck.SizeMetres = SquallLayout.SizeMetres;
                    speck.R = SquallLayout.DarkRed
                              + (SquallLayout.BrightRed - SquallLayout.DarkRed) * k;
                    speck.G = SquallLayout.DarkGreen
                              + (SquallLayout.BrightGreen - SquallLayout.DarkGreen) * k;
                    speck.B = SquallLayout.DarkBlue
                              + (SquallLayout.BrightBlue - SquallLayout.DarkBlue) * k;
                    speck.Alpha = SquallLayout.Alpha;
                    list.Add(speck);
                }
            }

            return list.ToArray();
        }
    }
}
