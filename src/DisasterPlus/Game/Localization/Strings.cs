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
        public static string ForecastResetButton = "Reset the forecast button position";
        public static string ForecastTitle = "Weather forecast";
        public static string ForecastTemperature = "Temperature";
        public static string ForecastRain = "Rain";
        public static string ForecastCloud = "Cloud";
        public static string ForecastWind = "Wind";
        public static string ForecastLightning = "Lightning";
        public static string ForecastTornado = "Tornado";
        public static string ForecastShowOnMap = "Show on map";
        public static string ForecastAtCursor = "Hazard at cursor";
        public static string ForecastUnavailable = "Weather data unavailable";
        public static string ForecastNdrNote =
            "Natural Disasters Renewal governs disaster occurrence.";
        public static string TrendRising = "up";
        public static string TrendFalling = "down";
        public static string TrendSteady = "steady";
        public static string LogChannelForecast = "Forecast";
    }
}
