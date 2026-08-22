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

        /// <summary>
        /// 谷を数えるときの不感帯（m）。**これより小さい上下は起伏と数えない。**
        /// 滑らかな円錐の円周は定数なので、無いと float の丸めを谷として数えてしまう。
        /// </summary>
        private const float DeadBandMetres = 0.05f;

        /// <summary>試す波長（m）。16 m 格子で 2 / 3 / 4 / 5 / 7 / 11 セル。</summary>
        private static readonly float[] Wavelengths = { 32f, 48f, 64f, 80f, 110f, 170f };

        /// <summary>
        /// **谷が「線」として繋がっているか。** 2026-08-22 のレビューが差し戻した点で、
        /// ここまでの計測（1 本のノイズの極値密度）では捕まえられなかったものである。
        ///
        /// 谷は半径方向に走るので:
        ///
        /// <code>
        /// 谷に沿って（半径方向）の極値密度 = 破線度。0 に近いほど溝が繋がっている
        /// 谷を横切って（方位方向）の極値の数 = 谷の本数。多いほど細かい
        /// </code>
        ///
        /// 破線に見えるのは**前者が大きいとき**である。後者だけを見ていると
        /// 「本数は増えたが 1 本 1 本が点線」を緑にしてしまう。
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
        /// 谷に沿って（半径方向）16 m ごとに歩いたときの極値密度。
        /// 滑らかな円錐なら 0（単調に下るだけ）。
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

        /// <summary>半径 <paramref name="fraction"/>R の円周に沿った谷（極小）の数。</summary>
        private static int Troughs(VolcanoRelief relief, float r, float h, float fraction)
        {
            float ring = fraction * r;
            int reversals = 0;
            int sign = 0;
            float previous = 0f;

            // 円周を 16 m 刻みで歩く（ゲームの格子と同じ密度で数える）。
            int steps = (int)(2.0 * Math.PI * ring / Cell);
            if (steps < 16) steps = 16;

            for (int a = 0; a <= steps; a++)
            {
                double th = 2.0 * Math.PI * a / steps;
                float v = VolcanoCrater.ProfileAt(relief, (float)(Math.Cos(th) * ring),
                                                  (float)(Math.Sin(th) * ring), r, h);
                if (a > 0)
                {
                    // ★ 不感帯。滑らかな円錐の円周は定数なので、差は float の丸め
                    //   （±1e-7 m）だけになって符号がでたらめに反転する。
                    //   不感帯が無いと**起伏 0 の山が 83 本の谷を持つ**と報告した（実際に出た）。
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
