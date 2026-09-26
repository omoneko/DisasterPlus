using System;
using System.IO;
using DisasterPlus.Core.Typhoon;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.TyphoonParcelPreview
{
    /// <summary>
    /// Draws the clouds of <see cref="TyphoonCloudParcels"/> from above and checks them
    /// **without launching the game** (2026-08-22, the owner's instruction: "the volcano's
    /// eruption cloud has the right texture for it, so please build the typhoon's cloud by
    /// applying the eruption cloud effect").
    ///
    /// It is viewed from above because that is the only place the typhoon's shape (the eye,
    /// the eyewall and the spiral arms) shows up.
    /// It compiles and calls <b>the real thing from Core</b> directly, so it is not a
    /// rewritten approximation.
    ///
    ///   dotnet run --project tools/TyphoonParcelPreview -- docs/images/typhoon
    /// </summary>
    internal static class Program
    {
        private const int Size = 820;

        /// <summary>Outer radius of the vortex (m). Close to the game's default.</summary>
        private const float Radius = 4200f;

        /// <summary>The extent shown (m, half-width). Wide enough to include the area beyond
        /// the vortex.</summary>
        private const float ViewHalf = 5200f;

        private const uint Seed = 20260822u;

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            foreach (float t in new[] { 90f, 130f, 170f })
            {
                string path = Path.Combine(dir, "typhoon-parcels-t" + ((int)t) + ".png");
                Png.Write(path, Size, Size, Render(t));
                Console.WriteLine("wrote " + path);
            }

            Measure();
            return 0;
        }

        /// <summary>
        /// Do not just look at it. Things to **confirm with numbers**:
        /// the fraction covered, whether it moves over time, and <b>whether the eye is
        /// open</b>.
        /// </summary>
        private static void Measure()
        {
            Console.WriteLine();
            Console.WriteLine("   t |  cover | moved | eye cover");

            byte[] previous = null;
            foreach (float t in new[] { 90f, 91f, 130f, 170f })
            {
                byte[] rgb = Render(t);

                int lit = 0, moved = 0;
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    if (rgb[i] > 40) lit++;
                    if (previous != null)
                    {
                        int d = rgb[i] - previous[i];
                        if (d < 0) d = -d;
                        if (d > 12) moved++;
                    }
                }

                // How much the eye (the centre) is filled in. Confirms **that it is open**.
                int eyeLit = 0, eyeAll = 0;
                float eyePx = TyphoonCloudParcels.EyeFraction * Radius / ViewHalf * (Size / 2f);
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float dx = x - Size / 2f, dy = y - Size / 2f;
                        if (dx * dx + dy * dy > eyePx * eyePx) continue;
                        eyeAll++;
                        if (rgb[(y * Size + x) * 3] > 40) eyeLit++;
                    }
                }

                Console.WriteLine(t.ToString("F0").PadLeft(4)
                    + " |" + (lit * 100f / (Size * Size)).ToString("F1").PadLeft(6) + "%"
                    + " |" + (previous == null ? "    -"
                        : (moved * 100f / (Size * Size)).ToString("F1").PadLeft(4) + "%")
                    + " |" + (eyeAll > 0 ? (eyeLit * 100f / eyeAll).ToString("F1") : "?")
                        .PadLeft(9) + "%");
                previous = rgb;
            }
        }

        private static byte[] Render(float t)
        {
            var rgb = new byte[Size * Size * 3];
            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 20; rgb[i + 1] = 40; rgb[i + 2] = 62;   // the sea
            }

            // Paint the lower parcels first (so the higher ones end up on top).
            var order = new int[TyphoonCloudParcels.Count];
            var height = new float[TyphoonCloudParcels.Count];
            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                order[i] = i;
                height[i] = TyphoonCloudParcels.At(i, t, Radius, 0f, Seed).Y;
            }
            Array.Sort(height, order);

            for (int n = 0; n < TyphoonCloudParcels.Count; n++)
            {
                TyphoonParcel p = TyphoonCloudParcels.At(order[n], t, Radius, 0f, Seed);
                if (p.Alpha <= 0.01f) continue;
                Splat(rgb, p);
            }

            return rgb;
        }

        private static void Splat(byte[] rgb, TyphoonParcel p)
        {
            int cx = ToPixel(p.X);
            int cy = ToPixel(p.Z);
            float pr = p.RadiusMetres / (ViewHalf * 2f) * Size;
            if (pr < 1f) pr = 1f;

            float b = p.Brightness;
            float rr = 132f + 110f * b;
            float gg = 136f + 108f * b;
            float bb = 146f + 102f * b;

            int r0 = (int)pr;
            for (int dy = -r0; dy <= r0; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= Size) continue;

                for (int dx = -r0; dx <= r0; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= Size) continue;

                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / pr;
                    if (d >= 1f) continue;

                    float a = p.Alpha * (1f - d * d) * 0.5f;
                    if (a <= 0f) continue;
                    if (a > 1f) a = 1f;

                    int at = (y * Size + x) * 3;
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

        private static int ToPixel(float metres)
        {
            return (int)((metres + ViewHalf) / (ViewHalf * 2f) * (Size - 1));
        }
    }
}
