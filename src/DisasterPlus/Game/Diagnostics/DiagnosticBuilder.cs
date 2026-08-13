using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断行の組み立て。sim スレッドで 1 機能ずつ使い回す。
    /// Take() で中身を引き渡し、自分は空になる（次の機能で再利用するため）。
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

        /// <summary>組み立てた行を渡し、自身を空にする。</summary>
        public IList<DiagnosticLine> Take()
        {
            var taken = _lines;
            _lines = new List<DiagnosticLine>();
            return taken;
        }
    }
}
