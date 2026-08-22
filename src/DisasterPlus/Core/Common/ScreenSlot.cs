namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 「画面の中に置く」という判断だけを持つ純算術。<b>Core なのでエンジンには
    /// 一切触らない</b>（<c>UnityEngine.Rect</c> も <c>Mathf</c> も使わない）。
    ///
    /// ── なぜ Core に切り出したのか ─────────────────────────────
    ///
    /// <c>Game/UI/FreeSlotFinder</c> は「空いている場所を下へ探す」だけを書いており、
    /// **その候補が画面の中かどうかを一度も見ていなかった。** 実機の
    /// output_log には
    /// <c>Disaster + info button installed at (8,1094)</c> と残っている ——
    /// 高さ 1080 の画面で y = 1094、つまり探索は下端を歩いて画面の外へ出て、
    /// そこを「空いている」と正しく判定していた。**空いていたのは画面の外だから**である。
    ///
    /// 以前はこの逆（4 個のボタンが同じ座標に積み上がる）で壊れていた。
    /// 重なるのと見えないのは<b>どちらも使えない</b>が、重なるほうがまだ押せる。
    /// その優先順位を、テストの掛かる形でここに置く。
    /// </summary>
    public static class ScreenSlot
    {
        /// <summary>
        /// 幅・高さ <paramref name="size"/> の矩形を <paramref name="pos"/> に置いたとき、
        /// <c>[0, extent]</c> に完全に収まるか。
        ///
        /// <paramref name="extent"/> が 0 以下（＝画面サイズが読めなかった）なら
        /// <b>true を返す</b> —— 読めない寸法で「画面外」と決めつけると、
        /// この関数のせいでボタンが 1 個も置けなくなる。読めないことは
        /// 呼び出し側が <see cref="IsUsableExtent"/> で先に見分ける。
        /// </summary>
        public static bool FitsWithin(float pos, float size, float extent)
        {
            if (!IsUsableExtent(extent)) return true;
            if (size <= 0f) return pos >= 0f && pos <= extent;
            return pos >= 0f && pos + size <= extent;
        }

        /// <summary>画面サイズとして意味のある値か（正で、NaN でも無限でもない）。</summary>
        public static bool IsUsableExtent(float extent)
        {
            return extent > 0f && !float.IsNaN(extent) && !float.IsInfinity(extent);
        }

        /// <summary>
        /// <paramref name="pos"/> を <c>[0, extent - size]</c> へ丸める。
        ///
        /// **矩形が画面より大きいときは 0 を返す**（左上を優先する）——
        /// 負の位置に置くと、押せる部分がいちばん少なくなる。
        /// <paramref name="extent"/> が読めないときは <paramref name="pos"/> をそのまま返す。
        /// </summary>
        public static float ClampInto(float pos, float size, float extent)
        {
            if (!IsUsableExtent(extent)) return pos;
            if (float.IsNaN(pos) || float.IsInfinity(pos)) return 0f;

            float last = extent - (size > 0f ? size : 0f);
            if (last <= 0f) return 0f;
            if (pos < 0f) return 0f;
            if (pos > last) return last;
            return pos;
        }

        /// <summary>
        /// <paramref name="startX"/> / <paramref name="startY"/> から
        /// <c>(stepX, stepY)</c> ずつ進むとき、**両軸とも画面に収まったまま検査できる
        /// 候補の数**。<see cref="CandidatesInside(float,float,float,float,int)"/> の
        /// 2 軸版で、あちらは <c>stepX = 0</c> の場合にあたる。
        ///
        /// ── なぜ横向きが要るのか（2026-08-22、所有者の依頼）───────────────
        ///
        /// > D＋ボタンが左サイドメニューと重なる位置にあるので、バニラのサイドメニューを
        /// > 操作する際に邪魔になります。CSWARFRONT ボタンや SIREN Alert ボタンと
        /// > 同じ高さで並んで表示されるようにしてください
        ///
        /// あの 2 つは**画面最上段の横 1 列**に居る。縦にしか進めない探索では
        /// その列に並べない —— 1 歩目で塞がっていたら、次の候補はもう下の段である。
        ///
        /// 両軸のどちらかが 0 以下でも、もう一方が正なら探索は進む。
        /// **両方 0 以下なら候補は 1 つ**（同じ点を検査し続けても探索にならない）。
        /// </summary>
        public static int CandidatesInside(float startX, float startY,
                                           float sizeX, float sizeY,
                                           float stepX, float stepY,
                                           float extentX, float extentY,
                                           int maxTries)
        {
            if (maxTries <= 0) return 0;
            if (!FitsWithin(startX, sizeX, extentX)) return 0;
            if (!FitsWithin(startY, sizeY, extentY)) return 0;

            bool movesX = stepX > 0f;
            bool movesY = stepY > 0f;
            if (!movesX && !movesY) return 1;

            int alongX = movesX
                ? StepsInside(startX, sizeX, stepX, extentX, maxTries)
                : maxTries;
            int alongY = movesY
                ? StepsInside(startY, sizeY, stepY, extentY, maxTries)
                : maxTries;

            int count = alongX < alongY ? alongX : alongY;
            return count >= maxTries ? maxTries : count;
        }

        /// <summary>
        /// 1 軸ぶん。<paramref name="extent"/> が読めなければ <paramref name="maxTries"/>
        /// （＝この軸は制限しない）。
        /// </summary>
        private static int StepsInside(float start, float size, float step,
                                       float extent, int maxTries)
        {
            if (!IsUsableExtent(extent)) return maxTries;

            float last = extent - (size > 0f ? size : 0f);
            float room = last - start;
            if (room < 0f) return 0;

            long steps = (long)(room / step);
            if (steps < 0L) return 0;

            long count = steps + 1L;
            return count >= maxTries ? maxTries : (int)count;
        }

        /// <summary>
        /// <paramref name="startY"/> から <paramref name="stepY"/> ずつ下へ進むとき、
        /// **画面に収まったまま検査できる候補の数**。
        ///
        /// <paramref name="maxTries"/> が上限で、返るのは常に <c>[0, maxTries]</c>。
        /// 0 が返るのは「最初の候補すら画面に入らない」ときで、そのときは
        /// 探索そのものが無意味である（下へ進めばもっと外れる）。
        ///
        /// <paramref name="stepY"/> が 0 以下なら候補は 1 つしかない
        /// （同じ点を検査し続けても探索にならない）。
        /// </summary>
        public static int CandidatesInside(float startY, float sizeY, float stepY,
                                           float extentY, int maxTries)
        {
            if (maxTries <= 0) return 0;
            if (!FitsWithin(startY, sizeY, extentY)) return 0;
            if (!IsUsableExtent(extentY)) return maxTries;
            if (!(stepY > 0f)) return 1;

            float last = extentY - (sizeY > 0f ? sizeY : 0f);
            // startY は上で収まっていることが確かめてある。あと何歩ぶん降りられるか。
            float room = last - startY;
            if (room < 0f) return 0;

            long steps = (long)(room / stepY);
            if (steps < 0L) return 0;

            long count = steps + 1L;
            return count >= maxTries ? maxTries : (int)count;
        }
    }
}
