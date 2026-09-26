namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The damage factor for the time of day. **This is a presentation value this mod
    /// invented.**
    ///
    /// **There is no physical basis for <see cref="NightFactor"/> = 1.15.** Nowhere in
    /// vanilla is there any grounds for "damage is worse at night" (design doc §4.3), and
    /// the game does not use the clock in its disaster maths at all. The 15% was picked
    /// simply as "you can feel it, but it does not make you doubt the first layer's
    /// numbers". Do not claim it comes from real-world statistics.
    ///
    /// ── Where it applies (never, ever cross this boundary) ──────────────────────
    ///
    /// **It applies only to the second layer's additional damage.** Its one and only
    /// application point is where <c>LongPeriodDamage</c> multiplies the result of
    /// <see cref="LongPeriodResponse.ExtraCollapseChance"/>, and **it touches vanilla's own
    /// damage not at all**. Nor does it apply to the first layer's readouts (intensity,
    /// margin, warnings, waveform) — those are quantities vanilla actually computes, and
    /// mixing this mod's factor into them would stop them being measurements.
    ///
    /// ── With the day/night cycle off it quietly becomes a constant (§F-1) ──────────────
    ///
    /// When <c>m_enableDayNight == false</c> and we are in game mode,
    /// <c>m_dayTimeOffsetFrames</c> is reset every sim frame and **the clock is pinned at
    /// 12.0 for ever**. So on that setting this factor is always
    /// <see cref="DayFactor"/> (i.e. 1.0).
    ///
    /// **Do not add a special case to the formula.** 12.0 is the middle of the day, so the
    /// factor comes out at 1.0 naturally (<c>MiddayIsTheDayFactor</c> pins that). Adding a
    /// branch only adds a path taken "only when day/night is off", which is hard to verify.
    /// **Instead, own up to the fact in the panel and the diagnostic dump**
    /// (<c>Strings.EarthquakeNoDayNight</c>). Hiding the fact that something is disabled is
    /// the form of output this mod detests most.
    ///
    /// This is not made a checked item in <c>Assumptions</c>. Turning off the day/night
    /// cycle is **a legitimate player setting**, not a broken assumption, and making it a
    /// FAIL would leave "some features are unavailable" showing permanently in an
    /// environment where nothing is broken — back to the shape where the layer that exists
    /// to remove false positives produces false positives itself (point I5, already fixed
    /// once in ①).
    /// </summary>
    public static class TimeOfDayFactor
    {
        /// <summary><c>SimulationManager.SUNRISE_HOUR</c> (measured from IL, §F-1).</summary>
        public const float SunriseHour = 5f;

        /// <summary><c>SimulationManager.SUNSET_HOUR</c> (measured from IL, §F-1).</summary>
        public const float SunsetHour = 20f;

        /// <summary>The daytime factor. **Exactly 1.0** — by day nothing differs from
        /// vanilla.</summary>
        public const float DayFactor = 1f;

        /// <summary>
        /// The night-time factor. **A presentation value this mod chose, with no basis**
        /// (see the class doc).
        /// </summary>
        public const float NightFactor = 1.15f;

        /// <summary>
        /// How long (hours) it takes to cross the day/night boundary. Make the boundary a
        /// step and the additional damage jumps 15% in the single frame of sunrise.
        /// </summary>
        public const float RampHours = 1f;

        /// <summary>The length of a day (hours).</summary>
        private const float HoursPerDay = 24f;

        /// <summary>
        /// A straight copy of the game's own night test
        /// (<c>m_isNightTime = hour &lt; SUNRISE_HOUR || hour &gt; SUNSET_HOUR</c>, §F-1).
        /// It is a **hard boundary**.
        ///
        /// <see cref="Of"/> crosses the boundary smoothly over <see cref="RampHours"/>, so
        /// in the 30 minutes either side of it there is a stretch where "it is day, but the
        /// factor is not 1.0". **The display side must not put these two next to each
        /// other** (you would get 1.08 printed beside "day").
        /// </summary>
        public static bool IsNight(float hour)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return false;

            float h = Fold(hour);
            return h < SunriseHour || h > SunsetHour;
        }

        /// <summary>
        /// Derives the factor from the time of day (hours). The result always lands in
        /// [<see cref="DayFactor"/>, <see cref="NightFactor"/>].
        ///
        /// A broken value (NaN / Infinity) falls to <see cref="DayFactor"/> (neutral).
        /// **This is a number that multiplies a probability of actually destroying
        /// buildings**, so a failed read must never be turned into a jump in the multiplier.
        /// </summary>
        public static float Of(float hour)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return DayFactor;

            float night = Nightness(Fold(hour));
            return DayFactor + (NightFactor - DayFactor) * night;
        }

        /// <summary>
        /// "How night-like it is" ∈ [0, 1]. It crosses linearly over
        /// <see cref="RampHours"/> / 2 either side of the boundary (at sunrise: 1 at 4:30,
        /// 0 at 5:30).
        /// </summary>
        private static float Nightness(float hour)
        {
            float half = RampHours * 0.5f;
            if (half <= 0f) return hour < SunriseHour || hour > SunsetHour ? 1f : 0f;

            // Night → day (sunrise).
            float rising = Ramp((hour - (SunriseHour - half)) / RampHours);
            // Day → night (sunset).
            float falling = Ramp((hour - (SunsetHour - half)) / RampHours);

            // The 0:00 end is fully night, going to 0 at sunrise and back to 1 at sunset.
            float night = 1f - rising + falling;
            if (night < 0f) return 0f;
            return night > 1f ? 1f : night;
        }

        /// <summary>The identity map clamped to [0,1] (the body of the linear ramp).</summary>
        private static float Ramp(float t)
        {
            if (t <= 0f) return 0f;
            return t >= 1f ? 1f : t;
        }

        /// <summary>Folds the time of day into [0, 24).</summary>
        private static float Fold(float hour)
        {
            float h = hour % HoursPerDay;
            if (h < 0f) h += HoursPerDay;
            // Blocks the path where a tiny negative value rounds up to 24.0.
            return h >= HoursPerDay ? 0f : h;
        }
    }
}
