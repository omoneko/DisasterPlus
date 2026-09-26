using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// Formats a report into lines of text. The overlay and the dump share it, so what is
    /// on screen and what is in the file cannot disagree.
    ///
    /// The output is ASCII only, because IMGUI's default font most likely has no Japanese
    /// glyphs and would render tofu. This is a developer tool, so it is not translated.
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

                // Assumptions that passed are not listed. Print them all and the overlay
                // fills up, making the FAILs that matter hard to read. The dump side can
                // take the same view.
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
        /// Flattens newlines and non-ASCII, and cuts values that are too long.
        /// Keeps the one-line-per-item structure intact even for multi-line strings such as
        /// exception messages.
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
