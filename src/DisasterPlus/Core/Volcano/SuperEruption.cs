using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **The caldera-forming (super) eruption.** <b>This is Core, so it never touches the
    /// engine.</b>
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; I think the volcano's 25.5 scale is too small. Please look into whether a
    /// &gt; caldera-forming eruption can be reproduced at 25.5 only, and implement it.
    /// &gt; A volcano forming as magma rises from below → a vast magma chamber growing over
    /// &gt; tens of thousands of years → a caldera-forming eruption (a huge explosion) as the
    /// &gt; internal pressure reaches its limit → a great collapse under the ground's own
    /// &gt; weight, forming a caldera.
    ///
    /// ── Can it be done (what the investigation found) ──────────────────────
    ///
    /// **Yes.** ⑤ already writes terrain (<c>VolcanoUplift</c>), and the final write
    /// (<c>UpliftSchedule.RawTargetAt</c>) <b>does not care about the profile's sign</b> —
    /// we already use a negative profile to carve the crater (its doc:
    /// "<c>profileMetres</c> **may be negative**").
    ///
    /// ★★ <b>But it was not "the same mechanism, unchanged".</b> (Discovered during
    ///   implementation, 2026-08-22.)
    ///
    ///   The cone goes through <c>UpliftSchedule.GrowthMetresAt</c> and grows by "spreading
    ///   outwards from the summit". At the top of that function sits
    ///
    /// <code>
    ///   if (profileMetres &lt;= 0f) return 0f;
    /// </code>
    ///
    ///   so <b>a negative profile is crushed to 0 there and the caldera is not dug a single
    ///   millimetre.</b> So the inflation and the collapse are written <b>without going
    ///   through the growth rule</b>, as <c>profile × progress</c> (i.e. everything moves
    ///   uniformly) — the <c>UpliftStage</c> branch in <c>VolcanoUplift.Terrain.cs</c>.
    ///   Neither the inflation nor the collapse is something that "spreads from the rim to
    ///   the centre", so moving uniformly was the right thing anyway.
    ///
    /// Not one new terrain-writing path is needed (taking the rectangle, saving the old
    /// heights, flushing and counting against the ceiling are all exactly as
    /// <c>VolcanoUplift</c> does them, across all three stages).
    ///
    /// ── The four stages (in exactly the order the request gave) ────────────────────
    ///
    /// | The request's words | ⑤'s phase | What happens to the terrain |
    /// |---|---|---|
    /// | A volcano forming as magma rises from below | <c>Uplifting</c> | The cone rises (as before) |
    /// | A vast magma chamber growing over tens of thousands of years | <c>Inflating</c> | **The whole foot lifts over a wide area** |
    /// | A caldera-forming eruption (a huge explosion) as the internal pressure reaches its limit | <c>Erupting</c> | The terrain does not move (plume and blast) |
    /// | A great collapse under the ground's own weight, forming a caldera | <c>Collapsing</c> | **The whole mountain founders into a flat-floored basin** |
    ///
    /// ★★ <b>The inflation lifts "the whole foot", not the mountain.</b> The magma chamber
    ///   is deeper and wider than the mountain, so the swelling at the surface is <b>an
    ///   extremely low dome spanning several times the mountain's radius</b> (that is the
    ///   ground uplift observed at real caldera volcanoes).
    ///   Raise only the mountain and all you get is "a taller mountain".
    ///
    /// ★★ <b>A caldera is not a conical hole.</b> The roof gives way and drops, so it has
    ///   <b>a flat floor and steep walls</b> (<see cref="BowlProfileAt"/>).
    ///   Carve a funnel and it reads as nothing but "a big crater".
    /// </summary>
    public static class SuperEruption
    {
        /// <summary>
        /// The raw slider value at which the eruption becomes caldera-forming. **Exactly the
        /// maximum** (displayed as 25.5).
        ///
        /// The owner's instruction was "at 25.5 only", so it does not happen at 24.9.
        /// <c>IntensityUnlock</c> opens the limit up to 255, so you only get this scale by
        /// pushing the slider all the way to the right.
        /// </summary>
        public const int RawThreshold = 255;

        /// <summary>How many times the mountain's radius the inflation's radius is. **The
        /// lift extends far beyond the foot.**</summary>
        public const float InflationRadiusFactor = 2.4f;

        /// <summary>What fraction of the mountain's height the inflation's height is. The
        /// point is that it is **low and wide**.</summary>
        public const float InflationHeightFraction = 0.22f;

        /// <summary>How many times the mountain's radius the caldera's radius is.</summary>
        public const float CalderaRadiusFactor = 1.9f;

        /// <summary>
        /// How far below the original ground the caldera's floor sits, <b>as a multiple of
        /// the mountain's height</b>.
        ///
        /// ── ★★ 1.35 was too much (2026-08-22, the owner's question) ──────────────
        ///
        /// &gt; Why does the elevation inside the caldera always end up below sea level?
        ///
        /// <b>This coefficient was the cause, plain and simple.</b> It was 1.35, capped at
        /// 900 m, so:
        ///
        /// <code>
        ///   a 1,000 m mountain → a depth of 1,350 m → capped at 900 m
        ///   ground at 120 m − 900 m = **−780 m**
        ///   the game's sea level is 40 m (WaterSimulation.DEFAULT_SEA_LEVEL, from the IL)
        ///   → 820 m below sea level. **Wherever you place it, it floods.**
        /// </code>
        ///
        /// A real caldera's floor is at most <b>a few hundred metres below the surrounding
        /// ground</b>. What drops is <b>the edifice</b>; the land around it does not sink
        /// 900 m with it (Yellowstone's floor is at roughly the same height as the
        /// surrounding plateau, and is not even a hole).
        ///
        /// At 0.35 capped at 400 m, you get <b>a dry caldera on high ground and a flooded one
        /// near the sea</b> — flooding, as at Santorini or Krakatoa, is a correct outcome,
        /// but <b>flooding "always" was wrong</b>.
        /// </summary>
        public const float CalderaDepthFactor = 0.35f;

        /// <summary>
        /// What fraction of the whole the caldera's floor takes up (flat out to here). The
        /// rest is wall.
        ///
        /// ★★ <b>At 0.55 the walls were far too gentle</b> (2026-08-22, noticed by drawing
        ///   the cross-section). At a radius of 5,563 m and a depth of 900 m, the wall spans
        ///   2,500 m horizontally, giving <b>about 20 degrees</b> — which reads as "a shallow
        ///   basin", not "a caldera".
        ///   A real caldera drops almost vertically along ring faults, and collapsed debris
        ///   piles up to give a 30-45 degree scarp. At 0.78 it drops 900 m over 1,220 m
        ///   horizontally, <b>about 36 degrees</b>, which is inside that band.
        /// </summary>
        public const float FloorFraction = 0.78f;

        /// <summary>The fraction at which we are fully back to the terrain outside the wall.
        /// Beyond this, nothing moves by even a metre.</summary>
        public const float RimFraction = 1.0f;

        /// <summary>The floor on the caldera's depth (m). Too shallow and it reads as "a big
        /// crater".</summary>
        public const float MinDepthMetres = 120f;

        /// <summary>The ceiling on the caldera's depth (m).</summary>
        public const float MaxDepthMetres = 400f;

        /// <summary>
        /// How rough the caldera's floor is (as a ratio of the depth).
        ///
        /// ★★ **The floor is not flat.** (2026-08-22, the owner's observation: "it's odd for
        ///   the inside of a caldera to be flat ground".) The roof that falls does not land
        ///   intact as a single slab; it becomes **a heap of fractured, collapsed blocks**
        ///   (collapse breccia). Pyroclastic flows then accumulate on top of that.
        /// </summary>
        public const float FloorRoughFraction = 0.22f;

        /// <summary>The spacing of the floor's roughness (m). The size of one block.</summary>
        public const float RoughWavelengthMetres = 340f;

        /// <summary>The spacing of the finer roughness (m).</summary>
        public const float FineRoughWavelengthMetres = 110f;

        /// <summary>
        /// The radius of the resurgent dome (as a ratio of the caldera's radius).
        ///
        /// ★ Every real large caldera (Yellowstone, Toba, Aso) has <b>a central high</b>
        ///   pushed back up from below after the collapse.
        ///   Without one it reads as "a plain bowl".
        /// </summary>
        public const float ResurgentRadiusFraction = 0.34f;

        /// <summary>The resurgent dome's height (as a ratio of the depth). **Above the floor,
        /// below the rim.**</summary>
        public const float ResurgentHeightFraction = 0.42f;

        /// <summary>The cap on the caldera's radius (m). The map is only 17,280 m on a
        /// side.</summary>
        public const float MaxRadiusMetres = 6000f;

        /// <summary>
        /// Whether this intensity gives a caldera-forming eruption. <paramref name="raw"/> is
        /// the slider's raw value.
        /// </summary>
        public static bool IsSuper(int raw)
        {
            return raw >= RawThreshold;
        }

        /// <summary>
        /// The radius (m) the magma chamber's inflation reaches.
        /// <see cref="InflationRadiusFactor"/> times the mountain's radius, cut at
        /// <see cref="MaxRadiusMetres"/>.
        /// </summary>
        public static float InflationRadiusMetres(float volcanoRadiusMetres)
        {
            if (IsBad(volcanoRadiusMetres) || volcanoRadiusMetres <= 0f) return 0f;

            float r = volcanoRadiusMetres * InflationRadiusFactor;
            return r > MaxRadiusMetres ? MaxRadiusMetres : r;
        }

        /// <summary>The inflation's maximum height (m). **Lower than the mountain's
        /// height.**</summary>
        public static float InflationHeightMetres(float volcanoHeightMetres)
        {
            if (IsBad(volcanoHeightMetres) || volcanoHeightMetres <= 0f) return 0f;
            return volcanoHeightMetres * InflationHeightFraction;
        }

        /// <summary>
        /// The inflation's shape (m, **positive**). Highest at the centre and exactly 0 at
        /// <paramref name="reachMetres"/>: <b>a very flat dome</b>.
        ///
        /// We use a <c>cos</c> bulge because both the height and the slope go to 0 at the
        /// edge — a parabola leaves a crease at the edge that reads as a cliff.
        /// </summary>
        public static float InflationAt(float distanceMetres, float reachMetres,
                                        float heightMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(reachMetres) || reachMetres <= 0f) return 0f;
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            if (distanceMetres >= reachMetres) return 0f;

            float t = distanceMetres / reachMetres;
            // (1 + cos(pi t)) / 2 — 1 at t=0, 0 at t=1, with zero slope at both ends.
            float bell = 0.5f * (1f + (float)Math.Cos(Math.PI * t));
            return heightMetres * bell;
        }

        /// <summary>The caldera's radius (m).</summary>
        public static float CalderaRadiusMetres(float volcanoRadiusMetres)
        {
            if (IsBad(volcanoRadiusMetres) || volcanoRadiusMetres <= 0f) return 0f;

            float r = volcanoRadiusMetres * CalderaRadiusFactor;
            return r > MaxRadiusMetres ? MaxRadiusMetres : r;
        }

        /// <summary>
        /// The caldera's depth (m, **a positive value**).
        /// <paramref name="volcanoHeightMetres"/> is the mountain's height, and it drops
        /// below that (see <see cref="CalderaDepthFactor"/>).
        /// </summary>
        public static float CalderaDepthMetres(float volcanoHeightMetres)
        {
            // ★★ **Do not synthesise a depth from a broken height.** Returning 0 means
            //   <see cref="BowlProfileAt"/> does not dig a single millimetre — return
            //   <c>MinDepthMetres</c> here and a value we could not read would produce a
            //   120 m basin (every other shape function returns 0).
            //   Caught by Codex's secondary review.
            if (IsBad(volcanoHeightMetres) || volcanoHeightMetres <= 0f) return 0f;

            float d = volcanoHeightMetres * CalderaDepthFactor;
            if (d < MinDepthMetres) return MinDepthMetres;
            if (d > MaxDepthMetres) return MaxDepthMetres;
            return d;
        }

        /// <summary>
        /// The caldera's shape (m, **negative**). <b>A flat floor and steep walls</b>, and
        /// exactly 0 beyond <paramref name="calderaRadiusMetres"/>.
        ///
        /// ★★ Do not make it a funnel (a conical hole) — that reads as nothing but "a big
        ///   crater". A caldera is the roof giving way and <b>dropping as a block</b>, so the
        ///   floor is flat (out to <see cref="FloorFraction"/>) and we join that to the rim
        ///   with a smooth step.
        /// </summary>
        public static float BowlProfileAt(float distanceMetres, float calderaRadiusMetres,
                                          float depthMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(calderaRadiusMetres) || calderaRadiusMetres <= 0f) return 0f;
            if (IsBad(depthMetres) || depthMetres <= 0f) return 0f;
            if (distanceMetres >= calderaRadiusMetres * RimFraction) return 0f;

            float t = distanceMetres / calderaRadiusMetres;

            // The floor is flat. **This is what distinguishes it from "a big crater".**
            if (t <= FloorFraction) return -depthMetres;

            // The wall. A smoothstep from floor to rim, with both depth and slope reaching 0
            // at the rim.
            float w = (t - FloorFraction) / (RimFraction - FloorFraction);
            if (w > 1f) w = 1f;
            float k = 1f - w * w * (3f - 2f * w);
            return -depthMetres * k;
        }

        /// <summary>
        /// The shape of the caldera's floor (m, **0 or negative**), for one cell.
        /// It is <see cref="BowlProfileAt"/>'s smooth bowl plus <b>a resurgent dome</b> and
        /// <b>the roughness of collapsed blocks</b>.
        ///
        /// ── Why the bowl alone will not do (2026-08-22, the owner's observation) ────────
        ///
        /// &gt; It's odd for the inside of a caldera to be flat ground (please take the
        /// &gt; original terrain and the remains of the volcanic edifice into account and
        /// &gt; bring it closer to reality)
        ///
        /// Quite right: <see cref="BowlProfileAt"/> returns <b>a dead flat floor</b>.
        /// In reality the fallen roof fractures into a heap of blocks (collapse breccia),
        /// pyroclastic flows accumulate on top of it, and in time the centre is pushed back
        /// up.
        ///
        /// ★ <b>The original terrain</b> is handled by the caller —
        ///   <c>VolcanoUplift.ProfileFor</c> drops from <b>that cell's real ground</b> as the
        ///   reference outside the edifice (it does not paint over everything with the height
        ///   at the single centre point). What this returns is "how far below that reference".
        /// </summary>
        /// <param name="dx">The distance from the centre (m, in X). Used for the roughness's
        /// phase.</param>
        /// <param name="dz">The same, in Z.</param>
        /// <param name="seed">This volcano's seed. The same place always gives the same
        /// floor.</param>
        public static float CalderaFloorOffsetAt(float dx, float dz,
                                                 float calderaRadiusMetres, float depthMetres,
                                                 uint seed)
        {
            if (IsBad(dx) || IsBad(dz)) return 0f;

            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float bowl = BowlProfileAt(distance, calderaRadiusMetres, depthMetres);
            if (!(bowl < 0f)) return 0f;

            // ★ Both the roughness and the resurgent dome **fade out towards the rim**.
            //   Without that you get blocks scattered across the flat ground outside the
            //   caldera.
            float rim = calderaRadiusMetres > 0f ? distance / calderaRadiusMetres : 1f;
            if (rim > 1f) rim = 1f;
            float inside = 1f - rim * rim;

            // ── The collapsed blocks (two spacings stacked) ──────────────────────
            float rough =
                VolcanoRelief.ValueNoise(dx / RoughWavelengthMetres,
                                         dz / RoughWavelengthMetres, seed) * 0.7f
                + VolcanoRelief.ValueNoise(dx / FineRoughWavelengthMetres,
                                           dz / FineRoughWavelengthMetres, seed + 7717u) * 0.3f;
            // ★★ <c>ValueNoise</c> is <b>already in [-1,1]</b> (see its doc).
            //    When this was written as (n*2-1), the actual range came out as [-3,1] and
            //    the floor dropped out to 1.4 times the depth (-505 m against a depth of
            //    350 m).
            //    **Do not bring over the habit of remapping [0,1] to [-1,1] from other
            //    types.**
            rough *= FloorRoughFraction * depthMetres * inside;

            // ── The resurgent dome ────────────────────────────────────────
            float dome = 0f;
            float domeRadius = calderaRadiusMetres * ResurgentRadiusFraction;
            if (domeRadius > 0f && distance < domeRadius)
            {
                float k = distance / domeRadius;
                // A cosine hump. Both the height and the slope reach 0 at its edge (so no
                // seam shows).
                dome = depthMetres * ResurgentHeightFraction
                       * 0.5f * (1f + (float)Math.Cos(Math.PI * k));
            }

            float offset = bowl + rough + dome;

            // ★★ **The floor never comes above the original ground.** If it did, islands at
            //   the original height would be left inside a caldera that is supposed to have
            //   foundered.
            if (offset > 0f) return 0f;
            return offset;
        }

        /// <summary>
        /// **How far the edifice founders** (m, **0 or negative**), for one cell.
        ///
        /// ── Why it is not "a subtraction" (2026-08-22, the owner's observation) ─────────
        ///
        /// &gt; When a caldera forms, doesn't the edifice drop a long way and explode
        /// &gt; enormously…?
        ///
        /// ⑤ originally <b>subtracted the depth from the current ground</b>. The cone was
        /// +1,000 m and the depth 900 m, so <b>a 100 m stump was left at the summit</b> with
        /// 900 m dug out only around it — a picture of "a trench dug around the mountain",
        /// not "the mountain dropping".
        ///
        /// In a real caldera <b>the roof drops as a single slab</b>, so the floor comes out
        /// <b>flat at one height below the original ground</b> and the edifice is gone
        /// without a trace. So we set the target as an absolute height
        /// (<c>original ground + bowl</c>) and make the amount we move the difference to it.
        ///
        /// <code>
        ///   summit    base=+1000  target = 0 - 900 = -900   → drops -1900
        ///   mid-slope base= +400  target = 0 - 900 = -900   → drops -1300
        ///   rim       base=    0  target = 0 -   0 =    0   →     0 (does not move)
        /// </code>
        ///
        /// ★★ <b>The collapse never raises the ground.</b>
        ///   <paramref name="groundMetres"/> is a single ground height at the volcano's
        ///   centre, so on sloping land the target at the outer edge can come out above the
        ///   current ground. Lifting there would make <b>the rim that ought to be dropping
        ///   rise instead</b>, so a positive difference is cut to 0.
        /// </summary>
        /// <param name="bowlMetres">
        /// <see cref="BowlProfileAt"/>'s value (0 or negative). The basin's shape itself.
        /// </param>
        /// <param name="baseMetres">This cell's **current** ground height (m, including the
        /// cone).</param>
        /// <param name="groundMetres">The ground height before the volcano was placed
        /// (m).</param>
        public static float FounderDropAt(float bowlMetres, float baseMetres, float groundMetres)
        {
            if (IsBad(bowlMetres) || IsBad(baseMetres) || IsBad(groundMetres)) return 0f;

            float drop = (groundMetres + bowlMetres) - baseMetres;
            return drop < 0f ? drop : 0f;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
