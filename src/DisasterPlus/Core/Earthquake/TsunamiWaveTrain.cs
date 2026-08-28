using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>海溝型地震の津波。</b>震源を中心に<b>2〜3 本の波が続けて外へ広がる</b>。
    /// **エンジン非依存の純関数だけ。**
    ///
    /// ── 所有者の指示（2026-08-25）─────────────────────────────────
    ///
    /// &gt; 海溝型地震に伴う津波について、DLC の津波を使うのをやめましょう。
    /// &gt; 代わりに海溝型地震の震源地付近を中心とした領域で一定時間持続的な
    /// &gt; 海面上昇（震源地を中心に２-3 個の連続する山状：実際の津波メカニズムで）を
    /// &gt; 発生させてください。
    ///
    /// ── ★★ なぜ DLC の津波をやめるのか ─────────────────────────────
    ///
    /// <c>TsunamiAI</c> は<b>震源から波を出せない</b>。<c>FindSea</c> が候補にするのは
    /// <b>マップ外周のセルだけ</b>で（1080 &lt;&lt; 3 = 4320 個）、
    /// <c>m_targetPosition</c> は「どの外周区間を選ぶか」のヒントにしかならず、
    /// 開始時に原点セルの座標で<b>上書きされる</b>（IL_03D3–0426）。
    /// つまり実現できるのは「震源に最も近い<b>外周</b>から波が来る」までで、
    /// **沖合の震源から同心円状に広がる波は原理的に作れない。**
    ///
    /// ── 実際の津波のメカニズム（ここが写しているもの）──────────────────
    ///
    /// <list type="number">
    /// <item>海底が跳ね上がり、その真上の海面が<b>同じ形だけ持ち上がる</b></item>
    /// <item>それが重力で崩れ、<b>同心円状に外へ広がる</b></item>
    /// <item>1 本ではなく<b>数本の波列</b>になる。しかも
    ///   <b>第 1 波が最大とは限らない</b> —— 第 2・第 3 波のほうが高いことが多い</item>
    /// <item>浅い海に入ると遅くなって<b>高くなる</b>（浅水変形）</item>
    /// </list>
    ///
    /// <see cref="RiseAt"/> はこのうち 1〜3 を写す。4（浅水変形）は
    /// <b>ゲーム側が勝手にやってくれる</b> —— 持ち上げた水は
    /// <c>m_target</c> より高い地面には載らないので（<c>SimulateWater</c> IL_1E5E）、
    /// 谷筋と低い海岸だけが深く浸かる。
    ///
    /// ── 波の形 ────────────────────────────────────────────
    ///
    /// <code>
    /// rise(d, t) = A(t) × Σ_k  w_k × bell( (d − c·(t − t_k)) / width )
    /// </code>
    ///
    /// <list type="bullet">
    /// <item><c>c</c> … 波の速さ（<see cref="SpeedMetresPerSecond"/>）</item>
    /// <item><c>t_k</c> … k 本目が出る時刻。<see cref="CrestGapSeconds"/> 間隔</item>
    /// <item><c>w_k</c> … k 本目の高さの比（<see cref="CrestWeights"/>）。
    ///   **第 2 波がいちばん高い**</item>
    /// <item><c>A(t)</c> … 全体の包絡線。終わりに向かって静かに 0 へ戻す
    ///   —— 戻さないと水が引かない</item>
    /// </list>
    /// </summary>
    public static class TsunamiWaveTrain
    {
        /// <summary>波の本数。所有者の指示の「2-3 個」。</summary>
        public const int CrestCount = 3;

        // ── ★★ 「持ち上がったまま」の土台（2026-08-29）──────────────────────
        //
        // 実機報告:
        //
        //   > 波は減衰していくので、水源をすぐに除去してしまうとただの高潮に
        //   > なってしまっています。やはりバニラの津波のように海面全体が
        //   > 持ち上がるレベルじゃないと津波っぽさは出ない
        //
        // 3 本の細い山だけでは、通り過ぎたあとに水位が戻ってしまう。
        // **本物の津波は「海面が持ち上がったまましばらく戻らない」**（波長が
        // 数十 km あるので、岸では 1 本の波が何分も居座る）。
        //
        // そこで<b>山の下に台地を敷く</b>。震源のまわりの海がまるごと上がり、
        // その上を 3 本の山が走る。台地はゆっくりしか下がらない。

        /// <summary>台地の高さ（いちばん高い波に対する比）。</summary>
        public const float PlateauFraction = 0.62f;

        /// <summary>台地が全高で乗る半径（m）。ここまではまるごと持ち上がる。</summary>
        public const float PlateauRadiusMetres = 4200f;

        /// <summary>台地が 0 まで落ちる半径（m）。ここから外は上がらない。</summary>
        public const float PlateauEdgeMetres = 8200f;

        /// <summary>台地が立ち上がるまで（秒）。**一瞬で上げない**（水が壁になる）。</summary>
        public const float PlateauRampSeconds = 45f;

        /// <summary>
        /// 波が進む速さ（m/秒、ゲーム内時間）。
        /// **実際の津波（外洋で 200 m/s）ではない** —— そのままだとマップを
        /// 数十秒で通り抜けて何も起きない。ここは<b>この MOD が決めた演出値</b>である。
        /// </summary>
        /// <remarks>
        /// ★★ 140 -> 45 に落とした（2026-08-29、実機ログ）。140 m/s では
        ///   水源を置いた範囲（半径 3 km 前後）を<b>21 秒で通り抜ける</b>。
        ///   ゲームの水シミュは目標水位に向かって<b>徐々に</b>水を動かすので、
        ///   その間に水位が上がりきらない —— **波が来た形跡すら残らない。**
        ///   45 m/s なら 1 本が通るのに 67 秒かかり、水が乗る時間ができる。
        /// </remarks>
        public const float SpeedMetresPerSecond = 45f;

        /// <summary>波と波の間隔（秒）。</summary>
        /// <remarks>★ 波を遅くしたぶん、間隔も広げる（55 -> 90）。</remarks>
        public const float CrestGapSeconds = 90f;

        /// <summary>1 本の波の幅（m）。**狭いと海岸を素通りする。**</summary>
        /// <remarks>
        /// ★★ 900 -> 1800（同上）。狭い波は「一瞬水位が上がってすぐ下がる」に
        ///   なり、水シミュが追いつかない。津波の波長は実際にも数十 km あり、
        ///   <b>岸では「潮位が上がったまましばらく戻らない」</b>という見え方になる。
        /// </remarks>
        public const float CrestWidthMetres = 1800f;

        /// <summary>
        /// 波ごとの高さの比。**第 2 波がいちばん高い** ——
        /// 実際の津波でも第 1 波が最大とは限らず、避難を解いた人が
        /// 第 2 波にさらわれるのが典型的な被害である。
        /// </summary>
        public static readonly float[] CrestWeights = { 0.72f, 1f, 0.55f };

        /// <summary>波列ぜんぶが通り過ぎるまで（秒）。これを過ぎたら 0 を返す。</summary>
        /// <remarks>
        /// ★★ 420 -> 1080（2026-08-29、実機報告「水源の消失が速すぎる」）。
        ///   所有者の依頼は「<b>一定時間持続的な</b>海面上昇」である。
        ///   420 秒（ゲーム内 7 分）は速度 1 でも実時間 20 秒ほどで、
        ///   カメラを寄せる前に終わっていた。
        /// </remarks>
        public const float TotalSeconds = 1080f;

        /// <summary>包絡線が落ちはじめる時刻（<see cref="TotalSeconds"/> に対する比）。</summary>
        public const float FadeFromFraction = 0.72f;

        /// <summary>
        /// いちばん高い波の高さ（m）の上限。**台地と山が重なるとこれを超える** ——
        /// <see cref="PlateauFraction"/> のぶん（62%）が上乗せされるので、
        /// 実際の最大は 1.6 倍ほどになる。それが「海面全体が持ち上がった上を
        /// 波が走る」の見え方である。
        /// </summary>
        /// <remarks>
        /// ★★ 9 -> 22（2026-08-29、実機報告「波の高さが低すぎる」）。
        ///   実機ログの <c>peak wave 1.9 m</c> がそれで、しかもそのマップの
        ///   海面は <b>207 m</b> だった —— 207 m の海に 1.9 m 足しても
        ///   <b>何も起きていないようにしか見えない。</b>
        ///   22 m は実際の巨大津波の遡上高（10〜40 m）の範囲に入る。
        /// </remarks>
        public const float MaxAmplitudeMetres = 22f;

        /// <summary>同じく下限（強度が低くても、これ未満なら津波と呼べない）。</summary>
        /// <remarks>★ 1.5 -> 5（同上）。これ未満は「津波」と呼べない。</remarks>
        public const float MinAmplitudeMetres = 5f;

        /// <summary>
        /// 地震の強度（0〜255）から、いちばん高い波の高さ（m）を出す。
        /// </summary>
        public static float AmplitudeOf(byte intensity)
        {
            float a = MaxAmplitudeMetres * (intensity / 255f);
            if (a < MinAmplitudeMetres) return MinAmplitudeMetres;
            return a;
        }

        /// <summary>
        /// 震源から <paramref name="distanceMetres"/> の地点で、地震から
        /// <paramref name="elapsedSeconds"/> 秒後の<b>海面の持ち上がり</b>（m、0 以上）。
        ///
        /// 波列が過ぎたあとは 0 を返す —— <b>呼び出し側はそれで水位を戻す。</b>
        /// </summary>
        public static float RiseAt(float distanceMetres, float elapsedSeconds,
                                   float amplitudeMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedSeconds) || elapsedSeconds < 0f) return 0f;
            if (IsBad(amplitudeMetres) || amplitudeMetres <= 0f) return 0f;
            if (elapsedSeconds >= TotalSeconds) return 0f;

            float envelope = EnvelopeAt(elapsedSeconds);
            if (envelope <= 0f) return 0f;

            float sum = 0f;
            for (int k = 0; k < CrestCount; k++)
            {
                float birth = k * CrestGapSeconds;
                float age = elapsedSeconds - birth;
                if (age <= 0f) continue;

                // この波の今いる半径。
                float front = age * SpeedMetresPerSecond;
                float offset = distanceMetres - front;

                sum += CrestWeights[k] * Bell(offset, CrestWidthMetres);
            }

            // ★★ **台地を足す。** 山が通り過ぎても海面は上がったまま残る
            //    （上の doc）。これが無いと、ただの高潮に見える。
            sum += PlateauShapeAt(distanceMetres, elapsedSeconds);

            if (sum <= 0f) return 0f;
            return amplitudeMetres * envelope * sum;
        }

        /// <summary>
        /// 全体の包絡線 <c>[0,1]</c>。**終わりで確実に 0 になること** ——
        /// 0 にならないと、呼び出し側が水位を戻す合図を受け取れない。
        /// </summary>
        public static float EnvelopeAt(float elapsedSeconds)
        {
            if (IsBad(elapsedSeconds) || elapsedSeconds < 0f) return 0f;
            if (elapsedSeconds >= TotalSeconds) return 0f;

            float w = elapsedSeconds / TotalSeconds;
            if (w <= FadeFromFraction) return 1f;

            return (1f - w) / (1f - FadeFromFraction);
        }

        /// <summary>
        /// 波列の先頭が届いている半径（m）。呼び出し側が
        /// **どこまでを見に行けばよいか**を決めるのに使う。
        /// </summary>
        public static float FrontRadiusAt(float elapsedSeconds)
        {
            if (IsBad(elapsedSeconds) || elapsedSeconds <= 0f) return 0f;
            return elapsedSeconds * SpeedMetresPerSecond + CrestWidthMetres;
        }

        /// <summary>
        /// 台地の形（いちばん高い波に対する比）。<b>震源のまわりの海がまるごと
        /// 持ち上がったまま居座る</b>ぶんである。
        ///
        /// ★ 立ち上がりは <see cref="PlateauRampSeconds"/> かけて。
        ///   一瞬で上げると水が壁になって岸へ倒れ込む（それは津波ではなく決壊である）。
        /// ★ 下がるのは全体の包絡線に任せる（<see cref="RiseAt"/> が掛ける）ので、
        ///   ここでは下げない —— **2 か所で下げると、どちらが効いたのか分からなくなる。**
        /// </summary>
        public static float PlateauShapeAt(float distanceMetres, float elapsedSeconds)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedSeconds) || elapsedSeconds <= 0f) return 0f;

            // ── 立ち上がり ────────────────────────────────────
            float ramp = elapsedSeconds / PlateauRampSeconds;
            if (ramp > 1f) ramp = 1f;
            // なめらかに（線形だと立ち上がりに折れ目が出る）。
            ramp = ramp * ramp * (3f - 2f * ramp);

            // ── 広がり ──────────────────────────────────────
            if (distanceMetres >= PlateauEdgeMetres) return 0f;

            float reach = 1f;
            if (distanceMetres > PlateauRadiusMetres)
            {
                float t = (distanceMetres - PlateauRadiusMetres)
                          / (PlateauEdgeMetres - PlateauRadiusMetres);
                reach = 0.5f * (1f + (float)Math.Cos(Math.PI * t));
            }

            return PlateauFraction * ramp * reach;
        }

        /// <summary>釣鐘。<paramref name="offset"/> が 0 で 1、幅の外で 0。</summary>
        private static float Bell(float offset, float width)
        {
            if (width <= 0f) return 0f;

            float d = offset < 0f ? -offset : offset;
            if (d >= width) return 0f;

            // 余弦の山。縁で高さも傾きも 0 になる（折れ目が段差に見えない）。
            return 0.5f * (1f + (float)Math.Cos(Math.PI * (d / width)));
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
