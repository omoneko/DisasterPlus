using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The geometry behind "does this road touch the circle of effect". **Engine-free.**
    ///
    /// ── Why the midpoint alone is not enough (whole-mod review I4) ─────────────────
    ///
    /// ⑤ originally tested a road using only the single point
    /// <c>NetSegment.m_middlePosition</c>. The result was that **a main road whose midpoint
    /// lay outside the circle while the road itself ran in towards the centre was never
    /// removed.** A road that is not removed keeps <c>m_flags &amp; 3 == 1</c>, so
    /// <c>NetSegment.TerrainUpdated</c> goes on applying <c>Heights.PrimaryLevel</c> and
    /// **a flat trench is left through the mountain along that road's y** (IL findings doc
    /// §A-2 / §G-16 (c)). A front part way through the uplift gets picked up again by a
    /// later sweep, but **the outermost band is never swept a second time** — precisely the
    /// failure design doc §1.2 uncovered.
    ///
    /// ── Three representative points, tested as polyline-to-circle distance ───────────
    ///
    /// There are three positions available for a single road (measured from IL in §F-15):
    /// the positions of the two end nodes (<c>m_startNode</c> / <c>m_endNode</c>'s
    /// <c>m_position</c>) and <c>m_middlePosition</c> (the average of the midpoints of the
    /// two Béziers). If the distance from the circle's centre to the polyline through those
    /// three points is within the radius, the road is in range.
    ///
    /// **Looking only at the three points as points is not enough** (a long straight road
    /// that merely runs through the circle can have all three points outside it). So we
    /// measure the distance to the polyline's **segments**. Conversely, testing against
    /// <c>m_bounds</c> (the AABB) over-hits by the corners on any road running diagonally —
    /// and since **the side that destroys and the side that counts use the same predicate**,
    /// we want to avoid "counted but not destroyed" and "destroyed but not counted" equally.
    ///
    /// &gt; **This is still an approximation.** A Bézier can bulge out past the polyline, and
    /// &gt; it cannot express the case where only part of one long road is in range. That is
    /// &gt; one of the reasons ⑤ only ever reports the count as "approximate", which is how
    /// &gt; design doc §7.2 labels it.
    ///
    /// Engine-free (no <c>UnityEngine</c>, no LINQ, no <c>System.Random</c>).
    /// </summary>
    public static class FootprintReach
    {
        /// <summary>
        /// Does the polyline through the three points <paramref name="a"/> →
        /// <paramref name="mid"/> → <paramref name="b"/> touch the circle at
        /// <paramref name="centre"/> with radius <paramref name="radiusMetres"/>?
        ///
        /// False if the radius is NaN or 0 or less (nothing can touch a range that is not
        /// there). **Legs containing a NaN point are ignored** — judging on the remaining
        /// legs is the safer side, compared to dropping a whole road because one point
        /// could not be read. If all three points are NaN, false.
        /// </summary>
        public static bool CircleTouchesPolyline(Vec2 centre, float radiusMetres,
                                                 Vec2 a, Vec2 mid, Vec2 b)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return false;

            float radiusSquared = radiusMetres * radiusMetres;

            bool aOk = IsFinite(a);
            bool midOk = IsFinite(mid);
            bool bOk = IsFinite(b);

            if (aOk && midOk
                && DistanceSquaredToSegment(centre, a, mid) <= radiusSquared) return true;

            if (midOk && bOk
                && DistanceSquaredToSegment(centre, mid, b) <= radiusSquared) return true;

            // When only the midpoint could not be read, join the two ends directly (so as
            // not to be left with no legs at all).
            if (!midOk && aOk && bOk
                && DistanceSquaredToSegment(centre, a, b) <= radiusSquared) return true;

            // Fall back to whichever single point is left (when the other two are NaN).
            if (aOk && !midOk && !bOk) return centre.DistanceSquaredTo(a) <= radiusSquared;
            if (midOk && !aOk && !bOk) return centre.DistanceSquaredTo(mid) <= radiusSquared;
            if (bOk && !aOk && !midOk) return centre.DistanceSquaredTo(b) <= radiusSquared;

            return false;
        }

        /// <summary>
        /// The squared distance between the point <paramref name="p"/> and the segment
        /// <paramref name="a"/>–<paramref name="b"/>. **No square root is taken** (the
        /// comparison stays in squared space).
        /// If the segment is degenerate (the two points are the same) this becomes the
        /// distance to that endpoint.
        /// </summary>
        public static float DistanceSquaredToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            float abx = b.X - a.X;
            float abz = b.Z - a.Z;
            float apx = p.X - a.X;
            float apz = p.Z - a.Z;

            float lengthSquared = abx * abx + abz * abz;
            if (!(lengthSquared > 0f)) return apx * apx + apz * apz;

            float t = (apx * abx + apz * abz) / lengthSquared;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float dx = apx - abx * t;
            float dz = apz - abz * t;
            return dx * dx + dz * dz;
        }

        private static bool IsFinite(Vec2 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Z)
                   && !float.IsInfinity(v.X) && !float.IsInfinity(v.Z);
        }
    }
}
