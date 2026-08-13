using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// 診断の不変スナップショット。sim スレッドで作り、main スレッドで読む。
    /// 一度作ったら書き換えない。
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
            // Game 層が null を渡してもオーバーレイが毎フレーム落ちないようにする。
            Header = header ?? new List<DiagnosticLine>();
            Assumptions = assumptions ?? new List<AssumptionResult>();
            Sections = sections ?? new List<DiagnosticSection>();

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
