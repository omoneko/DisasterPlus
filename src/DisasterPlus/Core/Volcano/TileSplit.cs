namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// TerrainModify.UpdateArea へ渡す矩形の分割。**分割は呼び出し側の責任である。**
    ///
    /// UpdateArea は矩形が 128×128 raw セルを超えると、**タイル分割せずに無言で
    /// 切り捨てる**（§A-1、IL_026C の Min(m_maxX, m_minX + 120 + 8)）。
    /// はみ出した部分は更新されないまま残る。バニラ自身
    /// （TerrainManager.UpdateData）も 9×9 パッチ × 120 セルで自分で分割している。
    ///
    /// もう 1 つ、**単発の要求面積が 10000 セルを超えると入れ子のバッチを無視して
    /// 即フラッシュする**（§A-1 IL_0399 / §D-12）。
    ///
    /// **判定に掛かるのは UpdateArea に実際に渡した矩形である。** ⑤は境目に段差を
    /// 作らないため各タイルを ±Margin だけ広げて渡すので、その広げたあとの寸法で
    /// 両方の閾値を下回らなければならない:
    ///
    ///     CoreTileSide (95) + 2 * Margin (2) = MaxPassedSide (99)
    ///     99 &lt; 128        99*99 = 9801 &lt; 10000
    ///
    /// したがって <see cref="TileAt"/> は<b>広げたあとの矩形</b>を返す。
    /// **呼び出し側で margin を足し直さないこと**（足すと 103×103 = 10609 セルになり、
    /// 10000 の閾値を跨いで毎回フラッシュする）。
    /// </summary>
    public static class TileSplit
    {
        /// <summary>raw セル添字の上限（§C-8 の <c>res = 1080</c>。配列そのものは 1081²）。</summary>
        public const int RawResolution = 1080;

        /// <summary>raw セルの一辺（m）。</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>ワールド座標 → セル添字のオフセット（§C-8 の <c>+ 540</c>）。</summary>
        public const int CellOffset = 540;

        /// <summary>1 タイルが担当する（広げる前の）セル数。</summary>
        public const int CoreTileSide = 95;

        /// <summary>継ぎ目に段差を作らないための広げ幅（<c>MakeCrater</c> / <c>MakeCrack</c> と同じ ±2）。</summary>
        public const int Margin = 2;

        /// <summary>実際に UpdateArea へ渡す矩形の一辺の上限。**128 未満であること。**</summary>
        public const int MaxPassedSide = CoreTileSide + 2 * Margin;

        /// <summary>実際に UpdateArea へ渡す矩形のセル数の上限。**10000 未満であること。**</summary>
        public const int MaxPassedCells = MaxPassedSide * MaxPassedSide;

        /// <summary>
        /// 中心と半径から、覆うべき raw セルの矩形（両端を含む）を出す。
        ///
        /// 式は <c>MakeCrater</c> の実測（§C-8 IL_002C–0085）と同じ ——
        /// <c>(v / 16) + 540</c>、最大側は <c>+ 1</c>、両端を <c>[0, 1080]</c> でクランプ。
        /// NaN や半径 0 以下は false（呼び出し側は何もしない）。
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
        /// この矩形は**1 回の <c>UpdateArea</c> でそのまま出せるか**。
        ///
        /// 出せる条件は <see cref="TileAt"/> が守っているのと同じ 2 つで、
        /// **±<see cref="Margin"/> を足したあと**に評価する:
        ///
        ///     一辺 + 2*Margin &lt;= MaxPassedSide (99) &lt; 128     … 切り捨てない
        ///     (一辺 + 2*Margin)^2 &lt;= MaxPassedCells (9801) &lt; 10000 … 途中フラッシュしない
        ///
        /// 両方とも <c>CoreTileSide</c>（95）以下という 1 つの条件に帰着する。
        ///
        /// **これは「タイル分割を省いてよいか」を判定するためだけにある。**
        /// false のときは今までどおり <see cref="TileAt"/> で分割すること。
        /// 「だいたい入るから」で分割を省くと、はみ出した部分が
        /// **更新されないまま残る**（切り捨ては例外にならない、§A-1）。
        /// </summary>
        public static bool FitsSinglePass(int minX, int minZ, int maxX, int maxZ)
        {
            if (minX > maxX || minZ > maxZ) return false;
            return (maxX - minX + 1) <= CoreTileSide && (maxZ - minZ + 1) <= CoreTileSide;
        }

        /// <summary>
        /// 矩形を <see cref="Margin"/> だけ広げてクランプする。**そのまま
        /// <c>UpdateArea</c> へ渡す矩形**（<see cref="TileAt"/> の戻り値と同じ性質で、
        /// 呼び出し側で margin を足し直してはいけない）。
        ///
        /// <see cref="FitsSinglePass"/> が true の矩形にだけ使うこと。
        /// クランプは縮める向きにしか働かないので、返り値が
        /// <see cref="MaxPassedSide"/> を超えることはない。
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

        /// <summary>矩形を覆うのに要るタイル数。空の矩形なら 0。</summary>
        public static int TileCountFor(int minX, int minZ, int maxX, int maxZ)
        {
            if (minX > maxX || minZ > maxZ) return 0;
            return CeilDiv(maxX - minX + 1, CoreTileSide) * CeilDiv(maxZ - minZ + 1, CoreTileSide);
        }

        /// <summary>
        /// <paramref name="index"/> 番目のタイルの、**UpdateArea へそのまま渡す矩形**。
        /// 行優先（X が先に進む）。**呼び出し側で margin を足し直さないこと**（クラス doc）。
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

            // ★ 広げてからクランプする。クランプは縮める向きにしか働かないので、
            //   返り値の一辺が MaxPassedSide を超えることはない。
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
