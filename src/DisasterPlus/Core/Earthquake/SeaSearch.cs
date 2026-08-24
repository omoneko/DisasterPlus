namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// クリックされた地点から<b>いちばん近い海</b>を探すときの、調べる順序と刻み。
    /// **エンジン非依存の純関数だけ。**（水があるかどうかを聞くのは Game 側）
    ///
    /// ── 所有者の指示（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 発生は、アイコンクリック→左クリックした場所に一番近い海で発生に
    /// &gt; してください。
    ///
    /// ── なぜ順序を Core に置くのか ──────────────────────────────
    ///
    /// 「近い順に調べる」は<b>目で見て確かめられない</b>。順序が壊れていても
    /// 海はどこかで見つかるので、<b>少し遠い海が選ばれるだけ</b>で、
    /// 実機では誰も気づけない。だから順序そのものをテストで固定する。
    ///
    /// <see cref="OutwardCellOrder"/> の同心リング（チェビシェフ距離）を使う。
    /// 厳密な最近傍ではない —— リングは正方形なので、対角のセルが
    /// 同じリングの軸上のセルより √2 倍遠い。<b>それでよい。</b>
    /// 刻み（<see cref="StepMetres"/>）より細かい差は、
    /// 「一番近い海」としてどのみち区別が付かない。
    ///
    /// ★★ <b>見つからなかったときに「適当な海」を返さない。</b>
    ///   内陸マップでは海が無いのが正しい答えである。
    ///   呼び出し側は断って、理由を名乗ること。
    /// </summary>
    public static class SeaSearch
    {
        /// <summary>
        /// 1 リングぶんの距離（m）。細かすぎると遠い海まで届かず、
        /// 粗すぎると狭い入り江を跨いでしまう。
        /// </summary>
        public const float StepMetres = 96f;

        /// <summary>
        /// 探す最大のリング半径。<see cref="StepMetres"/> × これ ＝ 探索の届く距離で、
        /// 96 × 96 = 9216 m ——**マップの半辺（8640 m）より少し広い**ので、
        /// マップのどこを指しても、海があるなら必ず届く。
        /// </summary>
        public const int MaxRing = 96;

        /// <summary>
        /// 調べる地点の総数。呼び出し側はこれで <see cref="At"/> を回す。
        /// </summary>
        public static int Count { get { return CountUpTo(MaxRing); } }

        /// <summary>リング <paramref name="ring"/> までの地点数。</summary>
        public static int CountUpTo(int ring)
        {
            if (ring < 0) return 0;
            int side = ring * 2 + 1;
            return side * side;
        }

        /// <summary>
        /// <paramref name="ordinal"/> 番目に調べる地点の、クリック地点からのずれ（m）。
        /// <b>近い順である</b>（0 番目はクリック地点そのもの）。
        ///
        /// 範囲外なら false を返す。**「それらしい 0」を返さない。**
        /// </summary>
        public static bool At(int ordinal, out float offsetX, out float offsetZ)
        {
            offsetX = 0f;
            offsetZ = 0f;

            int dx, dz;
            if (!OutwardCellOrder.Offset(ordinal, out dx, out dz)) return false;
            if (dx > MaxRing || dx < -MaxRing || dz > MaxRing || dz < -MaxRing) return false;

            offsetX = dx * StepMetres;
            offsetZ = dz * StepMetres;
            return true;
        }

        /// <summary>
        /// その地点がクリック地点から何 m 離れているか（診断と、
        /// 「思ったより遠い海が選ばれた」を名乗るため）。
        /// </summary>
        public static float DistanceMetres(float offsetX, float offsetZ)
        {
            return (float)System.Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
        }
    }
}
