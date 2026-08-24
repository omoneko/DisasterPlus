namespace DisasterPlus.Game
{
    /// <summary>
    /// 全ての表示文字列。英語を既定値として持ち、LocaleLoader がフィールド名をキーに
    /// リフレクションで上書きする。
    ///
    /// 必ず public static フィールドにすること（const にすると書き換えられない）。
    /// ドロップダウン用の配列をここに static readonly で置いてはいけない。
    /// 型初期化時の言語で凍結する。必要ならメソッドにして毎回組み直す。
    /// </summary>
    public static class Strings
    {
        public static string ModDescription =
            "Adds realistic disaster phenomena: fire whirls, typhoons, volcanoes and hazard visualisation.";

        public static string GroupFireWhirl = "Fire whirl";
        public static string GroupGeneral = "General";

        public static string FireWhirlEnabled = "Enable fire whirls";
        // ★★ 海溝型地震（2026-08-22、所有者の依頼「海溝型地震を追加したいです…
        //   アイコンも新規で…左クリックした場所に一番近い海で発生に」）。
        //
        //   ★ ラベルは**押した場所では起きない**ことを名乗る。名乗らないと、
        //     指した山の上で地震が起きなかったのを不具合と読まれる。
        public static string TrenchQuakeEnabled =
            "Show the trench earthquake tile (the only quake that brings a tsunami)";

        public static string TrenchQuakeButtonLabel = "Trench quake";

        public static string TrenchQuakeButtonTooltip =
            "Raise a megathrust earthquake at the sea nearest to the point you click. "
            + "This is the only earthquake that brings a tsunami - the game's own "
            + "(fault) earthquakes no longer do.";

        public static string TrenchQuakeNeedsDlc =
            "The Natural Disasters DLC is required for the trench earthquake.";

        // ★ 火災旋風のバニラ竜巻抑止（2026-08-22、所有者の指示
        //   「バニラの竜巻は発生しないようにしてください」）。
        public static string NoVanillaTornado =
            "Stop the game from spawning its own tornadoes";

        public static string DetectRadius = "Detection radius (m)";
        public static string DetectCount = "Buildings required";
        public static string MaxLifetime = "Maximum lifetime (in-game minutes)";
        public static string SpreadStrength = "Fire spread strength (0 = off)";
        public static string MinSeparation = "Minimum separation (m)";

        public static string IntensityUnlock = "Unlock disaster intensity up to 25.5";
        public static string IntensityUnlockHandledByOther =
            "Handled by Natural Disasters Renewal. Enable only if you want Disaster + to control it.";

        public static string EarthquakeDamageOwner = "Earthquake damage is calculated by";
        public static string EarthquakeOwnerOther = "Natural Disasters Renewal";
        public static string EarthquakeOwnerSelf = "Disaster +";

        // ★★ **退役した 2 件。** ③のタイル（災害パネルの「火災旋風」ボタン）と、その
        //    ツールチップ「Place a stationary, burning vortex」の文言だった。
        //    火災旋風は**意図的に起こせるものではなく、大火事のときにだけ自然発生する**
        //    ことになり、手動発生の経路ごとタイルを撤去したので、どちらも表示されない。
        //
        //    **キーは消さない** —— Strings / Locales\en.txt / Locales\ja.txt のキー集合は
        //    一致させ続ける必要があり、既訳を捨てる理由も無い（LogChannelFireWhirl・
        //    *ResetButton と同じ扱い）。**別の意味で再利用してもいけない**
        //    （とくに Tooltip は「置ける」と言っている。置けなくなった今その文を
        //     別の場所で使い回すと、嘘の説明がそのまま生き返る）。
        public static string FireWhirlName = "Fire whirl";
        public static string FireWhirlTooltip = "Place a stationary, burning vortex";

        // ★ 1 文に縮めたが、**「バニラ由来の破壊はあちらの竜巻設定に従う」は
        //   落とせない** —— これは挙動の事実であって解説ではない。
        //   （落とした「延焼拡大は影響を受けません」は③のパネルと診断が名乗る。）
        public static string NdrDetected =
            "Natural Disasters Renewal detected: vanilla-side destruction follows its "
            + "tornado settings.";

        public static string FireWhirlNeedsDlc =
            "Fire whirls require the Natural Disasters DLC.";

        // --- 左上のショートカットと、その下のタブ帯（InfoHub）---
        //
        // ★ ボタンの見分けは**文字**で付ける。スプライト名はアトラスのデータで
        //   あってアセンブリからは読めないので、アイコン名を当てにいくと
        //   「見えないボタン」になり得る（InfoHub のクラス doc）。
        //   そのため InfoButtonLabel は**短い文字列でなければならない**
        //   （32 px 四方のボタンに収まること）。
        public static string InfoButtonLabel = "D+";
        public static string InfoButtonTooltip = "Disaster + information";
        public static string InfoDragTooltip = "Drag to move this window";
        public static string InfoTabDiagnostics = "Diagnostics";

        // --- 診断のタブ ---
        public static string DiagnosticsTitle = "Diagnostics";
        public static string DiagnosticsOverlayRow = "Overlay";
        public static string DiagnosticsHotkeyRow = "Hotkey";
        public static string DiagnosticsOn = "on";
        public static string DiagnosticsOff = "off";
        public static string DiagnosticsDumpButton = "Write a diagnostics dump";
        public static string DiagnosticsDumpHint =
            "The dump is written into the mod folder.";

        public static string GroupDebug = "Debug";
        public static string OverlayEnabled = "Enable diagnostic overlay";
        public static string OverlayHotkey = "Overlay hotkey (Ctrl + key writes a dump file)";
        public static string LogChannels = "Verbose log channels";
        public static string LogChannelGeneral = "General";

        // 現在 UI からは出していない。FireWhirl チャンネルを付けた Log.Diag 呼び出しが
        // 1 件も無く、チェックボックスが何も制御しないため（Mod.OnSettingsUI 参照）。
        // ②〜⑤がチャンネル付きログを出すときに復活させる。キーは 3 ファイル
        // （Strings / Locales\en.txt / Locales\ja.txt）で一致させ続ける必要があるので消さない。
        public static string LogChannelFireWhirl = "Fire whirl";

        public static string AssumptionsFailedTitle = "Some features are unavailable";
        public static string AssumptionsFailedHint =
            "Load a city once, then reopen this page to refresh.";

        public static string GroupForecast = "Weather forecast";
        public static string ForecastEnabled = "Enable the forecast panel";

        // ★ 現在 UI からは出していない 4 件（*ResetButton）。ボタンはバニラの災害
        //   パネルの中に置かれるようになり、位置はパネルの autolayout が決めるので
        //   （DisasterPanelBar）、「位置をリセット」は何も制御しない死んだボタンになる。
        //   **キーは消さない** —— Strings / Locales\en.txt / Locales\ja.txt の
        //   キー集合は一致させ続ける必要があり、既訳を捨てる理由も無い
        //   （LogChannelFireWhirl・EarthquakeBandWeak と同じ扱い）。
        public static string ForecastResetButton =
            "Reset the forecast button position (takes effect next time you load a city)";
        public static string ForecastTitle = "Weather forecast";
        public static string ForecastTemperature = "Temperature";
        public static string ForecastRain = "Rain";
        public static string ForecastCloud = "Cloud";
        // 全体レビュー指摘(I5): Fog は毎 tick 読んでスナップショットに載せながら
        // どこにも表示していなかった。読むなら出す、出さないなら読まない。
        // 表示する側を選んだ（雨・雲と全く同じ ForecastReading で、行を 1 つ足すだけ）。
        public static string ForecastFog = "Fog";
        public static string ForecastWind = "Wind";
        public static string ForecastLightning = "Lightning";
        public static string ForecastTornado = "Tornado";
        public static string ForecastShowOnMap = "Show on map";
        public static string ForecastAtCursor = "Hazard at cursor";
        public static string ForecastUnavailable = "Weather data unavailable";
        public static string ForecastNdrNote =
            "Natural Disasters Renewal governs disaster occurrence.";
        // レビュー指摘: 無印の確率%はラベル無しだと「降水確率」等と誤読される。
        // これは実際には設定された確率値であり、tick 毎の実際の発生判定は二乗と
        // 都市面積補正を経る（ForecastFeature/ForecastPanel のコメント参照）。
        public static string ForecastProbability = "Disaster probability (setting)";
        // レビュー指摘: ハザードビューが出ていないときに汎用の ForecastUnavailable を
        // 使うと「気象データが読めない」という誤った原因を暗示する。実際は気象データは
        // 生きていて、ハザードビューが出ていないだけ。かつワンクリックで直せる。
        public static string ForecastSwitchHazardView =
            "Switch a hazard view on to read a value here.";

        // 全体レビュー指摘（最重要）: バニラのハザードマップは静的なリスク面ではなく、
        // 「測位済みで進行中の嵐」の予測被害範囲である（WeatherSnapshot.
        // LocatedLightningStorms の doc に IL の根拠）。該当する嵐が 1 つも無いとき
        // グリッドは全セル 0 になり、以前はそれを「落雷: 0」と表示していた。
        // 数値としては本物だが、プレイヤーが読み取る意味（「ここは安全」）は嘘になる。
        // 0 のときは数値を出さず、空である理由と、どうすれば埋まるかを書く。
        public static string ForecastNoStormDetected =
            "No storm detected right now. This map shows where a detected storm will hit, "
            + "so it stays empty until a Weather Radar finds one.";

        // 全体レビュー指摘(I2): DLC が無いと雷雨・竜巻の prefab も気象レーダーも
        // 存在しないので、ハザードの 2 行は永久に空のビューと 0 を出し続ける。
        // FireWhirlNeedsDlc と同じ形で理由を書く（気象・傾向の行は DLC 不要なので残す）。
        public static string ForecastHazardNeedsDlc =
            "Lightning and tornado hazard maps require the Natural Disasters DLC.";

        // 全体レビュー指摘: ロード直後にポーズしていると最初のスナップショットが
        // まだ無い。以前はこれを「気象データを読み取れません」と表示しており、
        // 「まだ読んでいない」と「WeatherManager が居ない」が区別できなかった。
        public static string ForecastWaiting = "Waiting for the first simulation update.";

        public static string TrendRising = "up";
        public static string TrendFalling = "down";
        public static string TrendSteady = "steady";
        public static string LogChannelForecast = "Forecast";

        // --- ②地震（Task 3） ---
        // 既存の EarthquakeDamageOwner / EarthquakeOwnerOther / EarthquakeOwnerSelf は
        // ③の NDR 互換ドロップダウン用で別物。名前を衝突させないこと。
        public static string GroupEarthquake = "Earthquake";
        public static string EarthquakeEnabled = "Enable the earthquake panel";
        // ★ 現在 UI からは出していない（ForecastResetButton の doc）。
        public static string EarthquakeResetButton =
            "Reset the earthquake button position (takes effect next time you load a city)";
        public static string EarthquakeNeedsDlc =
            "Earthquakes require the Natural Disasters DLC.";
        public static string LogChannelEarthquake = "Earthquake";

        // --- ②地震（Task 4: パネル） ---

        public static string EarthquakeTitle = "Earthquake";

        // 第 1 層と第 2 層を見分けるための接頭辞。EarthquakePanel の
        // SetLayer1 / SetLayer2 以外からは絶対に参照しないこと（呼び出し側が
        // どちらを名乗るか選べる状態にすると、この分離は必ずいつか崩れる）。
        public static string SourceVanilla = "[measured]";
        public static string SourceModel = "[Disaster + model]";

        /// <summary>
        /// 翻訳文の中で「ここに実測値の印が入る」を表すトークン。
        /// 表示の直前に <see cref="SourceVanilla"/> へ差し替える
        /// （<c>TyphoonRows.SetModelNote</c> が唯一の差し替え箇所）。
        ///
        /// ★ <b><c>public static string</c> にしてはいけない。</b>
        ///   <c>LocaleLoader</c> は <c>GetFields(Public | Static)</c> の string を
        ///   全部「翻訳できるキー」として扱い、<c>en.txt</c> の生成にも同じ列挙を使う。
        ///   ここを public にすると、トークン自体が翻訳対象として出力され、
        ///   翻訳者が訳した瞬間に差し替えが効かなくなる（const なら
        ///   <c>SetValue</c> が例外になる、というもっと悪い壊れ方もする）。
        /// </summary>
        internal const string MeasuredToken = "{measured}";

        public static string EarthquakeLayer1Header = "What the game actually computes";
        public static string EarthquakeLayer2Header =
            "Added by Disaster + (not vanilla behaviour)";

        // タブの見出し（パネルの構造変更で追加）。行の中身は 1 つも変えていないので、
        // 増えた文言はこの 2 つだけである。**どちらのタブも第 1 層**で、
        // 第 2 層の節はタブの外・その下に常設されている（EarthquakePanelTabs の doc）。
        public static string EarthquakeTabQuake = "Quake, cursor and maps";
        public static string EarthquakeTabDamage = "Buildings and seismographs";

        public static string EarthquakeNoneActive = "No earthquake in progress.";
        public static string EarthquakeCount = "Earthquakes in progress";
        public static string EarthquakeIntensity = "Intensity";
        public static string EarthquakeRadius = "Affected radius";
        public static string EarthquakePhase = "Phase";
        public static string EarthquakePhaseEmerging = "before the main shock";
        public static string EarthquakePhaseActive = "shaking";
        public static string EarthquakePhaseClearing = "aftermath";

        // 「本震まで」は予測ではなく**予定表の読み上げ**である。m_activationFrame は
        // StartDisaster が m_startFrame + m_emergingDuration として書き込んだ確定値で
        // （§A-1）、①が禁じている「あと何時間で嵐が来る」（乱数由来の発生判定）とは
        // 根拠がまったく違う。設計書 §7-2 がこの区別を要求している。
        public static string EarthquakeTimeToShock = "Time to the main shock";
        // ただし m_activationFrame == 0 は「今」ではなく「予定が無い」。
        public static string EarthquakeTimeUnknown = "not scheduled";
        public static string EarthquakeMinutes = "min";

        // ★ この 2 つの文言は全体レビュー(C3)で直したもの。以前は
        //    "Shaking at cursor" / "outside the shaken area" だったが、
        //    s = 1 - d/R は**倒壊・出火のランプ**であって揺れではない。バニラの揺れは
        //    amp = 0.3/(1 + dist*0.001) で、**半径による打ち切りが一切無い**（§A-7）。
        //    つまり以前の文言は、同じフレームで CameraShakeBooster が揺れを足し続け
        //    SeismographRecorder が非ゼロの変位を書き続けている地点について
        //    「揺れていません」と書いていた。揺れは EarthquakeShakeAtCursor が別に出す。
        public static string EarthquakeAtCursor = "Destruction factor at cursor";
        public static string EarthquakeOutOfRange = "outside the destruction radius";

        // バニラ自身の揺れの振幅（§A-7 IL_0069 の amp、包絡線を掛ける前）。
        // 倒壊ランプとは別の量なので別の行にする。
        public static string EarthquakeShakeAtCursor = "Ground shaking at cursor";
        // 揺れの窓（Emerging|Active かつ e が m_activeDuration の内側）が開いていない。
        public static string EarthquakeNotShaking = "not shaking right now";

        // 収束中（Clearing）の地震では DestroyBuildings がそもそも呼ばれない
        // （§A-3: 呼び出しは Active 分岐にしか無い）。数値を出さずに理由を書く。
        public static string EarthquakeNoDamageInPhase =
            "this quake is past its shaking phase; the game runs no destruction for it any more";

        // カーソル位置には 2 つの別のモデルの値が並ぶ。違う数字が出るのが正常なので、
        // なぜ違うのかを画面で名乗る（全体レビュー M3）。

        public static string EarthquakeFaultBand = "Fault zone";
        public static string EarthquakeFaultInside = "inside";
        public static string EarthquakeFaultOutside = "outside";

        // ★ 「マップに表示」から改名した（震度分布オーバーレイの追加に伴う）。
        //    ボタンが 2 つ並ぶようになり、片方が「マップに表示」のままだと
        //    **どちらがどちらの絵を出すのかが名前から分からない**。
        //    こちらはバニラの情報ビュー（§A-6 のグリッド）、隣は本 MOD の
        //    震央からのランプで、塗る形も、地震計を要るか要らないかも違う。
        public static string EarthquakeShowOnMap = "Show the game's own hazard view";

        // ①の ForecastNoStormDetected と同じ構図・同じ文体。地震のハザードマップも
        // Located && (Emerging|Active) の 2 段ゲートを持ち（§A-6）、地震に Located を
        // 立てられるのは地震計だけ（§A-2 / §C-2）。したがって地震計が無ければ
        // このビューは恒久的に空で、それが正常。**空を「安全」と読ませない。**
        public static string EarthquakeNotLocated =
            "No earthquake is located right now. This map only shows a located, in-progress "
            + "quake, and an Earthquake Sensor is what locates one.";
        public static string EarthquakeSwitchHazardView =
            "Switch the earthquake hazard view on to read a value here.";

        // 計画の 29 件に対する 2 件の追加（意図的な逸脱）。
        // ①はこの 2 つの状況をどちらも ForecastUnavailable（「気象データを読み取れません」）で
        // まかなっていたが、②で同じことをすると「地震データが読めない」と「カーソルが
        // 地形の上に無い」が同じ文言になる。後者はパネルを読んでいる間ほぼ常に起きる
        // （マウスがパネルの上にある）ので、いちばん頻繁に目に入る行が誤った原因を
        // 名指しし続けることになる。原因ごとに分ける。
        public static string EarthquakeUnavailable = "Earthquake data cannot be read right now.";
        public static string EarthquakeCursorUnknown =
            "Move the cursor over the terrain to read a value here.";

        // 10 段階のバーに対して名前は 5 区分だけ（SeismicScale の doc）。
        // **実在の震度階級の名前は使わない**（設計書 §3.1、§7-4）。
        //
        // ★ 全体レビュー(C3)以降、この 4 件はどこからも表示していない。
        //    弱い/中程度/強い/非常に強い は**この MOD が付けた名前**であって、
        //    バニラは probability の係数を計算しているだけである。それを
        //    [measured] の接頭辞の下に出すと、ゲームが「非常に強い」と判断している
        //    という嘘になる。数値とバーだけを第 1 層で出し、区分名は第 2 層
        //    （Task 9 以降）が名乗るときまで表示しない。
        //    **キーは消さない** —— Strings / en.txt / ja.txt のキー集合は一致させ続ける
        //    必要があり、SeismicScale.BandOf も現役のまま（LogChannelFireWhirl と同じ扱い）。
        public static string EarthquakeBandWeak = "weak";
        public static string EarthquakeBandModerate = "moderate";
        public static string EarthquakeBandStrong = "strong";
        public static string EarthquakeBandSevere = "very strong";

        // --- ②地震（Task 5: 建物ごとの余裕度） ---
        //
        // 計画の 7 件に対する 3 件の追加（意図的な逸脱。Task 4 と同じ理由付けで、
        // 「文言が足りないので既存キーを流用する」を避けるための追加である）。
        // 計画 Step 7 は「AlreadyDown / Unknown の文言は既存キーを使い回す」と
        // 書いているが、既存キーにその意味を持つものが実際には無かった:
        //   - AlreadyDown  … 「圏外」でも「断層帯の内側」でもない。流用すると
        //                     瓦礫の上で誤った理由を名乗ることになる。
        //   - Unknown      … 「プレハブ 4 値が読めていないので判定を出せない」は
        //                     この機能でいちばん出してはいけない嘘（＝断定）を
        //                     避けるための文言そのものなので、代用が効かない。
        //   - SurviveAnyDistance … 計画 5.2 の表が「X ≦ 0 なら『どれだけ近くても
        //                     倒壊しません』」と明示的に別の文言を要求している。
        // InsideFaultZone は計画どおり EarthquakeFaultBand / EarthquakeFaultInside と
        // 常設の EarthquakeFaultBandNote を流用する（新しいキーを増やさない）。

        public static string EarthquakeBuildingUnderCursor = "Building under the cursor";
        public static string EarthquakeNoBuilding = "no building under the cursor";
        // 「調べたが無かった」と「調べられなかった」を同じ文言にしない
        // （BuildingProbeOutcome の doc）。前者は実測値、後者は読み取り失敗である。
        public static string EarthquakeProbeFailed =
            "the buildings under the cursor could not be read right now";
        public static string EarthquakeCollapseWithin = "Collapses within";
        public static string EarthquakeCurrentDistance = "current distance";
        public static string EarthquakeVerdictCollapse = "will collapse";
        public static string EarthquakeVerdictSurvive = "will not collapse";
        public static string EarthquakeVerdictSurviveAnyDistance =
            "will not collapse at any distance at this intensity";
        public static string EarthquakeAlreadyDown = "already collapsed or burning";

        // --- 出火（全体レビュー M1）---
        //
        // 依頼文が名指ししていた「揺れによる火災」の答え。材料（2 回目の引き）は
        // 最初から BuildingMargin.BurnThresholdValue にあり、スナップショットにも
        // 載っていて、ユニットテストまであったのに、**どこにも表示していなかった**。
        public static string EarthquakeBurnLabel = "Catches fire";
        public static string EarthquakeBurnWithin = "Catches fire within";
        public static string EarthquakeVerdictBurn = "will catch fire";
        public static string EarthquakeVerdictNoBurn = "will not catch fire";
        public static string EarthquakeVerdictNoBurnAnyDistance =
            "will not catch fire at any distance at this intensity";
        // IL は else if (hitB && ...) なので、倒壊が当たっていれば出火の分岐へは来ない。
        public static string EarthquakeBurnAfterCollapse =
            "(the collapse happens first; the same draw becomes the burn damage of the rubble)";

        // --- 破壊コードが他 MOD に置き換えられている場合（全体レビュー C2）---
        //
        // NDR は DisasterHelpers.DestroyBuildings を Prefix が false を返す形で完全置換し、
        // probability == 0.02f をバニラ地震の目印にして 0.04 を使う（§E-2）。
        // 「DisasterHelpers を経由しない」という②の方針は**被害を書く側**の話で、
        // **読む側にはまったく効かない**。強度 55 / しきい値 300 の建物について、
        // この MOD は「どの距離でも倒壊しません」、NDR は「775 m まで倒れる」と言う。
        public static string EarthquakeVerdictNdr =
            "no verdict (another mod replaces the game's destruction code)";
        public static string EarthquakeNdrNote =
            "Natural Disasters Renewal replaces the routine that destroys and ignites buildings and "
            + "doubles the city-wide probability, so the collapse and fire verdicts are withheld. "
            + "Every other row here is read straight from the game and is unaffected.";
        // 「分からない」を「外側」と言い換えないための行。判定を出さない理由を書く。
        public static string EarthquakeVerdictUnknown =
            "no verdict (the fault geometry could not be read from the EarthquakeAI prefab)";

        // この機能でいちばん重要な 1 文。倒壊は乱数ではなく、地震が始まった瞬間に
        // 既に決まっている（§A-3: 種は (建物, 災害) の組に対して定数）。ただし
        // それが言えるのは全体円盤についてだけである。
        public static string EarthquakeGlobalDiscOnly =
            "This is decided for the city-wide disc, and it was already decided the moment the "
            + "quake started. Inside the fault zone the four rupture patches judge separately.";

        // --- ②地震（Task 6: カメラの揺れ） ---
        //
        // 注記の最後の 1 文が本質。「既定の強度では何も変わらない」は言い訳ではなく、
        // この設定を既定 ON にしてよい根拠そのものである（強度 55 で追加分が厳密に 0）。
        public static string EarthquakeShakeBoost =
            "Scale camera shake with intensity and distance";
        // ★ **設定画面からは降ろした。** 何をする設定かは EarthquakeShakeBoost の
        //   ラベルが名乗っており、「バニラはこうしている」の解説は診断ダンプにある
        //   （Mod.OnSettingsUI の doc の表）。**キーは消さない。再利用もしない。**
        public static string EarthquakeShakeBoostNote =
            "Vanilla ignores intensity here, so a 25.5 quake shakes exactly as much as a 5.5 one. "
            + "At the vanilla default intensity (5.5) this option changes nothing.";

        // --- ②地震（Task 7: 地震計の既存効果） ---
        //
        // 地震計を建てると何が変わるかは、ゲーム内のどこにも書かれていない。
        // 変わるのは 2 つだけで、どちらもバニラの実測（第 1 層）である（§A-2 / §C-2）:
        //   1. 警報リードタイムが 1755 → 最大 8192 フレーム（38.6 分 → ちょうど 3.0 時間）
        //   2. located が立ち、**そもそも地震がハザードマップに描かれるようになる**
        //
        // 計画 Step 5 の表は 5 件だが、Step 4 の本文が参照している EarthquakeNoSensor が
        // その表から漏れている。カバレッジ 0（＝本機能の看板の説明そのもの）を裸の「0」
        // だけで済ませないために、計画本文のほうに従って 6 件目として足す。
        public static string EarthquakeSensorSection = "Earthquake sensors";
        // 上限 100 はキーの側に入れる。値の隣に "max" と英語を直書きすると、
        // 日本語表示のときにそこだけ翻訳から外れる。
        public static string EarthquakeCoverageAtEpicentre =
            "Sensor coverage at the epicentre (capped at 100)";
        // 震央の行と同じ量なのに片方だけ「(上限 100)」と書いてあると、こちらが
        // パーセントに見える。実体は免疫的リソースの生のセル値（ushort）で、
        // 100 を超えることも普通にある（全体レビュー M9）。
        public static string EarthquakeCoverageAtCursor =
            "Sensor coverage at cursor (raw cell value, not a percentage)";
        public static string EarthquakeWarningLead = "Warning lead time";
        public static string EarthquakeNoSensor = "no Earthquake Sensor reaches the epicentre";
        // 地震が起きているかどうかに関わらず**常に**出す。これは地震計という建物の
        // 性質の説明であって、今この瞬間の観測値ではない。
        public static string EarthquakeSensorEffect =
            "An Earthquake Sensor extends the warning from 38.6 minutes to up to 3 hours, and "
            + "makes the quake appear on the hazard map at all. Only sensors whose range covers "
            + "the epicentre count.";

        // --- ②地震（Task 8: 波形グラフ） ---
        //
        // **この 4 件はどれも「誰が測ったのか」を名乗るためにある。**
        // EarthquakeSensorAI は時系列データを一切持たない（§C-1、ABSENT）ので、
        // ここに出る線はゲーム内のセンサーが計測した値では**ない**。バニラ自身の
        // 揺れの式（§A-7、カメラを動かしているのと同じ式）を、カメラの代わりに
        // 地震計の位置で評価したものである。その 1 点を隠すと、この機能は
        // 「もっともらしいが出所の分からないグラフ」に落ちる。
        public static string EarthquakeWaveform = "Ground motion at the sensor";
        public static string EarthquakeWaveformNeedsSensor =
            "Build an Earthquake Sensor to record ground motion. The game itself keeps no "
            + "ground-motion history at all, so Disaster + samples it at the sensor.";
        public static string EarthquakeWaveformUnavailable =
            "Waveform drawing is unavailable on this build; showing the peak amplitude instead.";
        // 構築には成功したが、描画中に落ちて以後描かなくなった状態。以前はこの行が
        // パネル構築時にしか作られず、実行時に落ちると**黙って空欄**になっていた
        // （WaveformView のクラス doc が禁じている壊れ方そのもの、全体レビュー I6）。
        public static string EarthquakeWaveformDrawFailed =
            "Waveform drawing stopped after an error; showing the peak amplitude instead.";

        // --- ②地震（第 2 層: 合成記象 P/S/コーダ） ---
        //
        // ★★ **ここの 5 件はどれも第 2 層である。[measured] を付けてはいけない。**
        //    プレイヤーの指摘（「同じ波形が連続している」）は正しく、バニラの揺れは
        //    固定の 2 本の正弦波で（§A-7）、到達も立ち上がりも減衰も持たない。
        //    それを直すには**この MOD が波形を発明するしかない**——つまり
        //    EarthquakeWaveformNote が名乗っている「ゲーム自身の式」ではなくなる。
        //    そこで、直すかわりに**バニラの線を消さずに並べて描き**、
        //    どちらがどちらかを凡例と接頭辞と色の 3 つで名乗る。
        //
        //    EarthquakeWaveformModelNote が色を名指ししているのは、グラフには
        //    接頭辞を書ける場所が無いからである（行の接頭辞は EarthquakeRows が
        //    付けるが、テクスチャの中には文字を置かない）。
        public static string EarthquakeSeismogramEnabled =
            "Synthesize a realistic seismogram (also changes how the camera shakes)";
        // ★ **設定画面からは降ろした**（EarthquakeShakeBoostNote と同じ扱い）。
        public static string EarthquakeSeismogramNote =
            "Vanilla shakes the camera with two fixed sine waves that never arrive, never build "
            + "and never decay. This option replaces that pattern with a synthesized record and "
            + "adds a second line to the waveform graph. It is Disaster +'s own model, not "
            + "anything the game computes, so it is off by default.";
        public static string EarthquakeWaveformModel =
            "Synthesized seismogram (orange line)";
        // ── 第 3 層＝火山性微動（2026-08-22、所有者の依頼）─────────────────
        //    第 2 層とまったく同じ扱いにする。これも**この MOD のモデル**であって、
        //    ゲームが計算している値でも、カメラが実際に足した変位でもない
        //    （Game/Volcano/VolcanoTremorTrace のクラス doc）。
        public static string EarthquakeWaveformTremor =
            "Volcanic tremor (teal line)";
        // 火山だけが揺れているときの見出し。**震央からの距離ではない**ので、
        // そう名乗らずに出すと「起きていない地震の記録」の顔になる。
        public static string EarthquakeWaveformTremorSource = "volcano";

        // 初期微動継続時間。**震源距離とともに開く**のがこのモデルの看板であり、
        // 数値で出しておかないと「絵がそれらしい」だけになる。
        public static string EarthquakeWaveformSMinusP = "P-S interval";
        public static string EarthquakeFrames = "frames";

        // --- ②地震（震度分布の地図オーバーレイ） ---
        //
        // 依頼文の「都市内での震源からの距離に応じた震度の分布の概念もありません」に
        // **地図として**答える部分。全体レビューの判定は「カーソル 1 点の数値と
        // 10 文字のバーでは分布ではない」であり、その通りである。
        //
        // ここの文言でいちばん重要なのは EarthquakeOverlayLegend の後半 ——
        // このオーバーレイと、すぐ隣のボタンが出すバニラのハザードビューは
        // **別の量**である（§A-6: 亀裂線分までの距離・2 次減衰・Rmax = R+400・
        // 地震計が要る／こちらは震央からの線形ランプ・地震計不要）。
        // 2 つを同じものだと読ませないことが、この機能の誠実さの担保になる。

        public static string EarthquakeOverlayShow = "Show the intensity distribution on the map";
        public static string EarthquakeOverlayHide = "Hide the intensity distribution";
        public static string EarthquakeOverlayRow = "Intensity distribution overlay";
        public static string EarthquakeOverlayOff = "off";
        public static string EarthquakeOverlayOn = "on";
        public static string EarthquakeOverlayQuakes = "earthquake(s) drawn";
        // 描くべき地震が 1 つも無い。**「安全」ではない**ので理由を書く。
        public static string EarthquakeOverlayNothingToDraw =
            "on, but nothing to draw: no earthquake is in its pre-shock or shaking phase. "
            + "The game only runs its destruction pass while a quake is shaking.";
        // 断層帯だけが出ない理由。推測した大きさで描かないことの説明でもある。
        public static string EarthquakeOverlayFaultUnknown =
            "The fault zone is not drawn: the four EarthquakeAI prefab values could not be read, "
            + "so its size is unknown. Drawing a guessed size on the map would be indistinguishable "
            + "from a measured one.";
        // 予算切れ。地震は同時に 256 個まで存在しうる（§E-1）。
        public static string EarthquakeOverlayCapped =
            "More earthquakes are in progress than the overlay draws at once; the rest are omitted "
            + "rather than drawn partially.";
        public static string EarthquakeOverlayUnavailable =
            "The map overlay could not be registered with the game's renderer on this build.";
        // 両方出ているときの注意。いちばん誤解が起きる状態なので名指しする。
        public static string EarthquakeOverlayBothOn =
            "The game's hazard info view is on at the same time. The two pictures are different "
            + "quantities - see the legend below.";

        // 凡例は**色の読み方だけ**にしてある。「バニラのハザードビューとは別の量だ」は
        // すぐ上の EarthquakeCursorModelsNote が既に言っており（そちらは 2 つの量の
        // 違いそのものを説明する行）、同じ内容を 2 箇所に書くと縦が足りなくなる。
        // 代わりにあちらの末尾に「下のオーバーレイが描いているのは前者だ」を足した。
        public static string EarthquakeOverlayLegend =
            "Legend - blue-green: the destruction factor, densest at the epicentre. Magenta: the fault zone. White: the epicentre and the fault strike";

        // --- ②地震（Task 9: 海中震源からの津波連鎖 ＝ 第 2 層の 1 つ目） ---
        //
        // **ここから先はバニラに存在しない挙動である。** 全て既定 OFF で、
        // パネルでは EarthquakeLayer2Header の節の下に [Disaster + model] 付きで出る。
        //
        // EarthquakeTsunamiFromShore がこの機能でいちばん重要な 1 文。依頼は
        // 「海中で地震を起こしてもプレート境界型の津波が来ない」だったが、
        // **「震源から波が広がる」は TsunamiAI では literally 不可能**である
        // （FindSea はマップ外周セルしか候補にせず、m_targetPosition も m_angle も
        //  開始時に上書きされる。§B-3）。実現しているのは「震源に最も近い海側の
        // 外周から津波が来る」であり、**できていないことをできているように書かない**。
        //
        // EarthquakeTsunamiNoSea は**失敗の文言ではない**。内陸マップでは
        // 海側外周区間が 10 セルに満たず、何も起きないのが正常な結果である。
        // ①の ForecastNoStormDetected と同じ扱いで、「0 を安全と読ませない」の裏返し
        // ——「何も起きなかった」を「壊れた」と読ませない。

        public static string EarthquakeTsunamiChain =
            "Raise a tsunami after an undersea earthquake";
        public static string EarthquakeTsunamiDelay = "Tsunami delay (in-game minutes)";
        public static string EarthquakeTsunamiPending = "Tsunami expected in";
        public static string EarthquakeTsunamiRaised = "Tsunami raised";
        public static string EarthquakeTsunamiFromShore =
            "The wave arrives from the sea nearest the epicentre, not from the epicentre itself. "
            + "The game can only start a tsunami at the map edge.";
        public static string EarthquakeTsunamiNoSea =
            "No sea close enough to this map edge, so no tsunami was raised. This is normal on "
            + "an inland map.";

        // --- ②地震（Task 10: 長周期地震動 ＝ 第 2 層の 2 つ目） ---
        //
        // **津波より踏み込んでいる。** 津波はバニラの災害を 1 個起こすだけだったが、
        // こちらは**バニラなら倒れなかった建物を倒す**。だから:
        //
        //   - 設定のラベル（EarthquakeLongPeriodEnabled）自体に
        //     「バニラには無い被害を足します」と書く。チェックを入れる前に読める場所は
        //     ここしかない
        //   - EarthquakeLongPeriodNote は、バニラが建物の高さを揺れにも被害にも
        //     一切使っていないこと（§A-7 / §A-3）を名乗る。この 1 文が無いと、
        //     プレイヤーは「高層ほど揺れる」をゲームの仕様だと思う
        //   - EarthquakeLongPeriodNoHeight は**計画の 6 キー表に無い 7 つ目**である。
        //     計画 Step 6 は「高さが読めない環境では理由の 1 行だけを出す」と要求して
        //     いるが、その文言を持つキーが表から漏れていた。既存キーの流用では
        //     「読めなかった」と「低いので対象外」が同じ文になってしまうので新設した

        public static string EarthquakeLongPeriod = "Long-period ground motion";
        public static string EarthquakeLongPeriodEnabled =
            "Enable long-period ground motion (adds damage vanilla never does)";
        public static string EarthquakeLongPeriodStrength = "Long-period strength (0 = off)";
        public static string EarthquakeLongPeriodNoHeight =
            "This building's height could not be read, so no long-period damage is applied to it. "
            + "The mod never guesses a height.";
        public static string EarthquakeBuildingHeight = "Building height";
        public static string EarthquakeResonance = "Resonance";
        // 計画の 6 キー表に無い 8 つ目。計画自身が示している行の見本
        // （「追加倒壊リスク 6.4%」）に必要な語で、表から漏れていた。
        // 裸の「+3.1%」だけを出すと、何の確率なのかがどの言語でも読めない。
        public static string EarthquakeLongPeriodRisk = "extra collapse risk";

        // 第 2 層レビュー I1 / M3 で足した 2 キー。どちらも「確信を持って誤った数値」を
        // 出さないためだけに在る。
        //
        //   - EarthquakeLongPeriodBeforeShock … 追加被害が走るのは Active だけだが
        //     （LongPeriodDamage.Step）、カーソル行は Emerging も対象にする
        //     QuakeSelection.SelectDamaging の選定を使う。本震前に「追加倒壊リスク
        //     6.4%」とだけ出すと、まだ何にも適用されていない確率が確定値の顔で出る
        //   - EarthquakeLongPeriodCapped … 走査は震央から外へ向かうので打ち切られても
        //     震央の周りは評価済みだが、外側はまだである。「もう抽選が済んだ」と
        //     「これからである」を黙って混ぜない
        //
        // **短く保つこと。** どちらもカーソル行の末尾に足されるので、
        // 長いと行が折り返しの高さを超えて途中で切れる（EarthquakeLayer2Rows が
        // 津波の行で 1 度踏んだ形）。行の高さ 72f との釣り合いで決めてある。
        public static string EarthquakeLongPeriodBeforeShock =
            "not applied until the main shock";
        public static string EarthquakeLongPeriodCapped =
            "sweep truncated this pass; not rolled yet";

        // --- ②地震（Task 11: 時間帯係数 ＝ 第 2 層の 3 つ目） ---
        //
        // **単独の設定は作っていない。** 掛かる先は長周期地震動の追加被害だけなので、
        // 独立したスイッチにすると、長周期が OFF のときに何も制御しない
        // 死んだスイッチになる。したがって行も長周期と一緒に出入りする。
        //
        // EarthquakeNoDayNight がこの機能でいちばん重要な 1 文。日夜サイクルを
        // 切っているとゲーム内時刻は**永久に 12.0 に固定される**（§F-1。
        // m_dayTimeOffsetFrames が毎 sim フレーム再設定される）ので、係数は黙って
        // 定数 1.00 になる。**無効化を隠さない**ためにこれを併記する。
        // 「昼」「夜」の語は用意しない —— Of() は境界を 1 時間かけて渡すので、
        // ゲーム自身の硬い判定（hour < 5 || hour > 20）と一致しない時間帯がある。

        public static string EarthquakeTimeOfDay = "Time of day";
        public static string EarthquakeTimeFactor = "factor";
        public static string EarthquakeNoDayNight =
            "The day/night cycle is off, so the in-game hour is pinned at 12:00 and the "
            + "time-of-day factor never changes.";

        // --- ④台風（Task 2: 骨格・プレハブ実測・前提検証） ---
        //
        // このタスクの時点では**パネルもボタンも無い**（T5 で入る）。ここで足すのは
        // 設定画面の 4 つと、ログチャンネルの名前だけである。
        //
        // ④の表示規約: **④が出す数値は原則すべて本 MOD のもの**なので、行ごとの
        // 出所の印は付けない（設計書 §1.2 / §7）。例外は WeatherManager から読んだ
        // 雨量・雲量だけで、そこにだけ SourceVanilla が付く。したがって
        // **Strings.SourceModel を④の表示コードから参照してはいけない。**
        public static string GroupTyphoon = "Typhoon";
        public static string TyphoonEnabled = "Enable the typhoon panel";
        // ★ 現在 UI からは出していない（ForecastResetButton の doc）。
        public static string TyphoonResetButton =
            "Reset the typhoon button position (takes effect next time you load a city)";
        public static string TyphoonNeedsDlc =
            "Typhoons require the Natural Disasters DLC.";
        public static string LogChannelTyphoon = "Typhoon";

        // --- ④台風（Task 3: 論理オブジェクトと経路追従） ---
        // ★ **これは「既定値」である。** 実際に使う強度は災害パネルの④タイルを
        //   押したときに出るスライダーで選ぶ（そのスライダーの初期値がこの値）。
        //   ラベルが「既定」と名乗らないと、スライダーで変えた強度が
        //   ここに従っていないように見える。
        public static string TyphoonIntensity =
            "Default typhoon intensity (10-255; vanilla storms use 55)";
        // ★ **設定画面からは降ろした。** 「バニラの嵐は 55」はスライダーの
        //   ラベル（TyphoonIntensity）に畳んである。
        public static string TyphoonIntensityNote =
            "The game's own storms use 55. Above 100 is beyond anything vanilla generates.";

        // --- ④台風（Task 5: パネル・ボタン・表示規約） ---
        //
        // ★ ここから下の行ラベルは全て**印の付かない行**に入る。出所は
        //   TyphoonModelHeader / TyphoonModelNote が見出しで一度だけ名乗る
        //   （設計書 §7-1）。唯一 [measured] が付くのは TyphoonRainRow /
        //   TyphoonCloudRow の 2 行で、接頭辞は TyphoonRows.SetMeasured が付ける。
        //
        // **m/s を出す文字列を足さないこと**（設計書 §7-3）。④の「風速相当」は
        // ゲームの倒壊確率に掛ける係数であって実在の風速ではない。②が気象庁震度階級を
        // 名乗らなかったのと同じ理由で、単位を名乗ると実在の意味があると誤解させる。
        public static string TyphoonTitle = "Typhoon";

        // ★★ **この文に印の文字列そのものを書かないこと**（全体レビュー I5）。
        //    以前 TyphoonModelNote は英語の "[measured]" を本文に埋め込んでおり、
        //    ja.txt はその日本語訳でも "[measured]" のままだった。ところが
        //    ja.txt の SourceVanilla は「[実測]」なので、**日本語のプレイヤーは
        //    画面に一度も出ない文字列を探すことになっていた**（英語では偶然一致
        //    していたので誰も気付かない形の壊れ方である）。
        //    設計書 §7.1 は「行ごとの印を付けない」代わりに「見出しが意味を担う」と
        //    決めているので、この 1 文が壊れると④の表示規約そのものが伝わらない。
        //
        //    印は 1 箇所（TyphoonRows.SetMeasured / SetModelNote）でしか作らない。
        //    翻訳文には**印そのものではなく <see cref="MeasuredToken"/> を置き**、
        //    表示の直前に Strings.SourceVanilla へ差し替える ——
        //    **構造として 2 度とずれない。**
        //
        //    string.Format を使わないのは本 MOD の規律だが、それは位置指定
        //    （{0} の数がずれると実行時に落ちる）を避けるためである。名前付きの
        //    トークンを String.Replace で差し替えるのは落ちない ——
        //    翻訳がトークンを落としても、その文だけが印に触れなくなるだけで、
        //    それは build.ps1 の locale 検査が捕まえる。
        // ★ ④のタイルもこのボタンも、押すと**配置カーソルが構わる**（バニラの災害
        //   ボタンと同じ約束）。文言を「起こす」から「地点を指す」へ改めてあるのは、
        //   押した瞬間には何も起きないからである —— 起きると書いてあるのに起きないと、
        //   プレイヤーは壊れたと判断してもう一度押す。
        // ★ **退役した 2 件。** 台風を起こすのは災害パネルの④タイルだけになり
        //   （バニラの災害ボタンと同じ 3 手）、パネルの中の「発生」ボタンは撤去した。
        //   **キーは消さない**（LogChannelFireWhirl・*ResetButton と同じ扱い）。
        //   **別の意味で再利用してもいけない。**
        public static string TyphoonStart = "Choose where the typhoon forms";
        public static string TyphoonPlaceHint =
            "Click the map where the typhoon should form. Right-click to cancel.";
        public static string TyphoonButtonTooltip =
            "Typhoon: pick a strength on the slider, then click the map";
        public static string TyphoonStop = "Stop the typhoon";
        public static string TyphoonInactive = "No typhoon right now.";
        public static string TyphoonWaiting = "Waiting for the first simulation update.";
        public static string TyphoonUnavailable = "Typhoon data unavailable";
        public static string TyphoonPrefabUnreadable =
            "The game's thunderstorm prefab could not be read, so no typhoon can be started. "
            + "Disaster + will not guess its radius or its lifetime.";
        public static string TyphoonCentre = "Centre";
        public static string TyphoonHeading = "Heading";
        public static string TyphoonCoreStrength = "Core strength";
        public static string TyphoonStormRadius = "Storm radius";
        public static string TyphoonGaleRadius = "Gale radius";
        public static string TyphoonPhaseLabel = "Phase";
        public static string TyphoonPhaseApproaching = "approaching";
        public static string TyphoonPhasePeak = "at its peak";
        public static string TyphoonPhasePassing = "passing";
        public static string TyphoonPhaseGone = "gone";
        public static string TyphoonLandfall = "Landfall in";

        // ゲーム内分の単位。②の EarthquakeMinutes と同じ語だが、④のパネルから
        // ②のキーを引くと、片方の翻訳を直したときにもう片方が黙って変わる。
        public static string TyphoonMinutes = "min";

        // **「上陸まで 0 分」と書かないための語。** 中心が既に陸の上にあるとき、
        // 残り時間 0 は「もう起きた」であって「これから起きる」ではない。
        // ①②が繰り返し確立した「0 と、0 ではない状態を混ぜない」の④版。
        public static string TyphoonLandfallNow = "already over land";

        // ★ 設計書 §7-2。「あと何分で上陸」を出してよいのは、④が経路を決定論的に
        //   持っているからである。①の天気予報パネルは乱数で発生を判定しているので
        //   同じことを出せない。**その違いをパネルに書く**のがこの 1 行の役目で、
        //   これが無いと「ゲームが予測している」と読まれる。

        // 「0 分」と混ぜないための文言。海上を通り抜ける経路では、上陸しないのが正常。
        public static string TyphoonNoLandfall = "stays over water on its current track";
        public static string TyphoonRainRow = "Rain";
        public static string TyphoonCloudRow = "Cloud";

        // --- ④台風（Task 6: 落雷） ---
        //
        // ★ 落雷の行も**印を付けない**（④の表示規約）。数字はどれもゲームが計算した
        //   ものではなく、④が自分で数えている台帳と、バニラの式から見積もった上限である。
        //
        // TyphoonLightningRow は**4 つの数を並べる順序をラベルで名乗る**形にしてある。
        // この MOD は書式文字列（string.Format）を 1 箇所も使っていない ——
        // 翻訳の {0} がずれると実行時に落ちるので、位置は語で説明する。
        public static string TyphoonEffectsHeader = "What the typhoon brings";
        public static string TyphoonLightningRow =
            "Lightning (in flight / total / left to the host storm / dropped)";

        // ★ 全体レビュー I4。強度 170 以上では宿主の嵐の取り分だけで 20 発の枠を
        //   使い切るため、④は落雷を 1 発も積まなくなる（LightningBudget の
        //   IntensityWithNoShareAtPeak に導出がある）。**その強度はスライダーの
        //   範囲の中にある**ので、プレイヤーは T6 の目的そのもの（壁雲への偏り）を
        //   黙って失いうる。診断ダンプだけでなく**パネルにも**出す。
        public static string TyphoonLightningYielded =
            "At this intensity the host thunderstorm is expected to use the whole 20-strike "
            + "queue, so Disaster + adds none of its own. The lightning you see is the host "
            + "storm's, spread evenly over its disc instead of around the eye wall. Lower the "
            + "typhoon intensity to get the eye-wall placement back.";

        // --- ④台風（Task 7: 風害） ---
        //
        // ★ ここも印を付けない（④の表示規約）。**特に「風速」を名乗らないこと** ——
        //   ④の風速相当は倒壊確率に掛ける係数であって m/s ではない（設計書 §7.3）。
        //   TyphoonWindNote がその事実を一度だけ名乗る。
        // TyphoonLightningRow と同じ形で、**並べる順序をラベルが語で名乗る**
        // （書式文字列を使わない。翻訳の {0} がずれると実行時に落ちる）。
        public static string TyphoonWindRow =
            "Wind damage (collapsed this pass / total / examined / refused by the game)";
        public static string TyphoonWindEnabled =
            "Wind damage (buildings vanilla would never collapse)";
        public static string TyphoonWindStrength = "Wind damage strength (0 = off)";
        public static string TyphoonWindCapped =
            "sweep truncated this pass; the outer edge has not been rolled yet";

        // ★ 危険半円。**左右が逆でもプレイヤーには気付けない**ので、どちら側が
        //   強いかを設定画面とパネルの両方で名乗る。
        public static string TyphoonSouthernHemisphere =
            "Southern hemisphere (the dangerous side is the LEFT of the track)";
        // ★ **設定画面からは降ろした。** どちら側が危険半円かは
        //   TyphoonSouthernHemisphere のラベルが名乗っており、理屈は診断ダンプにある。
        public static string TyphoonDangerousSideNote =
            "A real typhoon is not symmetric: on one side the spin and the storm's own "
            + "travel add up. That side gets a slightly wider and slightly more likely "
            + "damage footprint - the right of the track in the northern hemisphere, the "
            + "left in the southern one.";
        public static string TyphoonDangerousSideRight = "stronger on the right of the track";
        public static string TyphoonDangerousSideLeft = "stronger on the left of the track";

        // --- ④台風（Task 8: 河川氾濫） ---
        //
        // ★ TyphoonFloodNoSources は「なぜ何も起きないか」を出す行である
        //   （設計書 §7.4。①の「なぜハザードマップが空か」と同じ扱い）。
        //   **不具合ではないと明示する。**
        public static string TyphoonFloodRow = "River flooding";
        public static string TyphoonFloodEnabled =
            "River flooding (raises the map's own water sources)";
        public static string TyphoonFloodStrength = "River flooding strength (0 = off)";
        public static string TyphoonFloodNoSources =
            "This map has no natural water sources near the storm, so no river can rise. "
            + "Nothing is wrong - the game has no flood disaster of its own, and Disaster + "
            + "only raises water sources the map already has.";
        public static string TyphoonFloodRaised = "raised";

        // --- ④台風（竜巻並みの局所被害。随伴竜巻の後継） ---
        //
        // ★★ TyphoonGustNote は**この機能の説明そのもの**である。
        //   「竜巻の姿はどこにも出ないのに、狭い範囲だけが竜巻並みに壊れる」は
        //   説明が無ければ不具合にしか見えない。**短くしないこと。**
        //
        // ★★ TyphoonTornadoRetiredNote は**退役の告知**である。随伴竜巻を ON に
        //   していたプレイヤーには、チェックボックスが消えた理由と、.cgs に残った
        //   値がもう読まれないことを 1 度は見せる。設定は公開契約なので、
        //   黙って消したり別の意味で再利用したりしない
        //   （ModSettings.TyphoonTornadoes の doc）。
        //
        // TyphoonGustRow は他の行と同じく**並べる順序をラベルが語で名乗る**
        // （書式文字列を使わない。翻訳の {0} がずれると実行時に落ちる）。
        public static string TyphoonGustRow =
            "Tornado-strength damage (patches now / collapsed this pass / total / refused "
            + "by the game)";
        public static string TyphoonGustEnabled =
            "Tornado-strength damage patches (no tornado is spawned)";
        public static string TyphoonGustStrength =
            "Tornado-strength damage (0 = off)";
        public static string TyphoonTornadoRetiredNote =
            "The old \"accompanying tornadoes\" option was removed; its saved value is "
            + "never read again.";

        // --- ④台風（Task 9: 巨大な回転雲） ---
        //
        // ★ TyphoonCloudUnavailable は「なぜ空全体が変わらないか」を出す行である。
        //   DayNightDynamicCloudsProperties は DLC・グラフィック設定によっては
        //   存在しない（IL 事実文書 §C-2、PARTIAL）。**不具合ではない**ので、
        //   ④自身の雲は変わらず描かれることまで書く。
        public static string TyphoonCloudEnabled = "Draw the typhoon's cloud spiral";
        public static string TyphoonVanillaCloudBoost =
            "Also thicken and speed up the game's own sky clouds";
        public static string TyphoonCloudUnavailable =
            "The game's sky cloud settings are not present in this environment, so only "
            + "Disaster +'s own cloud is drawn.";

        // --- ④台風（暴風雨の演出） ---
        //
        // ★ 「被害」ではないことを名乗る。飛沫は描画だけ、吹き飛ばしは市民と車だけで、
        //   建物・道路・樹木には一切触れない（風害のつまみとは別である）。
        public static string TyphoonStormFx = "Show the storm at ground level";
        public static string TyphoonStormSound = "Play the storm's wind sound";

        // --- ⑤火山（Task 2: 骨格・地形 API の解決・前提検証） ---
        //
        // このタスクの時点では**パネルもボタンも火山も無い**（T3 以降で入る）。
        // ここで足すのは設定画面の 3 つと、ログチャンネルの名前、そして
        // 「読み取れません」の 1 行だけである。
        //
        // ⑤の表示規約: **⑤が出す数値は原則すべて本 MOD のもの**なので、行ごとの
        // 出所の印は付けない（設計書 §7.4）。例外は設置地点の地形高さと、
        // 影響範囲の建物数・道路セグメント数の 3 行だけで、そこにだけ
        // SourceVanilla が付く。したがって
        // **Strings.SourceModel を⑤の表示コードから参照してはいけない。**
        //
        // ★ ⑤には TyphoonNeedsDlc に相当するキーが無い。**⑤は ND DLC を要らない**
        //   （設計書 §1.4）。DLC が要るのは樹木の着火だけで、それは T8 が
        //   FeatureHost.NoteDegraded で名乗る。ここに「DLC が必要です」を
        //   置くと嘘になる。
        public static string GroupVolcano = "Volcano";
        public static string VolcanoEnabled = "Enable volcanoes";
        // ★ 現在 UI からは出していない（ForecastResetButton の doc）。
        public static string VolcanoResetButton =
            "Reset the volcano button position (takes effect next time you load a city)";
        public static string VolcanoUnavailable = "Volcano data unavailable";
        public static string LogChannelVolcano = "Volcano";

        // --- ⑤火山（Task 3: パネル・ボタン・表示規約） ---
        //
        // ★ ここから下の行ラベルは全て**印の付かない行**に入る。出所は
        //   VolcanoModelHeader / VolcanoModelNote が見出しで一度だけ名乗る
        //   （設計書 §7.4）。⑤で [measured] が付いてよいのは
        //   **火山タブの「影響範囲」の 1 行**（範囲内の建物数と道路セグメント数）
        //   だけで、接頭辞は VolcanoRows.SetMeasured が付ける。
        //
        // **実在の物理単位を名乗る文字列を足さないこと**（設計書 §7.5）。
        // ⑤が出してよいのは距離 (m)・高さ (m)・ゲーム内時間・0〜10 の段階だけで、
        // 溶岩の「温度」も「粘性」も⑤は持っていない。
        public static string VolcanoTitle = "Volcano";
        public static string VolcanoButtonLabel = "Volcano";
        public static string VolcanoButtonTooltip =
            "Volcano: pick a size on the slider, then click the map. The terrain change is permanent.";

        // ★★ **この文に印の文字列そのものを書かないこと**（④の全体レビュー I5 と
        //    同じ罠）。ja.txt の SourceVanilla は「[実測]」なので、本文に英語の
        //    "[measured]" を埋め込むと**日本語のプレイヤーは画面に一度も出ない
        //    文字列を探すことになる**。翻訳文には MeasuredToken を置き、
        //    表示の直前に VolcanoRows.SetModelNote が SourceVanilla へ差し替える。
        //    tools\CheckLocales.ps1 がこのキーにトークンが在ることを検査する。

        // ★ **常設の警告**（設計書 §7.1）。火山が無いときも出す。
        //   「うるさいから」と条件付きにしないこと —— 利用者は「不可逆でよい」と
        //   判断したが、それはプレイヤーに黙っていてよいという意味ではない。
        public static string VolcanoIrreversibleWarning =
            "The terrain change is permanent: nothing can undo it and it is saved.";

        public static string VolcanoInactive = "No volcano right now.";
        public static string VolcanoWaiting = "Waiting for the first simulation update.";
        public static string VolcanoTerrainUnavailable =
            "Disaster + cannot reach the terrain height array in this build of the game, so "
            + "volcanoes are disabled. See the diagnostic dump for which call could not be "
            + "resolved.";

        public static string VolcanoFormRow = "Shape";
        public static string VolcanoFormShield = "Shield volcano";
        public static string VolcanoFormStrato = "Stratovolcano";
        public static string VolcanoFormDome = "Lava dome";
        public static string VolcanoRadiusRow = "Radius";
        public static string VolcanoHeightRow = "Final height";
        public static string VolcanoPhaseRow = "Phase";
        public static string VolcanoGroundHeightRow = "Ground at the chosen spot";
        public static string VolcanoMetres = "m";

        // --- ⑤火山（Task 4: 配置ツール・影響範囲の調査） ---
        //
        // ★★ 設計書 §7.1 / §7.2 / §7.3 の 3 つの断定は、全部この節の文字列である。
        //   VolcanoIrreversibleWarning（§7.1）・VolcanoEstimateNote（§7.2）・
        //   VolcanoBuildabilityNote（§7.3）。**短くしたくなっても、
        //   どの断定を落とすことになるのかを先に読むこと。**
        //
        // ★ **単位を名乗る文字列はメートルとゲーム内分だけ。** ⑤は m/s も度も
        //   カロリーも持っていない（設計書 §7.5）。
        // ★ **退役した 2 件。** 火山を置くのは災害パネルの⑤タイルだけになり
        //   （バニラの災害ボタンと同じ 3 手）、パネルの中の「設置」ボタンは撤去した。
        //   **キーは消さない。別の意味で再利用してもいけない。**
        public static string VolcanoPlace = "Place a volcano";
        public static string VolcanoPlaceHint =
            "Click where the volcano should rise. Right-click to cancel.";

        /// <summary>
        /// 依頼を積んでから sim が拾うまでの 1 tick に出る行。**位相ではない** ——
        /// <c>VolcanoPhase.Surveying</c> は退役した（<c>VolcanoState</c> のクラス doc）。
        /// </summary>
        public static string VolcanoSurveying = "Surveying the area...";

        // ★★ **退役した 5 件（2026-08-21）。** 確認の窓を撤去したときに
        //    表示されなくなった行である。所有者の指示は「ほかの災害と同じように
        //    タイル → スライダー → クリックで起こす」で、確認の段そのものが無くなった。
        //
        //    **キーは消さない** —— Strings / Locales\en.txt / Locales\ja.txt のキー集合は
        //    一致させ続ける必要があり、既訳を捨てる理由も無い（FireWhirlName /
        //    FireWhirlTooltip と同じ扱い）。**別の意味で再利用してもいけない。**
        //    とくに ConfirmYes / ConfirmNo / SettingsChanged / PausedNote は
        //    「押せば始まる」「もう一度確認が出る」という、今は存在しない挙動を
        //    名乗っている。
        public static string VolcanoConfirmHeader = "Build a volcano here?";
        public static string VolcanoConfirmYes = "Build the volcano here";
        public static string VolcanoConfirmNo = "Cancel";
        public static string VolcanoConfirmDestroyed = "Will be destroyed (approx.)";

        public static string VolcanoBuildingsRow = "buildings";
        public static string VolcanoSegmentsRow = "roads";

        /// <summary>
        /// 火山タブの「影響範囲（概算）」の行。**確認の窓から降りてきた唯一の行**で、
        /// ⑤で <c>[実測]</c> の印が付くのもここだけである（<c>VolcanoRows</c> の grep 5）。
        /// 数は調べた瞬間のもので、地面をならしている間も街は動き続けるので
        /// 実際の数は前後する —— それは診断ダンプの <c>note: counts</c> に書いてある。
        /// </summary>
        public static string VolcanoFootprintRow = "Footprint (approx.)";

        // ★ §7.2 の「概数であることも明示する」。実数をそのまま出すとプレイヤーは
        //   「ぴったりその数だけ壊れる」と読む（ClearanceEstimate のクラス doc）。
        //
        // ★ **退役した 2 件。** 概数の言い訳の 2 行は画面から降ろし、
        //   行の見出しの「（概算）」（VolcanoFootprintRow）と診断ダンプへ移した
        //   （所有者の指示「あれこれ説明は出さなくていい」）。
        //   **キーは消さない**（LogChannelFireWhirl・*ResetButton と同じ扱い）。
        //   **別の意味で再利用してもいけない。**
        public static string VolcanoEstimateApprox = "About this many will be removed";
        public static string VolcanoEstimateNote =
            "The two rows above are what the survey counted at that moment. The city keeps "
            + "changing while the ground is cleared, so the real number will differ.";

        // ★★ 全体レビュー I6。**進行中の火山はセーブに残らない**（設計書 §1.3）。
        //   途中で保存して読み直すと、火口も噴火も溶岩も無い切り株の山が、完成させる
        //   ことも消すこともできない形で残る。同じ場所に置き直すと**その上に積み上がる**
        //   （VolcanoUplift は「今の地形」を元の高さとして控え直す）。

        // ★★ **退役（2026-08-21）。** 確認の窓と一緒に消えた。ポーズ中でも地図を
        //    クリックすれば火山は確定し、**解除した瞬間から動き出す**（バニラの災害と
        //    同じ）。**キーは消さない。別の意味で再利用してもいけない。**
        public static string VolcanoPausedNote = "The game is paused. Unpause to start.";

        // ★ 走査が 1 tick ぶんの上限で打ち切られたとき。**影響範囲の概数は下限になる。**
        //   これを黙っていると、概数どころか「実際より少ない数」を確定値のように見せる。

        public static string VolcanoSegmentsUnknown =
            "(roads could not be counted, but they are destroyed too)";

        // ★ §1.2 そのもの。**「壊さずに地面を上げる」が選べない理由**を書く ——
        //   これが書いていないと、破壊は MOD の乱暴な選択に見える。
        // ★ **退役 1 件。**「なぜ壊す必要があるのか」は設計の説明であって、
        //   起こす場でする判断ではない。同じ内容は診断ダンプにある
        //   （VolcanoFeature.WriteNotes）。キーは残すが再利用しないこと。
        public static string VolcanoClearingWarning =
            "The roads and buildings inside the footprint will be destroyed. Raising the "
            + "ground without clearing them first does not work: the game pins the terrain "
            + "back to the height of every road and building on every update, so the "
            + "mountain would end up full of flat trenches and bowls.";

        // ★ §7.3。**不具合ではないと明示する**（①の「なぜハザードマップが空か」と同じ扱い）。

        // ★ §C-10。天井に当たっても例外は出ず**無言で山頂が平らな台地になる**ので、
        //   火山タブの影響範囲の行に添えて名乗る（確認の窓が無くなったので、
        //   これは「置く前の警告」ではなく「なぜ低いのか」の説明になった）。
        public static string VolcanoHeightLimited =
            "The 1024 m terrain ceiling makes this volcano lower than asked.";

        // ★★ **退役（2026-08-21）。** 確認の窓が無くなったので「確認の直前に設定が
        //    変わっていたら調べ直す」経路そのものが消えた（クリックした瞬間の設定と
        //    スライダーの値で 1 回だけ調べる）。
        //    **キーは消さない。別の意味で再利用してもいけない。**
        public static string VolcanoSettingsChanged =
            "The settings changed, so the area was surveyed again.";

        /// <summary>ゲーム内の分。**実在の物理単位ではない**ので m/s の類とは扱いが違う。</summary>

        /// <summary>
        /// ゲーム内の時間。**「建てられる地面」の遅れはこちらで出す**（全体レビュー M13）——
        /// 分で出すと 400 を超える数になり、不可逆の決定の瞬間にプレイヤーが 60 で割る。
        /// </summary>
        public static string VolcanoHours = "in-game hours";

        public static string VolcanoShapeSetting = "Volcano shape";

        // --- ⑤火山（Task 5: 準備 — 道路と建物の段階的破壊） ---
        //
        // ★ ここの行にも [measured] は付かない。走査した半径も壊した数も
        //   **⑤が自分で数えた実績**であって、ゲームが計算した値ではない
        //   （設計書 §7.4。確認の 3 行だけが例外で、それは T4 の節にある）。
        public static string VolcanoClearingRow = "Clearing";
        public static string VolcanoClearedRadius = "Radius swept";

        // ★ 計画の文言から**内容を変えてある**。計画は「シェルター・地下保管庫・ダムは
        //   壊せません」と書いていたが、それは④が demolish:false のときの挙動しか
        //   読んでいなかったためで、T5 Step 1 の IL 実測では
        //   ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / DecorationBuildingAI /
        //   TsunamiBuoyAI の 5 つとも **demolish:true は受け付ける**
        //   （VolcanoClearing のクラス doc の 4）。名指しすると嘘になるので、
        //   「断られたものがあれば」という条件つきの一般形にしてある。

        // ★ 設計書 §1.2 そのもの。**「道路だけ諦めて隆起する」を選ばない**理由を書く。
        //
        // ★ 全体レビュー M9 で、判定が「道路の経路」から「準備の経路（道路と建物）」に
        //   広がった。**文言も一緒に広げること** —— 建物側が解決できない環境で
        //   「道路を取り除く方法が見つからなかった」と出すのは嘘である。
        public static string VolcanoClearingPathUnavailable =
            "Disaster + could not find a usable way to remove the roads and buildings inside "
            + "the footprint in this build of the game, so it will not build a volcano at all. "
            + "Raising the ground without removing them first does not work - the game pins "
            + "the terrain back to the height of every road and building on every update, and "
            + "the mountain would come out full of flat trenches and bowls.";

        public static string VolcanoClearingLead =
            "How far the clearing runs ahead of the uplift (m)";

        // ★ VolcanoStopButton は 2026-08-22 に退役した（キーごと削除）。
        //   所有者の判断「止めるボタンは不要です。だって実際に噴火を止めることなんて
        //   現実じゃできないでしょう？」。**同じキーを別の意味で使い回さないこと。**

        // --- ⑤火山（Task 6: 隆起） ---
        //
        // ★ ここも [measured] は付かない。進捗も山頂も有効半径も**⑤が決めた数字**で
        //   あって、ゲームが計算した値ではない（設計書 §7.4）。
        //
        // ★ **有効半径には「準備が届いた範囲」と添える**（VolcanoActiveRadiusRow）。
        //   これが罠 1 の可視化であり、実機で「準備が止まると隆起も止まる」ことを
        //   目で確かめられる唯一の行である。**短くしないこと。**
        public static string VolcanoUpliftRow = "Uplift";
        public static string VolcanoUpliftProgress = "Progress";
        public static string VolcanoSummitRow = "Summit";
        // ★ 2026-08-20 に意味が変わった行（実機の指摘⑤）。以前は「フットプリントを
        //   覆うタイルのうち何枚目か」だったが、⑤は今、**その tick に実際に変わった
        //   範囲だけ**を流す。プレイヤーにとって意味があるのは「変わった分が画面に
        //   出るまでに地形更新が何回要るか」で、1 なら同じ tick で全部出ている
        //   ＝ いちばん滑らかな状態である（VolcanoUplift のクラス doc）。
        public static string VolcanoUpliftMinutes = "Time the uplift takes (in-game minutes)";
        public static string VolcanoReliefStrength = "Relief on the mountain's flanks (%, 0 = a smooth cone)";

        // ★★ ⑤を構えているあいだの強度スライダーの説明。**言葉はここ 1 行だけ**で、
        //    ラベルには数字（倍率と実寸）しか出さない（VolcanoSizeReadout のクラス doc）。
        //    スライダーは 2026-08-21 から既に大きさのつまみだったが、画面のどこにも
        //    そう書いていなかった —— それが 2026-08-22 の指摘④である。
        public static string VolcanoSizeSliderTooltip =
            "Volcano size: the slider scales the radius and height set in the options.";

        // ★ 設計書 §7.3 の見積り。**不具合ではない**ことは VolcanoBuildabilityNote が
        //   既に言っているので、ここは数字の見出しだけを持つ。


        // --- ⑤火山（Task 7: 噴火） ---
        //
        // ★ ここも [measured] は付かない。噴出の強さは⑤が決めた 0〜10 の段階であって、
        //   ゲームが計算した値でも実在の物理量でもない（設計書 §7.4 /
        //   計画「出してよい断定の範囲」の 5）。
        public static string VolcanoEruptionRow = "Eruption";

        /// <summary>設定のチェックボックス（表示行の <c>VolcanoEruptionRow</c> と別物）。</summary>
        public static string VolcanoEruptionFx = "Draw the eruption plume";

        /// <summary>設定のチェックボックス。音そのものは MOD 同梱の wav である。</summary>
        public static string VolcanoEruptionSound = "Play the eruption sound";

        // ★ **短くしないこと。** ゲームには溶岩も噴火も存在しない（§B-5 で
        //   文字列ヒープにヒット 0）ことを名乗る唯一の場所である。
        //   炎だけがゲーム自身のもので、それも DLC 不要である。
        //
        // ★★ 音について「無音である」と書いていた版は**もう正しくない**。
        //    借り物のエフェクトが鳴らさないのは今も事実だが（§H-17）、⑤は自前の
        //    音源を同梱してバニラの効果音グループへ流し込むようになった。
        //    **ここは実測に合わせて直すこと** —— 出るものを「出ない」と書き続けるのは、
        //    出ないものを「出る」と書くのと同じ害である。

        // --- ⑤火山（火砕流の代用）---
        //
        // ★★ **短くしないこと。** 「火砕流」と名乗るものが火砕流ではないことを
        //   名乗る唯一の場所である。ゲームに火砕流のエフェクトは 1 つも無い
        //   （出荷アセットの EffectInfo 277 個を全数確認した）。
        public static string VolcanoPyroclasticSetting =
            "Show a dust surge fanning out down the slopes";


        // ★ バニラのエフェクトが 1 つも引けなかったときの断り。**噴火は続く。**
        public static string VolcanoEffectsMissing =
            "Disaster + could not borrow the game's particle effects in this environment, so "
            + "the eruption is not drawn. The mountain still rises, the lava still flows and "
            + "it still sets buildings on fire.";

        // --- ⑤火山（Task 8: 溶岩の前進と着火） ---
        //
        // ★ ここも [measured] は付かない。流れた距離も着火数も**⑤が自分で数えた
        //   実績**であって、ゲームが計算した値ではない（設計書 §7.4）。
        public static string VolcanoLavaRow = "Lava";
        public static string VolcanoLavaFlowsSetting = "Number of lava flows (0 = off)";
        public static string VolcanoLavaFireSetting = "Lava sets fire to what it touches";
        public static string VolcanoLavaLongest = "Longest flow";

        // ★ **短くしないこと。** 「木が燃えない」を「不具合」と読まれないための
        //   唯一の説明であり、ND DLC 非所持が正常であることを名乗る場所である（§B-7c）。

        // ★ 道路が燃えないのは⑤の手抜きではなく、ゲームに API が無いためである（§B-7d）。

        // --- ⑤火山（Task 9: 溶岩の描画） ---
        //
        // ★ T9 は他のどのタスクからも依存されない。**この 3 キーと設定 1 個と
        //   VolcanoFeature の 4 行を消せば、T9 を丸ごと落としても T1〜T8 は動く。**
        // ★★ 火山性地震。**②の設定とは無関係に効く**（VolcanoTremorShake のクラス doc）。
        //    「建物は壊さない」を必ず書く —— 地震と名の付くものが被害を出さないのは
        //    プレイヤーの予想と違うので、設定画面で先に言っておかないと不具合に見える。
        public static string VolcanoQuakeSetting = "Volcanic earthquakes (shaking only)";
        public static string VolcanoQuakeRow = "Volcanic earthquakes";

        // 火山雷（2026-08-22）。噴煙の描画とは別に切れる。
        public static string VolcanoLightningSetting =
            "Lightning inside the ash plume";
        public static string VolcanoLavaRenderSetting = "Draw the lava surface";

        // ★ 見えないことを黙らない。**流れも焦げも着火も変わらない**ことを同時に言う。
        public static string VolcanoLavaNoMaterial =
            "Disaster + could not build a material for the lava in this environment, so the "
            + "lava is invisible. It still flows, scorches the ground and sets buildings on fire.";
    }
}
