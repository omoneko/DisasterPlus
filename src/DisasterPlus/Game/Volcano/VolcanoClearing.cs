using System;
using System.Reflection;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 準備 —— 道路と建物の段階的破壊。**sim スレッド専用。**
    ///
    /// ★★ <b>このタスクが⑤の要である。</b> 設計書 §1.2 は「地面を上げれば山ができる」が
    /// 市街地では成立しないことを発見し、⑤の設計そのものを書き換えた。
    /// 準備を隆起より先に置くのがその結論であり、それを実装するのがこの型である。
    ///
    /// ── なぜ隆起より先なのか（設計書 §1.2 ／ IL 事実文書 §A-2 / §A-3）───────────
    ///
    /// <c>TerrainModify.UpdateAreaImplementation</c> は毎回 7 つのマネージャに
    /// <c>ApplyQuad</c> させる。そのうち 2 つが⑤を殺す —— <c>NetSegment.TerrainUpdated</c> は
    /// <c>m_flattenTerrain</c> の道路の下で <c>Heights.PrimaryLevel</c> を掛けて
    /// <c>primaryMin = primaryMax = 道路の y</c> にし、<c>Building.TerrainUpdated</c> は
    /// <c>SecondaryLevel</c> で建物の y に固定する（1:4 の裾つき）。
    /// 反映段は <c>Clamp(Clamp(target, secondaryMin, secondaryMax), primaryMin, primaryMax)</c>
    /// なので、<b>下限と上限が同じ値なら target に何を入れていても打ち消される。</b>
    /// しかも**毎フラッシュゼロからやり直される**ので、書き続けても勝てない。
    /// そして<b>ゲームは何も壊さない</b>（<c>NetAI.AfterTerrainUpdate</c> は <c>ret</c> 1 命令、§A-3）。
    ///
    /// > したがって、⑤が自分で壊さない限り、山の中に道路の平らな溝と建物のすり鉢が
    /// > 必ず残る。**これは実装の良し悪しではなく順序の問題である。**
    ///
    /// ── <see cref="ClearedRadiusMetres"/> は T6 の唯一許される入力である ──────────
    ///
    /// <c>VolcanoUplift</c>（T6）が書いてよいセルは
    /// <c>UpliftSchedule.ActiveRadiusMetres(shapeRadius, VolcanoClearing.ClearedRadiusMetres)</c>
    /// の内側だけである。**第 2 引数にこれ以外を渡してはいけない**（計画の罠 1）。
    /// まだ 1 本も壊していなければ 0 で、Core 側のテストが「0 なら 0 を返す」を固定している。
    ///
    /// ★ <b><see cref="ClearedRadiusMetres"/> は「壊し終えた」ではなく「走査し終えた」である。</b>
    /// 断られた建物（下記）が残っていても走査は終わっている。**そこは⑤が壊せないと
    /// 分かっている場所**なので、T6 は上げてよい —— 上げても押し戻されるが、それは
    /// 「⑤が壊せない建物が 1 つある」という事実の可視化であって⑤の欠陥ではない。
    ///
    /// ── 準備は隆起の前を走る（ring lockstep）───────────────────────
    ///
    /// <code>
    /// front   = UpliftSchedule.ClearingFrontMetres(R, frontUnit, VolcanoClearingLeadMetres)
    /// cleared = 実際に走査を終えた半径（front 以下。届かなければ届いた分だけ）
    /// </code>
    ///
    /// 1 回の走査で <c>front</c> まで届かなかったら <see cref="ClearedRadiusMetres"/> は
    /// **届いた分しか進めない**。上限に当たった状態で「全部終わった」と報告すると、
    /// T6 が壊れていない場所を上げる。<b>火山の中心は動かない</b>ので、④の風害と違って
    /// 走査位置（<see cref="_buildingCursor"/> / <see cref="_segmentCursor"/>）を捨てる条件は
    /// 「別の火山になったとき」だけである。
    ///
    /// ── 建物は <c>demolish: true</c>（④の風害と判断が逆になる）──────────────
    ///
    /// ④の風害は「台風で**壊れた**ように見せる」ので跡地が残ってよく <c>false</c> だった。
    /// ⑤の準備は「その場所を**空ける**」ので跡地が残ってはいけない —— 残ると
    /// <c>Building.TerrainUpdated</c> が地形固定を続ける（§A-2、§G-16 (c)）。
    /// <c>burnAmount</c> は <b>0</b> —— 準備は焼損ではない（火を付けるのは T8）。
    ///
    /// > **罠 5。<c>Building.m_fireIntensity</c> を直接書かない。** 消費するのは
    /// > <c>CommonBuildingAI</c> の系だけで、それ以外の AI に書くと誰も消さない永久の
    /// > 幽霊火災になり、**バニラの建物配列に入るのでセーブに焼き付き、MOD を外しても残る**。
    /// > 本プロジェクトは一度これを出荷している。
    /// > レビューの grep（★ 実際に走らせて件数を合わせてある。全体レビュー M12 ——
    /// > 素で走らせると**この規則の文そのもの**が引っかかり、
    /// > 「0 件」という手順が最初から成立していなかった）:
    /// > <code>
    /// > grep -rn --include=*.cs "m_fireIntensity" src/DisasterPlus/Game/Volcano/ \
    /// >   | grep -vE ':[0-9]+: *//' | wc -l        # -> 0
    /// > </code>
    /// > <c>grep -v '///'</c> では足りない（<c>//</c> 1 本のコメントも落とす）。
    ///
    /// > **<c>DisasterHelpers</c> の破壊ヘルパを絶対に呼ばない**（§E-14）。Natural Disasters
    /// > Renewal はそれらを Prefix で完全置換する。<c>BuildingAI.CollapseBuilding</c> と
    /// > <c>NetAI.CollapseSegment</c> を直接呼べばパッチ面を**完全に迂回できる**。
    ///
    /// ── ★ T5 Step 1: IL で確定させた破壊経路（典拠は IL 事実文書 §G-16。再導出しない）──
    ///
    /// 設計書 付録と計画 T5 Step 1 が「未確定」と名指ししていた 2 項目の答えである。
    /// **全文と IL のオフセットは §G-16 にある。** ここには、このファイルのコードが
    /// どの結論に乗っているかだけを書く:
    ///
    ///   1. **道路は本当に解放される**（§G-16 (a)）。<c>demolish: true</c> は
    ///      <c>PlayerNetAI.CollapseSegment</c> に集約され、
    ///      <c>NetManager.ReleaseSegment(id, keepNodes: false)</c> を呼ぶ。
    ///      19 個の override のうち断るのは <c>SupportCableAI</c> だけである
    ///      （ケーブルカーの支線。地形を平らにしない種別なので、残っても溝にはならない）。
    ///      <c>Untouchable</c> のセグメントは所有建物の倒壊が断られるとセグメントごと断られる
    ///   2. **最後のセグメントが消えたノードはその場で解放される**（§G-16 (b)）。
    ///      孤児ノードは残らない。ただし<b>道路の解放が建物を巻き込むことがある</b>
    ///      （<c>ReleaseNodeImplementation</c> → <c>ReleaseBuilding</c>）ので、
    ///      建物側の走査は「次の ID を行動前に控える」規律を必ず守ること
    ///   3. **<c>Collapsed</c> を立てただけでは地形固定が止まらない**（§G-16 (c)）。
    ///      <c>NetSegment.TerrainUpdated</c> は <c>m_flags &amp; 3</c> しか見ず、
    ///      <c>Building.TerrainUpdated</c> は <c>m_flags &amp; 524291</c>
    ///      （<c>Created|Deleted|Demolishing</c>）しか見ない。**これが④の
    ///      <c>demolish: false</c> と判断が逆になる、IL 上の理由である**
    ///   4. **断る 5 つの AI は <c>demolish: true</c> を通す**（§G-16 (d)）。
    ///      ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / TsunamiBuoyAI は
    ///      <c>CommonBuildingAI</c> へ委譲し、DecorationBuildingAI は <c>Demolishing</c> を
    ///      立てる。**「防災施設の足元だけ地形が残る」は起きない。**
    ///      それでも <see cref="LastBuildingsRefused"/> を残してあるのは、断りうる経路が
    ///      他にもあるからで、**1 棟でも断られたらそのセルは永久に固定されたまま隆起に
    ///      取り残される**ので診断とパネルで見えるようにしておく。
    ///      <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> / <c>MonorailPylonAI</c> は
    ///      dry-run で嘘をつく（④ §F-2）が、⑤は dry-run を**フィルタに使わない**ので
    ///      取りこぼさない
    ///   5. **<c>InstanceManager.Group</c> は <c>null</c> を渡す**（§G-16 (e)）。
    ///      ⑤は災害スロットに載らないので束ねる先が無く、null 検査は 3 箇所とも在る。
    ///      空の <c>Group</c> を new して渡すより、null のほうが実測どおりである
    ///
    /// > **1 と 2 が成立しなかった場合、⑤は隆起を始めない**（計画 T5 Step 1）。
    /// > <see cref="ClearingPathAvailable"/> が false になり、<c>VolcanoState.HandleStart</c> は
    /// > 1 本も壊さずに <c>Refused</c> へ落ちて、パネルが
    /// > <c>Strings.VolcanoRoadPathUnavailable</c> を出す。
    /// > **「道路だけ諦めて隆起する」を選んではいけない** —— それは設計書 §1.2 が
    /// > 発見した失敗（山の中の平らな溝）を、分かったうえで出荷することになる。
    ///
    /// ── 1 tick あたりの仕事量の上限（明示する）──────────────────────
    ///
    /// 走査の間隔は <see cref="IntervalFrames"/> フレームぶんの**経過ゲーム内時間**である。
    /// <c>frameIndex % N</c> にしない —— <c>m_currentFrameIndex</c> は 1 tick で
    /// <c>FinalSimulationSpeed</c>（1/3/9）進むので、剰余だとゲーム速度で判定がまばらになる
    /// （火災旋風 付録 A-4）。
    ///
    /// 1 回の走査の上限は<b>建物側・道路側それぞれ グリッドセル
    /// <see cref="MaxCellsPerPass"/> 個</b>と<b>建物 <see cref="MaxBuildingsPerPass"/> 棟 /
    /// 道路 <see cref="MaxSegmentsPerPass"/> 本</b>。半径 3000 m（形態の最大）でも矩形は
    /// 95×95 ＝ 9025 セルなので、セル上限に当たるのは中心がマップの端にある場合だけで、
    /// 実際に効くのは件数の上限である。打ち切ったら次回はカーソルから再開し、
    /// <see cref="ClearedRadiusMetres"/> は**届いた分しか進めない**。
    ///
    /// ── 候補マスクが②④と違う（ここが⑤に固有）──────────────────────
    ///
    /// ②④は <c>Collapsed</c> を候補から弾いていた（<c>CollapseBuilding</c> が必ず
    /// false を返すので、跡地の瓦礫が毎回 refused に積まれて診断が読めなくなるため）。
    /// **⑤は弾かない。** 上の 3 のとおり、<c>Collapsed</c> を立てただけの瓦礫は
    /// <b>地形を固定し続ける</b>ので、⑤にとってはまさに取り除かなければならない相手である。
    /// 代わりに <c>Demolishing</c> を弾く（そちらは既に地形固定が止まっている）。
    /// </summary>
    public static partial class VolcanoClearing
    {
        /// <summary>走査の間隔（フレーム相当のゲーム内時間）。</summary>
        private const int IntervalFrames = 64;

        /// <summary>
        /// 1 回の走査で見るグリッドセルの上限（建物側・道路側それぞれ）。
        ///
        /// ★ **32768 は死んだ定数だった**（全体レビュー M16）。走査するリングの
        /// 通し番号は形態の最大（R=3000 m）でも 99² = 9801 が上限なので、
        /// 32768 には**構造上 1 度も届かない**。実際に効く値へ下げてある ——
        /// 最大の火山でも 3 回に分けて走ることになり、1 tick でグリッドを
        /// 歩く量そのものに上限が付く。届かなかった分は次回のカーソルから続き、
        /// <see cref="ClearedRadiusMetres"/> は届いた分しか進まない。
        /// </summary>
        private const int MaxCellsPerPass = 4096;

        /// <summary>
        /// 1 回の走査で壊しにいく建物の上限。
        ///
        /// ★ **2048 から下げた**（全体レビュー M16）。<c>demolish: true</c> の 1 回は
        /// フラグを立てるだけではない —— 建物の解放、下請け建物への再帰、
        /// 道路側ではノードの解放と経路の無効化と <c>UpdateArea</c> を引き連れる。
        /// 建物 2048 ＋ 道路 2048 ＝ **1 sim tick に 4096 回**は、
        /// 「1 tick あたりの仕事量に上限を置く」と名乗れる数ではない。
        /// 走査は 64 フレームおきなので、128 でも 1 ゲーム内分あたり
        /// およそ 90 棟が消える速さである。
        /// </summary>
        private const int MaxBuildingsPerPass = 128;

        /// <summary>1 回の走査で壊しにいく道路セグメントの上限（建物側と同じ理由）。</summary>
        private const int MaxSegmentsPerPass = 128;

        /// <summary>建物・道路グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>グリッドのセル寸法（m）。</summary>
        private const float GridCellSize = 64f;

        /// <summary>ワールド座標 → セル添字のオフセット。</summary>
        private const float GridCellOffset = 135f;

        /// <summary>建物の連結リストを辿る回数の上限（建物バッファの大きさ）。</summary>
        private const int BuildingChainGuard = 49152;

        /// <summary>道路の連結リストを辿る回数の上限（<c>Array16&lt;NetSegment&gt;(36864)</c>）。</summary>
        private const int SegmentChainGuard = 36864;

        // ★★ **マスクも余白も当たり判定もここには置かない**（全体レビュー I2 / I4）。
        //    <see cref="VolcanoScan"/> の 1 組を調査（VolcanoSurvey）と共有する。
        //    以前は同じ規則を 2 つのファイルに写し、「同じ値でなければずれる」と
        //    **注意書きで**担保していた。注意書きは、実際にマスクがずれたことを
        //    防げなかった —— 調査だけが Untouchable と Collapsed を弾いており、
        //    不可逆の操作の直前に壊れる数を実際より少なく見せていた。

        /// <summary>
        /// 先行距離の下限（m）。raw セル 1 つ分。**0 を許すと ring lockstep が
        /// 自分自身を待って永久に止まる**（<see cref="LeadMetres"/>）。
        /// </summary>
        private const float MinLeadMetres = 16f;

        private static VolcanoDestructionFacts _facts;
        private static bool _factsScanned;

        private static float _minutesSincePass;
        private static Vec3 _centre;
        private static bool _centreValid;

        private static float _clearedRadius;
        private static float _frontRadius;
        private static float _shapeRadius;

        private static int _buildingCursor;
        private static int _segmentCursor;

        // ★ カーソルが途中のまま持ち越された走査が、「一周した」と言ってよい半径の
        //   上限（全体レビュー M8。<c>VolcanoClearing.Sweep.cs</c> の <c>PassFront</c>）。
        //   0 は「控えていない」。
        private static float _buildingPassFront;
        private static float _segmentPassFront;

        // ★★ **走査の 2 本は別々に一周する。だから届いた半径も別々に憶える。**
        //    （2026-08-22、実機で楯状 r=3000 m が永久に終わらなかった原因。）
        //
        //    以前はこの 2 つを持たず、<c>Sweep</c> が**そのパスの**建物側と道路側の
        //    小さいほうを取って <see cref="_clearedRadius"/> へ入れていた。
        //    ところが 1 周に要するパス数は 2 本で違う（道路側の矩形は
        //    <c>VolcanoScan.SegmentGridMargin</c> ぶん広い）ので、
        //    **前線が伸びていく途中で 2 本の周回位相がずれる。** ずれたあとは
        //    「片方が一周し終えたパス」と「もう片方が一周し終えたパス」が
        //    永久に別のパスになり、**同じパスで両方が前線に届くことが二度と無い。**
        //    実機の log はその状態をそのまま記録している ——
        //    <c>cleared=2816/3000</c> が 660 パス以上動かず、隆起は
        //    <c>progress=1.000</c> のまま <c>Uplifting</c> で止まり、
        //    火山性微動が鳴りやまなかった。
        //
        //    それぞれの側で単調に憶えて、**最後に小さいほうを取る**。
        //    意味は変わらない（「両方が走査し終えた半径」）が、
        //    2 本が同じパスで揃う必要が無くなる。
        private static float _buildingReachedRadius;
        private static float _segmentReachedRadius;

        private static bool _errorLogged;

        // ── 診断カウンタ 9 個（全て sim スレッドからのみ読み書きする）────────────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastBuildingsDestroyed;
        private static int _lastBuildingsRefused;
        private static int _lastSegmentsDestroyed;
        private static int _lastSegmentsRefused;
        private static int _totalBuildingsDestroyed;
        private static int _totalSegmentsDestroyed;
        private static bool _lastCapped;

        private static string _lastFailure;

        /// <summary>
        /// ★★ <b>T6 の唯一許される入力。</b> 走査を終えた半径（m）。
        /// **まだ 1 本も壊していなければ 0** で、そのとき
        /// <c>UpliftSchedule.ActiveRadiusMetres</c> は 0 を返す（＝隆起は 1 セルも動かない）。
        ///
        /// 「壊し終えた」ではなく「**走査し終えた**」である（クラス doc）。
        /// </summary>
        public static float ClearedRadiusMetres { get { return _clearedRadius; } }

        /// <summary>
        /// 今この瞬間に追いかけている前線（m）。<see cref="ClearedRadiusMetres"/> が
        /// ここまで届いたら、その進捗ぶんの準備は済んでいる。診断と位相の遷移に使う。
        /// </summary>
        public static float FrontMetres { get { return _frontRadius; } }

        /// <summary>今の前線まで走査が届いているか（＝この進捗ぶんの準備が済んだか）。</summary>
        public static bool FrontReached
        {
            get { return _passes > 0 && _clearedRadius >= _frontRadius; }
        }

        /// <summary>山の半径まで走査を終えたか（＝準備が全部終わったか）。</summary>
        public static bool Complete
        {
            get { return _shapeRadius > 0f && _clearedRadius >= _shapeRadius; }
        }

        /// <summary>
        /// 準備の破壊経路がこの環境で成立するか。**false なら⑤は火山を 1 つも作らない**
        /// （クラス doc の Step 1）。<see cref="Tick"/> を 1 度も呼んでいなくても答えられる。
        ///
        /// ★★ <b>述語は <see cref="Sweep"/> が実際に門にしている式と同じでなければならない</b>
        /// （全体レビュー M9）。ここが <c>RoadPathUsable</c>（道路だけ）だった頃、
        /// <c>CollapseBuilding</c> が解決できず道路側だけ解決できた環境では
        /// **火山が確定して <c>Clearing</c> に入り、そこで永久に止まった** ——
        /// <c>Sweep</c> は <c>Facts().Usable</c> で毎回引き返すので走査回数が 1 回も
        /// 増えず、<c>FrontReached</c> が真にならず、位相は <c>Refused</c> にすらならない。
        /// </summary>
        public static bool ClearingPathAvailable
        {
            get { return Facts().Usable && SegmentGridUsable(); }
        }

        /// <summary>これまでに走った走査の回数（この火山での累計）。</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>直近 1 回で候補として見た建物と道路の合計。</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>直近 1 回で実際に取り除いた建物数。</summary>
        public static int LastBuildingsDestroyed { get { return _lastBuildingsDestroyed; } }

        /// <summary>
        /// 直近 1 回で**バニラが断った**建物数。0 でないなら、その足元のセルは
        /// 元の高さに固定されたまま隆起に取り残される（クラス doc の 4）。
        /// </summary>
        public static int LastBuildingsRefused { get { return _lastBuildingsRefused; } }

        /// <summary>直近 1 回で実際に取り除いた道路セグメント数。</summary>
        public static int LastSegmentsDestroyed { get { return _lastSegmentsDestroyed; } }

        /// <summary>
        /// 直近 1 回で**バニラが断った**道路セグメント数。<c>SupportCableAI</c> と、
        /// 所有建物が倒壊を断った <c>Untouchable</c> のセグメントがここに入る。
        /// </summary>
        public static int LastSegmentsRefused { get { return _lastSegmentsRefused; } }

        /// <summary>この火山でこれまでに取り除いた建物数。</summary>
        public static int TotalBuildingsDestroyed { get { return _totalBuildingsDestroyed; } }

        /// <summary>この火山でこれまでに取り除いた道路セグメント数。</summary>
        public static int TotalSegmentsDestroyed { get { return _totalSegmentsDestroyed; } }

        /// <summary>直近 1 回が上限で打ち切られたか（続きは次回。前線には届いていない）。</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>
        /// 直近に走れなかった理由（**英語・診断用**）。走れていれば null。
        /// **黙って何もしないをやらない**ための口である。
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// 火山を手放すときとレベルアンロードで呼ぶ。冪等である。
        /// **進行中の火山は保存しない**ので、都市を出入りすると準備は 0 からになる
        /// （地形はそのときの形のまま残る。設計書 §1.3）。
        /// </summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _centre = new Vec3(0f, 0f, 0f);
            _centreValid = false;
            _clearedRadius = 0f;
            _frontRadius = 0f;
            _shapeRadius = 0f;
            _buildingCursor = 0;
            _segmentCursor = 0;
            _buildingPassFront = 0f;
            _segmentPassFront = 0f;
            _buildingReachedRadius = 0f;
            _segmentReachedRadius = 0f;
            _passes = 0;
            _lastScanned = 0;
            _lastBuildingsDestroyed = 0;
            _lastBuildingsRefused = 0;
            _lastSegmentsDestroyed = 0;
            _lastSegmentsRefused = 0;
            _totalBuildingsDestroyed = 0;
            _totalSegmentsDestroyed = 0;
            _lastCapped = false;
            _lastFailure = null;

            // ★ _errorLogged と破壊経路の実測は戻さない。どちらも「この DLL が参照して
            //    いるゲームのビルドに対する事実」であって都市ごとの状態ではない
            //    （TyphoonWind / VolcanoReader と同じ判断）。
        }

        /// <summary>
        /// 破壊経路を走査するだけの純粋関数。キャッシュを一切触らないので、
        /// **どのスレッドから呼んでもこの型の状態を壊さない**。
        /// <see cref="Assumptions"/>（main スレッド）はこちらを使うこと。
        ///
        /// メソッドは <c>GetMethod</c> で**引数の型まで指定**して見る（②が確立した形）。
        /// 名前だけの <c>GetMethod</c> はオーバーロードで例外を投げるうえ、
        /// シグネチャ変更を見逃す。
        /// </summary>
        public static VolcanoDestructionFacts ScanFacts()
        {
            bool building = HasMethod(typeof(BuildingAI), "CollapseBuilding", new Type[]
            {
                typeof(ushort), typeof(Building).MakeByRefType(),
                typeof(InstanceManager.Group), typeof(bool), typeof(bool), typeof(int)
            });

            bool segment = HasMethod(typeof(NetAI), "CollapseSegment", new Type[]
            {
                typeof(ushort), typeof(NetSegment).MakeByRefType(),
                typeof(InstanceManager.Group), typeof(bool)
            });

            bool release = HasMethod(typeof(NetManager), "ReleaseSegment", new Type[]
            {
                typeof(ushort), typeof(bool)
            });

            return new VolcanoDestructionFacts(building, segment, release);
        }

        /// <summary>
        /// sim スレッド。**必ず <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に建物が消える）。
        ///
        /// <paramref name="frontUnit"/> は<b>隆起の前線 [0,1]</b>である。準備が始まった
        /// ばかり（隆起がまだ動いていない）なら 0 で、前線は
        /// <c>ModSettings.VolcanoClearingLeadMetres</c> だけになる。
        ///
        /// ★★ **進捗そのものを渡さないこと**（<c>VolcanoUplift.GrowthFrontUnit</c> を渡す）。
        ///   火口のぶん円錐を立て直しているので、隆起の前線は進捗より先に出る
        ///   （<c>UpliftSchedule.GrowthFrontUnit</c>）。進捗を渡すと、準備が届く前に
        ///   隆起が届いてしまい、山の外周が切り立った円で止まって見える
        ///   ——**危険側ではない**（<c>ActiveRadiusMetres</c> が書き込みを止める）が、
        ///   毎回そこで待たされる。
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, float frontUnit, float deltaMinutes)
        {
            try
            {
                Step(footprint, frontUnit, deltaMinutes);
            }
            catch (Exception e)
            {
                _lastFailure = "the clearing pass threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano clearing failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcClear",
                             "volcano clearing failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float frontUnit, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            // ★ 火山が入れ替わったら走査位置と実績を捨てる。**中心は動かない**ので、
            //    ここが唯一カーソルを捨てる条件である（クラス doc）。
            if (!_centreValid || !SamePoint(_centre, footprint.Centre))
            {
                float keptMinutes = _minutesSincePass;
                Reset();
                _minutesSincePass = keptMinutes;
                _centre = footprint.Centre;
                _centreValid = true;
            }

            _shapeRadius = footprint.RadiusMetres;

            // ★ 間隔の累積は対象より先に進める（④の TyphoonWind と同じ形。
            //    巻き戻すと走査が 1 度も走らない）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (framesPerMinute <= 0f) return;

            _frontRadius = UpliftSchedule.ClearingFrontMetres(
                footprint.RadiusMetres, frontUnit, LeadMetres());

            // 前線に既に届いているなら走らない。**累積を消費しない**ので、
            // 前線が伸びた次の tick で即座に走る。
            if (_clearedRadius >= _frontRadius) return;

            if (_minutesSincePass < interval) return;

            // 余りを繰り越さない（ロード直後の大きな deltaMinutes で連続発火させない）。
            _minutesSincePass = 0f;

            Sweep(footprint);
        }

        /// <summary>
        /// 1 回ぶんの走査。建物と道路をそれぞれ中心から外へ辿り、前線の内側を取り除く。
        ///
        /// **届いた半径は 2 つの小さいほう**である。片方だけ前線まで届いても、
        /// もう片方が届いていなければそこはまだ「準備できた場所」ではない。
        ///
        /// ★★ <b>ただし「このパスの 2 つの小さいほう」ではない。</b>
        /// 建物と道路は別々に一周しており、**1 周に要するパス数が違う**。
        /// 直接比べると周回位相がずれたとたん永久に前線へ届かなくなるので、
        /// それぞれを単調に憶えて（<c>_buildingReachedRadius</c> /
        /// <c>_segmentReachedRadius</c>）から小さいほうを取る。
        /// </summary>
        private static void Sweep(VolcanoFootprint footprint)
        {
            VolcanoDestructionFacts facts = Facts();
            if (!facts.Usable)
            {
                _lastFailure = "no usable destruction path in this build of the game";
                return;
            }

            _lastFailure = null;

            bool buildingsCapped;
            float buildingsReached;
            int buildingsScanned = ClearBuildings(footprint, out buildingsCapped,
                                                  out buildingsReached);

            bool segmentsCapped;
            float segmentsReached;
            int segmentsScanned = ClearSegments(footprint, out segmentsCapped,
                                                out segmentsReached);

            // ★★ **それぞれの側で単調に憶えてから、小さいほうを取る。**
            //    このパスの 2 つを直接比べてはいけない —— 1 周に要するパス数が
            //    2 本で違うので周回位相がずれ、**同じパスで両方が前線に届くことが
            //    二度と無くなる**（<see cref="_buildingReachedRadius"/> の由来）。
            //    意味は変わらない: 小さいほうは今も「両方が走査し終えた半径」である。
            if (buildingsReached > _buildingReachedRadius)
            {
                _buildingReachedRadius = buildingsReached;
            }
            if (segmentsReached > _segmentReachedRadius)
            {
                _segmentReachedRadius = segmentsReached;
            }

            float reached = _buildingReachedRadius < _segmentReachedRadius
                ? _buildingReachedRadius
                : _segmentReachedRadius;
            if (reached > _clearedRadius) _clearedRadius = reached;

            _passes++;
            _lastScanned = buildingsScanned + segmentsScanned;
            _lastCapped = buildingsCapped || segmentsCapped;

            WriteDiag();
        }

        /// <summary>
        /// 準備の前線が隆起の前線より何メートル先を走るか。
        ///
        /// **負にしない** —— 負にすると「まだ壊していない場所を上げる」ことになり、
        /// 罠 1 そのものになる。
        ///
        /// ★★ **0 にもしない。** 準備と隆起は ring lockstep で噛み合っている ——
        /// 隆起してよい半径は準備が届いた半径で、準備の前線は隆起の進捗で決まる。
        /// 先行距離が 0 だと進捗 0 のとき前線も 0 になり、
        /// <b>何も壊さない → 何も上がらない → 進捗が動かない</b>の輪から永久に出られない。
        /// 例外は 1 つも出ず、位相が <c>Clearing</c> のまま黙って止まる。
        /// 下限は raw セル 1 つ分（16 m）—— それより細かい先行に意味は無い
        /// （地形の格子がそれ以上細かくならないため）。
        /// </summary>
        private static float LeadMetres()
        {
            int lead = ModSettings.VolcanoClearingLeadMetres.value;
            return lead > MinLeadMetres ? lead : MinLeadMetres;
        }

        /// <summary>
        /// 道路グリッドが実測どおり <see cref="GridSide"/>² か（§F-15）。
        /// **推測で走らない**（設計書 §6）—— 合わないまま <c>z*270+x</c> で引くと、
        /// まったく別の場所の道路を壊す。
        ///
        /// <c>NetManager</c> がまだ居ないとき（メインメニュー・前提検証）は
        /// <b>true</b> を返す。「まだ読めていない」を「使えない」と名乗らないためで、
        /// 実際に壊す直前には必ず居る。
        /// </summary>
        private static bool SegmentGridUsable()
        {
            try
            {
                if (!Singleton<NetManager>.exists) return true;

                var nm = Singleton<NetManager>.instance;
                if (nm == null) return true;

                var grid = nm.m_segmentGrid;
                return grid != null && grid.Length == GridSide * GridSide;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>破壊経路の実測をキャッシュ越しに返す。**sim スレッド専用**（キャッシュを書く）。</summary>
        private static VolcanoDestructionFacts Facts()
        {
            if (_factsScanned) return _facts;

            _factsScanned = true;
            _facts = ScanFacts();
            return _facts;
        }

        private static bool HasMethod(Type declaringType, string name, Type[] parameterTypes)
        {
            try
            {
                return declaringType.GetMethod(name,
                    BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>1/64 m（raw 1 単位）より細かい差は「同じ地点」とみなす。</summary>
        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        /// <summary>
        /// **破壊 0 のときも毎回出す。** 「機能が死んでいる」と「範囲内に何も無い」が
        /// ログ上で区別できなくなる（③で実際に起きた形）。
        ///
        /// <c>Log.Diag</c> は同一キーで 512 sim フレームに 1 回に間引かれるが、
        /// **引数の文字列連結は毎回走ってしまう**ので <c>DiagEnabled</c> で先に落とす
        /// （C# は引数を呼び出し前に評価し切る）。
        /// </summary>
        private static void WriteDiag()
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcClear",
                "clearing pass#" + _passes
                + " front=" + _frontRadius.ToString("F0")
                + " cleared=" + _clearedRadius.ToString("F0")
                + "/" + _shapeRadius.ToString("F0")
                // ★ 2 本を別々に出す。片方だけが止まっているのが、揃えた 1 つの数からは
                //   読めなかった（上の _buildingReachedRadius の由来）。
                + " reach b=" + _buildingReachedRadius.ToString("F0")
                + " r=" + _segmentReachedRadius.ToString("F0")
                + " scanned=" + _lastScanned
                + " buildings=" + _lastBuildingsDestroyed + " (refused " + _lastBuildingsRefused + ")"
                + " roads=" + _lastSegmentsDestroyed + " (refused " + _lastSegmentsRefused + ")"
                + " total=" + _totalBuildingsDestroyed + "/" + _totalSegmentsDestroyed
                + (_lastCapped ? " (capped; the front was not reached this pass)" : ""));
        }
    }
}
