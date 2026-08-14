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

        public static string FireWhirlName = "Fire whirl";
        public static string FireWhirlTooltip = "Place a stationary, burning vortex";

        public static string NdrDetected =
            "Natural Disasters Renewal detected. Vanilla-side destruction follows its tornado settings; "
            + "fire spread is unaffected.";

        public static string FireWhirlNeedsDlc =
            "Fire whirls require the Natural Disasters DLC.";

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
        public static string EarthquakeShakeNote =
            "Vanilla's shaking has no radius limit at all: it only falls off with distance, and "
            + "it ignores intensity. The destruction radius above is a different quantity.";
        // 揺れの窓（Emerging|Active かつ e が m_activeDuration の内側）が開いていない。
        public static string EarthquakeNotShaking = "not shaking right now";

        // 収束中（Clearing）の地震では DestroyBuildings がそもそも呼ばれない
        // （§A-3: 呼び出しは Active 分岐にしか無い）。数値を出さずに理由を書く。
        public static string EarthquakeNoDamageInPhase =
            "this quake is past its shaking phase; the game runs no destruction for it any more";

        // カーソル位置には 2 つの別のモデルの値が並ぶ。違う数字が出るのが正常なので、
        // なぜ違うのかを画面で名乗る（全体レビュー M3）。
        public static string EarthquakeCursorModelsNote =
            "The destruction factor above and this hazard value are different quantities: a linear "
            + "ramp from the epicentre, versus the game's own map (distance to the crack segment, "
            + "squared falloff, radius 400 m larger, and only for a located quake). Both are read "
            + "from the game. The map overlay below draws the first one.";

        public static string EarthquakeFaultBand = "Fault zone";
        public static string EarthquakeFaultInside = "inside";
        public static string EarthquakeFaultOutside = "outside";
        public static string EarthquakeFaultBandNote =
            "The four rupture patches move every step, so this zone is where they can land, "
            + "not where they will.";

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
        public static string EarthquakeWaveformNote =
            "This is the game's own shake formula, evaluated at the sensor using the distance "
            + "from the epicentre instead of the distance from the camera.";
        public static string EarthquakeWaveformUnavailable =
            "Waveform drawing is unavailable on this build; showing the peak amplitude instead.";
        // 構築には成功したが、描画中に落ちて以後描かなくなった状態。以前はこの行が
        // パネル構築時にしか作られず、実行時に落ちると**黙って空欄**になっていた
        // （WaveformView のクラス doc が禁じている壊れ方そのもの、全体レビュー I6）。
        public static string EarthquakeWaveformDrawFailed =
            "Waveform drawing stopped after an error; showing the peak amplitude instead.";

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
            "Legend. Blue-green: the destruction factor s, in the same 10 steps as the bar above "
            + "(densest at the epicentre, zero at the rim). Magenta: the fault zone, drawn at a flat "
            + "density because its patches destroy with probability 1, not along a ramp. White: the "
            + "epicentre and the fault strike. Shaking has no radius limit, so the ground moves "
            + "outside the disc too.";
    }
}
