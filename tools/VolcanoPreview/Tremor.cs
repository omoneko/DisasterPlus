using System;
using System.Globalization;
using System.IO;
using System.Text;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// 火山性地震の地動を**ゲームを起動せずに**描いて確かめる。
    /// 呼ぶのは <see cref="VolcanicTremor"/> の実物である。
    ///
    /// 揺れは動画でしか見えないので、ここでは<b>記象（波形）</b>として描く ——
    /// 「切れ目が無いか」「群発の山が立っているか」「噴火で最大になるか」は
    /// 波形を見れば分かる。
    /// </summary>
    internal static class Tremor
    {
        private const int Width = 900;
        private const int RowHeight = 110;

        /// <summary>描画フレーム（60 fps）。**ゲームが評価するのと同じ刻み。**</summary>
        private const float SampleHz = 60f;

        /// <summary>1 段に描く秒数。</summary>
        private const float WindowSeconds = 30f;

        private static readonly float[] Activities = { 0.18f, 0.45f, 0.75f, 1.00f };

        public static void Report(string dir, StringBuilder log)
        {
            uint seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));

            log.AppendLine("## volcanic tremor (swarm + harmonic tremor)");
            log.AppendLine("  sampled at " + F(SampleHz) + " fps (the rate the camera shake runs at)");
            log.AppendLine("  activity | peak | rms  | quiet windows of 1 s (must be 0)");

            int rows = Activities.Length;
            var rgb = new byte[Width * RowHeight * rows * 3];
            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 20; rgb[i + 1] = 22; rgb[i + 2] = 26;
            }

            for (int r = 0; r < rows; r++)
            {
                float a = Activities[r];
                float peak = 0f;
                double sum = 0.0;
                int n = 0;
                int quiet = 0;

                int top = r * RowHeight;
                int mid = top + RowHeight / 2;

                for (int x = 0; x < Width; x++) Set(rgb, x, mid, 0x303840);

                float windowPeak = 0f;
                float nextWindow = 1f;

                for (float t = 0f; t < WindowSeconds; t += 1f / SampleHz)
                {
                    float v = VolcanicTremor.DisplacementAt(seed, t, a);
                    float abs = v < 0f ? -v : v;
                    if (abs > peak) peak = abs;
                    if (abs > windowPeak) windowPeak = abs;
                    sum += v * v;
                    n++;

                    if (t >= nextWindow)
                    {
                        if (windowPeak <= 0.01f) quiet++;
                        windowPeak = 0f;
                        nextWindow += 1f;
                    }

                    int x = (int)(t / WindowSeconds * Width);
                    int y = mid - (int)(v * (RowHeight * 0.45f));
                    Set(rgb, x, y, r == rows - 1 ? 0xFF7038 : 0xE8C060);
                }

                log.AppendLine("     " + a.ToString("F2", CultureInfo.InvariantCulture)
                               + "  | " + F2(peak) + " | " + F2(Math.Sqrt(sum / n))
                               + " | " + quiet);
            }

            Png.Write(Path.Combine(dir, "tremor-waveform.png"), Width, RowHeight * rows, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, "tremor-waveform.png")
                              + "  (" + rows + " activity levels x " + F(WindowSeconds) + " s)");

            log.AppendLine("  attenuation (reach = 4.5 R; outside it is exactly 0):");
            const float reach = 5400f;
            for (float d = 0f; d <= reach; d += reach / 6f)
            {
                log.AppendLine("    " + F(d).PadLeft(5) + " m -> "
                               + F2(VolcanicTremor.AttenuationAt(d, reach)));
            }
            log.AppendLine();
        }

        private static void Set(byte[] rgb, int x, int y, int colour)
        {
            if (x < 0 || y < 0 || x >= Width) return;
            int i = (y * Width + x) * 3;
            if (i < 0 || i + 2 >= rgb.Length) return;
            rgb[i] = (byte)(colour >> 16);
            rgb[i + 1] = (byte)(colour >> 8);
            rgb[i + 2] = (byte)colour;
        }

        private static string F(double v) { return v.ToString("F0", CultureInfo.InvariantCulture); }
        private static string F2(double v) { return v.ToString("F2", CultureInfo.InvariantCulture); }
    }
}
