using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The summit crater. **Part of the height profile**, not a hole carved afterwards.
    ///
    /// ── Why <c>MakeCrater</c> was dropped (2026-08-22, in-game report ①) ────────────
    ///
    /// The owner's report:
    ///
    /// > I think it would be better if the crater were generated as a depression from the
    /// > start, rather than being generated last of all.
    ///
    /// It used to call <c>DisasterHelpers.MakeCrater</c> **once, at the end** of the uplift.
    /// That method calls <c>TerrainModify.RefreshAllModifications()</c> at its top (IL fact
    /// §C-8), so **every call forces one full terrain flush**, and it cannot be put on a
    /// per-tick path. That is why it was "once at the end" — and **that is what decided the
    /// moment the depression came into existence**.
    ///
    /// Fold the crater <b>into the profile itself</b> and the constraint disappears
    /// entirely. The uplift writes "the absolute target at this moment" every tick (see
    /// <see cref="UpliftSchedule"/>), so if the depression is in the target shape from the
    /// start, the depression grows along with the mountain.
    /// Nothing in ⑤ calls <c>MakeCrater</c> any more.
    ///
    /// ── The two hard constraints are kept with nothing but a multiply and a <c>min</c> ─────
    ///
    /// <list type="number">
    /// <item><b>Never exceed the radius R by a single millimetre</b> — raise a place the
    ///   preparation (the destruction) has not reached and a road pulls the cell back down,
    ///   leaving a flat trench through the mountain (design doc §1.2).
    ///   The crater only ever works through <c>min</c>, so the 0 outside R stays 0.</item>
    /// <item><b>Never exceed the final height H by a single millimetre</b> — the ceiling is
    ///   1023.98 m (§C-10) and <see cref="VolcanoShape.HeightFor"/> reasons in terms of H.
    ///   <see cref="CeilingMetres"/> is always at most H, so <c>min(relief, ceiling)</c> is
    ///   always at most H too.</item>
    /// </list>
    ///
    /// ── ★★ The cone is raised to "reach H at the crater rim" (<see cref="SummitScale"/>) ───────
    ///
    /// Naively "subtracting a depression from a cone" makes **the crater disappear on a
    /// stratovolcano**. The cone drops <c>H × 0.12</c> (72 m by default) across the crater
    /// radius <c>cr = 0.12R</c>, while the crater's depth is at most 60 m under
    /// <c>CraterDepthOf</c>. Subtracting leaves the centre still higher than the rim, so you
    /// get **a rounded summit**, not a depression (confirmed by measurement).
    ///
    /// The correct shape is reality itself — "a crater is the scar left where the cone's
    /// summit was cut off" — and **the rim's height is the mountain's height**. So the cone
    /// is raised to
    ///
    /// <code>
    /// coneHeight = H / profileFraction(cr)      profileFraction = VolcanoShape.ProfileAt(form, cr, R, 1)
    /// </code>
    ///
    /// and everything inside it is shaved off by the ceiling. The result's maximum is
    /// <b>exactly H at d = cr</b>, and the crater floor is <c>H − depth</c>. The virtual
    /// summit (<c>coneHeight</c>) is never written to a single cell of terrain.
    /// It is <c>coneHeight = 1.136 H</c> for a stratovolcano, 1.029 H for a lava dome and
    /// 1.0004 H for a shield volcano (a shield's summit is flat to begin with, so it barely
    /// changes).
    ///
    /// The flanks get correspondingly steeper. **That is not a side effect but another face
    /// of the same fact**: the slopes of a cone with a crater extend up towards the summit
    /// that was cut off.
    ///
    /// ── How it grows (and that it meshes with the uplift) ──────────────────────────
    ///
    /// Since <c>UpliftSchedule.GrowthMetresAt(profile, H, p) = max(0, profile − H(1−p))</c>:
    ///
    /// <code>
    /// rim    growth = H·p                    (because the profile is H)
    /// floor  growth = max(0, H·p − depth)    (because the profile is H − depth)
    /// </code>
    ///
    /// In other words <b>the rim comes up first and the floor follows, lagging by depth</b>.
    /// The depression's depth is <c>min(H·p, depth)</c>, which fills up at progress
    /// <c>depth/H</c> (10% for the default stratovolcano), and **from there on it rises
    /// with the mountain at a constant depth**.
    /// "It is there as a depression from the start" is structurally guaranteed by that one
    /// line of arithmetic.
    ///
    /// ★ What <see cref="FloorMetresAt"/> returns is the height of that floor, and **the
    ///   vents (flames, ash plume, ejecta) sit on it**. Sit them on the summit and they
    ///   float above the depression for the whole time it is growing.
    /// </summary>
    public static class VolcanoCrater
    {
        /// <summary>
        /// The range over which the crater floor is flat (as a fraction of the crater
        /// radius). From here out to the rim it rises smoothly.
        /// **A flat floor is required** — the vent's flame is a disc with a radius, so with
        /// a bowl-shaped floor it sinks into the ground at its edges.
        /// </summary>
        public const float FloorFraction = 0.55f;

        /// <summary>
        /// How far the flank relief is allowed to act inside the crater (0 = the smooth cone
        /// itself).
        ///
        /// ★ **Without this the depression gets shallow.** Measured on a 16 m grid, the
        ///   relief (at strength 1) shaves 20-30 m off the crater rim, which on the default
        ///   stratovolcano <b>reduces a 60 m depression to 32 m</b> (see the crater table in
        ///   <c>tools/VolcanoPreview</c>). The floor is not shaved, because the ceiling
        ///   clamps it flat, so **only the difference disappears**.
        ///
        /// Physically the un-shaved version is right too — the radial gullies that score the
        /// flanks begin below the crater rim.
        /// </summary>
        public const float ReliefInsideCrater = 0.35f;

        /// <summary>The distance over which the relief returns to full (as a fraction of the
        /// crater radius). So as not to leave a step at the rim.</summary>
        public const float ReliefBlendRadiusFactor = 1.8f;

        /// <summary>
        /// The cap on the factor by which the cone is re-raised. **It is a value the bands
        /// of the forms (<c>MinRadiusOf</c>) never reach**, but <c>.cgs</c> can be edited by
        /// hand, so we stop here before the flanks go vertical.
        /// </summary>
        public const float MaxSummitScale = 2f;

        /// <summary>
        /// The factor (&gt;= 1) that raises the cone so the crater rim reaches the
        /// mountain's height H. The formula is in the class doc.
        /// **For input where a crater cannot exist (R &lt;= 0 / H &lt;= 0 / cr &gt;= R /
        /// NaN) it returns 1** — <see cref="CeilingMetres"/> then returns H as well, so the
        /// shape is the plain cone we had before.
        /// </summary>
        public static float SummitScale(VolcanoForm form, float radiusMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 1f;

            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            if (!(crater > 0f) || crater >= radiusMetres) return 1f;

            // The fraction a unit-height cone still has left at the crater radius.
            float fraction = VolcanoShape.ProfileAt(form, crater, radiusMetres, 1f);
            if (IsBad(fraction) || fraction <= 0f) return 1f;

            float scale = 1f / fraction;
            if (IsBad(scale) || scale < 1f) return 1f;
            return scale > MaxSummitScale ? MaxSummitScale : scale;
        }

        /// <summary>
        /// The "virtual summit height" (m) handed to the relief. **It is never written to a
        /// single cell of terrain** (everything inside is always shaved off by
        /// <see cref="CeilingMetres"/>).
        /// </summary>
        public static float ConeHeightMetres(VolcanoForm form, float radiusMetres,
                                             float heightMetres)
        {
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            return heightMetres * SummitScale(form, radiusMetres);
        }

        /// <summary>
        /// The greatest height allowed at this distance (m). Out to
        /// <see cref="FloorFraction"/> × the crater radius from the centre it is
        /// <c>H − depth</c>, from there it returns smoothly to <c>H</c> at the crater
        /// radius, and **outside that it is H throughout**.
        ///
        /// **It always returns a value in <c>[0, heightMetres]</c>.** That is the whole of
        /// constraint 2 from the class doc.
        /// </summary>
        public static float CeilingMetres(float distanceMetres, float radiusMetres,
                                          float heightMetres)
        {
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            if (IsBad(distanceMetres) || distanceMetres < 0f) return heightMetres;
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return heightMetres;

            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(crater > 0f) || !(depth > 0f)) return heightMetres;
            if (distanceMetres >= crater) return heightMetres;

            float ramp = SmoothStep(FloorFraction * crater, crater, distanceMetres);
            float ceiling = heightMetres - depth * (1f - ramp);
            if (ceiling < 0f) return 0f;
            return ceiling > heightMetres ? heightMetres : ceiling;
        }

        /// <summary>
        /// **This single method is the final shape ⑤ writes to the terrain** (both
        /// <c>Game/Volcano/VolcanoUplift</c> and <c>tools/VolcanoPreview</c> call it; do not
        /// write the formula in two places).
        ///
        /// It evaluates the relief (<see cref="VolcanoRelief"/>) at the **virtual summit
        /// height** and cuts it off with the crater's ceiling. The return value is always in
        /// <c>[0, heightMetres]</c>, and is always exactly 0 when
        /// <c>√(dx²+dz²) &gt;= radiusMetres</c>.
        /// </summary>
        public static float ProfileAt(VolcanoRelief relief, float dx, float dz,
                                      float radiusMetres, float heightMetres)
        {
            if (relief == null) return 0f;
            if (IsBad(dx) || IsBad(dz)) return 0f;
            if (IsBad(radiusMetres) || IsBad(heightMetres)) return 0f;
            if (radiusMetres <= 0f || heightMetres <= 0f) return 0f;

            float cone = ConeHeightMetres(relief.Form, radiusMetres, heightMetres);
            float raised = relief.ProfileAt(dx, dz, radiusMetres, cone);
            if (!(raised > 0f)) return 0f;

            float d = (float)Math.Sqrt(dx * dx + dz * dz);

            // ★ Dilute the relief inside the crater (<see cref="ReliefInsideCrater"/>).
            //   The relief only ever works in the direction of shaving away (see its own
            //   class doc), so the diluted value always lands between raised and the smooth
            //   cone — in other words **the upper bound is still the smooth cone**, and
            //   constraint 2 is not relaxed by a single millimetre.
            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            if (crater > 0f && d < crater * ReliefBlendRadiusFactor)
            {
                float smooth = VolcanoShape.ProfileAt(relief.Form, d, radiusMetres, cone);
                if (smooth > raised)
                {
                    float blend = SmoothStep(crater, crater * ReliefBlendRadiusFactor, d);
                    float keep = ReliefInsideCrater + (1f - ReliefInsideCrater) * blend;
                    raised = smooth + (raised - smooth) * keep;
                }
            }

            float ceiling = CeilingMetres(d, radiusMetres, heightMetres);
            return raised < ceiling ? raised : ceiling;
        }

        /// <summary>
        /// How far the crater floor has risen at this moment (m, relative to the original
        /// terrain height).
        /// <paramref name="summitMetres"/> is how far the rim has risen
        /// (<c>VolcanoUplift.SummitMetres</c>).
        ///
        /// **This is what decides the vents' Y.** Sit them on the rim and the flames appear
        /// to float above the depression for the whole time it is growing (in-game report ②).
        /// </summary>
        public static float FloorMetresAt(float summitMetres, float heightMetres)
        {
            if (IsBad(summitMetres) || summitMetres <= 0f) return 0f;

            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(depth > 0f)) return summitMetres;

            float floor = summitMetres - depth;
            return floor > 0f ? floor : 0f;
        }

        /// <summary>
        /// Whether the crater has reached its full depth (i.e. whether we may claim "the
        /// depression is complete"). That is the point at which the rim has risen by
        /// <c>depth</c>.
        /// </summary>
        public static bool FullDepthReached(float summitMetres, float heightMetres)
        {
            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(depth > 0f)) return false;
            return !IsBad(summitMetres) && summitMetres >= depth;
        }

        private static float SmoothStep(float from, float to, float t)
        {
            if (!(to > from)) return t >= to ? 1f : 0f;
            float u = (t - from) / (to - from);
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            return u * u * (3f - 2f * u);
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
