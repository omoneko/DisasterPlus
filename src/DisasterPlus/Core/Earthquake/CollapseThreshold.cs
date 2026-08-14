namespace DisasterPlus.Core.Earthquake
{
    /// <summary>1 建物ぶんの、この地震に対する固定しきい値 2 個。不変。</summary>
    public struct BuildingThresholds
    {
        /// <summary>倒壊しきい値。1 回目の引き。</summary>
        public readonly int Collapse;

        /// <summary>出火しきい値。2 回目の引き。</summary>
        public readonly int Burn;

        public BuildingThresholds(int collapse, int burn)
        {
            Collapse = collapse;
            Burn = burn;
        }
    }

    /// <summary>
    /// **本機能の目玉。** バニラが引くのと同一のしきい値を先読みする。
    ///
    /// IL 事実文書 §A-3、DisasterHelpers.DestroyBuildings の建物ループ:
    ///   rnd  = new Randomizer(buildingID | (seed &lt;&lt; 16))   // seed は災害 ID
    ///   fD   = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    ///   hitD = rnd.Int32(10000) &lt; fD * probability * 10000
    ///   hitB = rnd.Int32(10000) &lt; fB * probability * 10000
    ///
    /// **この種はフレームにもステップにも依存しない。**（建物, 災害）の組に対して定数で、
    /// 同じステップ内の 5 回の呼び出しも、次のステップも、同じ 2 個を引く。
    /// したがって全体円盤に関する限り、**倒れるかどうかは地震が始まった瞬間に既に
    /// 決まっている**。依頼文の「倒壊はおそらくランダム」は、建物ごとのしきい値が
    /// 一様乱数だという意味では正しく、距離減衰が無いという意味では間違っていた。
    ///
    /// **主張できる範囲の限界（呼び出し側は必ず併記すること）:**
    /// ここで出せるのは**全体円盤（probability = 0.02、震央中心）についてだけ**である。
    /// 断層に沿った 4 個の円盤は毎ステップ位置が振り直され、probability = 1 で
    /// その場をほぼ確実に壊す。断層帯の内側の建物について「倒れません」と言うと、
    /// 確信を持って誤った断定になる。<c>FaultBand</c>（Task 4）で内外を判定し、
    /// 内側なら別判定であることを明示すること。
    /// </summary>
    public static class CollapseThreshold
    {
        /// <summary>IL: rnd.Int32(10000)。UInt32 版のオーバーロードが呼ばれている。</summary>
        public const uint Draws = 10000u;

        /// <summary>全体円盤の probability（IL: ldc.r4 0.02）。震央で 2%。</summary>
        public const float GlobalDiscProbability = 0.02f;

        /// <summary>
        /// IL: buildingID | (disasterID &lt;&lt; 16)。
        /// どちらも ushort なので int に広げてから合成する。災害 ID は 1〜255 なので
        /// 実際には負にならないが、上位ビットが立った場合の符号拡張は
        /// VanillaRandomizer の ctor が引き受ける（そちらのテスト参照）。
        /// </summary>
        public static int SeedFor(ushort buildingId, ushort disasterId)
        {
            return buildingId | (disasterId << 16);
        }

        /// <summary>
        /// この（建物, 災害）の組に固定された 2 個のしきい値。
        /// **引く順序が意味を持つ** — 1 回目が倒壊、2 回目が出火。入れ替えないこと。
        /// </summary>
        public static BuildingThresholds For(ushort buildingId, ushort disasterId)
        {
            var rnd = new VanillaRandomizer(SeedFor(buildingId, disasterId));
            int collapse = rnd.Int32(Draws);
            int burn = rnd.Int32(Draws);
            return new BuildingThresholds(collapse, burn);
        }

        /// <summary>
        /// IL の比較そのもの: <c>threshold &lt; localFactor * probability * 10000</c>。
        /// int が float へ昇格して比較される。等号は含まない。
        ///
        /// **これは円盤の種類を問わない汎用の比較である。** localFactor（＝IL の fD）を
        /// どの円盤の幾何から計算するかは呼び出し側の責任で、全体円盤なら
        /// <see cref="GlobalDiscHits"/> を使うこと。断層 4 円盤の fD は
        /// <c>(2w - dist) / Max(1, 2w - w)</c> であって全体円盤の <c>1 - d/R</c> とは
        /// 別式なので、両者を取り違えると数字は出るが意味が無くなる。
        /// </summary>
        public static bool Hits(int threshold, float localFactor, float probability)
        {
            if (float.IsNaN(localFactor) || float.IsNaN(probability)) return false;
            return threshold < localFactor * probability * Draws;
        }

        /// <summary>
        /// 全体円盤での当たり判定。<paramref name="localFactor"/> は
        /// <see cref="SeismicIntensity.At"/> が返す s（＝<c>1 - d/R</c>）でなければならない。
        /// probability を呼び出し側に選ばせないための入口で、
        /// <see cref="GlobalDiscCollapseDistance"/> と必ず対で使う。
        /// </summary>
        public static bool GlobalDiscHits(int threshold, float localFactor)
        {
            return Hits(threshold, localFactor, GlobalDiscProbability);
        }

        /// <summary>
        /// この建物が**全体円盤で**倒壊し始める震央距離。0 以下ならどの距離でも倒れない。
        ///
        ///   threshold &lt; (1 - d/R) * 0.02 * 10000
        ///   d &lt; R * (1 - threshold / (0.02 * 10000))
        ///
        /// 「予言」ではなく、バニラが既に決めた値から導いた**事実**である。
        /// ただし全体円盤についてのみ（クラス doc の限界を参照）。
        ///
        /// ── なぜ probability を引数に取らないのか（この設計は意図的） ──────
        ///
        /// 以前の署名は <c>CollapseDistance(threshold, intensity, probability)</c> だったが、
        /// **ランプの分母を <c>R = RadiusOf(intensity)</c> に決め打ちしていた**。R は
        /// 全体円盤の幾何そのものである。断層 4 円盤は毎ステップ振り直される**別の中心**の
        /// まわりで <c>min = w, max = 2w</c> のランプを持つので、あの署名に
        /// <c>probability = 1</c> を渡すと「もっともらしいが何の意味も無い距離」が返っていた。
        /// 禁止事項として doc に書くだけでは、いつか誰かが渡す。**引数を消して
        /// 構造的に不可能にした。** 断層帯の到達距離が要るなら
        /// <see cref="FaultBand.HalfWidthAt"/> を使うこと（あちらは別のモデルである）。
        /// </summary>
        public static float GlobalDiscCollapseDistance(int threshold, byte intensity)
        {
            // 定数なので 0 にも NaN にもならない。分母のガードは不要。
            const float denominator = GlobalDiscProbability * Draws;

            float d = SeismicIntensity.RadiusOf(intensity) * (1f - threshold / denominator);
            return d > 0f ? d : 0f;
        }
    }
}
