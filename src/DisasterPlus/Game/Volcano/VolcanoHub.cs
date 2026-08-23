using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// main スレッドから sim スレッドへ渡す依頼の種類。
    ///
    /// **⑤はプレイヤーが地点を指してから始まる。** 自動発生は無い ——
    /// 地形の変更は不可逆で（§E-13）、セーブに焼き付き、⑤にもアンドゥが無いので、
    /// 「気づいたら都市の真ん中に山ができていた」は起こしてはいけない（設計書 §7.1）。
    ///
    /// ★★ <b>確認の窓（<c>Start</c> / <c>Cancel</c>）は 2026-08-21 に撤去した。</b>
    /// 所有者の指示は「ほかの災害と同じように、タイル → スライダー → 地図をクリックで
    /// 起きる」である。したがって main が積む依頼は
    /// <see cref="Place"/> と <see cref="Stop"/> の 2 つしか無い。
    /// **「はい」を待つ依頼を足し直さないこと。**
    /// </summary>
    public enum VolcanoRequest
    {
        None,

        /// <summary>
        /// この地点に火山を作る。**押した時点で決まりである**（確認は無い）。
        ///
        /// sim 側はこの 1 件で「調査 → 準備の着手」まで進む
        /// （<c>VolcanoState.HandlePlace</c>）。調査そのものは残っている ——
        /// 建物と道路のグリッドは sim スレッドが所有しているので、
        /// **影響範囲は main では数えられない**からである。
        /// </summary>
        Place,

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

        /// <summary>クリックされたワールド座標（<see cref="VolcanoRequest.Place"/> のときだけ意味を持つ）。</summary>
        public readonly Vec3 Point;

        /// <summary>
        /// クリックした瞬間に**バニラのスライダーが指していた大きさの倍率**
        /// （<see cref="VolcanoRequest.Place"/> のときだけ意味を持つ）。1.0 が設定画面
        /// どおりのサイズで、2.0 なら半径も最終高も 2 倍になる
        /// （<c>Core.Volcano.VolcanoSizeScale</c>）。
        ///
        /// ★ 実際に何メートルになるかは形態ごとの帯がさらにクランプする。
        ///   クランプ後の実寸は火山タブの調査の行と診断ダンプが名乗る。
        /// </summary>
        public readonly float SizeScale;

        /// <summary>
        /// クリックした瞬間のスライダーの**生値**（0〜255。表示はこの 1/10）。
        ///
        /// ★★ <b><see cref="SizeScale"/> から割り戻さないこと。</b> あちらは
        ///   <c>VolcanoSizeScale</c> が帯へクランプした後の値なので、上端では
        ///   複数の生値が同じ倍率に潰れている。**「スライダーがいちばん上か」は
        ///   生値でしか判定できない**（<c>SuperEruption.IsSuper</c>）。
        /// </summary>
        public readonly int SizeRaw;

        public VolcanoRequestData(VolcanoRequest kind, Vec3 point, float sizeScale, int sizeRaw)
        {
            Kind = kind;
            Point = point;
            SizeScale = sizeScale;
            SizeRaw = sizeRaw;
        }

        /// <summary>地点も倍率も要らない依頼（<see cref="VolcanoRequest.Stop"/>）。</summary>
        public static VolcanoRequestData Of(VolcanoRequest kind)
        {
            return new VolcanoRequestData(kind, new Vec3(0f, 0f, 0f), 1f,
                                          DisasterPlus.Core.Volcano.VolcanoSizeScale.AnchorRaw);
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
        /// 押した順ではなく「最後に押したほうが勝つ」で正しい —— 設置と中止を
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
