namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドが Publish し main スレッドが Latest を読む。
    /// DiagnosticsHub / FireWhirlRegistry と同じ形（net35 に Concurrent は無いので素の lock）。
    /// WeatherSnapshot は不変なので参照を渡すだけで安全。
    /// </summary>
    public static class ForecastHub
    {
        private static readonly object _gate = new object();
        private static WeatherSnapshot _latest;

        public static void Publish(WeatherSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>まだ publish されていなければ null。呼び出し側で判定すること。</summary>
        public static WeatherSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>レベルアンロード時。都市をまたいで状態を持ち越さない。</summary>
        public static void Clear()
        {
            lock (_gate) { _latest = null; }
        }
    }
}
