using System;
using System.Globalization;
using System.Text;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// 「どこまで細かくできるか」を**数えて**決めるための計測。
    ///
    /// 起伏を細かくしたくなるたびに「16 m 格子だから無理」と言うだけでは、
    /// どこが本当の床なのかが誰にも分からない。ここでは
    /// <see cref="VolcanoRelief.ValueNoise"/>（**実物**。書き直した近似ではない）を
    /// ゲームと同じ 16 m 格子で標本化して、**極値密度**を数える。
    ///
    /// <code>
    /// 極値密度 = (1 セル進むごとに斜面の向きが反転した回数) / (セル数)
    /// </code>
    ///
    ///   - 滑らかな波なら 1 波長に 2 個 ＝ 2 / (波長 ÷ 16 m)
    ///   - **市松模様なら 1 セルに 1 個 ＝ 0.5**（＝ 折り返している）
    ///
    /// この 1 つの数で「起伏か、ノイズか」を切り分けられる。
    /// </summary>
    internal static class Relief
    {
        private const float Cell = VolcanoShape.RawCellSizeMetres;

        /// <summary>試す波長（m）。16 m 格子で 2 / 3 / 4 / 5 / 7 / 11 セル。</summary>
        private static readonly float[] Wavelengths = { 32f, 48f, 64f, 80f, 110f, 170f };

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
        /// 4 km の直線に沿って 16 m ごとに <see cref="VolcanoRelief.ValueNoise"/> を
        /// 標本化し、極値（1 階差分の符号反転）の密度を返す。
        /// 格子に平行でない向き（斜め）で測る —— 平行に測ると格子点の上を通るので、
        /// いちばん都合のよい値が出る。
        /// </summary>
        private static float ExtremaDensity(float wavelengthMetres)
        {
            const int Samples = 256;
            const uint Seed = 0x5EEDF14Eu;

            // 斜め（1, 0.37）方向。格子と通約でない向きを選ぶ。
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
