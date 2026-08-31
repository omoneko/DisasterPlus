using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b>DLC の津波そのもの。</b><c>WaterWave.GetSeaLevel</c>（<c>m_type == 1</c>）の再現。
    ///
    /// ── なぜ移植するのか（2026-08-31）────────────────────────────
    ///
    /// こちらの波は <c>TYPE_IMPACT</c>、つまり<b>海の中に置く仮想の丘</b>である。
    /// 丘は水を押しのけるだけで<b>水を作らない</b> —— 押した分は必ずどこかから
    /// 引いてくる。だから正味ゼロの外力しか出せず、出せるのは「双極子」であって
    /// 「水の壁」ではない。
    ///
    /// DLC の津波は<b>まったく別の仕掛け</b>である。<c>SimulateWater</c> の
    /// 最外周ループの中でしか評価されず、そこで<b>外周セルの海面そのものを
    /// 書き換える</b>。外周は Dirichlet 境界 —— 足りなければ<b>水を湧かせる</b>。
    ///
    /// ★★ **つまり DLC の津波は無限の水源であり、こちらの波は水の使い回しである。**
    ///   同じ土俵で比べない限り「power が足りない」の正体は分からない。
    ///   このクラスはその土俵を作るためだけに在る。
    ///
    /// ── IL（docs/superpowers/specs/2026-08-29-tsunami-il-facts.md §3）────
    ///
    /// <code>
    /// phase = ((x - origX)*dirX + (z - origZ)*dirZ) &gt;&gt; 8   // dir 長 32768 -&gt; 1 セル = 128
    /// t     = currentTime - phase
    /// if (t &lt;= 0 || t &gt;= duration) return original
    /// amp   = delta * (65536 - currentTime) / 65536
    /// arg   = amp - amp*cos(2*pi * t / duration)             // (1-cos) の包絡
    /// off   = arg * sin(2*pi * 1.5 * t / duration) / 2       // 1.5 周期
    /// return original - off
    /// </code>
    ///
    /// 押し波の頂点は <c>t = duration/2</c> で <c>original + amp</c>。
    /// <c>currentTime</c> は 1 水ステップにつき +64、<c>duration = 16384</c>
    /// ＝ **256 水ステップで終わる。**
    /// </summary>
    public sealed class EdgeWave
    {
        /// <summary><c>m_duration</c>。バニラは <c>256 &lt;&lt; 6 = 16384</c>。</summary>
        public const int VanillaDuration = 16384;

        /// <summary>1 水ステップぶんの <c>m_currentTime</c> の進み（IL_03B3-03C6）。</summary>
        public const int TimePerStep = 64;

        /// <summary>
        /// <c>m_delta</c>（1/64 m）。<c>round(64 * 64 * intensity / 55)</c>。
        /// intensity 100 で 7447（116.4 m）、255 で 18991（296.7 m）。
        /// </summary>
        public static int DeltaFor(int intensity)
        {
            return (int)Math.Round(64.0 * 64.0 * intensity / 55.0);
        }

        public int OrigX;
        public int OrigZ;
        public int DirX = 32768;   // 内向き、長さ 32768
        public int DirZ;
        public int MinX;
        public int MinZ;
        public int MaxX;
        public int MaxZ;
        public int Delta;
        public int Duration = VanillaDuration;
        public int CurrentTime;

        /// <summary>まだ海面を動かしているか。</summary>
        public bool Active { get { return CurrentTime < Duration; } }

        /// <summary>1 水ステップ進める。</summary>
        public void Step() { CurrentTime += TimePerStep; }

        /// <summary>
        /// この外周セルの海面（1/64 m）。bbox の外と時間外は <paramref name="original"/> のまま。
        /// </summary>
        public int LevelAt(int x, int z, int original)
        {
            if (x < MinX || x > MaxX || z < MinZ || z > MaxZ) return original;

            int phase = ((x - OrigX) * DirX + (z - OrigZ) * DirZ) >> 8;
            int t = CurrentTime - phase;
            if (t <= 0 || t >= Duration) return original;

            double amp = (double)Delta * (65536 - CurrentTime) / 65536.0;
            double arg = amp - amp * Math.Cos(2.0 * Math.PI * t / Duration);
            double off = arg * Math.Sin(2.0 * Math.PI * 1.5 * t / Duration) / 2.0;

            return original - (int)off;
        }
    }
}
