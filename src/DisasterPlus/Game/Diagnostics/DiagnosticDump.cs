using System;
using System.IO;
using System.Text;
using System.Threading;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 状態を 1 ファイルに書き出す。オーバーレイと同じ DiagnosticFormatter を使うので、
    /// 画面に見えているものとファイルの内容が食い違わない。
    ///
    /// Ctrl+ホットキーは main スレッド（MonoBehaviour.Update）から押される。だが
    /// IDisasterFeature.WriteDiagnostics（延いては FeatureHost.BuildReport）は
    /// sim スレッド専用の契約になっている。ここで main スレッドから直接 BuildReport()
    /// を呼ぶと、その契約を信じて書かれた将来の機能実装（②〜⑤）がロック無しで
    /// 壊れる可能性があるため、書き出しは「main → 依頼、sim → 組み立て、
    /// main → ファイル書き出し」の 3 段に分ける。
    /// </summary>
    public static class DiagnosticDump
    {
        public const string FileName = "DisasterPlus-diagnostics.txt";

        // main スレッドが立て、sim スレッドが下ろす。1 ビットのフラグなので
        // lock より Interlocked の方が軽く、デッドロックの心配も無い。
        private static int _requested;

        // sim スレッドが組み立てたレポートを、main スレッドが書き出すまでの受け渡し場所。
        // DiagnosticReport 自体は不変なので、参照を渡すだけで安全。
        private static readonly object _pendingGate = new object();
        private static DiagnosticReport _pendingReport;

        /// <summary>main スレッドから呼ぶ。次の sim tick でダンプ 1 回ぶんが組み立てられる。</summary>
        public static void RequestDump()
        {
            Interlocked.Exchange(ref _requested, 1);
        }

        /// <summary>
        /// sim スレッドから毎 tick 呼ぶ。依頼が立っていれば true を返し、同時にフラグを下ろす
        /// （1 回の依頼で 2 回組み立てない）。
        /// </summary>
        public static bool ConsumeRequest()
        {
            return Interlocked.Exchange(ref _requested, 0) == 1;
        }

        /// <summary>
        /// sim スレッドから呼ぶ。組み立て済みレポートを main スレッドの書き出し待ちに置くだけで、
        /// ここではファイル I/O を一切行わない（sim tick を止めないため）。
        /// </summary>
        public static void SubmitReport(DiagnosticReport report)
        {
            lock (_pendingGate) { _pendingReport = report; }
        }

        /// <summary>
        /// main スレッドから毎フレーム呼ぶ。書き出し待ちのレポートがあれば 1 回だけファイルに書く。
        /// 何も無ければロックを取るだけで即座に戻るので、毎フレーム呼んでもコストは無視できる。
        /// </summary>
        public static void FlushPendingWrite()
        {
            DiagnosticReport report;
            lock (_pendingGate)
            {
                if (_pendingReport == null) return;
                report = _pendingReport;
                _pendingReport = null;
            }
            WriteToFile(report);
        }

        /// <summary>レベルアンロード時。テアダウン中に立った依頼を次の都市へ持ち越さない。</summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref _requested, 0);
            lock (_pendingGate) { _pendingReport = null; }
        }

        private static void WriteToFile(DiagnosticReport report)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Disaster + diagnostics");
                sb.AppendLine("generated at frame " + SimulationManager.instance.m_currentFrameIndex);
                sb.AppendLine();

                foreach (string line in DiagnosticFormatter.Format(report))
                {
                    sb.AppendLine(line);
                }

                string dir = LocaleLoader.ModDirectoryPath();
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warn("diagnostics dump skipped: mod directory not resolved");
                    return;
                }

                string path = Path.Combine(dir, FileName);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Log.Info("diagnostics written to " + path);
            }
            catch (Exception e)
            {
                Log.Error("diagnostics dump failed", e);
            }
        }
    }
}
