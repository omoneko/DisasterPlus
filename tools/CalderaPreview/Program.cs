using System;
using System.IO;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.CalderaPreview
{
    /// <summary>
    /// Draws and checks the terrain cross-sections of the four stages of a caldera-forming
    /// eruption **without launching the game** (2026-08-22, the owner's remark: "when a
    /// caldera forms, shouldn't the edifice collapse dramatically and blow up...?").
    ///
    /// This is the tool for this project's rule that "visual changes are drawn and measured
    /// offline by yourself before asking for a test in the game". It compiles and calls
    /// <b>the real thing from Core</b> directly, so it is not a rewritten approximation.
    ///
    ///   dotnet run --project tools/CalderaPreview -- docs/images/volcano
    /// </summary>
    internal static class Program
    {
        // The real size of a stratovolcano at the top of the slider (displayed as 25.5).
        // VolcanoState works the radius back from the caldera side, so the cone comes out
        // this size.
        private const float ConeRadius = 2928f;
        private const float ConeHeight = 1000f;
        private const float Ground = 120f;

        private const int Width = 1100;
        private const int Height = 520;

        /// <summary>The horizontal extent shown (m, half-width). Wide enough to include the
        /// area beyond the caldera.</summary>
        private const float ViewHalfWidth = 7000f;

        private const uint Seed = 20260822u;

        /// <summary>Sea level (m). Measured from
        /// <c>WaterSimulation.DEFAULT_SEA_LEVEL</c>.</summary>
        private const float SeaLevel = 40f;

        private static readonly VolcanoRelief Flat =
            VolcanoRelief.For(VolcanoForm.Strato, Seed, 0f);

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            float calderaR = SuperEruption.CalderaRadiusMetres(ConeRadius);
            float depth = SuperEruption.CalderaDepthMetres(ConeHeight);
            float bulgeR = SuperEruption.InflationRadiusMetres(ConeRadius);
            float bulgeH = SuperEruption.InflationHeightMetres(ConeHeight);

            Console.WriteLine("cone     r=" + ConeRadius.ToString("F0")
                              + " h=" + ConeHeight.ToString("F0"));
            Console.WriteLine("bulge    r=" + bulgeR.ToString("F0")
                              + " +" + bulgeH.ToString("F0"));
            Console.WriteLine("caldera  r=" + calderaR.ToString("F0")
                              + " floor=" + (Ground - depth).ToString("F0")
                              + " (ground was " + Ground.ToString("F0")
                              + ", sea level " + SeaLevel.ToString("F0") + ")");

            // ★ **Always state** whether the floor is above or below sea level. It used to be
            //   below without exception (the owner's question: "what is the reason it always
            //   ends up below sea level?").
            float floor = Ground - depth;
            Console.WriteLine(floor >= SeaLevel
                ? "         the caldera floor stays ABOVE sea level (dry caldera)"
                : "         the caldera floor is " + (SeaLevel - floor).ToString("F0")
                  + " m below sea level (it will flood, like Santorini)");

            string path = Path.Combine(dir, "caldera-cross-section.png");
            Png.Write(path, Width, Height, Render(calderaR, depth, bulgeR, bulgeH));
            Console.WriteLine("wrote " + path);

            PrintProfile(calderaR, depth, bulgeR, bulgeH);
            return 0;
        }

        /// <summary>The ground height (m) after that stage. The distance is from the centre
        /// (unsigned).</summary>
        private static float Stage1Cone(float d)
        {
            return Ground + VolcanoCrater.ProfileAt(Flat, d, 0f, ConeRadius, ConeHeight);
        }

        private static float Stage2Bulge(float d, float bulgeR, float bulgeH)
        {
            return Stage1Cone(d) + SuperEruption.InflationAt(d, bulgeR, bulgeH);
        }

        private static float Stage4Caldera(float d, float calderaR, float depth,
                                           float bulgeR, float bulgeH)
        {
            // ★ The collapse drops from "the ground after the inflation" (the same order as
            //   the game).
            float baseMetres = Stage2Bulge(d, bulgeR, bulgeH);

            // ★ The floor is not just a bowl —— collapsed blocks and a central cone sit on it.
            float offset = SuperEruption.CalderaFloorOffsetAt(d, 0f, calderaR, depth, Seed);

            // ★ Outside the edifice the reference is **that cell's real ground** (so the
            //   original terrain is preserved). The same rule as the game's
            //   VolcanoUplift.ReferenceGroundFor.
            float reference = ReferenceGround(d, baseMetres);

            return baseMetres + SuperEruption.FounderDropAt(offset, baseMetres, reference);
        }

        /// <summary>The same rule as the game's
        /// <c>VolcanoUplift.ReferenceGroundFor</c>.</summary>
        private static float ReferenceGround(float distance, float baseMetres)
        {
            float band = ConeRadius * 0.25f;
            if (distance <= ConeRadius) return Ground;
            if (distance >= ConeRadius + band) return baseMetres;

            float t = (distance - ConeRadius) / band;
            float k = t * t * (3f - 2f * t);
            return Ground + (baseMetres - Ground) * k;
        }

        private static void PrintProfile(float calderaR, float depth,
                                         float bulgeR, float bulgeH)
        {
            Console.WriteLine();
            Console.WriteLine("   dist |   cone |  bulge | caldera");
            for (int i = 0; i <= 14; i++)
            {
                float d = ViewHalfWidth * i / 14f;
                Console.WriteLine(d.ToString("F0").PadLeft(7)
                                  + " |" + Stage1Cone(d).ToString("F0").PadLeft(7)
                                  + " |" + Stage2Bulge(d, bulgeR, bulgeH).ToString("F0").PadLeft(7)
                                  + " |" + Stage4Caldera(d, calderaR, depth, bulgeR, bulgeH)
                                               .ToString("F0").PadLeft(8));
            }
        }

        private static byte[] Render(float calderaR, float depth, float bulgeR, float bulgeH)
        {
            var rgb = new byte[Width * Height * 3];

            // The vertical extent is set from "the highest point" and "the floor".
            float top = Ground + ConeHeight + bulgeH + 150f;
            float bottom = Ground - depth - 150f;

            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 16; rgb[i + 1] = 18; rgb[i + 2] = 24;
            }

            // The line of the original ground (the reference).
            PlotLine(rgb, top, bottom, d => Ground, 70, 70, 80);

            // The three cross-sections.
            PlotLine(rgb, top, bottom, Stage1Cone, 210, 140, 90);
            PlotLine(rgb, top, bottom, d => Stage2Bulge(d, bulgeR, bulgeH), 240, 200, 90);
            PlotLine(rgb, top, bottom,
                     d => Stage4Caldera(d, calderaR, depth, bulgeR, bulgeH), 235, 90, 70);

            return rgb;
        }

        private static void PlotLine(byte[] rgb, float top, float bottom,
                                     Func<float, float> heightAt, byte r, byte g, byte b)
        {
            int previous = -1;
            for (int px = 0; px < Width; px++)
            {
                float worldX = (px / (float)(Width - 1) * 2f - 1f) * ViewHalfWidth;
                float d = worldX < 0f ? -worldX : worldX;
                float h = heightAt(d);

                int py = (int)((top - h) / (top - bottom) * (Height - 1));
                if (py < 0) py = 0;
                if (py >= Height) py = Height - 1;

                // Fill in where it jumps vertically (a cliff) as well —— with isolated points
                // the wall would not be visible.
                int from = previous < 0 ? py : previous;
                int lo = from < py ? from : py;
                int hi = from < py ? py : from;
                for (int y = lo; y <= hi; y++)
                {
                    int at = (y * Width + px) * 3;
                    rgb[at] = r; rgb[at + 1] = g; rgb[at + 2] = b;
                }
                previous = py;
            }
        }
    }
}
