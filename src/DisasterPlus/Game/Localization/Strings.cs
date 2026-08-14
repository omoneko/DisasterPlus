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

        public static string EarthquakeAtCursor = "Shaking at cursor";
        // 半径 R の外はバニラが preRadius で判定自体を打ち切る領域なので、
        // 「強度 0.0」ではなく「圏外」と出す（SeismicIntensity.At の doc）。
        public static string EarthquakeOutOfRange = "outside the shaken area";

        public static string EarthquakeFaultBand = "Fault zone";
        public static string EarthquakeFaultInside = "inside";
        public static string EarthquakeFaultOutside = "outside";
        public static string EarthquakeFaultBandNote =
            "The four rupture patches move every step, so this zone is where they can land, "
            + "not where they will.";

        public static string EarthquakeShowOnMap = "Show on map";

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
        public static string EarthquakeBandWeak = "weak";
        public static string EarthquakeBandModerate = "moderate";
        public static string EarthquakeBandStrong = "strong";
        public static string EarthquakeBandSevere = "very strong";
    }
}
