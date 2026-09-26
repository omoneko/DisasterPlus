namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>Far from the epicentre, fires and collapses still happen — at a rate that
    /// scales with the magnitude.</b> The model behind the trench earthquake.
    /// **Engine-free.**
    ///
    /// ── What the owner asked for (2026-09-02) ─────────────────────────────
    ///
    /// &gt; The trench earthquake does too little damage. Make fires and collapses
    /// &gt; happen at some rate even far from the epicentre (scaled to the magnitude).
    ///
    /// ── ★★ Why the trench quake <b>alone</b> was so harmless (the cause is known) ──
    ///
    /// Vanilla earthquake damage comes out of <c>EarthquakeAI.SimulationStep</c> as
    ///
    /// <code>
    /// R = 2000 + m_intensity * 20                     (§A-3, SeismicIntensity)
    /// DestroyBuildings(preRadius: R, min: 0, max: R, probability: 0.02f)
    ///   each building is multiplied by fD = 1 - dist/R
    /// </code>
    ///
    /// — a <b>disc centred on the epicentre</b>. For a fault quake the epicentre is
    /// <b>wherever the player clicked</b>, so that disc lands on the city.
    ///
    /// ★★ **A trench quake's epicentre is out at sea** (that is the definition, see
    ///   <c>TrenchQuakeSlot</c>). So the centre of the disc falls on water and all the
    ///   city ever gets is <b>the thinnest part of the rim</b>. At intensity 100,
    ///   R = 4,000 m; 3 km offshore gives fD = 0.25, so the effective chance is
    ///   <c>0.02 × 0.25 = 0.5%</c> — <b>that is what "too little damage" really was.
    ///   Not a bug in the game, just the inevitable result of putting a disc model
    ///   out at sea.</b>
    ///
    /// ── Real megathrust earthquakes do not behave that way ─────────────────
    ///
    /// A plate-boundary rupture runs for <b>hundreds of kilometres</b>, so it does not
    /// fall off neatly with distance from the epicentre. Far away the shaking still
    /// lasts a long time, and <b>fires break out here and there from toppled heaters
    /// and electrical faults</b> — which depends on whether there are buildings there,
    /// not on the distance to the epicentre.
    ///
    /// So this model <b>has a floor (<see cref="FloorFraction"/>)</b>. Even far out,
    /// <see cref="FloorFraction"/> of the near-field rate survives. That is what the
    /// owner meant by "at some rate even far away".
    ///
    /// ── Tying it to the magnitude ("scaled to the magnitude") ──────────────
    ///
    /// Intensity <c>i</c> acts as <c>(i/255)²</c>. **Not linear.** Linear would make
    /// the default slider (55) do a fifth of the damage of the maximum (255), which
    /// erases the distinction that only a great earthquake wrecks the city. Squared,
    /// it is a twenty-first.
    ///
    /// Rough figures. **These were counted offline, not estimated** (strength slider
    /// at its default of 6, a city of 8,000 buildings in a 4 km square, epicentre
    /// 3 km off its coast):
    ///
    /// | Intensity | Reach | Collapsed | Set alight |
    /// |------|------|------|------|
    /// | 55   |  9,300 m |   9 |  14 |
    /// | 100  | 12,000 m |  32 |  53 |
    /// | 150  | 15,000 m |  77 | 129 |
    /// | 255  | 21,300 m | 243 | 405 |
    ///
    /// Under the same conditions vanilla's disc hands out <b>0.50%</b> at intensity
    /// 100; this model gives <b>1.24%</b> (collapses and fires together), about 2.5×.
    ///
    /// ── The draw depends on (quake, building) and nothing else ─────────────
    ///
    /// ★★ **Never mix in the frame or the sweep count** (the same discipline as
    ///   <c>LongPeriodDamage</c>). Mix them in and the same building is re-drawn on
    ///   every sweep, so <b>the longer the quake runs the more it destroys, without
    ///   limit</b>. Leave them out and the table above <b>holds no matter how many
    ///   sweeps run</b>.
    /// </summary>
    public static class DistantDamage
    {
        /// <summary>
        /// Reach ÷ vanilla's whole-quake disc R.
        ///
        /// ★ It is 3× so that **intensity 255 reaches 21,300 m — the whole map** (even
        ///   across all 25 tiles, half the diagonal is only about 12,200 m). At the
        ///   default slider (55) it is still 9,300 m, which covers most of a 9-tile
        ///   city. A trench epicentre sits offshore, so if this were short the damage
        ///   <b>would never reach the city at all</b>.
        /// </summary>
        public const float ReachFactor = 3f;

        /// <summary>
        /// The fraction left at the far edge. **This is what "at some rate even far
        /// away" actually means.** Set it to 0 and the damage fades with distance
        /// again, back to the untouched-far-away behaviour the owner complained about.
        /// </summary>
        public const float FloorFraction = 0.35f;

        /// <summary>
        /// Where the floor starts being taken away, as a fraction of the reach.
        ///
        /// ★ Without this the chance drops <b>in a step</b> from
        ///   <see cref="FloorFraction"/> to 0 at the edge of the reach. Damage
        ///   stopping dead along a line on the map is visible, so the outer quarter
        ///   eases it down to 0 instead.
        /// </summary>
        public const float TaperStart = 0.75f;

        /// <summary>Collapse chance at intensity 255, at the epicentre, at full strength.</summary>
        public const float PeakCollapseChance = 0.06f;

        /// <summary>
        /// The same for fires. **Higher than the collapse chance.** What burns a city
        /// down after a megathrust is mostly fire; collapses cluster where the shaking
        /// is strongest (1923, 1995 and 2011 all went that way).
        /// </summary>
        public const float PeakFireChance = 0.10f;

        /// <summary>Ceiling on the collapse chance for one building.</summary>
        public const float MaxCollapseChance = 0.25f;

        /// <summary>Ceiling on the fire chance for one building.</summary>
        public const float MaxFireChance = 0.35f;

        /// <summary>Full scale on the strength slider. <c>strength / 10</c> is the multiplier.</summary>
        public const float MaxStrength = 10f;

        /// <summary>
        /// Salt for the fire draw. <b>Do not draw it with the same key as the
        /// collapse</b> — the same key would correlate the two, making a building that
        /// survived the collapse draw less likely to catch fire, so the two misfortunes
        /// would stop being independent.
        /// </summary>
        public const uint FireSalt = 0x5EA1F17Eu;

        /// <summary>
        /// How far this damage reaches (m): <see cref="ReachFactor"/> × vanilla's
        /// whole-quake disc.
        ///
        /// ★ This is <b>the range over which this mod adds damage</b>, not a physical
        ///   boundary of the shaking (the same caveat as
        ///   <c>LongPeriodResponse.RangeOf</c>).
        /// </summary>
        public static float ReachMetres(byte intensity)
        {
            return SeismicIntensity.RadiusOf(intensity) * ReachFactor;
        }

        /// <summary>
        /// Falloff with distance ([0, 1]): 1 at the epicentre, 0 at the far edge.
        ///
        /// <code>
        /// u = d / reach
        /// f = Floor + (1 - Floor) * (1 - u)          the floor survives far out
        /// if (u &gt; TaperStart) f *= (1 - u) / (1 - TaperStart)    eased to 0 at the edge
        /// </code>
        /// </summary>
        public static float Falloff(float distance, byte intensity)
        {
            float reach = ReachMetres(intensity);
            if (!(reach > 0f)) return 0f;

            // NaN falls out on the !(d >= 0) side.
            if (!(distance >= 0f)) return 0f;
            if (distance >= reach) return 0f;

            float u = distance / reach;
            float f = FloorFraction + (1f - FloorFraction) * (1f - u);

            if (u > TaperStart)
            {
                f *= (1f - u) / (1f - TaperStart);
            }

            if (f < 0f) return 0f;
            return f > 1f ? 1f : f;
        }

        /// <summary>
        /// How the magnitude acts ([0, 1]): <c>(i/255)²</c> (see the class doc).
        /// </summary>
        public static float MagnitudeFactor(byte intensity)
        {
            float t = intensity / 255f;
            return t * t;
        }

        /// <summary>
        /// The chance this building collapses. **Every condition that returns 0 errs
        /// on the side of destroying nothing.**
        /// </summary>
        /// <param name="distance">Horizontal distance from the epicentre (m).</param>
        /// <param name="intensity">The raw <c>DisasterData.m_intensity</c>.</param>
        /// <param name="strength">The strength setting (0-10). 0 disables it completely.</param>
        public static float CollapseChance(float distance, byte intensity, float strength)
        {
            return ChanceOf(distance, intensity, strength,
                            PeakCollapseChance, MaxCollapseChance);
        }

        /// <summary>
        /// The chance this building catches fire. Same conditions as
        /// <see cref="CollapseChance"/>.
        /// </summary>
        public static float FireChance(float distance, byte intensity, float strength)
        {
            return ChanceOf(distance, intensity, strength, PeakFireChance, MaxFireChance);
        }

        private static float ChanceOf(float distance, byte intensity, float strength,
                                      float peak, float max)
        {
            if (float.IsNaN(distance) || float.IsNaN(strength)) return 0f;
            if (strength <= 0f) return 0f;

            float scale = strength / MaxStrength;
            if (scale > 1f) scale = 1f;

            float chance = peak * MagnitudeFactor(intensity) * Falloff(distance, intensity)
                           * scale;

            if (chance < 0f) return 0f;
            return chance > max ? max : chance;
        }
    }
}
