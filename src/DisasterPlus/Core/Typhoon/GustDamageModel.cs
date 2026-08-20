namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 局所被害域（パッチ）の中の倒壊確率。**これも本 MOD が発明した物理である。**
    ///
    /// <see cref="WindDamageModel"/>（台風全体の風害）との違いは 2 つ:
    ///
    /// 1. **桁が違う。** あちらは 1 走査あたり最大 8 %、こちらは 1 回の遭遇で
    ///    最大 <see cref="MaxCollapseChance"/> ＝ 70 %。狭い範囲だけが竜巻に
    ///    襲われたように壊れる、というのが指示そのものである。
    /// 2. **建物の高さを見ない。** 竜巻は平屋も高層も等しく壊す。
    ///    高さで差を付けると「竜巻の被害」ではなく「強い風害」になってしまう。
    ///
    /// 抽選は呼び出し側（<c>Game/Typhoon/TyphoonGust</c>）が
    /// <c>DeterministicRandom.Unit(パッチの種, 建物 ID)</c> で行い、
    /// **フレームを混ぜない**。つまり 1 個の建物に対する目は 1 個のパッチにつき
    /// 1 回しか決まらず、パッチが近づいて確率が上がったときに初めて倒れる ——
    /// これが「パッチが通り過ぎた跡が壊れている」の実装である。
    /// フレームを混ぜると、同じ建物が毎 tick 抽選し直されて確率が青天井になる。
    ///
    /// **単位は無い。** 返るのは確率だけで、風速（m/s）ではない。
    /// </summary>
    public static class GustDamageModel
    {
        /// <summary>パッチの中心での最大確率（強さ 10・破壊力 1.0 のとき）。</summary>
        public const float MaxCollapseChance = 0.70f;

        /// <summary>強さスライダーの最大値（0〜10）。</summary>
        private const float MaxStrength = 10f;

        /// <summary>
        /// 倒壊確率。
        ///
        /// <paramref name="distanceFraction"/> は パッチ中心からの距離 ÷ パッチ半径
        /// （0 が中心、1 が縁）。**1 以上は 0**（パッチの外は 1 棟も壊さない）。
        ///
        /// <paramref name="strengthFraction"/> はパッチ個体の破壊力 [0, 1]
        /// （<c>GustPatchPlan.Patch</c> が出す）。
        ///
        /// <paramref name="strength"/> は設定の 0〜10。**0 で厳密に 0 を返す**
        /// （スライダーで完全に無効化できることの保証）。<c>.cgs</c> は公開契約なので
        /// 範囲外の値もここでクランプする。
        ///
        /// 壊れた入力（NaN・負）は 0。**「距離 NaN で全棟倒壊」を作らない。**
        /// </summary>
        public static float CollapseChance(float distanceFraction, float strengthFraction,
                                           int strength)
        {
            if (strength <= 0) return 0f;
            if (strength > (int)MaxStrength) strength = (int)MaxStrength;

            // NaN は !(x >= 0) 側で落ちる。
            if (!(distanceFraction >= 0f) || distanceFraction >= 1f) return 0f;
            if (float.IsNaN(strengthFraction) || strengthFraction <= 0f) return 0f;
            if (strengthFraction > 1f) strengthFraction = 1f;

            // 中心で 1、縁で 0。2 乗にして縁を素早く落とす
            // ——「縁でも半分壊れる」を作らない。
            float falloff = 1f - distanceFraction;
            falloff *= falloff;

            float chance = MaxCollapseChance * falloff * strengthFraction
                           * (strength / MaxStrength);

            if (float.IsNaN(chance) || chance <= 0f) return 0f;
            return chance > MaxCollapseChance ? MaxCollapseChance : chance;
        }
    }
}
