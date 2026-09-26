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
    /// Draws the ash-cloud fan **seen from above** (checking point 5 from the in-game report).
    /// The game is not launched.
    ///
    /// Three things are overlaid in one image:
    /// <list type="number">
    /// <item>the mountain (the shading of <c>VolcanoCrater.ProfileAt</c>)</item>
    /// <item><b>the lava trails</b> —— the real <c>LavaPath</c>, actually run down the
    ///   mountain's slope</item>
    /// <item><b>the area the fan covers over one full cycle</b> —— the bands of
    ///   <c>PyroclasticSurge</c>'s 5 lobes, summed over the whole time the head travels from
    ///   the crater to the far end</item>
    /// </list>
    ///
    /// Point 5 was "it only flows down on top of the lava flows". So what is being checked is
    /// <b>that the grey (the fan) covers a far wider area than the red (the lava)</b>.
    /// It is also printed as numbers (the surge table in <c>measurements.txt</c>).
    /// </summary>
    internal static class Surge
    {
        /// <summary>Metres per pixel.</summary>
        private const float MetresPerPixel = 8f;

        /// <summary>How much margin to leave outside the mountain (as a fraction of the
        /// radius).</summary>
        private const float Margin = 1.35f;

        /// <summary>Number of lava flows (the default of
        /// <c>ModSettings.VolcanoLavaFlows</c>).</summary>
        private const int LavaFlows = 4;

        /// <summary>Step at which the slope is read (m). 4 m, the same as the game's detail
        /// cell.</summary>
        private const float SampleMetres = 4f;

        internal static void Report(string dir, StringBuilder log)
        {
            const VolcanoForm form = VolcanoForm.Strato;
            float radius = VolcanoShape.DefaultRadiusOf(form);
            float height = VolcanoShape.DefaultHeightOf(form);
            var relief = VolcanoRelief.For(form,
                DeterministicRandom.Hash(275u, unchecked((uint)(-283))), 1f);

            uint seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));
            float crater = VolcanoShape.CraterRadiusOf(radius);
            var vent = new Vec2(0f, 0f);

            int half = (int)(radius * Margin / MetresPerPixel);
            int n = half * 2 + 1;

            // ── Actually run the lava downhill (the real LavaPath) ──────────────────
            var trails = new Vec2[LavaFlows][];
            var counts = new int[LavaFlows];
            float ventRadius = LavaPath.VentRadiusMetres(crater);

            for (int f = 0; f < LavaFlows; f++)
            {
                Vec2 out0 = LavaPath.InitialDirection(seed, f, LavaFlows);
                var head = new Vec2(vent.X + out0.X * ventRadius, vent.Z + out0.Z * ventRadius);

                var trail = new Vec2[LavaPath.MaxSteps];
                int count = 0;
                trail[count++] = head;

                for (int s = 0; s < LavaPath.MaxSteps - 1; s++)
                {
                    float dx, dz;
                    Slope(relief, head, radius, height, out dx, out dz);

                    Vec2 next;
                    if (!LavaPath.NextPosition(head, new Vec2(-dx, -dz), LavaPath.StepMetres,
                                               out next))
                    {
                        break;
                    }

                    head = next;
                    trail[count++] = head;
                    if (Math.Sqrt(head.X * head.X + head.Z * head.Z) > radius * Margin) break;
                }

                trails[f] = trail;
                counts[f] = count;
            }

            // ── The area the fan covers over one full cycle ────────────────────────────────
            var bearings = new float[LavaFlows];
            int channels = 0;
            for (int f = 0; f < LavaFlows; f++)
            {
                float bearing;
                if (PyroclasticSurge.TryBearing(trails[f], 0, counts[f], vent, out bearing))
                {
                    bearings[channels++] = bearing;
                }
            }

            var surge = new float[n * n];
            var lava = new float[n * n];

            for (int f = 0; f < LavaFlows; f++)
            {
                for (int i = 0; i < counts[f]; i++)
                {
                    Stamp(lava, n, half, trails[f][i],
                          LavaPath.SpreadRadiusFor(i * LavaPath.StepMetres));
                }
            }

            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                float reach = PyroclasticSurge.ReachMetres(radius, 1f, seed, i);
                float azimuth = PyroclasticSurge.LobeAzimuth(seed, i, PyroclasticSurge.LobeCount);

                bool found;
                float channel = PyroclasticSurge.NearestChannel(azimuth, bearings, channels,
                                                                out found);
                if (!found) channel = azimuth;

                for (float head = 0f; head <= reach + PyroclasticSurge.BandLengthMetres;
                     head += 20f)
                {
                    Vec2 a, b, c, d;
                    if (!PyroclasticSurge.TryLobe(vent, azimuth, channel, reach, head,
                                                  out a, out b, out c, out d))
                    {
                        continue;
                    }

                    float halfWidth = PyroclasticSurge.HalfWidthMetres(head);
                    for (int k = 0; k <= 16; k++)
                    {
                        float t = k / 16f;
                        Stamp(surge, n, half, Bezier(a, b, c, d, t), halfWidth);
                    }
                }
            }

            // ── Measure ─────────────────────────────────────────
            int lavaCells = 0, surgeCells = 0, both = 0;
            for (int i = 0; i < surge.Length; i++)
            {
                bool l = lava[i] > 0f;
                bool s = surge[i] > 0f;
                if (l) lavaCells++;
                if (s) surgeCells++;
                if (l && s) both++;
            }

            float cellArea = MetresPerPixel * MetresPerPixel / 1e6f;   // km²
            log.AppendLine("## pyroclastic fan (strato, intensity 1.0, " + LavaFlows + " lava flows)");
            log.AppendLine("  lobes            : " + PyroclasticSurge.LobeCount);
            log.AppendLine("  lava covers      : "
                           + (lavaCells * cellArea).ToString("F3", CultureInfo.InvariantCulture) + " km2");
            log.AppendLine("  the fan covers   : "
                           + (surgeCells * cellArea).ToString("F3", CultureInfo.InvariantCulture) + " km2  ("
                           + (surgeCells / (float)Math.Max(1, lavaCells)).ToString("F1", CultureInfo.InvariantCulture)
                           + "x the lava)");
            log.AppendLine("  of that, on lava : "
                           + (100f * both / Math.Max(1, surgeCells)).ToString("F0", CultureInfo.InvariantCulture)
                           + " %   (the rest is flank the lava never touched)");
            log.AppendLine();

            // ── Draw ─────────────────────────────────────────
            var rgb = new byte[n * n * 3];
            for (int z = 0; z < n; z++)
            {
                float wz = (z - half) * MetresPerPixel;
                for (int x = 0; x < n; x++)
                {
                    float wx = (x - half) * MetresPerPixel;

                    float h = VolcanoCrater.ProfileAt(relief, wx, wz, radius, height);
                    float hx = VolcanoCrater.ProfileAt(relief, wx + MetresPerPixel, wz, radius, height)
                               - VolcanoCrater.ProfileAt(relief, wx - MetresPerPixel, wz, radius, height);
                    float hz = VolcanoCrater.ProfileAt(relief, wx, wz + MetresPerPixel, radius, height)
                               - VolcanoCrater.ProfileAt(relief, wx, wz - MetresPerPixel, radius, height);

                    float nx = -hx / (2f * MetresPerPixel);
                    float nz = -hz / (2f * MetresPerPixel);
                    float len = (float)Math.Sqrt(nx * nx + 1f + nz * nz);
                    float lambert = (nx * -0.62f + 0.48f + nz * -0.62f) / len;
                    if (lambert < 0f) lambert = 0f;
                    float shade = 0.35f + 1.1f * lambert;

                    float t = height > 0f ? h / height : 0f;
                    float r, g, b;
                    if (h <= 0f) { r = 0.42f; g = 0.47f; b = 0.40f; shade = 1f; }
                    else { r = 0.34f + 0.34f * t; g = 0.46f - 0.12f * t; b = 0.28f - 0.02f * t; }

                    r *= shade; g *= shade; b *= shade;

                    int c = z * n + x;

                    // The fan (grey). It is summed over one full cycle, so the density is
                    // "how many times it was swept over".
                    if (surge[c] > 0f)
                    {
                        float k = surge[c] > 1f ? 1f : surge[c];
                        k = 0.35f + 0.45f * k;
                        r = r + (0.72f - r) * k;
                        g = g + (0.70f - g) * k;
                        b = b + (0.69f - b) * k;
                    }

                    // The lava (red). Drawn on top of the fan (hidden underneath it there
                    // would be nothing to compare).
                    if (lava[c] > 0f)
                    {
                        r = 0.95f; g = 0.32f; b = 0.08f;
                    }

                    int o = (z * n + x) * 3;
                    rgb[o] = Byte(r); rgb[o + 1] = Byte(g); rgb[o + 2] = Byte(b);
                }
            }

            Png.Write(Path.Combine(dir, "surge-strato-fan.png"), n, n, rgb);
            Console.WriteLine("wrote " + Path.Combine(dir, "surge-strato-fan.png"));
        }

        /// <summary>The slope of the mountainside (uphill direction). The same 4 m step as the
        /// game's <c>SampleDetailHeight</c>.</summary>
        private static void Slope(VolcanoRelief relief, Vec2 p, float radius, float height,
                                  out float dx, out float dz)
        {
            float hx0 = VolcanoCrater.ProfileAt(relief, p.X - SampleMetres, p.Z, radius, height);
            float hx1 = VolcanoCrater.ProfileAt(relief, p.X + SampleMetres, p.Z, radius, height);
            float hz0 = VolcanoCrater.ProfileAt(relief, p.X, p.Z - SampleMetres, radius, height);
            float hz1 = VolcanoCrater.ProfileAt(relief, p.X, p.Z + SampleMetres, radius, height);

            dx = (hx1 - hx0) / (2f * SampleMetres);
            dz = (hz1 - hz0) / (2f * SampleMetres);
        }

        private static Vec2 Bezier(Vec2 a, Vec2 b, Vec2 c, Vec2 d, float t)
        {
            float u = 1f - t;
            float w0 = u * u * u;
            float w1 = 3f * u * u * t;
            float w2 = 3f * u * t * t;
            float w3 = t * t * t;
            return new Vec2(a.X * w0 + b.X * w1 + c.X * w2 + d.X * w3,
                            a.Z * w0 + b.Z * w1 + c.Z * w2 + d.Z * w3);
        }

        /// <summary>Adds one disc of radius <paramref name="radiusMetres"/>.</summary>
        private static void Stamp(float[] field, int n, int half, Vec2 p, float radiusMetres)
        {
            float cx = half + p.X / MetresPerPixel;
            float cz = half + p.Z / MetresPerPixel;
            float r = radiusMetres / MetresPerPixel;

            int x0 = (int)(cx - r), x1 = (int)(cx + r) + 1;
            int z0 = (int)(cz - r), z1 = (int)(cz + r) + 1;
            if (x0 < 0) x0 = 0;
            if (z0 < 0) z0 = 0;
            if (x1 > n) x1 = n;
            if (z1 > n) z1 = n;

            float r2 = r * r;
            for (int z = z0; z < z1; z++)
            {
                float dz = z - cz;
                for (int x = x0; x < x1; x++)
                {
                    float dx = x - cx;
                    if (dx * dx + dz * dz >= r2) continue;
                    field[z * n + x] += 0.08f;
                }
            }
        }

        private static byte Byte(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }
    }
}
