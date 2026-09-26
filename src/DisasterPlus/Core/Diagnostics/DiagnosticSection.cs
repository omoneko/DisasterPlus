using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>The diagnostics for one feature. Note carries things like why it
    /// degraded.</summary>
    public class DiagnosticSection
    {
        public readonly string Name;
        public readonly FeatureHealth Health;
        public readonly string Note;
        public readonly IList<DiagnosticLine> Lines;

        public DiagnosticSection(string name, FeatureHealth health, string note, IList<DiagnosticLine> lines)
        {
            Name = name ?? "";
            Health = health;
            Note = note ?? "";
            // Take a defensive copy, so that the caller changing the list it passed in later
            // cannot reach us.
            Lines = lines == null ? new List<DiagnosticLine>() : new List<DiagnosticLine>(lines);
        }
    }
}
