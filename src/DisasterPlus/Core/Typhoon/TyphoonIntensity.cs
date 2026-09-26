namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The range of **peak intensity** (<c>peak</c>) that ④'s typhoon will accept.
    /// <b>This is Core, so it touches the engine not at all.</b>
    ///
    /// ── Why it will not take 0 ─────────────────────────────────
    ///
    /// The raw value of vanilla's intensity slider is <c>[0, 255]</c> and becomes
    /// <c>DisasterData.m_intensity</c> as it stands (see the class doc on
    /// <c>IntensitySlider</c>). So the player can drag the slider all the way down to 0.
    /// **A typhoon of intensity 0 is a typhoon that happens and then does nothing** — it
    /// burns one disaster slot and squats there for 8192 frames, producing a state where
    /// nothing is broken and yet nothing happens. From the point of view of whoever
    /// pressed the button, that is indistinguishable from "the button did not work".
    ///
    /// So we <b>raise it to the floor</b>. We raise rather than refuse because vanilla's
    /// disaster tiles always make *something* happen from the same gesture — if ④ alone
    /// answered a click with no reaction, that would be the more inexplicable behaviour.
    /// The caller can own up to the raise via <see cref="WasRaised"/>.
    ///
    /// <see cref="MinPeak"/> is lined up with the lower bound of the options-screen slider
    /// (10) so that "the weakest you can pick in the settings" and "the weakest you can
    /// pick from the tile" are the same thing.
    /// </summary>
    public static class TyphoonIntensity
    {
        /// <summary>The smallest peak intensity accepted. Same as the options slider's
        /// floor.</summary>
        public const int MinPeak = 10;

        /// <summary>The largest peak intensity accepted (<c>DisasterData.m_intensity</c> is a
        /// byte).</summary>
        public const int MaxPeak = 255;

        /// <summary>
        /// Brings the raw requested value down to a peak intensity we can actually use.
        /// The result lands in <c>[<see cref="MinPeak"/>, <see cref="MaxPeak"/>]</c>.
        /// </summary>
        public static byte PeakOf(int requested)
        {
            if (requested < MinPeak) return (byte)MinPeak;
            if (requested > MaxPeak) return (byte)MaxPeak;
            return (byte)requested;
        }

        /// <summary>Whether the requested value was raised to the floor (for diagnostics and
        /// logging).</summary>
        public static bool WasRaised(int requested)
        {
            return requested < MinPeak;
        }
    }
}
