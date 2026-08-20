using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤が地形を書けるかどうか。**Task 2 の主目的そのもの**である。
    ///
    /// ①はバニラのハザードマップを、②はバニラの決定論的な被害モデルを見せた。
    /// ④は嵐プレハブの実数値を実機で測った。**⑤が実機でしか測れないのはこれ** ——
    /// <c>RawHeights</c> の実寸と、地形を書き換える 4 つの到達経路である。
    /// ここが揃わなければ⑤は 1 メートルも山を上げない（<see cref="Usable"/>）。
    ///
    /// struct にしているのは④の <see cref="TyphoonPrefabFacts"/> と同じ理由
    /// （bool と int しか持たないので、キャッシュしても Unity の fake-null
    /// 自己修復問題を持ち込まない）。既定値は全て false ＝「まだ／もう読めていない」。
    ///
    /// **フラグは 1 本にまとめない。** 山（<see cref="HeightsResolved"/> /
    /// <see cref="UpdateAreaResolved"/>）・火口と焦げ（<see cref="CraterResolved"/> /
    /// <see cref="BurnGroundResolved"/>）・溶岩の流路（<see cref="SlopeSampleResolved"/>）は
    /// 独立に壊れうる。まとめると「溶岩が流れないだけ」の環境で山まで止まる。
    /// </summary>
    public struct VolcanoTerrainFacts
    {
        /// <summary>マップ全体の raw セル数（1081²）。§A-1 の <c>TerrainManager.Awake</c> 実測。</summary>
        public const int ExpectedRawArrayLength = 1168561;

        /// <summary><c>TerrainManager.RawHeights</c>（<c>ushort[]</c>）を取れたか。</summary>
        public readonly bool HeightsResolved;

        /// <summary>
        /// <c>RawHeights.Length</c>。**1081² = 1168561 でなければ以後のセル計算が全部ずれる**
        /// （<c>index = z*1081 + x</c>）。読めなかったときは 0。
        /// </summary>
        public readonly int RawArrayLength;

        /// <summary>
        /// <c>TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool)</c> を解決できたか。
        /// **書いた高さをゲームへ反映する唯一の経路**（§A-1）。
        /// </summary>
        public readonly bool UpdateAreaResolved;

        /// <summary><c>DisasterHelpers.MakeCrater(Vector2,float,float,bool)</c>（§C-8）。</summary>
        public readonly bool CraterResolved;

        /// <summary><c>DisasterHelpers.BurnGround(Vector2,float,float)</c>（§B-7b。**DLC 不要**）。</summary>
        public readonly bool BurnGroundResolved;

        /// <summary>
        /// <c>TerrainManager.SampleDetailHeight(Vector3, out float, out float)</c>（§B-6）。
        /// **溶岩が下り方向を見つける唯一の経路。** 山と準備と噴火はこれに依存しない。
        /// </summary>
        public readonly bool SlopeSampleResolved;

        /// <summary>
        /// Natural Disasters DLC を持っているか。**⑤は DLC を要らない**（設計書 §1.4）。
        /// 分岐するのは樹木の着火だけ（<c>TreeManager.BurnTree</c> は DLC ゲート、§B-7c）なので、
        /// **false でも FAIL 扱いにしない。**
        /// </summary>
        public readonly bool NaturalDisastersOwned;

        public VolcanoTerrainFacts(bool heightsResolved, int rawArrayLength,
                                   bool updateAreaResolved, bool craterResolved,
                                   bool burnGroundResolved, bool slopeSampleResolved,
                                   bool naturalDisastersOwned)
        {
            HeightsResolved = heightsResolved;
            RawArrayLength = rawArrayLength;
            UpdateAreaResolved = updateAreaResolved;
            CraterResolved = craterResolved;
            BurnGroundResolved = burnGroundResolved;
            SlopeSampleResolved = slopeSampleResolved;
            NaturalDisastersOwned = naturalDisastersOwned;
        }

        /// <summary>
        /// ⑤が山を 1 つでも作ってよいか。**これが⑤全体の門である。**
        ///
        /// 配列そのものと、書いた値をゲームへ反映する経路の 2 つが要る。
        /// 長さが 1081² でないときに「とりあえず書く」をやると、
        /// <c>z*1081 + x</c> の添字が別のセルを指し、**マップの無関係な場所が隆起する**。
        /// 推測で代替しない（設計書 §6）。
        ///
        /// **前提検証（<c>Assumptions.Volcano</c>）はこの式そのものを述語にすること。**
        /// 「フィールドが解決した」を述語にすると、値が使えない場合に PASS が出る
        /// （④のレビューと②の監査が同じ欠陥を見つけている）。
        /// </summary>
        public bool Usable
        {
            get
            {
                return HeightsResolved
                       && RawArrayLength == ExpectedRawArrayLength
                       && UpdateAreaResolved;
            }
        }
    }

    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// ①の <c>WeatherSnapshot</c>・②の <c>EarthquakeSnapshot</c>・
    /// ④の <see cref="TyphoonSnapshot"/> と同じ規律で、**一度作ったら書き換えない**。
    ///
    /// **T3 以降がフィールドを足していく。追加は必ず ctor の末尾に付けること**
    /// （既存の呼び出し側を全部直させないため）。
    ///
    /// ⑤の表示規約: ここに載る値のうちバニラの実測なのは
    /// <see cref="Terrain"/>（配列の実寸と到達経路）と <see cref="GameMode"/> だけで、
    /// それ以外は全て本 MOD が決めた量である（設計書 §7.4）。
    /// </summary>
    public class VolcanoSnapshot
    {
        /// <summary>読み取りに成功したか。false なら表示側は「読み取れません」と出す。</summary>
        public readonly bool Valid;

        /// <summary>地形 API の実測。**⑤が動けるかどうかはここだけで決まる。**</summary>
        public readonly VolcanoTerrainFacts Terrain;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>。</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// ゲームモードか（マップエディタなら false）。
        /// <c>m_blockHeights</c> の追随速度がゲームで上へ 2 m、エディタで 8 m に変わる（§A-2）。
        /// **読めなければゲームモードとみなす**（遅いほうを名乗る）。
        /// </summary>
        public readonly bool GameMode;

        /// <summary>
        /// この tick の頭での位相（T4 以降）。
        ///
        /// ★ <b>これは 1 tick 前の状態である。</b> <c>VolcanoFeature.OnSimulationTick</c> は
        /// 「読んで publish する」をポーズガードより上で行い、位相を進める
        /// <c>VolcanoState.Tick</c> はその下にある。ポーズ中でもパネルが凍らないよう
        /// この順序にしてあるので、位相の反映は設計上 1 tick 遅れる
        /// （④の <see cref="TyphoonSnapshot"/> も同じ扱い）。
        /// </summary>
        public readonly VolcanoPhase Phase;

        /// <summary>直近の調査結果。<c>Valid == false</c> なら「まだ調べていない」。</summary>
        public readonly VolcanoFootprint Footprint;

        /// <summary>
        /// 隆起の進捗 [0,1]。**T6 が動かすまで常に 0 である。**
        /// <b>0 のうちは行にしないこと</b>（<see cref="VolcanoState.ProgressUnit"/> の doc）。
        /// </summary>
        public readonly float ProgressUnit;

        /// <summary>直近に断った理由（**英語・診断用**）。断っていなければ null。</summary>
        public readonly string Refusal;

        /// <summary>確認の直前に設定が変わったので調べ直したか。</summary>
        public readonly bool SettingsChanged;

        // ── T5（準備）が足した 7 つ ────────────────────────────────

        /// <summary>
        /// 準備の走査が届いた半径（m）。**T6 が隆起してよい半径そのもの**である
        /// （<c>VolcanoClearing.ClearedRadiusMetres</c>）。**表示専用のコピーであり、
        /// T6 はこれではなく sim 側の static を読むこと**（スナップショットは
        /// 設計上 1 tick 遅れる）。
        /// </summary>
        public readonly float ClearedRadiusMetres;

        /// <summary>準備が山の半径まで届いたか。</summary>
        public readonly bool ClearingComplete;

        /// <summary>この火山でこれまでに取り除いた建物数。</summary>
        public readonly int BuildingsDestroyed;

        /// <summary>この火山でこれまでに取り除いた道路セグメント数。</summary>
        public readonly int SegmentsDestroyed;

        /// <summary>
        /// 直近の走査でバニラが取り除きを断った建物数。**0 でないのは異常ではない**が、
        /// その足元だけは地形が元の高さに残る（<c>VolcanoClearing</c> のクラス doc）。
        /// </summary>
        public readonly int BuildingsRefused;

        /// <summary>直近の走査が 1 回ぶんの上限で打ち切られたか。</summary>
        public readonly bool ClearingCapped;

        /// <summary>
        /// 準備（道路と建物を取り除くこと）の経路がこのゲームのビルドで成立するか。
        /// **false なら⑤は火山を 1 つも作らない**（設計書 §1.2）。
        ///
        /// ★ 道路だけでなく建物側（<c>CollapseBuilding</c>）も含む
        /// （全体レビュー M9。<c>VolcanoClearing.ClearingPathAvailable</c> の doc）。
        /// </summary>
        public readonly bool ClearingPathAvailable;

        // ── T6（隆起）が足した 6 つ ────────────────────────────────
        //
        // ★ 隆起の進捗そのものは新しいフィールドを作らず <see cref="ProgressUnit"/> を
        //   使う（T2 から在って T5 まで常に 0 だった）。進捗を 2 つ持つと、
        //   いつか片方だけ更新される。

        /// <summary>今の山頂の盛り上がり（m）。**元の地形高さからの相対量**である。</summary>
        public readonly float SummitMetres;

        /// <summary>
        /// 今この tick に上げてよい半径（m）＝**準備が届いた範囲**。
        /// パネルはそう添えて出す —— 罠 1（準備より先に上げる）を実機で目で
        /// 確かめられる唯一の行である。
        /// </summary>
        public readonly float ActiveRadiusMetres;

        /// <summary>隆起が終わったか。</summary>
        public readonly bool UpliftComplete;

        /// <summary>山頂の火口を彫ったか（⑤全体で 1 回だけ起きる）。</summary>
        public readonly bool CraterCarved;

        /// <summary>影響矩形を覆うタイル数。</summary>
        public readonly int UpliftTileCount;

        /// <summary>次に <c>UpdateArea</c> するタイルの番号。</summary>
        public readonly int UpliftTileCursor;

        // ── T7（噴火）が足した 3 つ ────────────────────────────────
        //
        // ★ この 3 つは**描画のためだけ**に在る。ゲームの状態を 1 つも表さないので、
        //   sim 側（VolcanoState / VolcanoClearing / VolcanoUplift）はこれを読まない。

        /// <summary>噴火が進行中か。main の <c>VolcanoEruptionFx.Update</c> の唯一の門。</summary>
        public readonly bool EruptionActive;

        /// <summary>
        /// 噴出の強さ <c>[0,1]</c>。**本 MOD が決めた量**であって、ゲームが
        /// 計算した値ではない（設計書 §7.4）。実在の物理単位は名乗らない。
        /// </summary>
        public readonly float EruptionIntensityUnit;

        /// <summary>
        /// 噴出口のワールド座標（<c>Y</c> は <c>SampleDetailHeight</c> ＋ 少しの浮き）。
        /// sim が読んだ値を main がそのまま使う ——
        /// **main スレッドから地形を引き直さない**（経路を 1 本にする）。
        /// </summary>
        public readonly Vec3 SummitWorld;

        // ── T8（溶岩）が足した 9 つ ───────────────────────────────

        /// <summary>火口から出した流れの本数（設定の値。0 なら完全に無効）。</summary>
        public readonly int LavaFlowCount;

        /// <summary>まだ動いている流れの本数。</summary>
        public readonly int LavaAliveCount;

        /// <summary>いちばん長く流れた距離（m）。</summary>
        public readonly float LavaLongestMetres;

        /// <summary>これまでに火を付けた建物の数。</summary>
        public readonly int LavaBuildingsIgnited;

        /// <summary>これまでに火を付けた木の数（ND 非所持なら常に 0）。</summary>
        public readonly int LavaTreesIgnited;

        /// <summary>木に火を付けられる環境か（＝ ND DLC を持っているか。§B-7c）。</summary>
        public readonly bool LavaTreesAvailable;

        /// <summary>
        /// 全流路の軌跡点を連結した**不変配列**。T9 の描画が読む唯一の口である。
        ///
        /// ★ <b>計画は <c>LavaHeads</c>（1 本の <c>Vec2[]</c>）と書いていたが、
        /// 点列と本数の 2 つに分けてある。</b> 1 本の配列では **N 本の別々の折れ線を
        /// 表せない** —— 別の流れの先端どうしを繋いだリボンは、流れの間を
        /// 飛び回る帯になる。<see cref="LavaTrailCounts"/> が境目を持つ。
        /// </summary>
        public readonly Vec2[] LavaTrailPoints;

        /// <summary>各流路の点数（**不変配列**）。合計が <see cref="LavaTrailPoints"/> の長さ。</summary>
        public readonly int[] LavaTrailCounts;

        /// <summary>
        /// 冷え具合 <c>[0,1]</c>。1 が「まだ熱い」、0 が「冷え切った」。
        /// T9 の描画がこれで色を落とす（止まった溶岩が永久に光っていないこと）。
        /// </summary>
        public readonly float LavaCoolUnit;

        public VolcanoSnapshot(bool valid, VolcanoTerrainFacts terrain,
                               uint currentFrame, bool gameMode,
                               VolcanoPhase phase, VolcanoFootprint footprint,
                               float progressUnit, string refusal, bool settingsChanged,
                               float clearedRadiusMetres, bool clearingComplete,
                               int buildingsDestroyed, int segmentsDestroyed,
                               int buildingsRefused, bool clearingCapped,
                               bool clearingPathAvailable,
                               float summitMetres, float activeRadiusMetres,
                               bool upliftComplete, bool craterCarved,
                               int upliftTileCount, int upliftTileCursor,
                               bool eruptionActive, float eruptionIntensityUnit,
                               Vec3 summitWorld,
                               int lavaFlowCount, int lavaAliveCount, float lavaLongestMetres,
                               int lavaBuildingsIgnited, int lavaTreesIgnited,
                               bool lavaTreesAvailable, Vec2[] lavaTrailPoints,
                               int[] lavaTrailCounts, float lavaCoolUnit)
        {
            Valid = valid;
            Terrain = terrain;
            CurrentFrame = currentFrame;
            GameMode = gameMode;
            Phase = phase;
            Footprint = footprint;
            ProgressUnit = progressUnit;
            Refusal = refusal;
            SettingsChanged = settingsChanged;
            ClearedRadiusMetres = clearedRadiusMetres;
            ClearingComplete = clearingComplete;
            BuildingsDestroyed = buildingsDestroyed;
            SegmentsDestroyed = segmentsDestroyed;
            BuildingsRefused = buildingsRefused;
            ClearingCapped = clearingCapped;
            ClearingPathAvailable = clearingPathAvailable;
            SummitMetres = summitMetres;
            ActiveRadiusMetres = activeRadiusMetres;
            UpliftComplete = upliftComplete;
            CraterCarved = craterCarved;
            UpliftTileCount = upliftTileCount;
            UpliftTileCursor = upliftTileCursor;
            EruptionActive = eruptionActive;
            EruptionIntensityUnit = eruptionIntensityUnit;
            SummitWorld = summitWorld;
            LavaFlowCount = lavaFlowCount;
            LavaAliveCount = lavaAliveCount;
            LavaLongestMetres = lavaLongestMetres;
            LavaBuildingsIgnited = lavaBuildingsIgnited;
            LavaTreesIgnited = lavaTreesIgnited;
            LavaTreesAvailable = lavaTreesAvailable;
            LavaTrailPoints = lavaTrailPoints;
            LavaTrailCounts = lavaTrailCounts;
            LavaCoolUnit = lavaCoolUnit;
        }

        /// <summary>
        /// 読み取りに失敗したときの 1 個。**0 を並べた「それらしい」値を作らない。**
        ///
        /// ★ <see cref="ClearingPathAvailable"/> だけ <b>true</b> を入れる。ここが false だと
        /// パネルは「取り除けないので火山は作りません」という**確定的な断り**を
        /// 出すが、この 1 個が言えるのは「今回の読み取りが失敗した」だけである。
        /// 読めなかったことを、測って分かった結論として名乗らない。
        /// </summary>
        public static VolcanoSnapshot Invalid()
        {
            return new VolcanoSnapshot(false, new VolcanoTerrainFacts(), 0u, true,
                                       VolcanoPhase.Idle, VolcanoFootprint.None, 0f, null, false,
                                       0f, false, 0, 0, 0, false, true,
                                       0f, 0f, false, false, 0, 0,
                                       false, 0f, new Vec3(0f, 0f, 0f),
                                       0, 0, 0f, 0, 0, false,
                                       new Vec2[0], new int[0], 0f);
        }
    }
}
