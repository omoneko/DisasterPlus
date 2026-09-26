using System;
using System.Globalization;
using System.Text;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// A measurement that settles "how fine can we go" by **counting**.
    ///
    /// Merely saying "impossible, it is a 16 m grid" every time someone wants finer relief
    /// leaves nobody knowing where the real floor is. Here
    /// <see cref="VolcanoRelief.ValueNoise"/> (**the real thing**, not a rewritten
    /// approximation) is sampled on the same 16 m grid as the game and the **density of
    /// extrema** is counted.
    ///
    /// <code>
    /// extrema density = (times the slope direction reverses per cell) / (number of cells)
    /// </code>
    ///
    ///   - for a smooth wave, 2 per wavelength = 2 / (wavelength / 16 m)
    ///   - **for a checkerboard, 1 per cell = 0.5** (i.e. it is aliasing)
    ///
    /// This single number separates "relief" from "noise".
    /// </summary>
    internal static class Relief
    {
        private const float Cell = VolcanoShape.RawCellSizeMetres;

        /// <summary>
        /// The dead band when counting troughs (m). **Rises and falls smaller than this do not
        /// count as relief.** The circumference of a smooth cone is constant, so without it
        /// float rounding would be counted as troughs.
        /// </summary>
        private const float DeadBandMetres = 0.05f;

        /// <summary>The wavelengths tried (m). On a 16 m grid: 2 / 3 / 4 / 5 / 7 / 11
        /// cells.</summary>
        private static readonly float[] Wavelengths = { 32f, 48f, 64f, 80f, 110f, 170f };

        /// <summary>
        /// **Whether the gullies join up as "lines".** This is what the review of 2026-08-22
        /// sent back, and what the measurements up to that point (the extrema density of a
        /// single noise source) could not catch.
        ///
        /// The gullies run radially, so:
        ///
        /// <code>
        /// extrema density along a groove (radial)  = dashedness. Closer to 0 = a joined groove
        /// number of extrema across the grooves (azimuthal) = how many gullies. More = finer
        /// </code>
        ///
        /// It looks dashed **when the former is large**. Looking only at the latter, one ends
        /// up passing "more of them, but each one a dotted line" as green.
        /// </summary>
        public static void Continuity(StringBuilder log, string label, VolcanoRelief relief,
                                      VolcanoForm form, float r, float h)
        {
            log.AppendLine("  " + label.PadRight(18) + ": along-groove "
                           + F3(AlongGroove(relief, r, h, 0.50f, 0.98f))
                           + " (0 = a solid groove, 0.5 = dots)"
                           + ", troughs @0.85R " + Troughs(relief, r, h, 0.85f)
                           + ", @0.70R " + Troughs(relief, r, h, 0.70f));
        }

        /// <summary>
        /// The extrema density when walking along a groove (radially) in 16 m steps.
        /// For a smooth cone it is 0 (it only descends monotonically).
        /// </summary>
        private static float AlongGroove(VolcanoRelief relief, float r, float h,
                                         float fromFraction, float toFraction)
        {
            const int Azimuths = 360;
            int reversals = 0;
            int counted = 0;

            for (int a = 0; a < Azimuths; a++)
            {
                double th = 2.0 * Math.PI * a / Azimuths;
                float cx = (float)Math.Cos(th);
                float cz = (float)Math.Sin(th);

                int sign = 0;
                float previous = 0f;
                bool have = false;

                for (float d = fromFraction * r; d <= toFraction * r; d += Cell)
                {
                    float v = VolcanoCrater.ProfileAt(relief, cx * d, cz * d, r, h);
                    if (have)
                    {
                        float delta = v - previous;
                        int next = delta > 0f ? 1 : (delta < 0f ? -1 : sign);
                        if (sign != 0 && next != 0 && next != sign) reversals++;
                        if (sign != 0) counted++;
                        sign = next;
                    }
                    previous = v;
                    have = true;
                }
            }

            return counted > 0 ? reversals / (float)counted : 0f;
        }

        /// <summary>The number of troughs (minima) around the circle at radius
        /// <paramref name="fraction"/>R.</summary>
        private static int Troughs(VolcanoRelief relief, float r, float h, float fraction)
        {
            float ring = fraction * r;
            int reversals = 0;
            int sign = 0;
            float previous = 0f;

            // Walk the circle in 16 m steps (count at the same density as the game's grid).
            int steps = (int)(2.0 * Math.PI * ring / Cell);
            if (steps < 16) steps = 16;

            for (int a = 0; a <= steps; a++)
            {
                double th = 2.0 * Math.PI * a / steps;
                float v = VolcanoCrater.ProfileAt(relief, (float)(Math.Cos(th) * ring),
                                                  (float)(Math.Sin(th) * ring), r, h);
                if (a > 0)
                {
                    // ★ The dead band. The circumference of a smooth cone is constant, so the
                    //   difference is nothing but float rounding (+/-1e-7 m) and the sign flips
                    //   at random. Without the dead band it reported **a mountain with 0 relief
                    //   as having 83 gullies** (that actually happened).
                    float delta = v - previous;
                    int next = delta > DeadBandMetres ? 1
                             : (delta < -DeadBandMetres ? -1 : sign);
                    if (sign != 0 && next != 0 && next != sign) reversals++;
                    sign = next;
                }
                previous = v;
            }

            return reversals / 2;
        }

        public static void Report(StringBuilder log)
        {
            log.AppendLine("## grid limit (how fine the 16 m terrain grid honestly carries)");
            log.AppendLine("  extrema density = slope reversals per cell along a 4 km line");
            log.AppendLine("  0.5 = a reversal at every cell = a checkerboard, not relief");
            log.AppendLine("  wavelength m | cells | extrema density | verdict");

            for (int i = 0; i < Wavelengths.Length; i++)
            {
                float w = Wavelengths[i];
                float density = ExtremaDensity(w);
                string verdict = w < VolcanoRelief.MinWavelengthMetres
                    ? "REFUSED (below MinWavelengthMetres)"
                    : (Math.Abs(w - VolcanoRelief.MinWavelengthMetres) < 0.5f
                        ? "the floor we ship"
                        : "usable");

                log.AppendLine("   " + F(w).PadLeft(10) + "  | " + F1(w / Cell).PadLeft(5)
                               + " | " + F3(density).PadLeft(15) + " | " + verdict);
            }

            log.AppendLine();
            log.AppendLine("  azimuthal floor = " + F(VolcanoRelief.MinAzimuthWavelengthMetres)
                           + " m (" + F1(VolcanoRelief.MinAzimuthWavelengthMetres / Cell)
                           + " cells); it fades, it is not a hard floor, because the");
            log.AppendLine("  azimuthal wavelength shrinks towards the summit (2 pi d / order)");
            log.AppendLine();
        }

        /// <summary>
        /// Samples <see cref="VolcanoRelief.ValueNoise"/> every 16 m along a 4 km line and
        /// returns the density of extrema (sign reversals of the first difference).
        /// Measured along a direction that is not parallel to the grid (a diagonal) —— measure
        /// it parallel and the samples land on grid points, which gives the most flattering
        /// value possible.
        /// </summary>
        private static float ExtremaDensity(float wavelengthMetres)
        {
            const int Samples = 256;
            const uint Seed = 0x5EEDF14Eu;

            // The diagonal (1, 0.37) direction. A direction incommensurate with the grid.
            float ux = 1f, uz = 0.37f;
            float len = (float)Math.Sqrt(ux * ux + uz * uz);
            ux /= len; uz /= len;

            float previous = 0f;
            bool havePrevious = false;
            int sign = 0;
            int reversals = 0;

            for (int i = 0; i < Samples; i++)
            {
                float s = i * Cell;
                float v = VolcanoRelief.ValueNoise(s * ux / wavelengthMetres,
                                                   s * uz / wavelengthMetres, Seed);
                if (havePrevious)
                {
                    float delta = v - previous;
                    int next = delta > 0f ? 1 : (delta < 0f ? -1 : sign);
                    if (sign != 0 && next != 0 && next != sign) reversals++;
                    sign = next;
                }
                previous = v;
                havePrevious = true;
            }

            return reversals / (float)(Samples - 1);
        }

        private static string F(double v) { return v.ToString("F0", CultureInfo.InvariantCulture); }
        private static string F1(double v) { return v.ToString("F1", CultureInfo.InvariantCulture); }
        private static string F3(double v) { return v.ToString("F3", CultureInfo.InvariantCulture); }
    }
}
