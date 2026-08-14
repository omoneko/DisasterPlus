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
        /// </summary>
        public static bool Hits(int threshold, float localFactor, float probability)
        {
            if (float.IsNaN(localFactor) || float.IsNaN(probability)) return false;
            return threshold < localFactor * probability * Draws;
        }

        /// <summary>
        /// この建物が全体円盤で倒壊し始める震央距離。0 以下ならどの距離でも倒れない。
        ///
        ///   threshold &lt; (1 - d/R) * p * 10000
        ///   d &lt; R * (1 - threshold / (p * 10000))
        ///
        /// 「予言」ではなく、バニラが既に決めた値から導いた**事実**である。
        /// ただし全体円盤についてのみ（クラス doc の限界を参照）。
        /// </summary>
        public static float CollapseDistance(int threshold, byte intensity, float probability)
        {
            float denominator = probability * Draws;
            if (denominator <= 0f || float.IsNaN(denominator)) return 0f;

            float d = SeismicIntensity.RadiusOf(intensity) * (1f - threshold / denominator);
            return d > 0f ? d : 0f;
        }
    }
}
