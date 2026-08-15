namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドが Publish し main スレッドが <see cref="Latest"/> を読む。
    /// ①の <c>ForecastHub</c>・②の <see cref="EarthquakeHub"/> と同形
    /// （net35 に <c>System.Collections.Concurrent</c> は無いので素の lock 1 本）。
    /// <see cref="TyphoonSnapshot"/> は不変なので参照を渡すだけで安全。
    ///
    /// **T3 が逆向きの経路を足す**（パネルの「台風を発生させる／止める」を
    /// main → sim へ渡す <c>TyphoonRequest</c>）。**そのときも同じ <c>_gate</c> 1 本で
    /// 守ること。** 別のロックを足すと、2 本のロックの取得順という、この MOD が
    /// まだ一度も抱えていない種類の問題を作ることになる（<see cref="EarthquakeHub"/> の
    /// クラス doc が同じ判断を書いている）。
    /// </summary>
    public static class TyphoonHub
    {
        private static readonly object _gate = new object();
        private static TyphoonSnapshot _latest;

        public static void Publish(TyphoonSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>まだ publish されていなければ null。呼び出し側で判定すること。</summary>
        public static TyphoonSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// レベルロード／アンロード時。都市をまたいで状態を持ち越さない。
        ///
        /// **T3 が足す依頼（<c>_request</c>）もここで <c>None</c> に戻すこと。**
        /// 戻し忘れると、次の都市がロードされた瞬間に前の都市で押されたボタンが発火する。
        /// </summary>
        public static void Clear()
        {
            lock (_gate) { _latest = null; }
        }
    }
}
