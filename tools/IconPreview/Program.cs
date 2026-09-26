using System;
using System.IO;
using DisasterPlus.Core.Common;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.IconPreview
{
    /// <summary>
    /// Draws and checks the artwork that goes on the disaster panel's tiles, **without
    /// launching the game** (2026-08-22, the owner's request: "please make the volcano and
    /// typhoon tab icons into illustrations").
    ///
    /// It compiles and calls <b>the real thing from Core
    /// (<see cref="DisasterIconArt"/>)</b> directly, so it is not a rewritten approximation.
    ///
    /// ★ What matters most is <b>whether it reads at actual size</b>. The tile is 109×100
    ///   and the artwork is about 70% of that —— **around 70 px**. Looking tidy when blown up
    ///   means nothing if it turns to mush at actual size. So three sizes (actual, 2x and 6x)
    ///   are laid out side by side, on both a light and a dark background.
    ///
    ///   dotnet run --project tools/IconPreview -- docs/images/ui
    /// </summary>
    internal static class Program
    {
        /// <summary>Side of the artwork in the game (px). 70% of the 109×100 tile.</summary>
        private const int ActualSize = 70;

        /// <summary>Side of the texture itself (px). The game builds it at this size
        /// too.</summary>
        private const int TextureSize = 128;

        private static readonly int[] Zooms = { 1, 2, 6 };

        /// <summary>The tile's background can be light or dark. Look at both.</summary>
        private static readonly byte[][] Backgrounds =
        {
            new byte[] { 218, 220, 224 },
            new byte[] { 46, 48, 54 },
        };

        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "docs/images/ui";
            Directory.CreateDirectory(outDir);

            Sheet(outDir, "icon-volcano.png", Kind.Volcano);
            Sheet(outDir, "icon-typhoon.png", Kind.Typhoon);
            Sheet(outDir, "icon-trench.png", Kind.TrenchQuake);

            Console.WriteLine("actual size on the tile: " + ActualSize + " px"
                              + "   texture: " + TextureSize + " px");
            return 0;
        }

        /// <summary>One sheet with actual size, 2x and 6x laid out on a light and a dark
        /// background.</summary>
        /// <summary>Which artwork to bake. **With a two-valued bool a third one cannot be
        /// added.**</summary>
        private enum Kind { Volcano, Typhoon, TrenchQuake }

        private static void Sheet(string dir, string name, Kind kind)
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
                    Draw(rgb, width, height, left, top + Pad, ActualSize * z, kind);
                    left += ActualSize * z + Pad;
                }
            }

            Png.Write(Path.Combine(dir, name), width, height, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, name));
        }

        /// <summary>
        /// One image. Drawn by **the same procedure as the game** —— build a
        /// <see cref="TextureSize"/> texture and then shrink it to <paramref name="size"/>
        /// (the game likewise pastes a 128 px texture into a 70 px frame).
        /// </summary>
        private static void Draw(byte[] rgb, int width, int height, int left, int top,
                                 int size, Kind kind)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // When shrinking, average over 2x2 (to look close to the game's bilinear).
                    int samples = size < TextureSize ? 2 : 1;
                    float r = 0f, g = 0f, b = 0f, a = 0f;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples) / size;
                            float v = 1f - (y + (sy + 0.5f) / samples) / size;

                            IconPixel p;
                            switch (kind)
                            {
                                case Kind.Volcano:
                                    p = DisasterIconArt.Volcano(u, v); break;
                                case Kind.Typhoon:
                                    p = DisasterIconArt.Typhoon(u, v); break;
                                default:
                                    p = DisasterIconArt.TrenchQuake(u, v); break;
                            }

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
