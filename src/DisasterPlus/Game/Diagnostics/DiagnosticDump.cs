using System;
using System.IO;
using System.Text;
using System.Threading;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Writes the state out to a single file. It uses the same DiagnosticFormatter as the
    /// overlay, so what is on screen and what is in the file never disagree.
    ///
    /// The Ctrl+hotkey is pressed on the main thread (MonoBehaviour.Update). But
    /// IDisasterFeature.WriteDiagnostics (and hence FeatureHost.BuildReport) is contracted as
    /// sim-thread only. Calling BuildReport() directly from the main thread here could break
    /// a future feature implementation (② to ⑤) written in good faith on that contract,
    /// without any lock, so the write is split into three stages: "main → request,
    /// sim → assemble, main → write the file".
    /// </summary>
    public static class DiagnosticDump
    {
        public const string FileName = "DisasterPlus-diagnostics.txt";

        // Raised by the main thread and lowered by the sim thread. It is a one-bit flag, so
        // Interlocked is lighter than a lock and carries no risk of deadlock.
        private static int _requested;

        // The hand-over point for the report the sim thread assembled, until the main thread
        // writes it out. DiagnosticReport itself is immutable, so passing the reference is all
        // that is needed.
        private static readonly object _pendingGate = new object();
        private static DiagnosticReport _pendingReport;

        /// <summary>Call from the main thread. One dump's worth is assembled on the next sim tick.</summary>
        public static void RequestDump()
        {
            Interlocked.Exchange(ref _requested, 1);
        }

        /// <summary>
        /// Call from the sim thread every tick. Returns true if a request is raised, and
        /// lowers the flag at the same time (one request never assembles twice).
        /// </summary>
        public static bool ConsumeRequest()
        {
            return Interlocked.Exchange(ref _requested, 0) == 1;
        }

        /// <summary>
        /// Call from the sim thread. It only parks the assembled report for the main thread to
        /// write; no file I/O whatsoever happens here (so as not to stall the sim tick).
        /// </summary>
        public static void SubmitReport(DiagnosticReport report)
        {
            lock (_pendingGate) { _pendingReport = report; }
        }

        /// <summary>
        /// Call from the main thread every frame. If a report is waiting, it is written to the
        /// file exactly once. With nothing waiting it takes the lock and returns immediately,
        /// so the per-frame cost is negligible.
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

        /// <summary>On level unload. Do not carry a request raised during teardown over to the next city.</summary>
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
