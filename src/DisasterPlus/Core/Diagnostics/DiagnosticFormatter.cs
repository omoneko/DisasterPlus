using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// レポートをテキスト行に整形する。オーバーレイとダンプが共用するので、
    /// 画面に見えているものとファイルの内容が食い違わない。
    ///
    /// 出力は ASCII のみ。IMGUI の既定フォントは日本語グリフを持たない
    /// 可能性が高く、豆腐になるため。開発者向けツールなので翻訳しない。
    /// </summary>
    public static class DiagnosticFormatter
    {
        public const int MaxValueLength = 120;

        private const string IndentUnit = "  ";

        public static List<string> Format(DiagnosticReport report)
        {
            var outLines = new List<string>();
            if (report == null) return outLines;

            for (int i = 0; i < report.Header.Count; i++)
            {
                outLines.Add(Render(report.Header[i]));
            }

            if (report.Assumptions.Count > 0)
            {
                outLines.Add("ASSUMPTIONS  " + report.PassedCount + " passed, "
                             + report.FailedCount + " FAILED");

                // 通った前提は列挙しない。全部出すとオーバーレイが埋まって
                // 肝心の FAIL が読みにくくなる。ダンプ側も同じ判断でよい。
                for (int i = 0; i < report.Assumptions.Count; i++)
                {
                    var a = report.Assumptions[i];
                    if (a.Passed) continue;
                    outLines.Add(IndentUnit + "FAIL  " + Sanitize(a.Name));
                    if (a.Impact.Length > 0)
                    {
                        outLines.Add(IndentUnit + IndentUnit + "-> " + Sanitize(a.Impact));
                    }
                }
            }

            for (int i = 0; i < report.Sections.Count; i++)
            {
                var s = report.Sections[i];
                outLines.Add("");
                string head = "-- " + Sanitize(s.Name) + " --  " + HealthText(s.Health);
                if (s.Note.Length > 0) head += "  (" + Sanitize(s.Note) + ")";
                outLines.Add(head);

                for (int k = 0; k < s.Lines.Count; k++)
                {
                    outLines.Add(Render(s.Lines[k]));
                }
            }

            return outLines;
        }

        private static string HealthText(FeatureHealth h)
        {
            switch (h)
            {
                case FeatureHealth.Degraded: return "Degraded";
                case FeatureHealth.Disabled: return "Disabled";
                default: return "Healthy";
            }
        }

        private static string Render(DiagnosticLine line)
        {
            string pad = "";
            for (int i = 0; i < line.Indent; i++) pad += IndentUnit;

            string label = Sanitize(line.Label);
            string value = Sanitize(line.Value);
            if (value.Length == 0) return pad + label;
            return pad + label + ": " + value;
        }

        /// <summary>
        /// 改行と非 ASCII を潰し、長すぎる値を切る。
        /// 1 行 = 1 要素の構造を、例外メッセージのような多行文字列でも崩さない。
        /// </summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new System.Text.StringBuilder(s.Length);
            bool truncated = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r') continue;   // produces no output, skip before cap check

                char emitted = (c == '\n' || c == '\t') ? ' '
                             : (c < 32 || c > 126) ? '?' : c;

                if (sb.Length >= MaxValueLength) { truncated = true; break; }
                sb.Append(emitted);
            }

            if (truncated) sb.Append("...");
            return sb.ToString();
        }
    }
}
