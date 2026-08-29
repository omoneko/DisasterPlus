using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 津波の水源を<b>どこに何個置くか</b>を決める。**エンジン非依存の純関数だけ。**
    ///
    /// ── ★★ なぜこれが要るのか（2026-08-29、実機報告の原因 2 と 3）─────────────
    ///
    /// 所有者:「津波がやはり高さと波の継続力がとても弱いです。」
    /// 前の版は<b>震源のまわりに水源を撒きっぱなし</b>にしていた。数えると:
    ///
    /// <code>
    ///   原因 2 ── 水源どうしが届いていなかった
    ///     水源の作用半径は IL 実測で  r = Sqrt(rate) * 0.4 + 10  [m]
    ///     rate = 250,000 なら r = 210 m、つまり直径 420 m。
    ///     ところが格子の間隔は 480 m だった。
    ///     ＝ **隣どうしが 60 m 離れていて、一度も繋がらない。**
    ///     持ち上がるのは点々とした円盤だけで、その水はすぐ隙間へ流れ落ちる。
    ///     これが「高さが弱い」の正体である。壁になっていなかった。
    ///
    ///   原因 3 ── 水源が前線についていけなかった
    ///     上限 300 個 × 間隔 480 m が覆えるのは半径 4,691 m まで。
    ///     環はそこを 124 ゲーム内秒（≒1.6 実秒）で通り抜け、
    ///     その先（〜8,200 m）には**水源が 1 つも無い**。
    ///     ＝ 「継続力が弱い」の正体。波は消えたのではなく、
    ///        持ち上げる相手がいなくなっただけだった。
    /// </code>
    ///
    /// ── 直し方: <b>撒くのをやめて、前線に沿って並べ直す</b> ──────────────────
    ///
    /// 持ち上がっているのは<b>環（と、③より前は円盤）だけ</b>である。
    /// 海全体に水源は要らない。だから毎 tick、
    ///
    /// <list type="bullet">
    /// <item>今の帯 <c>[inner, outer]</c> を<b>同心円で埋める</b>（<see cref="Fill"/>）</item>
    /// <item>使う水源だけ位置と流量を書き換え、<b>余りは流量 0 にして黙らせる</b>
    ///   —— IL では <c>inRate &gt; 0</c> / <c>outRate &gt; 0</c> の門があるので、
    ///   流量 0 の水源は<b>1 セルも走査されない</b>（＝ただ）</item>
    /// <item>間隔は必ず<b>作用直径より狭く</b>する（<see cref="StepFor"/>）</item>
    /// </list>
    ///
    /// これで環は半径がいくつでも同じ密度で支えられ、8,200 m まで痩せない。
    /// </summary>
    public static class TsunamiSourceLayout
    {
        /// <summary>
        /// 水源の作用半径（m）。**IL 実測**（<c>WaterSimulation.SimulateWater</c>、
        /// 吐き出し側 IL_1E10 付近）: <c>r = Sqrt(rate) * 0.4 + 10</c>。
        ///
        /// ★ <c>TYPE_FACILITY</c> / <c>TYPE_CLEANER</c> には 50 m の上限が掛かるが、
        ///   <c>TYPE_NATURAL</c> には掛からない。だから大きくできる。
        /// </summary>
        public static float RadiusForRate(uint rate)
        {
            return (float)Math.Sqrt(rate) * 0.4f + 10f;
        }

        /// <summary>上の逆。欲しい作用半径（m）から流量を出す。</summary>
        public static uint RateForRadius(float metres)
        {
            if (float.IsNaN(metres) || metres <= 10f) return 0u;

            double s = (metres - 10f) / 0.4;
            double rate = s * s;
            if (rate > 4000000000d) rate = 4000000000d;
            return (uint)rate;
        }

        /// <summary>
        /// 隣の水源とどれだけ重ねるか（作用直径に対する比）。
        ///
        /// ★★ **1 未満にしないと壁にならない。** 1 ちょうどだと円盤が接するだけで、
        ///   接点の水位は上がらず、そこから水が抜ける（原因 2 そのもの）。
        /// </summary>
        public const float OverlapFactor = 0.85f;

        /// <summary>作用半径 <paramref name="radiusMetres"/> の水源を並べる間隔（m）。</summary>
        public static float StepFor(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return 1f;
            return radiusMetres * 2f * OverlapFactor;
        }

        /// <summary>
        /// 帯 <c>[inner, outer]</c>（m、震源からの距離）を同心円で埋め、
        /// <paramref name="xz"/> に <c>(dx, dz)</c> を詰める。**戻り値は詰めた個数。**
        ///
        /// ★★ <b>外側の輪から先に詰める。</b> 予算（<paramref name="max"/>）が
        ///   尽きたときに落ちるのは<b>内側</b>である —— 見えているのは
        ///   前線（外側）なので、削るならそちらではない。
        /// </summary>
        /// <param name="xz">長さ <c>2 * max</c> 以上の配列。</param>
        /// <param name="spinRadians">全体の回し。輪ごとの継ぎ目を毎回ずらす。</param>
        public static int Fill(float innerMetres, float outerMetres, float stepMetres,
                               float spinRadians, float[] xz, int max)
        {
            if (xz == null || max <= 0) return 0;
            if (max * 2 > xz.Length) max = xz.Length / 2;
            if (max <= 0) return 0;

            if (IsBad(innerMetres) || innerMetres < 0f) innerMetres = 0f;
            if (IsBad(outerMetres) || outerMetres < innerMetres) return 0;
            if (IsBad(stepMetres) || stepMetres <= 0f) return 0;
            if (IsBad(spinRadians)) spinRadians = 0f;

            // 帯を覆う同心円の本数。両端を必ず含める。
            int rings = (int)Math.Ceiling((outerMetres - innerMetres) / stepMetres) + 1;
            if (rings < 1) rings = 1;

            float gap = rings > 1 ? (outerMetres - innerMetres) / (rings - 1) : 0f;

            int written = 0;

            for (int i = 0; i < rings && written < max; i++)
            {
                float r = outerMetres - gap * i;      // ★ 外から内へ
                if (r < 0f) r = 0f;

                if (r < 1f)
                {
                    xz[written * 2] = 0f;
                    xz[written * 2 + 1] = 0f;
                    written++;
                    continue;
                }

                int count = (int)Math.Ceiling(6.2831853f * r / stepMetres);
                if (count < 1) count = 1;

                // ★ 輪ごとに黄金角ぶんずらす。揃えると放射状の筋が残る。
                float spin = spinRadians + i * 2.39996323f;

                for (int k = 0; k < count && written < max; k++)
                {
                    float a = spin + 6.2831853f * k / count;
                    xz[written * 2] = (float)Math.Cos(a) * r;
                    xz[written * 2 + 1] = (float)Math.Sin(a) * r;
                    written++;
                }
            }

            return written;
        }

        /// <summary>
        /// 半径 <paramref name="outerMetres"/> の円盤を <paramref name="max"/> 個で
        /// 覆うのに必要な間隔（m）。**後始末（残った水を吸わせる）で使う。**
        ///
        /// ★ 円盤の面積を頭割りするだけなので、<see cref="StepFor"/> より粗くなる。
        ///   吸うほうは隙間があっても水が流れてくるので、これでよい。
        /// </summary>
        public static float DrainStepFor(float outerMetres, int max)
        {
            if (IsBad(outerMetres) || outerMetres <= 0f || max <= 0) return 1f;
            return outerMetres * (float)Math.Sqrt(Math.PI / max);
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
