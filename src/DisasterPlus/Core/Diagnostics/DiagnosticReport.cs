using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// An immutable snapshot of the diagnostics. Built on the sim thread, read on the main
    /// thread. Once built, it is never rewritten.
    /// </summary>
    public class DiagnosticReport
    {
        public readonly IList<DiagnosticLine> Header;
        public readonly IList<AssumptionResult> Assumptions;
        public readonly IList<DiagnosticSection> Sections;

        private readonly int _passed;
        private readonly int _failed;

        public DiagnosticReport(
            IList<DiagnosticLine> header,
            IList<AssumptionResult> assumptions,
            IList<DiagnosticSection> sections)
        {
            // Keeps the overlay from dying every frame if the Game layer passes null.
            // We also take defensive copies, to protect against the caller modifying or
            // clearing the lists it handed us afterwards. _passed/_failed are computed
            // here, so the counts stay fixed even if the original lists are changed.
            Header = header == null ? new List<DiagnosticLine>() : new List<DiagnosticLine>(header);
            Assumptions = assumptions == null ? new List<AssumptionResult>() : new List<AssumptionResult>(assumptions);
            Sections = sections == null ? new List<DiagnosticSection>() : new List<DiagnosticSection>(sections);

            for (int i = 0; i < Assumptions.Count; i++)
            {
                if (Assumptions[i].Passed) _passed++;
                else _failed++;
            }
        }

        public int PassedCount { get { return _passed; } }
        public int FailedCount { get { return _failed; } }
        public bool HasFailures { get { return _failed > 0; } }
    }
}
