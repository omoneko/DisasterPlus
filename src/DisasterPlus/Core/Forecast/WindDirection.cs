namespace DisasterPlus.Core.Forecast
{
    /// <summary>角度を 16 方位のラベルにする。</summary>
    public static class WindDirection
    {
        private const int Sectors = 16;
        private const float SectorWidth = 360f / Sectors;   // 22.5

        /// <summary>
        /// ラベルは static readonly 配列で持ってよい。方位略号は英語固定で、
        /// ローカライズ対象ではないため（言語で凍結する問題が起きない）。
        /// </summary>
        private static readonly string[] Labels =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW",
        };

        public static string LabelOf(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return "?";

            // 負の角度と 360 超えの両方を 0-360 に畳む。
            float d = degrees % 360f;
            if (d < 0f) d += 360f;

            // セクタ中心を境界にするため半セクタ分ずらしてから割る。
            int index = (int)((d + SectorWidth * 0.5f) / SectorWidth);
            if (index >= Sectors) index -= Sectors;
            if (index < 0) index = 0;

            return Labels[index];
        }
    }
}
