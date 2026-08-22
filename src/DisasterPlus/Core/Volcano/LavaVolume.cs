using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **噴火の規模から、流れ出る溶岩の量を決める。**
    /// <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// > 噴火の規模によって流れ出るマグマの量も変えてください。
    ///
    /// それまで溶岩は**本数も長さも設定の固定値**で、スライダーを 1.0 にしても
    /// 25.5 にしても同じ本数が同じだけ流れていた。山の大きさだけが変わって
    /// 溶岩が変わらないのは、**大きい火山ほど溶岩が目立たなくなる**ということである。
    ///
    /// ── 何を「規模」とするか ─────────────────────────────
    ///
    /// <b>山の半径</b>である。⑤の大きさのつまみは強度スライダー 1 本だけで
    /// （<see cref="VolcanoSizeScale"/>）、それは半径と最終高を同じ倍率で伸ばす。
    /// 噴出の強さ（<c>EruptionIntensityUnit</c>）は<b>使わない</b> ——
    /// あれは噴火のあいだ 1 区切りごとにゆらぐ量で、それで本数を決めると
    /// **流れの本数が噴火中に増えたり減ったりする**（既に流れているものを
    /// 消すことになる）。規模は<b>置いた瞬間に決まって二度と動かない</b>ものでなければ、
    /// 溶岩の側が状態を持てない。
    ///
    /// ── 比 ───────────────────────────────────────
    ///
    /// 本数も長さも<b>半径に比例させない</b>。半径は 250 m 〜 3000 m と 12 倍あり、
    /// 比例させると小さい火山から溶岩が 1 本も出ず、大きい火山では
    /// <c>MaxFlows</c> に張り付いて差が消える。<see cref="Unit"/> は
    /// <b>基準の半径に対する平方根</b>で、12 倍の半径差を 3.5 倍に均す。
    /// </summary>
    public static class LavaVolume
    {
        /// <summary>
        /// 規模 1.0 とみなす半径（m）。成層火山の推奨値
        /// （<c>VolcanoShape.DefaultRadiusOf(Strato)</c>）と同じにしてある ——
        /// **既定の設定でスライダーを既定位置に置いたとき、今までどおりの本数**になる。
        /// </summary>
        public const float ReferenceRadiusMetres = 1200f;

        /// <summary>規模の下限。0 にしない（0 は「溶岩を出さない」であって小さい噴火ではない）。</summary>
        public const float MinUnit = 0.45f;

        /// <summary>規模の上限。</summary>
        public const float MaxUnit = 2.0f;

        /// <summary>
        /// 流れの長さの倍率の下限。**1 未満にもする** ——
        /// 小さい火山の溶岩が裾を越えて何 km も走るのは、規模が効いていないのと同じである。
        /// </summary>
        public const float MinLengthFactor = 0.40f;

        /// <summary>流れの長さの倍率の上限。</summary>
        public const float MaxLengthFactor = 1.8f;

        /// <summary>
        /// 半径から規模 <c>[MinUnit, MaxUnit]</c> へ。
        /// <paramref name="radiusMetres"/> が異常なら 1（＝設定どおり）。
        /// </summary>
        public static float Unit(float radiusMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 1f;

            float ratio = radiusMetres / ReferenceRadiusMetres;
            float unit = (float)Math.Sqrt(ratio);

            if (unit < MinUnit) return MinUnit;
            if (unit > MaxUnit) return MaxUnit;
            return unit;
        }

        /// <summary>
        /// 実際に出す本数。<paramref name="configuredFlows"/> は設定の本数で、
        /// <paramref name="maxFlows"/> は実装の上限である。
        ///
        /// ★★ <b>設定が 0 なら 0 を返す。</b> 0 は「溶岩を完全に切っている」であって
        ///   「いちばん小さい噴火」ではない —— 規模で 1 本に戻してはいけない。
        ///
        /// ★ 設定が 1 以上なら<b>必ず 1 本以上</b>返す。小さい火山で 0 本にすると、
        ///   プレイヤーには「溶岩の機能が壊れている」としか見えない。
        /// </summary>
        public static int FlowCount(int configuredFlows, int maxFlows, float radiusMetres)
        {
            if (configuredFlows <= 0) return 0;
            if (maxFlows <= 0) return 0;

            int wanted = (int)(configuredFlows * Unit(radiusMetres) + 0.5f);
            if (wanted < 1) wanted = 1;
            if (wanted > configuredFlows && wanted > maxFlows) wanted = maxFlows;
            if (wanted > maxFlows) wanted = maxFlows;
            return wanted;
        }

        /// <summary>
        /// 流れの長さに掛ける倍率 <c>[MinLengthFactor, MaxLengthFactor]</c>。
        /// 本数より効き方を強くしてある（本数は整数で刻みが粗く、
        /// **長さのほうが「量が変わった」と見えやすい**）。
        /// </summary>
        public static float LengthFactor(float radiusMetres)
        {
            float unit = Unit(radiusMetres);

            // 規模 1 で 1.0。上下には規模より少しだけ強く振る。
            float factor = 1f + (unit - 1f) * 1.35f;

            if (factor < MinLengthFactor) return MinLengthFactor;
            if (factor > MaxLengthFactor) return MaxLengthFactor;
            return factor;
        }

        /// <summary>
        /// 1 本が歩いてよい歩数。<paramref name="baseSteps"/> は実装の上限
        /// （<c>LavaPath.MaxSteps</c>）で、**それを超えない**（配列の長さがそれで決まる）。
        /// </summary>
        public static int StepBudget(int baseSteps, float radiusMetres)
        {
            if (baseSteps <= 0) return 0;

            int steps = (int)(baseSteps * LengthFactor(radiusMetres) + 0.5f);
            if (steps < 1) steps = 1;
            if (steps > baseSteps) steps = baseSteps;
            return steps;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
