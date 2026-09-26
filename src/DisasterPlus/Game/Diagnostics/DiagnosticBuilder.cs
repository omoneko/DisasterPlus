using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Assembles diagnostic lines. Reused one feature at a time on the sim thread.
    /// Take() hands the contents over and leaves this empty (ready to be reused by the next
    /// feature).
    /// </summary>
    public class DiagnosticBuilder
    {
        private List<DiagnosticLine> _lines = new List<DiagnosticLine>();

        public void Line(int indent, string label, string value)
        {
            _lines.Add(new DiagnosticLine(indent, label, value));
        }

        public void Line(int indent, string label)
        {
            _lines.Add(new DiagnosticLine(indent, label, ""));
        }

        /// <summary>Hands over the assembled lines and empties itself.</summary>
        public IList<DiagnosticLine> Take()
        {
            var taken = _lines;
            _lines = new List<DiagnosticLine>();
            return taken;
        }
    }
}
