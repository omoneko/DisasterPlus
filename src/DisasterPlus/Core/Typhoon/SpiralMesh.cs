using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// Generates **only the vertices** of the typhoon's spiral cloud. Pure, engine-free data;
    /// assembling the <c>Mesh</c> is done by <c>Game/Typhoon/TyphoonCloud</c> (design
    /// document §5).
    ///
    /// ── Why we build it ourselves ──────────────────────────────────
    ///
    /// There is **no** vanilla cloud we can borrow. <c>DisasterInfo.m_effect</c> does not
    /// exist as a field at all; the only <c>EffectInfo</c>s around disasters are
    /// <c>DisasterProperties.m_mediumExplosion</c> and <c>MeteorAI.m_impactEffect</c>, and
    /// neither clouds nor vortices are prefabbed (§C-1 of the IL facts document). On top of
    /// that, vanilla's clouds are a noise shader painted on a 6,400 km sky dome and **have no
    /// world coordinates**, so "put a cloud vortex at the typhoon's position" is impossible
    /// in principle (§C-2). So ④ builds its own mesh and draws it itself.
    ///
    /// The model to follow is <c>VortexAI.GenerateMesh()</c> (16,250 vertices, a 2,000 m tall
    /// funnel generated procedurally from the fixed seed <c>Randomizer(2975689)</c>; §C-1).
    /// ④'s cloud only needs a single sheet up in the sky and does not need a funnel's
    /// density, so it is kept to 2,304 vertices — **an order of magnitude smaller than the
    /// tornado's**.
    ///
    /// ── The shape ────────────────────────────────────────────
    ///
    /// On an annulus with a hole for the eye at its centre (<c>innerRadius</c>), we stack
    /// <see cref="Arms"/> spiral ribbons in <see cref="Rings"/> layers. One ribbon has
    /// <see cref="SegmentsPerArm"/> cross-sections, and each cross-section emits two vertices,
    /// inner and outer.
    ///
    /// **The ribbon's width is built from "the distance from the centre", never from an
    /// offset along the normal.** With a normal offset, the vertices bite into the eye hole
    /// at the spiral's inner end and overshoot the rim at its outer end. Built from the
    /// distance, clamping both ends to <c>[inner, outer]</c> is enough to guarantee "the eye
    /// is always open and the rim is never crossed" (tests pin down both).
    ///
    /// ── Triangles are laid on both faces ──────────────────────────────
    ///
    /// We lay two sets of triangles with opposite windings on the same quad to make it
    /// **double-sided**. It costs nothing but doubling the indices, with no extra vertices.
    /// It is insurance — "the cloud is invisible from directly above" can be caused by
    /// nothing more than the winding, it is a failure you cannot spot until it is on real
    /// hardware, and on top of that a <c>Shader.Find("Standard")</c> material does not draw
    /// back faces.
    ///
    /// The only randomness used is <see cref="DeterministicRandom"/> (never
    /// <c>VanillaRandomizer</c> — what is decided here is not a value vanilla draws but a
    /// shape ④ invented).
    /// **The same arguments always give the same shape** (reloading the city does not change
    /// the cloud's shape).
    /// </summary>
    public static class SpiralMesh
    {
        /// <summary>The number of spiral arms.</summary>
        public const int Arms = 4;

        /// <summary>The number of cross-sections per arm.</summary>
        public const int SegmentsPerArm = 96;

        /// <summary>The number of layers stacked (in the height direction).</summary>
        public const int Rings = 3;

        /// <summary>The angle one arm sweeps around the centre (rad). 1.6 turns.</summary>
        private const float TurnRadians = 10.053096f;

        /// <summary>
        /// The ribbon's half-width ÷ (outer radius − inner radius).
        ///
        /// **It must be a value that keeps the gaps between arms open.** The radial spacing
        /// between successive passes of neighbouring arms is
        /// <c>span / (turns × <see cref="Arms"/>)</c> = about span/6.4.
        /// Once the ribbon's full width (= twice this value) catches up with that, the spiral
        /// looks like a disc with no gaps at all (measured by rendering offline, which is why
        /// it came down from 0.07).
        /// </summary>
        private const float BandHalfWidth = 0.04f;

        /// <summary>
        /// How the ribbon tapers at its ends. Without tapering both ends, the inner and outer
        /// ends get **cut off flat** by the <c>[inner, outer]</c> clamp and the spiral's tips
        /// become square stubs (confirmed by rendering offline).
        /// </summary>
        private const float TaperFloor = 0.15f;

        /// <summary>
        /// The amplitude (as a fraction of span) and frequency of the arms' waviness.
        /// **A perfectly evenly spaced spiral looks less like a typhoon than like a
        /// hypnotist's disc** (confirmed by rendering offline). We add one low-frequency
        /// wave per arm, each at a different phase, to break up the regularity.
        /// </summary>
        private const float WobbleAmplitude = 0.035f;

        private const float WobbleFrequency = 9f;

        /// <summary>The per-layer height jitter (as a fraction of a layer's
        /// thickness).</summary>
        private const float HeightJitter = 0.12f;

        /// <summary>
        /// The maximum per-layer phase shift (rad). Keep it well below the angular spacing of
        /// the arms (2π/<see cref="Arms"/> ≒ 1.57) — make it large and the three layers fill
        /// in each other's gaps and the spiral disappears.
        /// </summary>
        private const float RingPhaseSpread = 0.12f;

        private const float TwoPi = 6.2831855f;

        /// <summary>
        /// The vertex count. Arms × layers × cross-sections × 2 (inner and outer).
        /// **An order of magnitude below the tornado's 16,250** (see the class doc).
        /// </summary>
        public static int VertexCount
        {
            get { return Arms * Rings * SegmentsPerArm * 2; }
        }

        /// <summary>
        /// The triangle index count. Two sets per quad, front and back = 12 indices (see the
        /// class doc).
        /// </summary>
        public static int TriangleIndexCount
        {
            get { return Arms * Rings * (SegmentsPerArm - 1) * 12; }
        }

        /// <summary>
        /// Fills the arrays. **The caller allocates** (this method never calls <c>new</c>).
        /// If an array is too short we do nothing — writing part of the way through would
        /// give the hardest form of all to investigate, "the vertices are there but the faces
        /// are broken".
        /// </summary>
        /// <param name="innerRadius">The eye hole's radius. No vertex is placed inside
        /// it.</param>
        /// <param name="outerRadius">The rim. No vertex is placed outside it.</param>
        /// <param name="height">The layers' total thickness. Y stays within
        /// <c>[0, height]</c>.</param>
        /// <param name="vertices">At least <see cref="VertexCount"/> long.</param>
        /// <param name="uvs">At least <see cref="VertexCount"/> × 2 long (u and v
        /// interleaved).</param>
        /// <param name="triangles">At least <see cref="TriangleIndexCount"/> long.</param>
        public static void Build(float innerRadius, float outerRadius, float height,
                                 Vec3[] vertices, float[] uvs, int[] triangles)
        {
            if (vertices == null || uvs == null || triangles == null) return;
            if (vertices.Length < VertexCount) return;
            if (uvs.Length < VertexCount * 2) return;
            if (triangles.Length < TriangleIndexCount) return;

            float inner = Sane(innerRadius, 0f);
            float outer = Sane(outerRadius, 0f);
            if (!(outer > inner)) outer = inner + 1f;
            float tall = Sane(height, 0f);

            float span = outer - inner;
            float halfWidth = span * BandHalfWidth;
            float layer = Rings > 0 ? 1f / Rings : 1f;

            int v = 0;
            int uv = 0;
            int tri = 0;

            for (int a = 0; a < Arms; a++)
            {
                float armBase = TwoPi * a / Arms;
                // The wave's phase for this arm. Deterministic, so reloading the city does
                // not change the shape.
                float wobblePhase = DeterministicRandom.Unit((uint)(a + 1), 0x574F4243u) * TwoPi;

                for (int r = 0; r < Rings; r++)
                {
                    // Shift the phase a little per layer. If the three layers line up
                    // exactly when viewed from directly above, you cannot see that there
                    // are layers at all.
                    float ringPhase = DeterministicRandom.Unit((uint)a, (uint)(r + 1))
                                      * RingPhaseSpread;
                    float ringHeight = (r + 0.5f) * layer;

                    for (int s = 0; s < SegmentsPerArm; s++)
                    {
                        float t = SegmentsPerArm > 1 ? (float)s / (SegmentsPerArm - 1) : 0f;
                        float angle = armBase + ringPhase + t * TurnRadians;
                        float cos = (float)Math.Cos(angle);
                        float sin = (float)Math.Sin(angle);

                        // ★ The width is built from "the distance from the centre" and both
                        //   ends are clamped (see the class doc). The ends taper
                        //   (TaperFloor); without the taper the clamp cuts the spiral's tips
                        //   off flat and leaves square stubs.
                        float taper = TaperFloor
                                      + (1f - TaperFloor) * (float)Math.Sin(Math.PI * t);
                        // The waviness also dies away at the ends (same reason as the taper:
                        // do not press it against the rim).
                        float wobble = span * WobbleAmplitude * taper
                                       * (float)Math.Sin(t * WobbleFrequency + wobblePhase);
                        float centreRadius = inner + span * t + wobble;
                        float lo = centreRadius - halfWidth * taper;
                        float hi = centreRadius + halfWidth * taper;
                        if (lo < inner) lo = inner;
                        if (hi > outer) hi = outer;

                        float jitter = (DeterministicRandom.Unit((uint)(s + 1),
                                            (uint)(a * Rings + r + 1)) - 0.5f)
                                       * HeightJitter * layer;
                        float unitY = ringHeight + jitter;
                        if (unitY < 0f) unitY = 0f;
                        if (unitY > 1f) unitY = 1f;
                        float y = unitY * tall;

                        vertices[v] = new Vec3(lo * cos, y, lo * sin);
                        vertices[v + 1] = new Vec3(hi * cos, y, hi * sin);

                        // u is progress along the spiral, v is 0 on the inside and 1 on the
                        // outside. Both are in [0,1].
                        uvs[uv] = t;
                        uvs[uv + 1] = 0f;
                        uvs[uv + 2] = t;
                        uvs[uv + 3] = 1f;

                        v += 2;
                        uv += 4;
                    }

                    // Lay the quads for this arm and this layer. The vertices are already
                    // placed, so the index base is "the start of the 2*SegmentsPerArm we
                    // just wrote".
                    int start = v - SegmentsPerArm * 2;
                    for (int s = 0; s < SegmentsPerArm - 1; s++)
                    {
                        int i0 = start + s * 2;          // inner, s
                        int i1 = i0 + 1;                 // outer, s
                        int i2 = i0 + 2;                 // inner, s+1
                        int i3 = i0 + 3;                 // outer, s+1

                        triangles[tri] = i0;
                        triangles[tri + 1] = i2;
                        triangles[tri + 2] = i1;
                        triangles[tri + 3] = i1;
                        triangles[tri + 4] = i2;
                        triangles[tri + 5] = i3;

                        // ★ The back face (the insurance from the class doc). Only the
                        //    winding is reversed.
                        triangles[tri + 6] = i0;
                        triangles[tri + 7] = i1;
                        triangles[tri + 8] = i2;
                        triangles[tri + 9] = i1;
                        triangles[tri + 10] = i3;
                        triangles[tri + 11] = i2;

                        tri += 12;
                    }
                }
            }
        }

        /// <summary>Drops NaN, infinity and negative values. We do not create "a cloud vertex
        /// that is NaN".</summary>
        private static float Sane(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return fallback;
            return value;
        }
    }
}
