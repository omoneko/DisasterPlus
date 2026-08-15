namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 河川氾濫の水位計算。<c>WaterSource.m_target</c> をどれだけ持ち上げるかを決める。
    ///
    /// ── 単位はゲーム由来。上げ幅は④の発明 ─────────────────────────
    ///
    /// <c>WaterSource.m_target</c> は**絶対水位、1/64 m 単位の UInt16**である
    /// （IL 事実文書 §D-4。海面 40 m = 2560）。<see cref="UnitsPerMetre"/> と
    /// <see cref="MaxTargetUnits"/> はゲーム側の事実で、④が選んだ数字ではない。
    /// **ここを 1 m 単位と取り違えると、2 m 上げたつもりが 128 m 上がる。**
    ///
    /// 一方 <see cref="RiseMetresOf"/> と <see cref="RiseAt"/> の式は**全部④の発明**である。
    /// バニラに洪水災害は存在せず（§D-1、<c>GenericFloodAI</c> はフィールド 0・
    /// メソッド 0 の空クラス）、参照すべき「台風で川がどれだけ増水するか」の量が無い。
    ///
    /// ── 飽和させる（巻き戻りが最悪の壊れ方）───────────────────────
    ///
    /// <see cref="RaisedTarget"/> は <c>int</c> で計算してから飽和クランプし、
    /// **その後で <c>(ushort)</c> へキャストする**。65535 を超えた瞬間に
    /// <c>m_target</c> が 0 付近へ巻き戻ると、自己調整の泉が反転して
    /// **川を吸い尽くす**（§D-4 の吸い込み側ループ）。
    ///
    /// **下げない。** 負の上げ幅も NaN も元の値をそのまま返す。下げると川が干上がる。
    /// </summary>
    public static class FloodTarget
    {
        /// <summary>
        /// <c>m_target</c> の 1 m あたりの目盛り数。**ゲーム側の事実**（§D-4）。
        /// <c>TerrainManager.RawHeights</c> と同じスケール。
        /// </summary>
        public const int UnitsPerMetre = 64;

        /// <summary><c>m_target</c>（UInt16）の上限。**ゲーム側の事実。**</summary>
        public const ushort MaxTargetUnits = 65535;

        /// <summary>
        /// 最強の台風が最大でどれだけ水位を上げるか（m）。**④が選んだ数字。**
        /// 6 m は「谷筋の川が明らかに溢れるが、丘の街区までは届かない」あたりで、
        /// そもそも <c>natural &amp;&amp; terrain >= m_target</c> のセルはスキップされる
        /// （§D-4）ので、丘の上には最初から水が載らない。
        /// </summary>
        public const float MaxRiseMetres = 6f;

        /// <summary>
        /// これ以下の降雨では 1 mm も上げない。**④が選んだ数字。**
        /// 台風の外縁を掠めただけで川が溢れるのはおかしい。
        /// </summary>
        public const float MinRainForRise = 0.5f;

        /// <summary>強さスライダーの最大値（0〜10）。</summary>
        private const float MaxStrength = 10f;

        /// <summary>強度（byte）の最大値。</summary>
        private const float MaxIntensity = 255f;

        /// <summary>
        /// 台風の中心での上げ幅（m）。強度と降雨量と設定の強さから決める。
        /// **これは④が発明した式である**（クラス doc）。
        ///
        /// <paramref name="strength"/> が 0 以下なら厳密に 0（スライダーで完全に切れる）。
        /// <paramref name="rain"/> が <see cref="MinRainForRise"/> 以下でも 0。
        /// </summary>
        public static float RiseMetresOf(byte intensity, float rain, int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            // NaN は !(rain > MinRainForRise) 側で落ちる。
            if (!(rain > MinRainForRise)) return 0f;
            if (rain > 1f) rain = 1f;

            float rainFactor = (rain - MinRainForRise) / (1f - MinRainForRise);
            float rise = (intensity / MaxIntensity) * rainFactor
                         * (strength / MaxStrength) * MaxRiseMetres;

            if (float.IsNaN(rise) || rise <= 0f) return 0f;
            return rise > MaxRiseMetres ? MaxRiseMetres : rise;
        }

        /// <summary>
        /// 台風の中心から <paramref name="distanceToCentre"/> m の水源の上げ幅（m）。
        /// 中心で <paramref name="peakRise"/>、強風域の縁でちょうど 0 まで線形に落ちる。
        ///
        /// <paramref name="galeRadius"/> が 0（＝プレハブ半径が読めていない）なら
        /// **0 を返す**。推測した半径で川を溢れさせない（設計書 §6）。
        /// </summary>
        public static float RiseAt(float distanceToCentre, float galeRadius, float peakRise)
        {
            if (!(galeRadius > 0f)) return 0f;
            if (!(peakRise > 0f)) return 0f;
            if (!(distanceToCentre >= 0f) || distanceToCentre >= galeRadius) return 0f;

            float rise = peakRise * (1f - distanceToCentre / galeRadius);
            return rise > 0f ? rise : 0f;
        }

        /// <summary>
        /// 持ち上げた <c>m_target</c>。**飽和する。巻き戻らない**（クラス doc）。
        /// 上げ幅が 0 以下・NaN なら元の値をそのまま返す（下げない）。
        /// </summary>
        public static ushort RaisedTarget(ushort original, float riseMetres)
        {
            if (!(riseMetres > 0f)) return original;

            // ★ int で計算してから飽和クランプし、その後で (ushort) へキャストする。
            //   先にキャストすると 65535 を超えた瞬間に 0 付近へ巻き戻り、
            //   自己調整の泉が反転して川を吸い尽くす。
            int units = (int)(riseMetres * UnitsPerMetre + 0.5f);
            if (units <= 0) return original;

            long raised = (long)original + units;
            return raised >= MaxTargetUnits ? MaxTargetUnits : (ushort)raised;
        }
    }
}
