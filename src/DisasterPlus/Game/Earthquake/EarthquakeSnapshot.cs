using System.Collections.Generic;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <c>EarthquakeAI</c> プレハブに焼き込まれている 4 つの調整値。
    ///
    /// **この 4 個の実数値は DLL に存在しない**（IL 事実文書 §A-0）。プレハブの
    /// シリアライズ値なので IL 逆アセンブルでは見えず、UnityPy による
    /// <c>sharedassets</c> の読み出しも型ツリーが読めずに失敗している。したがって
    /// **実行時に <c>DisasterManager.FindDisasterInfo&lt;EarthquakeAI&gt;()</c> から読んで
    /// 診断ダンプに出すのが唯一の入手経路**であり、それが Task 3 の主目的である。
    /// ②の以後の持続時間の設計は全てこの 4 個の上に乗る。
    ///
    /// struct にしているのは、キャッシュしても Unity の fake-null 自己修復問題を
    /// 持ち込まないため（float / uint しか持たないので <c>DisasterInfo</c> の参照を
    /// 抱え込まずに済む）。既定値は <see cref="Resolved"/> == false ＝「まだ／もう読めていない」。
    /// </summary>
    public struct EarthquakePrefabFacts
    {
        /// <summary>4 値を実際に読めたか。false のとき他のフィールドは全て 0 で、意味を持たない。</summary>
        public readonly bool Resolved;

        public readonly float CrackLength;
        public readonly float CrackWidth;
        public readonly uint EmergingDuration;
        public readonly uint ActiveDuration;

        public EarthquakePrefabFacts(float crackLength, float crackWidth,
                                     uint emergingDuration, uint activeDuration)
        {
            Resolved = true;
            CrackLength = crackLength;
            CrackWidth = crackWidth;
            EmergingDuration = emergingDuration;
            ActiveDuration = activeDuration;
        }
    }

    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// ①の <see cref="WeatherSnapshot"/> と同じ規律で、**一度作ったら書き換えない**。
    ///
    /// </summary>
    public class EarthquakeSnapshot
    {
        /// <summary>
        /// 地震が 1 個も無いときに全スナップショットで共有する空リスト。
        /// sim tick ごとの空リスト確保を避けるためだけにある。
        ///
        /// <c>ReadOnlyCollection</c> で包んでいるのは意図的。素の <c>List</c> のまま
        /// <c>IList</c> として配ると <c>snapshot.Quakes.Add(...)</c> がコンパイルも実行も
        /// 通ってしまい、**共有された空リストを 1 箇所が汚すと以後の全スナップショットが
        /// 壊れる**。ここで包んでおけば、その誤用は最初の 1 回で即座に例外になる。
        /// </summary>
        private static readonly IList<EarthquakeReading> NoQuakes =
            new List<EarthquakeReading>(0).AsReadOnly();

        /// <summary>
        /// 今この瞬間、生きている（Created かつ Deleted でない）地震。
        /// **構築後に変更しないこと**（<see cref="EarthquakeReader"/> は読み取り専用に
        /// 包んでから渡している）。破られると main スレッドが列挙している最中に
        /// sim スレッドが書き換えることになり、スタックトレースの出ない例外に化ける。
        /// </summary>
        public readonly IList<EarthquakeReading> Quakes;

        public readonly EarthquakePrefabFacts Prefab;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>。表示側が残り時間を出す基準。</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// sim スレッドの時刻（0 〜 23.99963）。
        /// <c>m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR</c> であって
        /// <c>m_currentDayTimeHour</c> ではない（§F-1。詳細は <see cref="EarthquakeReader"/>）。
        /// </summary>
        public readonly float HourOfDay;

        /// <summary>
        /// <c>SimulationManager.m_enableDayNight</c>。
        ///
        /// **false のとき <see cref="HourOfDay"/> は永久に 12.0 に固定される**
        /// （sim スレッドが毎フレーム <c>m_dayTimeOffsetFrames</c> を再設定するため、§F-1）。
        /// これはバグでもゲームの不整合でもなく、プレイヤーが選べる正当な設定なので
        /// 前提検証の FAIL にはしない。ただし**表示側はこの事実を隠さないこと**
        /// （12.0 という数字だけを出すと「正午に固定された時計」が読めた値に見える）。
        /// </summary>
        public readonly bool DayNightEnabled;

        /// <summary>読み取りに成功したか。false なら表示側は「読み取れません」と出す。</summary>
        public readonly bool Valid;

        /// <summary>
        /// main スレッドが publish したカーソル座標の直下にあった建物 1 個の余裕度
        /// （<see cref="BuildingProbe"/> が sim スレッドで作る）。
        ///
        /// 建物が見つからなかった／カーソルが無効だったときは
        /// <c>HasBuilding == false</c> の <see cref="BuildingMargin.None"/>。
        /// **main スレッドはこれを描くだけで、建物バッファには一切触らない。**
        /// </summary>
        public readonly BuildingMargin CursorBuilding;

        /// <summary>
        /// <see cref="CursorBuilding"/> がどの地震についての判定か（災害バッファ上の添字）。
        ///
        /// **0 は「建物が無かった」ではなく「そもそも調べていない」**——カーソルが
        /// 無効（パネルが閉じている／マウスが UI の上／地形を外している）か、
        /// 破壊判定が走る地震（Active / Emerging）が 1 つも無かったか。
        /// 「調べたが建物が無かった」は <c>CursorQuakeId != 0</c> かつ
        /// <c>CursorBuilding.HasBuilding == false</c> で表す。この 2 つを混ぜると、
        /// 建物の上にカーソルを置いているのに「建物がありません」と出る。
        ///
        /// **表示側はこれを必ず出すこと。** 地震は同時に複数進行しうる（§E-1）。
        /// 1 個ぶんの判定だけを出して黙っていると、他の地震について何も言っていない
        /// ことが読み手に伝わらず、また「確信を持って誤った数値」になる。
        /// </summary>
        public readonly ushort CursorQuakeId;

        /// <summary>
        /// カーソル直下の建物走査が**どう終わったか**（<see cref="BuildingProbeOutcome"/>）。
        ///
        /// <see cref="CursorQuakeId"/> だけでは「調べたが建物が無かった」と
        /// 「調べようとして失敗した」を区別できない。前者は実測値、後者は読み取り失敗で、
        /// 同じ文言にすると読み取り失敗が実測値の顔で出てくる。
        /// </summary>
        public readonly BuildingProbeOutcome CursorProbe;

        /// <summary>
        /// <see cref="CursorBuilding"/> の高さ（m）。**第 2 層（長周期地震動）専用。**
        ///
        /// **0 は「低い」ではなく「読めなかった」**（<see cref="BuildingHeight.MetresOf"/>）。
        /// 表示側はこの 2 つを混ぜてはいけない —— 混ぜると、高さが読めない環境で
        /// 「この建物は低いので長周期の影響を受けません」という、**根拠の無い断定**が出る。
        ///
        /// バニラはこの量を揺れにも被害にも一切使っていない（§A-7 / §A-3）。
        /// したがってこれを使う行は必ず第 2 層である。
        /// </summary>
        public readonly float CursorBuildingHeight;

        /// <summary>
        /// カーソル地点の <c>ImmaterialResourceManager.Resource.EarthquakeCoverage</c> の生値。
        /// <see cref="CursorCoverageValid"/> が false のときこの値は無意味。
        ///
        /// **これはリードタイムの根拠ではない。** バニラがリードタイムに使うのは
        /// **震央**のカバレッジ 1 点だけで（<see cref="EarthquakeReading.CoverageAtEpicentre"/>、
        /// §A-2）、カーソル地点の値は「今この場所に地震計が届いているか」を
        /// プレイヤーが確かめるためだけにある。この 2 つを取り違えて
        /// カーソル地点からリードタイムを出すと、地震計を建てる場所の判断が丸ごと狂う。
        /// </summary>
        public readonly int CursorCoverage;

        /// <summary>
        /// カーソル地点のカバレッジを実際に読めたか。
        ///
        /// <see cref="EarthquakeReading.CoverageKnown"/> と同じ理由で分けてある。
        /// **カバレッジ 0 は「地震計が届いていない」という意味のある実測値**であり、
        /// 本機能の看板の説明そのものなので、読み取り失敗と同じ 0 に潰してはいけない。
        ///
        /// false になるのは 2 通り —— main スレッドがまだ有効なカーソル座標を
        /// publish していない（パネルが閉じている／マウスが UI の上／地形を外している）か、
        /// <c>ImmaterialResourceManager</c> が読めなかったか。表示側は自分が持っている
        /// 「今カーソルが地形の上にあるか」と突き合わせて、この 2 つを言い分ける。
        /// </summary>
        public readonly bool CursorCoverageValid;

        /// <summary>
        /// 地震計の位置で観測した地動の波形。**震央に近い順**に並ぶ（先頭が最も近い）。
        ///
        /// これは<b>ゲーム内のセンサーが計測した値ではない</b> ——
        /// <c>EarthquakeSensorAI</c> は時系列データを一切持たない（§C-1）。
        /// この MOD が**バニラ自身の揺れの式**（§A-7）を地震計の位置で評価して
        /// 貯めたものである（<see cref="SeismographRecorder"/> のクラス doc）。
        ///
        /// 空になるのは 2 通りで、**表示側はこれを言い分けること**:
        ///   - <see cref="WaveformQuakeId"/> == 0 … そもそも記録していない（地震が無い）
        ///   - <see cref="WaveformQuakeId"/> != 0 … 記録対象の地震はあるが地震計が 0 個
        ///
        /// さらに「観測点はあるがサンプルが 0 件」（本震前で揺れの窓がまだ開いていない）は
        /// <c>SeismographTrace.Count == 0</c> で表す。**空のグラフと平らなグラフは
        /// 別の意味**なので、0 件を「変位 0」として描いてはいけない。
        /// </summary>
        public readonly IList<SeismographTrace> Traces;

        /// <summary>
        /// <see cref="Traces"/> がどの地震についての記録か（災害バッファ上の添字）。
        /// **0 は「記録していない」** —— 進行中（Emerging|Active）の地震が 1 つも無い。
        /// </summary>
        public readonly ushort WaveformQuakeId;

        /// <summary>
        /// **第 2 層。** 海中震源からの津波連鎖が今どうなっているか
        /// （<see cref="TsunamiChain"/>）。これはバニラが計算している量ではなく、
        /// **本 MOD が発明した挙動の状態**なので、表示側は必ず第 2 層の行として出す。
        ///
        /// <see cref="TsunamiChain"/> の静的状態を main スレッドから直接読まないための
        /// 経路である。<c>EarthquakeReader.Read()</c> は <c>TsunamiChain.Tick()</c> より
        /// **前**に走るので、ここに載るのは最大 1 tick 前の状態になる
        /// （<see cref="CursorBuilding"/> が既に 1 tick 遅れているのと同じ性質の遅延）。
        /// </summary>
        public readonly TsunamiChainState TsunamiState;

        /// <summary>
        /// 津波の予約が満了するフレーム。<see cref="TsunamiState"/> が
        /// <see cref="TsunamiChainState.Scheduled"/> のときだけ意味を持つ。
        /// </summary>
        public readonly uint TsunamiDueFrame;

        /// <summary>
        /// 津波連鎖が監視している地震（災害バッファ上の添字）。0 なら監視していない。
        /// **表示側はこれを名乗る** —— 地震は同時に複数進行しうるので（§E-1）、
        /// どの地震から連鎖したのかを黙っていると、他の地震について何も
        /// 言っていないことが読み手に伝わらない。
        /// </summary>
        public readonly ushort TsunamiQuakeId;

        /// <summary>
        /// **第 2 層。** 長周期の直近の走査が 1 回ぶんの上限で打ち切られたか
        /// （<c>LongPeriodDamage.LastCapped</c>）。
        ///
        /// 走査は震央から外へ向かうので（<c>OutwardCellOrder</c>）、打ち切りが起きても
        /// 震央の周りは必ず評価済みである。しかし**外側はまだ評価されていない**ので、
        /// 遠くの建物について「追加倒壊リスク N.N%」とだけ出すと、この走査では
        /// まだ抽選されていない確率を確定値の顔で出すことになる（第 2 層レビュー I1）。
        /// 表示側はこれを注記として必ず名乗ること。
        ///
        /// <see cref="TsunamiState"/> と同じく、<c>EarthquakeReader.Read()</c> は
        /// <c>LongPeriodDamage.Apply()</c> より**前**に走るので、載るのは最大 1 tick
        /// 前の状態である。
        /// </summary>
        public readonly bool LongPeriodCapped;

        public EarthquakeSnapshot(IList<EarthquakeReading> quakes, EarthquakePrefabFacts prefab,
                                  uint currentFrame, float hourOfDay, bool dayNightEnabled,
                                  BuildingMargin cursorBuilding, ushort cursorQuakeId,
                                  BuildingProbeOutcome cursorProbe, float cursorBuildingHeight,
                                  int cursorCoverage, bool cursorCoverageValid,
                                  IList<SeismographTrace> traces, ushort waveformQuakeId,
                                  TsunamiChainState tsunamiState, uint tsunamiDueFrame,
                                  ushort tsunamiQuakeId, bool longPeriodCapped,
                                  bool valid)
        {
            LongPeriodCapped = longPeriodCapped;
            TsunamiState = tsunamiState;
            TsunamiDueFrame = tsunamiDueFrame;
            TsunamiQuakeId = tsunamiQuakeId;
            Traces = traces == null ? SeismographRecorder.EmptyTraceList : traces;
            WaveformQuakeId = waveformQuakeId;
            Quakes = quakes == null ? NoQuakes : quakes;
            Prefab = prefab;
            CurrentFrame = currentFrame;
            HourOfDay = hourOfDay;
            DayNightEnabled = dayNightEnabled;
            CursorBuilding = cursorBuilding;
            CursorQuakeId = cursorQuakeId;
            CursorProbe = cursorProbe;
            CursorBuildingHeight = cursorBuildingHeight;
            CursorCoverage = cursorCoverage;
            CursorCoverageValid = cursorCoverageValid;
            Valid = valid;
        }

        public static EarthquakeSnapshot Invalid()
        {
            return new EarthquakeSnapshot(NoQuakes, new EarthquakePrefabFacts(), 0u, 0f, false,
                                          BuildingMargin.None(), 0,
                                          BuildingProbeOutcome.NotProbed, 0f, 0, false,
                                          SeismographRecorder.EmptyTraceList, 0,
                                          TsunamiChainState.Idle, 0u, 0, false, false);
        }

        /// <summary>地震が 1 個も無いときに使う共有の空リスト。読み取り側専用。</summary>
        public static IList<EarthquakeReading> EmptyQuakeList
        {
            get { return NoQuakes; }
        }
    }
}
