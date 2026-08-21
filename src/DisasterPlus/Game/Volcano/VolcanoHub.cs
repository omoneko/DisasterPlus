using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// main スレッドから sim スレッドへ渡す依頼の種類。
    ///
    /// **⑤はプレイヤーが地点を指してから始まる。** 自動発生は無い ——
    /// 地形の変更は不可逆で（§E-13）、セーブに焼き付き、⑤にもアンドゥが無いので、
    /// 「気づいたら都市の真ん中に山ができていた」は起こしてはいけない（設計書 §7.1）。
    /// </summary>
    public enum VolcanoRequest
    {
        None,

        /// <summary>地点の影響範囲を調べるだけ（**壊さない・上げない**）。T4。</summary>
        Survey,

        /// <summary>確認ダイアログで「はい」が押された。準備 → 隆起 → 噴火を始める。T4。</summary>
        Start,

        /// <summary>確認ダイアログを閉じた（調査結果を捨てる）。T4。</summary>
        Cancel,

        /// <summary>進行中の火山を止める。**既に変わった地形は戻らない。** T4。</summary>
        Stop,
    }

    /// <summary>
    /// 依頼 1 件。④と違い⑤の依頼は**座標を運ぶ**ので、enum ではなくこの値型を渡す。
    /// <see cref="Kind"/> が <see cref="VolcanoRequest.None"/> のとき
    /// <see cref="Point"/> は読まれない。
    /// </summary>
    public struct VolcanoRequestData
    {
        public readonly VolcanoRequest Kind;

        /// <summary>クリックされたワールド座標（<c>Survey</c> / <c>Start</c> のときだけ意味を持つ）。</summary>
        public readonly Vec3 Point;

        /// <summary>
        /// クリックした瞬間に**バニラのスライダーが指していた大きさの倍率**
        /// （<c>Survey</c> のときだけ意味を持つ）。1.0 が設定画面どおりのサイズで、
        /// 2.0 なら半径も最終高も 2 倍になる（<c>Core.Volcano.VolcanoSizeScale</c>）。
        ///
        /// ★ 実際に何メートルになるかは形態ごとの帯がさらにクランプし、
        ///   **確認の行がその結果をメートルで見せてから**でないと 1 つも壊れない。
        /// </summary>
        public readonly float SizeScale;

        public VolcanoRequestData(VolcanoRequest kind, Vec3 point, float sizeScale)
        {
            Kind = kind;
            Point = point;
            SizeScale = sizeScale;
        }

        /// <summary>地点も倍率も要らない依頼（<c>Start</c> / <c>Cancel</c> / <c>Stop</c>）。</summary>
        public static VolcanoRequestData Of(VolcanoRequest kind)
        {
            return new VolcanoRequestData(kind, new Vec3(0f, 0f, 0f), 1f);
        }

        /// <summary>「依頼なし」。<c>default(VolcanoRequestData)</c> と同じだが、意図を名乗る。</summary>
        public static VolcanoRequestData None
        {
            get { return Of(VolcanoRequest.None); }
        }
    }

    /// <summary>
    /// sim スレッドが Publish し main スレッドが <see cref="Latest"/> を読む。
    /// ①の <c>ForecastHub</c>・②の <c>EarthquakeHub</c>・④の <see cref="TyphoonHub"/> と同形
    /// （net35 に <c>System.Collections.Concurrent</c> は無いので素の lock 1 本）。
    /// <see cref="VolcanoSnapshot"/> は不変なので参照を渡すだけで安全。
    ///
    /// 逆向きの経路（配置ツールとパネルの依頼を main → sim へ渡す）も
    /// **同じ <c>_gate</c> 1 本で守っている。2 本目のロックを足さないこと** ——
    /// 2 本のロックの取得順という、この MOD がまだ一度も抱えていない種類の問題を
    /// 作ることになる（<see cref="TyphoonHub"/> のクラス doc が同じ判断を書いている）。
    /// </summary>
    public static class VolcanoHub
    {
        private static readonly object _gate = new object();
        private static VolcanoSnapshot _latest;
        private static VolcanoRequestData _request;

        public static void Publish(VolcanoSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>まだ publish されていなければ null。呼び出し側で判定すること。</summary>
        public static VolcanoSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// **main スレッドから。** 依頼を 1 個だけ積む。
        ///
        /// 直前の依頼がまだ sim に拾われていなければ**上書きする**（深さ 1 の後勝ち）。
        /// 押した順ではなく「最後に押したほうが勝つ」で正しい —— 調査と中止を
        /// 続けて押した人が望んでいるのは後者だけである。
        /// </summary>
        public static void Request(VolcanoRequestData request)
        {
            lock (_gate) { _request = request; }
        }

        /// <summary>
        /// **main スレッドから（表示専用）。** まだ sim に拾われていない依頼。
        ///
        /// パネルが「依頼中」を出すためだけに在る。押してから実際に反応するまでは
        /// **設計上 1 tick かかる**ので、この口が無いと押した直後のパネルは
        /// 前の状態のままになり、**プレイヤーはもう一度押す**。
        ///
        /// <see cref="TakeRequest"/> と違って**取り出さない**。ここで消費すると
        /// パネルを開いているかどうかで sim の挙動が変わる。
        /// </summary>
        public static VolcanoRequestData PendingRequest
        {
            get { lock (_gate) { return _request; } }
        }

        /// <summary>
        /// **sim スレッドから。** 積まれている依頼を取り出し、
        /// <see cref="VolcanoRequest.None"/> に戻す。**1 tick に 1 回だけ呼ぶこと**
        /// （2 回呼ぶと 2 回目が必ず None になり、呼び出し順に依存した取りこぼしを作る）。
        /// </summary>
        public static VolcanoRequestData TakeRequest()
        {
            lock (_gate)
            {
                var r = _request;
                _request = VolcanoRequestData.None;
                return r;
            }
        }

        /// <summary>
        /// レベルロード／アンロード時。都市をまたいで状態を持ち越さない。
        /// </summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                // ★ 戻し忘れると、次の都市がロードされた瞬間に
                //    前の都市で指された地点に山が生え始める。**地形は戻せない。**
                _request = VolcanoRequestData.None;
            }
        }
    }
}
