using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// The water surface averaged <b>by distance from the centre</b>.
    ///
    /// A tsunami runs in concentric circles, so looking at a single cross-section (along the
    /// x axis, say) picks up grid-induced jaggedness and the grain of the random numbers.
    /// **Averaging every cell at the same radius** makes the position and height of the wave
    /// front easy to read off.
    /// </summary>
    internal sealed class Profile
    {
        /// <summary>Mean water surface at radius r cells (positive above sea level, m).</summary>
        public readonly float[] MeanMetres;

        /// <summary>Number of cells that fell into that radius. Where it is 0 there is no
        /// value.</summary>
        public readonly int[] Count;

        /// <summary>Radius of the highest ring (cells). -1 if none was found.</summary>
        public readonly int PeakRadiusCells;

        /// <summary>Height of that ring (m).</summary>
        public readonly float PeakMetres;

        private Profile(float[] mean, int[] count, int peakR, float peakM)
        {
            MeanMetres = mean;
            Count = count;
            PeakRadiusCells = peakR;
            PeakMetres = peakM;
        }

        /// <summary>
        /// Averages the water surface of <paramref name="field"/> radially about (cx, cz).
        /// The radius steps in units of one cell (16 m), binned by the rounded integer distance.
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

                // ★ The wave front is a "ring", not the centre itself, so search from r >= 1.
                if (r >= 1 && (peakR < 0 || mean[r] > peakM))
                {
                    peakR = r;
                    peakM = mean[r];
                }
            }

            return new Profile(mean, count, peakR, peakM);
        }

        /// <summary>Mean water surface (m) at <paramref name="metres"/> from the centre.
        /// NaN if out of range.</summary>
        public float AtMetres(float metres)
        {
            int r = (int)(metres / WaterField.CellSizeMetres + 0.5f);
            if (r < 0 || r >= MeanMetres.Length || Count[r] == 0) return float.NaN;
            return MeanMetres[r];
        }
    }
}
