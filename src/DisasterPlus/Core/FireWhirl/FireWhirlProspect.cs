using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 「なぜ今、火災旋風が出ていないのか」を 1 個の値にしたもの。
    ///
    /// ── なぜ要るか ────────────────────────────────────────
    ///
    /// ③は**自然発生しか経路を持たない**（手動の配置ツールは撤去した）。
    /// つまり「出ない」が既定の状態であり、実機テストでは
    ///
    ///   DIAG fireWhirl: burning=0 active=0
    ///
    /// の 1 行しか出ないまま「壊れているのか、まだ火が足りないのか」が
    /// 分からない、という報告が実際に上がっている。**その 2 つを見分けられない
    /// 診断は診断ではない。**
    ///
    /// ここは <see cref="FireWhirlDetector.Detect(System.Collections.Generic.IList{BurningBuilding},
    /// FireWhirlConfig, System.Collections.Generic.IList{Vec2}, out FireWhirlProspect)"/> が
    /// 判定と**同じ 1 パスの中で**埋める。別パスで数え直すと、診断が本判定と
    /// 食い違う（＝いちばん質の悪い診断になる）。
    ///
    /// エンジン非依存の値型。<see cref="Describe"/> の文面はユニットテストで固定する。
    /// </summary>
    public struct FireWhirlProspect
    {
        /// <summary>直近に完了した走査で燃えていた建物の総数。</summary>
        public readonly int BurningCount;

        /// <summary>
        /// **いちばん密な塊の棟数**（判定半径以内、自分自身を含む）。
        /// <see cref="RequiredCount"/> に届いていなければ、それが「出ない理由」である。
        /// </summary>
        public readonly int DensestCount;

        /// <summary>いちばん密な塊の重心。<see cref="DensestCount"/> が 0 のときは意味を持たない。</summary>
        public readonly Vec2 DensestCentre;

        /// <summary>判定半径（m）。診断の 1 行だけを読んで意味が通るように、閾値も一緒に運ぶ。</summary>
        public readonly float RadiusMetres;

        /// <summary>判定棟数。</summary>
        public readonly int RequiredCount;

        /// <summary>
        /// 条件は満たしたのに、生存中の旋風／クールダウン中の地点に近すぎて
        /// 捨てられた候補の数。**「火は足りているのに出ない」の唯一の説明**である。
        /// </summary>
        public readonly int SuppressedCount;

        /// <summary>この tick に実際に採用された候補の数。</summary>
        public readonly int AcceptedCount;

        public FireWhirlProspect(int burningCount, int densestCount, Vec2 densestCentre,
                                 float radiusMetres, int requiredCount,
                                 int suppressedCount, int acceptedCount)
        {
            BurningCount = burningCount;
            DensestCount = densestCount;
            DensestCentre = densestCentre;
            RadiusMetres = radiusMetres;
            RequiredCount = requiredCount;
            SuppressedCount = suppressedCount;
            AcceptedCount = acceptedCount;
        }

        /// <summary>あと何棟足りないか。足りていれば 0。</summary>
        public int Shortfall
        {
            get
            {
                int missing = RequiredCount - DensestCount;
                return missing > 0 ? missing : 0;
            }
        }

        /// <summary>
        /// 診断の 1 行（英語）。**「条件が足りない」と「壊れている」を必ず言い分ける。**
        ///
        /// この関数は「壊れている」とは決して言わない —— 壊れているかどうかを
        /// 知っているのは呼び出し側（prefab の解決可否や延焼の空振り検知）である。
        /// ここが言えるのは<b>条件の側の事実</b>だけで、それを言い切ることが
        /// 「条件は足りているのに何も起きない」を残りの診断に押し出す。
        /// </summary>
        public string Describe()
        {
            if (BurningCount <= 0)
            {
                return "nothing is on fire; a fire whirl needs " + RequiredCount
                     + " buildings burning within " + Format(RadiusMetres) + " m of each other";
            }

            if (DensestCount < RequiredCount)
            {
                return BurningCount + " burning, but the densest group is only " + DensestCount
                     + " within " + Format(RadiusMetres) + " m (need " + RequiredCount
                     + "; " + Shortfall + " more, or a wider radius / lower count in the settings)";
            }

            if (AcceptedCount <= 0 && SuppressedCount > 0)
            {
                return "the fire is dense enough (" + DensestCount + " within "
                     + Format(RadiusMetres) + " m), but " + SuppressedCount
                     + " candidate(s) were too close to a live fire whirl or a spot still cooling down";
            }

            if (AcceptedCount <= 0)
            {
                // 密度は足りている・離隔でも弾かれていない。ここから先で止まっているなら
                // 原因は条件の側ではない（prefab / スポーン失敗）。**そう言い切る。**
                return "the fire is dense enough (" + DensestCount + " within "
                     + Format(RadiusMetres) + " m) and nothing suppressed it; "
                     + "if no fire whirl appears, the cause is not the fire conditions";
            }

            return AcceptedCount + " spawn point(s) met the conditions this pass ("
                 + DensestCount + " within " + Format(RadiusMetres) + " m)";
        }

        private static string Format(float metres)
        {
            return metres.ToString("F0");
        }
    }
}
