using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The instructions for spawning one patch of driving rain (a squall). **Everything is a
    /// ratio of the scatter radius**, and converting to world coordinates is
    /// <c>Game/Typhoon/TyphoonSquallFx</c>'s job.
    /// </summary>
    public struct SquallPatch
    {
        /// <summary>The lateral offset from the centre ÷ the scatter radius.</summary>
        public readonly float OffsetXFraction;

        /// <summary>The same, in depth.</summary>
        public readonly float OffsetZFraction;

        /// <summary>The spawn height ÷ the squall's height, in [0, 1].</summary>
        public readonly float HeightFraction;

        /// <summary>The disc's radius ÷ the scatter radius.</summary>
        public readonly float DiscFraction;

        /// <summary>The vertical extent ÷ the squall's height.</summary>
        public readonly float BandFraction;

        /// <summary>The density ratio, in [0, 1].</summary>
        public readonly float DensityFraction;

        public SquallPatch(float offsetXFraction, float offsetZFraction, float heightFraction,
                           float discFraction, float bandFraction, float densityFraction)
        {
            OffsetXFraction = offsetXFraction;
            OffsetZFraction = offsetZFraction;
            HeightFraction = heightFraction;
            DiscFraction = discFraction;
            BandFraction = bandFraction;
            DensityFraction = densityFraction;
        }
    }

    /// <summary>
    /// Pure data for showing <b>a rainstorm</b> at ground level. **This is Core, so it never
    /// touches the engine.**
    ///
    /// ── Why it is needed (the owner's observation, 2026-08-22) ───────────────────
    ///
    /// &gt; I'd like the rainstorm reproduced.
    ///
    /// Until now all ④ could produce as "the storm's strength" were <c>m_targetRain</c> and
    /// <c>m_targetCloud</c>, and both are **settings for the whole sky**.
    /// The rain falls straight down and obeys neither the wind's direction nor the typhoon's
    /// position. To a player down on the ground, a strong typhoon and light rain look the
    /// same.
    ///
    /// **Vanilla's rain amount is already pinned at its limit** (1.0 at the peak). What is
    /// more, <c>m_currentRain &gt; 0.8</c> is the boundary that makes the game create a
    /// thunderstorm of its own, and ④'s lightning budget is balanced just below it (design
    /// document §4.2).
    /// **There is no room left to raise "the rain amount" any further.**
    ///
    /// So what we add is <b>spray driving sideways</b>. We spawn droplets with a downwind
    /// velocity in <see cref="PatchCount"/> places around the camera.
    /// Unlike vanilla's rain they **stream along the wind direction**, so it looks like a
    /// rainstorm.
    ///
    /// ── ★ Placed around the camera, not the typhoon's centre ─────────────────────
    ///
    /// The vortex (<see cref="VortexPuffLayout"/>) goes at the typhoon's centre. This does
    /// not — it is <b>there to show "it is blowing a gale where I am standing"</b>, so it
    /// goes under the camera. If the typhoon is 5 km away and the spray flies 5 km away, the
    /// screen shows nothing at all.
    ///
    /// The strength is decided by <see cref="StrengthOf"/> from
    /// <c>TyphoonProfile.WindAt</c>'s wind equivalent. Below <see cref="MinWindUnit"/> it
    /// **spawns nothing at all** — spray flying about while you are outside the typhoon would
    /// be the stranger thing.
    ///
    /// ── The wind direction ────────────────────────────────────────
    ///
    /// <see cref="WindDirection"/> returns the typhoon's secondary circulation (tangential +
    /// inflow). It is <b>the same as the direction the vortex turns</b> (the particles in
    /// <see cref="VortexPuffLayout"/> also stream in the direction of increasing angle). Get
    /// this out of step and the clouds and the rain stream in opposite directions.
    ///
    /// ── The jitter ────────────────────────────────────────
    ///
    /// The placement is a <see cref="DeterministicRandom"/> **function of the index alone**.
    /// Mix in the frame and the spray's spawn points jump about and flicker every frame.
    /// <c>System.Random</c> is not used (the discipline across this whole mod).
    /// </summary>
    public static class SquallLayout
    {
        /// <summary>How many <c>RenderEffect</c> calls we fire per frame. **This is the
        /// cap.**</summary>
        public const int PatchCount = 9;

        /// <summary>
        /// Below this wind equivalent we spawn nothing at all.
        /// **Outside the typhoon (outside the gale zone) it is always 0** (<c>WindAt</c>
        /// returns 0 there).
        /// </summary>
        public const float MinWindUnit = 0.18f;

        /// <summary>The wind equivalent at which the squall peaks. Here
        /// <see cref="StrengthOf"/> is 1.</summary>
        public const float FullWindUnit = 0.75f;

        /// <summary>The inflow component ÷ the tangential component. A typhoon turns while
        /// drawing inwards.</summary>
        public const float InflowFraction = 0.35f;

        // ── The particles' properties (a table of numbers written straight into the
        //    clones) ──────────────────────
        //
        // ★ The reason it lives in Core is the same as for VortexCloudProfile.
        //   tools/TyphoonPreview draws the spray with the same numbers without launching the
        //   game, so **it must not be written in two places**.
        //   The in-game counterpart is copied one-for-one in
        //   Game/Typhoon/TyphoonSquallFx.Clone.

        /// <summary>The scatter radius (m). We scatter over this area on the ground directly
        /// below the camera.</summary>
        public const float SpreadMetres = 300f;

        /// <summary>
        /// Every time the camera rises by this much, <see cref="SpreadMetres"/> grows by one
        /// multiple.
        /// **Unless we scatter wider as you pull back, all you get is a small smudge in the
        /// middle of the screen.**
        /// </summary>
        public const float SpreadReferenceHeightMetres = 700f;

        /// <summary>The cap on the scatter radius (m). The higher it goes, the lower the
        /// density per droplet.</summary>
        public const float MaxSpreadMetres = 1200f;

        /// <summary>
        /// The squall's height (m). **Low** — we scatter in a band from <b>the ground</b> up
        /// to this height. It is the band buildings occupy (not the clouds).
        /// </summary>
        public const float HeightMetres = 140f;

        /// <summary>The size of one droplet (m).</summary>
        public const float SizeMetres = 14f;

        /// <summary>Lifetime (seconds). **Short** — it flies, falls and is gone.</summary>
        public const float LifeMinSeconds = 0.9f;

        public const float LifeMaxSeconds = 1.9f;

        /// <summary>Initial speed (m/s). The speed the droplets scatter at themselves,
        /// separate from the wind in the <c>velocity</c> argument.</summary>
        public const float SpeedMin = 14f;

        public const float SpeedMax = 34f;

        /// <summary>The emission angle (degrees). 0 is along the axis (i.e. up) and 90 is
        /// straight out sideways. **They scatter almost horizontally.**</summary>
        public const float SpawnAngleMinDegrees = 66f;

        public const float SpawnAngleMaxDegrees = 104f;

        /// <summary>The gravity multiplier. **Positive = it falls** (it is rain, after
        /// all).</summary>
        public const float GravityModifier = 1.15f;

        /// <summary>The particle count multiplier. **Never 0** (at 0 not a single particle
        /// appears; the trap in §D-2).</summary>
        public const float RateOverTime = 20f;

        /// <summary>The cap on live particles. A separate allowance from the vortex's
        /// (8,000).</summary>
        public const int MaxParticles = 3000;

        /// <summary>How many particles the vortex spawns per second in total.</summary>
        public const float ParticlesPerSecond = 900f;

        /// <summary>The visibility distance (m). The effect we borrowed from only goes to
        /// 500-2,000 m, so we raise it.</summary>
        public const float VisibilityMetres = 3000f;

        /// <summary>How fast it is carried on the wind (m/s). This value at strength
        /// 1.</summary>
        public const float DriftMetresPerSecond = 46f;

        /// <summary>The colour on the bright side (0..1). White spray.</summary>
        public const float BrightRed = 0.94f;

        public const float BrightGreen = 0.96f;

        public const float BrightBlue = 1f;

        /// <summary>The colour on the dark side (0..1). Streaks of rain are darker than the
        /// sky.</summary>
        public const float DarkRed = 0.62f;

        public const float DarkGreen = 0.68f;

        public const float DarkBlue = 0.78f;

        /// <summary>Opacity. **Low** — the city must not become invisible.</summary>
        public const float Alpha = 0.30f;

        /// <summary>The jitter's seed. **A fixed value** (so that it is a function of the
        /// index alone).</summary>
        private const uint Seed = 0x53515544u;   // "SQUD"

        private const float TwoPi = 6.28318530718f;

        /// <summary>
        /// The scatter radius (m) at a camera height of
        /// <paramref name="cameraHeightAboveGround"/> (m above the ground). The further you
        /// pull back, the wider we scatter (see <see cref="SpreadReferenceHeightMetres"/> in
        /// the class doc). Negative values and NaN give the floor.
        /// </summary>
        public static float SpreadFor(float cameraHeightAboveGround)
        {
            if (!(cameraHeightAboveGround > 0f)) return SpreadMetres;

            float spread = SpreadMetres
                           * (1f + cameraHeightAboveGround / SpreadReferenceHeightMetres);
            if (float.IsNaN(spread) || spread < SpreadMetres) return SpreadMetres;
            if (spread > MaxSpreadMetres) return MaxSpreadMetres;
            return spread;
        }

        /// <summary>
        /// Maps the wind equivalent [0, 1] to the squall's strength [0, 1].
        /// 0 at or below <see cref="MinWindUnit"/>, 1 at or above
        /// <see cref="FullWindUnit"/>. NaN gives 0.
        /// </summary>
        public static float StrengthOf(float windUnit)
        {
            if (float.IsNaN(windUnit)) return 0f;
            if (windUnit <= MinWindUnit) return 0f;
            if (windUnit >= FullWindUnit) return 1f;
            return (windUnit - MinWindUnit) / (FullWindUnit - MinWindUnit);
        }

        /// <summary>
        /// The wind direction (a unit vector) at the point <c>(dx, dz)</c> as seen from the
        /// typhoon's centre. The tangential component (the direction the vortex turns) plus
        /// <see cref="InflowFraction"/> of inflow.
        ///
        /// Exactly above the centre (distance 0) the direction is undefined, so we return
        /// <c>(1, 0)</c> — **we never throw** (this path runs every frame).
        /// </summary>
        public static void WindDirection(float dx, float dz, out float wx, out float wz)
        {
            float d2 = dx * dx + dz * dz;
            if (!(d2 > 1e-6f) || float.IsNaN(d2))
            {
                wx = 1f;
                wz = 0f;
                return;
            }

            float d = (float)System.Math.Sqrt(d2);
            float ux = dx / d;
            float uz = dz / d;

            // The tangential component is in the direction of increasing angle (the same as
            // the vortex's particles). The inflow points towards the centre.
            float tx = -uz - InflowFraction * ux;
            float tz = ux - InflowFraction * uz;

            float len = (float)System.Math.Sqrt(tx * tx + tz * tz);
            if (!(len > 0f))
            {
                wx = 1f;
                wz = 0f;
                return;
            }

            wx = tx / len;
            wz = tz / len;
        }

        /// <summary>
        /// Where squall patch number <paramref name="index"/> goes. Everything is
        /// **a normalised ratio**.
        ///
        /// **An out-of-range index is rounded to 0 rather than throwing.** This path runs
        /// every frame, so a miscount on the caller's part must not break a level load.
        /// </summary>
        public static SquallPatch PatchAt(int index)
        {
            if (index < 0 || index >= PatchCount) index = 0;

            // The first goes directly below the camera and the rest in two rings. **Not
            // evenly spaced** (that looks mechanical).
            float ring;
            float angle;
            if (index == 0)
            {
                ring = 0f;
                angle = 0f;
            }
            else if (index <= 4)
            {
                ring = 0.5f;
                angle = TwoPi * (index - 1) / 4f;
            }
            else
            {
                ring = 1f;
                angle = TwoPi * (index - 5) / 4f + TwoPi * 0.125f;
            }

            angle += (DeterministicRandom.Unit(Seed, (uint)index) * 2f - 1f) * 0.35f;
            ring += (DeterministicRandom.Unit(Seed + 1u, (uint)index) * 2f - 1f) * 0.12f;
            if (ring < 0f) ring = 0f;
            if (ring > 1.1f) ring = 1.1f;

            float offsetX = (float)System.Math.Cos(angle) * ring;
            float offsetZ = (float)System.Math.Sin(angle) * ring;

            // Low heights (spray at ground level). The further out, the slightly higher and
            // slightly thinner.
            float height = 0.06f + 0.30f * ring
                           + (DeterministicRandom.Unit(Seed + 2u, (uint)index) * 2f - 1f) * 0.05f;
            if (height < 0f) height = 0f;
            if (height > 1f) height = 1f;

            // Tight and dense on the inside, spread out and thin on the outside (it streams
            // downwind and disperses).
            float disc = 0.28f + 0.22f * ring;
            float band = 0.30f + 0.25f * ring;
            float density = 1f - 0.35f * ring;

            return new SquallPatch(offsetX, offsetZ, height, disc, band, density);
        }
    }
}
