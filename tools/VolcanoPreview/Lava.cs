using System;
using System.Globalization;
using System.IO;
using System.Text;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// 溶岩の帯を**そのまま描く**（実機の指摘④の確認）。ゲームは起動しない。
    ///
    /// 描いているのは <c>Core/Volcano/LavaGlow</c> が返す色と不透明度そのもので、
    /// <c>Game/Volcano/VolcanoLavaFx.BuildTexture</c> が焼くテクスチャと
    /// **同じ式・同じ大きさ・同じ並び**である（<c>u</c> が帯を横切る向き、
    /// <c>v</c> が帯に沿う向きで <c>v=0</c> が火口、<c>v=1</c> が前進端）。
    ///
    /// 2 枚出す:
    /// <list type="number">
    /// <item><c>lava-band.png</c> —— 帯 1 本を上から見た図。**時間の引数は無い**ので、
    ///   これがそのまま「動かない絵」である（点滅しない）</item>
    /// <item><c>lava-cooling.png</c> —— 冷え具合 1.0 / 0.6 / 0.3 / 0.1 / 0.0 の 5 枚。
    ///   **いちばん右がまっさらな地面**であることが指摘④の後半の確認である</item>
    /// </list>
    /// </summary>
    internal static class Lava
    {
        /// <summary>帯の解像度（**実機のテクスチャと同じ値を Core から取る**）。</summary>
        private const int Texture = LavaGlow.TextureSize;

        /// <summary>1 セルを何ピクセルで描くか。</summary>
        private const int Zoom = 3;

        /// <summary>焦げた地面の色（<c>BurnGround</c> の跡）。</summary>
        private static readonly float[] Ground = { 0.16f, 0.14f, 0.12f };

        internal static void Report(string dir, StringBuilder log)
        {
            log.AppendLine("## lava band (glow is a function of place, never of time)");
            log.AppendLine("  v = 0 is the vent, v = 1 is the advancing front");
            log.AppendLine("  cool | mean brightness | max brightness");

            float[] cools = { 1.0f, 0.6f, 0.3f, 0.1f, 0.0f };

            int w = Texture * Zoom;
            int h = Texture * Zoom;
            var sheet = new byte[(w * cools.Length + (cools.Length - 1) * 8) * h * 3];
            int sheetW = w * cools.Length + (cools.Length - 1) * 8;
            for (int i = 0; i < sheet.Length; i++) sheet[i] = 0xFF;

            for (int c = 0; c < cools.Length; c++)
            {
                float mean, max;
                byte[] frame = Draw(cools[c], out mean, out max);

                log.AppendLine("   " + cools[c].ToString("F2", CultureInfo.InvariantCulture)
                               + " |      " + mean.ToString("F3", CultureInfo.InvariantCulture)
                               + "      |     " + max.ToString("F3", CultureInfo.InvariantCulture));

                if (c == 0)
                {
                    Png.Write(Path.Combine(dir, "lava-band.png"), w, h, frame);
                    Console.WriteLine("wrote " + Path.Combine(dir, "lava-band.png"));
                }

                int ox = c * (w + 8);
                for (int y = 0; y < h; y++)
                {
                    Array.Copy(frame, y * w * 3, sheet, (y * sheetW + ox) * 3, w * 3);
                }
            }

            Png.Write(Path.Combine(dir, "lava-cooling.png"), sheetW, h, sheet);
            Console.WriteLine("wrote " + Path.Combine(dir, "lava-cooling.png"));
            log.AppendLine();
        }

        /// <summary>
        /// 帯 1 本。**上が火口、下が前進端**（<c>v</c> を縦に取る）。
        /// 焦げた地面の上に、色 × 不透明度で重ねる。
        /// </summary>
        private static byte[] Draw(float coolUnit, out float mean, out float max)
        {
            var rgb = new byte[Texture * Zoom * Texture * Zoom * 3];
            float fade = LavaGlow.CoolFade(coolUnit);

            double sum = 0;
            max = 0f;

            for (int y = 0; y < Texture; y++)
            {
                float v = y / (float)(Texture - 1);
                for (int x = 0; x < Texture; x++)
                {
                    float u = x / (float)(Texture - 1);

                    float glow = LavaGlow.GlowUnit(u, v);
                    float alpha = LavaGlow.AcrossFalloff(u) * (0.30f + 0.70f * glow);

                    float g2 = glow * glow;
                    float pr = (0.10f + 0.90f * glow) * fade;
                    float pg = (0.02f + 0.62f * g2) * fade * 0.55f;
                    float pb = (0.01f + 0.22f * g2 * glow) * fade * 0.2f;
                    float pa = alpha * fade;

                    float r = Ground[0] + (pr - Ground[0]) * pa;
                    float g = Ground[1] + (pg - Ground[1]) * pa;
                    float b = Ground[2] + (pb - Ground[2]) * pa;

                    float brightness = (pr + pg + pb) * pa / 3f;
                    sum += brightness;
                    if (brightness > max) max = brightness;

                    for (int zy = 0; zy < Zoom; zy++)
                    {
                        int row = (y * Zoom + zy) * Texture * Zoom;
                        for (int zx = 0; zx < Zoom; zx++)
                        {
                            int o = (row + x * Zoom + zx) * 3;
                            rgb[o] = Byte(r); rgb[o + 1] = Byte(g); rgb[o + 2] = Byte(b);
                        }
                    }
                }
            }

            mean = (float)(sum / (Texture * Texture));
            return rgb;
        }

        private static byte Byte(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }
    }
}
