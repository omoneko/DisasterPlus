using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>The "source" of the tsunami from a trench earthquake.</b> **Pure engine-free
    /// functions only.**
    ///
    /// ── The research (2026-08-29) ──────────────────────────────────────
    ///
    /// The full text is in <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>.
    /// The conclusion:
    ///
    /// &gt; **Vanilla's tsunami is not a "wave".** It is a <b>boundary condition</b> that
    /// &gt; merely moves the sea level up and down through 1.5 cycles in one patch on the
    /// &gt; map's rim, and the wall of water that hits the city is entirely the result of the
    /// &gt; game's own shallow-water solver (<c>WaterSimulation.SimulateWater</c>)
    /// &gt; propagating that shaking inland.
    ///
    /// That same solver has <b>an external force that can be placed anywhere on the map</b>
    /// (<c>TYPE_IMPACT</c>, IL_0845–0A49). Per cell:
    ///
    /// <code>
    ///   f(d) = delta * (1 - d^2 / R^2)          d is the distance in cells, 0 for d&gt;=R
    ///   accX += f(dx,dz) - f(dx+1,dz)           ← added to **the water surface's slope**
    /// </code>
    ///
    /// In other words <b>a virtual hill that fools the solver into thinking there is a hill
    /// of water there</b>, which is <b>the same as the seabed being uplifted</b> — the
    /// textbook source of a tsunami.
    /// With <c>delta &gt; 0</c> the water flows outwards, with <c>delta &lt; 0</c> inwards.
    ///
    /// ── ★★ The clock's unit is the "water step", not the sim frame ────────────────
    ///
    /// **On 2026-08-30 this turned out to be the real cause of "the tsunami does not happen".**
    ///
    /// <code>
    ///   SimulateWater ends by calling SetCurrentWaterFrame(start, progress, .., max, ..),
    ///     m_waterFrameIndex = (start &amp; ~63) + (progress &lt;&lt; 6)/max = start + 64
    ///   WaterThread only runs while m_waterFrameIndex &lt; m_simulationFrameIndex, and
    ///   SimulationStep waits at m_simulationFrameIndex &gt; m_waterFrameIndex + 1.
    ///   = **SimulateWater runs only once every 64 sim frames.**
    /// </code>
    ///
    /// So "drive it for 1080 frames" was <b>only 17 water steps</b>. That is why it only
    /// rose 0.7 m in the game.
    ///
    /// ★★ There are two independent confirmations:
    ///   · the wave speed measured in the offline reproduction (<c>tools/WaterSolverSim</c>),
    ///     8.22 m / water step ÷ 64 = **0.128 m / sim frame**
    ///   · the lifetime constant vanilla uses in <c>TsunamiAI.IsStillActive</c>,
    ///     **0.125 m / sim frame** (IL_0024)
    ///   They agree to two decimal places.
    ///
    /// ★ One water step = 64 sim frames ≈ **1.07 real seconds** at game speed 1.
    ///
    /// ── ★★ The forcing has to be "draw → push → draw" ────────────────
    ///
    /// **The second mistake, found by the offline reproduction on 2026-08-30.**
    /// The previous version was "① draw (40 steps) → ②③ push and hold (140 steps)". The result:
    ///
    /// <code>
    ///   running 300 steps at drive 800 —
    ///     sea level at the hypocentre -13.9 m (the seabed exposed), the ring a mere 1.8 m
    ///   at drive 4000 the hypocentre hit -40.00 m = **dug right through to the seabed.**
    /// </code>
    ///
    /// A forcing that is held on creates <b>a steady outflow</b>. A steady outflow is not a
    /// wave, it is **a hole**. What makes a wave is <b>the change</b>, which is why
    /// vanilla's tsunami is 1.5 cycles of <b>oscillation</b> too (drawback → push →
    /// drawback).
    ///
    /// ★★ **Which shape is best was decided by trying six of them in the offline
    ///   reproduction** (2026-08-30; judged in <c>tools/WaterSolverSim</c> against five
    ///   gates: G1 bulge / G2 no hole left / G3 ring / G4 reach / G5 safety).
    ///   The winner was <b>"draw in for a long time, push hard for a short time"</b>:
    ///
    /// <code>
    ///   w = t / TotalSteps
    ///   w &lt; 0.6 : drive = -sin(pi * w / 0.6)              ① **the bulge grows**
    ///   w &gt;= 0.6: drive = +sin(pi * (w-0.6)/0.4) * 1.5    ② plateau → doughnut → ring
    ///   after that the forcing is 0 = ③ the solver alone carries the ring
    /// </code>
    ///
    /// ★ The time integral is exactly 0 (0.6×(2/pi) = 0.4×1.5×(2/pi)). **No hole is left.**
    ///
    /// ★ A record of the shapes that lost:
    ///   · 1.5 cycles (draw → push → draw) only grew the bulge to 3.2 m
    ///   · a radius of 200 cells <b>turned the source itself into a tub that resonated</b>;
    ///     after the centre punched through to the seabed it **rebounded to +111 m**
    ///     (we had been stopping at 420 steps, so at first it looked "green" — it only
    ///     showed up once we ran to 1080 steps)
    /// </summary>
    public static class TsunamiSource
    {
        // ── The clock (**water steps**. Read the ★★ in the class doc) ──────────────

        /// <summary>
        /// How long the forcing runs (water steps). 300 steps ≈ 320 real seconds ≈ 5.3 real
        /// minutes.
        ///
        /// ★ The scale comes from vanilla: the DLC tsunami's source is
        ///   <c>m_duration 16384 / 64</c> = **256 water steps** (≈ 4.5 real minutes).
        ///   The same order of magnitude.
        /// </summary>
        public const float TotalSteps = 300f;

        /// <summary>
        /// The fraction spent drawing in. **The rest is the push out.**
        ///
        /// ★★ 0.6 is <b>the value that won the offline reproduction's sweep</b> (six shapes
        ///   tried in parallel and judged against five gates; see
        ///   <c>docs/…/2026-08-29-tsunami-il-facts.md</c>).
        ///   Draw in for a long time, then push hard for a short time — that was the shape
        ///   that best gave "the bulge is clearly visible, then it becomes a ring and runs".
        /// </summary>
        public const float DrawInFraction = 0.6f;

        /// <summary>
        /// The height of the push's peak (taking the draw-in's peak as 1).
        ///
        /// ★ Against the <c>0.6 : 0.4</c> split of time, the heights are <c>1 : 1.5</c>, so
        ///   <b>the time integral is exactly 0</b> (0.6×(2/π) = 0.4×1.5×(2/π)).
        ///   **This is the algebraic guarantee of "no hole is left".**
        /// </summary>
        public const float PushOvershoot = 1.5f;

        /// <summary>The water step at which ① (the draw-in) ends. For diagnostics and
        /// tests.</summary>
        public static float DrawInSteps { get { return TotalSteps * DrawInFraction; } }

        /// <summary>The water step at which ② (the push) is strongest.</summary>
        public static float PushSteps
        {
            get { return TotalSteps * (DrawInFraction + (1f - DrawInFraction) * 0.5f); } }

        // ── Dimensions ──────────────────────────────────────────────

        /// <summary>
        /// The source's radius (cells; one cell is 16 m).
        ///
        /// ★ In the IL it is <c>R = 1 + max(maxX-origX, origX-minX)</c> — <b>it looks at X
        ///   only.</b> 80 cells = 1280 m. It is the size of the hypocentre's "bulge" itself.
        ///
        /// ★★ 120 -> 80 (2026-08-30, the offline reproduction's sweep). Make it larger and
        ///   <b>the source itself becomes a tub and resonates</b> — at a radius of 200 cells
        ///   the wave raised at the rim came back to the centre and <b>after the centre
        ///   punched through to the seabed it rebounded to +111 m</b> (shape 1 of the sweep).
        /// </summary>
        public const int RadiusCells = 80;

        /// <summary>The same in metres.</summary>
        public const float RadiusMetres = RadiusCells * 16f;

        // ── The magnitude of the forcing ───────────────────────────────────────

        /// <summary><c>WaterWave.m_delta</c> is an Int16. **Go past this and it wraps.**</summary>
        public const int MaxDeltaUnits = 32767;

        /// <summary>
        /// How many water waves are stacked at the same place.
        ///
        /// ★ The forcing is summed per wave, so this is how a forcing greater than an Int16
        ///   is built. The cost is "the number of waves × the cells inside the bbox".
        ///   **Measure it in-game before raising it.**
        /// </summary>
        public const int MaxStackedWaves = 8;

        /// <summary>The cap on the forcing the whole stack can produce.</summary>
        public const int MaxDriveUnits = MaxStackedWaves * MaxDeltaUnits;

        /// <summary>One metre's worth of <c>m_delta</c> (IL:
        /// <c>depth * 65536 / 1024</c>).</summary>
        public const int UnitsPerMetre = 64;

        /// <summary>
        /// The forcing for the weakest earthquake (in <c>m_delta</c> units, at a water depth
        /// of <see cref="ReferenceDepthMetres"/>).
        /// **A value measured and fixed in <c>tools/WaterSolverSim</c>**, not a guess.
        /// </summary>
        public const int MinDriveUnits = 780;

        /// <summary>
        /// The forcing for the strongest earthquake (intensity 255, i.e. 25.5 with the
        /// intensity unlock). Measured the same way.
        ///
        /// ★★ **The narrow intensity band is not laziness.**
        ///   What decides the size of the wave is <b>the depth of the sea</b> — the solver's
        ///   flow is capped by <c>v = min(v, m_height)</c>, so <b>a shallow sea cannot carry
        ///   a large wave however strong the earthquake</b>.
        ///   Measured at a water depth of 40 m (<c>tools/WaterSolverSim</c>, grid 1081, 780 steps):
        ///
        /// <code>
        ///     drive   bulge    deepest centre    water left on the bed   ring (2km)
        ///       896   18.1 m   -29.2 m (73%)     10.8 m                  5.7 m   ← the safe operating point
        ///      1200   24.4 m   -38.8 m (97%)      1.25 m                 8.2 m   ← **dug through**
        ///      1500   30.3 m   -40.00 m (100%)    0 m                    —       ← seabed exposed
        /// </code>
        ///
        /// ★★ **1200 is not "strong", it is broken.** (2026-08-30, final verification.)
        ///   At 1200 only 1.25 m of the hypocentre's 40 m water column is left, and the
        ///   screen shows <b>a 2 km wide hole for 130 real seconds</b>. The intensity slider
        ///   swings all the way to 255 on the first click, so this was not a corner case but
        ///   <b>the default worst case</b>.
        ///
        /// ★ So the cap is 900 — a value fixed by "the dug-out fraction ≤ 75%".
        ///   The narrow band is not laziness: <b>what decides the size of the wave is the
        ///   depth of the sea</b>, and the solver's flow is capped by
        ///   <c>v = min(v, m_height)</c>.
        /// </summary>
        public const int MaxIntensityDriveUnits = 900;

        /// <summary>
        /// The water depth (m) at which the numbers above were measured.
        ///
        /// ★★ **The forcing has to be scaled by the water depth.** (2026-08-30, the
        ///   adjudicating agent.)
        ///   The solver's flow is capped at the water depth by <c>v = min(v, m_height)</c>,
        ///   so the same forcing <b>digs right through more easily the shallower the sea</b>.
        ///   Apply the forcing meant for 40 m of water in 10 m of water and the hypocentre's
        ///   cells had <b>the seabed exposed for 136 water steps (≈145 real seconds)</b>.
        ///   The height of the bulge and the depth of the excavation are the same quantity
        ///   in this solver, so in shallow water the only option is to make the bulge itself
        ///   smaller.
        /// </summary>
        public const float ReferenceDepthMetres = 40f;

        /// <summary>
        /// The floor on the depth factor. **Set it to 0 and the tsunami disappears in the
        /// shallows.**
        ///
        /// ★ 0.15 -> 0.08 (2026-08-30). At 0.15, a sea 5 m deep got a forcing meant for 6 m
        ///   of water and the hypocentre had <b>its seabed exposed</b> (measured -5.00 m,
        ///   i.e. the whole water column). This lowers the range over which the plain ratio
        ///   (depth/40) can be used down to a depth of 3.2 m.
        /// </summary>
        public const float MinDepthFactor = 0.08f;

        /// <summary>
        /// The ceiling on the same.
        ///
        /// ★★ **1.0 -> 12.5 (2026-08-30, the in-game report "the tsunami does not happen").**
        ///
        ///   The grounds for 1.0 were "CS terrain is at or above elevation 0, so a map with
        ///   a sea level of 40 m cannot have sea deeper than 40 m". **That premise was
        ///   wrong** — the sea level differs per map, and the map in-game had
        ///   <b>a sea level of 207 m and a water depth of 174 m</b>. The forcing being
        ///   applied there was 806, which <b>dug out only 15% of the water column</b>
        ///   (leaving 85% of the available margin unused).
        ///   The ring only reached 2-3 m, which is <b>invisible</b> in 174 m of open ocean.
        ///
        ///   <c>WaterSimulation.MAX_SEA_LEVEL</c> is 500 (measured from IL), so the deepest
        ///   possible is 500 m. The ceiling is that divided through: 500/40 = 12.5.
        ///
        /// ★★ **The response is completely independent of the water depth**
        ///   (<c>tools/WaterSolverSim</c>: running grid 1081, 1200 steps and drive 806 at
        ///   depths of 40 m and 174 m gave <b>identical values in every column and every
        ///   frame</b>). The depth only enters through the <c>v = min(v, m_height)</c> cap,
        ///   and until that is touched the same forcing makes the same wave. So
        ///   <b>the dug-out fraction is proportional to the forcing and inversely
        ///   proportional to the depth</b>:
        ///
        /// <code>
        ///     dug-out fraction ≈ 0.0329 * drive / depth        (from the measurements at depths 40 and 174)
        ///     -> the line for staying under 70% is  drive ≈ 21 * depth
        /// </code>
        ///
        ///   <see cref="DriveUnitsFor"/> gives <c>base * depth/40</c> (base being 780-900),
        ///   i.e. <c>(19.5-22.5) * depth</c>, so it stays at **the same fraction at any
        ///   depth**. Measured at a depth of 174 m (drive 3500, equivalent to intensity 55):
        ///   a bulge of 72 m, a minimum of -110 m (63%), and a ring of 23.7 m at 2.4 km and
        ///   12.8 m at 8.2 km.
        /// </summary>
        public const float MaxDepthFactor = 12.5f;

        /// <summary>
        /// Derives the magnitude of the forcing (unsigned) from the earthquake's intensity
        /// (0-255) and <b>the water depth at the hypocentre</b>.
        /// </summary>
        public static int DriveUnitsFor(byte intensity, float depthMetres)
        {
            float d = MinDriveUnits
                      + (MaxIntensityDriveUnits - MinDriveUnits) * (intensity / 255f);

            d *= DepthFactor(depthMetres);

            int units = (int)(d + 0.5f);
            if (units < 1) return 1;
            if (units > MaxDriveUnits) return MaxDriveUnits;
            return units;
        }

        /// <summary>The discount for water depth. **Essential, for the reason in the class doc
        /// above.**</summary>
        public static float DepthFactor(float depthMetres)
        {
            if (IsBad(depthMetres) || depthMetres <= 0f) return MinDepthFactor;

            float f = depthMetres / ReferenceDepthMetres;
            if (f < MinDepthFactor) return MinDepthFactor;
            if (f > MaxDepthFactor) return MaxDepthFactor;
            return f;
        }

        // ── Vanilla's scale (kept purely for comparison) ─────────────────────

        /// <summary>
        /// The measured prefab value of <c>TsunamiAI.m_height</c> (m).
        /// Read from the <c>Tsunami</c> GameObject in sharedassets55.
        /// </summary>
        public const float VanillaHeightMetres = 64f;

        /// <summary>
        /// How high vanilla's tsunami lifts the sea level at the rim (in <c>m_delta</c> units).
        /// <code>  round(m_height * 65536/1024 * intensity / 55)  </code>
        ///
        /// ★ This is <b>the amplitude of a boundary condition</b>, which means something
        ///   different from our <b>virtual hill</b>. They can be written side by side only
        ///   because the units are the same —
        ///   **you cannot read it as "smaller than vanilla, therefore weaker".**
        /// </summary>
        public static int VanillaDeltaUnits(byte intensity)
        {
            float d = VanillaHeightMetres * UnitsPerMetre * intensity / 55f;
            int units = (int)(d + 0.5f);
            if (units > MaxDeltaUnits) return MaxDeltaUnits;
            if (units < 0) return 0;
            return units;
        }

        // ── The shape of the forcing ──────────────────────────────────────────

        /// <summary>
        /// The forcing produced by <b>the whole stack of waves</b> (in <c>m_delta</c> units)
        /// <paramref name="elapsedSteps"/> water steps after the earthquake.
        ///
        /// <b>Negative gathers water towards the centre, positive pushes it outwards.</b>
        /// 0 once it is over — <b>that is how the caller knows to release the waves.</b>
        /// </summary>
        public static int DeltaAt(float elapsedSteps, int driveUnits)
        {
            if (IsBad(elapsedSteps) || elapsedSteps < 0f) return 0;
            if (elapsedSteps >= TotalSteps) return 0;
            if (driveUnits <= 0) return 0;

            float f = DriveAt(elapsedSteps);
            int units = (int)(f * driveUnits + (f >= 0f ? 0.5f : -0.5f));

            if (units > MaxDriveUnits) return MaxDriveUnits;
            if (units < -MaxDriveUnits) return -MaxDriveUnits;
            return units;
        }

        /// <summary>
        /// The shape of the forcing alone (normalised so the peak is 1). <c>[-1, 1]</c>.
        ///
        /// ★★ **The time integral must be as close to 0 as makes no difference.** (See the
        ///   class doc.) Hold the push on and all you leave is a hole in the seabed; it does
        ///   not become a wave.
        /// </summary>
        public static float DriveAt(float elapsedSteps)
        {
            if (IsBad(elapsedSteps) || elapsedSteps <= 0f) return 0f;
            if (elapsedSteps >= TotalSteps) return 0f;

            double w = elapsedSteps / TotalSteps;

            // ① Draw in for a long time (negative = the water gathers towards the centre =
            //    the bulge grows).
            if (w < DrawInFraction)
            {
                return (float)(-Math.Sin(Math.PI * w / DrawInFraction));
            }

            // ②③ Push hard for a short time (positive = outwards. The bulge goes plateau →
            //     doughnut → ring).
            double k = (w - DrawInFraction) / (1.0 - DrawInFraction);
            return (float)(Math.Sin(Math.PI * k) * PushOvershoot);
        }

        /// <summary>
        /// Whether the forcing has been switched off (i.e. we are in ④). From here on
        /// <b>the solver alone carries the wave</b>, so the caller may release the water waves.
        /// </summary>
        public static bool IsFinished(float elapsedSteps)
        {
            return !IsBad(elapsedSteps) && elapsedSteps >= TotalSteps;
        }

        /// <summary>Which stage we are in (**English, for diagnostics**).</summary>
        public static string StageAt(float elapsedSteps)
        {
            if (IsBad(elapsedSteps) || elapsedSteps < 0f) return "not started";
            if (elapsedSteps >= TotalSteps) return "4 done - the ring is on its own now";

            float w = elapsedSteps / TotalSteps;
            if (w < DrawInFraction) return "1 the sea is drawn in and the bulge rises";
            return "2 the bulge is pushed out into a spreading ring";
        }

        // ── Distributing across the stacked waves ─────────────────────────────────────

        /// <summary>
        /// The <c>m_delta</c> to put into stacked wave number <paramref name="index"/>
        /// (0-based). <paramref name="driveUnits"/> is handed out
        /// <see cref="MaxDeltaUnits"/> at a time.
        /// </summary>
        public static int DeltaForWave(int index, int driveUnits)
        {
            if (index < 0 || index >= MaxStackedWaves) return 0;

            int magnitude = driveUnits < 0 ? -driveUnits : driveUnits;
            if (magnitude > MaxDriveUnits) magnitude = MaxDriveUnits;

            int given = index * MaxDeltaUnits;
            int left = magnitude - given;
            if (left <= 0) return 0;
            if (left > MaxDeltaUnits) left = MaxDeltaUnits;

            return driveUnits < 0 ? -left : left;
        }

        /// <summary>How many waves are needed right now (for diagnostics).</summary>
        public static int WavesNeeded(int driveUnits)
        {
            int magnitude = driveUnits < 0 ? -driveUnits : driveUnits;
            if (magnitude <= 0) return 0;
            if (magnitude > MaxDriveUnits) magnitude = MaxDriveUnits;

            return (magnitude + MaxDeltaUnits - 1) / MaxDeltaUnits;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
