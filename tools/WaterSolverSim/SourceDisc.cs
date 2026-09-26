using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b>Reproduction of <c>WaterSource</c>'s <c>TYPE_NATURAL</c> (the river springs on a map).</b>
    ///
    /// ── Why this one (2026-08-31, the owner's instruction) ────────────────────
    ///
    /// &gt; If we just use the DLC as it is, there is no point in shipping a mod at all.
    /// &gt; Analyse the mechanism and apply it: make a tsunami radiate in concentric circles
    /// &gt; from near the epicentre.
    ///
    /// What makes the DLC tsunami strong is <b>not the amplitude but the trick</b> —— it
    /// rewrites the sea surface in the outermost cells, and the Dirichlet boundary
    /// <b>creates water</b>. A <c>TYPE_IMPACT</c> mound only displaces water, it never creates
    /// any, so no matter how big it is made it cannot imitate this (see the class doc of
    /// <see cref="EdgeWave"/>, and the offline comparison of 2026-08-31: on the same shelf,
    /// 84.8 m of shoreline run-up against 19.3 m).
    ///
    /// ★★ **But the game does have a tool that does the same thing in the middle of the map.**
    ///   <c>WaterSource</c>'s <c>TYPE_NATURAL</c> is a device that
    ///   "fills / drains a given circle to a given water level", and it can be placed anywhere.
    ///   <b>Bring the boundary condition to the epicentre</b> —— that is what "analyse and
    ///   apply" amounts to.
    ///
    /// ── The IL (<c>WaterSimulation.SimulateWater</c> IL_184A-20B3) ────────────
    ///
    /// Intake (around <c>m_inputRate</c> and <c>m_inputPosition</c>):
    /// <code>
    /// r     = sqrt(inputRate)*0.4 + 10          // m. only type 2/3 apply min(50, r)
    /// total = SUM min(terrain + h - max(target, terrain), h)   // water above the target
    /// take  = min(inputRate, total &gt;&gt; 1)        // natural takes half the excess per step
    /// // second pass: subtract share = (share*take + total - 1)/total from each cell
    /// </code>
    ///
    /// Output (around <c>m_outputRate</c> and <c>m_outputPosition</c>):
    /// <code>
    /// out  = outputRate                          // ★ natural springs regardless of m_water
    /// r    = sqrt(out)*0.4 + 10                  // ★ natural has no upper limit
    /// room = SUM min(terrain + h - max(target, terrain), h)   // shortfall to the target (negative)
    ///        but natural skips cells where terrain &gt;= target (i.e. never outputs onto land)
    /// out  = min(out, -(room &gt;&gt; 1))              // half the shortfall per step
    /// // second pass: add share = (out + count/2)/count evenly to each cell
    /// </code>
    ///
    /// ★ <b>The radius is set by the flow rate</b> (<c>r = sqrt(rate)*0.4 + 10</c>). For a
    ///   radius of 1280 m the rate comes out around 1.0e7. The rate is the "speed" and
    ///   <c>m_target</c> sets the "height", so the two can be dialled in independently.
    /// </summary>
    internal sealed class SourceDisc
    {
        /// <summary>Centre of the circle (cell).</summary>
        public int CellX;
        public int CellZ;

        /// <summary>Target water surface (absolute elevation in 1/64 m). In the game
        /// <c>m_target</c> is a ushort.</summary>
        public int Target;

        /// <summary>Intake flow rate. 0 means nothing is taken in.</summary>
        public long InputRate;

        /// <summary>Output flow rate. 0 means nothing is put out.</summary>
        public long OutputRate;

        /// <summary>Amount created so far (diagnostic).</summary>
        public long Made;

        /// <summary>Amount drained so far (diagnostic).</summary>
        public long Taken;

        /// <summary>
        /// <b>How many times the game's int32 would have overflowed.</b> (A diagnostic with no
        /// counterpart in the game.)
        ///
        /// ★★ A setting where this is not 0 <b>must not be used in the real game</b>.
        ///   See the doc at the top of this class, and IL_1B4C-1B58.
        /// </summary>
        public long Int32Overflows;

        /// <summary>Largest product that overflowed (diagnostic). int.MaxValue =
        /// 2,147,483,647.</summary>
        public long WorstProduct;

        /// <summary>
        /// The highest water surface inside the circle (absolute elevation in 1/64 m).
        /// **Needed to pin the target down from measurement** (Program's --ringgap).
        /// </summary>
        public int MaxSurface(WaterField field, float radiusMetres)
        {
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, radiusMetres, out minX, out minZ, out maxX, out maxZ)) return 0;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = radiusMetres * radiusMetres;
            int best = int.MinValue;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int s = terrain[i] + cells[i].Height;
                    if (s > best) best = s;
                }
            }

            return best == int.MinValue ? 0 : best;
        }

        /// <summary>The same radius as the game (m). natural has no upper limit.</summary>
        public static float RadiusMetres(long rate)
        {
            return (float)Math.Sqrt(rate) * 0.4f + 10f;
        }

        /// <summary>One water step. **Intake first, output second** (the order in the IL).</summary>
        public void Apply(WaterField field)
        {
            Take(field);
            Give(field);
        }

        private void Take(WaterField field)
        {
            if (InputRate <= 0) return;

            float rm = RadiusMetres(InputRate);
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, rm, out minX, out minZ, out maxX, out maxZ)) return;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = rm * rm;

            long total = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];
                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    total += Math.Min(g + h - lvl, h);
                }
            }

            // ★★ In the game `total` is an int32 too (loc154). **Check it before the early
            //    return** —— put it after and a total that overflowed negative (when there is
            //    land inside the circle) goes uncounted (2026-08-31, second round of checks).
            if (total > int.MaxValue || total < int.MinValue)
            {
                Int32Overflows++;
                if (Math.Abs(total) > WorstProduct) WorstProduct = Math.Abs(total);
            }

            long take = Math.Min(InputRate, total >> 1);
            if (take <= 0) return;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];
                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    long share = Math.Min(g + h - lvl, h);

                    // ★★ **The game holds everything up to just before the division in int32**
                    //    (IL_1B4C-1B58: the mul, add and sub are all int32).
                    //    Looking only at `share * take` was <b>getting the expression wrong</b>
                    //    (2026-08-31, second round of checks).
                    long product = share * take + total - 1;
                    if (product > int.MaxValue || product < int.MinValue)
                    {
                        Int32Overflows++;
                        if (Math.Abs(product) > WorstProduct) WorstProduct = Math.Abs(product);
                    }

                    share = (share * take + total - 1) / total;
                    if (share <= 0) continue;

                    // ★★ **The game has no clamp at h** (IL_1B59-1BDA only has the
                    //    `share <= 0` test and then goes straight to
                    //    `m_height = (ushort)(h - share)`).
                    //    Clamping here was <b>the only reason the reproduction stayed intact</b>.
                    //    Truncate to ushort exactly as the game does, and let it break if it breaks.
                    cells[i].Height = (ushort)(h - share);
                    Taken += share;
                }
            }
        }

        private void Give(WaterField field)
        {
            if (OutputRate <= 0) return;

            long give = OutputRate;
            float rm = RadiusMetres(give);
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, rm, out minX, out minZ, out maxX, out maxZ)) return;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = rm * rm;

            long room = 0;
            int count = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];

                    // ★ natural never outputs onto land (IL_1E51-1E61).
                    if (g >= Target) continue;

                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    room += Math.Min(g + h - lvl, h);
                    count++;
                }
            }

            if (count <= 0) return;

            // ★ On the output side, room is an int32 as well (loc181).
            if (room > int.MaxValue || room < int.MinValue)
            {
                Int32Overflows++;
                if (Math.Abs(room) > WorstProduct) WorstProduct = Math.Abs(room);
            }

            long allowed = -(room >> 1);
            if (give > allowed) give = allowed;
            if (give <= 0) return;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    if (terrain[i] >= Target) continue;

                    int h = cells[i].Height;
                    long share = (give + count / 2) / count;
                    if (share > 65535 - h) share = 65535 - h;
                    if (share <= 0) continue;

                    cells[i].Height = (ushort)(h + share);
                    Made += share;
                }
            }
        }

        private bool Bounds(WaterField field, float rm, out int minX, out int minZ,
                            out int maxX, out int maxZ)
        {
            int rc = (int)Math.Ceiling(rm / WaterField.CellSizeMetres);
            minX = Math.Max(0, CellX - rc);
            minZ = Math.Max(0, CellZ - rc);
            maxX = Math.Min(field.LastIndex, CellX + rc);
            maxZ = Math.Min(field.LastIndex, CellZ + rc);
            return minX <= maxX && minZ <= maxZ;
        }
    }
}
