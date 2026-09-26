using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The **one and only place** where ⑤ decides "which buildings and roads are inside the
    /// affected range". Both the counting side (<see cref="VolcanoSurvey"/>) and the destroying
    /// side (<see cref="VolcanoClearing"/>) go through here.
    /// **Sim thread only** (it reads the game's buffers).
    ///
    /// ── why a single type was created (whole-project review I2 / I4) ───────────────────────
    ///
    /// The same rules used to be copied into two files, with <c>VolcanoClearing</c>'s doc
    /// guaranteeing **by a note** that "<c>SegmentGridMargin</c> must be the same value as in
    /// <c>VolcanoSurvey</c>, or the number counted and the number destroyed will drift apart".
    /// The review found, right next to it, that **the masks had actually drifted** —
    /// the survey side alone was excluding <c>Untouchable</c> and <c>Collapsed</c>, so
    /// **immediately before an irreversible operation it was showing fewer things about to be
    /// destroyed than there really were**.
    ///
    /// > The note did not prevent the same mistake a second time. **So the constants are shared.**
    /// > If you ever want to change any one of the mask, the margin or the hit test between the
    /// > survey and the clearing, here is the only place to change it, and changing it necessarily
    /// > affects both.
    ///
    /// ── the mask (IL facts doc §G-16 (c)(d)) ───────────────────────────────────────────────
    ///
    /// What ⑤ has to remove is "<b>anything that keeps pinning the terrain to its own height</b>".
    /// <c>Building.TerrainUpdated</c> only looks at <c>m_flags &amp; 524291</c>
    /// (<c>Created|Deleted|Demolishing</c>), and <c>NetSegment.TerrainUpdated</c> only at
    /// <c>m_flags &amp; 3</c>. Therefore:
    ///
    ///   - <c>Untouchable</c> buildings and roads **also pin**, so they are candidates
    ///   - <c>Collapsed</c> rubble and already-collapsed roads **also pin**, so they are
    ///     candidates. On top of that, <c>demolish: true</c> already works on <c>Collapsed</c>
    ///     buildings: it sets <c>Demolishing</c> and <b>returns true</b> (IL_0210 in §G-16 (d);
    ///     re-disassembled and confirmed in this task)
    ///   - Only <c>Demolishing</c> is excluded — there the terrain pinning has already stopped
    ///     (<c>NetSegment.Flags</c> has no <c>Demolishing</c>. §F-15)
    ///
    /// ── the road hit test (§F-15 / <see cref="FootprintReach"/>) ──────────────────────────
    ///
    /// The position that decides the cell is **the midpoint of the two end nodes**, while the
    /// field that holds a position is <c>m_middlePosition</c> (the mean of the beziers'
    /// midpoints), and **the two are not the same**.
    /// So the rectangle is widened by <see cref="SegmentGridMargin"/> cells, and the distance is
    /// measured along <b>the polyline end node → midpoint → end node</b>.
    /// Measure at the single midpoint and arterial roads running in towards the centre are left in
    /// place, so **a flat trench is left inside the finished mountain** (that class's doc).
    /// </summary>
    internal static class VolcanoScan
    {
        /// <summary>
        /// The number of cells by which the road rectangle is widened. The position that decides
        /// the cell (the midpoint of the two end nodes) and the extent the polyline reaches are
        /// not the same (class doc / §F-15).
        ///
        /// **Even this can miss some**: a road sticking more than 128 m outside the rectangle has
        /// its cell outside the rectangle even when its polyline touches the circle. Two cells is
        /// ample against the length of an actual road segment (the game's limit is on the order of
        /// a few hundred metres), but it is worth stating that this is not "always all of them".
        /// </summary>
        internal const int SegmentGridMargin = 2;

        /// <summary>
        /// The candidate condition for buildings. **Including** <c>Untouchable</c> and
        /// <c>Collapsed</c> is the call specific to ⑤; the reason is in the class doc.
        /// </summary>
        internal const Building.Flags BuildingCandidateMask =
            Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Demolishing;

        /// <summary>
        /// The candidate condition for roads. <c>NetSegment.Flags</c> has no <c>Demolishing</c>
        /// (§F-15), so all that is checked is <c>Created</c> and not <c>Deleted</c>.
        /// </summary>
        internal const NetSegment.Flags SegmentCandidateMask =
            NetSegment.Flags.Created | NetSegment.Flags.Deleted;

        /// <summary>Whether this building is ⑤'s business (<see cref="BuildingCandidateMask"/>).</summary>
        internal static bool IsCandidate(Building.Flags flags)
        {
            return (flags & BuildingCandidateMask) == Building.Flags.Created;
        }

        /// <summary>Whether this road is ⑤'s business (<see cref="SegmentCandidateMask"/>).</summary>
        internal static bool IsCandidate(NetSegment.Flags flags)
        {
            return (flags & SegmentCandidateMask) == NetSegment.Flags.Created;
        }

        /// <summary>
        /// Whether a building is inside the affected range. The distance is measured from
        /// <c>m_position</c> — a large building whose corner is inside the range is not included
        /// if its centre is outside, but
        /// **that is an approximation, and saying so is better than lying that "everything gets
        /// destroyed"** (design doc §7.2).
        /// </summary>
        internal static bool BuildingInside(Vector3 position, Vec2 origin, float radiusSquared)
        {
            return origin.DistanceSquaredTo(new Vec2(position.x, position.z)) <= radiusSquared;
        }

        /// <summary>
        /// Whether a road is inside the affected range (the polyline test in the class doc).
        ///
        /// If <paramref name="nodes"/> is null (the node buffer cannot be read), the test uses the
        /// single point <c>m_middlePosition</c>. **That can miss roads running in towards the
        /// centre**, but since the counting side and the destroying side go through the same
        /// function, the two still give the same answer.
        /// </summary>
        internal static bool SegmentInside(NetSegment[] segments, NetNode[] nodes, ushort id,
                                           Vec2 origin, float radiusMetres)
        {
            Vector3 middle = segments[id].m_middlePosition;
            var mid = new Vec2(middle.x, middle.z);

            Vec2 start = mid;
            Vec2 end = mid;

            if (nodes != null)
            {
                ushort startNode = segments[id].m_startNode;
                ushort endNode = segments[id].m_endNode;

                if (startNode != 0 && startNode < nodes.Length)
                {
                    Vector3 p = nodes[startNode].m_position;
                    start = new Vec2(p.x, p.z);
                }

                if (endNode != 0 && endNode < nodes.Length)
                {
                    Vector3 p = nodes[endNode].m_position;
                    end = new Vec2(p.x, p.z);
                }
            }

            return FootprintReach.CircleTouchesPolyline(origin, radiusMetres, start, mid, end);
        }

        /// <summary>
        /// The road node buffer. null if it cannot be read (<see cref="SegmentInside"/> then falls
        /// back to the midpoint-only test). **It never throws.**
        /// </summary>
        internal static NetNode[] NodeBuffer(NetManager nm)
        {
            if (nm == null) return null;
            return nm.m_nodes != null ? nm.m_nodes.m_buffer : null;
        }
    }
}
