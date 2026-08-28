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

        /// <summary>
        /// 波が進む速さ（m/秒、ゲーム内時間）。
        /// **実際の津波（外洋で 200 m/s）ではない** —— そのままだとマップを
        /// 数十秒で通り抜けて何も起きない。ここは<b>この MOD が決めた演出値</b>である。
        /// </summary>
        public const float SpeedMetresPerSecond = 140f;

        /// <summary>波と波の間隔（秒）。</summary>
        public const float CrestGapSeconds = 55f;

        /// <summary>1 本の波の幅（m）。**狭いと海岸を素通りする。**</summary>
        public const float CrestWidthMetres = 900f;

        /// <summary>
        /// 波ごとの高さの比。**第 2 波がいちばん高い** ——
        /// 実際の津波でも第 1 波が最大とは限らず、避難を解いた人が
        /// 第 2 波にさらわれるのが典型的な被害である。
        /// </summary>
        public static readonly float[] CrestWeights = { 0.72f, 1f, 0.55f };

        /// <summary>波列ぜんぶが通り過ぎるまで（秒）。これを過ぎたら 0 を返す。</summary>
        public const float TotalSeconds = 420f;

        /// <summary>包絡線が落ちはじめる時刻（<see cref="TotalSeconds"/> に対する比）。</summary>
        public const float FadeFromFraction = 0.72f;

        /// <summary>いちばん高い波の高さ（m）の上限。</summary>
        public const float MaxAmplitudeMetres = 9f;

        /// <summary>同じく下限（強度が低くても、これ未満なら津波と呼べない）。</summary>
        public const float MinAmplitudeMetres = 1.5f;

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
