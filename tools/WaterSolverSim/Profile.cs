using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// 水面を<b>中心からの距離ごと</b>にならしたもの。
    ///
    /// 津波は同心円で走るので、1 本の断面（例えば x 軸上）だけを見ると
    /// 格子由来のギザギザと乱数の粒が乗る。**同じ半径のセルを全部平均する**と
    /// 波頭の位置と高さがきれいに読める。
    /// </summary>
    internal sealed class Profile
    {
        /// <summary>半径 r セルの平均水面（海面より上を正、m）。</summary>
        public readonly float[] MeanMetres;

        /// <summary>その半径に入ったセル数。0 のところは値が無い。</summary>
        public readonly int[] Count;

        /// <summary>いちばん高い輪の半径（セル）。見つからなければ -1。</summary>
        public readonly int PeakRadiusCells;

        /// <summary>その輪の高さ（m）。</summary>
        public readonly float PeakMetres;

        private Profile(float[] mean, int[] count, int peakR, float peakM)
        {
            MeanMetres = mean;
            Count = count;
            PeakRadiusCells = peakR;
            PeakMetres = peakM;
        }

        /// <summary>
        /// <paramref name="field"/> の水面を (cx, cz) 中心で半径方向にならす。
        /// 半径は 1 セル（16 m）刻みで、四捨五入した整数距離で束ねる。
        /// </summary>
        public static Profile Build(WaterField field, int cx, int cz)
        {
            int n = field.Size;
            int maxR = Math.Min(Math.Min(cx, cz), Math.Min(n - 1 - cx, n - 1 - cz));

            double[] sum = new double[maxR + 1];
            int[] count = new int[maxR + 1];

            Cell[] cells = field.Cells;
            ushort[] terrain = field.Terrain;
            int sea = field.SeaLevelUnits;

            for (int z = cz - maxR; z <= cz + maxR; z++)
            {
                int dz = z - cz;
                int row = z * n;
                for (int x = cx - maxR; x <= cx + maxR; x++)
                {
                    int dx = x - cx;
                    double d = Math.Sqrt((double)dx * dx + (double)dz * dz);
                    int r = (int)(d + 0.5);
                    if (r > maxR) continue;

                    int i = row + x;
                    sum[r] += (terrain[i] + cells[i].Height - sea) / (double)WaterField.UnitsPerMetre;
                    count[r]++;
                }
            }

            float[] mean = new float[maxR + 1];
            int peakR = -1;
            float peakM = 0f;

            for (int r = 0; r <= maxR; r++)
            {
                if (count[r] == 0) continue;
                mean[r] = (float)(sum[r] / count[r]);

                // ★ 波頭は中心そのものではなく「輪」なので r >= 1 から探す。
                if (r >= 1 && (peakR < 0 || mean[r] > peakM))
                {
                    peakR = r;
                    peakM = mean[r];
                }
            }

            return new Profile(mean, count, peakR, peakM);
        }

        /// <summary>中心から <paramref name="metres"/> の地点の平均水面（m）。範囲外なら NaN。</summary>
        public float AtMetres(float metres)
        {
            int r = (int)(metres / WaterField.CellSizeMetres + 0.5f);
            if (r < 0 || r >= MeanMetres.Length || Count[r] == 0) return float.NaN;
            return MeanMetres[r];
        }
    }
}
