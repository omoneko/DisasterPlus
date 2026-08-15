namespace DisasterPlus.Game
{
    /// <summary>
    /// 準備段（<see cref="VolcanoClearing"/>）が使う破壊経路の実測。
    /// **IL 事実文書 §G-16 が典拠**で、設計書 付録が「未確定」と書いていた
    /// 2 項目の答えそのものである。
    ///
    /// struct なのは <see cref="VolcanoTerrainFacts"/> と同じ理由（bool しか持たないので
    /// キャッシュしても Unity の fake-null 自己修復問題を持ち込まない）。
    /// 既定値は全て false ＝「まだ／もう読めていない」。
    ///
    /// **専用ファイルにしてあるのは 800 行の規則のため**である
    /// （<see cref="VolcanoClearing"/> は⑤でいちばん doc の厚い型で、
    /// 同居させると規則を割る）。型としては <see cref="VolcanoClearing"/> の付属物で、
    /// 他から参照するのは <see cref="Assumptions"/> の 1 件だけである。
    /// </summary>
    public struct VolcanoDestructionFacts
    {
        /// <summary>
        /// <c>BuildingAI.CollapseBuilding(ushort, ref Building, InstanceManager.Group,
        /// bool testOnly, bool demolish, int burnAmount)</c> を解決できたか。
        /// ②が既に使っている 6 引数版と同じもの。
        /// </summary>
        public readonly bool BuildingCollapseResolved;

        /// <summary>
        /// <c>NetAI.CollapseSegment(ushort, ref NetSegment, InstanceManager.Group,
        /// bool demolish)</c> を解決できたか。**⑤が道路を取り除く唯一の入口**（§G-16 (a)）。
        /// </summary>
        public readonly bool SegmentCollapseResolved;

        /// <summary>
        /// <c>NetManager.ReleaseSegment(ushort, bool keepNodes)</c>（public / instance /
        /// 非 virtual）を解決できたか。
        ///
        /// ★ ⑤はこれを**自分では呼ばない**。見るのは、<c>demolish: true</c> が
        /// 「フラグを立てるだけ」ではなく**本当にセグメントを解放する**という §G-16 (a) の
        /// 委譲の鎖が、この環境でも同じ形をしていることの最も安い証拠だからである。
        /// これが無ければ、鎖の終端が変わっている ＝ 測ったのとは別のビルドである。
        /// </summary>
        public readonly bool SegmentReleaseResolved;

        public VolcanoDestructionFacts(bool buildingCollapseResolved,
                                       bool segmentCollapseResolved,
                                       bool segmentReleaseResolved)
        {
            BuildingCollapseResolved = buildingCollapseResolved;
            SegmentCollapseResolved = segmentCollapseResolved;
            SegmentReleaseResolved = segmentReleaseResolved;
        }

        /// <summary>
        /// 道路を取り除く経路がこの環境で成立するか。**成立しなければ⑤は山を作らない**
        /// （設計書 §1.2）。
        /// </summary>
        public bool RoadPathUsable
        {
            get { return SegmentCollapseResolved && SegmentReleaseResolved; }
        }

        /// <summary>
        /// 準備段そのものが成立するか。**これが⑤の 2 つ目の門である**
        /// （1 つ目は <see cref="VolcanoTerrainFacts.Usable"/>）。
        /// </summary>
        public bool Usable
        {
            get { return BuildingCollapseResolved && RoadPathUsable; }
        }
    }
}
