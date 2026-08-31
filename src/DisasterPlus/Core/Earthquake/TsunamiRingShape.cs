using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>震源から同心円状に立つ津波の「形」。</b>エンジンに触らない層。
    ///
    /// ── 何を作り直したのか（2026-08-31、所有者の指示）────────────────
    ///
    /// &gt; DLC そのまま使っちゃったら わざわざ MOD で出す意味ないじゃないですか。
    /// &gt; 仕組みを解析して応用、震源付近から津波が同心円状に発生するのを作ってほしい
    ///
    /// それまでの <c>TsunamiSource</c> は <c>TYPE_IMPACT</c> の水波、つまり
    /// <b>海中に置く仮想の丘</b>を出していた。丘は水を押しのけるだけで<b>作らない</b>。
    /// 正味ゼロの外力しか出せず、出せるのは双極子であって水の壁ではない。
    /// オフライン実測（同じ棚・水深 174 m・同じ汀線、2026-08-31）:
    ///
    /// <list type="bullet">
    /// <item>DLC の津波（強度 100）… 汀線 <b>84.8 m</b>（震源 88.8 m からほぼ減衰しない）</item>
    /// <item>こちらの丘（drive 3857）… 汀線 <b>19.3 m</b>（震源 72.7 m から 1/4 に落ちる）</item>
    /// </list>
    ///
    /// 差は振幅ではなく<b>仕掛け</b>だった。DLC は外周セルの海面を書き換え、
    /// そこは Dirichlet 境界なので<b>水が湧く</b>。丘をどれだけ大きくしても真似できない。
    ///
    /// ★★ **そこでゲームの別の道具を使う。**<c>WaterSource</c> の
    ///   <c>TYPE_NATURAL</c>（マップの川の湧き出し）は
    ///   <b>「指定した円を指定した水位まで満たす／抜く」</b>装置で、
    ///   置き場所が自由である —— <b>境界条件を震源へ持ってきたもの</b>。
    ///   波形は DLC のものをそのまま使い、置き場所だけを移す。それが
    ///   「仕組みを解析して応用」の中身である。
    ///
    /// ── 波形（<c>WaterWave.GetSeaLevel</c> IL_0000-00B6 と同一）──────
    ///
    /// <code>
    /// amp = delta * (65536 - t) / 65536                  // ゆっくり減衰
    /// arg = amp * (1 - cos(2*pi * t / T))                // 0 -> 2amp -> 0 の包絡
    /// off = arg * sin(2*pi * 1.5 * t / T) / 2            // 1.5 周期
    /// level = seaLevel - off
    /// </code>
    ///
    /// t は 1 水ステップにつき +64、<c>T = 16384</c> ＝ **256 水ステップ**。
    /// 押し波の頂点は <c>t = T/2</c> で <c>seaLevel + amp</c>。
    /// 前後に引き波が来る —— <b>引き → 押し → 引き</b>。
    /// </summary>
    public static class TsunamiRingShape
    {
        /// <summary>バニラの <c>m_duration</c>。<c>256 &lt;&lt; 6</c>。</summary>
        public const int DurationTicks = 16384;

        /// <summary>1 水ステップぶんの時計の進み（<c>m_currentTime</c> は +64）。</summary>
        public const int TicksPerWaterStep = 64;

        /// <summary>波形が続く水ステップ数。256 ＝ 16,384 sim フレーム ≒ 4.5 実分。</summary>
        public const int WaterSteps = DurationTicks / TicksPerWaterStep;

        /// <summary><c>m_target</c> は ushort。1023.98 m を超える水位は書けない。</summary>
        public const int MaxLevelUnits = 65535;

        /// <summary>
        /// 円の半径（m）。<c>TYPE_NATURAL</c> は施設と違い<b>上限が無い</b>
        /// （IL_1D0D-1D21: <c>sqrt(rate)*0.4 + 10</c>、type 2/3 だけ clamp 10..50）。
        /// </summary>
        public static float RadiusMetresForRate(long rate)
        {
            if (rate <= 0L) return 0f;
            return (float)Math.Sqrt(rate) * 0.4f + 10f;
        }

        /// <summary>
        /// その半径を出すのに要る流量。<b>半径と流量は切り離せない</b> ——
        /// 半径は流量の平方根で決まるので、広い震源は必ず大流量になる。
        /// 高さを決めるのは流量ではなく <c>m_target</c> のほうである。
        /// </summary>
        public static long RateForRadiusMetres(float radiusMetres)
        {
            if (radiusMetres <= 10f) return 0L;
            double q = (radiusMetres - 10.0) / 0.4;
            return (long)(q * q);
        }

        /// <summary>
        /// バニラの <c>m_delta</c>（1/64 m）。<c>round(64 * 64 * intensity / 55)</c>。
        /// 強度 100 で 7447（116.4 m）、255 で 18991（296.7 m）。
        /// </summary>
        public static int VanillaDeltaUnits(int intensity)
        {
            if (intensity < 0) intensity = 0;
            return (int)Math.Round(64.0 * 64.0 * intensity / 55.0);
        }

        /// <summary>
        /// 平常の海面からのずれ（1/64 m）。<b>正なら押し波、負なら引き波。</b>
        ///
        /// バニラの式は <c>level = original - off</c> なので、ここではその
        /// <c>-off</c> をそのまま返す（符号の取り違えを呼び出し側に持ち込まない）。
        /// </summary>
        /// <param name="elapsedTicks">経過（1 水ステップ = 64）。</param>
        /// <param name="deltaUnits">振幅の元（<c>m_delta</c> 相当、1/64 m）。</param>
        public static int LevelOffsetUnits(int elapsedTicks, int deltaUnits)
        {
            if (elapsedTicks <= 0 || elapsedTicks >= DurationTicks) return 0;
            if (deltaUnits <= 0) return 0;

            double amp = (double)deltaUnits * (65536 - elapsedTicks) / 65536.0;
            double phase = 2.0 * Math.PI * elapsedTicks / DurationTicks;
            double arg = amp - amp * Math.Cos(phase);
            double off = arg * Math.Sin(1.5 * phase) / 2.0;

            return -(int)off;
        }

        /// <summary>
        /// 引き波の深さを抑える。**海を空にしてはいけない。**
        ///
        /// ★★ オフライン実測（2026-08-31）で、抑えないと震源の水柱が
        ///   <b>100% 抜けて海底が露出した</b>（強度 255・半径 1280 m で 20 水ステップ）。
        ///   引き波は現実にも起きるが、海底が丸見えになるのは絵として壊れている。
        ///   水深の <paramref name="maxFraction"/> までに留める。
        /// </summary>
        /// <param name="offsetUnits">生の <see cref="LevelOffsetUnits"/>。</param>
        /// <param name="depthUnits">震源の水深（1/64 m）。</param>
        public static int ClampDraw(int offsetUnits, int depthUnits, float maxFraction)
        {
            if (offsetUnits >= 0) return offsetUnits;
            if (depthUnits <= 0) return 0;

            int limit = (int)(depthUnits * maxFraction);
            if (limit < 0) limit = 0;
            return offsetUnits < -limit ? -limit : offsetUnits;
        }
    }
}
