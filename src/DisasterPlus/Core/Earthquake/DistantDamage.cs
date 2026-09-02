namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>震源から遠くても、規模に応じた確率で火災と倒壊が起きる。</b>
    /// 海溝型地震のためのモデル。**エンジン非依存。**
    ///
    /// ── 所有者の指示（2026-09-02）────────────────────────────────
    ///
    /// &gt; 海溝型地震の、地震による被害が少ないです。震源から離れていても
    /// &gt; 一定確率で火災や倒壊が起きるようにしてください（地震の規模に合わせて）
    ///
    /// ── ★★ なぜ海溝型<b>だけ</b>被害が少ないのか（原因は分かっている）───────
    ///
    /// バニラの地震被害は <c>EarthquakeAI.SimulationStep</c> が
    ///
    /// <code>
    /// R = 2000 + m_intensity * 20                     （§A-3、SeismicIntensity）
    /// DestroyBuildings(preRadius: R, min: 0, max: R, probability: 0.02f)
    ///   建物ごとに fD = 1 - dist/R  を掛ける
    /// </code>
    ///
    /// と<b>震央を中心にした円盤</b>で配る。断層型は<b>プレイヤーが指した場所</b>が
    /// 震央なので、この円盤は街の上に落ちる。
    ///
    /// ★★ **海溝型の震央は沖の海の上である**（<c>TrenchQuakeSlot</c> の定義）。
    ///   だから円盤の中心は海に落ち、街に届くのは<b>外周のいちばん薄いところ</b>
    ///   だけになる。強度 100 で R = 4,000 m、3 km 沖なら fD = 0.25、
    ///   実効確率は <c>0.02 × 0.25 = 0.5%</c> —— <b>これが「被害が少ない」の正体で、
    ///   バニラの不具合ではなく、円盤モデルを海の上に置いた当然の帰結である。</b>
    ///
    /// ── 現実の海溝型地震はそうではない ────────────────────────────
    ///
    /// プレート境界の破壊は<b>数百 km の長さ</b>に及ぶので、震央からの距離で
    /// きれいに減らない。遠方でも長く揺れ、<b>火災は転倒した火器と電気系から
    /// 散発的に出る</b> —— 震央からの距離ではなく、そこに建物があるかで決まる。
    ///
    /// そこでこのモデルは<b>床（<see cref="FloorFraction"/>）を持つ</b>。
    /// 遠方でも近傍の <see cref="FloorFraction"/> 倍は残る。それが所有者の言う
    /// 「離れていても一定確率で」である。
    ///
    /// ── 規模との対応（「地震の規模に合わせて」）──────────────────────
    ///
    /// 強度 <c>i</c> の効き方は <c>(i/255)²</c> にしてある。**線形にしない。**
    /// 線形だと既定のスライダー（55）でも最大（255）の 1/5 の被害が出てしまい、
    /// 「大地震だけが街を壊す」という区別が消える。二乗なら 1/21 になる。
    ///
    /// 目安。**オフラインで数えた実測値**である（強さスライダー既定 6、
    /// 4 km 四方に 8,000 棟の街、震央はその 3 km 沖）:
    ///
    /// | 強度 | 到達 | 倒壊 | 出火 |
    /// |------|------|------|------|
    /// | 55   |  9,300 m |   9 棟 |  14 棟 |
    /// | 100  | 12,000 m |  32 棟 |  53 棟 |
    /// | 150  | 15,000 m |  77 棟 | 129 棟 |
    /// | 255  | 21,300 m | 243 棟 | 405 棟 |
    ///
    /// 同じ条件でバニラの円盤が配るのは、強度 100 で <b>0.50%</b> ——
    /// このモデルは <b>1.24%</b>（倒壊と出火の合計）で、約 2.5 倍である。
    ///
    /// ── 抽選は (地震, 建物) だけで決まる ──────────────────────────
    ///
    /// ★★ **フレームも走査回数も混ぜない**（<c>LongPeriodDamage</c> と同じ規律）。
    ///   混ぜると同じ建物が走査のたびに抽選し直され、<b>地震が長引くほど
    ///   際限なく壊れる</b>。混ぜなければ、上の表は<b>走査が何回走っても変わらない</b>。
    /// </summary>
    public static class DistantDamage
    {
        /// <summary>
        /// 到達距離 ÷ バニラの全体円盤 R。
        ///
        /// ★ 3 倍にしてあるのは、**強度 255 で 21,300 m ＝ マップ全域**を確実に
        ///   覆うためである（25 タイル全域でも対角の半分は約 12,200 m）。
        ///   既定のスライダー（55）でも 9,300 m あり、9 タイルの街はほぼ入る。
        ///   海溝型の震央は沖にあるので、ここが短いと<b>街に一切届かない</b>。
        /// </summary>
        public const float ReachFactor = 3f;

        /// <summary>
        /// 到達端に残る割合。**これが「離れていても一定確率で」の中身である。**
        /// 0 にすると距離に比例して消え、依頼の前と同じ「遠方は無傷」に戻る。
        /// </summary>
        public const float FloorFraction = 0.35f;

        /// <summary>
        /// 床を落とし始める位置（到達距離に対する割合）。
        ///
        /// ★ これが無いと、到達端で確率が <see cref="FloorFraction"/> から 0 へ
        ///   <b>段差で落ちる</b>。マップの端で被害がぷつりと切れるのは目に見えるので、
        ///   外側の 1/4 で滑らかに 0 へ落とす。
        /// </summary>
        public const float TaperStart = 0.75f;

        /// <summary>強度 255・震央・強さ満目盛りでの倒壊確率。</summary>
        public const float PeakCollapseChance = 0.06f;

        /// <summary>
        /// 同・出火確率。**倒壊より高い。** 海溝型で街を焼くのは主に火災で、
        /// 倒壊は揺れの強い近傍に偏る（1923 も 1995 も 2011 もそうだった）。
        /// </summary>
        public const float PeakFireChance = 0.10f;

        /// <summary>1 棟あたりの倒壊確率の上限。</summary>
        public const float MaxCollapseChance = 0.25f;

        /// <summary>1 棟あたりの出火確率の上限。</summary>
        public const float MaxFireChance = 0.35f;

        /// <summary>強さスライダーの満目盛り。<c>strength / 10</c> が倍率になる。</summary>
        public const float MaxStrength = 10f;

        /// <summary>
        /// 火災の抽選に使う塩。倒壊と<b>同じ鍵で引かない</b> ——
        /// 同じ鍵だと「倒壊しなかった建物ほど燃えにくい」という相関が付き、
        /// 2 つの災いが独立でなくなる。
        /// </summary>
        public const uint FireSalt = 0x5EA1F17Eu;

        /// <summary>
        /// この被害が届く距離（m）。バニラの全体円盤の <see cref="ReachFactor"/> 倍。
        ///
        /// ★ これは<b>本 MOD がこの被害を足す範囲</b>であって、揺れの物理的な
        ///   境界ではない（<c>LongPeriodResponse.RangeOf</c> と同じ断り）。
        /// </summary>
        public static float ReachMetres(byte intensity)
        {
            return SeismicIntensity.RadiusOf(intensity) * ReachFactor;
        }

        /// <summary>
        /// 距離による減り方（[0, 1]）。震央で 1、到達端で 0。
        ///
        /// <code>
        /// u = d / reach
        /// f = Floor + (1 - Floor) * (1 - u)          床のぶんは遠方でも残る
        /// if (u &gt; TaperStart) f *= (1 - u) / (1 - TaperStart)    端で 0 へ
        /// </code>
        /// </summary>
        public static float Falloff(float distance, byte intensity)
        {
            float reach = ReachMetres(intensity);
            if (!(reach > 0f)) return 0f;

            // NaN は !(d >= 0) 側で落ちる。
            if (!(distance >= 0f)) return 0f;
            if (distance >= reach) return 0f;

            float u = distance / reach;
            float f = FloorFraction + (1f - FloorFraction) * (1f - u);

            if (u > TaperStart)
            {
                f *= (1f - u) / (1f - TaperStart);
            }

            if (f < 0f) return 0f;
            return f > 1f ? 1f : f;
        }

        /// <summary>
        /// 規模の効き方（[0, 1]）。<c>(i/255)²</c>（クラス doc）。
        /// </summary>
        public static float MagnitudeFactor(byte intensity)
        {
            float t = intensity / 255f;
            return t * t;
        }

        /// <summary>
        /// この建物が倒壊する確率。**0 を返す条件は全て「壊さない」側に倒れている。**
        /// </summary>
        /// <param name="distance">震央からの水平距離（m）。</param>
        /// <param name="intensity"><c>DisasterData.m_intensity</c> の生値。</param>
        /// <param name="strength">設定の強さ（0〜10）。0 で完全に無効。</param>
        public static float CollapseChance(float distance, byte intensity, float strength)
        {
            return ChanceOf(distance, intensity, strength,
                            PeakCollapseChance, MaxCollapseChance);
        }

        /// <summary>この建物が出火する確率。条件は <see cref="CollapseChance"/> と同じ。</summary>
        public static float FireChance(float distance, byte intensity, float strength)
        {
            return ChanceOf(distance, intensity, strength, PeakFireChance, MaxFireChance);
        }

        private static float ChanceOf(float distance, byte intensity, float strength,
                                      float peak, float max)
        {
            if (float.IsNaN(distance) || float.IsNaN(strength)) return 0f;
            if (strength <= 0f) return 0f;

            float scale = strength / MaxStrength;
            if (scale > 1f) scale = 1f;

            float chance = peak * MagnitudeFactor(intensity) * Falloff(distance, intensity)
                           * scale;

            if (chance < 0f) return 0f;
            return chance > max ? max : chance;
        }
    }
}
