using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="VolcanoClearing"/> のうち、**実際にグリッドを辿って取り除く部分**。
    /// **sim スレッド専用。**
    ///
    /// 分割してあるのは 800 行の規則のためで、意味の境目でもある ——
    /// 本体（<c>VolcanoClearing.cs</c>）が「いつ・どこまで走るか」を決め、
    /// こちらが「どう辿って、何を呼ぶか」を持つ。**判断は 1 つも増やさないこと。**
    /// 走査の形（セル 64・オフセット 135・<c>[0,269]</c>・<c>z*270+x</c>・
    /// <c>OutwardCellOrder</c>・次の ID を行動前に控える）は
    /// <c>VolcanoSurvey</c> / <c>TyphoonWind</c> / <c>LongPeriodDamage</c> と同じである。
    /// </summary>
    public static partial class VolcanoClearing
    {
        /// <summary>
        /// 建物の走査。<c>demolish: true</c> / <c>burnAmount: 0</c>（クラス doc）。
        ///
        /// **dry-run はフィルタに使わない。** <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> /
        /// <c>MonorailPylonAI</c> は <c>if (testOnly) return false;</c> の直後に本物の倒壊を
        /// 行う（④ §F-2）ので、dry-run を信じて呼ばないと送電柱を 1 本も取り除けない。
        /// ⑤は本番だけを呼び、**その戻り値をそのまま「取り除けたか」とする**。
        /// </summary>
        private static int ClearBuildings(VolcanoFootprint footprint, out bool capped,
                                          out float reachedRadius)
        {
            capped = false;
            reachedRadius = _frontRadius;

            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので exists で先に見る。
            if (!Singleton<BuildingManager>.exists) return 0;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return 0;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return 0;

            // ★ 実測した長さと合わなければ走らない（推測で走らない。設計書 §6）。
            //   このとき**届いた半径は 0 にする** —— 前線に届いたことにすると、
            //   T6 が壊れていない場所を上げる。準備は Clearing のまま止まり、
            //   理由は LastFailure と診断に出る。
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the building grid is not 270x270 in this build; "
                               + "nothing was cleared";
                reachedRadius = 0f;
                return 0;
            }

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
                if (cells >= MaxCellsPerPass || scanned >= MaxBuildingsPerPass)
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

                while (id != 0 && id < buildings.Length)
                {
                    // ★ 次の ID は**行動する前に**控える（②④と同じ）。倒壊は建物を
                    //    解放しうるし、道路側の解放が建物を巻き込むこともある
                    //    （NetManager.ReleaseNodeImplementation → ReleaseBuilding）。
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & BuildingCandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        if (origin.DistanceSquaredTo(new Vec2(p.x, p.z)) <= radiusSquared)
                        {
                            scanned++;
                            if (Demolish(buildings, id)) destroyed++;
                            else refused++;
                        }
                    }

                    id = next;
                    if (++guard >= BuildingChainGuard) break;
                }
            }

            _buildingCursor = ordinal >= ordinalCount ? 0 : ordinal;
            _lastBuildingsDestroyed = destroyed;
            _lastBuildingsRefused = refused;
            _totalBuildingsDestroyed += destroyed;

            if (capped) reachedRadius = ReachedRadius(lastRing);
            return scanned;
        }

        /// <summary>
        /// 道路の走査。<c>demolish: true</c> で <c>PlayerNetAI</c> の解放経路へ入る
        /// （クラス doc の Step 1 の 1）。**dry-run に相当する引数がそもそも無い。**
        ///
        /// 矩形は <see cref="SegmentGridMargin"/> セルだけ広げる —— セルを決める位置
        /// （両端ノードの中点）と距離を測る位置（<c>m_middlePosition</c>）が違うので、
        /// 広げないと範囲の縁にある曲線道路を取りこぼす（§F-15）。
        /// </summary>
        private static int ClearSegments(VolcanoFootprint footprint, out bool capped,
                                         out float reachedRadius)
        {
            capped = false;
            reachedRadius = _frontRadius;

            if (!Singleton<NetManager>.exists) return 0;

            var nm = Singleton<NetManager>.instance;
            if (nm == null) return 0;

            var segments = nm.m_segments != null ? nm.m_segments.m_buffer : null;
            var grid = nm.m_segmentGrid;
            if (segments == null || grid == null) return 0;

            // ★ 実測した長さと合わなければ走らない（推測で走らない。設計書 §6）。
            //    合わないまま z*270+x で引くと、まったく別の場所の道路を壊す。
            //    **届いた半径は 0**（上の建物側と同じ理由）。ここへ来る前に
            //    RoadPathAvailable が false になって着手そのものを断っているので、
            //    実際にはまず到達しない二重の保険である。
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the road grid is not 270x270 in this build; no road was removed";
                reachedRadius = 0f;
                return 0;
            }

            int minX, maxX, minZ, maxZ, centreX, centreZ;
            RectFor(footprint.Centre, _frontRadius, SegmentGridMargin,
                    out minX, out maxX, out minZ, out maxZ, out centreX, out centreZ);

            float radiusSquared = _frontRadius * _frontRadius;
            var origin = new Vec2(footprint.Centre.X, footprint.Centre.Z);

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            int scanned = 0, destroyed = 0, refused = 0, cells = 0, lastRing = 0;

            int ordinal = _segmentCursor;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass || scanned >= MaxSegmentsPerPass)
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

                while (id != 0 && id < segments.Length)
                {
                    // ★ 次の ID は**行動する前に**控える。解放はこのセルの連結リストを
                    //    その場で繋ぎ替えるので、控えないと残りが黙って飛ぶ。
                    ushort next = segments[id].m_nextGridSegment;

                    if ((segments[id].m_flags & SegmentCandidateMask) == NetSegment.Flags.Created)
                    {
                        var p = segments[id].m_middlePosition;
                        if (origin.DistanceSquaredTo(new Vec2(p.x, p.z)) <= radiusSquared)
                        {
                            scanned++;
                            if (Demolish(segments, id)) destroyed++;
                            else refused++;
                        }
                    }

                    id = next;
                    if (++guard >= SegmentChainGuard) break;
                }
            }

            _segmentCursor = ordinal >= ordinalCount ? 0 : ordinal;
            _lastSegmentsDestroyed = destroyed;
            _lastSegmentsRefused = refused;
            _totalSegmentsDestroyed += destroyed;

            if (capped) reachedRadius = ReachedRadius(lastRing);
            return scanned;
        }

        /// <summary>
        /// 建物を 1 棟取り除く。**<c>DisasterHelpers</c> を通らない**（§E-14）。
        /// <c>demolish: true</c> ＝ 跡地を残さない、<c>burnAmount: 0</c> ＝ 焼損ではない。
        /// **<c>m_fireIntensity</c> には触れない**（罠 5）。
        /// </summary>
        private static bool Demolish(Building[] buildings, ushort id)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            // group は null。⑤は災害スロットに載らないので束ねる先が無く、
            // バニラ側は 3 箇所とも null を検査している（クラス doc の 5）。
            return info.m_buildingAI.CollapseBuilding(id, ref buildings[id], null,
                                                      false, true, 0);
        }

        /// <summary>
        /// 道路セグメントを 1 本取り除く。<c>demolish: true</c> が
        /// <c>PlayerNetAI.CollapseSegment</c> の <c>NetManager.ReleaseSegment(id, false)</c> へ
        /// 繋がる（クラス doc の Step 1 の 1）。**<c>DisasterHelpers</c> を通らない。**
        /// </summary>
        private static bool Demolish(NetSegment[] segments, ushort id)
        {
            var info = segments[id].Info;
            if (info == null || info.m_netAI == null) return false;

            return info.m_netAI.CollapseSegment(id, ref segments[id], null, true);
        }

        /// <summary>
        /// 上限で打ち切ったときに「確実に走査を終えた」と言える半径（m）。
        ///
        /// リングは中心の**セル**を中心とした正方形なので、リング
        /// <paramref name="lastRing"/> の途中で止まったなら完全に終わっているのは
        /// <c>lastRing - 1</c> 本ぶんである。中心の点はそのセルの中のどこにでもありうるので、
        /// 保証できる円の半径はさらに 1 セルぶん内側になる —— それが <c>(lastRing - 1) * 64</c>
        /// である。**多めに言わない。** 多めに言うと T6 が壊れていない場所を上げる。
        /// </summary>
        private static float ReachedRadius(int lastRing)
        {
            int rings = lastRing - 1;
            if (rings < 0) rings = 0;

            float reached = rings * GridCellSize;
            return reached > _frontRadius ? _frontRadius : reached;
        }

        /// <summary>
        /// 走査する矩形とリングの中心セル。<paramref name="marginCells"/> は道路側だけ
        /// 0 でない。矩形と同じクランプを中心にも掛けるので、中心がマップの外でも
        /// グリッドの中に落ちる（<c>VolcanoSurvey.RectFor</c> と同じ形）。
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
