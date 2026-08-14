using ColossalFramework;
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
    /// ── **どちらを採るか（第 2 層レビュー I2 で入れ替えた）** ──────────────
    ///
    /// 当初は「ゲーム自身が建物の高さとして使っている値」という理由で
    /// <c>m_collisionHeight</c> を主にしていた。**それは誤りだった。**
    /// <c>CheckReferences</c> は敷地上の<b>樹木</b>の高さまで取り込んでいる
    /// （本修正で IL 実測）:
    ///
    /// ```
    /// BuildingInfo::CheckReferences
    ///   IL_0019-0025  m_collisionHeight = m_size.y                （出発点）
    ///   IL_0270-02F6  h = prop.m_generatedInfo.m_center.y
    ///                     + prop.m_generatedInfo.m_size.y * 0.5
    ///                 h *= prop.m_maxScale
    ///                 if (m_props[i].m_fixedHeight) h += m_props[i].m_position.y
    ///                 m_collisionHeight = Mathf.Max(m_collisionHeight, h)   ※プロップ
    ///   IL_03EA-0470  同じ式を **TreeInfo** について繰り返す（m_finalTree /
    ///                 TreeInfoGen::m_center・m_size / TreeInfo::m_maxScale）  ※樹木
    /// ```
    ///
    /// バニラの低密度住宅の敷地には樹木プロップが載っており、
    /// <c>TreeInfoGen.m_size.y</c> は <c>m_maxScale</c> を掛ける前で 15〜25 m に
    /// 達しうる。つまり<b>平屋が 20 m 以上を名乗り、長周期の候補になってしまう</b>。
    /// この機能の前提そのもの（「高層ほど倒れる」）と、実機チェックリスト項目 82
    /// （同じ距離で高層が低層より多く倒れること）が、**樹木がでっち上げた高さ**で
    /// 測られることになる。
    ///
    /// **したがって主は <c>BuildingInfo.m_size.y</c>（メッシュ境界）にする。**
    /// この値が汚れていないことも IL で確定させた:
    ///
    /// ```
    /// BuildingInfo::InitializePrefab  IL_09BE  m_size = m_generatedInfo.m_size
    /// BuildingInfoBase::CalculateGeneratedInfo(MeshFilter[], SkinnedMeshRenderer[])
    ///   IL_0135-014A  y = Mathf.Max(y, mesh.vertices[k].y)   ← メッシュ頂点だけ
    ///   IL_05E6       m_generatedInfo.m_size = new Vector3(x*2, y, z*2)
    /// アセンブリ全体で BuildingInfo::m_size に stfld するのは InitializePrefab だけ、
    /// BuildingInfoGen::m_size に stfld するのは CalculateGeneratedInfo だけ（全走査で確認）。
    /// ```
    ///
    /// **<c>m_collisionHeight</c> へは退避しない。** 退避が要るのは
    /// <c>m_size.y</c> が使えないときだけで、その状況では <c>m_collisionHeight</c> の
    /// 出発点（IL_0019）も同じ使えない値なので、そこから <c>Max</c> で残るのは
    /// **プロップと樹木がでっち上げた高さそのもの**になる。つまり退避が効く唯一の
    /// 場面で、退避先が返すのは嘘である。読めなければ 0（＝不明）を返し、
    /// 呼び出し側は何もしない。
    ///
    /// **副作用（引き受ける）:** <c>m_size.y</c> はサブ建物を含まないので、
    /// 本体メッシュが低くサブ建物で高さを出しているプレハブは低く出る。
    /// 過小評価は「被害を与えない」側に倒れるので、過大評価より安全である。
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

                // ★ メッシュ境界の高さ。**m_collisionHeight は使わない**
                //   （クラス doc「どちらを採るか」。敷地の樹木で膨らむ）。
                float meshHeight = info.m_size.y;
                if (!float.IsNaN(meshHeight) && meshHeight > 0f) return meshHeight;

                // 予備経路は m_size の出所そのもの（InitializePrefab IL_09BE）だけ。
                // m_size が書かれていないプレハブでも、生成情報が残っていれば読める。
                var generated = info.m_generatedInfo;
                if (generated == null) return 0f;

                float generatedHeight = generated.m_size.y;
                return !float.IsNaN(generatedHeight) && generatedHeight > 0f
                    ? generatedHeight : 0f;
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
    /// （<see cref="_cursorOrdinal"/>）。選定は (地震, 建物) だけで決まり
    /// フレームを混ぜないので、途中で切っても結論は変わらない。
    ///
    /// **走査の順序は震央から外側へ**（<see cref="OutwardCellOrder"/>）。行優先だと
    /// 最初に見るのが矩形の角＝震央からいちばん遠い＝確率がほぼ 0 の場所になり、
    /// 上限が**いちばん壊れやすい建物を切り捨てる**（第 2 層レビュー I1。
    /// 順序を発明した理由の全文は <see cref="OutwardCellOrder"/> のクラス doc）。
    ///
    /// 参考: バニラ自身の全体円盤は <c>preRadius + 72</c> ＝ 最大 7172 m の
    /// グリッド走査を **1 シミュレーションステップごとに**、上限なしで行う（§A-3）。
    /// ここの上限はそれよりはるかに保守的である。
    ///
    /// **地震が変わった直後の 1 tick は走らせない。** <c>SeismographRecorder.Rescan</c> が
    /// 地震の開始 tick に建物バッファの全スロット走査を行うので、そこへ重ねない。
    /// ただし<b>間隔の累積（<see cref="_minutesSincePass"/>）は巻き戻さない</b> ——
    /// 地震が複数同時に進行して <c>SelectDamaging</c> の選定が入れ替わり続けると、
    /// 巻き戻す実装では累積が毎回 0 に戻り、**走査が 1 度も走らない**（第 2 層レビュー I3）。
    ///
    /// ── 対象は「今いちばん強い地震」1 個だけ（明示する）─────────────────
    ///
    /// <c>QuakeSelection.SelectDamaging</c> が選ぶ 1 個だけを追う。同時に 2 個以上の
    /// 地震が Active でも、追加被害を受けるのは選ばれた 1 個の周りだけである
    /// （<c>TsunamiChain</c> と同じ制限で、あちらと同じくクラス doc で名乗る）。
    /// 選定が入れ替わったときは走査の位置（<see cref="_cursorOrdinal"/>）を捨てて
    /// 新しい震央から測り直し、その事実を診断へ 1 行出す。
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
        /// 1 セルの連結リストを辿る回数の上限（壊れた保存データ対策）。
        /// バニラの <c>DisasterHelpers.DestroyBuildings</c> の内側ループと同じ
        /// 49152 ＝ 建物バッファの大きさ（IL_0521 の <c>ldc.i4 49152</c>）。
        /// </summary>
        private const int GridChainGuard = 49152;

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

        /// <summary>
        /// 次に見るセルの序数（<see cref="OutwardCellOrder"/> の順序）。0 が震央のセル。
        /// 上限で打ち切られたときだけ 0 以外で残る。
        /// </summary>
        private static int _cursorOrdinal;

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
            _cursorOrdinal = 0;
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
            // ★ 間隔の累積は**対象の地震より先に**進める。地震が入れ替わっても、
            //    無くなっても、時間は流れているという扱いにする。ここを地震ごとの
            //    状態にすると、複数の地震が Emerging/Active を出入りするたびに
            //    累積が 0 に戻り、走査が永久に走らなくなる（第 2 層レビュー I3）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;

            // 累積は間隔ぶんで頭打ちにする。地震が 1 個も無い時間が何時間続いても
            // 値が伸び続けない（float の桁を食わせない）。頭打ちにしても
            // 「間隔に達している」という結論は変わらない。
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

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
                // ★ 地震が変わった tick は走らせない。SeismographRecorder.Rescan が
                //    同じ tick で建物バッファの全スロット走査を行うので、そこへ重ねない。
                //    **累積は巻き戻さない**（上のコメント）。走査の位置だけ捨てる ——
                //    震央が変われば矩形もリングの中心も別物なので、続きから再開する
                //    意味が無い。
                ushort previous = _quakeId;
                Forget();
                _quakeId = quake.DisasterId;

                // 黙って乗り換えない。「対象は 1 個だけ」という制限（クラス doc）が
                // 効いた瞬間はログに残す。Log.Diag はキーごとにスロットルされる。
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "longPeriodSwitch",
                    "damaging quake changed #" + previous + " -> #" + quake.DisasterId
                    + "; sweep position discarded, interval carried over");
                return;
            }

            // 換算が取れないときは走らない（間隔が決まらない）。累積は上で
            // 進んでいるので、取れるようになった tick からふつうに動き出す。
            if (framesPerMinute <= 0f) return;
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

        /// <summary>
        /// 監視をやめる。**間隔の累積（<see cref="_minutesSincePass"/>）は触らない**
        /// —— あれは地震ではなく時間の状態で、地震の出入りで巻き戻すと走査が
        /// 走らなくなる（クラス doc / 第 2 層レビュー I3）。カウンタは診断のために残す。
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _cursorOrdinal = 0;
        }

        /// <summary>
        /// 1 回ぶんの走査。上限に達したら打ち切り、次回は <see cref="_cursorOrdinal"/> から
        /// 再開する（クラス doc の「1 tick あたりの仕事量の上限」）。
        ///
        /// **セルを見る順序は震央から外側へ**（<see cref="OutwardCellOrder"/>）。
        /// 1 周し終えたら序数を 0 に戻して震央から測り直す —— <see cref="IsSelected"/> は
        /// フレームもセル順も混ぜないので同じ建物は同じ結論になり、倒れた建物は
        /// <see cref="CandidateMask"/> の <c>Collapsed</c> で落ちる。
        /// </summary>
        private static void Sweep(EarthquakeReading quake, int strength, float timeFactor)
        {
            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので、exists で先に
            //    確認する（Log.CurrentFrame と同じ理由。TsunamiChain / EarthquakeReader も同様）。
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
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

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // リングの中心は震央のセル。矩形と同じクランプを掛けるので、震央が
            // マップの外でも中心は必ずグリッドの中に落ちる。
            int centreX = Clamp((int)(epicentre.X / 64f + 135f));
            int centreZ = Clamp((int)(epicentre.Z / 64f + 135f));
            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(quake.DisasterId);

            int scanned = 0, selected = 0, attempted = 0, refused = 0, collapsed = 0;
            int unknownHeight = 0, cells = 0;
            bool capped = false;

            int ordinal = _cursorOrdinal;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;
            int startOrdinal = ordinal;

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

                int x = centreX + dx;
                int z = centreZ + dz;

                // ★ 矩形の外は**数えずに飛ばす**（OutwardCellOrder のクラス doc）。
                //    数えると上限がはみ出しぶんだけ目減りし、診断の cells が
                //    「実際に見たセル数」でなくなる。
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ 次の ID は**行動する前に**控える。バニラの
                    //    DisasterHelpers.DestroyBuildings も同じ形で、ループの先頭
                    //    （IL_00E2）で m_nextGridBuilding をローカルへ写し、
                    //    CollapseBuilding を 2 回呼んだ後の IL_0516 でそれを使う。
                    //    バニラの CollapseBuilding 実装に ReleaseBuilding を呼ぶものは
                    //    無いので今日は同値だが、倒壊で建物を解放するサードパーティの
                    //    AI が居ると buildings[id] が 0 で埋まり、**このセルの残りが
                    //    黙って飛ぶ**。ローカル 1 個で塞げる。
                    ushort next = buildings[id].m_nextGridBuilding;

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

                    id = next;

                    // 連結リストが壊れている保存データで無限ループしないための保険。
                    // 上限はバニラの内側ループと同じ 49152（＝建物バッファの大きさ。
                    // DestroyBuildings IL_0521）。1 セルにそれ以上並ぶことはありえない。
                    if (++guard > GridChainGuard) break;
                }
            }

            // 1 周し終えていれば次回は震央から。打ち切りなら続きから。
            _cursorOrdinal = ordinal >= ordinalCount ? 0 : ordinal;
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
                // 走査は震央のセル（序数 0）から外へ。次回の再開点も出す
                // ——「震央まで届いていない」を診断から見えるようにするため。
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
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
        /// <c>InstanceManager</c> がまだ居なければ <c>null</c>（<c>Group</c> は参照型で、
        /// バニラ自身も <c>null</c> を渡す経路を持つ。集計が積まれないだけで倒壊は走る）。
        /// <c>DisasterAI.CreateDisaster</c> が <c>m_ownerInstance.Disaster = 災害ID</c> で
        /// 作って <c>InstanceManager</c> に登録している（③で IL 確認済み）。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            // new GameObject を走らせる main スレッド専用 API（Log.CurrentFrame の doc）。
            // ここは sim スレッドなので exists で先に確認する。
            if (!Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
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
                    "no building height could be read (BuildingInfo.m_size.y and "
                    + "m_generatedInfo.m_size.y were both unusable); long-period damage is "
                    + "applying nothing rather than guessing a height");
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
