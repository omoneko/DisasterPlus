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
        /// </summary>
        public const float DefaultDeadband = 0.02f;

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
