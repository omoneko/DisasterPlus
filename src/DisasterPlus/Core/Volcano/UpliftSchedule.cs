using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// How the uplift progresses. **It returns the absolute target at a given moment, not an
    /// increment.**
    ///
    /// The naive <c>raw[i] += step</c> is certain to break silently. RawHeights is ushort in
    /// units of raw/64 m, and the writing side skips with <c>if (n != raw)</c> (§C-8). If one
    /// tick's change in a cell's height falls below 1/64 m = 0.015625 m it **vanishes in the
    /// rounding and that cell never moves again**. Cells further out rise more slowly, so in
    /// the naive implementation **the foot of the mountain stops dead from the very start**.
    /// Not one exception is raised.
    ///
    /// With an absolute target, a cell that has not yet reached one raw unit is merely "not
    /// written", and the progress is accumulated in the float called progress. The moment it
    /// gets there, it steps up by one.
    ///
    /// **Even so, the summit must move every tick.** If it does not, the uplift itself looks
    /// stopped, and <see cref="TotalTicksFor"/> guarantees it structurally by truncating the
    /// requested tick count at H×64. **Do not remove that truncation.**
    ///
    /// <see cref="ActiveRadiusMetres"/> is the single most important line in all of ⑤.
    /// **Never raise ground that the preparation (destroying roads and buildings) has not
    /// reached.** With m_flattenTerrain, a road **pins** its cells to the road's y via
    /// Heights.PrimaryLevel, and a building pins them to its own y via SecondaryLevel.
    /// What is more, that is redone from scratch on every flush (§A-2), so **you cannot win
    /// by writing harder**. Without preparing first you are left with flat trenches and
    /// bowls inside the mountain (design document §1.2).
    /// When clearedRadius is 0 (i.e. nothing has been destroyed yet) it returns 0.
    /// **That must not be reinterpreted as "no limit".**
    ///
    /// <see cref="BlockHeightCatchUpFrames"/> is how far "the buildable ground" and the water
    /// level lag behind. In game mode m_blockHeights only moves up 2 m per 64 sim frames
    /// (§A-2), and WaterSimulation.m_heightBuffer is that very array (§A-4). **This is not a
    /// defect.** Callers should explain this value to the player (design document §7.3).
    /// </summary>
    public static class UpliftSchedule
    {
        /// <summary>The representable limit of <c>RawHeights</c>. **Always clamp to this
        /// before the cast.**</summary>
        public const int MaxRaw = 65535;

        /// <summary>Raw units per metre (the same value as
        /// <see cref="VolcanoShape.RawUnitsPerMetre"/>).</summary>
        public const float RawUnitsPerMetre = 64f;

        /// <summary>
        /// How much raw <c>m_blockHeights</c> can move upwards per cycle (§A-2, game mode).
        /// 128 raw = 2 m. In the editor it is 512 raw = 8 m, but ⑤ only claims game mode.
        /// </summary>
        public const int BlockHeightRiseRawPerCycle = 128;

        /// <summary>One cycle of <c>m_blockHeights</c> (sim frames). The 1/64-of-the-rows
        /// sweep from §A-2.</summary>
        public const int BlockHeightCycleFrames = 64;

        /// <summary>
        /// The number of ticks the uplift uses. **The requested value is truncated at H×64**
        /// (trap 2).
        ///
        /// An H-metre mountain can only be divided into H×64 raw units at most. Divide it
        /// more finely than that and the summit's change per tick falls below one raw unit,
        /// vanishes in the rounding and the uplift stops silently. **Do not remove this
        /// truncation thinking it is meddling.**
        /// </summary>
        public static int TotalTicksFor(float heightMetres, int requestedTicks)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f)
            {
                return requestedTicks > 1 ? requestedTicks : 1;
            }

            int maxTicks = (int)(heightMetres * RawUnitsPerMetre);
            if (maxTicks < 1) maxTicks = 1;

            if (requestedTicks < 1) return 1;
            return requestedTicks > maxTicks ? maxTicks : requestedTicks;
        }

        /// <summary>Tick number → progress in [0,1]. 0 when <paramref name="totalTicks"/> is
        /// zero or below.</summary>
        public static float ProgressAt(int tick, int totalTicks)
        {
            if (totalTicks <= 0) return 0f;
            if (tick <= 0) return 0f;
            if (tick >= totalTicks) return 1f;
            return tick / (float)totalTicks;
        }

        /// <summary>
        /// **The uplift spreading outwards from the summit** (m). How far this point has
        /// risen at <paramref name="progress"/>.
        ///
        /// ── Why not <c>profile × progress</c> ──────────────
        ///
        /// <c>profile × progress</c> makes **the whole mountain swell uniformly**. The
        /// finished mountain looks as if it were being silently inflated out of the ground,
        /// and the order is the reverse of SimCity 4's uplift. There, erupted material
        /// **piles up** into a mountain — the summit rises first and the foot spreads
        /// outwards afterwards.
        ///
        /// Writing that down directly gives "sink the final shape by H(1−p) and the mountain
        /// right now is whatever sticks out of the ground":
        ///
        /// <code>
        /// grown(d, p) = max(0, profile(d) − H·(1 − p))
        /// </code>
        ///
        /// At p, what is above ground is the region <c>profile(d) &gt; H(1−p)</c>, that is
        /// **a small cone around the summit**, and it spreads outwards with p.
        /// For a straight cone (a stratovolcano) the front is exactly <c>R·p</c>, which meshes
        /// with the preparation front that <see cref="ClearingFrontMetres"/> runs ahead of it.
        ///
        /// ── Against trap 2 it is <b>stronger, not weaker</b> ───────────────────
        ///
        /// Every cell that is currently growing rises at <b>the same rate</b>, H/totalTicks
        /// (differentiate with respect to p and you get H). Since
        /// <see cref="TotalTicksFor"/> truncates totalTicks at H×64, this is always at least
        /// one raw unit per tick.
        /// With <c>profile × progress</c> the change per tick got smaller further out, and
        /// **"cells whose total rise is small" vanished in the rounding**. This formula has
        /// no such place.
        ///
        /// Bad input gives 0. Only when <paramref name="heightMetres"/> is zero or below do
        /// we fall back to the old proportional result (without knowing H there is no way to
        /// decide how far to sink it).
        /// </summary>
        public static float GrowthMetresAt(float profileMetres, float heightMetres, float progress)
        {
            if (float.IsNaN(profileMetres) || float.IsNaN(heightMetres) || float.IsNaN(progress))
            {
                return 0f;
            }
            if (profileMetres <= 0f) return 0f;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            if (p >= 1f) return profileMetres;
            if (heightMetres <= 0f) return profileMetres * p;

            float grown = profileMetres - heightMetres * (1f - p);
            return grown > 0f ? grown : 0f;
        }

        /// <summary>
        /// This cell's **absolute target** raw height at this moment.
        ///
        /// <paramref name="progress"/> is clamped to [0,1], but
        /// <paramref name="profileMetres"/> **may be negative** (the crater-carving side uses
        /// that).
        /// The result is always in [0, <see cref="MaxRaw"/>]. **Clamp before the cast to
        /// ushort** — without it the summit wraps around to sea level.
        /// </summary>
        public static ushort RawTargetAt(ushort baseRaw, float profileMetres, float progress)
        {
            if (float.IsNaN(profileMetres) || float.IsNaN(progress)) return baseRaw;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            int delta = (int)Math.Round((double)profileMetres * p * RawUnitsPerMetre);

            int v = baseRaw + delta;
            if (v < 0) v = 0;
            if (v > MaxRaw) v = MaxRaw;
            return (ushort)v;
        }

        /// <summary>
        /// Whether this cell **hit the game's height ceiling and got clipped**.
        ///
        /// ── The ceiling is 1024 m and cannot be raised from a mod (measured from the IL) ──
        ///
        /// | What was measured | Value |
        /// |---|---|
        /// | the type of <c>TerrainManager.m_rawHeights</c> | <c>ushort[]</c> (max 65535) |
        /// | the raw → metres conversion | <c>0.015625</c> (= 1/64), **hard-coded at each call site** |
        /// | <c>TerrainManager.TERRAIN_HEIGHT</c> | <c>const int = 1024</c> |
        ///
        /// So the height limit is <c>65535 / 64 = 1023.98 m</c>.
        /// **A mod cannot extend it**:
        ///
        ///   1. As long as the array is <c>ushort[]</c>, no value above 65535 fits in it
        ///   2. <c>TERRAIN_HEIGHT</c> is <c>const</c> (a compile-time literal), so it is
        ///      <b>baked into every place it is used</b>. Rewriting the field changes nothing
        ///   3. The rendering side receives the heights as a **texture**
        ///      (<c>_TerrainHeight</c>), and the vertical scale lives inside a compiled
        ///      shader. Even if you rewrote everything on the managed side, **the terrain
        ///      would still be drawn at the old scale**
        ///
        /// So ⑤ does not try to raise the ceiling; it **says that it hit it**.
        /// Quietly producing a flat summit is exactly the sort of thing this mod avoids most.
        /// </summary>
        public static bool CeilingClipped(ushort baseRaw, float profileMetres, float progress)
        {
            if (float.IsNaN(profileMetres) || float.IsNaN(progress)) return false;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            int delta = (int)Math.Round((double)profileMetres * p * RawUnitsPerMetre);
            return baseRaw + delta > MaxRaw;
        }

        /// <summary>The ceiling height (m). <c>65535 / 64</c>.</summary>
        public const float CeilingMetres = MaxRaw / RawUnitsPerMetre;

        /// <summary>
        /// **The single most important line in all of ⑤** (trap 1). The radius (m) it is
        /// permitted to raise.
        ///
        /// The only thing that may be passed as <paramref name="clearedRadiusMetres"/> is
        /// <c>VolcanoClearing.ClearedRadiusMetres</c> (the type constraint from plan T5→T6).
        /// **When nothing has been destroyed yet (zero and below, or NaN) it returns 0.**
        /// Reinterpret that as "no limit" and the uplift raises the whole area at once, the
        /// roads and buildings push it back on every flush, and you are left with flat
        /// trenches and bowls inside the mountain.
        /// </summary>
        public static float ActiveRadiusMetres(float shapeRadiusMetres, float clearedRadiusMetres)
        {
            if (float.IsNaN(shapeRadiusMetres) || float.IsNaN(clearedRadiusMetres)) return 0f;
            if (shapeRadiusMetres <= 0f || clearedRadiusMetres <= 0f) return 0f;
            return shapeRadiusMetres < clearedRadiusMetres ? shapeRadiusMetres : clearedRadiusMetres;
        }

        /// <summary>
        /// How far the uplift front has come at progress <paramref name="progress"/> (as a
        /// ratio of the radius).
        ///
        /// <c>GrowthMetresAt</c> brings only the region <c>profile(d) &gt; H(1−p)</c> above
        /// ground. For a straight cone (a stratovolcano), writing the factor by which the
        /// profile was rebuilt to allow for the crater as <paramref name="summitScale"/>, we
        /// have <c>profile(d) = H·scale·(1 − d/R)</c>, so the front is exactly
        ///
        /// <code>
        /// front / R = 1 − (1 − p) / scale
        /// </code>
        ///
        /// **At a factor of 1 it reduces to plain p, as before.**
        ///
        /// ★ The fronts of shield and dome volcanoes run slightly ahead of this formula
        ///   (their profiles are not straight). That is absorbed by
        ///   <c>ClearingFrontMetres</c>'s <c>leadMetres</c> — the relationship was the same
        ///   before the crater was introduced, so nothing here has newly become lax.
        /// </summary>
        public static float GrowthFrontUnit(float progress, float summitScale)
        {
            if (float.IsNaN(progress)) return 0f;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            if (float.IsNaN(summitScale) || summitScale < 1f) return p;

            float front = 1f - (1f - p) / summitScale;
            if (front < 0f) return 0f;
            return front > 1f ? 1f : front;
        }

        /// <summary>
        /// Where the preparation (destruction) front should have got to by now (m).
        /// It runs <paramref name="leadMetres"/> ahead of the uplift front
        /// (<c>shapeRadius × progress</c>) and never exceeds the mountain's radius.
        /// **It never moves backwards.**
        /// </summary>
        public static float ClearingFrontMetres(float shapeRadiusMetres, float progress,
                                                float leadMetres)
        {
            if (float.IsNaN(shapeRadiusMetres) || float.IsNaN(progress)) return 0f;
            if (shapeRadiusMetres <= 0f) return 0f;

            float p = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            float lead = float.IsNaN(leadMetres) || leadMetres < 0f ? 0f : leadMetres;

            float front = shapeRadiusMetres * p + lead;
            return front > shapeRadiusMetres ? shapeRadiusMetres : front;
        }

        /// <summary>
        /// How many sim frames it takes "the buildable ground" and the water level to catch
        /// up.
        ///
        /// In game mode <c>m_blockHeights</c> only moves up 2 m per 64 sim frames (§A-2), and
        /// the water simulation's <c>m_heightBuffer</c> is that very array (§A-4).
        /// **This is not a defect.** Callers should explain this value to the player.
        /// </summary>
        public static int BlockHeightCatchUpFrames(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0;

            float metresPerCycle = BlockHeightRiseRawPerCycle / RawUnitsPerMetre;
            int cycles = (int)Math.Ceiling(heightMetres / metresPerCycle);
            return cycles * BlockHeightCycleFrames;
        }
    }
}
