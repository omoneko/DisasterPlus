namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// Distance from the typhoon's centre → wind-speed equivalent (a factor in [0, 1]), and
    /// the display step and bar for it.
    ///
    /// **This type does not return m/s.** ④'s "wind-speed equivalent" is a factor that
    /// multiplies the game's collapse probability; it is not a real wind speed (design doc
    /// §7.3). For the same reason ② declined to claim the JMA seismic intensity scale,
    /// claiming a unit makes people think it carries a real meaning. All that comes back is
    /// a factor in [0,1] and a step from 0 to <see cref="Steps"/>. **Do not add a method
    /// that returns m/s.**
    ///
    /// The radius alone is lined up with vanilla's formula. <see cref="StormRadiusOf"/> is
    /// **the same** R = m_radius × (0.25 + intensity × 0.0075) as ThunderStormAI's lightning
    /// scatter radius and hazard disc (IL findings doc §A-1 / §A-2), which makes the disc
    /// vanilla paints and the gale radius ④ displays the same size.
    /// The shape of the wind (the eye, the eyewall, the outer falloff) is ④'s invention.
    ///
    /// <c>m_radius</c> is a prefab value and **the DLL contains no actual number for it**
    /// (§A-0, PARTIAL). If it cannot be read, everything returns 0 and the caller does
    /// nothing (design doc §6).
    /// **0 is also used to mean "no wind", but a radius of 0 means "could not be read"** —
    /// the display side must not conflate the two (whether the radius was readable is owned
    /// up to on the <c>TyphoonPrefabFacts</c> side).
    /// </summary>
    public static class TyphoonProfile
    {
        /// <summary>The eye's radius as a fraction of the gale radius.</summary>
        public const float EyeFraction = 0.12f;

        /// <summary>The eyewall's (the strongest ring's) radius as a fraction of the gale
        /// radius.</summary>
        public const float WallFraction = 0.34f;

        /// <summary>Strong-wind radius ÷ gale radius.</summary>
        public const float GaleFactor = 2.2f;

        /// <summary>
        /// The wind-speed equivalent at the **rim** of the eye. The centre is exactly 0
        /// (the middle of a real typhoon's eye is calm too), it rises linearly from there
        /// to this value at the rim, and reaches 1 between the rim and the eyewall.
        ///
        /// The pseudocode in plan §1.2 writes the eye's branch as
        /// <c>EyeWind + (1-EyeWind)·(d/eye)</c>, but that reaches exactly 1 at the rim of
        /// the eye, which cannot coexist with the eyewall branch that follows (which also
        /// says "1f at wall") — you would get a discontinuity of 1.0 just inside the rim
        /// and 0.15 just outside it. To preserve the structure where the eyewall is the
        /// strongest ring (which the tests pin), the eye's branch rises from 0 to EyeWind.
        /// </summary>
        public const float EyeWind = 0.15f;

        public const int Steps = 10;
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>
        /// The gale radius (m). **The same formula as vanilla's scatter radius and hazard
        /// disc**: R = prefabRadius × (0.25 + intensity × 0.0075) (§A-1 IL_0177–0191 / §A-2).
        ///
        /// If <paramref name="prefabRadius"/> is 0 or less, or NaN, the result is **0**
        /// (i.e. the prefab could not be read). It never returns a guessed radius.
        /// </summary>
        public static float StormRadiusOf(byte intensity, float prefabRadius)
        {
            // This rejects NaN along with the rest (NaN > 0 is false).
            if (!(prefabRadius > 0f)) return 0f;
            return prefabRadius * (0.25f + intensity * 0.0075f);
        }

        /// <summary>The strong-wind radius (m). <see cref="GaleFactor"/> times the gale
        /// radius.</summary>
        public static float GaleRadiusOf(byte intensity, float prefabRadius)
        {
            return StormRadiusOf(intensity, prefabRadius) * GaleFactor;
        }

        /// <summary>
        /// The wind-speed equivalent ([0, 1]) at a point <paramref name="distance"/> m from
        /// the centre.
        ///
        /// 0 at the centre of the eye → <see cref="EyeWind"/> at the rim of the eye →
        /// exactly 1 at the eyewall → exactly 0 at the edge of the strong-wind radius.
        /// Those two boundary points are pinned by the tests.
        /// Outside the strong-wind radius, a negative distance, or NaN, all give 0.
        /// </summary>
        public static float WindAt(float distance, byte intensity, float prefabRadius)
        {
            float storm = StormRadiusOf(intensity, prefabRadius);
            if (storm <= 0f) return 0f;

            float gale = storm * GaleFactor;

            // NaN falls out on the !(d >= 0) side.
            if (!(distance >= 0f) || distance >= gale) return 0f;

            float eye = storm * EyeFraction;
            float wall = storm * WallFraction;

            if (distance <= eye)
            {
                // eye is certain to be positive, since storm > 0 and EyeFraction > 0.
                return EyeWind * (distance / eye);
            }

            if (distance <= wall)
            {
                return EyeWind + (1f - EyeWind) * (distance - eye) / (wall - eye);
            }

            return 1f - (distance - wall) / (gale - wall);
        }

        /// <summary>
        /// Turns the wind-speed equivalent into a step from 0 to <see cref="Steps"/>.
        /// Monotonically increasing; 1 lands exactly on Steps.
        /// Same shape as ②'s <c>SeismicScale.StepOf</c> (NaN gives 0, out-of-range clamps).
        /// </summary>
        public static int StepOf(float wind)
        {
            if (float.IsNaN(wind) || wind <= 0f) return 0;
            if (wind >= 1f) return Steps;

            int step = (int)(wind * Steps);
            if (step < 0) step = 0;
            if (step > Steps) step = Steps;
            return step;
        }

        /// <summary>
        /// An ASCII bar of length <see cref="Steps"/>. The number filled matches
        /// <see cref="StepOf"/>.
        /// It is fixed ASCII for the same reason as ①'s <c>HazardLevel</c> and ②'s
        /// <c>SeismicScale</c>: there is no guarantee the CS UI font has the block-drawing
        /// characters (and without them you get tofu).
        /// </summary>
        public static string BarOf(float wind)
        {
            int filled = StepOf(wind);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }
    }
}
