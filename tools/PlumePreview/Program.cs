using System;
using System.IO;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.PlumePreview
{
    /// <summary>
    /// Draws and checks the eruption plume of <see cref="PlumeParcels"/> **without launching
    /// the game** (2026-08-22, the owner's remark: "please make the smoke animation natural
    /// and chaotic rather than geometric").
    ///
    /// This is the tool for this project's rule that "visual changes are drawn and measured
    /// offline by yourself before asking for a test in the game". It compiles and calls
    /// <b>the real thing from Core</b> directly, so it is not a rewritten approximation.
    ///
    /// The parcels are painted <b>from the back forwards</b> (the painter's algorithm). The
    /// game's <c>ParticleSystem</c> layers them in the same order, so how it looks can be
    /// judged here.
    ///
    ///   dotnet run --project tools/PlumePreview -- docs/images/volcano
    /// </summary>
    internal static class Program
    {
        private const int Width = 900;
        private const int Height = 760;

        /// <summary>The horizontal extent shown (m, half-width).</summary>
        private const float ViewHalfWidth = 2400f;

        /// <summary>The crater of a stratovolcano at the top of the slider
        /// (2928 × 0.12).</summary>
        private const float VentRadius = 351f;

        private const float ColumnHeight = 1800f;

        private const float WindX = 9f;
        private const float WindZ = 0f;

        private const uint Seed = 20260822u;

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            // Three images. Also a check that **the same picture does not come out twice**
            // (i.e. that it moves over time).
            foreach (float t in new[] { 40f, 47f, 54f })
            {
                string path = Path.Combine(dir, "plume-t" + ((int)t).ToString() + ".png");
                Png.Write(path, Width, Height, Render(t));
                Console.WriteLine("wrote " + path);
            }

            Measure();
            return 0;
        }

        /// <summary>
        /// Do not just look at it. Things to **confirm with numbers**:
        /// the fraction covered (is it too sparse), whether it moves over time,
        /// and whether it is slender enough to be a column (does the width exceed the height).
        /// </summary>
        private static void Measure()
        {
            Console.WriteLine();
            Console.WriteLine("  t |  cover | moved | top(m) | halfWidth(m)");

            byte[] previous = null;
            foreach (float t in new[] { 40f, 41f, 47f, 54f })
            {
                byte[] rgb = Render(t);

                int lit = 0;
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    if (rgb[i] > 30) lit++;
                }

                int moved = 0;
                if (previous != null)
                {
                    for (int i = 0; i < rgb.Length; i += 3)
                    {
                        int d = rgb[i] - previous[i];
                        if (d < 0) d = -d;
                        if (d > 12) moved++;
                    }
                }

                float top = 0f, wide = 0f;
                for (int i = 0; i < PlumeParcels.Count; i++)
                {
                    PlumeParcel p = PlumeParcels.At(i, t, VentRadius, ColumnHeight,
                                                    WindX, WindZ, Seed);
                    if (p.Alpha <= 0.02f) continue;
                    if (p.Y + p.RadiusMetres > top) top = p.Y + p.RadiusMetres;
                    float ax = p.X < 0f ? -p.X : p.X;
                    if (ax + p.RadiusMetres > wide) wide = ax + p.RadiusMetres;
                }

                Console.WriteLine(t.ToString("F0").PadLeft(3)
                                  + " |" + (lit * 100f / (Width * Height)).ToString("F1").PadLeft(6) + "%"
                                  + " |" + (previous == null ? "    -"
                                        : (moved * 100f / (Width * Height)).ToString("F1").PadLeft(4) + "%")
                                  + " |" + top.ToString("F0").PadLeft(7)
                                  + " |" + wide.ToString("F0").PadLeft(13));
                previous = rgb;
            }
        }

        private static byte[] Render(float t)
        {
            var rgb = new byte[Width * Height * 3];
            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 24; rgb[i + 1] = 32; rgb[i + 2] = 46;   // the sky
            }

            int groundPy = YToPixel(0f);
            if (groundPy >= 0 && groundPy < Height)
            {
                for (int px = 0; px < Width; px++)
                {
                    int at = (groundPy * Width + px) * 3;
                    rgb[at] = 60; rgb[at + 1] = 52; rgb[at + 2] = 44;
                }
            }

            // From the back (larger Z) to the front.
            var order = new int[PlumeParcels.Count];
            var depth = new float[PlumeParcels.Count];
            for (int i = 0; i < PlumeParcels.Count; i++)
            {
                order[i] = i;
                depth[i] = PlumeParcels.At(i, t, VentRadius, ColumnHeight,
                                           WindX, WindZ, Seed).Z;
            }
            Array.Sort(depth, order);

            for (int n = PlumeParcels.Count - 1; n >= 0; n--)
            {
                PlumeParcel p = PlumeParcels.At(order[n], t, VentRadius, ColumnHeight,
                                                WindX, WindZ, Seed);
                if (p.Alpha <= 0.01f) continue;
                Splat(rgb, p);
            }

            return rgb;
        }

        private static void Splat(byte[] rgb, PlumeParcel p)
        {
            int cx = XToPixel(p.X);
            int cy = YToPixel(p.Y);
            float pr = p.RadiusMetres / (ViewHalfWidth * 2f) * Width;
            if (pr < 1f) pr = 1f;

            // Black near the vent, sunlit grey-white higher up.
            float b = p.Brightness;
            float rr = 46f + 168f * b;
            float gg = 44f + 166f * b;
            float bb = 44f + 168f * b;

            int r0 = (int)pr;
            for (int dy = -r0; dy <= r0; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= Height) continue;

                for (int dx = -r0; dx <= r0; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= Width) continue;

                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / pr;
                    if (d >= 1f) continue;

                    // Soften the rim (the same idea as the game's cloud texture).
                    float soft = 1f - d * d;
                    float a = p.Alpha * soft * 0.55f;
                    if (a <= 0f) continue;
                    if (a > 1f) a = 1f;

                    int at = (y * Width + x) * 3;
                    rgb[at] = Mix(rgb[at], rr, a);
                    rgb[at + 1] = Mix(rgb[at + 1], gg, a);
                    rgb[at + 2] = Mix(rgb[at + 2], bb, a);
                }
            }
        }

        private static byte Mix(byte under, float over, float a)
        {
            float v = under * (1f - a) + over * a;
            if (v < 0f) v = 0f;
            if (v > 255f) v = 255f;
            return (byte)v;
        }

        private static int XToPixel(float metres)
        {
            return (int)((metres + ViewHalfWidth) / (ViewHalfWidth * 2f) * (Width - 1));
        }

        private static int YToPixel(float metres)
        {
            // Keep the vertical scale the same as the horizontal (stretch it and you misjudge
            // the shape).
            float perPixel = ViewHalfWidth * 2f / Width;
            return (Height - 40) - (int)(metres / perPixel);
        }
    }
}
