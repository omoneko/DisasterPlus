namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// A pure function that reduces a run of samples to "a row number per column". The
    /// drawing itself is done by <c>Game/Earthquake/WaveformView</c> (this side never
    /// touches <c>UnityEngine</c>).
    ///
    /// ── Buckets are split by **frame range**, never by array index ──────────────
    ///
    /// <c>SimulationManager.SimulationStep</c> loops <c>FinalSimulationSpeed</c> times
    /// within one tick (1/3/9 times at game speeds 1/2/3), so <c>m_currentFrameIndex</c>
    /// jumps by 1/3/9 per tick (fire whirl design document, appendix A-4).
    /// **The spacing between samples changes with the game speed.** Split them evenly by
    /// index and, the moment you raise the speed, you draw a waveform whose time axis has
    /// stretched ninefold. It is the same class of mistake as building a period out of
    /// <c>frameIndex % N</c>, and it is hard to spot because the symptom is "a picture that
    /// looks plausible but is wrong".
    ///
    /// ── A column with no samples at all is <see cref="Empty"/> (-1) ─────────────
    ///
    /// **It must not return 0.** 0 lands on the middle row (= zero displacement), so a
    /// stretch with no data arriving gets drawn as a stretch with no shaking. This is how
    /// the rule carried over from ① — "never call something you could not read 0" — shows
    /// up in this feature.
    /// </summary>
    public static class WaveformPlot
    {
        /// <summary>The marker for "this column has no samples at all".</summary>
        public const int Empty = -1;

        /// <summary>
        /// Returns the row number for each column (0 = top, <paramref name="height"/> =
        /// bottom, the middle = zero displacement). The result is always
        /// <paramref name="width"/> long.
        ///
        /// <paramref name="scale"/> is the factor at which "a displacement of 1.0 reaches
        /// from the middle to the top". The caller decides it from the amplitude (say
        /// <c>1 / peak amplitude</c>). **Pass zero, a negative value or NaN and every column
        /// comes back <see cref="Empty"/>** — drawing a flat line down the middle while the
        /// vertical axis is undetermined reads as a waveform with no shaking.
        ///
        /// Samples outside the window (below <paramref name="fromFrame"/> or above
        /// <paramref name="toFrame"/>) are discarded rather than squeezed into the end
        /// columns. Squeeze them in and you get a column there alone piled up with old
        /// shaking.
        ///
        /// Bad input (null arrays, a count of 0, a zero-width window) never throws.
        /// </summary>
        public static int[] Columns(uint[] frames, float[] values, int count,
                                    uint fromFrame, uint toFrame,
                                    int width, int height, float scale)
        {
            if (width < 1) width = 1;

            int[] columns = new int[width];
            for (int i = 0; i < width; i++) columns[i] = Empty;

            if (frames == null || values == null) return columns;
            if (count > frames.Length) count = frames.Length;
            if (count > values.Length) count = values.Length;
            if (count <= 0) return columns;
            if (height < 1) return columns;
            if (scale <= 0f || float.IsNaN(scale)) return columns;
            if (toFrame <= fromFrame) return columns;

            long span = (long)toFrame - fromFrame;
            int middle = height / 2;

            // Per column, "the largest displacement by absolute value so far". Columns is
            // only called on a redraw (not every frame), so this one allocation is fine.
            float[] best = new float[width];

            for (int i = 0; i < count; i++)
            {
                float v = values[i];
                if (float.IsNaN(v)) continue;

                // Do not subtract one uint from another. A sample older than the window
                // would turn into an enormous positive value.
                long relative = (long)frames[i] - fromFrame;
                if (relative < 0 || relative > span) continue;

                int column = (int)(relative * width / span);
                if (column >= width) column = width - 1;

                float magnitude = v < 0f ? -v : v;
                float bestMagnitude = best[column] < 0f ? -best[column] : best[column];

                // When several land in the same column, take the one largest in absolute
                // value. Take the last one instead and the shaking vanishes in whichever
                // column happened to land on a zero crossing.
                if (columns[column] != Empty && magnitude <= bestMagnitude) continue;

                best[column] = v;
                columns[column] = RowOf(v, scale, middle, height);
            }

            return columns;
        }

        /// <summary>
        /// Displacement → row number. The middle row is zero displacement, and the row
        /// number gets smaller as you go up.
        ///
        /// We round rather than truncate. A float 0.9 widened to double is 0.899999976…, so
        /// truncating pulls it one row inwards and the waveform comes out thin.
        /// The multiplication is done in double and clamped **before** the cast to int
        /// (overflow the int range and the sign flips, so a column that should shoot to the
        /// top comes out at the bottom).
        /// </summary>
        private static int RowOf(float value, float scale, int middle, int height)
        {
            double offset = (double)value * scale * middle;
            if (offset > height) offset = height;
            if (offset < -height) offset = -height;

            int row = middle - (int)System.Math.Round(offset);
            if (row < 0) row = 0;
            if (row > height) row = height;
            return row;
        }
    }
}
