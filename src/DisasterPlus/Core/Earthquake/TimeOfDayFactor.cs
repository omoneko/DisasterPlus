namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 時間帯による被害の係数。**これは本 MOD が発明した演出値である。**
    ///
    /// **<see cref="NightFactor"/> = 1.15 に物理的な根拠は無い。** バニラのどこにも
    /// 「夜の方が被害が大きい」という根拠は無く（設計書 §4.3）、ゲームは時刻を
    /// 災害の計算に一切使っていない。15 % は「体感できるが、第 1 層の数字を
    /// 疑わせない程度」として選んだだけの値である。実在の統計を名乗らないこと。
    ///
    /// ── どこに掛かるのか（境界を絶対に越えない）──────────────────────
    ///
    /// **第 2 層の追加被害にだけ掛ける。** 唯一の適用点は
    /// <c>LongPeriodDamage</c> が <see cref="LongPeriodResponse.ExtraCollapseChance"/> の
    /// 結果に掛けるところで、**バニラの被害には一切触れない**。第 1 層の表示
    /// （震度・余裕度・警報・波形）にも掛けない —— あれらはバニラが実際に
    /// 計算している量であり、そこへ本 MOD の係数を混ぜたらもう実測ではない。
    ///
    /// ── 日夜サイクル OFF のときは黙って定数になる（§F-1）──────────────
    ///
    /// <c>m_enableDayNight == false</c> かつゲームモードのとき、
    /// <c>m_dayTimeOffsetFrames</c> が毎 sim フレーム再設定され、**時刻は永久に
    /// 12.0 に固定される**。したがってこの係数はその設定では常に
    /// <see cref="DayFactor"/>（＝ 1.0）になる。
    ///
    /// **式に特別扱いを足さない。** 12.0 は昼のど真ん中なので、係数は自然に 1.0 に
    /// なる（<c>MiddayIsTheDayFactor</c> がそれを固定している）。分岐を足すと
    /// 「日夜 OFF のときだけ別の道を通る」という検証しにくい経路が増える。
    /// **代わりに、その事実をパネルと診断ダンプで名乗る**
    /// （<c>Strings.EarthquakeNoDayNight</c>）。無効化を隠すのは、この MOD が
    /// 最も嫌う形の出力である。
    ///
    /// これを <c>Assumptions</c> の検証項目にはしない。日夜サイクルを切るのは
    /// **プレイヤーの正当な設定**であって前提の破れではなく、FAIL にすると
    /// 何も壊れていない環境に「一部機能が利用できません」が出続ける
    /// ——誤検知を消すための層が誤検知を出す形（①で 1 度直した I5 の指摘）に戻る。
    /// </summary>
    public static class TimeOfDayFactor
    {
        /// <summary><c>SimulationManager.SUNRISE_HOUR</c>（IL 実測、§F-1）。</summary>
        public const float SunriseHour = 5f;

        /// <summary><c>SimulationManager.SUNSET_HOUR</c>（IL 実測、§F-1）。</summary>
        public const float SunsetHour = 20f;

        /// <summary>昼の係数。**1.0 ちょうど**＝昼はバニラと何も変わらない。</summary>
        public const float DayFactor = 1f;

        /// <summary>
        /// 夜の係数。**本 MOD が選んだ演出値で、根拠は無い**（クラス doc）。
        /// </summary>
        public const float NightFactor = 1.15f;

        /// <summary>
        /// 昼夜の境界を渡りきるのにかける時間（時）。境界を段差にすると、
        /// 日の出の 1 フレームで追加被害が 15 % 跳ねる。
        /// </summary>
        public const float RampHours = 1f;

        /// <summary>1 日の長さ（時）。</summary>
        private const float HoursPerDay = 24f;

        /// <summary>
        /// ゲーム自身の夜の判定（<c>m_isNightTime = hour &lt; SUNRISE_HOUR || hour &gt; SUNSET_HOUR</c>、
        /// §F-1）をそのまま写したもの。**硬い境界**である。
        ///
        /// <see cref="Of"/> は境界を <see cref="RampHours"/> かけて滑らかに渡すので、
        /// 境界の前後 30 分では「昼だが係数が 1.0 ではない」時間帯が生じる。
        /// **表示側はこの 2 つを並べて出さないこと**（「昼」の横に 1.08 が出る）。
        /// </summary>
        public static bool IsNight(float hour)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return false;

            float h = Fold(hour);
            return h < SunriseHour || h > SunsetHour;
        }

        /// <summary>
        /// 時刻（時）から係数を出す。戻り値は必ず
        /// [<see cref="DayFactor"/>, <see cref="NightFactor"/>] に収まる。
        ///
        /// 壊れた値（NaN / Infinity）は <see cref="DayFactor"/>（中立）に倒す。
        /// **これは実際に建物を壊す確率に掛かる数字**なので、読み取り失敗を
        /// 倍率の跳ね上がりに変えてはいけない。
        /// </summary>
        public static float Of(float hour)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return DayFactor;

            float night = Nightness(Fold(hour));
            return DayFactor + (NightFactor - DayFactor) * night;
        }

        /// <summary>
        /// 「どれだけ夜か」∈ [0, 1]。境界の前後 <see cref="RampHours"/> / 2 で
        /// 線形に渡る（日の出なら 4:30 で 1、5:30 で 0）。
        /// </summary>
        private static float Nightness(float hour)
        {
            float half = RampHours * 0.5f;
            if (half <= 0f) return hour < SunriseHour || hour > SunsetHour ? 1f : 0f;

            // 夜 → 昼（日の出）。
            float rising = Ramp((hour - (SunriseHour - half)) / RampHours);
            // 昼 → 夜（日の入り）。
            float falling = Ramp((hour - (SunsetHour - half)) / RampHours);

            // 0:00 側は完全な夜、日の出で 0 へ、日の入りで再び 1 へ。
            float night = 1f - rising + falling;
            if (night < 0f) return 0f;
            return night > 1f ? 1f : night;
        }

        /// <summary>[0,1] にクランプした恒等写像（線形ランプの本体）。</summary>
        private static float Ramp(float t)
        {
            if (t <= 0f) return 0f;
            return t >= 1f ? 1f : t;
        }

        /// <summary>時刻を [0, 24) に畳む。</summary>
        private static float Fold(float hour)
        {
            float h = hour % HoursPerDay;
            if (h < 0f) h += HoursPerDay;
            // 負の極小値が丸めで 24.0 になる経路を塞ぐ。
            return h >= HoursPerDay ? 0f : h;
        }
    }
}
