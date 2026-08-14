namespace DisasterPlus.Core.Forecast
{
    /// <summary>
    /// WeatherManager の current と target の差から傾向を出す。
    ///
    /// これがこの機能の中核。バニラは hazard（静的な危険度）しか見せず、
    /// 「今どちらへ向かっているか」を出さない。current/target の両方が
    /// public フィールドとして読めるので、そこから時間軸を作る。
    /// </summary>
    public static class TrendMath
    {
        /// <summary>
        /// 既定の不感帯。current は target へ連続的に補間されるので、
        /// 厳密比較だと常に Rising か Falling になり矢印が意味を失う。
        ///
        /// 雨・雲・霧はいずれも 0.0-1.0 に正規化された値なので、この 0.02 は
        /// 「全レンジの 2%」を意味する。同じ数字を気温に使ってはいけない
        /// （<see cref="TemperatureDeadband"/> 参照）。
        /// </summary>
        public const float DefaultDeadband = 0.02f;

        /// <summary>
        /// 気温専用の不感帯（度）。
        ///
        /// 気温だけスケールが違う。雨・雲・霧は 0.0-1.0 の正規化値だが、気温は摂氏の
        /// 実値（季節推移でおよそ -20〜+35）を取るため、<see cref="DefaultDeadband"/>
        /// の 0.02 は「0.02 度」＝実質ゼロになる。季節補間は毎 tick わずかに動くので、
        /// それでは傾向が常時 Rising か Falling に張り付き、「今どちらへ向かっているか」
        /// という本機能の中核の表示が意味を失う。
        ///
        /// 0.5 度という値は、パネルの表示が F1（0.1 度刻み）で丸めることより粗く、かつ
        /// ゲーム内の季節変化（1 ゲーム内日で数度）は取り逃さない水準として選んだ。
        ///
        /// この定数がここ（Core）にあるのは意図的。以前は WeatherReader（Game/、
        /// ユニットテスト不可）に 0.5f がベタ書きされており、他の不感帯が全て
        /// TrendMath から来ているのに気温だけがテストの当たらない場所にあった。
        /// </summary>
        public const float TemperatureDeadband = 0.5f;

        public static Trend Of(float current, float target, float deadband)
        {
            // 破損値で矢印が嘘をつかないこと。NaN の比較は全て false になるため明示的に弾く。
            if (float.IsNaN(current) || float.IsNaN(target)) return Trend.Steady;
            if (float.IsInfinity(current) || float.IsInfinity(target)) return Trend.Steady;

            float band = deadband > 0f ? deadband : 0f;
            float diff = target - current;

            // 境界はちょうど band のとき Steady 側へ倒す（ちらつきを減らす）。
            if (diff > band) return Trend.Rising;
            if (diff < -band) return Trend.Falling;
            return Trend.Steady;
        }
    }
}
