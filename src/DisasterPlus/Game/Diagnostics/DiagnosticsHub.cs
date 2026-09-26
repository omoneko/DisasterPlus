using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Hands diagnostic snapshots over. The sim thread Publishes and the main thread reads
    /// Latest.
    /// The same snapshot-then-render as FireWhirlRegistry (net35 has no Concurrent
    /// collections, so a plain lock).
    ///
    /// DiagnosticReport is immutable, so passing the reference is all that is needed.
    /// </summary>
    public static class DiagnosticsHub
    {
        private static readonly object _gate = new object();
        private static DiagnosticReport _latest;
        private static bool _collectionEnabled;

        /// <summary>
        /// Whether to run collection. false when the overlay is closed and no dump has been
        /// requested, and the collection cost is zero then.
        /// </summary>
        public static bool CollectionEnabled
        {
            get { lock (_gate) { return _collectionEnabled; } }
            set { lock (_gate) { _collectionEnabled = value; } }
        }

        public static void Publish(DiagnosticReport report)
        {
            lock (_gate) { _latest = report; }
        }

        /// <summary>null if nothing has been published yet. The caller must check.</summary>
        public static DiagnosticReport Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                _collectionEnabled = false;
            }
        }
    }
}
