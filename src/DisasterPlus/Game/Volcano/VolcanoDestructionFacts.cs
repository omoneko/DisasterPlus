namespace DisasterPlus.Game
{
    /// <summary>
    /// The measured destruction path used by the clearing stage
    /// (<see cref="VolcanoClearing"/>).
    /// **The source is IL facts doc §G-16**, and this is precisely the answer to the two items the
    /// design doc appendix wrote down as "unsettled".
    ///
    /// It is a struct for the same reason as <see cref="VolcanoTerrainFacts"/> (it holds nothing
    /// but bools, so caching it does not drag in Unity's fake-null self-repair problem).
    /// All defaults are false = "not read yet / no longer readable".
    ///
    /// **It has its own file because of the 800-line rule**
    /// (<see cref="VolcanoClearing"/> is the type with the thickest docs in ⑤, and putting them
    /// together would break the rule). As a type it is an appendage of
    /// <see cref="VolcanoClearing"/>, and the only thing outside that references it is a single
    /// entry in <see cref="Assumptions"/>.
    /// </summary>
    public struct VolcanoDestructionFacts
    {
        /// <summary>
        /// Whether <c>BuildingAI.CollapseBuilding(ushort, ref Building, InstanceManager.Group,
        /// bool testOnly, bool demolish, int burnAmount)</c> could be resolved.
        /// The same six-argument version ② already uses.
        /// </summary>
        public readonly bool BuildingCollapseResolved;

        /// <summary>
        /// Whether <c>NetAI.CollapseSegment(ushort, ref NetSegment, InstanceManager.Group,
        /// bool demolish)</c> could be resolved. **The only entrance through which ⑤ removes
        /// roads** (§G-16 (a)).
        /// </summary>
        public readonly bool SegmentCollapseResolved;

        /// <summary>
        /// Whether <c>NetManager.ReleaseSegment(ushort, bool keepNodes)</c> (public / instance /
        /// non-virtual) could be resolved.
        ///
        /// ★ ⑤ **never calls it itself**. It is checked because it is the cheapest evidence that
        /// the chain of delegation in §G-16 (a) — the one showing that <c>demolish: true</c> does
        /// not merely set a flag but **really releases the segment** — still has the same shape in
        /// this environment.
        /// Without it, the end of the chain has changed = this is a different build from the one
        /// that was measured.
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
        /// Whether the path for removing roads holds in this environment. **If it does not, ⑤
        /// builds no mountain** (design doc §1.2).
        /// </summary>
        public bool RoadPathUsable
        {
            get { return SegmentCollapseResolved && SegmentReleaseResolved; }
        }

        /// <summary>
        /// Whether the clearing stage itself holds. **This is ⑤'s second gate**
        /// (the first is <see cref="VolcanoTerrainFacts.Usable"/>).
        /// </summary>
        public bool Usable
        {
            get { return BuildingCollapseResolved && RoadPathUsable; }
        }
    }
}
