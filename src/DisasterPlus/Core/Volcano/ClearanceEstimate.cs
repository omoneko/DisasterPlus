using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The "how wide the area is" and "rounding to an approximate figure" shown on the
    /// affected-area line.
    ///
    /// ★★ <b>This is not "the number that will be destroyed".</b> What you pass to
    /// <see cref="RoundedEstimate"/> is <b>the exact count that was inside the area at the
    /// moment of the survey</b>, and what comes back is that count rounded. The preparation
    /// (destruction) spreads outwards over several in-game minutes, and **the city changes
    /// during that time** — buildings go up, roads get laid, and things also disappear to
    /// fires and other disasters. So the number actually destroyed will not match this
    /// value. That is why design document §7.2 requires us to "state explicitly that it is
    /// approximate" (<c>Strings.VolcanoEstimateNote</c> is that sentence).
    ///
    /// <b>Never show the exact count as-is.</b> Write "243 buildings" and the player reads
    /// it as "exactly 243 buildings and no more will be destroyed". ⑤ makes no such
    /// guarantee.
    ///
    /// The rounding is <b>to two significant figures, rounding half up</b>, and anything
    /// below 100 is shown as-is ("roughly 3 buildings" sounds fake, and rounding a
    /// single-digit value can give 0 — "0 buildings" and "we counted, and it was few" are
    /// different things).
    ///
    /// > **It rounds rather than truncates.** Truncation always makes the destruction look
    /// > smaller. This number is shown immediately before an operation that irreversibly
    /// > destroys the player's assets, so we do not choose a rounding that biases low.
    /// > That it is monotonically non-decreasing is pinned down by tests.
    ///
    /// Engine-free (no <c>UnityEngine</c>, no LINQ, no <c>System.Random</c>).
    /// </summary>
    public static class ClearanceEstimate
    {
        /// <summary>The side of one raw cell (m). The <c>cell = 16</c> from §A-1 /
        /// §C-8.</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>The cut-off below which counts are shown as-is (see the class
        /// doc).</summary>
        private const int ExactBelow = 100;

        /// <summary>The number of significant figures.</summary>
        private const int SignificantDigits = 2;

        /// <summary>
        /// Roughly how many raw cells a circle of radius <paramref name="radiusMetres"/>
        /// covers.
        ///
        /// **This is a measure of area, not the number of cells scanned** (the scan runs on
        /// the 64 m building grid). NaN or zero-and-below gives 0.
        /// </summary>
        public static int CellsInside(float radiusMetres)
        {
            double area = FootprintAreaSquareMetres(radiusMetres);
            if (area <= 0d) return 0;

            double cells = area / (RawCellSizeMetres * (double)RawCellSizeMetres);
            if (cells >= int.MaxValue) return int.MaxValue;
            return (int)cells;
        }

        /// <summary>
        /// The area of the affected region (m²). NaN or zero-and-below gives 0.
        /// </summary>
        public static float FootprintAreaSquareMetres(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return 0f;
            return (float)(Math.PI * radiusMetres * (double)radiusMetres);
        }

        /// <summary>
        /// Exact count → approximate figure (see the class doc). Zero and below give 0,
        /// below 100 is passed through, and above that it is rounded to two significant
        /// figures. **Monotonically non-decreasing.**
        /// </summary>
        public static int RoundedEstimate(int exactCount)
        {
            if (exactCount <= 0) return 0;
            if (exactCount < ExactBelow) return exactCount;

            // unit = 10^(digits - SignificantDigits), and lead lands in [10, 99].
            long unit = 1L;
            long lead = exactCount;
            while (lead >= ExactBelow)
            {
                lead /= 10L;
                unit *= 10L;
            }

            long remainder = exactCount - lead * unit;
            // remainder * 2 >= unit gives "round up when it is half or more".
            // We never form 0.5 by division, so the rounding direction does not depend on
            // the platform.
            long rounded = (remainder * 2L >= unit) ? (lead + 1L) * unit : lead * unit;

            return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
        }
    }
}
