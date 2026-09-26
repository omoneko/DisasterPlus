namespace DisasterPlus.Core.Forecast
{
    /// <summary>Turns an angle into one of the 16 compass-point labels.</summary>
    public static class WindDirection
    {
        private const int Sectors = 16;
        private const float SectorWidth = 360f / Sectors;   // 22.5

        /// <summary>
        /// These labels may live in a static readonly array. The compass abbreviations are
        /// fixed English and are not localised, so the freeze-on-first-language problem
        /// cannot arise here.
        /// </summary>
        private static readonly string[] Labels =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW",
        };

        public static string LabelOf(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return "?";

            // Fold both negative angles and angles past 360 back into 0-360.
            float d = degrees % 360f;
            if (d < 0f) d += 360f;

            // Shift by half a sector before dividing, so the sector centres land on the boundaries.
            int index = (int)((d + SectorWidth * 0.5f) / SectorWidth);
            if (index >= Sectors) index -= Sectors;
            if (index < 0) index = 0;

            return Labels[index];
        }
    }
}
