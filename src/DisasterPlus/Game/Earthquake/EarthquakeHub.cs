namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドが Publish し main スレッドが Latest を読む。
    /// ①の <see cref="ForecastHub"/> と同形（net35 に Concurrent は無いので素の lock 1 本）。
    /// <see cref="EarthquakeSnapshot"/> は不変なので参照を渡すだけで安全。
    /// </summary>
    public static class EarthquakeHub
    {
        private static readonly object _gate = new object();
        private static EarthquakeSnapshot _latest;

        public static void Publish(EarthquakeSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>まだ publish されていなければ null。呼び出し側で判定すること。</summary>
        public static EarthquakeSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>レベルロード／アンロード時。都市をまたいで状態を持ち越さない。</summary>
        public static void Clear()
        {
            lock (_gate) { _latest = null; }
        }
    }
}
