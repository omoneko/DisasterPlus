namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// 前提 1 件の検証結果。
    /// Impact は「破れたときに何が壊れるか」で、プレイヤーにそのまま見せる文。
    /// </summary>
    public struct AssumptionResult
    {
        public readonly string Name;
        public readonly bool Passed;
        public readonly string Impact;

        public AssumptionResult(string name, bool passed, string impact)
        {
            Name = name ?? "";
            Passed = passed;
            Impact = impact ?? "";
        }
    }
}
