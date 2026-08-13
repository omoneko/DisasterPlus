namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>診断出力の 1 行。Indent は整形時の字下げ段数。</summary>
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
