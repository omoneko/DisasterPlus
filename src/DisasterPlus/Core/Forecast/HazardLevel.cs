namespace DisasterPlus.Core.Forecast
{
    /// <summary>
    /// Turns a hazard value (byte 0-255) into a display step and a bar string.
    /// Vanilla only ever shows it as a colour, so putting a step and a number on it is
    /// what this feature adds.
    /// </summary>
    public static class HazardLevel
    {
        public const int Steps = 10;

        /// <summary>
        /// Build the bar out of ASCII only. There is no guarantee the CS UI font has the
        /// block-drawing characters (▓ ░), and without them you get tofu. Showing up
        /// reliably matters more than looking nice.
        /// </summary>
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>Maps 0-255 onto 0-Steps. Monotonically increasing.</summary>
        public static int StepOf(byte hazard)
        {
            // Plain integer division, no rounding up, so that 255 lands exactly on Steps.
            // 255 * Steps / 255 == Steps, so both ends come out exact.
            return hazard * Steps / 255;
        }

        /// <summary>A bar of length Steps. The number filled matches StepOf.</summary>
        public static string BarOf(byte hazard)
        {
            int filled = StepOf(hazard);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }
    }
}
