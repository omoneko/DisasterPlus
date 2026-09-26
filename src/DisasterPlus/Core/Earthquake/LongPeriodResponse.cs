namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The response model for long-period ground motion. **This is physics this mod
    /// invented; there is no corresponding value in vanilla, nor in real seismology.**
    ///
    /// Vanilla's shaking is just two sinusoids at 0.63 and 0.17 rad/frame, and the
    /// low-frequency component is a 256-frame envelope (a pulsing of the amplitude), not
    /// ground motion (IL findings doc §A-7). Building height enters neither the shaking nor
    /// the damage at all (<c>DisasterHelpers.DestroyBuildings</c> looks only at the
    /// distance between <c>m_position</c>s, §A-3). So what is here is not "visualisation"
    /// but "addition".
    ///
    /// **Every unit is sim frames.** Not seconds, and not real periods. Only the **shape**
    /// of the real-world relation T ≈ 0.02h [seconds] is borrowed, with frames substituted
    /// for the unit. So <see cref="PeriodFramesPerMetre"/> = 4 is not a physical constant
    /// but a gameplay tuning value meaning "buildings around 52 m tall get shaken the
    /// most". **Do not claim it as a real architectural or seismological figure.**
    ///
    /// Callers must always present this value as second-layer (<c>Strings.SourceModel</c>).
    ///
    /// ── Never, ever add it to the waveform graph (and its relation to whole-mod review I5) ───
    ///
    /// During the period when the first layer's waveform graph took only one point every
    /// nine frames at speed 3, it was drawing **a fake long-period wave with a period of
    /// ≈92 frames** (aliasing). That looked exactly like "long-period ground motion", and
    /// since §A-7 establishes that vanilla has no long-period component, the first layer's
    /// graph was drawing a second-layer phenomenon. The fix was backfilling one point per
    /// frame via <see cref="ShakeWaveform.FirstUnsampledFrame"/>, which created a state
    /// where **a low-frequency component appearing on the graph is still a defect**.
    ///
    /// **This model does not destroy that state.** The low-frequency component made here
    /// <b>produces no displacement whatsoever</b> — it enters only the damage-selection
    /// probability, and adds not one term to <see cref="ShakeWaveform"/> or to
    /// <c>SeismographRecorder</c>. So "a long-period wobble visible in the waveform"
    /// remains, even with this feature switched on, **impossible to explain as anything but
    /// a sampling defect**.
    /// If someone is ever tempted to feed this into the waveform, understand first that
    /// doing so destroys that distinction at a stroke.
    /// </summary>
    public static class LongPeriodResponse
    {
        /// <summary>
        /// The angular rate of the long-period component (rad/frame). An order of magnitude
        /// below vanilla's 0.63 / 0.17.
        /// **A value this mod chose; it is neither measured nor real.**
        /// </summary>
        public const float WaveRateRadPerFrame = 0.03f;

        /// <summary>
        /// The natural period (frames) per metre of building height.
        /// A tuning value that borrows only the **shape** of the real T ≈ 0.02h [seconds].
        /// </summary>
        public const float PeriodFramesPerMetre = 4f;

        /// <summary>How sharp the resonance is. The larger it is, the wider the band of
        /// heights affected (frames).</summary>
        public const float ResonanceWidthFrames = 60f;

        /// <summary>
        /// How many times vanilla's whole-quake disc <c>R = 2000 + 20i</c> the reach is.
        /// A multiplier whose only job is to express "it carries further than the
        /// short-period motion".
        /// </summary>
        public const float RangeFactor = 2f;

        /// <summary>
        /// Buildings below this height are out of scope. A threshold that exists **to state
        /// the assumption** that low-rise buildings are not affected by long-period motion;
        /// it is not a measured boundary.
        /// </summary>
        public const float MinHeightMetres = 20f;

        /// <summary>
        /// The cap on the additional collapse probability <see cref="ExtraCollapseChance"/>
        /// returns per sweep.
        ///
        /// **Against vanilla's whole-quake disc at 0.02, this is at most 0.25 — an order of
        /// magnitude larger.** That is deliberate, and it is precisely why this feature is
        /// off by default and can be held back with the strength slider.
        ///
        /// ── **This is not the cap on the probability actually used** (second-layer review M8) ───
        ///
        /// The callers (<c>LongPeriodDamage.IsSelected</c>, and
        /// <c>EarthquakeLongPeriodText.CursorRow</c>, which mirrors the same ordering)
        /// multiply by <c>TimeOfDayFactor.Of(hour)</c> (at most
        /// <c>TimeOfDayFactor.NightFactor</c> = 1.15) **after** this clamp. So the real cap
        /// that reaches the screen and the damage selection is
        /// <c>0.25 × 1.15 = 0.2875</c>.
        ///
        /// The order is not swapped because we want this constant to keep meaning "**the
        /// largest value this model itself produces**"; the time-of-day factor is a separate
        /// quantity applied from outside the model.
        /// In exchange, everywhere that states the cap (this doc and the model row of the
        /// diagnostic dump) **must go as far as saying 0.2875** — writing only 0.25 would
        /// make it "a confidently wrong number".
        /// </summary>
        public const float MaxExtraChance = 0.25f;

        /// <summary>Full scale on the strength slider. <c>strength / 10</c> is the
        /// multiplier.</summary>
        public const float MaxStrength = 10f;

        /// <summary>The long-period component's period (frames) =
        /// <c>2π / WaveRateRadPerFrame</c> ≈ 209.</summary>
        public static float WavePeriodFrames
        {
            get { return (float)(2.0 * System.Math.PI / WaveRateRadPerFrame); }
        }

        /// <summary>
        /// A building's natural period (frames). It is simply proportional to height and
        /// looks at neither mass, nor stiffness, nor the number of storeys.
        /// **This is not an approximation; it is just a gameplay mapping.**
        /// </summary>
        public static float BuildingPeriodFrames(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0f;
            return heightMetres * PeriodFramesPerMetre;
        }

        /// <summary>
        /// The resonance multiplier ∈ (0, 1]. Exactly 1 at
        /// <see cref="WavePeriodFrames"/>, approaching 0 the further away you get
        /// (Lorentzian).
        /// </summary>
        public static float Resonance(float buildingPeriodFrames)
        {
            if (float.IsNaN(buildingPeriodFrames)) return 0f;

            float detune = (buildingPeriodFrames - WavePeriodFrames) / ResonanceWidthFrames;
            float r = 1f / (1f + detune * detune);
            if (r < 0f) return 0f;
            return r > 1f ? 1f : r;
        }

        /// <summary>
        /// How far the long-period component reaches (m). <see cref="RangeFactor"/> times
        /// the whole-quake disc.
        /// **There is no radius cut-off on the shaking itself** (§A-7), so this is "the
        /// range over which this mod adds this damage", not a physical boundary.
        /// </summary>
        public static float RangeOf(byte intensity)
        {
            return SeismicIntensity.RadiusOf(intensity) * RangeFactor;
        }

        /// <summary>
        /// The additional collapse probability added per sweep.
        ///
        /// <code>
        /// Resonance(h*4) × (1 - d/Range) × (strength/10) × 0.25
        /// </code>
        ///
        /// **Every condition that returns 0 errs on the side of destroying nothing**
        /// (height unreadable, low-rise, out of range, strength 0, broken input). Applying
        /// "the taller it is the more it breaks" when the height is unknown would be the
        /// form of lie this mod detests most.
        /// </summary>
        /// <param name="heightMetres">The building's height (m). **0 means "could not be
        /// read"**.</param>
        /// <param name="distance">Horizontal distance from the epicentre (m).</param>
        /// <param name="intensity">The raw <c>DisasterData.m_intensity</c>.</param>
        /// <param name="strength">The strength setting (0-10). 0 disables it completely.</param>
        public static float ExtraCollapseChance(float heightMetres, float distance,
                                                byte intensity, float strength)
        {
            if (float.IsNaN(heightMetres) || float.IsNaN(distance) || float.IsNaN(strength))
            {
                return 0f;
            }
            if (heightMetres < MinHeightMetres) return 0f;
            if (strength <= 0f) return 0f;

            float range = RangeOf(intensity);
            if (range <= 0f) return 0f;
            if (distance < 0f) distance = 0f;
            if (distance >= range) return 0f;

            float scale = strength / MaxStrength;
            if (scale > 1f) scale = 1f;

            float falloff = 1f - distance / range;
            float chance = Resonance(BuildingPeriodFrames(heightMetres))
                           * falloff * scale * MaxExtraChance;

            if (chance < 0f) return 0f;
            return chance > MaxExtraChance ? MaxExtraChance : chance;
        }
    }
}
