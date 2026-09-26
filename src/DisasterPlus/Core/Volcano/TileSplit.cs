namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// Splitting the rectangle handed to TerrainModify.UpdateArea. **The splitting is the
    /// caller's responsibility.**
    ///
    /// When the rectangle exceeds 128×128 raw cells, UpdateArea **silently truncates it
    /// rather than splitting it into tiles** (§A-1, the Min(m_maxX, m_minX + 120 + 8) at
    /// IL_026C). Whatever overflows stays un-updated. Vanilla itself
    /// (TerrainManager.UpdateData) also does its own splitting, into 9×9 patches of 120
    /// cells.
    ///
    /// There is a second limit: **a single request over 10,000 cells in area ignores any
    /// enclosing batch and flushes immediately** (§A-1 IL_0399 / §D-12).
    ///
    /// **What these tests apply to is the rectangle actually passed to UpdateArea.** To
    /// avoid a step at the seams, ⑤ widens each tile by ±Margin before passing it, so it is
    /// the widened dimensions that must stay under both thresholds:
    ///
    ///     CoreTileSide (95) + 2 * Margin (2) = MaxPassedSide (99)
    ///     99 &lt; 128        99*99 = 9801 &lt; 10000
    ///
    /// So <see cref="TileAt"/> returns <b>the already-widened rectangle</b>.
    /// **The caller must not add the margin back on** (add it and you get 103×103 = 10,609
    /// cells, which crosses the 10,000 threshold and flushes every time).
    /// </summary>
    public static class TileSplit
    {
        /// <summary>The upper bound on raw cell indices (the <c>res = 1080</c> from §C-8;
        /// the array itself is 1081²).</summary>
        public const int RawResolution = 1080;

        /// <summary>The side of one raw cell (m).</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>The offset from world coordinates to cell indices (the <c>+ 540</c> from
        /// §C-8).</summary>
        public const int CellOffset = 540;

        /// <summary>How many cells one tile covers (before widening).</summary>
        public const int CoreTileSide = 95;

        /// <summary>How far we widen to avoid a step at the seams (the same ±2 as
        /// <c>MakeCrater</c> / <c>MakeCrack</c>).</summary>
        public const int Margin = 2;

        /// <summary>The upper bound on the side of the rectangle actually passed to
        /// UpdateArea. **Must be under 128.**</summary>
        public const int MaxPassedSide = CoreTileSide + 2 * Margin;

        /// <summary>The upper bound on the cell count of the rectangle actually passed to
        /// UpdateArea. **Must be under 10,000.**</summary>
        public const int MaxPassedCells = MaxPassedSide * MaxPassedSide;

        /// <summary>
        /// Works out, from a centre and a radius, the rectangle of raw cells to cover
        /// (inclusive at both ends).
        ///
        /// The formula is the same as the one measured in <c>MakeCrater</c>
        /// (§C-8 IL_002C-0085) — <c>(v / 16) + 540</c>, <c>+ 1</c> on the maximum side, and
        /// both ends clamped to <c>[0, 1080]</c>.
        /// NaN, or a radius of zero or below, gives false (and the caller does nothing).
        /// </summary>
        public static bool CellRangeFor(float centreX, float centreZ, float radiusMetres,
                                        out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = 0;
            minZ = 0;
            maxX = 0;
            maxZ = 0;

            if (float.IsNaN(centreX) || float.IsNaN(centreZ) || float.IsNaN(radiusMetres)) return false;
            if (radiusMetres <= 0f) return false;

            minX = ClampCell((int)((centreX - radiusMetres) / RawCellSizeMetres) + CellOffset);
            minZ = ClampCell((int)((centreZ - radiusMetres) / RawCellSizeMetres) + CellOffset);
            maxX = ClampCell((int)((centreX + radiusMetres) / RawCellSizeMetres) + CellOffset + 1);
            maxZ = ClampCell((int)((centreZ + radiusMetres) / RawCellSizeMetres) + CellOffset + 1);

            return minX <= maxX && minZ <= maxZ;
        }

        /// <summary>
        /// Whether this rectangle **can be emitted as-is in a single <c>UpdateArea</c>**.
        ///
        /// The conditions are the same two <see cref="TileAt"/> respects, evaluated
        /// **after adding ±<see cref="Margin"/>**:
        ///
        ///     side + 2*Margin &lt;= MaxPassedSide (99) &lt; 128     … no truncation
        ///     (side + 2*Margin)^2 &lt;= MaxPassedCells (9801) &lt; 10000 … no mid-way flush
        ///
        /// Both reduce to the single condition "at most <c>CoreTileSide</c> (95)".
        ///
        /// **This exists solely to decide whether the tile split can be skipped.**
        /// When it is false, split with <see cref="TileAt"/> as before.
        /// Skip the split because it "roughly fits" and whatever overflows **stays
        /// un-updated** (truncation raises no exception, §A-1).
        /// </summary>
        public static bool FitsSinglePass(int minX, int minZ, int maxX, int maxZ)
        {
            if (minX > maxX || minZ > maxZ) return false;
            return (maxX - minX + 1) <= CoreTileSide && (maxZ - minZ + 1) <= CoreTileSide;
        }

        /// <summary>
        /// Widens the rectangle by <see cref="Margin"/> and clamps it. **This is the
        /// rectangle to pass straight to <c>UpdateArea</c>** (the same property as
        /// <see cref="TileAt"/>'s return value: the caller must not add the margin back on).
        ///
        /// Only use it on rectangles for which <see cref="FitsSinglePass"/> is true.
        /// The clamp only ever shrinks, so the result can never exceed
        /// <see cref="MaxPassedSide"/>.
        /// </summary>
        public static bool ExpandForPass(int minX, int minZ, int maxX, int maxZ,
                                         out int pMinX, out int pMinZ, out int pMaxX, out int pMaxZ)
        {
            pMinX = 0;
            pMinZ = 0;
            pMaxX = 0;
            pMaxZ = 0;

            if (minX > maxX || minZ > maxZ) return false;

            pMinX = ClampCell(minX - Margin);
            pMinZ = ClampCell(minZ - Margin);
            pMaxX = ClampCell(maxX + Margin);
            pMaxZ = ClampCell(maxZ + Margin);
            return true;
        }

        /// <summary>How many tiles it takes to cover the rectangle. 0 for an empty
        /// rectangle.</summary>
        public static int TileCountFor(int minX, int minZ, int maxX, int maxZ)
        {
            if (minX > maxX || minZ > maxZ) return 0;
            return CeilDiv(maxX - minX + 1, CoreTileSide) * CeilDiv(maxZ - minZ + 1, CoreTileSide);
        }

        /// <summary>
        /// The <paramref name="index"/>-th tile, as **the rectangle to pass straight to
        /// UpdateArea**. Row-major (X advances first). **The caller must not add the margin
        /// back on** (see the class doc).
        /// </summary>
        public static bool TileAt(int index, int minX, int minZ, int maxX, int maxZ,
                                  out int tMinX, out int tMinZ, out int tMaxX, out int tMaxZ)
        {
            tMinX = 0;
            tMinZ = 0;
            tMaxX = 0;
            tMaxZ = 0;

            if (minX > maxX || minZ > maxZ) return false;

            int columns = CeilDiv(maxX - minX + 1, CoreTileSide);
            int rows = CeilDiv(maxZ - minZ + 1, CoreTileSide);
            if (index < 0 || index >= columns * rows) return false;

            int column = index % columns;
            int row = index / columns;

            int coreMinX = minX + column * CoreTileSide;
            int coreMinZ = minZ + row * CoreTileSide;
            int coreMaxX = coreMinX + CoreTileSide - 1;
            int coreMaxZ = coreMinZ + CoreTileSide - 1;
            if (coreMaxX > maxX) coreMaxX = maxX;
            if (coreMaxZ > maxZ) coreMaxZ = maxZ;

            // ★ Widen first, then clamp. The clamp only ever shrinks, so the returned side
            //   can never exceed MaxPassedSide.
            tMinX = ClampCell(coreMinX - Margin);
            tMinZ = ClampCell(coreMinZ - Margin);
            tMaxX = ClampCell(coreMaxX + Margin);
            tMaxZ = ClampCell(coreMaxZ + Margin);
            return true;
        }

        private static int CeilDiv(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        private static int ClampCell(int cell)
        {
            if (cell < 0) return 0;
            if (cell > RawResolution) return RawResolution;
            return cell;
        }
    }
}
