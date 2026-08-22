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
    /// 噴石の弾道を**ゲームを起動せずに**描いて確かめる。
    /// 呼ぶのは <see cref="EjectaBallistics"/> の実物で、書き直した近似ではない。
    ///
    /// 2 枚出す:
    ///   - <c>ejecta-plan.png</c>    上から見た着弾点（山の輪郭と火口も描く）
    ///   - <c>ejecta-section.png</c> 横から見た弾道（山肌の断面と一緒に）
    /// </summary>
    internal static class Ballistics
    {
        private const int PlanPixels = 720;
        private const int SectionWidth = 900;
        private const int SectionHeight = 420;

        private const VolcanoForm Form = VolcanoForm.Strato;
        private const int Blasts = 6;

        public static void Report(string dir, StringBuilder log)
        {
            float r = VolcanoShape.DefaultRadiusOf(Form);
            float h = VolcanoShape.DefaultHeightOf(Form);
            float vent = VolcanoCrater.FloorMetresAt(h, h);

            uint seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));

            log.AppendLine("## ejecta (strato, R=" + F(r) + " m, H=" + F(h)
                           + " m, vent " + F(vent) + " m above the base)");
            log.AppendLine("  blocks per blast: " + EjectaBallistics.BlocksPerBlast(0f)
                           + " (weakest) .. " + EjectaBallistics.BlocksPerBlast(1f)
                           + " (strongest)");
            log.AppendLine("  bands           : range " + F1(EjectaBallistics.MinRangeFraction)
                           + " .. " + F1(EjectaBallistics.MaxRangeFraction) + " R, elevation "
                           + F(EjectaBallistics.MinElevationDegrees) + " .. "
                           + F(EjectaBallistics.MaxElevationDegrees) + " deg");

            Summary(log, seed, r, h, vent, 1f, "strongest");
            Summary(log, seed, r, h, vent, 0.35f, "weak");

            Plan(dir, seed, r, h, vent);
            Section(dir, seed, r, h, vent);
            log.AppendLine();
        }

        private static void Summary(StringBuilder log, uint seed, float r, float h,
                                    float vent, float unit, string label)
        {
            float shortest = float.MaxValue, longest = 0f, sum = 0f;
            float longestFlight = 0f;
            int n = 0;
            int beyond = 0;

            for (int b = 0; b < Blasts * 4; b++)
            {
                int count = EjectaBallistics.BlocksPerBlast(unit);
                for (int i = 0; i < count; i++)
                {
                    EjectaBlock block = EjectaBallistics.Plan(seed, b, i, unit, Form, r, h, vent);
                    if (!block.Valid) continue;

                    if (block.RangeMetres < shortest) shortest = block.RangeMetres;
                    if (block.RangeMetres > longest) longest = block.RangeMetres;
                    if (block.FlightSeconds > longestFlight) longestFlight = block.FlightSeconds;
                    if (block.RangeMetres > r) beyond++;
                    sum += block.RangeMetres;
                    n++;
                }
            }

            if (n == 0) { log.AppendLine("  " + label + ": no blocks"); return; }

            log.AppendLine("  " + label.PadRight(10) + ": range " + F(shortest) + " .. "
                           + F(longest) + " m (mean " + F(sum / n) + "), "
                           + (beyond * 100 / n) + " % land beyond the foot, longest flight "
                           + F1(longestFlight) + " s");
            // ★ 飛んでいる間に地面へ潜っていないか。潜っていたら「山肌に落ちる」が嘘になる。
            int buried = 0;
            float deepest = 0f;
            for (int b = 0; b < Blasts * 4; b++)
            {
                int count = EjectaBallistics.BlocksPerBlast(unit);
                for (int i = 0; i < count; i++)
                {
                    EjectaBlock block = EjectaBallistics.Plan(seed, b, i, unit, Form, r, h, vent);
                    if (!block.Valid) continue;

                    for (float t = 0.1f; t < block.FlightSeconds; t += 0.1f)
                    {
                        float dx, dy, dz;
                        EjectaBallistics.OffsetAt(block, t, out dx, out dy, out dz);
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        float ground = EjectaBallistics.GroundAt(Form, d, r, h);
                        float below = ground - (vent + dy);
                        if (below > 1f)
                        {
                            buried++;
                            if (below > deepest) deepest = below;
                            break;
                        }
                    }
                }
            }
            log.AppendLine("            " + "".PadRight(2) + ": " + buried + " of " + n
                           + " blocks pass through the ground (deepest " + F(deepest) + " m)");

        }

        /// <summary>上から見た着弾点。山の輪郭・火口・裾の外まで飛んだ岩が分かる。</summary>
        private static void Plan(string dir, uint seed, float r, float h, float vent)
        {
            int n = PlanPixels;
            var rgb = new byte[n * n * 3];
            float span = r * 2.6f;               // 画像の一辺（m）
            float scale = n / span;              // px / m

            float crater = VolcanoShape.CraterRadiusOf(r);

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float mx = (x - n * 0.5f) / scale;
                    float mz = (y - n * 0.5f) / scale;
                    float d = (float)Math.Sqrt(mx * mx + mz * mz);

                    float ground = EjectaBallistics.GroundAt(Form, d, r, h);
                    float t = ground / h;

                    byte v = (byte)(38 + 150 * t);
                    int i = (y * n + x) * 3;
                    rgb[i] = (byte)(v * 0.72f);
                    rgb[i + 1] = (byte)(v * 0.78f);
                    rgb[i + 2] = (byte)(v * 0.60f);

                    // 山の輪郭と火口の縁を薄く引く。
                    if (Math.Abs(d - r) < 1.5f / scale * 2f) { rgb[i] = 90; rgb[i + 1] = 90; rgb[i + 2] = 90; }
                    if (Math.Abs(d - crater) < 1.5f / scale * 2f) { rgb[i] = 120; rgb[i + 1] = 80; rgb[i + 2] = 60; }
                }
            }

            for (int b = 0; b < Blasts; b++)
            {
                int count = EjectaBallistics.BlocksPerBlast(1f);
                for (int i = 0; i < count; i++)
                {
                    EjectaBlock block = EjectaBallistics.Plan(seed, b, i, 1f, Form, r, h, vent);
                    if (!block.Valid) continue;

                    // 弾道を薄い点で、着弾点を大きく。
                    for (float t = 0f; t < block.FlightSeconds; t += 0.35f)
                    {
                        float dx, dy, dz;
                        EjectaBallistics.OffsetAt(block, t, out dx, out dy, out dz);
                        Dot(rgb, n, scale, dx, dz, 1, 0xC08040);
                    }

                    float lx, ly, lz;
                    EjectaBallistics.OffsetAt(block, block.FlightSeconds, out lx, out ly, out lz);
                    int size = 2 + (int)(block.SizeUnit * 4f);
                    Dot(rgb, n, scale, lx, lz, size, 0xFF3818);
                }
            }

            Png.Write(Path.Combine(dir, "ejecta-plan.png"), n, n, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, "ejecta-plan.png")
                              + "  (plan view, " + F(span) + " m across)");
        }

        /// <summary>横から見た弾道。山肌の断面と一緒に描く。</summary>
        private static void Section(string dir, uint seed, float r, float h, float vent)
        {
            var rgb = new byte[SectionWidth * SectionHeight * 3];
            for (int i = 0; i < rgb.Length; i += 3)
            {
                rgb[i] = 24; rgb[i + 1] = 26; rgb[i + 2] = 30;
            }

            float spanX = r * 2.4f;
            float spanY = h * 2.4f;
            float sx = SectionWidth / spanX;
            float sy = SectionHeight / spanY;

            // 山肌（中心から右へ）。
            for (int px = 0; px < SectionWidth; px++)
            {
                float d = px / sx;
                float ground = EjectaBallistics.GroundAt(Form, d, r, h);
                int py = SectionHeight - 1 - (int)(ground * sy);
                for (int y = py; y < SectionHeight; y++) Set(rgb, SectionWidth, px, y, 0x38402C);
            }

            for (int b = 0; b < Blasts; b++)
            {
                int count = EjectaBallistics.BlocksPerBlast(1f);
                for (int i = 0; i < count; i++)
                {
                    EjectaBlock block = EjectaBallistics.Plan(seed, b, i, 1f, Form, r, h, vent);
                    if (!block.Valid) continue;

                    int colour = block.SizeUnit > 0.6f ? 0xFF6020 : 0xFFC060;
                    for (float t = 0f; t < block.FlightSeconds; t += 0.12f)
                    {
                        float dx, dy, dz;
                        EjectaBallistics.OffsetAt(block, t, out dx, out dy, out dz);

                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        int px = (int)(d * sx);
                        int py = SectionHeight - 1 - (int)((vent + dy) * sy);
                        Set(rgb, SectionWidth, px, py, colour);
                    }
                }
            }

            Png.Write(Path.Combine(dir, "ejecta-section.png"), SectionWidth, SectionHeight, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, "ejecta-section.png")
                              + "  (section, " + F(spanX) + " x " + F(spanY) + " m)");
        }

        private static void Dot(byte[] rgb, int n, float scale, float mx, float mz,
                                int size, int colour)
        {
            int cx = (int)(n * 0.5f + mx * scale);
            int cy = (int)(n * 0.5f + mz * scale);
            for (int y = cy - size; y <= cy + size; y++)
            {
                for (int x = cx - size; x <= cx + size; x++) Set(rgb, n, x, y, colour);
            }
        }

        private static void Set(byte[] rgb, int width, int x, int y, int colour)
        {
            if (x < 0 || y < 0 || x >= width) return;
            int i = (y * width + x) * 3;
            if (i < 0 || i + 2 >= rgb.Length) return;
            rgb[i] = (byte)(colour >> 16);
            rgb[i + 1] = (byte)(colour >> 8);
            rgb[i + 2] = (byte)colour;
        }

        private static string F(double v) { return v.ToString("F0", CultureInfo.InvariantCulture); }
        private static string F1(double v) { return v.ToString("F1", CultureInfo.InvariantCulture); }
    }
}
