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
    ///
    /// ★★ <b>「何が範囲に入っているか」はこのファイルが決めていない。</b>
    /// マスクも余白も当たり判定も <see cref="VolcanoScan"/> にあり、
    /// **調査（<see cref="VolcanoSurvey"/>）が同じものを使う** ——
    /// 数える側と壊す側の述語が二度とずれないための構造である（全体レビュー I2 / I4）。
    ///
    /// ── 1 回の走査で壊す数の上限（全体レビュー M16）─────────────────
    ///
    /// 上限の判定は<b>連結リストを辿る途中にも置く</b>。セルの先頭でしか見ないと、
    /// 1 つのセルに数百棟ぶら下がっている密集地で予算を大きく踏み越える。
    /// 途中で打ち切ったセルは<b>次回の走査で先頭からやり直す</b>
    /// （<c>ordinal</c> を 1 つ戻す）—— 途中まで進んだセルを終わったことにすると、
    /// そのセルに残った建物の足元だけ地形が固定されたまま隆起する。
    /// やり直しても二重に壊すことは無い（<c>Demolishing</c> はマスクが弾く）。
    ///
    /// > ★★ <b>やり直すのは「このセルで 1 つでも壊せたとき」だけである。</b>
    /// > 壊せたものはマスクから外れるので、やり直しは必ず有限回で終わる。
    /// > 無条件にやり直すと、**バニラが断る建物が 1 セルに予算ぶん並んでいるだけで
    /// > 準備が永久に足踏みし、位相が <c>Clearing</c> のまま二度と進まない**
    /// > （断られた建物は <c>Created</c> のままなので、次の走査も同じ数を数える）。
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

            // ★★ **届いた半径の初期値は 0 である**（全体レビュー M7）。
            //    以前はここで _frontRadius を入れていたので、下の「読めなかった」
            //    3 つの return が**前線まで走査し終えたと名乗り**、T6 に
            //    1 棟も壊れていない街の上を隆起させる許可を出していた。
            //    実際に走れたと分かってから _frontRadius に上げる。
            reachedRadius = 0f;

            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので exists で先に見る。
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

            // ★ 実測した長さと合わなければ走らない（推測で走らない。設計書 §6）。
            //   このとき**届いた半径は 0 にする** —— 前線に届いたことにすると、
            //   T6 が壊れていない場所を上げる。準備は Clearing のまま止まり、
            //   理由は LastFailure と診断に出る。
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the building grid is not 270x270 in this build; "
                               + "nothing was cleared";
                return 0;
            }

            // ★ この 1 周が言える半径の上限（全体レビュー M8）。カーソルの途中から
            //   再開したパスは、[0, cursor) を**そのときの（より小さい）前線**で
            //   走査し終えている。今の前線でその内側まで走査したことにすると、
            //   前回より外へ出た分の建物が残ったまま隆起が追い越す。
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
                    // ★ 次の ID は**行動する前に**控える（②④と同じ）。倒壊は建物を
                    //    解放しうるし、道路側の解放が建物を巻き込むこともある
                    //    （NetManager.ReleaseNodeImplementation → ReleaseBuilding）。
                    ushort next = buildings[id].m_nextGridBuilding;

                    // ★ 述語は調査とまったく同じもの（VolcanoScan）。
                    if (VolcanoScan.IsCandidate(buildings[id].m_flags)
                        && VolcanoScan.BuildingInside(buildings[id].m_position, origin,
                                                      radiusSquared))
                    {
                        // ★ 予算は**壊す直前**に見る（クラス doc の M16）。
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

                    // ★★ **やり直すのは、このセルで 1 棟でも壊せたときだけ**である。
                    //    壊せた建物は Demolishing が立ってマスクから外れるので、
                    //    やり直しは必ず有限回で終わる。**1 棟も壊せなかったセルで
                    //    やり直すと、そこで永久に足踏みする** —— バニラが断る建物が
                    //    1 セルに予算ぶん並んでいるだけで、準備が二度と前へ進まなくなる
                    //    （断られた建物は Created のままなので、次回も同じ数だけ数える）。
                    //    断られたものはこの先も断られる。その足元だけ地形が残るのは
                    //    既知の帰結で、refused に積んで診断とパネルに出している。
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

            // 一周し切ったらカーソルは 0 に戻っている。次のパスは今の前線で
            // 全域を走査するので、上限も今の前線に戻る。
            if (_buildingCursor == 0) _buildingPassFront = 0f;
            return scanned;
        }

        /// <summary>
        /// 道路の走査。<c>demolish: true</c> で <c>PlayerNetAI</c> の解放経路へ入る
        /// （クラス doc の Step 1 の 1）。**dry-run に相当する引数がそもそも無い。**
        ///
        /// 矩形は <c>VolcanoScan.SegmentGridMargin</c> セルだけ広げ、距離は
        /// **両端ノード → 中点 → 両端ノードの折れ線**で測る（<see cref="VolcanoScan"/>）。
        /// 中点 1 点で測っていた頃は、中心へ向かって伸びる幹線道路が取り除かれずに残り、
        /// **完成した山の中に平らな溝が残っていた**（全体レビュー I4）。
        /// </summary>
        private static int ClearSegments(VolcanoFootprint footprint, out bool capped,
                                         out float reachedRadius)
        {
            capped = false;

            // ★★ 建物側と同じ理由で初期値は 0（全体レビュー M7）。
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

            // ★ 実測した長さと合わなければ走らない（推測で走らない。設計書 §6）。
            //    合わないまま z*270+x で引くと、まったく別の場所の道路を壊す。
            //    **届いた半径は 0**（上の建物側と同じ理由）。ここへ来る前に
            //    ClearingPathAvailable が false になって着手そのものを断っているので、
            //    実際にはまず到達しない二重の保険である。
            if (grid.Length != GridSide * GridSide)
            {
                _lastFailure = "the road grid is not 270x270 in this build; no road was removed";
                return 0;
            }

            // ★ ノードのバッファ（折れ線判定）。読めなければ null のままで、
            //   VolcanoScan が中点 1 点の判定に落ちる（調査もまったく同じ）。
            NetNode[] nodes = VolcanoScan.NodeBuffer(nm);

            // ★ 建物側と同じ「このパスが言える半径の上限」（全体レビュー M8）。
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
                    // ★ 次の ID は**行動する前に**控える。解放はこのセルの連結リストを
                    //    その場で繋ぎ替えるので、控えないと残りが黙って飛ぶ。
                    ushort next = segments[id].m_nextGridSegment;

                    // ★ 述語は調査とまったく同じもの（VolcanoScan）。
                    if (VolcanoScan.IsCandidate(segments[id].m_flags)
                        && VolcanoScan.SegmentInside(segments, nodes, id, origin, _frontRadius))
                    {
                        // ★ 予算は**壊す直前**に見る（クラス doc の M16）。
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

                    // ★★ 建物側と同じ理由で、**1 本でも解放できたときだけやり直す**。
                    //    解放された道路は配列から消えるのでやり直しは有限回で終わる。
                    //    断られる道路（SupportCableAI と、所有建物が倒壊を断った
                    //    Untouchable）はこの先も断られるので、そこで足踏みしない。
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
        /// この 1 周が「走査し終えた」と言ってよい半径の上限（m）。**全体レビュー M8。**
        ///
        /// カーソルが 0 でない ＝ 前のパスが上限で打ち切られ、内側のリングは
        /// <b>そのときの前線</b>で走査済みである。前線はその後も伸びるので、
        /// 今の前線で「一周した」と言うと、内側のリングにある
        /// 「前回の前線より外・今の前線より内」の建物と道路が**走査されないまま
        /// 済んだことになる**。したがって上限は関わった前線の最小値である。
        ///
        /// 一周し切ってカーソルが 0 に戻ると呼び出し側が控えを捨てるので、
        /// 次のパスは今の前線をそのまま名乗れる（自己修復する）。
        /// </summary>
        private static float PassFront(int cursor, ref float carried)
        {
            float front = _frontRadius;

            if (cursor > 0 && carried > 0f && carried < front) front = carried;

            carried = front;
            return front;
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
