using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 建物の高さ（m）を読む唯一の場所。**読めなければ 0 を返す。**
    ///
    /// ── IL で確定させたこと（Task 10 Step 1。設計書 付録の未確定項目） ─────────
    ///
    /// 設計書は「建物高さは <c>Building.Info.m_generatedInfo</c> 系から取る。
    /// **具体的なフィールド名を IL で確定してから書く**」としていた。実測した結果:
    ///
    /// ```
    /// Building（構造体）に高さのフィールドは無い。
    ///   実在するのは m_baseHeight（Byte、地形側の基準高）、
    ///   m_width / m_length（Byte、8 m セル数）。**m_height は存在しない。**
    ///
    /// 高さはプレハブ側にある:
    ///   BuildingInfoGen.m_size / m_min / m_max : Vector3   （メッシュ境界、m）
    ///   BuildingInfo.m_size                    : Vector3
    ///   BuildingInfo.m_collisionHeight         : Single
    ///
    /// BuildingInfo.InitializePrefab
    ///   IL_01EE  m_generatedInfo.m_size == Vector3.zero -> PrefabException("Generated info has zero size")
    ///   IL_09B3  m_size = m_generatedInfo.m_size
    ///   IL_0AC2  m_collisionHeight = m_size.y
    /// BuildingInfo.CheckReferences
    ///   IL_0019  m_collisionHeight = m_size.y
    ///   IL_02E8  m_collisionHeight = Max(m_collisionHeight, プロップ上端)   ※ サブ建物ぶんも同様
    /// ```
    ///
    /// **単位がメートルであることの決め手**（<c>CommonBuildingAI.CollapseIfFlooded</c>、IL_0031–0053）:
    ///
    /// ```
    /// if (TerrainManager.WaterLevel(XZ(m_position)) > m_position.y + Mathf.Max(4f, m_info.m_collisionHeight))
    ///     ... 水没で倒壊
    /// ```
    ///
    /// <c>m_position.y</c> はワールド座標（m）、<c>4f</c> もメートルなので、
    /// <c>m_collisionHeight</c> は**建物の基準面からの高さ（m）**で確定である。
    /// <c>BuildingInfo.IsMeshSmallOrMissing</c> が <c>m_min</c> / <c>m_max</c> を
    /// <c>m_cellWidth * 4</c>（＝ 8 m セルの半分）でクランプしていることも、
    /// この Vector3 がメートル系であることを裏づける。
    ///
    /// **信頼性:** <c>InitializePrefab</c> が <c>m_generatedInfo.m_size == zero</c> で
    /// <c>PrefabException</c> を投げるので、**初期化に成功した BuildingInfo は
    /// 必ず非ゼロの大きさを持つ**。ただし y はもともと低い建物（公園・装飾）では
    /// 小さく、それは読み取り失敗ではなく実測値である。
    /// <see cref="LongPeriodResponse.MinHeightMetres"/> 未満は対象外になる。
    ///
    /// **既知の粗さ:** <c>m_collisionHeight</c> はプロップとサブ建物の上端も含む。
    /// アンテナ 1 本で背が高いことになる建物がありうる。メッシュそのものの高さは
    /// <c>m_size.y</c> だが、そちらは <c>m_collisionHeight</c> の**出所**であり、
    /// 「ゲーム自身が建物の高さとして使っている値」は前者なので、そちらを採る。
    ///
    /// 計画は <c>MetresOf(ushort id, ref Building b)</c> という形を指定していたが、
    /// <b>高さはプレハブ側にしか無く、建物 ID は 1 度も要らない</b>ことが上の実測で
    /// 確定したので、使わない引数は置いていない。
    /// </summary>
    public static class BuildingHeight
    {
        /// <summary>
        /// この建物の高さ（m）。**読めなければ 0。** 呼び出し側は 0 を
        /// 「低い」ではなく「不明」として扱い、被害を一切与えないこと。
        /// </summary>
        public static float MetresOf(ref Building b)
        {
            try
            {
                var info = b.Info;
                // UnityEngine.Object の == オーバーロードで破棄済み(fake-null)も弾く。
                if (info == null) return 0f;

                float h = info.m_collisionHeight;
                if (!float.IsNaN(h) && h > 0f) return h;

                // 予備経路。m_collisionHeight の出所そのもの（InitializePrefab IL_0AC2）。
                // ここが 0 なら、この環境ではこの建物の高さは本当に分からない。
                float meshHeight = info.m_size.y;
                return !float.IsNaN(meshHeight) && meshHeight > 0f ? meshHeight : 0f;
            }
            catch
            {
                return 0f;
            }
        }
    }

    /// <summary>
    /// **第 2 層の 2 つ目。長周期地震動で高層建物を追加で倒す。**
    /// <b>sim スレッド専用。</b>既定 OFF。
    ///
    /// ── これは可視化ではない ─────────────────────────────────
    ///
    /// <see cref="LongPeriodResponse"/> のクラス doc のとおり、バニラの揺れには
    /// 長周期成分が無く、建物の高さは揺れにも被害にも入っていない（§A-7 / §A-3）。
    /// ここで倒れる建物は**バニラなら倒れなかった建物**である。だから既定 OFF で、
    /// 表示は必ず第 2 層（<c>Strings.SourceModel</c>）、強さのスライダーで 0 にできる。
    ///
    /// ── 足すだけ。抑えない ─────────────────────────────────
    ///
    /// バニラの破壊にはパッチも介入もしない。既に倒壊している建物は
    /// <c>CollapseBuilding</c> 自身が弾く（<c>CommonBuildingAI.CollapseBuilding</c>
    /// IL_0007: <c>m_flags &amp; 0x400000</c> で即 false）ので二重被害にはならない。
    ///
    /// ── <c>DisasterHelpers</c> を経由しない（§E-2）───────────────────
    ///
    /// Natural Disasters Renewal は <c>DisasterHelpers.DestroyBuildings</c> を
    /// Prefix で完全置換する。<c>BuildingAI.CollapseBuilding</c> を直接呼べば
    /// NDR のパッチ面 2 つを**完全に迂回できる**ので、あちらの破壊設定に
    /// 左右されずにこの機能だけが動く（③の火災旋風と同じ判断）。
    ///
    /// **<c>Building.m_fireIntensity</c> は絶対に直接書かない。** このフィールドを
    /// 消費するのは <c>CommonBuildingAI</c> の系だけで、それ以外の AI に書き込むと
    /// 誰も消さない永久の幽霊火災になり、**バニラの建物配列に入るのでセーブに焼き付き、
    /// MOD を外しても残る**。本プロジェクトは一度これを出荷している。
    /// ここで渡す <c>burnAmount</c> は <b>0</b>（長周期は「揺すられて潰れる」であって
    /// 焼損ではない）で、火勢の面倒はバニラ側が見る。
    ///
    /// ── 1 tick あたりの仕事量の上限（明示する）──────────────────────
    ///
    /// 走査が走るのは**地震が Active の間だけ**で、間隔は
    /// <see cref="IntervalFrames"/> フレームぶんの**経過ゲーム内時間**である
    /// （<c>frameIndex % N</c> にしない —— <c>m_currentFrameIndex</c> は 1 tick で
    /// <c>FinalSimulationSpeed</c>（1/3/9）進むので、剰余だとゲーム速度で
    /// 判定がまばらになる。火災旋風 設計書 付録 A-4）。
    ///
    /// 1 回の走査の上限は <b>グリッドセル <see cref="MaxCellsPerPass"/> 個</b>と
    /// <b>建物 <see cref="MaxBuildingsPerPass"/> 棟</b>。到達範囲は最大
    /// 2 × 7100 m ＝ 建物グリッド（270×270、1 セル 64 m）の全域になりうるので、
    /// 上限に達したら**そこで打ち切り、次の走査は続きから**再開する
    /// （<see cref="_cursorCell"/>）。選定は (地震, 建物) だけで決まり
    /// フレームを混ぜないので、途中で切っても結論は変わらない。
    ///
    /// 参考: バニラ自身の全体円盤は <c>preRadius + 72</c> ＝ 最大 7172 m の
    /// グリッド走査を **1 シミュレーションステップごとに**、上限なしで行う（§A-3）。
    /// ここの上限はそれよりはるかに保守的である。
    ///
    /// **地震が変わった直後は 1 間隔ぶん待つ。** <c>SeismographRecorder.Rescan</c> が
    /// 地震の開始 tick に建物バッファの全スロット走査を行うので、そこへ
    /// 重ねない（<see cref="Reset"/> / 地震 ID の切り替えで累積を 0 に戻す）。
    ///
    /// ── 診断（③の失敗を繰り返さない）───────────────────────────
    ///
    /// ③では「延焼が動いているか診断から一切見えない」まま欠陥を出荷した。
    /// ここは <c>scanned</c> / <c>selected</c> / <c>attempted</c> / <c>refused</c> /
    /// <c>collapsed</c> を必ず持ち、**倒壊 0 のときも 1 行出す** ——
    /// 「壊れていない」と「近くに対象が無い」がログで区別できなくなるからである。
    /// </summary>
    public static class LongPeriodDamage
    {
        /// <summary>
        /// 走査の間隔（フレーム相当のゲーム内時間）。バニラの災害 1 個あたりの
        /// <c>SimulationStep</c> 間隔と揃えてある。
        /// </summary>
        private const int IntervalFrames = 256;

        /// <summary>1 回の走査で見るグリッドセルの上限。到達範囲は全域になりうる。</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>1 回の走査で調べる建物の上限。</summary>
        private const int MaxBuildingsPerPass = 2048;

        /// <summary>建物グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>
        /// 候補にするフラグ条件。**バニラの <c>DestroyBuildings</c> の一次カリングと
        /// 同じマスク・同じ比較**（§A-3、<c>(m_flags &amp; 0x80013) != 1</c>）に、
        /// <c>Collapsed</c> の除外を足したもの。
        ///
        /// <c>Collapsed</c> を弾くのは <c>CollapseBuilding</c> が必ず false を返すからで、
        /// 弾かないと成功した跡地の瓦礫が毎回 refused に積まれ、診断の数字が読めなくなる
        /// （<c>FireWhirlDamage.CollectMask</c> と同じ理由）。
        ///
        /// **炎上中は弾かない。** <c>CommonBuildingAI.CollapseBuilding</c> は火勢を見ずに
        /// 倒壊させる（IL 実測）。燃えている高層が長周期で潰れるのは、この機能が
        /// 表現したい挙動そのものである。
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>Degraded の自己申告キー（<c>FeatureHost.NoteDegraded</c>）。</summary>
        private const string HeightNoteKey = "eqLongPeriodHeight";

        private static float _minutesSincePass;
        private static ushort _quakeId;
        private static int _cursorCell;
        private static bool _heightNotePosted;
        private static bool _errorLogged;

        // ── 診断カウンタ（全て sim スレッドからのみ読み書きする）──────────────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastSelected;
        private static int _lastAttempted;
        private static int _lastRefused;
        private static int _lastCollapsed;
        private static int _lastUnknownHeight;
        private static bool _lastCapped;
        private static int _totalCollapsed;

        /// <summary>これまでに走った走査の回数（セッション累計）。</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>直近 1 回で調べた建物数（候補マスクを通り、範囲内にあったもの）。</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>直近 1 回で確率選定を通った棟数。</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>直近 1 回で「バニラが dry-run で受け付ける」と答えた棟数。</summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>直近 1 回で「バニラが設計上断る」と答えた棟数。</summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>直近 1 回で実際に倒壊した棟数。</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>
        /// 直近 1 回で**高さが読めなかった**棟数。0 でないこと自体が異常の合図で、
        /// この建物には追加被害を一切与えていない。
        /// </summary>
        public static int LastUnknownHeight { get { return _lastUnknownHeight; } }

        /// <summary>直近 1 回が上限で打ち切られたか（続きは次回）。</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>セッション累計の倒壊棟数。</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>**レベルアンロードで必ず呼ぶ。** 都市をまたいで何も持ち越さない。</summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _quakeId = 0;
            _cursorCell = 0;
            _passes = 0;
            _lastScanned = 0;
            _lastSelected = 0;
            _lastAttempted = 0;
            _lastRefused = 0;
            _lastCollapsed = 0;
            _lastUnknownHeight = 0;
            _lastCapped = false;
            _totalCollapsed = 0;
            if (_heightNotePosted)
            {
                _heightNotePosted = false;
                FeatureHost.ClearDegraded(EarthquakeFeature.FeatureName, HeightNoteKey);
            }
            // _errorLogged は戻さない。「投げる」はこの DLL が参照しているゲームの
            // ビルドに対する事実であって、都市ごとの状態ではない
            // （EarthquakeReader._readErrorLogged と同じ判断）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>EarthquakeFeature.OnSimulationTick</c> の
        /// ポーズガードより下から呼ぶこと**（ポーズ中に建物が倒れる）。
        /// 設定が OFF のときは呼び出し側が呼ばない。
        /// </summary>
        public static void Apply(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            if (snapshot == null || !snapshot.Valid) return;

            try
            {
                Step(snapshot, deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("long-period damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqLongPeriod", "long-period damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            // 破壊が実際に走っている地震だけを対象にする。バニラの全体円盤の
            // DestroyBuildings は SimulationStep の **Active 分岐にしか無い**（§A-3）ので、
            // 本震前（Emerging）に建物を潰すと、揺れる前に倒れることになる。
            var quake = QuakeSelection.SelectDamaging(snapshot.Quakes);
            if (quake == null || quake.Phase != EarthquakePhase.Active)
            {
                if (_quakeId != 0) Forget();
                return;
            }

            if (quake.DisasterId != _quakeId)
            {
                // ★ 地震が変わった直後は 1 間隔ぶん待つ。SeismographRecorder.Rescan が
                //    同じ tick で建物バッファの全スロット走査を行うので、そこへ重ねない。
                Forget();
                _quakeId = quake.DisasterId;
                return;
            }

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            float interval = IntervalFrames / framesPerMinute;
            if (_minutesSincePass < interval) return;

            // 余りを繰り越さない。ロード直後などに大きな deltaMinutes が来ても
            // 次 tick に連続発火せず「間隔ごとに 1 回」を保つ（FireWhirlDamage と同じ）。
            _minutesSincePass = 0f;

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            if (strength < 0) strength = 0;

            // ★ 時間帯係数（Task 11）。**第 2 層の追加被害にだけ掛かる唯一の適用点**で、
            //    バニラの被害にも第 1 層の表示にも触れない（TimeOfDayFactor のクラス doc）。
            //    時刻は sim スレッドの m_dayTimeFrame 由来で、main スレッドが書く
            //    m_currentDayTimeHour ではない（§F-1。EarthquakeReader が読んでいる）。
            //
            //    **日夜サイクル OFF でも式は変えない。** 時刻が 12.0 に固定される
            //    （§F-1）ので係数は自然に 1.0 になる。特別扱いの分岐を足すと
            //    「日夜 OFF のときだけ別の道を通る」という検証しにくい経路が増える。
            //    その事実はパネルと診断ダンプが名乗る（Strings.EarthquakeNoDayNight）。
            float timeFactor = TimeOfDayFactor.Of(snapshot.HourOfDay);

            Sweep(quake, strength, timeFactor);
        }

        /// <summary>監視をやめて累積も巻き戻す。カウンタは診断のために残す。</summary>
        private static void Forget()
        {
            _quakeId = 0;
            _cursorCell = 0;
            _minutesSincePass = 0f;
        }

        /// <summary>
        /// 1 回ぶんの走査。上限に達したら打ち切り、次回は <see cref="_cursorCell"/> から
        /// 再開する（クラス doc の「1 tick あたりの仕事量の上限」）。
        /// </summary>
        private static void Sweep(EarthquakeReading quake, int strength, float timeFactor)
        {
            var bm = BuildingManager.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            float range = LongPeriodResponse.RangeOf(quake.Intensity);
            var epicentre = quake.Epicentre.ToVec2();

            // 建物グリッドは 1 セル 64m、270x270。境界をはみ出さないようクランプする
            // （バニラの DestroyBuildings と同じセル 64・オフセット 135・[0,269]）。
            int minX = Clamp((int)((epicentre.X - range) / 64f + 135f));
            int maxX = Clamp((int)((epicentre.X + range) / 64f + 135f));
            int minZ = Clamp((int)((epicentre.Z - range) / 64f + 135f));
            int maxZ = Clamp((int)((epicentre.Z + range) / 64f + 135f));

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            int cellCount = width * height;
            if (cellCount <= 0) return;
            if (_cursorCell >= cellCount) _cursorCell = 0;

            var group = GroupOf(quake.DisasterId);

            int scanned = 0, selected = 0, attempted = 0, refused = 0, collapsed = 0;
            int unknownHeight = 0, cells = 0;
            bool capped = false;
            int cell = _cursorCell;

            while (cells < cellCount)
            {
                if (cells >= MaxCellsPerPass || scanned >= MaxBuildingsPerPass)
                {
                    capped = true;
                    break;
                }

                int x = minX + cell % width;
                int z = minZ + cell / width;
                int index = z * GridSide + x;

                if (index >= 0 && index < grid.Length)
                {
                    ushort id = grid[index];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            float d = Distance(epicentre, p.x, p.z);
                            if (d < range)
                            {
                                scanned++;
                                float metres = BuildingHeight.MetresOf(ref buildings[id]);
                                if (metres <= 0f)
                                {
                                    // ★ 高さが分からない建物には**何もしない**。
                                    //    推測した高さで「高層ほど壊れる」を適用したら、
                                    //    それはこの MOD が最も嫌う形の嘘になる。
                                    unknownHeight++;
                                }
                                else if (IsSelected(quake, id, metres, d, strength, timeFactor))
                                {
                                    selected++;
                                    bool accepted;
                                    if (Collapse(buildings, id, group, out accepted)) collapsed++;
                                    if (accepted) attempted++; else refused++;
                                }
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // 連結リストが壊れている保存データで無限ループしないための保険。
                        if (++guard > 32768) break;
                    }
                }

                cells++;
                cell++;
                if (cell >= cellCount) cell = 0;
            }

            _cursorCell = cell;
            _passes++;
            _lastScanned = scanned;
            _lastSelected = selected;
            _lastAttempted = attempted;
            _lastRefused = refused;
            _lastCollapsed = collapsed;
            _lastUnknownHeight = unknownHeight;
            _lastCapped = capped;
            _totalCollapsed += collapsed;

            UpdateHeightNote(scanned, unknownHeight);

            // ★ collapsed > 0 で囲ってはいけない。「機能が死んでいる」と
            //    「近くに高層が無い」がログ上で区別できなくなる（③で実際に起きた形）。
            //    Log.Diag はキーごとにスロットルされるので毎回書いても溢れない。
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "longPeriod",
                "pass#" + _passes + " quake#" + quake.DisasterId
                + " strength=" + strength
                + " timeFactor=" + timeFactor.ToString("F2")
                + " range=" + range.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                + " scanned=" + scanned + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " collapsed=" + collapsed
                + " unknownHeight=" + unknownHeight
                + (capped ? " (capped; resumes next pass)" : ""));
        }

        /// <summary>
        /// この建物を選ぶか。**乱数は <see cref="DeterministicRandom"/>**
        /// （<c>VanillaRandomizer</c> ではない）——これは**この MOD が発明した判断**で、
        /// バニラが引く値と一致する必要が無い。むしろ一致させると、第 1 層で
        /// 先読みしたしきい値と混ざって、どちらの層の結論なのかが追えなくなる。
        ///
        /// **フレームを混ぜない。** 混ぜると同じ建物が走査のたびに抽選し直され、
        /// 時間とともに壊れる建物が際限なく増える。
        /// </summary>
        private static bool IsSelected(EarthquakeReading quake, ushort buildingId,
                                       float heightMetres, float distance, int strength,
                                       float timeFactor)
        {
            float chance = LongPeriodResponse.ExtraCollapseChance(
                heightMetres, distance, quake.Intensity, strength);
            // 時間帯係数は上限（MaxExtraChance）の**後**に掛かる。上限は
            // 「このモデル自身が出す最大値」という意味のままにしておきたいので、
            // ここで再クランプはしない（最大 0.25 x 1.15 = 0.2875）。
            chance *= timeFactor;
            if (chance <= 0f) return false;

            float roll = DeterministicRandom.Unit(quake.DisasterId, buildingId);
            return roll < chance;
        }

        /// <summary>
        /// 実際に倒す。**<c>DisasterHelpers</c> を経由しない**（クラス doc）。
        ///
        /// dry-run（<c>testOnly: true</c>）は副作用の無い問い合わせである
        /// （<c>CommonBuildingAI.CollapseBuilding</c> IL_0013: <c>testOnly</c> なら
        /// 書き込みの手前で <c>ldc.i4.1; ret</c>）。**ただし dry-run が false でも
        /// 本番は呼ぶ** —— <c>PowerPoleAI.CollapseBuilding</c> は
        /// <c>if (testOnly) return false;</c> の直後に本物の倒壊を行う（IL 実測）ので、
        /// dry-run を信じて呼ばないと本物の倒壊を握り潰すことになる
        /// （<c>FireWhirlDamage.Ignite</c> と同じ判断）。
        /// </summary>
        /// <param name="accepted">
        /// バニラ自身が dry-run に「受け付ける」と答えたか。
        /// <c>FireWhirlDamage</c> の attempted と同じ定義で、空振り検出の証拠に
        /// なるのはこの数だけである。
        /// </param>
        private static bool Collapse(Building[] buildings, ushort id,
                                     InstanceManager.Group group, out bool accepted)
        {
            accepted = false;

            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            var ai = info.m_buildingAI;

            // demolish: false（瓦礫を残す）、burnAmount: 0（揺すられて潰れるのであって
            // 焼損ではない）。m_fireIntensity には触れない。
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// 災害グループ。渡すとバニラ側の集計（災害ごとの被害棟数）が正しく積まれる。
        /// <c>DisasterAI.CreateDisaster</c> が <c>m_ownerInstance.Disaster = 災害ID</c> で
        /// 作って <c>InstanceManager</c> に登録している（③で IL 確認済み）。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return InstanceManager.instance.GetGroup(groupId);
        }

        /// <summary>
        /// 「高さが 1 棟も読めなかった」を自己申告する。**黙って何もしない状態を作らない。**
        /// 高さが読めない環境ではこの機能は正しく何もしないが、それは
        /// 「効いていない」と見分けが付かないので、必ず名乗る。
        /// </summary>
        private static void UpdateHeightNote(int scanned, int unknownHeight)
        {
            bool broken = scanned > 0 && unknownHeight == scanned;
            if (broken == _heightNotePosted) return;

            _heightNotePosted = broken;
            if (broken)
            {
                FeatureHost.NoteDegraded(EarthquakeFeature.FeatureName, HeightNoteKey,
                    "no building height could be read (BuildingInfo.m_collisionHeight and "
                    + "m_size.y were both unusable); long-period damage is applying nothing "
                    + "rather than guessing a height");
            }
            else
            {
                FeatureHost.ClearDegraded(EarthquakeFeature.FeatureName, HeightNoteKey);
            }
        }

        private static float Distance(Vec2 epicentre, float x, float z)
        {
            float dx = x - epicentre.X;
            float dz = z - epicentre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > GridSide - 1) return GridSide - 1;
            return v;
        }
    }
}
