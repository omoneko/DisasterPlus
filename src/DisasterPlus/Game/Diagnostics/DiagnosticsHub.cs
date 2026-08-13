using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断スナップショットの受け渡し。sim スレッドが Publish し、main スレッドが Latest を読む。
    /// FireWhirlRegistry と同じ snapshot-then-render（net35 に Concurrent は無いので素の lock）。
    ///
    /// DiagnosticReport は不変なので、参照を渡すだけで安全。
    /// </summary>
    public static class DiagnosticsHub
    {
        private static readonly object _gate = new object();
        private static DiagnosticReport _latest;
        private static bool _collectionEnabled;

        /// <summary>
        /// 収集を走らせるか。オーバーレイが閉じていてダンプ要求も無ければ false で、
        /// そのとき収集コストはゼロになる。
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

        /// <summary>まだ一度も publish されていなければ null。呼び出し側で判定すること。</summary>
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
