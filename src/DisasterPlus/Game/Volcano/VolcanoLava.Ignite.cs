using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="VolcanoLava"/> のうち、**地形を読む部分と、実際に火を付ける部分**。
    /// **sim スレッド専用。**
    ///
    /// 分割してあるのは 800 行の規則のためで、意味の境目でもある ——
    /// 本体（<c>VolcanoLava.cs</c>）が「いつ・どこへ進むか」を決め、
    /// こちらが「その地点で何を呼ぶか」を持つ。**判断は 1 つも増やさないこと。**
    ///
    /// ここに現れる API は 4 つだけである（§B-7 / §B-6 / §A-4）:
    ///
    /// <code>
    /// TerrainManager.SampleDetailHeight(Vector3, out float, out float)   高さと勾配
    /// TerrainManager.HasWater(Vector2)                                   水（sim 専用）
    /// DisasterHelpers.BurnGround(Vector2, float, float)                  地面の焦げ（DLC 不要）
    /// BuildingAI.BurnBuilding(ushort, ref Building, Group, bool)         建物（DLC 不要）
    /// TreeManager.BurnTree(uint, Group, int)                             樹木（★ ND 必須）
    /// </code>
    ///
    /// **道路を燃やす API はゲームに存在しない**（§B-7d。<c>BurnSegment</c> /
    /// <c>BurnNode</c> に相当するものが 1 つも無い）。溶岩の下の道路が残るのは仕様で、
    /// <c>Strings.VolcanoLavaRoadsNote</c> がそう名乗る。取り除かれるのは火山の範囲内の
    /// 道路だけで、それは準備の段（T5）で起きる。
    /// </summary>
    public static partial class VolcanoLava
    {
        /// <summary>建物グリッドの 1 辺のセル数（1 セル 64 m。②④ T5 と同じ実測値）。</summary>
        private const int BuildingGridSide = 270;

        /// <summary>建物グリッドの 1 セルの大きさ（m）。</summary>
        private const float BuildingGridCellSize = 64f;

        /// <summary>建物グリッドのセル座標のオフセット。</summary>
        private const float BuildingGridCellOffset = 135f;

        /// <summary>建物の連結リストが壊れていたときの保険（<c>m_buildings</c> の容量）。</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>1 歩で見る建物グリッドのセル数の上限（半径 60 m なら実際は高々 3×3）。</summary>
        private const int MaxBuildingCellsPerStep = 25;

        /// <summary>
        /// 1 歩で <c>BurnBuilding</c> を呼ぶ回数の上限。**全体レビュー M17。**
        ///
        /// 上限が<b>セル数だけ</b>だった頃、密集地の 1 セルにぶら下がる数百棟に対して
        /// 1 歩で数百回の呼び出しが出ていた（セルの上限 25 は「歩が見る範囲」を
        /// 縛るだけで、仕事量を縛っていない）。溶岩は 1 歩 12 m しか進まないのに
        /// 着火半径は最大 60 m なので、**同じ建物を 1 本の流れが 5 回前後、
        /// 8 本で最大 40 回叩く**。ここで縛るのは呼び出しそのものである。
        /// </summary>
        private const int MaxBuildingsPerStep = 48;

        /// <summary>
        /// 樹木グリッドの 1 辺のセル数。<c>TreeManager.Awake</c> / <c>InitializeTree</c> の
        /// IL 実測（<c>TREEGRID_RESOLUTION = 540</c>、添字は <c>z*540 + x</c>）。
        /// </summary>
        private const int TreeGridSide = 540;

        /// <summary>樹木グリッドの 1 セルの大きさ（m）。<c>TREEGRID_CELL_SIZE = 32</c>。</summary>
        private const float TreeGridCellSize = 32f;

        /// <summary>
        /// 樹木グリッドのセル座標のオフセット。**本タスクで IL から導出した**
        /// （建物グリッドの 135 とは別の値なので、写し間違えないこと）:
        ///
        /// <code>
        /// TreeInstance.set_Position（ゲームモード）: m_posX = world * 3.792593
        /// TreeManager.InitializeTree               : cell = (m_posX + 32768) * 540 / 65536
        ///   => cell = world * 3.792593 * 540 / 65536 + 270 = world / 32 + 270
        /// </code>
        ///
        /// （<c>ToolController.m_mode == 4</c>（アセットエディタ）のときだけ
        /// <c>m_posX</c> の縮尺が 16 倍になり、<c>InitializeTree</c> 側も先に 16 で割る。
        /// **ゲームモードでは上の式でよい。**）
        /// </summary>
        private const float TreeGridCellOffset = 270f;

        /// <summary>樹木の連結リストが壊れていたときの保険（<c>m_trees</c> の容量）。</summary>
        private const int TreeChainGuard = 262144;

        /// <summary>1 歩で見る樹木グリッドのセル数の上限（半径 60 m なら実際は高々 5×5）。</summary>
        private const int MaxTreeCellsPerStep = 49;

        /// <summary>
        /// 1 歩で <c>BurnTree</c> を呼ぶ回数の上限（建物側と同じ理由。全体レビュー M17）。
        /// 樹木は 1 セル 32 m でグリッドが密なので、建物より多めに取る。
        /// </summary>
        private const int MaxTreesPerStep = 96;

        /// <summary>
        /// <c>BurnTree</c> に渡す強さ。**<c>conv.u1</c> で切り捨てられる（クランプされない）**
        /// ので、呼び出し側で <c>[128, 255]</c> に収める（§B-7c）。
        /// バニラの実値は <c>DisasterHelpers.DestroyTrees</c> が 128、
        /// <c>ForestFireAI</c> が <c>Min(255, 128 + intensity)</c>。⑤は溶岩なので強めの 192。
        /// </summary>
        private const int TreeFireIntensity = 192;

        private const int TreeFireIntensityMin = 128;
        private const int TreeFireIntensityMax = 255;

        /// <summary>
        /// 地形の高さ（m）と勾配を読む。**<c>slopeX</c> / <c>slopeZ</c> は上り方向**
        /// （本体のクラス doc の IL 実測）。呼び出し側が符号を反転して使う。
        ///
        /// <c>Physics.Raycast</c> は使わない —— **地形にコライダーは無く、必ず外れる**
        /// （§B-6 / 既知）。
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
        /// 水に触れたか（設計書 §4.5）。
        ///
        /// ★ <b><c>HasWater</c> は sim スレッド専用である。</b> IL は
        /// <c>m_waterSimulation.BeginRead()</c> / <c>EndRead()</c> を try/finally で
        /// 取っており（②の <c>TsunamiChain.IsUnderWater</c> と同じ扱い）、
        /// main スレッドから呼ぶと水シミュのバッファ交換と競合する。
        /// 判定は「水面 − 地形 &gt;= 8 raw 単位（= 0.125 m）」である。
        ///
        /// 読めなければ **false（水は無い）** に倒す —— 読めないことを理由に
        /// 溶岩を止めると、水の無いマップで溶岩が 1 歩も動かなくなる。
        /// </summary>
        private static bool HasWater(Vec2 p)
        {
            if (!Singleton<TerrainManager>.exists) return false;

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null) return false;

            return tm.HasWater(new Vector2(p.X, p.Z));
        }

        /// <summary>
        /// 購入していないタイルへ出たかを控える。**不具合ではない** ——
        /// <c>GetDetailHeight</c> が <c>m_simDetailIndex == 0</c> のとき
        /// <c>SampleFinalHeight</c> へ落ちるので、サンプリングが 4 m から
        /// 16 m 補間になる（§B-6）。段差は出ないが挙動は変わるので、診断に 1 行出す。
        ///
        /// <c>GameAreaManager.PointOutOfArea(Vector3)</c> は public instance で、
        /// 読み取りだけなので sim スレッドから安全に呼べる。
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
                // 分からなければ何も名乗らない。溶岩の挙動には影響しない。
            }
        }

        /// <summary>ND DLC を持っているか（樹木の着火だけがこれで分岐する。§B-7c）。</summary>
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
        /// 溶岩が通った 1 点で火を付ける。**設定で切れる**（切っても溶岩は流れる）。
        /// </summary>
        private static void Ignite(Vec2 p, float travelledMetres)
        {
            if (!ModSettings.VolcanoLavaFire.value) return;

            float radius = LavaPath.SpreadRadiusFor(travelledMetres);

            BurnGround(p, radius);
            IgniteBuildings(p, radius);

            // ★ 樹木だけ ND 分岐。非所持なら**燃やさず説明する**（ReleaseTree で
            //   消す代替はやらない。本体のクラス doc）。
            if (_treesAvailable) IgniteTrees(p, radius);
        }

        /// <summary>
        /// 地面を焦がす。**DLC 不要**（§B-7b）。
        /// <c>intensity</c> は <b>0.0–1.0 の正規化値</b>で、内部で ×255 される。
        /// 値は単調非減少なので、同じ場所を何度通っても薄くならない。
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
        /// 溶岩に触れた建物へ火を付ける。**<c>DisasterHelpers</c> を通らない**（§E-14）。
        ///
        /// 走査は**行優先**である（本体のクラス doc に理由がある。矩形は高々 3×3 セル）。
        /// 「次の ID を行動する前に控える」規律は②④ T5 と同じ ——
        /// 着火そのものは建物を解放しないが、規律を破る形のコードを残さない。
        ///
        /// 距離は建物の <c>m_position</c> で測る。大きな建物は角が半径の中にあっても
        /// 中心が外なら燃えないが、**それは概算であり、そう名乗るほうが
        /// 「全部燃える」と嘘をつくより正しい**（設計書 §7.2 と同じ判断）。
        ///
        /// ★★ <b>戻り値のあとで火勢のフィールドを書き足さない</b>（罠 5）。
        /// 火勢は <c>GetFireParameters</c> が建物ごとに決める。
        /// 断られる（水没中・瓦礫）のは正常なので <see cref="_buildingsRefused"/> に積むだけ。
        /// </summary>
        private static void IgniteBuildings(Vec2 p, float radius)
        {
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ 実測した長さと合わなければ走らない（推測で走らない。設計書 §6）。
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
                                // ★ 呼び出しそのものを縛る（全体レビュー M17）。
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
        /// 建物 1 棟に火を付ける。<c>group</c> は <c>null</c>（§G-16 (e)。⑤は災害スロットに
        /// 載らないので束ねる先が無く、バニラ側は null を検査している）。
        /// <c>testOnly: false</c> ＝ 本番。**dry-run はフィルタに使わない。**
        /// </summary>
        private static bool Burn(Building[] buildings, ushort id)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            return info.m_buildingAI.BurnBuilding(id, ref buildings[id], null, false);
        }

        /// <summary>
        /// 溶岩に触れた木へ火を付ける。**ND DLC が無いと必ず false になる**ので、
        /// 呼ぶ前に <c>_treesAvailable</c> で分岐している（§B-7c の DLC ゲート。
        /// ★ 全体レビュー M11 の追跡性の訂正: 事実文書は
        /// <c>SupportsExpansion</c> と書いているが、本 MOD がここで実際に評価するのは
        /// <c>ModCompat.NaturalDisastersOwned</c> ＝ <c>SteamHelper.IsDLCOwned</c> である。
        /// レベルがロードされている間、この 2 つは同じ答えを返す）。
        ///
        /// 一度燃えた木は二度と燃えない（<c>m_flags &amp; 64 FireDamage</c> を
        /// <c>BurnTree</c> 自身が見る）。**空振りを異常として数えない。**
        /// </summary>
        private static void IgniteTrees(Vec2 p, float radius)
        {
            if (!Singleton<TreeManager>.exists) return;

            var tm = Singleton<TreeManager>.instance;
            if (tm == null) return;

            var trees = tm.m_trees != null ? tm.m_trees.m_buffer : null;
            var grid = tm.m_treeGrid;
            if (trees == null || grid == null) return;

            // ★ 実測した長さと合わなければ走らない（設計書 §6）。
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

                        // Created(1) が立っていて Deleted(2) が立っていないものだけ。
                        // FireDamage(64) は BurnTree 自身が見るので、ここでは見ない。
                        if ((trees[id].m_flags & 3) == 1)
                        {
                            Vector3 tp = trees[id].Position;
                            float dx = tp.x - p.X;
                            float dz = tp.z - p.Z;

                            if (dx * dx + dz * dz <= radiusSquared)
                            {
                                // ★ 呼び出しそのものを縛る（全体レビュー M17）。
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
        /// <c>[128, 255]</c> に収める。**<c>BurnTree</c> は <c>conv.u1</c> で切り捨てる
        /// （クランプしない）**ので、256 は 0 に、300 は 44 になる（§B-7c）。
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
