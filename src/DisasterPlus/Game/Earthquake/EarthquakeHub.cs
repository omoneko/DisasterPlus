using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// sim スレッドが Publish し main スレッドが Latest を読む。
    /// ①の <see cref="ForecastHub"/> と同形（net35 に Concurrent は無いので素の lock 1 本）。
    /// <see cref="EarthquakeSnapshot"/> は不変なので参照を渡すだけで安全。
    ///
    /// **Task 5 で逆向きの経路が 1 本増えた**（カーソル座標を main → sim へ渡す）。
    /// 両方向とも**同じ <c>_gate</c> 1 本**で守ること。別のロックを足すと、
    /// 2 本のロックの取得順という、この MOD がまだ一度も抱えていない種類の問題を
    /// 作ることになる。中身は Vec3 と bool だけなので、1 本で競合しない。
    /// </summary>
    public static class EarthquakeHub
    {
        private static readonly object _gate = new object();
        private static EarthquakeSnapshot _latest;

        private static Vec3 _cursor;
        private static bool _cursorValid;

        public static void Publish(EarthquakeSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>まだ publish されていなければ null。呼び出し側で判定すること。</summary>
        public static EarthquakeSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// **main スレッドから。** カーソル直下の地形座標を sim 側へ渡す。
        ///
        /// カーソル座標は <c>Input.mousePosition</c> と <c>Camera.main</c> から来るので
        /// main スレッドでしか取れない。一方その下の建物を調べるには
        /// <c>BuildingManager</c> のバッファ（sim スレッド所有）が要る。
        /// この 1 本がその橋渡しで、渡す側は座標だけを渡す。
        ///
        /// パネルが閉じている・カーソルが地形の上に無いときは <c>valid = false</c> を
        /// 渡すこと。渡さないと、sim 側は最後に見た座標を永久に調べ続ける。
        /// </summary>
        public static void PublishCursor(Vec3 pos, bool valid)
        {
            lock (_gate) { _cursor = pos; _cursorValid = valid; }
        }

        /// <summary>**sim スレッドから。** 最後に publish された座標を読む。</summary>
        public static bool TakeCursor(out Vec3 pos)
        {
            lock (_gate) { pos = _cursor; return _cursorValid; }
        }

        /// <summary>レベルロード／アンロード時。都市をまたいで状態を持ち越さない。</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                _cursor = new Vec3(0f, 0f, 0f);
                // ★ これを戻し忘れると、次の都市が前の都市の座標を 1 tick ぶん調べる。
                _cursorValid = false;
            }
        }
    }
}
