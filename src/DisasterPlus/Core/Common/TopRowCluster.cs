using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 画面最上段の**左から続く一団**がどこで終わるかだけを決める純算術。
    /// <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── なぜ要るのか（2026-08-22、実機で 2 回外した）─────────────────────
    ///
    /// 所有者の指示は最初からこうだった:
    ///
    /// > WF ボタン ＞ ！ボタン ＞ D＋ボタンの順に左上のところに並ぶように
    ///
    /// 1 回目は「最上段を**左端から**探す」にした ——
    /// **先に間に合った者がいちばん左を取る**ので、この MOD が 1 番だと
    /// 画面の左端に張り付いた（「まだ左すぎます」）。
    ///
    /// 2 回目は「最上段で**いちばん右の端**の右へ置く」にした ——
    /// CS の最上段には**右上にもバニラの UI がある**（設定のボタン等）ので、
    /// いちばん右の端はそちらになり、**画面の右端でバニラのボタンと重なった**。
    ///
    /// 正しいのは<b>「左から続いている一団の、右端」</b>である。
    /// 左端から歩き、<see cref="DefaultMaxGapPixels"/> より広い隙間で打ち切る ——
    /// 右上のバニラ UI との間には画面幅ぶんの隙間があるので、そこで必ず切れる。
    ///
    /// ★ **一団が 1 つも無ければ 0 を返す。** 呼び出し側はそのとき左端から始める
    ///   （MOD が 1 つも居ない環境では、左端が正しい）。
    /// </summary>
    public static class TopRowCluster
    {
        /// <summary>
        /// これより広く空いていたら**別の一団**とみなす（px）。
        ///
        /// 32〜44 px のボタンが 8 px 空けて並ぶのが左上の慣習なので、
        /// ボタン 2 個ぶんに満たない 96 px を境にする。
        /// 右上のバニラ UI との隙間は画面幅の半分以上あるので、確実に切れる。
        /// </summary>
        public const float DefaultMaxGapPixels = 96f;

        /// <summary>
        /// <paramref name="fromX"/> から右へ、隙間が <paramref name="maxGap"/> 以下の
        /// あいだ繋がっている一団の**右端**。1 つも繋がらなければ 0。
        ///
        /// <paramref name="starts"/> / <paramref name="ends"/> は帯に居る要素の左右端で、
        /// **並び順は問わない**（この中で選び出す。呼び出し側に整列を要求しない）。
        /// 有効なのは先頭 <paramref name="count"/> 件。
        ///
        /// 異常な値（NaN・∞・end &lt;= start）は**その 1 件だけ無視する** ——
        /// 1 件のせいで探索そのものを諦めない。
        /// </summary>
        public static float RightEdge(float[] starts, float[] ends, int count,
                                      float fromX, float maxGap)
        {
            if (starts == null || ends == null) return 0f;
            if (count > starts.Length) count = starts.Length;
            if (count > ends.Length) count = ends.Length;
            if (count <= 0) return 0f;

            if (IsBad(fromX)) fromX = 0f;
            if (IsBad(maxGap) || maxGap < 0f) maxGap = 0f;

            float edge = 0f;
            bool any = false;

            // 端から届く範囲を広げていく。1 周で 1 件以上伸びなくなったら終わり。
            // 件数は最上段に居る要素の数（数十）なので、O(n²) で十分速い。
            for (int pass = 0; pass < count; pass++)
            {
                bool grew = false;

                for (int i = 0; i < count; i++)
                {
                    float s = starts[i];
                    float e = ends[i];
                    if (IsBad(s) || IsBad(e) || e <= s) continue;

                    // 右端より左で終わっているものは、もう飲み込んでいる。
                    float reach = any ? edge : fromX;
                    if (e <= reach) continue;

                    // 届く範囲（reach + maxGap）より右で始まるなら、まだ別の一団。
                    if (s > reach + maxGap) continue;

                    edge = e;
                    any = true;
                    grew = true;
                }

                if (!grew) break;
            }

            return any ? edge : 0f;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
