using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風の風害。<b>sim スレッド専用。</b>既定 ON。
    ///
    /// ── これは可視化ではなく、まったく新しい物理である ──────────────────
    ///
    /// バニラには風による破壊機構が 1 つも無く、**風速を上げるフィールドすら存在しない**
    /// （IL 事実文書 §A-5 / §B5）。ここで倒れる建物は**バニラなら絶対に倒れなかった
    /// 建物**である。モデルの中身と「単位が無い」ことは
    /// <see cref="WindDamageModel"/> のクラス doc にある。
    ///
    /// ── 既定 ON にする理由（②の第 2 層とは判断が違う）────────────────
    ///
    /// ②の第 2 層は既定 OFF だった。「バニラに存在しない挙動を既定で入れると、
    /// プレイヤーは地震のあと勝手に津波が来る原因が MOD だと気付く手段を持たない」
    /// からである。**④にはその問題が無い。** 台風はプレイヤーが「台風を発生させる」を
    /// 押さない限り 1 個も起きないので、押した直後に起きることは全部台風に帰属する。
    /// ただし強さは <c>ModSettings.TyphoonWindStrength</c> で 0 まで下げられる。
    ///
    /// ── <c>DisasterHelpers</c> を経由しない（§F-1）───────────────────
    ///
    /// Natural Disasters Renewal は <c>DisasterHelpers.DestroyBuildings</c> /
    /// <c>DestroyNetSegments</c>（＋前者を呼ぶ <c>DestroyStuff</c>）を Prefix で完全置換し、
    /// <c>burnRadiusMin == 0 &amp;&amp; burnRadiusMax == 0</c> を「竜巻だ」、
    /// <c>probability == 0.02f</c> を「地震だ」と嗅ぎ分ける。
    /// <c>BuildingAI.CollapseBuilding</c> を直接呼べばそのパッチ面を**完全に迂回できる**。
    ///
    /// **<c>Building.m_fireIntensity</c> は絶対に直接書かない。** 消費するのは
    /// <c>CommonBuildingAI</c> の系だけで、それ以外の AI に書くと誰も消さない永久の
    /// 幽霊火災になり、**バニラの建物配列に入るのでセーブに焼き付き、MOD を外しても
    /// 残る**。本プロジェクトは一度これを出荷している。ここで渡す <c>burnAmount</c> は
    /// <b>0</b>（風は吹き飛ばして潰すのであって焼損ではない）。
    ///
    /// **拒否する AI はそのままにする**（§F-2）。<c>demolish: false</c> では
    /// Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy が
    /// 黙って false を返す。**防災施設が台風で壊れないのは正しい挙動なので、
    /// <c>demolish: true</c> へ逃げない。** これらは <see cref="LastRefused"/> に積まれるので、
    /// 診断で「壊れていない」と「壊せない」が区別できる。
    ///
    /// **dry-run が false でも本番は必ず呼ぶ。** <c>PowerPoleAI</c> /
    /// <c>CableCarPylonAI</c> は <c>if (testOnly) return false;</c> の直後に本物の倒壊を
    /// 行う（§F-2）。dry-run を信じて呼ばないと、実際には壊せる送電柱を取りこぼす。
    ///
    /// ── 1 tick あたりの仕事量の上限（明示する）──────────────────────
    ///
    /// 走査が走るのは台風が動いている間だけで、間隔は <see cref="IntervalFrames"/>
    /// フレームぶんの**経過ゲーム内時間**である（<c>frameIndex % N</c> にしない ——
    /// <c>m_currentFrameIndex</c> は 1 tick で <c>FinalSimulationSpeed</c>（1/3/9）進むので、
    /// 剰余だとゲーム速度で判定がまばらになる。③ 付録 A-4）。
    ///
    /// 1 回の走査の上限は<b>グリッドセル <see cref="MaxCellsPerPass"/> 個</b>と
    /// <b>建物 <see cref="MaxBuildingsPerPass"/> 棟</b>。強風域は暴風域の 2.2 倍あるので
    /// 矩形は②の地震よりさらに大きくなりうる。上限に達したら打ち切り、
    /// 次回は <see cref="_cursorOrdinal"/> から再開する。
    ///
    /// **走査の順序は台風の中心から外側へ**（<see cref="OutwardCellOrder"/>）。行優先だと
    /// 最初に見るのが矩形の角＝中心からいちばん遠い＝確率がほぼ 0 の場所になり、
    /// 上限が**いちばん壊れやすい建物を切り捨てる**（②の第 2 層レビュー I1）。
    ///
    /// ④に固有の違いが 1 つある: **中心が毎 tick 動く。** 矩形もリングの中心も毎回
    /// 変わるので、<see cref="_cursorOrdinal"/> を持ち越す意味は「同じ中心セルのまま
    /// 打ち切られた続き」しか無い。**中心のセルが前回と変わったら 0 に戻す。**
    /// ただし<b>間隔の累積（<see cref="_minutesSincePass"/>）は巻き戻さない</b> ——
    /// 巻き戻すと中心が動くたびに 0 に戻り、**走査が 1 度も走らない**
    /// （②の第 2 層レビュー I3 がまさにこれ）。
    ///
    /// ── 打ち切りの続きは、実際にはほぼ起きない（全体レビュー I1）──────────────
    ///
    /// <b>そしてその「持ち越し」は、動いている台風ではまず成立しない。</b>
    /// <c>TyphoonTrack.SpeedFor</c> は進行速度を <c>[0.25, 6]</c> m/frame に
    /// クランプし、走査の間隔は <see cref="IntervalFrames"/> ＝ 256 フレームぶんの
    /// ゲーム内時間である。つまり眼は 1 走査のあいだに **64〜1536 m** 動く ——
    /// グリッドのセルは 64 m なので、中心のセルはほぼ毎回変わり、
    /// <see cref="_cursorOrdinal"/> は 0 に戻る。
    ///
    /// **したがって「上限で打ち切られた外縁が次の走査で続きから判定される」ことは
    /// 起きない。** 次の走査はまた眼から始まり、内側のリングをもう一度舐める。
    /// 診断ダンプはかつて「次の走査で続きから」と書いていたが、それは嘘だったので
    /// 消した（パネルの文言はもともと続きに触れていない）。今の文言は
    /// 「この走査では外縁まで届かなかった」だけを言う。
    ///
    /// 仕組みそのものは残してある。最低速度で斜めに進むと、稀に中心のセルが
    /// 変わらない走査があり、そのときだけ本当に続きから走る。**上限は安全側に
    /// 外す方向**（判定されない ＝ 倒れない）なので、これで害は無い。
    /// リング半径で持ち越す形に変えることもできるが、それは「中心が動いても
    /// 内側は再抽選しない」という別の挙動になるので、実機で挙動を見る前に
    /// 入れ替えない。
    ///
    /// ── 進行方向右側の危険半円（持ち主の指摘）─────────────────────────
    ///
    /// > 台風直下の範囲内で、進行方向〔右〕側に被害半径や被害の確率を若干強化して
    ///
    /// 実在の台風は左右対称ではない。渦の回転と台風自身の移動が足し算になる側を
    /// 危険半円と呼び、北半球では**進行方向の右**である（<see cref="TrackBias"/>）。
    /// ④はそこで
    ///
    /// - **被害半径**を最大 +18 %: 風速の場そのものを引き伸ばす
    ///   （<c>WindAt(距離 ÷ RadiusFactor, …)</c>）。半径の定数は書き換えない
    /// - **倒壊確率**を最大 +30 %: <c>CollapseChance</c> の結果に掛ける
    ///
    /// **走査の矩形も同じ倍率だけ広げる。** 広げないと、伸びた側の外縁の建物が
    /// そもそも走査に入らず、半径を伸ばした意味が消える（例外の出ない壊れ方）。
    /// 広げるのは矩形だけで、**リングの順序（眼から外へ）は 1 ビットも変えない。**
    ///
    /// 偏りは<b>毎走査 <c>TyphoonController.HeadingRadians</c> を読み直す</b>ので、
    /// 経路が曲がればその場で回る。方位をここへキャッシュしないこと。
    ///
    /// 南半球（左が危険半円）は <c>ModSettings.TyphoonSouthernHemisphere</c> で切り替わる。
    ///
    /// ── 乱数にフレームを混ぜない ──────────────────────────────
    ///
    /// 選定は (台風 ID, 建物 ID) だけで決まる。混ぜると同じ建物が走査のたびに
    /// 抽選し直され、時間とともに壊れる建物が際限なく増える。
    ///
    /// **それでも被害は広がる。** ④は②と違い中心が動くので、台風が近づけば同じ建物の
    /// <c>wind</c> が上がり、<c>roll</c> が固定でも <c>chance</c> が上がっていずれ閾値を
    /// 超える。**これが「台風が来ると被害が広がる」の実装であり、乱数にフレームを
    /// 混ぜる必要が無い理由でもある。**
    ///
    /// ── 演出と倒木 ─────────────────────────────────────
    ///
    /// <c>DisasterHelpers.AddWind</c> / <c>DestroyTrees</c> はどちらも NDR の
    /// パッチ対象外（§F-1）。**本タスクで両方の宣言を IL で直接確認した**
    /// （§B-1 は <c>DestroyStuff</c> の転送から並びを導いていただけだった）:
    ///
    /// ```
    /// public static void DisasterHelpers.AddWind(
    ///     Vector3 position, float radius, Vector3 directionalWind,
    ///     float rotationalWind, float radialWind, InstanceManager.Group group)
    ///
    /// public static void DisasterHelpers.DestroyTrees(
    ///     int seed, InstanceManager.Group group, Vector3 position,
    ///     float totalRadius, float removeRadius,
    ///     float destructionRadiusMin, float destructionRadiusMax,
    ///     float burnRadiusMin, float burnRadiusMax)
    /// ```
    ///
    /// **<c>burnRadiusMin</c> / <c>burnRadiusMax</c> は 0 を渡す。** 台風で木が
    /// **燃える**のはおかしい（<c>TreeManager.BurnTree</c> も使わない）。
    /// </summary>
    public static partial class TyphoonWind
    {
        /// <summary>走査の間隔（フレーム相当のゲーム内時間）。②の長周期と同じ 256。</summary>
        private const int IntervalFrames = 256;
        /// <summary>1 回の走査で見るグリッドセルの上限。</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>1 回の走査で調べる建物の上限。</summary>
        private const int MaxBuildingsPerPass = 2048;

        /// <summary>建物グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>
        /// 1 セルの連結リストを辿る回数の上限（壊れた保存データ対策）。
        /// バニラの <c>DisasterHelpers.DestroyBuildings</c> の内側ループと同じ
        /// 49152 ＝ 建物バッファの大きさ。
        /// </summary>
        private const int GridChainGuard = 49152;

        /// <summary>
        /// 候補にするフラグ条件。②の <c>LongPeriodDamage.CandidateMask</c> と同じ。
        /// <c>Collapsed</c> を弾くのは <c>CollapseBuilding</c> が必ず false を返すからで、
        /// 弾かないと跡地の瓦礫が毎回 refused に積まれて診断の数字が読めなくなる。
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>倒木の <c>Degraded</c> 自己申告キー（<c>FeatureHost.NoteDegraded</c>）。</summary>
        private const string TreeNoteKey = "typhoonWindTrees";

        /// <summary>
        /// 倒木の破壊半径を強風域半径に対してどれだけ取るか。
        /// **④が選んだ数字。** 建物より広く、演出として木がまとまって倒れる程度。
        /// </summary>
        private const float TreeRadiusFraction = 0.5f;

        /// <summary>倒木の「確実に倒れる」内側半径（<see cref="TreeRadiusFraction"/> に対する比）。</summary>
        private const float TreeInnerFraction = 0.3f;

        /// <summary>
        /// <c>AddWind</c> の高さオフセットと半径倍率。§B-1 の竜巻の実引数を手本にした
        /// （<c>position.y + rMax * 0.75</c> / <c>radius = rMax * 1.5</c>）。
        /// </summary>
        private const float WindHeightFraction = 0.75f;

        private const float WindRadiusFactor = 1.5f;

        /// <summary>吹き上げの鉛直成分（§B-1 の竜巻の実引数と同じ 80）。</summary>
        private const float WindUpward = 80f;

        /// <summary>回転成分と求心成分（§B-1 の竜巻の実引数と同じ）。</summary>
        private const float WindRotational = 0.5f;

        private const float WindRadial = -40f;

        /// <summary>
        /// 進行方向の押しの強さ（m/frame → 演出用の速度）。**④が選んだ数字。**
        /// 竜巻は自分の <c>m_velocity</c> をそのまま渡しているが、台風の中心速度は
        /// 数 m/frame しかなく、そのままでは市民が動かない。
        /// </summary>
        private const float WindDirectionalScale = 20f;

        private static float _minutesSincePass;
        private static ushort _typhoonId;
        private static int _centreCellX = -1;
        private static int _centreCellZ = -1;

        /// <summary>
        /// 次に見るセルの序数（<see cref="OutwardCellOrder"/> の順序）。0 が中心のセル。
        /// 上限で打ち切られたときだけ 0 以外で残る。
        /// </summary>
        private static int _cursorOrdinal;

        private static bool _errorLogged;
        private static bool _treeNotePosted;

        /// <summary>
        /// <c>DestroyTrees</c> がこの環境で解決できないと分かったか。
        /// 一度立てたら以後は倒木を呼ばない（毎走査で例外を出さない）。
        /// </summary>
        private static bool _treesUnavailable;

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

        /// <summary>直近 1 回で調べた建物数（候補マスクを通り、強風域内にあったもの）。</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>直近 1 回で確率選定を通った棟数。</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>直近 1 回で「バニラが dry-run で受け付ける」と答えた棟数。</summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>
        /// 直近 1 回で「バニラが設計上断る」と答えた棟数
        /// （dry-run も本番も false ＝ 本当に壊れなかったもの）。
        /// **0 でないのは正常**（防災施設は台風で壊れない。§F-2）。
        ///
        /// ★ <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> はここに入らない。
        ///   あれらは dry-run で false を返した直後に本物の倒壊を行うので
        ///   （§F-2）、<see cref="LastCollapsed"/> にだけ積まれる。
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>直近 1 回で実際に倒壊した棟数。</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>
        /// 直近 1 回で**高さが読めなかった**棟数。②と違い、これらは対象から
        /// 外れていない（高さボーナスを辞退しただけ）。
        /// </summary>
        public static int LastUnknownHeight { get { return _lastUnknownHeight; } }

        /// <summary>直近 1 回が上限で打ち切られたか（続きは次回）。</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>セッション累計の倒壊棟数。</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>
        /// 台風を手放すとき（<c>TyphoonController.Forget</c>）とレベルアンロードで呼ぶ。
        /// 冪等である（重ねて呼んでよい）。
        /// </summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _minutesSinceGale = 0f;
            _galePushes = 0;
            _typhoonId = 0;
            _centreCellX = -1;
            _centreCellZ = -1;
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

            if (_treeNotePosted)
            {
                _treeNotePosted = false;
                FeatureHost.ClearDegraded(TyphoonFeature.FeatureName, TreeNoteKey);
            }
            // ★ _errorLogged / _treesUnavailable は戻さない。どちらも「この DLL が
            //    参照しているゲームのビルドに対する事実」であって都市ごとの状態ではない
            //    （TyphoonLightning / TyphoonReader と同じ判断）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>TyphoonFeature.OnSimulationTick</c> のポーズガードより
        /// 下から、台風が動いているときだけ呼ぶこと**（ポーズ中に建物が倒れる）。
        /// 設定が OFF のときは呼び出し側が呼ばない。
        ///
        /// <paramref name="snapshot"/> は**前 tick の状態**なので位置も強度も読まない
        /// （<see cref="TyphoonSnapshot"/> の T3 節の注記）。<see cref="TyphoonController"/> の
        /// static から同じスレッドで直接読む。引数に残してあるのは他の要素と
        /// 呼び出しの形をそろえるためである。
        /// </summary>
        public static void Apply(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                Step(deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon wind damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWind",
                             "typhoon wind damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(float deltaMinutes)
        {
            // ★ 間隔の累積は**対象の台風より先に**進める。台風が入れ替わっても、
            //    無くなっても、時間は流れているという扱いにする（クラス doc / I3）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (!TyphoonController.Active)
            {
                _typhoonId = 0;
                _cursorOrdinal = 0;
                _centreCellX = -1;
                _centreCellZ = -1;
                return;
            }

            ushort id = TyphoonController.DisasterId;
            if (id != _typhoonId)
            {
                _typhoonId = id;
                _cursorOrdinal = 0;
                _centreCellX = -1;
                _centreCellZ = -1;
                // **累積は巻き戻さない**（クラス doc）。
            }

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;

            // 余りを繰り越さない。ロード直後などに大きな deltaMinutes が来ても
            // 次 tick に連続発火せず「間隔ごとに 1 回」を保つ。
            _minutesSincePass = 0f;

            int strength = ModSettings.TyphoonWindStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;

            Sweep(id, strength);
        }

        /// <summary>
        /// 1 回ぶんの走査。上限に達したら打ち切り、次回は <see cref="_cursorOrdinal"/> から
        /// 再開する（クラス doc の「1 tick あたりの仕事量の上限」）。
        /// </summary>
        private static void Sweep(ushort typhoonId, int strength)
        {
            float range = TyphoonController.GaleRadius;
            // プレハブ半径が読めていなければ 0。**推測した半径で走らない**（設計書 §6）。
            if (!(range > 0f)) return;

            byte intensity = TyphoonController.Intensity;

            // ★ 半径の**結果**（GaleRadius）から割り戻さない。強度 0 のとき
            //   StormRadiusOf は 0 になり、0 除算で NaN の風速相当が全棟に配られる。
            //   入力そのものを読む（TyphoonController.PrefabRadius の doc）。
            float prefabRadius = TyphoonController.PrefabRadius;
            if (!(prefabRadius > 0f)) return;

            var centre3 = TyphoonController.Centre;
            if (float.IsNaN(centre3.X) || float.IsNaN(centre3.Z)) return;
            var centre = new Vec2(centre3.X, centre3.Z);

            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので、exists で先に確認する。
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ 危険半円側で風速の場を引き伸ばすぶん、**矩形も同じ倍率だけ広げる**
            //   （クラス doc）。片側だけ広げることもできるが、矩形の 4 辺はどのみち
            //   セル境界に丸められるので全周に掛けたほうが読みやすく、広げすぎても
            //   「風速 0 の建物を数えずに飛ばす」だけで害が無い。
            float scanRange = range * (1f + TrackBias.MaxRadiusBoost);

            // 建物グリッドは 1 セル 64m、270x270（バニラの DestroyBuildings と同じ
            // セル 64・オフセット 135・[0,269] クランプ）。
            int minX = Clamp((int)((centre.X - scanRange) / 64f + 135f));
            int maxX = Clamp((int)((centre.X + scanRange) / 64f + 135f));
            int minZ = Clamp((int)((centre.Z - scanRange) / 64f + 135f));
            int maxZ = Clamp((int)((centre.Z + scanRange) / 64f + 135f));

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // リングの中心は台風のセル。矩形と同じクランプを掛けるので、中心が
            // マップの外でもグリッドの中に落ちる。
            int centreX = Clamp((int)(centre.X / 64f + 135f));
            int centreZ = Clamp((int)(centre.Z / 64f + 135f));

            // ★ 中心のセルが動いたら走査位置を捨てる。矩形もリングも別物になるので
            //    「続きから」に意味が無い（クラス doc）。**累積は触らない。**
            if (centreX != _centreCellX || centreZ != _centreCellZ)
            {
                _centreCellX = centreX;
                _centreCellZ = centreZ;
                _cursorOrdinal = 0;
            }

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(typhoonId);

            // ★ 偏りは**毎走査読み直す**（クラス doc）。ここでキャッシュした値は
            //   この 1 走査のあいだだけ有効で、次の走査ではまた読み直される ——
            //   だから経路が曲がれば偏りも回る。
            float heading = TyphoonController.HeadingRadians;
            bool southern = ModSettings.TyphoonSouthernHemisphere.value;
            int biasedSelected = 0;

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
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ 次の ID は**行動する前に**控える（②の LongPeriodDamage と同じ）。
                    //    倒壊で建物を解放するサードパーティの AI が居ると
                    //    buildings[id] が 0 で埋まり、このセルの残りが黙って飛ぶ。
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        float offsetX = p.x - centre.X;
                        float offsetZ = p.z - centre.Z;
                        float d = Distance(centre, p.x, p.z);

                        // ★★ 危険半円（クラス doc）。**半径の定数は書き換えず、
                        //    「もっと眼に近い」ことにして風速の場を引き伸ばす。**
                        //    左側と正面・真後ろでは倍率がちょうど 1 なので、
                        //    そちらは今日までと 1 ビットも変わらない。
                        float radiusFactor = TrackBias.RadiusFactor(heading, offsetX, offsetZ, southern);
                        float wind = TyphoonProfile.WindAt(d / radiusFactor,
                                                           intensity, prefabRadius);
                        if (wind > 0f)
                        {
                            scanned++;

                            // ★ 高さは**係数**であって足切りではない（②と違う点）。
                            //    読めなければボーナスを辞退するだけで、対象には残す。
                            float metres = BuildingHeight.MetresOf(ref buildings[id]);
                            if (metres <= 0f) unknownHeight++;

                            float chanceFactor = TrackBias.ChanceFactor(heading, offsetX, offsetZ, southern);
                            if (chanceFactor > 1f) biasedSelected++;

                            if (IsSelected(typhoonId, id, wind, metres, strength, chanceFactor))
                            {
                                selected++;
                                bool accepted;
                                bool fell = Collapse(buildings, id, group, out accepted);

                                // ★★ **倒れたものを「断られた」に数えない**（全体レビュー）。
                                //    dry-run は診断専用で、PowerPoleAI / CableCarPylonAI は
                                //    `if (testOnly) return false;` の直後に本物の倒壊を行う
                                //    （§F-2）。以前はそれらを refused と collapsed の
                                //    **両方**に積んでおり、診断の refused が
                                //    「シェルター等が設計上断った数」を名乗れなくなっていた。
                                if (fell) collapsed++;
                                else if (accepted) attempted++;
                                else refused++;
                            }
                        }
                    }

                    id = next;

                    // 連結リストが壊れている保存データで無限ループしないための保険。
                    if (++guard > GridChainGuard) break;
                }
            }

            // 1 周し終えていれば次回は中心から。打ち切りなら続きから。
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

            // 演出は走査 1 回につき 1 度、台風の中心で。**建物とは独立**に呼ぶ
            // （倒壊 0 でも風は吹くし、木は倒れる）。
            PushWind(centre3, group, range);
            FellTrees(typhoonId, centre3, group, range);

            // ★ collapsed > 0 で囲ってはいけない。「機能が死んでいる」と
            //    「近くに建物が無い」がログ上で区別できなくなる（③で実際に起きた形）。
            WriteDiag(typhoonId, strength, range, cells, cellCount,
                      startOrdinal, ordinal, scanned, selected, attempted,
                      refused, collapsed, unknownHeight, capped,
                      heading, southern, biasedSelected);
        }

        /// <summary>
        /// この建物を選ぶか。**乱数は <see cref="DeterministicRandom"/>**
        /// （<c>VanillaRandomizer</c> ではない）——これは④が発明した判断で、
        /// バニラが引く値と一致する必要が無い。
        ///
        /// **フレームを混ぜない**（クラス doc）。
        /// </summary>
        private static bool IsSelected(ushort typhoonId, ushort buildingId,
                                       float wind, float heightMetres, int strength,
                                       float chanceFactor)
        {
            float chance = WindDamageModel.CollapseChance(wind, heightMetres, strength);
            if (chance <= 0f) return false;

            // ★ 危険半円の上乗せは**モデルの外**で掛ける。WindDamageModel は
            //   「風速と高さと設定から確率を出す」ことだけを持ち、台風の向きを
            //   知らない。壊れた倍率が来ても抽選を狂わせない。
            if (!float.IsNaN(chanceFactor) && chanceFactor > 1f) chance *= chanceFactor;
            if (chance > 1f) chance = 1f;

            float roll = DeterministicRandom.Unit(typhoonId, buildingId);
            return roll < chance;
        }

        /// <summary>
        /// 実際に倒す。**<c>DisasterHelpers</c> を経由しない**（クラス doc）。
        ///
        /// **dry-run が false でも本番は必ず呼ぶ** —— <c>PowerPoleAI</c> /
        /// <c>CableCarPylonAI</c> は <c>if (testOnly) return false;</c> の直後に本物の
        /// 倒壊を行う（§F-2）。dry-run でフィルタすると送電柱を 1 本も倒せない。
        /// </summary>
        /// <param name="accepted">
        /// バニラ自身が dry-run に「受け付ける」と答えたか。false は
        /// 「設計上断られた」（防災施設 or 送電柱）であって不具合ではない。
        /// </param>
        private static bool Collapse(Building[] buildings, ushort id,
                                     InstanceManager.Group group, out bool accepted)
        {
            accepted = false;

            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            var ai = info.m_buildingAI;

            // demolish: false（瓦礫を残す。防災施設は断る＝正しい挙動）、
            // burnAmount: 0（風は吹き飛ばして潰すのであって焼損ではない）。
            // ★ m_fireIntensity には触れない（罠 5）。
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// 市民と車両を吹き飛ばす。**無害**（<c>AddWindCitizens</c> ＋
        /// <c>AddWindVehicles</c> の 2 行だけで、建物・道路・樹木には一切触らない。§B-1）。
        /// 引数の形は §B-1 の竜巻の実引数を手本にした。
        /// </summary>
        private static void PushWind(Vec3 centre, InstanceManager.Group group, float range)
        {
            float heading = TyphoonController.HeadingRadians;
            float speed = TyphoonController.Intensity / 255f * WindDirectionalScale;

            var position = new Vector3(centre.X,
                                       centre.Y + range * WindHeightFraction,
                                       centre.Z);
            var directional = new Vector3(Mathf.Cos(heading) * speed, WindUpward,
                                          Mathf.Sin(heading) * speed);

            DisasterHelpers.AddWind(position, range * WindRadiusFactor, directional,
                                    WindRotational, WindRadial, group);
        }

        /// <summary>
        /// 倒木。**燃やさない**（<c>burnRadiusMin</c> / <c>burnRadiusMax</c> は 0）——
        /// 台風で木が燃えるのはおかしい。<c>TreeManager.BurnTree</c> も使わない。
        ///
        /// この 1 経路だけが解決できない環境がありうる（そのときは倒木を諦め、
        /// 風害の残りはそのまま動かす。<c>FeatureHost.NoteDegraded</c> で自己申告する）。
        /// </summary>
        private static void FellTrees(ushort typhoonId, Vec3 centre,
                                      InstanceManager.Group group, float range)
        {
            if (_treesUnavailable) return;

            float outer = range * TreeRadiusFraction;
            if (!(outer > 0f)) return;
            float inner = outer * TreeInnerFraction;

            var position = new Vector3(centre.X, centre.Y, centre.Z);

            try
            {
                DisasterHelpers.DestroyTrees(typhoonId, group, position,
                                             outer,      // totalRadius（一次カリング）
                                             0f,         // removeRadius（跡形も無く消す範囲）
                                             inner,      // destructionRadiusMin
                                             outer,      // destructionRadiusMax
                                             0f, 0f);    // ★ 燃やさない
            }
            catch (System.Exception e)
            {
                // 一度でも投げたら以後呼ばない。**黙って諦めない。**
                _treesUnavailable = true;
                Log.Warn("typhoon wind: DisasterHelpers.DestroyTrees is unusable in this build ("
                         + e.GetType().Name + "); the wind sweep keeps running without felling "
                         + "trees");
                UpdateTreeNote();
            }
        }

        private static void UpdateTreeNote()
        {
            if (_treeNotePosted) return;
            _treeNotePosted = true;
            FeatureHost.NoteDegraded(TyphoonFeature.FeatureName, TreeNoteKey,
                "DisasterHelpers.DestroyTrees could not be called; the typhoon still collapses "
                + "buildings and pushes citizens, but it fells no trees");
        }

        /// <summary>
        /// 災害グループ。渡すとバニラ側の集計（災害ごとの被害棟数）が正しく積まれる。
        /// <c>InstanceManager</c> がまだ居なければ <c>null</c>（バニラ自身も null を渡す
        /// 経路を持つ。集計が積まれないだけで倒壊も風も走る）。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            // new GameObject を走らせる main スレッド専用 API。ここは sim スレッド。
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **倒壊 0 のときも毎回出す。** <c>Log.Diag</c> は同一キーで 512 sim フレームに
        /// 1 回に間引かれるが、**引数の文字列連結は毎回走ってしまう**ので
        /// <c>DiagEnabled</c> で先に落とす（C# は引数を呼び出し前に評価し切る）。
        /// </summary>
        private static void WriteDiag(ushort typhoonId, int strength, float range,
                                      int cells, int cellCount, int startOrdinal, int ordinal,
                                      int scanned, int selected, int attempted, int refused,
                                      int collapsed, int unknownHeight, bool capped,
                                      float heading, bool southern, int biasedSelected)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWind",
                "wind pass#" + _passes + " typhoon#" + typhoonId
                + " strength=" + strength
                + " galeRadius=" + range.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                // 走査は中心のセル（序数 0）から外へ。次回の再開点も出す
                // ——「中心まで届いていない」を診断から見えるようにするため。
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
                + " scanned=" + scanned + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " collapsed=" + collapsed
                + " unknownHeight=" + unknownHeight
                // ★ 危険半円がどちらを向いていて、何棟がその側に居たか。
                //   偏りが「効いていない」と「その側に建物が無い」を見分ける唯一の手。
                + " dangerousSide=" + (southern ? "left" : "right")
                + " heading=" + (heading * 57.29578f).ToString("F0") + "deg"
                + " onDangerousSide=" + biasedSelected
                + (_treesUnavailable ? " trees=unavailable" : " trees=felled")
                // ★ 「次回続きから」とは書かない（全体レビュー I1）。中心のセルが
                //   変わると _cursorOrdinal は 0 に戻り、次の走査は眼から
                //   やり直す —— 詳しくはクラス doc の「打ち切りの続き」節。
                + (capped ? " (capped; the outer edge was not rolled this pass)" : ""));
        }

        private static float Distance(Vec2 centre, float x, float z)
        {
            float dx = x - centre.X;
            float dz = z - centre.Z;
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
