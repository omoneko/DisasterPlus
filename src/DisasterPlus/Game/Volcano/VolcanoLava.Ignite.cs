using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="VolcanoLava"/> that **reads the terrain and actually sets things
    /// alight**. **Sim thread only.**
    ///
    /// The split is for the 800-line rule, but it is also a boundary in meaning — the main file
    /// (<c>VolcanoLava.cs</c>) decides "when and where to advance", and this one holds
    /// "what to call at that point". **Do not add a single decision here.**
    ///
    /// Only four APIs appear here (§B-7 / §B-6 / §A-4):
    ///
    /// <code>
    /// TerrainManager.SampleDetailHeight(Vector3, out float, out float)   height and gradient
    /// TerrainManager.HasWater(Vector2)                                   water (sim only)
    /// DisasterHelpers.BurnGround(Vector2, float, float)                  scorching the ground (no DLC needed)
    /// BuildingAI.BurnBuilding(ushort, ref Building, Group, bool)         buildings (no DLC needed)
    /// TreeManager.BurnTree(uint, Group, int)                             trees (★ ND required)
    /// </code>
    ///
    /// **There is no API in the game for burning roads** (§B-7d; there is nothing at all
    /// equivalent to <c>BurnSegment</c> / <c>BurnNode</c>). Roads surviving under the lava is by
    /// design, and <c>Strings.VolcanoLavaRoadsNote</c> says so. The only roads removed are those
    /// inside the volcano's range, and that happens in the clearing stage (T5).
    /// </summary>
    public static partial class VolcanoLava
    {
        /// <summary>Cells along one side of the building grid (one cell is 64 m. The same measurement as ②, ④ and T5).</summary>
        private const int BuildingGridSide = 270;

        /// <summary>The size of one building-grid cell (m).</summary>
        private const float BuildingGridCellSize = 64f;

        /// <summary>The offset of the building grid's cell coordinates.</summary>
        private const float BuildingGridCellOffset = 135f;

        /// <summary>Insurance against a corrupted building linked list (the capacity of <c>m_buildings</c>).</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>Maximum building-grid cells examined per step (at a 60 m radius it is at most 3×3 in practice).</summary>
        private const int MaxBuildingCellsPerStep = 25;

        /// <summary>
        /// Maximum <c>BurnBuilding</c> calls per step. **Whole-project review M17.**
        ///
        /// Back when the limit was <b>on the cell count alone</b>, a single cell in a dense area
        /// with several hundred buildings hanging off it produced several hundred calls in one
        /// step (the cell limit of 25 only bounds "how far the step looks", not the work done).
        /// The lava only advances 12 m per step while the ignition radius is up to 60 m, so
        /// **one flow hits the same building about five times, and eight flows up to 40 times**.
        /// What is bounded here is the calls themselves.
        /// </summary>
        private const int MaxBuildingsPerStep = 48;

        /// <summary>
        /// Cells along one side of the tree grid. Measured in the IL of <c>TreeManager.Awake</c> /
        /// <c>InitializeTree</c> (<c>TREEGRID_RESOLUTION = 540</c>, the index is <c>z*540 + x</c>).
        /// </summary>
        private const int TreeGridSide = 540;

        /// <summary>The size of one tree-grid cell (m). <c>TREEGRID_CELL_SIZE = 32</c>.</summary>
        private const float TreeGridCellSize = 32f;

        /// <summary>
        /// The offset of the tree grid's cell coordinates. **Derived from IL in this task**
        /// (it is a different value from the building grid's 135, so do not copy the wrong one):
        ///
        /// <code>
        /// TreeInstance.set_Position (game mode): m_posX = world * 3.792593
        /// TreeManager.InitializeTree           : cell = (m_posX + 32768) * 540 / 65536
        ///   => cell = world * 3.792593 * 540 / 65536 + 270 = world / 32 + 270
        /// </code>
        ///
        /// (Only when <c>ToolController.m_mode == 4</c> (the asset editor) does <c>m_posX</c>'s
        /// scale become 16 times larger, with <c>InitializeTree</c> dividing by 16 first.
        /// **In game mode the formula above is correct.**)
        /// </summary>
        private const float TreeGridCellOffset = 270f;

        /// <summary>Insurance against a corrupted tree linked list (the capacity of <c>m_trees</c>).</summary>
        private const int TreeChainGuard = 262144;

        /// <summary>Maximum tree-grid cells examined per step (at a 60 m radius it is at most 5×5 in practice).</summary>
        private const int MaxTreeCellsPerStep = 64;

        /// <summary>
        /// Maximum <c>BurnTree</c> calls per step (the same reasoning as the building side.
        /// Whole-project review M17).
        /// The tree grid is denser at 32 m per cell, so this is set higher than the buildings'.
        /// </summary>
        private const int MaxTreesPerStep = 96;

        /// <summary>
        /// The strength passed to <c>BurnTree</c>. **It is truncated (not clamped) by
        /// <c>conv.u1</c>**, so the caller keeps it within <c>[128, 255]</c> (§B-7c).
        /// Vanilla's real values are 128 in <c>DisasterHelpers.DestroyTrees</c> and
        /// <c>Min(255, 128 + intensity)</c> in <c>ForestFireAI</c>. ⑤ is lava, so it uses a
        /// stronger 192.
        /// </summary>
        private const int TreeFireIntensity = 192;

        private const int TreeFireIntensityMin = 128;
        private const int TreeFireIntensityMax = 255;

        /// <summary>
        /// Read the terrain height (m) and the gradient. **<c>slopeX</c> / <c>slopeZ</c> point
        /// uphill** (the IL measurements in the main file's class doc). The caller flips the sign
        /// before using them.
        ///
        /// <c>Physics.Raycast</c> is not used — **the terrain has no collider, so it always
        /// misses** (§B-6 / known).
        /// </summary>
        private static bool SampleSlope(Vec2 p, out float height, out float slopeX,
                                        out float slopeZ)
        {
            height = 0f;
            slopeX = 0f;
            slopeZ = 0f;

            if (!Singleton<TerrainManager>.exists) return false;

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null) return false;

            height = tm.SampleDetailHeight(new Vector3(p.X, 0f, p.Z), out slopeX, out slopeZ);

            if (float.IsNaN(height) || float.IsNaN(slopeX) || float.IsNaN(slopeZ))
            {
                height = 0f;
                slopeX = 0f;
                slopeZ = 0f;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether it touched water (design doc §4.5).
        ///
        /// ★ <b><c>HasWater</c> is sim thread only.</b> The IL takes
        /// <c>m_waterSimulation.BeginRead()</c> / <c>EndRead()</c> in a try/finally (handled the
        /// same way as ②'s <c>TsunamiChain.IsUnderWater</c>), so calling it from the main thread
        /// races the water simulation's buffer swap.
        /// The test is "water surface − terrain &gt;= 8 raw units (= 0.125 m)".
        ///
        /// If it cannot be read, fall to **false (there is no water)** — stop the lava because
        /// something could not be read and the lava would never move a single step on a map with
        /// no water.
        /// </summary>
        private static bool HasWater(Vec2 p)
        {
            if (!Singleton<TerrainManager>.exists) return false;

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null) return false;

            return tm.HasWater(new Vector2(p.X, p.Z));
        }

        /// <summary>
        /// Note whether it went out onto tiles that have not been purchased. **Not a bug** —
        /// <c>GetDetailHeight</c> falls through to <c>SampleFinalHeight</c> when
        /// <c>m_simDetailIndex == 0</c>, so the sampling goes from 4 m to 16 m interpolation
        /// (§B-6). There is no step in the terrain, but the behaviour changes, so report one line
        /// in the diagnostics.
        ///
        /// <c>GameAreaManager.PointOutOfArea(Vector3)</c> is a public instance method and a read
        /// only, so it is safe to call from the sim thread.
        /// </summary>
        private static void NoteArea(Vec2 p)
        {
            if (_outsidePurchasedArea) return;

            try
            {
                if (!Singleton<GameAreaManager>.exists) return;

                var gm = Singleton<GameAreaManager>.instance;
                if (gm == null) return;

                if (gm.PointOutOfArea(new Vector3(p.X, 0f, p.Z))) _outsidePurchasedArea = true;
            }
            catch
            {
                // If we cannot tell, claim nothing. It does not affect the lava's behaviour.
            }
        }

        /// <summary>Whether the ND DLC is owned (only igniting trees branches on it. §B-7c).</summary>
        private static bool ReadTreesAvailable()
        {
            try
            {
                return ModCompat.NaturalDisastersOwned;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Set things alight at one point the lava passed through. **It can be turned off in the
        /// settings** (the lava still flows when it is off).
        /// </summary>
        private static void Ignite(Vec2 p, float travelledMetres)
        {
            if (!ModSettings.VolcanoLavaFire.value) return;

            // ★ The width varies with the scale of the eruption (<c>LavaVolume.WidthFactor</c>).
            //   **It must be the same radius as the band that is drawn** — a building under
            //   glowing lava that does not burn is a lie (the doc of
            //   <c>LavaPath.SpreadRadiusFor</c>).
            float radius = LavaPath.SpreadRadiusFor(travelledMetres, _widthFactor);

            BurnGround(p, radius);
            IgniteBuildings(p, radius);

            // ★ Only the trees branch on ND. Without it, **do not burn them; explain instead**
            //   (do not substitute removing them with ReleaseTree. The main file's class doc).
            if (_treesAvailable) IgniteTrees(p, radius);
        }

        /// <summary>
        /// Scorch the ground. **No DLC needed** (§B-7b).
        /// <c>intensity</c> is <b>a normalised 0.0–1.0 value</b> and is multiplied by 255
        /// internally.
        /// The value is monotonically non-decreasing, so passing over the same place repeatedly
        /// never makes it fainter.
        /// </summary>
        private static void BurnGround(Vec2 p, float radius)
        {
            try
            {
                DisasterHelpers.BurnGround(new Vector2(p.X, p.Z), radius, GroundBurnIntensity);
            }
            catch
            {
                _lastFailure = "DisasterHelpers.BurnGround threw; the ground is not scorched";
            }
        }

        /// <summary>
        /// Set alight the buildings the lava touched. **It does not go through
        /// <c>DisasterHelpers</c>** (§E-14).
        ///
        /// The sweep is **row-major** (the reason is in the main file's class doc; the rectangle
        /// is at most 3×3 cells).
        /// The discipline of "note the next ID before acting" is the same as in ②, ④ and T5 —
        /// ignition itself does not release a building, but do not leave code shaped in a way that
        /// breaks the discipline.
        ///
        /// The distance is measured from the building's <c>m_position</c>. A large building whose
        /// corner is inside the radius will not burn if its centre is outside, but
        /// **that is an approximation, and saying so is better than lying that "everything burns"**
        /// (the same call as design doc §7.2).
        ///
        /// ★★ <b>Do not write extra to the fire-intensity field after the return value</b>
        /// (trap 5).
        /// The fire strength is decided per building by <c>GetFireParameters</c>.
        /// Being refused (flooded, rubble) is normal, so it is merely tallied into
        /// <see cref="_buildingsRefused"/>.
        /// </summary>
        private static void IgniteBuildings(Vec2 p, float radius)
        {
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ Do not run if it does not match the measured length (do not run on a guess.
            //   Design doc §6).
            if (grid.Length != BuildingGridSide * BuildingGridSide)
            {
                _lastFailure = "the building grid is not 270x270 in this build; "
                               + "the lava does not set buildings on fire";
                return;
            }

            int minX = ClampCell(CellOf(p.X - radius, BuildingGridCellSize,
                                        BuildingGridCellOffset), BuildingGridSide);
            int maxX = ClampCell(CellOf(p.X + radius, BuildingGridCellSize,
                                        BuildingGridCellOffset), BuildingGridSide);
            int minZ = ClampCell(CellOf(p.Z - radius, BuildingGridCellSize,
                                        BuildingGridCellOffset), BuildingGridSide);
            int maxZ = ClampCell(CellOf(p.Z + radius, BuildingGridCellSize,
                                        BuildingGridCellOffset), BuildingGridSide);

            float radiusSquared = radius * radius;
            int cells = 0;
            int burned = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (++cells > MaxBuildingCellsPerStep) return;

                    int index = z * BuildingGridSide + x;
                    if (index < 0 || index >= grid.Length) continue;

                    ushort id = grid[index];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        ushort next = buildings[id].m_nextGridBuilding;

                        if ((buildings[id].m_flags & (Building.Flags.Created
                                                      | Building.Flags.Deleted))
                            == Building.Flags.Created)
                        {
                            var bp = buildings[id].m_position;
                            float dx = bp.x - p.X;
                            float dz = bp.z - p.Z;

                            if (dx * dx + dz * dz <= radiusSquared)
                            {
                                // ★ Bound the calls themselves (whole-project review M17).
                                if (++burned > MaxBuildingsPerStep) return;

                                if (Burn(buildings, id)) _buildingsIgnited++;
                                else _buildingsRefused++;
                            }
                        }

                        id = next;
                        if (++guard >= BuildingChainGuard) break;
                    }
                }
            }
        }

        /// <summary>
        /// Set one building alight. <c>group</c> is <c>null</c> (§G-16 (e). ⑤ does not occupy a
        /// disaster slot, so there is nothing to group into, and vanilla checks for null).
        /// <c>testOnly: false</c> = for real. **The dry run is not used as a filter.**
        /// </summary>
        private static bool Burn(Building[] buildings, ushort id)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            return info.m_buildingAI.BurnBuilding(id, ref buildings[id], null, false);
        }

        /// <summary>
        /// Set alight the trees the lava touched. **Without the ND DLC this always returns
        /// false**, so we branch on <c>_treesAvailable</c> before calling (the DLC gate of §B-7c.
        /// ★ A traceability correction from whole-project review M11: the facts doc says
        /// <c>SupportsExpansion</c>, but what this mod actually evaluates here is
        /// <c>ModCompat.NaturalDisastersOwned</c> = <c>SteamHelper.IsDLCOwned</c>.
        /// While a level is loaded, the two return the same answer).
        ///
        /// A tree that has burnt once never burns again (<c>BurnTree</c> itself looks at
        /// <c>m_flags &amp; 64 FireDamage</c>). **Do not count those misses as anomalies.**
        /// </summary>
        private static void IgniteTrees(Vec2 p, float radius)
        {
            if (!Singleton<TreeManager>.exists) return;

            var tm = Singleton<TreeManager>.instance;
            if (tm == null) return;

            var trees = tm.m_trees != null ? tm.m_trees.m_buffer : null;
            var grid = tm.m_treeGrid;
            if (trees == null || grid == null) return;

            // ★ Do not run if it does not match the measured length (design doc §6).
            if (grid.Length != TreeGridSide * TreeGridSide)
            {
                _lastFailure = "the tree grid is not 540x540 in this build; "
                               + "the lava does not set trees on fire";
                return;
            }

            int minX = ClampCell(CellOf(p.X - radius, TreeGridCellSize, TreeGridCellOffset),
                                 TreeGridSide);
            int maxX = ClampCell(CellOf(p.X + radius, TreeGridCellSize, TreeGridCellOffset),
                                 TreeGridSide);
            int minZ = ClampCell(CellOf(p.Z - radius, TreeGridCellSize, TreeGridCellOffset),
                                 TreeGridSide);
            int maxZ = ClampCell(CellOf(p.Z + radius, TreeGridCellSize, TreeGridCellOffset),
                                 TreeGridSide);

            float radiusSquared = radius * radius;
            int intensity = ClampTreeIntensity(TreeFireIntensity);
            int cells = 0;
            int burned = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (++cells > MaxTreeCellsPerStep) return;

                    int index = z * TreeGridSide + x;
                    if (index < 0 || index >= grid.Length) continue;

                    uint id = grid[index];
                    int guard = 0;

                    while (id != 0u && id < trees.Length)
                    {
                        uint next = trees[id].m_nextGridTree;

                        // Only those with Created(1) set and Deleted(2) clear.
                        // FireDamage(64) is checked by BurnTree itself, so it is not checked here.
                        if ((trees[id].m_flags & 3) == 1)
                        {
                            Vector3 tp = trees[id].Position;
                            float dx = tp.x - p.X;
                            float dz = tp.z - p.Z;

                            if (dx * dx + dz * dz <= radiusSquared)
                            {
                                // ★ Bound the calls themselves (whole-project review M17).
                                if (++burned > MaxTreesPerStep) return;

                                if (tm.BurnTree(id, null, intensity)) _treesIgnited++;
                            }
                        }

                        id = next;
                        if (++guard >= TreeChainGuard) break;
                    }
                }
            }
        }

        /// <summary>
        /// Keep it within <c>[128, 255]</c>. **<c>BurnTree</c> truncates with <c>conv.u1</c>
        /// (it does not clamp)**, so 256 becomes 0 and 300 becomes 44 (§B-7c).
        /// </summary>
        private static int ClampTreeIntensity(int value)
        {
            if (value < TreeFireIntensityMin) return TreeFireIntensityMin;
            if (value > TreeFireIntensityMax) return TreeFireIntensityMax;
            return value;
        }

        private static int CellOf(float world, float cellSize, float offset)
        {
            return (int)(world / cellSize + offset);
        }

        private static int ClampCell(int cell, int side)
        {
            if (cell < 0) return 0;
            if (cell > side - 1) return side - 1;
            return cell;
        }
    }
}
