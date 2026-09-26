using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="VolcanoClearing"/> that **actually walks the grid and removes
    /// things**. **Sim thread only.**
    ///
    /// The split is for the 800-line rule, but it is also a boundary in meaning — the main file
    /// (<c>VolcanoClearing.cs</c>) decides "when and how far to run", and this one holds
    /// "how to walk and what to call". **Do not add a single decision here.**
    /// The shape of the sweep (64 m cells, offset 135, <c>[0,269]</c>, <c>z*270+x</c>,
    /// <c>OutwardCellOrder</c>, note the next ID before acting) is the same as in
    /// <c>VolcanoSurvey</c> / <c>TyphoonWind</c> / <c>LongPeriodDamage</c>.
    ///
    /// ★★ <b>"What is inside the range" is not decided by this file.</b>
    /// The masks, the margins and the hit test are all in <see cref="VolcanoScan"/>, and
    /// **the survey (<see cref="VolcanoSurvey"/>) uses the same ones** —
    /// a structure that stops the counting side's and the destroying side's predicates ever
    /// drifting apart again (whole-project review I2 / I4).
    ///
    /// ── the limit on how much one sweep destroys (whole-project review M16) ────────────────
    ///
    /// The limit is tested <b>partway through walking the linked list as well</b>. Test it only at
    /// the head of a cell and a dense area with several hundred buildings hanging off one cell
    /// blows well past the budget.
    /// A cell cut short partway is <b>redone from the head on the next sweep</b>
    /// (<c>ordinal</c> is stepped back by one) — treat a partly-processed cell as finished and the
    /// terrain stays pinned under the buildings left in it while the uplift goes ahead.
    /// Redoing it never destroys anything twice (the mask excludes <c>Demolishing</c>).
    ///
    /// > ★★ <b>It is only redone when at least one thing in that cell could be destroyed.</b>
    /// > What was destroyed drops out of the mask, so the redoing always terminates after a finite
    /// > number of times.
    /// > Redo it unconditionally and **one cell holding a budget's worth of buildings that vanilla
    /// > refuses is enough to make the clearing mark time for ever, and the phase never moves on
    /// > from <c>Clearing</c>** (a refused building stays <c>Created</c>, so the next sweep counts
    /// > exactly the same number).
    /// </summary>
    public static partial class VolcanoClearing
    {
        /// <summary>
        /// The building sweep. <c>demolish: true</c> / <c>burnAmount: 0</c> (class doc).
        ///
        /// **The dry run is not used as a filter.** <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> /
        /// <c>MonorailPylonAI</c> perform the real collapse immediately after
        /// <c>if (testOnly) return false;</c> (④ §F-2), so trusting the dry run and not calling
        /// would leave every power pole standing.
        /// ⑤ calls only for real, and **takes that return value as "was it removed"**.
        /// </summary>
        private static int ClearBuildings(VolcanoFootprint footprint, out bool capped,
                                          out float reachedRadius)
        {
            capped = false;

            // ★★ **The initial value of the radius reached is 0** (whole-project review M7).
            //    This used to be set to _frontRadius, so the three "could not read" returns below
            //    **claimed the sweep had reached the front** and gave T6 permission to uplift a
            //    city where not one building had been destroyed.
            //    Raise it to _frontRadius only once we know it actually ran.
            reachedRadius = 0f;

            // ★ Singleton<T>.instance is a main-thread-only API that runs FindObjectOfType and
            //    new GameObject when sInstance is null, so check exists first.
            if (!Singleton<BuildingManager>.exists)
            {
                _lastFailure = "BuildingManager is not available; nothing was cleared";
                return 0;
            }

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null)
            {
                _lastFailure = "BuildingManager is not available; nothing was cleared";
                return 0;
            }

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null)
            {
                _lastFailure = "the building buffer or grid is not readable; nothing was cleared";
                return 0;
            }

            // ★ Do not run if it does not match the measured length (do not run on a guess.
            //   Design doc §6).
            //   **The radius reached is 0 here** — claim it reached the front and T6 raises ground
            //   that has not been cleared. The clearing stalls at Clearing and the reason goes
            //   into LastFailure and the diagnostics.
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the building grid is not 270x270 in this build; "
                               + "nothing was cleared";
                return 0;
            }

            // ★ The upper bound on the radius this lap may claim (whole-project review M8). A pass
            //   resumed from partway through the cursor has swept [0, cursor) at
            //   **the front as it was then** (which was smaller). Claim we swept inside that at
            //   today's front and the buildings that have since come outside the old front are
            //   left standing while the uplift overtakes them.
            float passFront = PassFront(_buildingCursor, ref _buildingPassFront);

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(footprint.Centre, _frontRadius, 0, out minX, out maxX, out minZ, out maxZ,
                    out centreX, out centreZ);

            float radiusSquared = _frontRadius * _frontRadius;
            var origin = new Vec2(footprint.Centre.X, footprint.Centre.Z);

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            int scanned = 0, destroyed = 0, refused = 0, cells = 0, lastRing = 0;

            int ordinal = _buildingCursor;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                bool ok = OutwardCellOrder.Offset(ordinal, out dx, out dz);
                ordinal++;
                if (!ok) continue;

                lastRing = OutwardCellOrder.RingOf(dx, dz);

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;
                bool budgetHit = false;
                int destroyedHere = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ Note the next ID **before acting** (the same as ② and ④). A collapse can
                    //    release the building, and releasing a road can take buildings with it
                    //    (NetManager.ReleaseNodeImplementation → ReleaseBuilding).
                    ushort next = buildings[id].m_nextGridBuilding;

                    // ★ The predicate is exactly the survey's (VolcanoScan).
                    if (VolcanoScan.IsCandidate(buildings[id].m_flags)
                        && VolcanoScan.BuildingInside(buildings[id].m_position, origin,
                                                      radiusSquared))
                    {
                        // ★ Check the budget **immediately before destroying** (M16 in the class doc).
                        if (scanned >= MaxBuildingsPerPass)
                        {
                            budgetHit = true;
                            break;
                        }

                        scanned++;
                        if (Demolish(buildings, id)) { destroyed++; destroyedHere++; }
                        else refused++;
                    }

                    id = next;
                    if (++guard >= BuildingChainGuard) break;
                }

                if (budgetHit)
                {
                    capped = true;

                    // ★★ **Redo it only when at least one building in this cell was destroyed.**
                    //    A destroyed building gets Demolishing set and drops out of the mask, so
                    //    the redoing always terminates after a finite number of times.
                    //    **Redo a cell where nothing could be destroyed and it marks time there
                    //    for ever** — one cell holding a budget's worth of buildings that vanilla
                    //    refuses is enough to stop the clearing ever moving forward again
                    //    (refused buildings stay Created, so the next time counts the same number).
                    //    What was refused will go on being refused. Terrain remaining under those
                    //    alone is a known consequence, tallied into refused and reported in the
                    //    diagnostics and on the panel.
                    if (destroyedHere > 0) ordinal--;
                    break;
                }
            }

            _buildingCursor = ordinal >= ordinalCount ? 0 : ordinal;
            _lastBuildingsDestroyed = destroyed;
            _lastBuildingsRefused = refused;
            _totalBuildingsDestroyed += destroyed;

            reachedRadius = capped ? ReachedRadius(lastRing) : _frontRadius;
            if (reachedRadius > passFront) reachedRadius = passFront;

            // Once a full lap is done the cursor is back at 0. The next pass sweeps the whole area
            // at today's front, so the bound goes back to today's front too.
            if (_buildingCursor == 0) _buildingPassFront = 0f;
            return scanned;
        }

        /// <summary>
        /// The road sweep. <c>demolish: true</c> enters <c>PlayerNetAI</c>'s release path
        /// (point 1 of Step 1 in the class doc). **There is no argument corresponding to a dry run
        /// in the first place.**
        ///
        /// The rectangle is widened by <c>VolcanoScan.SegmentGridMargin</c> cells, and the
        /// distance is measured along **the polyline end node → midpoint → end node**
        /// (<see cref="VolcanoScan"/>).
        /// Back when it was measured at the single midpoint, arterial roads running in towards the
        /// centre were left in place, and **a flat trench was left inside the finished mountain**
        /// (whole-project review I4).
        /// </summary>
        private static int ClearSegments(VolcanoFootprint footprint, out bool capped,
                                         out float reachedRadius)
        {
            capped = false;

            // ★★ The initial value is 0 for the same reason as the building side
            //    (whole-project review M7).
            reachedRadius = 0f;

            if (!Singleton<NetManager>.exists)
            {
                _lastFailure = "NetManager is not available; no road was removed";
                return 0;
            }

            var nm = Singleton<NetManager>.instance;
            if (nm == null)
            {
                _lastFailure = "NetManager is not available; no road was removed";
                return 0;
            }

            var segments = nm.m_segments != null ? nm.m_segments.m_buffer : null;
            var grid = nm.m_segmentGrid;
            if (segments == null || grid == null)
            {
                _lastFailure = "the road buffer or grid is not readable; no road was removed";
                return 0;
            }

            // ★ Do not run if it does not match the measured length (do not run on a guess.
            //    Design doc §6).
            //    Index with z*270+x when it does not match and you destroy roads somewhere else
            //    entirely.
            //    **The radius reached is 0** (the same reason as the building side above). Before
            //    we ever get here, ClearingPathAvailable has gone false and refused the placement
            //    outright, so in practice this is a second layer of insurance that is never
            //    reached.
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the road grid is not 270x270 in this build; no road was removed";
                return 0;
            }

            // ★ The node buffer (for the polyline test). If it cannot be read it stays null and
            //   VolcanoScan falls back to the single-midpoint test (the survey does exactly the
            //   same).
            NetNode[] nodes = VolcanoScan.NodeBuffer(nm);

            // ★ The same "upper bound on the radius this pass may claim" as the building side
            //   (whole-project review M8).
            float passFront = PassFront(_segmentCursor, ref _segmentPassFront);

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(footprint.Centre, _frontRadius, VolcanoScan.SegmentGridMargin,
                    out minX, out maxX, out minZ, out maxZ, out centreX, out centreZ);

            var origin = new Vec2(footprint.Centre.X, footprint.Centre.Z);

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            int scanned = 0, destroyed = 0, refused = 0, cells = 0, lastRing = 0;

            int ordinal = _segmentCursor;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                bool ok = OutwardCellOrder.Offset(ordinal, out dx, out dz);
                ordinal++;
                if (!ok) continue;

                lastRing = OutwardCellOrder.RingOf(dx, dz);

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;
                bool budgetHit = false;
                int destroyedHere = 0;

                while (id != 0 && id < segments.Length)
                {
                    // ★ Note the next ID **before acting**. Releasing re-links this cell's linked
                    //    list on the spot, so without noting it the rest is silently skipped.
                    ushort next = segments[id].m_nextGridSegment;

                    // ★ The predicate is exactly the survey's (VolcanoScan).
                    if (VolcanoScan.IsCandidate(segments[id].m_flags)
                        && VolcanoScan.SegmentInside(segments, nodes, id, origin, _frontRadius))
                    {
                        // ★ Check the budget **immediately before destroying** (M16 in the class doc).
                        if (scanned >= MaxSegmentsPerPass)
                        {
                            budgetHit = true;
                            break;
                        }

                        scanned++;
                        if (Demolish(segments, id)) { destroyed++; destroyedHere++; }
                        else refused++;
                    }

                    id = next;
                    if (++guard >= SegmentChainGuard) break;
                }

                if (budgetHit)
                {
                    capped = true;

                    // ★★ For the same reason as the building side, **redo it only when at least
                    //    one segment could be released**. A released road disappears from the
                    //    array, so the redoing terminates after a finite number of times.
                    //    Roads that get refused (SupportCableAI, and Untouchable ones whose owning
                    //    building refused to collapse) will go on being refused, so do not mark
                    //    time on them.
                    if (destroyedHere > 0) ordinal--;
                    break;
                }
            }

            _segmentCursor = ordinal >= ordinalCount ? 0 : ordinal;
            _lastSegmentsDestroyed = destroyed;
            _lastSegmentsRefused = refused;
            _totalSegmentsDestroyed += destroyed;

            reachedRadius = capped ? ReachedRadius(lastRing) : _frontRadius;
            if (reachedRadius > passFront) reachedRadius = passFront;

            if (_segmentCursor == 0) _segmentPassFront = 0f;
            return scanned;
        }

        /// <summary>
        /// The upper bound on the radius this lap may claim to have "finished sweeping" (m).
        /// **Whole-project review M8.**
        ///
        /// A non-zero cursor means the previous pass was cut short by a limit, and the inner rings
        /// were swept at <b>the front as it was then</b>. The front keeps growing afterwards, so
        /// claiming "a full lap" at today's front would mean that the buildings and roads in the
        /// inner rings that are "outside the old front but inside today's" **count as done without
        /// ever being swept**. So the bound is the minimum of the fronts involved.
        ///
        /// Once a full lap finishes and the cursor returns to 0, the caller discards the noted
        /// value, so the next pass can claim today's front as it stands (it is self-repairing).
        /// </summary>
        private static float PassFront(int cursor, ref float carried)
        {
            float front = _frontRadius;

            if (cursor > 0 && carried > 0f && carried < front) front = carried;

            carried = front;
            return front;
        }

        /// <summary>
        /// Remove one building. **It does not go through <c>DisasterHelpers</c>** (§E-14).
        /// <c>demolish: true</c> = leave no ruin behind, <c>burnAmount: 0</c> = this is not
        /// scorching. **It never touches <c>m_fireIntensity</c>** (trap 5).
        /// </summary>
        private static bool Demolish(Building[] buildings, ushort id)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            // group is null. ⑤ does not occupy a disaster slot, so there is nothing to group into,
            // and vanilla checks for null at all three sites (point 5 of the class doc).
            return info.m_buildingAI.CollapseBuilding(id, ref buildings[id], null,
                                                      false, true, 0);
        }

        /// <summary>
        /// Remove one road segment. <c>demolish: true</c> leads into
        /// <c>PlayerNetAI.CollapseSegment</c>'s <c>NetManager.ReleaseSegment(id, false)</c>
        /// (point 1 of Step 1 in the class doc). **It does not go through
        /// <c>DisasterHelpers</c>.**
        /// </summary>
        private static bool Demolish(NetSegment[] segments, ushort id)
        {
            var info = segments[id].Info;
            if (info == null || info.m_netAI == null) return false;

            return info.m_netAI.CollapseSegment(id, ref segments[id], null, true);
        }

        /// <summary>
        /// The radius (m) we can claim to have "definitely finished sweeping" when cut short by a
        /// limit.
        ///
        /// The rings are squares centred on the centre **cell**, so stopping partway through ring
        /// <paramref name="lastRing"/> means what is fully done is <c>lastRing - 1</c> rings.
        /// The centre point can be anywhere inside that cell, so the circle we can guarantee is a
        /// further cell in — which is <c>(lastRing - 1) * 64</c>.
        /// **Do not claim more.** Claim more and T6 raises ground that has not been cleared.
        /// </summary>
        private static float ReachedRadius(int lastRing)
        {
            int rings = lastRing - 1;
            if (rings < 0) rings = 0;

            float reached = rings * GridCellSize;
            return reached > _frontRadius ? _frontRadius : reached;
        }

        /// <summary>
        /// The rectangle to sweep and the centre cell of the rings. <paramref name="marginCells"/>
        /// is non-zero only on the road side. The same clamp as the rectangle is applied to the
        /// centre too, so even a centre outside the map lands inside the grid (the same shape as
        /// <c>VolcanoSurvey.RectFor</c>).
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
