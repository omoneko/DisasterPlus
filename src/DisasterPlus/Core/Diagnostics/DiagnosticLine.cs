namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>One line of diagnostic output. Indent is how many levels to indent when
    /// formatting.</summary>
    public struct DiagnosticLine
    {
        public readonly int Indent;
        public readonly string Label;
        public readonly string Value;

        public DiagnosticLine(int indent, string label, string value)
        {
            Indent = indent < 0 ? 0 : indent;
            Label = label ?? "";
            Value = value ?? "";
        }
    }
}
