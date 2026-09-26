namespace DisasterPlus.Core.Earthquake
{
    /// <summary>A coarse banding of how hard the ground shakes. It does not borrow the names
    /// of any real intensity scale.</summary>
    public enum SeismicBand
    {
        None,
        Weak,
        Moderate,
        Strong,
        Severe,
    }

    /// <summary>
    /// Turns the local factor s (0-1) into a display step, a bar and a band name.
    ///
    /// **It does not claim to be the JMA seismic intensity scale** (design doc §3.1). s is
    /// neither an acceleration nor a measured intensity; it is the game's collapse factor.
    /// Borrowing the name of a real scale makes people think it carries a real meaning.
    /// Keep the wording to "strength" and "how hard it shakes", and always print the raw
    /// 0.0-1.0 value alongside it.
    ///
    /// Ten steps but five bands is deliberate. We want the bar's resolution to be ten
    /// steps, but localising ten step names would make the differences between the
    /// translations themselves look like "a scale that means something". Only go as far as
    /// naming five bands.
    ///
    /// The bar is fixed ASCII (same reason as ①'s HazardLevel: there is no guarantee the
    /// CS UI font has the block-drawing characters, and without them you get tofu).
    /// </summary>
    public static class SeismicScale
    {
        public const int Steps = 10;
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>Maps s onto 0-Steps. Monotonically increasing. s = 1 lands exactly on
        /// Steps.</summary>
        public static int StepOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return 0;
            if (s >= 1f) return Steps;

            int step = (int)(s * Steps);
            if (step < 0) step = 0;
            if (step > Steps) step = Steps;
            return step;
        }

        /// <summary>A bar of length Steps. The number filled matches StepOf.</summary>
        public static string BarOf(float s)
        {
            int filled = StepOf(s);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }

        /// <summary>The band name. Boundaries include the lower side (0.25 is Moderate).</summary>
        public static SeismicBand BandOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return SeismicBand.None;
            if (s < 0.25f) return SeismicBand.Weak;
            if (s < 0.5f) return SeismicBand.Moderate;
            if (s < 0.75f) return SeismicBand.Strong;
            return SeismicBand.Severe;
        }
    }
}
