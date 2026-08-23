using System;
using System.IO;
using DisasterPlus.Core.Common;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.IconPreview
{
    /// <summary>
    /// 災害パネルのタイルに載せる絵を、**ゲームを起動せずに**描いて確かめる
    /// （2026-08-22、所有者の依頼「タブアイコンの火山と台風をイラストにしてほしい」）。
    ///
    /// <b>Core の実物（<see cref="DisasterIconArt"/>）をそのままコンパイルして呼ぶ</b>ので、
    /// 書き直した近似ではない。
    ///
    /// ★ いちばん大事なのは<b>実寸で読めるか</b>である。タイルは 109×100 で、絵は
    ///   その 7 割ほど ——**70 px 前後**。拡大して整っていても、実寸で潰れては意味が無い。
    ///   だから 3 段（実寸・2 倍・6 倍）を、明るい背景と暗い背景の両方で並べる。
    ///
    ///   dotnet run --project tools/IconPreview -- docs/images/ui
    /// </summary>
    internal static class Program
    {
        /// <summary>実機の絵の一辺（px）。タイル 109×100 の 7 割。</summary>
        private const int ActualSize = 70;

        /// <summary>テクスチャそのものの一辺（px）。実機もこれで作る。</summary>
        private const int TextureSize = 128;

        private static readonly int[] Zooms = { 1, 2, 6 };

        /// <summary>タイルの背景は明るくも暗くもなりうる。両方で見る。</summary>
        private static readonly byte[][] Backgrounds =
        {
            new byte[] { 218, 220, 224 },
            new byte[] { 46, 48, 54 },
        };

        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "docs/images/ui";
            Directory.CreateDirectory(outDir);

            Sheet(outDir, "icon-volcano.png", true);
            Sheet(outDir, "icon-typhoon.png", false);

            Console.WriteLine("actual size on the tile: " + ActualSize + " px"
                              + "   texture: " + TextureSize + " px");
            return 0;
        }

        /// <summary>実寸・2 倍・6 倍を、明るい背景と暗い背景で並べた 1 枚。</summary>
        private static void Sheet(string dir, string name, bool volcano)
        {
            const int Pad = 12;

            int rowWidth = Pad;
            foreach (int z in Zooms) rowWidth += ActualSize * z + Pad;

            int rowHeight = ActualSize * Zooms[Zooms.Length - 1] + Pad * 2;
            int width = rowWidth;
            int height = rowHeight * Backgrounds.Length;

            var rgb = new byte[width * height * 3];

            for (int band = 0; band < Backgrounds.Length; band++)
            {
                byte[] back = Backgrounds[band];
                int top = band * rowHeight;

                for (int y = top; y < top + rowHeight && y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 3;
                        rgb[i] = back[0];
                        rgb[i + 1] = back[1];
                        rgb[i + 2] = back[2];
                    }
                }

                int left = Pad;
                foreach (int z in Zooms)
                {
                    Draw(rgb, width, height, left, top + Pad, ActualSize * z, volcano);
                    left += ActualSize * z + Pad;
                }
            }

            Png.Write(Path.Combine(dir, name), width, height, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, name));
        }

        /// <summary>
        /// 1 枚ぶん。**実機と同じ手順**で描く ——
        /// <see cref="TextureSize"/> のテクスチャを作ってから <paramref name="size"/> へ
        /// 縮める（実機も 128 px のテクスチャを 70 px の枠に貼る）。
        /// </summary>
        private static void Draw(byte[] rgb, int width, int height, int left, int top,
                                 int size, bool volcano)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 縮小のときは 2x2 で平均する（実機の双一次に近い見え方にする）。
                    int samples = size < TextureSize ? 2 : 1;
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples) / size;
                            float v = 1f - (y + (sy + 0.5f) / samples) / size;

                            IconPixel p = volcano
                                ? DisasterIconArt.Volcano(u, v)
                                : DisasterIconArt.Typhoon(u, v);

                            float pa = p.A / 255f;
                            r += p.R * pa;
                            g += p.G * pa;
                            b += p.B * pa;
                            a += pa;
                        }
                    }

                    int n = samples * samples;
                    r /= n;
                    g /= n;
                    b /= n;
                    a /= n;

                    int px = left + x;
                    int py = top + y;
                    if (px < 0 || px >= width || py < 0 || py >= height) continue;

                    int i = (py * width + px) * 3;
                    rgb[i] = (byte)(rgb[i] * (1f - a) + r);
                    rgb[i + 1] = (byte)(rgb[i + 1] * (1f - a) + g);
                    rgb[i + 2] = (byte)(rgb[i + 2] * (1f - a) + b);
                }
            }
        }
    }
}
