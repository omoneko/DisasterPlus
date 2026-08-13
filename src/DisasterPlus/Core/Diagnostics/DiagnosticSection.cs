using System.Collections.Generic;

namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>1 機能ぶんの診断。Note は劣化理由など。</summary>
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
            // 呼び出し元が渡したリストを後から変更することから保護するため防御的コピーを作る。
            Lines = lines == null ? new List<DiagnosticLine>() : new List<DiagnosticLine>(lines);
        }
    }
}
