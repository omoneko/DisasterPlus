using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The "result of surveying" one volcano. **An immutable value type** built on the sim thread,
    /// carried in the snapshot and read on the main thread.
    ///
    /// Only <see cref="SegmentCount"/> uses **-1 to mean "could not be counted"**.
    /// **Do not mix that with 0** — "there is not one road in range" and "there is no path in this
    /// environment for counting them" are entirely different facts, and in the latter case
    /// **the roads are still destroyed**.
    /// </summary>
    public struct VolcanoFootprint
    {
        /// <summary>Whether the survey succeeded. When false, do not read the other fields.</summary>
        public readonly bool Valid;

        /// <summary>The world coordinates clicked. <c>Y</c> is the terrain height (m).</summary>
        public readonly Vec3 Centre;

        /// <summary>The terrain height at the placement point (m). **A value merely read out of the game's arrays.**</summary>
        public readonly float GroundHeightMetres;

        public readonly VolcanoForm Form;

        /// <summary>The radius already clamped into the form's band (m).</summary>
        public readonly float RadiusMetres;

        /// <summary>The final height already cut down to allow for the ceiling (§C-10) (m).</summary>
        public readonly float HeightMetres;

        /// <summary>Whether the ceiling makes the mountain lower than requested. The flag that stops us **silently building a low mountain**.</summary>
        public readonly bool HeightLimitedByCeiling;

        /// <summary>The real number of buildings in range at survey time (before any rounding).</summary>
        public readonly int BuildingCount;

        /// <summary>
        /// The real number of road segments in range at survey time (before any rounding).
        /// **-1 means "could not be counted"** (class doc).
        /// </summary>
        public readonly int SegmentCount;

        /// <summary>The number of tiles the uplift will end up passing to <c>UpdateArea</c> (for diagnostics).</summary>
        public readonly int TileCount;

        /// <summary>Sim frames until the buildable ground and the water level catch up (§A-2 / §A-4).</summary>
        public readonly int BlockHeightCatchUpFrames;

        /// <summary>
        /// Whether the sweep was cut short by the one-tick limit. When true,
        /// <see cref="BuildingCount"/> / <see cref="SegmentCount"/> are **lower bounds**.
        /// </summary>
        public readonly bool Capped;

        public VolcanoFootprint(bool valid, Vec3 centre, float groundHeightMetres,
                                VolcanoForm form, float radiusMetres, float heightMetres,
                                bool heightLimitedByCeiling, int buildingCount, int segmentCount,
                                int tileCount, int blockHeightCatchUpFrames, bool capped)
        {
            Valid = valid;
            Centre = centre;
            GroundHeightMetres = groundHeightMetres;
            Form = form;
            RadiusMetres = radiusMetres;
            HeightMetres = heightMetres;
            HeightLimitedByCeiling = heightLimitedByCeiling;
            BuildingCount = buildingCount;
            SegmentCount = segmentCount;
            TileCount = tileCount;
            BlockHeightCatchUpFrames = blockHeightCatchUpFrames;
            Capped = capped;
        }

        /// <summary>
        /// Build one with the same location and the same survey result but **the radius and height
        /// swapped out**.
        ///
        /// Used by the super-eruption's stages (inflation / caldera). Those move a wider range
        /// than the mountain, so both <c>VolcanoUplift</c> and <c>VolcanoClearing</c> have to be
        /// <b>shown that stage's radius as it is</b> — widen it for only one of them and the
        /// ground under the roads alone gets pushed back and a flat trench is left
        /// (design doc §1.2 / trap 1).
        ///
        /// ★ The building and road counts (<see cref="BuildingCount"/> /
        ///   <see cref="SegmentCount"/>) are **left as the original survey's**. The widened range
        ///   has not been re-counted — so as not to quote a number we never counted, they are not
        ///   touched here.
        /// </summary>
        public VolcanoFootprint Resized(float radiusMetres, float heightMetres)
        {
            return new VolcanoFootprint(Valid, Centre, GroundHeightMetres, Form,
                                        radiusMetres, heightMetres, HeightLimitedByCeiling,
                                        BuildingCount, SegmentCount, TileCount,
                                        BlockHeightCatchUpFrames, Capped);
        }

        /// <summary>The "not surveyed" one. **Do not fabricate "plausible" values out of a row of zeroes.**</summary>
        public static VolcanoFootprint None
        {
            get
            {
                return new VolcanoFootprint(false, new Vec3(0f, 0f, 0f), 0f,
                                            VolcanoForm.Strato, 0f, 0f, false,
                                            0, -1, 0, 0, false);
            }
        }
    }

    /// <summary>
    /// The sweep that **only counts** the affected range. **Sim thread only.**
    ///
    /// ★★ <b>This type destroys nothing.</b> It touches not one building, road or terrain cell.
    /// Destroying is T5 (<c>VolcanoClearing</c>) and raising is T6 (<c>VolcanoUplift</c>).
    /// Review grep: **not one** building or road destruction API, or terrain write API, appears in
    /// this file (plan T4 Step 8).
    ///
    /// ── why it cannot be counted from the main thread (plan §4.1) ──────────────────────────
    ///
    /// <c>BuildingManager.m_buildingGrid</c> and <c>NetManager.m_segmentGrid</c> are
    /// **owned by the sim thread**. The placement tool's click handler is on the main thread, so
    /// counting from there produces an <c>IndexOutOfRangeException</c> with no stack trace,
    /// **later, inside vanilla's own code**. That is why ⑤'s click → confirm makes
    /// <b>two round trips</b> (the class doc of <see cref="VolcanoState"/>).
    ///
    /// ── the grid dimensions (measured in IL in this task; not guessed) ─────────────────────
    ///
    /// The building side uses the values ② and ④ already use (64 m cells, offset 135,
    /// <c>[0,269]</c>, <c>GridSide = 270</c>). **The road side is a first for this mod, so it was
    /// settled in IL**:
    ///
    /// <code>
    /// NetManager.Awake         : m_segmentGrid = new ushort[72900]     (= 270 * 270)
    /// NetManager.InitializeSegment (IL_0046-IL_00B6):
    ///     pos  = (nodes[m_startNode].m_position + nodes[m_endNode].m_position) * 0.5
    ///     x    = Mathf.Clamp((int)(pos.x / 64f + 135f), 0, 269)
    ///     z    = Mathf.Clamp((int)(pos.z / 64f + 135f), 0, 269)
    ///     idx  = z * 270 + x
    ///     segments[id].m_nextGridSegment = m_segmentGrid[idx];  m_segmentGrid[idx] = id
    /// NetSegment.m_nextGridSegment : UInt16 (public instance)
    /// NetSegment.m_flags           : NetSegment.Flags (Created=1 / Deleted=2 /
    ///                                Collapsed=8 / Untouchable=0x20)
    /// NetSegment.m_middlePosition  : Vector3 (NetSegment.UpdateBounds writes it as the mean of
    ///                                the midpoints of the two beziers)
    /// NetManager.Awake             : m_segments = new Array16&lt;NetSegment&gt;(36864)
    /// </code>
    ///
    /// **The position that puts it into a cell (the midpoint of the two end nodes) and the extent
    /// the road actually spans are not the same.** So the rectangle is widened by
    /// <c>VolcanoScan.SegmentGridMargin</c> cells before sweeping, and the distance is measured
    /// along **the polyline end node → midpoint → end node**.
    ///
    /// ★★ <b>This file holds not one mask, margin or hit test.</b>
    /// They are all in <see cref="VolcanoScan"/>, and **the clearing stage
    /// (<see cref="VolcanoClearing"/>) uses the same ones**. Whole-project review I2 found that
    /// because this file held its own masks, it missed <c>Untouchable</c> and <c>Collapsed</c> in
    /// the count and so **showed fewer things about to be destroyed than there really were,
    /// immediately before an irreversible operation**.
    ///
    /// **Even so, it cannot correctly represent a case where only part of one long road falls
    /// inside the range.** So this is an approximation, and
    /// <see cref="VolcanoConfirmRows"/> says so (design doc §7.2).
    /// </summary>
    public static class VolcanoSurvey
    {
        /// <summary>Cells along one side of the building/road grid (one cell is 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>Grid cell size (m).</summary>
        private const float GridCellSize = 64f;

        /// <summary>Offset from world coordinates to cell index.</summary>
        private const float GridCellOffset = 135f;

        /// <summary>Maximum grid cells examined in one sweep (the same as ④'s <c>TyphoonWind</c>).</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>Maximum buildings/roads counted in one sweep (the same as ④'s <c>TyphoonWind</c>).</summary>
        private const int MaxItemsPerPass = 2048;

        /// <summary>Guard on how many times the building linked list is walked (the size of the building buffer).</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>Guard on how many times the road linked list is walked (<c>Array16&lt;NetSegment&gt;(36864)</c>).</summary>
        private const int SegmentChainGuard = 36864;

        // ★★ **Neither the masks nor the margins nor the hit test belong here** (class doc).
        //    The single set in <see cref="VolcanoScan"/> is shared with the clearing stage
        //    (VolcanoClearing).
        //    Back when ②'s and ④'s CandidateMask was copied here and excluded Collapsed /
        //    Untouchable, the survey showed "the number about to be destroyed" as smaller than it
        //    really was (whole-project review I2).

        private static string _lastFailure;

        /// <summary>
        /// Why the most recent <see cref="Run"/> returned false (**English, for diagnostics**).
        /// null on success. This is the mouth that stops us **failing silently**.
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// A survey that finishes in one tick. **It destroys nothing.**
        ///
        /// The sweep order is outwards from the centre (<see cref="OutwardCellOrder"/>). That is
        /// so what gets cut off when a limit is hit is **the outermost part**; row-major would
        /// start at the corner of the rectangle, which is the furthest point from the centre
        /// (②'s second-layer review I1).
        ///
        /// **Do not split it and keep counting.** Saying "this is approximate" is better than
        /// making the player wait (plan §4.1). When a limit is hit, set <c>Capped</c> and stop.
        /// </summary>
        public static bool Run(Vec3 point, VolcanoForm form, float requestedRadius,
                               float requestedHeight, out VolcanoFootprint footprint)
        {
            footprint = VolcanoFootprint.None;
            _lastFailure = null;

            try
            {
                if (float.IsNaN(point.X) || float.IsNaN(point.Z))
                {
                    _lastFailure = "the picked point was not a number";
                    return false;
                }

                float radius = VolcanoShape.RadiusFor(form, requestedRadius);
                if (!(radius > 0f))
                {
                    _lastFailure = "the clamped radius was not positive";
                    return false;
                }

                // The terrain height. **A value merely read out of the game's arrays**, so this is
                // the only place a confirmation row may claim [measured] (design doc §7.4).
                //
                // ★ If it cannot be read, **refuse** (whole-project review M14). Fall to 0 and it
                //   is indistinguishable from "it was at sea level", while the ceiling test and
                //   the mountain's height are both computed on top of that 0 — and that value
                //   then gets a [measured] marker.
                float ground = TerrainHeightSampler.Instance.SampleHeight(point.X, point.Z);
                if (float.IsNaN(ground))
                {
                    _lastFailure = "the ground height at the picked point could not be read";
                    return false;
                }

                float height = VolcanoShape.HeightFor(form, requestedHeight, ground);
                bool limited = VolcanoShape.HeightWasLimitedByCeiling(form, requestedHeight, ground);

                int tiles = TileCountFor(point.X, point.Z, radius);
                int catchUp = UpliftSchedule.BlockHeightCatchUpFrames(height);

                bool cappedBuildings;
                int buildings = CountBuildings(point, radius, out cappedBuildings);

                bool cappedSegments;
                int segments = CountSegments(point, radius, out cappedSegments);

                footprint = new VolcanoFootprint(true,
                    new Vec3(point.X, ground, point.Z), ground, form, radius, height, limited,
                    buildings, segments, tiles, catchUp, cappedBuildings || cappedSegments);
                return true;
            }
            catch (System.Exception e)
            {
                _lastFailure = "the survey threw " + e.GetType().Name;
                Log.Error("volcano survey failed", e);
                footprint = VolcanoFootprint.None;
                return false;
            }
        }

        /// <summary>The number of tiles the uplift passes to <c>UpdateArea</c>. The splitting is ⑤'s responsibility (trap 3).</summary>
        private static int TileCountFor(float centreX, float centreZ, float radius)
        {
            int minX, minZ, maxX, maxZ;
            if (!TileSplit.CellRangeFor(centreX, centreZ, radius, out minX, out minZ,
                                        out maxX, out maxZ))
            {
                return 0;
            }
            return TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
        }

        /// <summary>
        /// The real number of buildings in range. If it cannot be read it returns 0 and leaves
        /// <see cref="LastFailure"/> untouched (indistinguishable from empty wilderness with 0
        /// buildings, but **there is no environment where the building grid cannot be read** —
        /// <c>BuildingManager</c> always exists in game mode).
        /// </summary>
        private static int CountBuildings(Vec3 centre, float radius, out bool capped)
        {
            capped = false;

            // ★ Singleton<T>.instance is a main-thread-only API that runs FindObjectOfType and
            //    new GameObject when sInstance is null, so check exists first.
            if (!Singleton<BuildingManager>.exists) return 0;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return 0;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return 0;

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(centre, radius, 0, out minX, out maxX, out minZ, out maxZ,
                    out centreX, out centreZ);

            float radiusSquared = radius * radius;
            var origin = new Vec2(centre.X, centre.Z);

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            int found = 0, cells = 0;

            for (int ordinal = 0; ordinal < ordinalCount; ordinal++)
            {
                if (cells >= MaxCellsPerPass || found >= MaxItemsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                if (!OutwardCellOrder.Offset(ordinal, out dx, out dz)) continue;

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ Note the next ID **before acting** (the same as ② and ④). This only reads,
                    //    but break the shape here and it gets copied, broken, when T5 writes the
                    //    destruction next to the same file.
                    ushort next = buildings[id].m_nextGridBuilding;

                    // ★ The predicate is exactly the clearing stage's (VolcanoScan).
                    if (VolcanoScan.IsCandidate(buildings[id].m_flags)
                        && VolcanoScan.BuildingInside(buildings[id].m_position, origin,
                                                      radiusSquared))
                    {
                        found++;
                    }

                    id = next;
                    if (++guard >= BuildingChainGuard) break;
                }
            }

            return found;
        }

        /// <summary>
        /// The real number of road segments in range. **-1 if it cannot be read** (see
        /// <c>SegmentCount</c> in the class doc). <b>Not being able to count them and not being
        /// able to destroy them are different matters, and whether they can be destroyed is T5's
        /// decision</b>, so a -1 here does not stop the placement.
        /// </summary>
        private static int CountSegments(Vec3 centre, float radius, out bool capped)
        {
            capped = false;

            if (!Singleton<NetManager>.exists) return -1;

            var nm = Singleton<NetManager>.instance;
            if (nm == null) return -1;

            var segments = nm.m_segments != null ? nm.m_segments.m_buffer : null;
            var grid = nm.m_segmentGrid;
            if (segments == null || grid == null) return -1;

            // ★ Do not count if it does not match the measured length (do not run on a guess.
            //    Design doc §6).
            //    Index with z*270+x when it does not match and you count roads somewhere else
            //    entirely.
            if (grid.Length != GridSide * GridSide) return -1;

            // ★ The node buffer is used for the polyline test. If it cannot be read it stays null
            //   and VolcanoScan falls back to the single-midpoint test (the clearing stage does
            //   exactly the same).
            NetNode[] nodes = VolcanoScan.NodeBuffer(nm);

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(centre, radius, VolcanoScan.SegmentGridMargin,
                    out minX, out maxX, out minZ, out maxZ, out centreX, out centreZ);

            var origin = new Vec2(centre.X, centre.Z);

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            int found = 0, cells = 0;

            for (int ordinal = 0; ordinal < ordinalCount; ordinal++)
            {
                if (cells >= MaxCellsPerPass || found >= MaxItemsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                if (!OutwardCellOrder.Offset(ordinal, out dx, out dz)) continue;

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < segments.Length)
                {
                    ushort next = segments[id].m_nextGridSegment;

                    // ★ The predicate is exactly the clearing stage's (VolcanoScan).
                    if (VolcanoScan.IsCandidate(segments[id].m_flags)
                        && VolcanoScan.SegmentInside(segments, nodes, id, origin, radius))
                    {
                        found++;
                    }

                    id = next;
                    if (++guard >= SegmentChainGuard) break;
                }
            }

            return found;
        }

        /// <summary>
        /// The rectangle to sweep and the centre cell of the rings. <paramref name="marginCells"/>
        /// is non-zero only on the road side (class doc). The same clamp as the rectangle is
        /// applied to the centre too, so even a centre outside the map lands inside the grid.
        /// </summary>
        private static void RectFor(Vec3 centre, float radius, int marginCells,
                                    out int minX, out int maxX, out int minZ, out int maxZ,
                                    out int centreX, out int centreZ)
        {
            minX = ClampCell(CellOf(centre.X - radius) - marginCells);
            maxX = ClampCell(CellOf(centre.X + radius) + marginCells);
            minZ = ClampCell(CellOf(centre.Z - radius) - marginCells);
            maxZ = ClampCell(CellOf(centre.Z + radius) + marginCells);
            centreX = ClampCell(CellOf(centre.X));
            centreZ = ClampCell(CellOf(centre.Z));
        }

        private static int CellOf(float world)
        {
            return (int)(world / GridCellSize + GridCellOffset);
        }

        private static int ClampCell(int cell)
        {
            if (cell < 0) return 0;
            if (cell > GridSide - 1) return GridSide - 1;
            return cell;
        }
    }
}
