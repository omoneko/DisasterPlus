namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// The verification result for one assumption.
    /// Impact is "what breaks when this assumption fails", written as a sentence shown to
    /// the player as-is.
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
