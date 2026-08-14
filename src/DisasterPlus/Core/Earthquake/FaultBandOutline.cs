using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <see cref="FaultBand"/> を地図に描くための、断層に沿った**半幅の折れ線**。
    ///
    /// ── なぜ「式で描く」のではなく「述語を測る」のか ─────────────────
    ///
    /// 帯の縁は閉じた式で書けない。到達範囲は
    /// <c>u ∈ [-0.4L, 0.4L]</c> にわたる「半径 <c>w(u)</c> の円盤（中心は蛇行で
    /// ±0.5·w(u) 動く）」の**和集合**で、<c>w</c> が u の 2 次関数なので
    /// <c>FaultBand.Gap</c> は 4 次の区分多項式になり単峰ですらない
    /// （<see cref="FaultBand.Contains"/> の doc、全体レビュー C1 追記）。
    /// ここで「だいたいこの形」を別に組むと、**パネルが「帯の内側」と言っている
    /// 建物が、地図では帯の外に描かれる**という食い違いが起きる。
    ///
    /// だから輪郭は <see cref="FaultBand.Contains"/> **そのものを二分探索して**
    /// 測る。定義上、描く形と判定する形は一致する。
    ///
    /// ── なぜ毎フレーム測らないのか ───────────────────────────
    ///
    /// 1 回の <see cref="Rebuild"/> は <c>(Segments+1) × (1 + BisectionSteps)</c>
    /// ＝ 209 回の <c>Contains</c> 呼び出しになり、<c>Contains</c> 自身が最大 145 回の
    /// <c>Gap</c> 評価を行う。**描画経路では地震 1 個につき最大 1 回まで。**
    ///
    /// ── 「絶対に呼ぶな」から「1 回までなら可」へ（第 2 層レビュー M1）──────────
    ///
    /// この doc はもともと「描画経路では絶対に呼ばないこと」と書いていたが、
    /// <c>EarthquakeOverlay.DrawQuake</c> は実際に <c>OnPostRender</c> の中から
    /// <c>Rebuild</c> を呼んでいる（<c>Matches</c> で地震 1 個につき 1 回に絞ってある）。
    /// **どちらかが間違っているので、量を見積もってから doc の方を直した:**
    ///
    ///   209 × 145 ≒ 3 万回の <c>Gap</c> 評価。<c>Gap</c> は分岐 1 個と乗算数個の
    ///   float 演算で、**1 フレームに 1 回だけ**、地震が現れた最初のフレームに払う。
    ///   毎フレーム払えば桁違いに重いが、地震 1 個につき 1 回なら
    ///   <c>OnPostRender</c> の中でも見えない。
    ///
    /// パネル側の tick へ移す案は採らなかった。オーバーレイはパネルが閉じていても
    /// 出るので、パネルの tick に置くと**パネルを開いていないプレイヤーには帯が
    /// 永久に描かれない**。1 フレーム遅らせて描く案も、帯が 1 フレームだけ欠ける
    /// （＝「幾何が不明」と見分けが付かない）ので採らない。
    ///
    /// **したがって規約はこうである: <see cref="Matches"/> が false のときだけ呼ぶ。
    /// 毎フレーム呼ぶ経路をこの型に持たせない。**
    ///
    /// 1 回で済むのは、この折れ線が <see cref="FaultBand.Length"/> と
    /// <see cref="FaultBand.Width"/> **だけ**の関数だからである
    /// （沿走 / 直交の局所座標で持つので、震央の位置にも <c>m_angle</c> にも依存しない）。
    /// L と W は <c>m_crackLength/Width × (0.5 + intensity×0.005)</c> で、
    /// 1 つの地震のあいだ定数である。したがって地震ごとに 1 回測れば足りる
    /// （<see cref="Matches"/> がその判定）。
    ///
    /// 配列は生成時に 1 本だけ確保して以後使い回す。**Rebuild は確保しない。**
    /// </summary>
    public sealed class FaultBandOutline
    {
        /// <summary>
        /// 台形の枚数。サンプル点は <c>Segments + 1</c> 個。
        ///
        /// 帯は沿走方向に <c>w(u) = W(1-4t²)</c> の 2 次で細るので、10 枚の台形
        /// （＝ 2 次曲線の 10 分割の折れ線近似）で目視できる差は出ない。
        /// 枚数を増やすと描画コールがそのまま増える（<c>MaxDrawCalls</c> の内訳）。
        /// </summary>
        public const int Segments = 10;

        /// <summary>二分探索の回数。上限 1.5W からの相対誤差 2^-18（W=100 m で 0.6 mm）。</summary>
        private const int BisectionSteps = 18;

        private readonly float[] _halfWidths = new float[Segments + 1];

        /// <summary>測ったときの L。<see cref="Matches"/> のキー。</summary>
        public float Length { get; private set; }

        /// <summary>測ったときの W。<see cref="Matches"/> のキー。</summary>
        public float Width { get; private set; }

        /// <summary>
        /// 沿走方向の片側の広がり <c>0.4L + w(0.4)</c>。
        /// 円盤**中心**の上限 0.4L に、その位置での到達距離が足される
        /// （設計書 §3.1 の末尾。旧実装は両端をちょうど w ぶん取りこぼしていた）。
        /// </summary>
        public float AlongExtent { get; private set; }

        /// <summary>
        /// 幾何が確定していて、描いてよいか。
        ///
        /// **false のときは帯を 1 本も描かないこと。** プレハブの 4 値
        /// （<c>m_crackLength</c> / <c>m_crackWidth</c> ほか）は DLL に無く
        /// （IL 事実文書 §A-0）、実機で読めなければ帯の大きさは分からない。
        /// 「分からない」を「たぶんこのくらい」で描くと、地図の上では
        /// それが実測値と区別できなくなる。
        /// </summary>
        public bool Known { get; private set; }

        /// <summary>この折れ線が、その L / W について測ったものか。</summary>
        public bool Matches(float length, float width)
        {
            return Known && Length == length && Width == width;
        }

        /// <summary>
        /// 沿走位置（帯の中心からの距離、負も取る）。<c>i = 0</c> が一方の端。
        /// </summary>
        public float AlongAt(int sample)
        {
            if (!Known || sample < 0 || sample > Segments) return 0f;
            return -AlongExtent + 2f * AlongExtent * sample / Segments;
        }

        /// <summary>
        /// その沿走位置で、断層線から直交方向に円盤が届く最大距離。
        /// 中央では <c>1.5W</c>（到達 w ＋ 蛇行 0.5w）に一致する。
        /// </summary>
        public float HalfWidthAt(int sample)
        {
            if (!Known || sample < 0 || sample > Segments) return 0f;
            return _halfWidths[sample];
        }

        /// <summary>
        /// <b>地震 1 個につき最大 1 回</b>（クラス doc）。呼ぶ前に必ず
        /// <see cref="Matches"/> を見ること。描画経路（<c>OnPostRender</c>）から
        /// 呼んでよいのは、その 1 回だけだからである。
        /// </summary>
        public void Rebuild(FaultBand band)
        {
            Known = false;
            Length = 0f;
            Width = 0f;
            AlongExtent = 0f;
            for (int i = 0; i <= Segments; i++) _halfWidths[i] = 0f;

            if (!band.Known) return;

            float extent = FaultBand.MaxOffset * band.Length + band.PatchRadiusAt(FaultBand.MaxOffset);
            if (extent <= 0f || float.IsNaN(extent)) return;

            // どの円盤も W より太くならないので、直交方向の上限は 1.5W で足りる
            // （FaultBand.Contains の早い棄却と同じ値）。
            float ceiling = 1.5f * band.Width;

            for (int i = 0; i <= Segments; i++)
            {
                float u = -extent + 2f * extent * i / Segments;

                // 断層線の上（across = 0）にすら届かない位置なら、そこは帯ではない。
                if (!Reaches(band, u, 0f)) { _halfWidths[i] = 0f; continue; }

                // Contains は across について単調（Gap の第 2 項が max(0, across-0.5w)²
                // で非減少）なので、二分探索が使える。
                float lo = 0f;
                float hi = ceiling;
                if (Reaches(band, u, hi)) { _halfWidths[i] = hi; continue; }

                for (int k = 0; k < BisectionSteps; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Reaches(band, u, mid)) lo = mid; else hi = mid;
                }
                _halfWidths[i] = lo;
            }

            Length = band.Length;
            Width = band.Width;
            AlongExtent = extent;
            Known = true;
        }

        /// <summary>
        /// 局所座標（沿走 <paramref name="along"/> / 直交 <paramref name="across"/>）を
        /// ワールドへ戻して <see cref="FaultBand.Contains"/> に問う。
        ///
        /// 直交の基底は <c>(Direction.Z, -Direction.X)</c>。<c>Contains</c> の
        /// <c>across = dx·Dir.Z - dz·Dir.X</c> と符号まで一致する（それを外すと
        /// 蛇行の非対称を測り違える）。
        /// </summary>
        private static bool Reaches(FaultBand band, float along, float across)
        {
            var p = band.Centre
                    + band.Direction * along
                    + new Vec2(band.Direction.Z, -band.Direction.X) * across;
            return band.Contains(p);
        }
    }
}
