using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>台風の下で木を激しく揺らす。</b>**描画スレッドから毎木呼ばれる。**
    ///
    /// ── なぜ格子をいじる手では足りなかったのか（2026-09-02）──────────────
    ///
    /// 木の揺れは <c>TreeInstance.RenderInstance</c> が
    ///
    /// <code>
    /// color.a = WeatherManager.GetWindSpeed(position);
    /// materialBlock.SetColor(TreeManager.ID_Color, color);
    /// </code>
    ///
    /// として<b>色のアルファに載せた風速</b>で、木のシェーダはそれを揺れ量に使う。
    /// そして <c>GetWindSpeed</c> の中身は
    ///
    /// <code>
    /// exposure = pos.y - m_windGrid[cell].m_totalHeight / 64
    /// return Mathf.Clamp(exposure * 0.02 + 1, 0, 2)
    /// </code>
    ///
    /// ★★ **末尾の <c>Clamp(…, 0, 2)</c> が全てだった。**
    ///   最初は <c>m_totalHeight</c> を下げて <c>exposure</c> を稼いだが、
    ///   それでは<b>どれだけ下げても 2.0 で頭打ち</b>になる ——
    ///   平常が 1.0 なので、<b>上限まで行っても 2 倍にしかならない</b>。
    ///   「もっと激しく」には足りなかった。
    ///
    /// ── ここを後置きで越える ────────────────────────────────────
    ///
    /// <c>GetWindSpeed</c> は public なので、Harmony の Postfix が
    /// <b>クランプの外側</b>で結果を掛けられる。利点は 3 つ:
    ///
    /// <list type="bullet">
    /// <item><b>上限が無い。</b>2.0 の壁の外で掛けるので、いくらでも強くできる。</item>
    /// <item><b>セーブに何も残らない。</b><c>m_windGrid</c> を 1 バイトも書かない ——
    ///   あれは <c>WeatherManager+Data.Serialize</c> が保存する配列で、
    ///   戻し損ねると都市に狂った遮蔽図が残る。触らなければその危険は<b>消える</b>。</item>
    /// <item><b>風力発電に響かない。</b>風車は <c>SampleWindSpeed</c> という
    ///   別のメソッドを通る。格子をいじる手では、そちらまで一緒に強くなっていた。</item>
    /// </list>
    ///
    /// ── 速さの制約 ────────────────────────────────────────────
    ///
    /// ★★ ここは<b>描画される木 1 本につき毎フレーム 1 回</b>呼ばれる。
    ///   何万回にもなるので、<b>確保も、Singleton の解決も、ロックもしない</b>。
    ///   見るのは静的な float 4 個と距離の二乗だけである。
    ///   台風が居ないときは <c>_active</c> の 1 回の読みで即座に戻る。
    ///
    /// ★ 状態は sim スレッドが <see cref="SetStorm"/> で書き、描画スレッドが読む。
    ///   <c>volatile</c> にしてあるが、**古い値を 1 フレーム読んでも何も壊れない**
    ///   （揺れの量が 1 フレーム前のものになるだけ）ので、錠は要らない。
    /// </summary>
    [HarmonyPatch(typeof(WeatherManager), "GetWindSpeed", new[] { typeof(Vector3) })]
    public static class TyphoonTreeWindPatch
    {
        private static volatile bool _active;
        private static float _centreX;
        private static float _centreZ;
        private static float _radiusSquared;
        private static float _gain;

        /// <summary>
        /// いま掛けている最大の倍率（診断）。1 なら効いていない。
        /// </summary>
        public static float Gain { get { return _active ? _gain : 1f; } }

        /// <summary>
        /// 台風の位置と強さを教える。**sim スレッド。** 毎 tick 呼んでよい。
        /// </summary>
        /// <param name="gain">
        /// 中心での倍率。1 で素通し。<b>2.0 の壁の外で掛かる</b>ので、
        /// 3 なら平常の 3 倍まで揺れる。
        /// </param>
        public static void SetStorm(float centreX, float centreZ, float radiusMetres,
                                    float gain)
        {
            if (radiusMetres <= 0f || gain <= 1f) { Clear(); return; }

            _centreX = centreX;
            _centreZ = centreZ;
            _radiusSquared = radiusMetres * radiusMetres;
            _gain = gain;
            _active = true;
        }

        /// <summary>台風が終わった／都市を出た。**必ず呼ぶ。**</summary>
        public static void Clear()
        {
            _active = false;
            _gain = 1f;
        }

        /// <summary>
        /// <c>WeatherManager.GetWindSpeed(Vector3)</c> の後置き。
        /// **クランプの外側**で倍率を掛ける（クラス doc の ★★）。
        /// </summary>
        public static void Postfix(Vector3 position, ref float __result)
        {
            if (!_active) return;

            float dx = position.x - _centreX;
            float dz = position.z - _centreZ;
            float distanceSquared = dx * dx + dz * dz;

            if (distanceSquared >= _radiusSquared) return;

            // ★ 中心で最大、縁で 1 倍へ落とす。境目で揺れが跳ばないように。
            float t = 1f - distanceSquared / _radiusSquared;
            __result *= 1f + (_gain - 1f) * t;
        }
    }
}
