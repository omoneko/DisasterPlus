using System;
using System.IO;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.TsunamiPreview
{
    /// <summary>
    /// <see cref="TsunamiWaveTrain"/> の海面を、**ゲームを起動せずに**上から描いて
    /// 確かめる。
    ///
    /// <b>上から見ないと「輪」かどうかが分からない。</b> 断面だけ見ていた頃は、
    /// 台地（一様に持ち上がる）と輪の違いが図から読めなかった。
    ///
    /// ── ★★ 2026-08-29、所有者「シミュレーションして確認してますか？」──────
    ///
    /// **形しか見ていなかった。** 形は正しかったのに実機で弱かったのは、
    /// <b>形を海に載せる側</b>（フレーム・水源の作用半径・覆える範囲）を
    /// 一度も数えていなかったからである。<see cref="Delivery"/> がそれを数える。
    ///
    ///   dotnet run --project tools/TsunamiPreview -- docs/images/earthquake
    /// </summary>
    internal static class Program
    {
        private const int Size = 700;

        /// <summary>見る範囲（m、片側）。波の届く限界より少し広く。</summary>
        private const float ViewHalf = 9000f;

        private const float Amplitude = 22f;

        /// <summary>ゲーム速度 1 のおおよそ。**フレームと実秒を混ぜないための注釈。**</summary>
        private const float FramesPerRealSecond = 60f;

        /// <summary><c>TsunamiSurge.Rate</c> と同じ値。</summary>
        private const uint Rate = 900000u;

        /// <summary><c>TsunamiSurge.MaxSources</c> と同じ値。</summary>
        private const int MaxSources = 240;

        /// <summary><c>TsunamiSurge.IntervalFrames</c> と同じ値。</summary>
        private const int IntervalFrames = 8;

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            // ★ 4 つの段が 1 枚ずつ見えるように選ぶ（隆起・台地・ドーナツ・伝播）。
            foreach (float f in new[] { 150f, 780f, 1500f, 2600f, 3700f })
            {
                string path = Path.Combine(dir, "tsunami-f" + ((int)f) + ".png");
                Png.Write(path, Size, Size, Render(f));
                Console.WriteLine("wrote " + path);
            }

            Shape();
            Delivery();
            return 0;
        }

        /// <summary>
        /// 目で見るだけにしない。**輪であることを数で確かめる。**
        /// 前線の外は 0、前線で最大、内側も 0（＝平常の海）であること。
        /// </summary>
        private static void Shape()
        {
            Console.WriteLine();
            Console.WriteLine("== 形 ==");
            Console.WriteLine(" frame |  real s | stage      | ring(m) | at ring | centre | outside");

            foreach (float f in new[] { 120f, 240f, 500f, 780f, 1100f, 1500f,
                                        2000f, 2600f, 3200f, 3833f })
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(f);

                string stage;
                if (f <= TsunamiWaveTrain.BulgeFrames) stage = "1 bulge  ";
                else if (f <= TsunamiWaveTrain.SpreadFrames) stage = "2 plateau";
                else if (f <= TsunamiWaveTrain.CollapseFrames) stage = "3 hollow ";
                else stage = "4 ring   ";

                float atRing = Peak(f, Math.Max(ring - 300f, 0f), ring + 300f);
                float centre = TsunamiWaveTrain.RiseAt(0f, f, Amplitude);
                float outside = TsunamiWaveTrain.RiseAt(
                    ring + TsunamiWaveTrain.RingWidthMetres + 300f, f, Amplitude);

                Console.WriteLine(f.ToString("F0").PadLeft(6)
                    + " |" + (f / FramesPerRealSecond).ToString("F1").PadLeft(8)
                    + " | " + stage
                    + " |" + ring.ToString("F0").PadLeft(8)
                    + " |" + atRing.ToString("F1").PadLeft(8)
                    + " |" + centre.ToString("F1").PadLeft(7)
                    + " |" + outside.ToString("F1").PadLeft(8));
            }
        }

        /// <summary>
        /// ★★ **形を海に載せられるのかを数える。** ここを見ていなかったのが
        ///    2026-08-29 の「高さと継続力が弱い」の原因だった。
        ///
        /// <list type="bullet">
        /// <item>水源の作用直径 &gt; 並べる間隔 か（＝壁が繋がるか）</item>
        /// <item>予算 <see cref="MaxSources"/> で前線の輪を丸ごと置けるか</item>
        /// <item>並べ直す間隔のあいだに前線が帯より大きく飛ばないか</item>
        /// </list>
        /// </summary>
        private static void Delivery()
        {
            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            float step = TsunamiSourceLayout.StepFor(radius);

            Console.WriteLine();
            Console.WriteLine("== 海に載せる側 ==");
            Console.WriteLine("  水源の作用半径 = Sqrt(" + Rate + ")*0.4+10 = "
                              + radius.ToString("F0") + " m（直径 "
                              + (radius * 2f).ToString("F0") + " m）");
            Console.WriteLine("  並べる間隔     = " + step.ToString("F0") + " m  -> "
                              + (step < radius * 2f ? "重なる（壁になる）"
                                                    : "★ 隙間ができる（壁にならない）"));
            Console.WriteLine("  旧設定の比較   = rate 250000 -> 半径 "
                              + TsunamiSourceLayout.RadiusForRate(250000u).ToString("F0")
                              + " m を 480 m 間隔 -> 隙間 "
                              + (480f - 2f * TsunamiSourceLayout.RadiusForRate(250000u))
                                .ToString("F0") + " m");

            float advance = TsunamiWaveTrain.SpeedMetresPerFrame * IntervalFrames;
            Console.WriteLine("  並べ直しの間に前線が進む距離 = " + advance.ToString("F0")
                              + " m（帯の幅 " + TsunamiWaveTrain.RingWidthMetres.ToString("F0")
                              + " m）-> "
                              + (advance < TsunamiWaveTrain.RingWidthMetres
                                 ? "追随できる" : "★ 帯より大きく飛ぶ"));

            Console.WriteLine();
            Console.WriteLine(" frame | ring(m) | 要る数 | 置ける | 前線の隣接(m) | 高さ(m)");

            var xz = new float[MaxSources * 2];

            for (float f = TsunamiWaveTrain.CollapseFrames;
                 f <= TsunamiWaveTrain.RingLeavesAtFrame + 1f; f += 300f)
            {
                float ring = Math.Min(TsunamiWaveTrain.RingRadiusAt(f),
                                      TsunamiWaveTrain.ReachEdgeMetres);
                float inner = TsunamiWaveTrain.RingInnerRadiusAt(f);

                int used = TsunamiSourceLayout.Fill(inner, ring, step, 0f, xz, MaxSources);

                int onCrest = 0;
                for (int i = 0; i < used; i++)
                {
                    float d = (float)Math.Sqrt(xz[i * 2] * xz[i * 2] +
                                               xz[i * 2 + 1] * xz[i * 2 + 1]);
                    if (Math.Abs(d - ring) < 1f) onCrest++;
                }

                int needed = (int)Math.Ceiling(6.2831853 * ring / step);
                float chord = onCrest > 0
                    ? 2f * ring * (float)Math.Sin(Math.PI / onCrest) : 0f;

                Console.WriteLine(f.ToString("F0").PadLeft(6)
                    + " |" + ring.ToString("F0").PadLeft(8)
                    + " |" + needed.ToString().PadLeft(7)
                    + " |" + onCrest.ToString().PadLeft(7)
                    + " |" + chord.ToString("F0").PadLeft(14)
                    + (chord < radius * 2f ? " ok" : " ★穴")
                    + " |" + TsunamiWaveTrain.RiseAt(ring, f, Amplitude)
                             .ToString("F1").PadLeft(8));
            }
        }

        private static float Peak(float f, float from, float to)
        {
            float best = 0f;
            for (float d = Math.Max(from, 0f); d <= to; d += 20f)
            {
                float r = TsunamiWaveTrain.RiseAt(d, f, Amplitude);
                if (r > best) best = r;
            }
            return best;
        }

        private static byte[] Render(float f)
        {
            var rgb = new byte[Size * Size * 3];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float wx = (x / (float)(Size - 1) * 2f - 1f) * ViewHalf;
                    float wz = (y / (float)(Size - 1) * 2f - 1f) * ViewHalf;
                    float d = (float)Math.Sqrt(wx * wx + wz * wz);

                    float rise = TsunamiWaveTrain.RiseAt(d, f, Amplitude);
                    float k = rise / (Amplitude * 1.2f);
                    if (k > 1f) k = 1f;

                    int at = (y * Size + x) * 3;

                    // 平常の海は暗い青、持ち上がるほど白へ。
                    rgb[at] = (byte)(18 + 232f * k);
                    rgb[at + 1] = (byte)(46 + 200f * k);
                    rgb[at + 2] = (byte)(78 + 172f * k);
                }
            }

            // 震源に印。
            int c = Size / 2;
            for (int i = -3; i <= 3; i++)
            {
                Mark(rgb, c + i, c);
                Mark(rgb, c, c + i);
            }

            return rgb;
        }

        private static void Mark(byte[] rgb, int x, int y)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size) return;
            int at = (y * Size + x) * 3;
            rgb[at] = 255; rgb[at + 1] = 90; rgb[at + 2] = 60;
        }
    }
}
