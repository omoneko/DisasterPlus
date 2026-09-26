using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **Pure data** producing the vertices of a lava ribbon. Treated exactly like ④'s
    /// <c>SpiralMesh</c>: it knows nothing whatsoever about <c>UnityEngine</c>.
    ///
    /// ── Why we build it from scratch ─────────────────────────────────
    ///
    /// There is **not a single** prefab, material or shader for lava, magma or molten
    /// anything **in the DLL's string heap** (§B-5: scanning for <c>lava</c> / <c>magma</c> /
    /// <c>molten</c> in both UTF-16 and ASCII gave zero hits). There is no off-the-shelf
    /// thing to borrow, so ⑤ builds both the shape and the surface itself.
    ///
    /// ── ⑤ does not hold the height ────────────────────────────────
    ///
    /// <paramref name="heightOffset"/> is only "how far above the ground to float".
    /// **The ground height is looked up by the Game side with <c>SampleDetailHeight</c> and
    /// added to each vertex.** We do not bring terrain into Core.
    ///
    /// ── A polyline is one flow's worth ──────────────────────────────
    ///
    /// Lava runs in several streams, but this type handles **one polyline** only.
    /// The caller calls it once per flow and packs the resulting vertices into a single mesh
    /// (hand it points from separate flows as one polyline and you get a ribbon flying back
    /// and forth between them).
    /// </summary>
    public static class LavaRibbon
    {
        /// <summary>The cap on points in one polyline. **We never let the arrays grow
        /// without limit.**</summary>
        public const int MaxPoints = 256;

        /// <summary>The ribbon's minimum width (m). A zero-width ribbon has no area and
        /// nothing gets drawn.</summary>
        public const float MinWidthMetres = 4f;

        /// <summary>The vertex count for a given point count (2 per point, left and
        /// right).</summary>
        public static int VertexCountFor(int pointCount)
        {
            if (pointCount < 2) return 0;
            return pointCount * 2;
        }

        /// <summary>The triangle index count for a given point count (2 triangles = 6
        /// indices per segment).</summary>
        public static int TriangleIndexCountFor(int pointCount)
        {
            if (pointCount < 2) return 0;
            return (pointCount - 1) * 6;
        }

        /// <summary>
        /// Builds the ribbon from a polyline and its widths. With fewer than 2 points it
        /// **builds nothing and returns false** (hand Unity a degenerate mesh and the
        /// rendering breaks quietly, with no exception).
        ///
        /// If <paramref name="count"/> exceeds <see cref="MaxPoints"/> it is cut to
        /// <see cref="MaxPoints"/>.
        ///
        /// The direction of travel is the difference between the neighbouring points (one
        /// side only at the ends). **If the difference is 0 we carry over the last valid
        /// direction** (or <c>(1, 0)</c> if there is none) — when lava stops, the same point
        /// repeats, and dividing by zero here would produce <c>NaN</c> vertices.
        /// </summary>
        public static bool Build(Vec2[] points, int count, float[] widths, float heightOffset,
                                 out Vec3[] vertices, out float[] uv, out int[] triangles)
        {
            vertices = null;
            uv = null;
            triangles = null;

            if (points == null || widths == null) return false;
            if (count < 2) return false;

            int n = count;
            if (n > MaxPoints) n = MaxPoints;
            if (n > points.Length) n = points.Length;
            if (n > widths.Length) n = widths.Length;
            if (n < 2) return false;

            vertices = new Vec3[VertexCountFor(n)];
            uv = new float[VertexCountFor(n) * 2];
            triangles = new int[TriangleIndexCountFor(n)];

            float lastDirX = 1f;
            float lastDirZ = 0f;

            for (int i = 0; i < n; i++)
            {
                int prev = i > 0 ? i - 1 : 0;
                int next = i < n - 1 ? i + 1 : n - 1;

                float dx = points[next].X - points[prev].X;
                float dz = points[next].Z - points[prev].Z;
                float length = (float)Math.Sqrt(dx * dx + dz * dz);

                if (length > 1e-4f && !IsBad(length) && !IsBad(dx) && !IsBad(dz))
                {
                    lastDirX = dx / length;
                    lastDirZ = dz / length;
                }

                // The unit vector at 90 degrees to the direction of travel.
                float normalX = -lastDirZ;
                float normalZ = lastDirX;

                float half = widths[i];
                if (IsBad(half) || half < MinWidthMetres) half = MinWidthMetres;
                half *= 0.5f;

                float px = points[i].X;
                float pz = points[i].Z;
                if (IsBad(px)) px = 0f;
                if (IsBad(pz)) pz = 0f;

                int left = i * 2;
                int right = left + 1;

                vertices[left] = new Vec3(px + normalX * half, heightOffset,
                                          pz + normalZ * half);
                vertices[right] = new Vec3(px - normalX * half, heightOffset,
                                           pz - normalZ * half);

                float v = n > 1 ? (float)i / (n - 1) : 0f;
                uv[left * 2] = 0f;
                uv[left * 2 + 1] = v;
                uv[right * 2] = 1f;
                uv[right * 2 + 1] = v;
            }

            for (int i = 0; i < n - 1; i++)
            {
                int t = i * 6;
                int a = i * 2;

                triangles[t] = a;
                triangles[t + 1] = a + 2;
                triangles[t + 2] = a + 1;

                triangles[t + 3] = a + 1;
                triangles[t + 4] = a + 2;
                triangles[t + 5] = a + 3;
            }

            return true;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
