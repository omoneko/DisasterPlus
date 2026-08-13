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
            // また、呼び出し元が渡したリストを後から変更・クリアすることから保護するため、
            // 防御的コピーを作る。_passed/_failed はここで計算されるため、
            // 元のリストが変更されても counts は不変のままになる。
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
