using System;
using System.IO;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.TsunamiPreview
{
    /// <summary>
    /// 津波の<b>発生源</b>を、ゲームを起動せずに見る。
    ///
    /// ── ★★ ここで描けるもの・描けないもの（2026-08-29）────────────────
    ///
    /// 作り直したあと、このツールが描けるのは<b>震源に与える外力</b>だけである。
    /// <b>水の壁そのものは描けない</b> —— それを作るのはゲームの浅水ソルバ
    /// （<c>WaterSimulation.SimulateWater</c>）で、こちらのコードには無いからである。
    ///
    /// ★ 前の版はここで「波の形」を描いて満足していた。**それが間違いだった。**
    ///   きれいな形を自分で描いても、ソルバを通っていない水は波として動かない。
    ///   いま描いているのは<b>ソルバへの入力</b>で、出力ではない。
    ///
    /// 比較対象として、DLC の津波が外周の海面に与えている入力
    /// （<c>WaterWave.GetSeaLevel</c>、IL 実測）も並べて描く。
    /// **同じ絵の上で「DLC より強いのか弱いのか」を言えるようにするため。**
    ///
    ///   dotnet run --project tools/TsunamiPreview -- docs/images/earthquake
    /// </summary>
    internal static class Program
    {
        private const int Width = 900;
        private const int Height = 420;

        /// <summary>ゲーム速度 1 の目安。</summary>
        private const float FramesPerRealSecond = 60f;

        private const byte Intensity = 100;

        // ── DLC の津波（IL 実測、プレハブ実測値）────────────────────────

        /// <summary><c>TsunamiAI.m_duration</c> のプレハブ実測値。</summary>
        private const int VanillaDurationField = 256;

        /// <summary><c>WaterWave.m_duration = m_duration &lt;&lt; 6</c>。</summary>
        private const int VanillaWaveDuration = VanillaDurationField << 6;

        /// <summary><c>m_currentTime</c> は毎水ステップ +64、1 水ステップ ≒ 1 sim フレーム。</summary>
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

        /// <summary>こちらの外力を数で出す。**符号が段どおりか**を見る。</summary>
        private static void Table()
        {
            int peak = TsunamiSource.PeakDeltaUnits(Intensity);

            Console.WriteLine();
            Console.WriteLine("== 震源に与える外力（TYPE_IMPACT の m_delta）==");
            Console.WriteLine("  intensity " + Intensity + " -> peak " + peak
                              + " units = "
                              + (peak / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                              + " m の仮想的な海底隆起");
            Console.WriteLine("  source radius " + TsunamiSource.RadiusMetres.ToString("F0")
                              + " m (" + TsunamiSource.RadiusCells + " cells)");
            Console.WriteLine();
            Console.WriteLine(" frame | real s | delta  | 向き   | stage");

            foreach (float f in new[] { 0f, 60f, 150f, 300f, 350f, 400f, 600f,
                                        900f, 990f, 1079f, 1080f })
            {
                int d = TsunamiSource.DeltaAt(f, peak);
                string dirn = d < 0 ? "中心へ" : d > 0 ? "外へ  " : "なし  ";

                Console.WriteLine(f.ToString("F0").PadLeft(6)
                    + " |" + (f / FramesPerRealSecond).ToString("F1").PadLeft(7)
                    + " |" + d.ToString().PadLeft(7)
                    + " | " + dirn
                    + " | " + TsunamiSource.StageAt(f));
            }
        }

        /// <summary>
        /// DLC の津波が外周の海面に与える入力（IL 実測の式そのまま）。
        /// **目盛りを合わせるために出す。**
        /// </summary>
        private static void Vanilla()
        {
            int delta = TsunamiSource.VanillaDeltaUnits(Intensity);

            Console.WriteLine();
            Console.WriteLine("== 比較: DLC の津波が外周の海面に与える入力 ==");
            Console.WriteLine("  m_delta " + delta + " units = "
                              + (delta / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                              + " m、発生源は " + (VanillaWaveDuration / TicksPerFrame)
                              + " フレーム（"
                              + (VanillaWaveDuration / (float)TicksPerFrame
                                 / FramesPerRealSecond).ToString("F1")
                              + " 実秒）だけ");
            Console.WriteLine();
            Console.WriteLine(" frame | real s | 海面(m) | 何が起きているか");

            for (int frame = 0; frame <= VanillaWaveDuration / TicksPerFrame; frame += 16)
            {
                float metres = VanillaSeaOffset(frame, delta)
                               / (float)TsunamiSource.UnitsPerMetre;

                string what = metres < -1f ? "引き波" : metres > 1f ? "押し波" : "";

                Console.WriteLine(frame.ToString().PadLeft(6)
                    + " |" + (frame / FramesPerRealSecond).ToString("F1").PadLeft(7)
                    + " |" + metres.ToString("F1").PadLeft(8)
                    + " | " + what);
            }
        }

        /// <summary>
        /// <c>WaterWave.GetSeaLevel</c> の中身（IL_0072–00B6）を、
        /// 外周セル（phase = 0）についてそのまま写したもの。
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

            // level = original - (arg*sin)/2  ==> 海面の上がりは -(arg*sin)/2
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

            int peak = TsunamiSource.PeakDeltaUnits(Intensity);
            float span = TsunamiSource.TotalFrames * 1.15f;

            // 段の境目。
            foreach (float f in new[] { TsunamiSource.DrawInFrames,
                                        TsunamiSource.DrawInFrames + TsunamiSource.TurnFrames,
                                        TsunamiSource.PushFrames,
                                        TsunamiSource.TotalFrames })
            {
                int x = (int)(f / span * (Width - 1));
                for (int y = 0; y < Height; y += 3) Set(rgb, x, y, 48, 54, 66);
            }

            // こちらの外力。正＝外へ（上）、負＝中心へ（下）。
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

            // DLC の入力を同じ縦目盛りで重ねる（点線）。
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
