using System;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Tools.TyphoonPreview
{
    /// <summary>
    /// Draws **the vortex built from our own white cloud puffs** (the 2026-08-22 rebuild).
    ///
    /// It is for checking, **before putting it in the game**, the answer to the owner's
    /// remark: "I can still see something like smoke — could you show the white cloud used
    /// by MissileDisaster's mushroom cloud effect, arranged as a vortex up in the sky?"
    ///
    /// ── Where this differs from <see cref="Vortex"/> (the old one) ─────────────────────
    ///
    /// | | old (scatter borrowed particles) | new (place our own) |
    /// |---|---|---|
    /// | number of puffs | thousands per puff position (vanilla spawns them) | **exactly 88** (<c>PuffCount</c>) |
    /// | position | scattered inside a disc and left to drift | **placed exactly as the table says** (no drift) |
    /// | image | <c>steam</c> (mean alpha 0.43) | **an opaque core** (1.0 out to 0.42) |
    ///
    /// The puff's image is evaluated with the <b>same formula</b> as
    /// <c>Game/Common/CloudParticleAssets.BuildTexture</c> (1 out to the core at
    /// <see cref="CoreEnd"/>, 0 at <see cref="EdgeEnd"/>, with the rim wobbling three times
    /// round). **It is not an approximation.**
    /// </summary>
    internal static class OwnedVortex
    {
        // The same values as Game/Common/CloudParticleAssets (that one is in Game, so it
        // cannot be referenced from here).
        private const float CoreEnd = 0.42f;
        private const float EdgeEnd = 0.95f;
        private const float RimWobble = 0.10f;

        // The same values as Game/Typhoon/TyphoonVortexPuffFx.
        private const float PuffSizeGain = 1.0f;
        private const float MaxAlpha = 0.95f;
        private const float MinAlpha = 0.42f;
        private const float ThicknessMetres = 2200f;

        /// <summary>One placed puff.</summary>
        internal struct Puff
        {
            public float X;
            public float Y;
            public float Z;
            public float Radius;
            public float Alpha;
            public float Shade;   // 0 = the sunlit white, 1 = the grey of the underside
        }

        /// <summary>
        /// The whole vortex. <paramref name="radius"/> is the gale radius (m).
        ///
        /// ★ The crowd is assembled by <see cref="VortexPuffCrowd"/> (the real thing from
        ///   Core). It is exactly the same table as on the game side
        ///   (<c>TyphoonVortexPuffFx</c>).
        /// </summary>
        internal static Puff[] Build(float radius, float spinDegrees)
        {
            var crowd = new CrowdPuff[VortexPuffCrowd.TotalCount];
            int n = VortexPuffCrowd.Build(crowd);

            var puffs = new Puff[n];
            float spin = spinDegrees * 0.0174532925f;

            for (int i = 0; i < n; i++)
            {
                CrowdPuff p = crowd[i];

                float a = p.AngleRadians + spin;
                float r = p.RadiusFraction * radius;

                puffs[i].X = (float)Math.Cos(a) * r;
                puffs[i].Y = p.HeightFraction * ThicknessMetres;
                puffs[i].Z = (float)Math.Sin(a) * r;

                puffs[i].Radius = p.SizeFraction * radius * PuffSizeGain;

                puffs[i].Alpha = MinAlpha + (MaxAlpha - MinAlpha) * Clamp01(p.DensityFraction);
                puffs[i].Shade = 1f - Clamp01(p.HeightFraction);
            }

            return puffs;
        }

        /// <summary>
        /// The texture's opacity. Derived from the fraction of the radius from the centre,
        /// <paramref name="d"/> (0-1 and beyond), and the angle, with the same formula as
        /// <c>CloudParticleAssets.BuildTexture</c>.
        /// </summary>
        internal static float TextureAlpha(float d, float angle)
        {
            float wobble = 1f + RimWobble * (float)Math.Sin(3.0 * angle);
            if (wobble < 0.0001f) wobble = 0.0001f;

            float t = (d / wobble - CoreEnd) / (EdgeEnd - CoreEnd);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>The opacity measured in the body of the cloud (near the centre). **It
        /// should be around 0.99.**</summary>
        internal static float CoreOpacity()
        {
            return TextureAlpha(0.2f, 0f);
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
