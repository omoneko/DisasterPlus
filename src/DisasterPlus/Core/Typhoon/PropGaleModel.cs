using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 暴風がプロップ（看板・標識・パラソル・街灯…）を持っていくかどうか。
    /// **エンジン非依存の純関数だけ。**
    ///
    /// ── 所有者の依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 街に暴風および大雨による小さな建物の破壊や看板プロップの破壊、
    /// &gt; 局所的な洪水を発生させることです。
    ///
    /// 建物と洪水は既にある（<c>TyphoonWind.Gale</c> / <c>TyphoonFlood</c>）。
    /// <b>プロップだけが 1 つも壊れていなかった</b>ので、ここで足す。
    ///
    /// ── ★★ 何を壊し、何を壊さないか ────────────────────────────────
    ///
    /// 壊れるのは<b>小さくて軽いもの</b>である。判定は
    /// <see cref="FragilityOf"/> —— プロップの<b>寸法</b>から出す。
    ///
    /// <list type="bullet">
    /// <item>看板・標識・ゴミ箱・パラソル（〜3 m）… 飛ぶ</item>
    /// <item>街灯・電柱（〜8 m）… 強い風でだけ折れる</item>
    /// <item>それより大きいもの（給水塔・煙突など）… 飛ばさない</item>
    /// </list>
    ///
    /// ★★ <b>木は対象外である。</b> 木は <c>TreeManager</c> の持ち物で、
    ///   <c>PropManager</c> には入っていない。倒木をやるなら別の型になる
    ///   ——**ここで「木も入っているつもり」にならないこと。**
    ///
    /// ★★ <b>飛ばした数は戻せない。</b> <c>PropManager.ReleaseProp</c> は
    ///   取り消せないので、しきい値は<b>渋め</b>にしてある。
    ///   「台風が来たら看板が全部消える」は、直せない壊れ方である。
    /// </summary>
    public static class PropGaleModel
    {
        /// <summary>これより弱い風では 1 つも飛ばない（m/秒）。</summary>
        public const float MinWindMetresPerSecond = 24f;

        /// <summary>この風速で、いちばん壊れやすいものが必ず飛ぶ（m/秒）。</summary>
        public const float FullWindMetresPerSecond = 62f;

        /// <summary>この寸法までは「いちばん壊れやすい」（m）。看板・標識の大きさ。</summary>
        public const float FragileSizeMetres = 3f;

        /// <summary>この寸法より大きいものは飛ばさない（m）。</summary>
        public const float SturdySizeMetres = 9f;

        /// <summary>
        /// 1 回の判定で飛ぶ割合の上限。**1 にしない** ——
        /// 1 にすると、暴風域に入った瞬間にその一帯の看板が全部消える。
        /// </summary>
        public const float MaxTakeRatio = 0.55f;

        /// <summary>
        /// プロップの壊れやすさ <c>[0,1]</c>。1 が看板、0 が「飛ばさない」。
        /// <paramref name="sizeMetres"/> は当たり判定の代表寸法。
        /// </summary>
        public static float FragilityOf(float sizeMetres)
        {
            if (IsBad(sizeMetres) || sizeMetres <= 0f) return 0f;
            if (sizeMetres <= FragileSizeMetres) return 1f;
            if (sizeMetres >= SturdySizeMetres) return 0f;

            return 1f - (sizeMetres - FragileSizeMetres)
                        / (SturdySizeMetres - FragileSizeMetres);
        }

        /// <summary>
        /// この風とこの壊れやすさで、飛ぶ確率 <c>[0, <see cref="MaxTakeRatio"/>]</c>。
        /// </summary>
        public static float TakeChance(float windMetresPerSecond, float fragility)
        {
            float f = Clamp01(fragility);
            if (f <= 0f) return 0f;

            if (IsBad(windMetresPerSecond)) return 0f;
            if (windMetresPerSecond <= MinWindMetresPerSecond) return 0f;

            float w = (windMetresPerSecond - MinWindMetresPerSecond)
                      / (FullWindMetresPerSecond - MinWindMetresPerSecond);
            w = Clamp01(w);

            // 風の効きは線形ではない（力は速度の 2 乗）。
            return MaxTakeRatio * w * w * f;
        }

        /// <summary>
        /// このプロップは飛ぶか。**決定論的** ——
        /// <paramref name="propId"/> と <paramref name="round"/> が同じなら
        /// 何度呼んでも同じ答えになる（この MOD の乱数の規律）。
        /// </summary>
        /// <param name="round">
        /// 判定の回。**毎回同じ値を渡さないこと** —— 渡すと、1 度助かった看板は
        /// 二度と飛ばない（風が強くなっても）。
        /// </param>
        public static bool Takes(ushort propId, uint round, float windMetresPerSecond,
                                 float fragility, uint seed)
        {
            float chance = TakeChance(windMetresPerSecond, fragility);
            if (chance <= 0f) return false;

            return DeterministicRandom.Unit(seed, (uint)propId * 31u + round * 7919u) < chance;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
