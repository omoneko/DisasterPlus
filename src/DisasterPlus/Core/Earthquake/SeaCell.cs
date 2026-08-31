namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>そのセルは「外洋」か。</b>エンジンに触らない層。
    ///
    /// ── なぜ Core に出したのか ────────────────────────────────────
    ///
    /// この 1 つの式を<b>3 日で 3 回書き直した</b>。そのたびに、片方しか見ていない
    /// せいで別の地形を取り違えていた（2026-08-31、第 5・6 回の相互検証）:
    ///
    /// <list type="bullet">
    /// <item><b>海底の高さだけ</b>見た版 …… 堤防で囲まれた干拓地やクレーターを
    ///   「海」と答えた。海面より低いまま<b>乾いている</b>のに。そこへ半径 3.8 km の
    ///   水源を置くと、引きの円は 160 m しかないので<b>戻せない水が永久に残る</b>。</item>
    /// <item><b>水柱だけ</b>見た版 …… 川・高い湖・<b>前の津波で冠水した街</b>を
    ///   「海」と答えた。2 本目の津波が円をその上に広げ、目標より低い陸を
    ///   いきなり満たす —— 同じ失敗に裏口から入る。</item>
    /// <item><b>水面の高さ</b>で川を落とす版 …… 高潮の最中に<b>外洋を丸ごと</b>
    ///   「海ではない」と答えた（水面が上がるので）。プレイヤーには
    ///   「2,304 m 以内に深い海がありません」と出る —— 見るからに海なのに。</item>
    /// </list>
    ///
    /// ★★ **だから式をここに固定して、テストで縛る。**
    ///   Game 層は配列から 3 つの数を拾ってここへ渡すだけにする。
    /// </summary>
    public static class SeaCell
    {
        /// <summary>
        /// 外洋か。
        ///
        /// <code>
        /// 水柱 &gt;= 最小水深      … 本当に水がある（乾いた窪地を落とす）
        /// かつ 海底 &lt;= 海面 - 最小水深 … 本当に海である（川・冠水した陸を落とす）
        /// </code>
        ///
        /// ★ 海底は動かないので、<b>高潮の最中でも海は海のまま</b>である。
        /// </summary>
        /// <param name="terrainUnits">海底の標高（1/64 m）。</param>
        /// <param name="columnUnits">その上に乗っている水の厚み（1/64 m）。</param>
        /// <param name="seaUnits">平常の海面（1/64 m）。</param>
        /// <param name="minDepthUnits">要求する最小水深（1/64 m）。</param>
        public static bool IsOpenSea(int terrainUnits, int columnUnits,
                                     int seaUnits, int minDepthUnits)
        {
            if (columnUnits < minDepthUnits) return false;
            return terrainUnits <= seaUnits - minDepthUnits;
        }
    }
}
