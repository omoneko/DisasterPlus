using System;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// **The alpha profile** applied to the spiral cloud's ribbon. Pure, engine-free data;
    /// assembling the <c>Texture2D</c> is done by <c>Game/Typhoon/TyphoonCloud</c>
    /// (the same split of duties as <see cref="SpiralMesh"/>).
    ///
    /// ── Why it is needed (from the overall review) ──────────────────────
    ///
    /// <see cref="SpiralMesh"/> emits a UV per vertex (u = progress along the spiral,
    /// v = 0 on the inside / 1 on the outside), and yet the cloud material never once
    /// assigned <c>_MainTex</c>. In other words **those 4,608 UVs were dead data nobody
    /// read**, and the cloud came out as flat fill with hard ribbon edges. Only two things
    /// can be right — throw them away or use them — and we chose **to use** them: the UVs
    /// already hold meaningful values, and simply fading the edges takes "a ribbon cut out
    /// of paper" closer to "a cloud".
    ///
    /// ── What it guarantees ──────────────────────────────────────
    ///
    /// **It is always 0 at both edges of the ribbon (v = 0 and v = 1).** If it is not 0
    /// there, the mesh's edge shows through and you are back to a hard band. The maximum is
    /// at the centre (v = 0.5), and the value there is <b>exactly as dense as before</b> —
    /// that is, this change "only softens the edges" and makes the cloud as a whole neither
    /// denser nor thinner (this project's discipline of not strengthening the look before
    /// seeing it on real hardware).
    ///
    /// Along u it only fades the tip and the root of each arm slightly. The mesh already
    /// tapers the width (<c>SpiralMesh.TaperFloor</c>), so fading hard here would make the
    /// arms look short.
    ///
    /// We do not write RGB. The caller should fill it with **white** — the colour is held by
    /// the material's tint, and putting colour in the texture would mean deciding the colour
    /// in two places.
    /// (<c>Particles/Alpha Blended</c> is <c>tex * _TintColor</c>, so white × tint = the
    ///  tint itself. There is no path by which black creeps in.)
    /// </summary>
    public static class CloudBandAlpha
    {
        /// <summary>Texels per side. 64×64 = 4 KB (16 KB in RGBA32).
        /// An edge gradient needs no more resolution than this.</summary>
        public const int Size = 64;

        /// <summary>The fraction of alpha kept at the tip and root of an arm (the floor of
        /// the fade along u).</summary>
        private const float LengthFloor = 0.55f;

        /// <summary>Fine variation along u. **A perfectly regular band does not read as a
        /// cloud** (the same reason as <see cref="SpiralMesh"/>'s WobbleAmplitude). Keep the
        /// amplitude small.</summary>
        private const float RippleAmplitude = 0.12f;

        private const float RippleFrequency = 13f;

        /// <summary>
        /// Fills in one texture's worth of alpha. **The caller allocates** (we never
        /// <c>new</c>). If the array is too short we do nothing — writing part of the way
        /// through would give the hardest form of all to investigate, "only one edge is
        /// hard" (the same judgement as <see cref="SpiralMesh.Build"/>).
        ///
        /// The layout is row-major: row <c>y</c> is v = <c>y / (Size - 1)</c>, and
        /// column <c>x</c> is u = <c>x / (Size - 1)</c>.
        /// </summary>
        public static void Build(byte[] alpha)
        {
            if (alpha == null || alpha.Length < Size * Size) return;

            for (int y = 0; y < Size; y++)
            {
                float v = (float)y / (Size - 1);
                // 0 at both edges, 1 in the middle. sin goes to 0 at the ends and its
                // derivative is near 0 there too, so the edge does not look "cut off".
                float across = (float)Math.Sin(Math.PI * v);
                across = across * across;   // softens the edge further (the middle stays 1)

                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / (Size - 1);

                    float along = LengthFloor
                                  + (1f - LengthFloor) * (float)Math.Sin(Math.PI * u);
                    float ripple = 1f - RippleAmplitude
                                        * (0.5f - 0.5f * (float)Math.Cos(u * RippleFrequency));

                    float a = across * along * ripple;
                    if (a < 0f) a = 0f;
                    if (a > 1f) a = 1f;

                    alpha[y * Size + x] = (byte)(a * 255f + 0.5f);
                }
            }
        }

        /// <summary>
        /// The maximum alpha in this texture (0-1). Tests pin down that it **is 1.0**.
        /// That is the promise itself: the material tint's α is the upper limit on the
        /// cloud's density, and the texture only fades the edges.
        /// </summary>
        public static float PeakAlpha
        {
            get { return 1f; }
        }
    }
}
