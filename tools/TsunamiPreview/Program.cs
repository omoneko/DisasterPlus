using System;
using System.IO;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.TsunamiPreview
{
    /// <summary>
    /// Look at the tsunami's <b>source</b> without launching the game.
    ///
    /// ── ★★ What can and cannot be drawn here (2026-08-29) ────────────────
    ///
    /// After the rewrite, all this tool can draw is <b>the force applied at the epicentre</b>.
    /// <b>The wall of water itself cannot be drawn</b> —— that is produced by the game's
    /// shallow-water solver (<c>WaterSimulation.SimulateWater</c>), which is not in our code.
    ///
    /// ★ The previous version drew "the shape of the wave" here and was satisfied with it.
    ///   **That was the mistake.** However pretty a shape you draw yourself, water that has
    ///   not gone through the solver does not move as a wave. What is drawn now is <b>the
    ///   input to the solver</b>, not its output.
    ///
    /// For comparison, the input the DLC tsunami applies to the sea surface at the outer ring
    /// (<c>WaterWave.GetSeaLevel</c>, measured from the IL) is drawn alongside it.
    /// **So that "stronger or weaker than the DLC" can be said on the same picture.**
    ///
    ///   dotnet run --project tools/TsunamiPreview -- docs/images/earthquake
    /// </summary>
    internal static class Program
    {
        private const int Width = 900;
        private const int Height = 420;

        /// <summary>A guide for game speed 1.</summary>
        private const float FramesPerRealSecond = 60f;

        private const byte Intensity = 100;

        // ── The DLC tsunami (measured from the IL and from the prefabs) ────────────────────────

        /// <summary>The value of <c>TsunamiAI.m_duration</c> measured from the prefab.</summary>
        private const int VanillaDurationField = 256;

        /// <summary><c>WaterWave.m_duration = m_duration &lt;&lt; 6</c>.</summary>
        private const int VanillaWaveDuration = VanillaDurationField << 6;

        /// <summary><c>m_currentTime</c> goes up by 64 per water step, and 1 water step ~= 1
        /// sim frame.</summary>
        private const int TicksPerFrame = 64;

        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, "tsunami-drive.png");
            Png.Write(path, Width, Height, Render());
            Console.WriteLine("wrote " + path);

            Table();
            Vanilla();
            return 0;
        }

        /// <summary>Print our force as numbers. Check **whether the signs follow the
        /// stages**.</summary>
        private static void Table()
        {
            int peak = TsunamiSource.VanillaDeltaUnits(Intensity) * 4;

            Console.WriteLine();
            Console.WriteLine("== the force applied at the epicentre (TYPE_IMPACT m_delta) ==");
            Console.WriteLine("  intensity " + Intensity + " -> peak " + peak
                              + " units = "
                              + (peak / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                              + " m of virtual sea-bed uplift");
            Console.WriteLine("  source radius " + TsunamiSource.RadiusMetres.ToString("F0")
                              + " m (" + TsunamiSource.RadiusCells + " cells)");
            Console.WriteLine();
            Console.WriteLine(" frame | real s | delta  | dir    | stage");

            foreach (float f in new[] { 0f, 60f, 150f, 300f, 350f, 400f, 600f,
                                        900f, 990f, 1079f, 1080f })
            {
                int d = TsunamiSource.DeltaAt(f, peak);
                string dirn = d < 0 ? "inward" : d > 0 ? "out   " : "none  ";

                Console.WriteLine(f.ToString("F0").PadLeft(6)
                    + " |" + (f / FramesPerRealSecond).ToString("F1").PadLeft(7)
                    + " |" + d.ToString().PadLeft(7)
                    + " | " + dirn
                    + " | " + TsunamiSource.StageAt(f));
            }
        }

        /// <summary>
        /// The input the DLC tsunami applies to the sea surface at the outer ring (exactly the
        /// formula measured from the IL).
        /// **Printed so that the scales can be matched up.**
        /// </summary>
        private static void Vanilla()
        {
            int delta = TsunamiSource.VanillaDeltaUnits(Intensity);

            Console.WriteLine();
            Console.WriteLine("== comparison: the input the DLC tsunami applies to the sea surface at the ring ==");
            Console.WriteLine("  m_delta " + delta + " units = "
                              + (delta / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                              + " m, and the source lasts only " + (VanillaWaveDuration / TicksPerFrame)
                              + " frames ("
                              + (VanillaWaveDuration / (float)TicksPerFrame
                                 / FramesPerRealSecond).ToString("F1")
                              + " real s)");
            Console.WriteLine();
            Console.WriteLine(" frame | real s | sea (m) | what is happening");

            for (int frame = 0; frame <= VanillaWaveDuration / TicksPerFrame; frame += 16)
            {
                float metres = VanillaSeaOffset(frame, delta)
                               / (float)TsunamiSource.UnitsPerMetre;

                string what = metres < -1f ? "drawback" : metres > 1f ? "crest" : "";

                Console.WriteLine(frame.ToString().PadLeft(6)
                    + " |" + (frame / FramesPerRealSecond).ToString("F1").PadLeft(7)
                    + " |" + metres.ToString("F1").PadLeft(8)
                    + " | " + what);
            }
        }

        /// <summary>
        /// The body of <c>WaterWave.GetSeaLevel</c> (IL_0072–00B6), copied exactly, for an
        /// outer-ring cell (phase = 0).
        /// </summary>
        private static int VanillaSeaOffset(int frame, int delta)
        {
            int currentTime = frame * TicksPerFrame;
            int t = currentTime;                       // phase 0
            if (t <= 0 || t >= VanillaWaveDuration) return 0;

            int amp = (int)((long)delta * (65536 - currentTime) >> 16);
            int d64 = VanillaWaveDuration >> 6;

            double cos = Math.Cos(2.0 * Math.PI * ((t * 1024.0) / d64) / 65536.0);
            double arg = amp - amp * cos;

            double sin = Math.Sin(2.0 * Math.PI * ((t * 1536.0) / d64) / 65536.0);

            // level = original - (arg*sin)/2  ==> the rise of the sea surface is -(arg*sin)/2
            return (int)(-(arg * sin) / 2.0);
        }

        private static byte[] Render()
        {
            var rgb = new byte[Width * Height * 3];

            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 16; rgb[i + 1] = 20; rgb[i + 2] = 28;
            }

            int mid = Height / 2;
            for (int x = 0; x < Width; x++) Set(rgb, x, mid, 70, 78, 92);

            int peak = TsunamiSource.VanillaDeltaUnits(Intensity) * 4;
            float span = TsunamiSource.TotalSteps * 1.15f;

            // The stage boundaries.
            foreach (float f in new[] { TsunamiSource.DrawInSteps,
                                        TsunamiSource.DrawInSteps + TsunamiSource.TurnSteps,
                                        TsunamiSource.PushSteps,
                                        TsunamiSource.TotalSteps })
            {
                int x = (int)(f / span * (Width - 1));
                for (int y = 0; y < Height; y += 3) Set(rgb, x, y, 48, 54, 66);
            }

            // Our force. Positive = outwards (up), negative = towards the centre (down).
            for (int x = 0; x < Width; x++)
            {
                float f = x / (float)(Width - 1) * span;
                int d = TsunamiSource.DeltaAt(f, peak);
                int y = mid - (int)(d / (float)peak * (mid - 20));

                for (int k = -1; k <= 1; k++)
                {
                    Set(rgb, x, y + k, d < 0 ? (byte)120 : (byte)90,
                        d < 0 ? (byte)190 : (byte)220,
                        d < 0 ? (byte)255 : (byte)200);
                }
            }

            // Overlay the DLC's input on the same vertical scale (dotted).
            int vanilla = TsunamiSource.VanillaDeltaUnits(Intensity);
            for (int x = 0; x < Width; x += 2)
            {
                float f = x / (float)(Width - 1) * span;
                int frame = (int)f;
                if (frame > VanillaWaveDuration / TicksPerFrame) break;

                int off = VanillaSeaOffset(frame, vanilla);
                int y = mid - (int)(off / (float)peak * (mid - 20));
                Set(rgb, x, y, 240, 170, 90);
            }

            return rgb;
        }

        private static void Set(byte[] rgb, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            int at = (y * Width + x) * 3;
            rgb[at] = r; rgb[at + 1] = g; rgb[at + 2] = b;
        }
    }
}
