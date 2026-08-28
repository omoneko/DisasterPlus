using System;
using System.IO;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.TsunamiPreview
{
    /// <summary>
    /// <see cref="TsunamiWaveTrain"/> の海面を、**ゲームを起動せずに**上から描いて
    /// 確かめる（2026-08-29、所有者の指摘「求めているのは、震源地から水の壁が
    /// 同心円状に生成される挙動です」）。
    ///
    /// <b>上から見ないと「輪」かどうかが分からない。</b> 断面だけ見ていた頃は、
    /// 台地（一様に持ち上がる）と輪の違いが図から読めなかった。
    ///
    ///   dotnet run --project tools/TsunamiPreview -- docs/images/earthquake
    /// </summary>
    internal static class Program
    {
        private const int Size = 700;

        /// <summary>見る範囲（m、片側）。波の届く限界より少し広く。</summary>
        private const float ViewHalf = 9000f;

        private const float Amplitude = 22f;

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            // ★ 4 つの段が 1 枚ずつ見えるように選ぶ（隆起・台地・ドーナツ・伝播）。
            foreach (float t in new[] { 10f, 40f, 78f, 160f, 300f })
            {
                string path = Path.Combine(dir, "tsunami-t" + ((int)t) + ".png");
                Png.Write(path, Size, Size, Render(t));
                Console.WriteLine("wrote " + path);
            }

            Measure();
            return 0;
        }

        /// <summary>
        /// 目で見るだけにしない。**輪であることを数で確かめる。**
        /// 前線の外は 0、前線で最大、内側は wake まで落ちること。
        /// </summary>
        private static void Measure()
        {
            Console.WriteLine();
            Console.WriteLine("    t | stage      | ring(m) | at ring | centre | outside");

            foreach (float t in new[] { 6f, 12f, 25f, 40f, 60f, 78f, 120f, 200f, 300f })
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(t);

                string stage;
                if (t <= TsunamiWaveTrain.BulgeSeconds) stage = "1 bulge  ";
                else if (t <= TsunamiWaveTrain.SpreadSeconds) stage = "2 plateau";
                else if (t <= TsunamiWaveTrain.CollapseSeconds) stage = "3 hollow ";
                else stage = "4 ring   ";

                float atRing = Peak(t, Math.Max(ring - 300f, 0f), ring + 300f);
                float centre = TsunamiWaveTrain.RiseAt(0f, t, Amplitude);
                float outside = TsunamiWaveTrain.RiseAt(
                    ring + TsunamiWaveTrain.RingWidthMetres + 300f, t, Amplitude);

                Console.WriteLine(t.ToString("F0").PadLeft(5)
                    + " | " + stage
                    + " |" + ring.ToString("F0").PadLeft(8)
                    + " |" + atRing.ToString("F1").PadLeft(8)
                    + " |" + centre.ToString("F1").PadLeft(7)
                    + " |" + outside.ToString("F1").PadLeft(8));
            }
        }

        private static float Peak(float t, float from, float to)
        {
            float best = 0f;
            for (float d = Math.Max(from, 0f); d <= to; d += 20f)
            {
                float r = TsunamiWaveTrain.RiseAt(d, t, Amplitude);
                if (r > best) best = r;
            }
            return best;
        }

        private static byte[] Render(float t)
        {
            var rgb = new byte[Size * Size * 3];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float wx = (x / (float)(Size - 1) * 2f - 1f) * ViewHalf;
                    float wz = (y / (float)(Size - 1) * 2f - 1f) * ViewHalf;
                    float d = (float)Math.Sqrt(wx * wx + wz * wz);

                    float rise = TsunamiWaveTrain.RiseAt(d, t, Amplitude);
                    float k = rise / (Amplitude * 1.1f);
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
