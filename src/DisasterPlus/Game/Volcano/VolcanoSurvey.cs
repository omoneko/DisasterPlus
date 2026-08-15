using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 1 つの火山の「調べた結果」。sim スレッドが作り、スナップショットに載って
    /// main スレッドが読む**不変の値型**である。
    ///
    /// <see cref="SegmentCount"/> だけ **-1 が「数えられなかった」**を表す。
    /// **0 と混ぜないこと** ——「範囲内に道路が 1 本も無い」と「本数を数える経路が
    /// この環境に無い」はまったく違う事実で、後者でも**道路は壊される**。
    /// </summary>
    public struct VolcanoFootprint
    {
        /// <summary>調査が成立したか。false のとき他のフィールドは読まない。</summary>
        public readonly bool Valid;

        /// <summary>クリックされたワールド座標。<c>Y</c> は地形高さ（m）。</summary>
        public readonly Vec3 Centre;

        /// <summary>設置地点の地形高さ（m）。**ゲームの配列から読んだだけの値。**</summary>
        public readonly float GroundHeightMetres;

        public readonly VolcanoForm Form;

        /// <summary>形態の帯へクランプ済みの半径（m）。</summary>
        public readonly float RadiusMetres;

        /// <summary>天井（§C-10）まで考慮して切り下げ済みの最終高（m）。</summary>
        public readonly float HeightMetres;

        /// <summary>天井のせいで要求より低い山になるか。**黙って低い山を作らない**ための旗。</summary>
        public readonly bool HeightLimitedByCeiling;

        /// <summary>調査時点で範囲内にあった建物の実数（丸める前）。</summary>
        public readonly int BuildingCount;

        /// <summary>
        /// 調査時点で範囲内にあった道路セグメントの実数（丸める前）。
        /// **-1 は「数えられなかった」**（クラス doc）。
        /// </summary>
        public readonly int SegmentCount;

        /// <summary>隆起で <c>UpdateArea</c> に渡すことになるタイル数（診断用）。</summary>
        public readonly int TileCount;

        /// <summary>「建てられる地面」と水位が追いつくまでの sim フレーム数（§A-2 / §A-4）。</summary>
        public readonly int BlockHeightCatchUpFrames;

        /// <summary>
        /// 1 tick ぶんの上限で走査を打ち切ったか。true のとき
        /// <see cref="BuildingCount"/> / <see cref="SegmentCount"/> は**下限**である。
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

        /// <summary>「調べていない」1 個。**0 を並べた「それらしい」値を作らない。**</summary>
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
    /// 影響範囲を**数えるだけ**の走査。**sim スレッド専用。**
    ///
    /// ★★ <b>この型は何も壊さない。</b> 建物も道路も地形も 1 つも触らない。
    /// 壊すのは T5（<c>VolcanoClearing</c>）、上げるのは T6（<c>VolcanoUplift</c>）である。
    /// レビューの grep: このファイルに建物や道路の破壊 API・地形の書き込み API が
    /// **1 つも現れないこと**（計画 T4 Step 8）。
    ///
    /// ── なぜ main スレッドから数えられないのか（計画 §4.1）─────────────
    ///
    /// <c>BuildingManager.m_buildingGrid</c> と <c>NetManager.m_segmentGrid</c> は
    /// **sim スレッドが所有している**。配置ツールのクリックハンドラは main スレッドなので、
    /// そこから数えると、スタックトレースの無い <c>IndexOutOfRangeException</c> が
    /// **後になってバニラのコードの中で**出る。だから⑤のクリック → 確認は
    /// <b>2 往復</b>する（<see cref="VolcanoState"/> のクラス doc）。
    ///
    /// ── グリッドの寸法（本タスクで IL 実測した。推測していない）───────────
    ///
    /// 建物側は②④が既に使っている値（セル 64・オフセット 135・<c>[0,269]</c>・
    /// <c>GridSide = 270</c>）。**道路側は本 MOD で初めてなので IL で確定させた**:
    ///
    /// <code>
    /// NetManager.Awake         : m_segmentGrid = new ushort[72900]     (= 270 * 270)
    /// NetManager.InitializeSegment (IL_0046-IL_00B6):
    ///     pos  = (nodes[m_startNode].m_position + nodes[m_endNode].m_position) * 0.5
    ///     x    = Mathf.Clamp((int)(pos.x / 64f + 135f), 0, 269)
    ///     z    = Mathf.Clamp((int)(pos.z / 64f + 135f), 0, 269)
    ///     idx  = z * 270 + x
    ///     segments[id].m_nextGridSegment = m_segmentGrid[idx];  m_segmentGrid[idx] = id
    /// NetSegment.m_nextGridSegment : UInt16（public instance）
    /// NetSegment.m_flags           : NetSegment.Flags（Created=1 / Deleted=2 /
    ///                                Collapsed=8 / Untouchable=0x20）
    /// NetSegment.m_middlePosition  : Vector3（NetSegment.UpdateBounds が
    ///                                2 本のベジェの中点の平均として書く）
    /// NetManager.Awake             : m_segments = new Array16&lt;NetSegment&gt;(36864)
    /// </code>
    ///
    /// **セルに入れる位置（両端ノードの中点）と、ここで距離を測る位置
    /// （<c>m_middlePosition</c>）は同じではない。** 曲がった道路ではベジェの中点が
    /// ノードの中点から離れるので、<b>矩形を <see cref="SegmentGridMargin"/> セルだけ
    /// 広げてから走査する</b>。広げないと、範囲の縁にある曲線道路を取りこぼす。
    /// 距離の代表点に <c>m_middlePosition</c> を選んだのは、それが「その道路が
    /// 実際にどこにあるか」だからである。
    ///
    /// **どちらの中点も、長い 1 本の道路の一部だけが範囲に掛かる場合を正しく表せない。**
    /// だからこれは概数であり、<see cref="VolcanoConfirmRows"/> はそう名乗る（設計書 §7.2）。
    /// </summary>
    public static class VolcanoSurvey
    {
        /// <summary>建物・道路グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>グリッドのセル寸法（m）。</summary>
        private const float GridCellSize = 64f;

        /// <summary>ワールド座標 → セル添字のオフセット。</summary>
        private const float GridCellOffset = 135f;

        /// <summary>1 回の走査で見るグリッドセルの上限（④の <c>TyphoonWind</c> と同じ）。</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>1 回の走査で数える建物／道路の上限（④の <c>TyphoonWind</c> と同じ）。</summary>
        private const int MaxItemsPerPass = 2048;

        /// <summary>建物の連結リストを辿る回数の上限（建物バッファの大きさ）。</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>道路の連結リストを辿る回数の上限（<c>Array16&lt;NetSegment&gt;(36864)</c>）。</summary>
        private const int SegmentChainGuard = 36864;

        /// <summary>道路の矩形を広げるセル数（クラス doc の「同じではない」）。</summary>
        private const int SegmentGridMargin = 2;

        /// <summary>
        /// 候補にするフラグ条件。②④の <c>CandidateMask</c> と同じ形。
        /// <c>Collapsed</c> と <c>Untouchable</c> を弾くのは、それらが準備段でも
        /// 壊れない（あるいは既に瓦礫である）ためで、数に入れると
        /// 「壊れる見込み」を実際より多く見せることになる。
        /// </summary>
        private const Building.Flags BuildingCandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>
        /// 道路側の同じもの。<c>NetSegment.Flags</c> に <c>Demolishing</c> は無い
        /// （IL 実測。クラス doc の一覧）。
        /// </summary>
        private const NetSegment.Flags SegmentCandidateMask =
            NetSegment.Flags.Created | NetSegment.Flags.Deleted
            | NetSegment.Flags.Untouchable | NetSegment.Flags.Collapsed;

        private static string _lastFailure;

        /// <summary>
        /// 直近の <see cref="Run"/> が false を返した理由（**英語・診断用**）。
        /// 成功したときは null。**黙って何もしないをやらない**ための口である。
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// 1 tick で終わる調査。**何も壊さない。**
        ///
        /// 走査順は中心から外側へ（<see cref="OutwardCellOrder"/>）。上限に当たったときに
        /// 切り捨てられるのが**いちばん外側**になるようにするためで、行優先だと
        /// 最初に見るのが矩形の角＝中心からいちばん遠い場所になる（②の第 2 層レビュー I1）。
        ///
        /// **分割して数え続けない。** プレイヤーを待たせるより「概数です」と言うほうが
        /// 正しい（計画 §4.1）。上限に当たったら <c>Capped</c> を立てて打ち切る。
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

                // 地形高さ。**ゲームの配列から読んだだけの値**なので、
                // 確認の行はここだけ [measured] を名乗ってよい（設計書 §7.4）。
                float ground = TerrainHeightSampler.Instance.SampleHeight(point.X, point.Z);
                if (float.IsNaN(ground)) ground = 0f;

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

        /// <summary>隆起で <c>UpdateArea</c> に渡すタイル数。分割は⑤の責任である（罠 3）。</summary>
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
        /// 範囲内の建物の実数。読めなければ 0 を返し <see cref="LastFailure"/> は触らない
        /// （建物が 0 棟の荒野と区別が付かないが、**建物グリッドが読めない環境は
        /// 存在しない** —— <c>BuildingManager</c> はゲームモードで必ず在る）。
        /// </summary>
        private static int CountBuildings(Vec3 centre, float radius, out bool capped)
        {
            capped = false;

            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので exists で先に見る。
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
                    // ★ 次の ID は**行動する前に**控える（②④と同じ）。ここは読むだけだが、
                    //    形を崩すと T5 が同じファイルの隣に破壊を書くときに崩れたまま写る。
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & BuildingCandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        if (origin.DistanceSquaredTo(new Vec2(p.x, p.z)) <= radiusSquared) found++;
                    }

                    id = next;
                    if (++guard >= BuildingChainGuard) break;
                }
            }

            return found;
        }

        /// <summary>
        /// 範囲内の道路セグメントの実数。**読めなければ -1**（クラス doc の
        /// <c>SegmentCount</c>）。<b>数えられないことと壊せないことは別の話であり、
        /// 壊せるかどうかは T5 が決める</b>ので、ここが -1 でも設置は止めない。
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

            // ★ 実測した長さと合わなければ数えない（推測で走らない。設計書 §6）。
            //    合わないまま z*270+x で引くと、まったく別の場所の道路を数える。
            if (grid.Length != GridSide * GridSide) return -1;

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(centre, radius, SegmentGridMargin, out minX, out maxX, out minZ, out maxZ,
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

                while (id != 0 && id < segments.Length)
                {
                    ushort next = segments[id].m_nextGridSegment;

                    if ((segments[id].m_flags & SegmentCandidateMask) == NetSegment.Flags.Created)
                    {
                        Vector3 p = segments[id].m_middlePosition;
                        if (origin.DistanceSquaredTo(new Vec2(p.x, p.z)) <= radiusSquared) found++;
                    }

                    id = next;
                    if (++guard >= SegmentChainGuard) break;
                }
            }

            return found;
        }

        /// <summary>
        /// 走査する矩形とリングの中心セル。<paramref name="marginCells"/> は
        /// 道路側だけ 0 でない（クラス doc）。矩形と同じクランプを中心にも掛けるので、
        /// 中心がマップの外でもグリッドの中に落ちる。
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
