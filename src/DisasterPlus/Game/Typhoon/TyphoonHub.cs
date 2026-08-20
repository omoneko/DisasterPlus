using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// main スレッドから sim スレッドへ渡す唯一の依頼。
    ///
    /// **台風はプレイヤーが明示的に起こす**（計画 §3.1）。バニラの雷雨災害を
    /// 見つけて台風に昇格させる案は却下してある —— ④が雨を上げると
    /// <c>m_currentRain &gt; 0.8</c> の条件でゲーム自身が雷雨災害を作りうるので
    /// （IL 事実文書 §A-3）、昇格方式は「④の雨がゲームに嵐を作らせ、それを④が
    /// 台風に昇格させ、その台風がまた雨を上げる」自己増殖になり、しかも
    /// プレイヤーからは原因が MOD だと分からない。
    /// </summary>
    public enum TyphoonRequest
    {
        None,
        Start,
        Stop,
    }

    /// <summary>
    /// 依頼 1 件。**⑤の <see cref="VolcanoRequestData"/> と同じ形にしてある。**
    ///
    /// ★★ ④のタイルはバニラの災害ボタンと同じように**配置カーソルを構える**ように
    ///    なったので、依頼は<b>座標を運ぶ</b>（<see cref="TyphoonPlacementTool"/>）。
    ///    enum 1 本のままにすると、sim 側が「どこに」を自分で発明することになる。
    ///
    /// <see cref="Kind"/> が <see cref="TyphoonRequest.None"/> ／
    /// <see cref="TyphoonRequest.Stop"/> のとき <see cref="Point"/> は読まれない。
    /// </summary>
    public struct TyphoonRequestData
    {
        public readonly TyphoonRequest Kind;

        /// <summary>クリックされたワールド座標（<c>Start</c> のときだけ意味を持つ）。</summary>
        public readonly Vec3 Point;

        public TyphoonRequestData(TyphoonRequest kind, Vec3 point)
        {
            Kind = kind;
            Point = point;
        }

        /// <summary>「依頼なし」。<c>default(TyphoonRequestData)</c> と同じだが、意図を名乗る。</summary>
        public static TyphoonRequestData None
        {
            get { return new TyphoonRequestData(TyphoonRequest.None, new Vec3(0f, 0f, 0f)); }
        }
    }

    /// <summary>
    /// sim スレッドが Publish し main スレッドが <see cref="Latest"/> を読む。
    /// ①の <c>ForecastHub</c>・②の <see cref="EarthquakeHub"/> と同形
    /// （net35 に <c>System.Collections.Concurrent</c> は無いので素の lock 1 本）。
    /// <see cref="TyphoonSnapshot"/> は不変なので参照を渡すだけで安全。
    ///
    /// **T3 が逆向きの経路を足した**（パネルの「台風を発生させる／止める」を
    /// main → sim へ渡す <see cref="TyphoonRequest"/>）。**同じ <c>_gate</c> 1 本で
    /// 守っている。** 別のロックを足さないこと —— 2 本のロックの取得順という、
    /// この MOD がまだ一度も抱えていない種類の問題を作ることになる
    /// （<see cref="EarthquakeHub"/> のクラス doc が同じ判断を書いている）。
    /// </summary>
    public static class TyphoonHub
    {
        private static readonly object _gate = new object();
        private static TyphoonSnapshot _latest;
        private static TyphoonRequestData _request;

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
        /// **main スレッドから。** ボタンの押下を 1 個だけ積む。
        ///
        /// 直前の依頼がまだ sim に拾われていなければ**上書きする**。押した順ではなく
        /// 「最後に押したほうが勝つ」で正しい —— 発生と停止を続けて押した人が
        /// 望んでいるのは後者だけである。
        /// </summary>
        public static void Request(TyphoonRequestData request)
        {
            lock (_gate) { _request = request; }
        }

        /// <summary>
        /// **main スレッドから（表示専用）。** まだ sim に拾われていない依頼。
        ///
        /// パネルが「依頼中」を出すためだけに在る。押してから実際に台風が現れるまでは
        /// **設計上 1 tick かかる**（実行は次の sim tick の <see cref="TyphoonController"/>）
        /// ので、この口が無いとボタンを押した直後のパネルは「台風は発生していません」の
        /// ままになり、**プレイヤーはもう一度押す**。
        ///
        /// <see cref="TakeRequest"/> と違って**取り出さない**。ここで消費すると
        /// パネルを開いているかどうかで sim の挙動が変わる。
        /// </summary>
        public static TyphoonRequestData PendingRequest
        {
            get { lock (_gate) { return _request; } }
        }

        /// <summary>
        /// **sim スレッドから。** 積まれている依頼を取り出し、<see cref="TyphoonRequest.None"/>
        /// に戻す。**1 tick に 1 回だけ呼ぶこと**（2 回呼ぶと 2 回目が必ず None になり、
        /// 呼び出し順に依存した取りこぼしを作る）。
        /// </summary>
        public static TyphoonRequestData TakeRequest()
        {
            lock (_gate)
            {
                var r = _request;
                _request = TyphoonRequestData.None;
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
                //    前の都市で押されたボタンが発火する。
                _request = TyphoonRequestData.None;
            }
        }
    }
}
