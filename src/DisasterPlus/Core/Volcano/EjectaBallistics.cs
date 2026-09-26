using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>The trajectory of one block thrown out of the crater. **A pure value** (no
    /// Unity, no random numbers).</summary>
    public struct EjectaBlock
    {
        /// <summary>Whether this slot is usable. If false, **draw nothing** (do not substitute
        /// 0).</summary>
        public readonly bool Valid;

        /// <summary>The horizontal direction (a unit vector).</summary>
        public readonly float DirX;

        public readonly float DirZ;

        /// <summary>Horizontal initial speed (m/s).</summary>
        public readonly float HorizontalSpeed;

        /// <summary>Vertical initial speed (m/s, up is positive).</summary>
        public readonly float VerticalSpeed;

        /// <summary>
        /// The block's size <c>[0,1]</c>. **The bigger it is the further it flies** (as the
        /// owner asked). The drawing side multiplies the particle count and the width of the
        /// impact dust by this.
        /// </summary>
        public readonly float SizeUnit;

        /// <summary>Seconds until impact. <see cref="EjectaBallistics.Plan"/> derives it by
        /// intersecting the flanks.</summary>
        public readonly float FlightSeconds;

        /// <summary>The impact point's horizontal distance from the crater (m).</summary>
        public readonly float RangeMetres;

        public EjectaBlock(float dirX, float dirZ, float horizontalSpeed, float verticalSpeed,
                           float sizeUnit, float flightSeconds, float rangeMetres)
        {
            Valid = true;
            DirX = dirX;
            DirZ = dirZ;
            HorizontalSpeed = horizontalSpeed;
            VerticalSpeed = verticalSpeed;
            SizeUnit = sizeUnit;
            FlightSeconds = flightSeconds;
            RangeMetres = rangeMetres;
        }
    }

    /// <summary>
    /// **The trajectories of ballistic blocks.** Pure engine-free functions only; no Unity
    /// types, no game types and no <c>System.Random</c> appear here.
    ///
    /// ── Why it lives in Core ─────────────────────────────────
    ///
    /// The owner's request of 2026-08-22:
    ///
    /// > I'd like an explosion plus flying-rock animation implemented for the eruption too.
    ///
    /// The "ejecta" up to now only kept spawning particles above the crater, and
    /// **individual rocks were never flying and landing**. To make them fly you have to
    /// decide "where does it land and how long does it take", and that is **just a
    /// parabola** — so there is no reason a single line of it cannot be checked without
    /// launching the game. Gather it here and pin it with tests.
    ///
    /// ── What is real and what is ⑤'s presentation value ─────────────────────────────
    ///
    ///   - <see cref="GravityMetresPerSecondSquared"/> = 9.81 is **real physics**
    ///   - The launch angles <see cref="MinElevationDegrees"/> to
    ///     <see cref="MaxElevationDegrees"/> (42-65°) are also close to real observations of
    ///     ejecta (they leave the crater at steep angles)
    ///   - **The range is decided first, not derived from the initial speed.** It is fixed
    ///     as a fraction of the mountain's radius (<see cref="MinRangeFraction"/> to
    ///     <see cref="MaxRangeFraction"/>) and then the initial speed is worked backwards as
    ///     <c>v = sqrt(range·g / sin 2φ)</c>.
    ///     Without that it **does not follow the mountain's size** — you would have rocks
    ///     flying 2.7 km out of a lava dome with a 350 m radius. ⑤ is a feature where the
    ///     player picks the size, so there the screen's requirements come before physics.
    ///     Measured (stratovolcano, R = 1200 m): **impacts at 255-1642 m (mean 876 m), 20%
    ///     land beyond the foot, and the longest flight is 27 seconds**
    ///   - No air resistance. Adding it means needing a terminal velocity to produce "the
    ///     bigger the further", which adds three numbers **none of which can be measured**.
    ///     The difference between large and small is made directly through the range
    ///     fraction
    ///
    /// ── Where it lands ────────────────────────────────────────
    ///
    /// The intersection of the parabola with **the flanks including the crater** (the cone
    /// cut by <c>VolcanoCrater</c>'s ceiling, before the relief is applied).
    ///
    /// ★★ <b>Do not forget to include the crater.</b> Make the ground
    ///   <c>VolcanoShape.ProfileAt</c> alone and it returns the summit height H at
    ///   <c>d = 0</c>, whereas the vent is at **the crater floor** — so the instant a rock
    ///   is launched it is judged "below the ground" and **not one block flies**
    ///   (it actually happened; a test caught it).
    ///
    /// The relief (<c>VolcanoRelief</c>) only ever works in the direction of shaving away,
    /// so **the real ground is at or below the surface used here** — meaning a block stops a
    /// few metres short and a few metres high inside a gully. The impact dust vanishes in
    /// under a second, so re-evaluating the relief is not worth it
    /// (<c>VolcanoRelief.ProfileAt</c> is about 300 flops a call, and this evaluates it 170
    /// times per block).
    ///
    /// Outside the radius the cone is 0, so it stops at **the original terrain height**
    /// (i.e. the ground at the point where the volcano was placed). On uneven ground that
    /// is a few metres off the real ground, but again that is under a second of dust.
    ///
    /// ── Random numbers ────────────────────────────────────────────
    ///
    /// <see cref="DeterministicRandom"/> only. **Do not mix the frame number into the
    /// seed** — mix it in and the same rock is re-drawn every frame, changing where it is
    /// going mid-flight.
    /// The seed is decided from "the volcano's location × the eruption number × the block
    /// number" alone.
    /// </summary>
    public static class EjectaBallistics
    {
        /// <summary>Gravitational acceleration (m/s²). **This alone is real physics.**</summary>
        public const float GravityMetresPerSecondSquared = 9.81f;

        /// <summary>The floor on how many blocks one eruption throws out (i.e. at its
        /// weakest).</summary>
        public const int MinBlocksPerBlast = 5;

        /// <summary>The ceiling on the same. **It is also the cap on how many particle systems
        /// are drawn at once.**</summary>
        public const int MaxBlocksPerBlast = 16;

        /// <summary>
        /// The smallest block's range (as a fraction of the mountain's radius).
        /// **This is the fraction "as if it landed on the flat"** — in practice it leaves
        /// the crater floor and lands on the flanks and the foot below that, so the impact
        /// is 30-40% further out than this.
        /// </summary>
        public const float MinRangeFraction = 0.25f;

        /// <summary>The largest block's range (as above). **It flies beyond the foot.**</summary>
        public const float MaxRangeFraction = 0.90f;

        /// <summary>The floor on the launch angle (degrees from the horizontal).</summary>
        public const float MinElevationDegrees = 42f;

        /// <summary>
        /// The ceiling on the launch angle (degrees). **Do not raise it to 80°.**
        /// For the same range, the steeper the angle the greater both the initial speed and
        /// the time aloft. The version that allowed 80° produced a 48-second trajectory
        /// (measured in tools/VolcanoPreview) — physically correct (real ejecta do take
        /// 20-40 seconds), but on screen that is a length of time where "the rock looks like
        /// it has stopped". At 65° the longest comes in at 27 seconds.
        /// </summary>
        public const float MaxElevationDegrees = 65f;

        /// <summary>The spread of ranges (± this fraction).</summary>
        public const float RangeJitter = 0.18f;

        /// <summary>The range fraction at eruption strength 0 (it is 1.0 at strength 1).</summary>
        public const float WeakRangeScale = 0.55f;

        /// <summary>The step used to march the trajectory (seconds). Making it finer moves the
        /// impact point by only a few metres.</summary>
        private const float MarchSeconds = 0.25f;

        /// <summary>
        /// No flight goes beyond this (seconds). **A cap to prevent an infinite loop**, not a
        /// presentation value — the longest trajectory within the bands above (range 0.9R,
        /// angle 65°) is 27 seconds, so **no trajectory should ever hit this**
        /// (a test measures the longest and pins it).
        /// </summary>
        public const float MaxFlightSeconds = 60f;

        /// <summary>The bisection count. Splitting the 0.25 s step 11 times gives 0.12
        /// ms.</summary>
        private const int RefineSteps = 11;

        /// <summary>
        /// How many blocks one eruption throws out. The stronger it is, the more.
        /// </summary>
        public static int BlocksPerBlast(float unit)
        {
            float u = Clamp01(unit);
            int n = MinBlocksPerBlast
                    + (int)((MaxBlocksPerBlast - MinBlocksPerBlast) * u + 0.5f);
            if (n < MinBlocksPerBlast) return MinBlocksPerBlast;
            if (n > MaxBlocksPerBlast) return MaxBlocksPerBlast;
            return n;
        }

        /// <summary>
        /// Decides one block's trajectory. **The same (seed, eruption number, block number)
        /// always gives the same trajectory.**
        ///
        /// <paramref name="ventAboveBaseMetres"/> is how many metres the vent sits above
        /// "the ground at the point the mountain was placed" (i.e. the crater floor's
        /// height). Values of 0 or less, and NaN, fall to 0.
        ///
        /// When the mountain's radius or final height cannot be read (0 or less) it returns
        /// <c>Valid == false</c> — **it does not substitute 0.**
        /// </summary>
        public static EjectaBlock Plan(uint seed, int blastIndex, int blockIndex, float unit,
                                       VolcanoForm form, float radiusMetres, float heightMetres,
                                       float ventAboveBaseMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return default(EjectaBlock);
            if (IsBad(heightMetres) || heightMetres <= 0f) return default(EjectaBlock);
            if (blastIndex < 0 || blockIndex < 0) return default(EjectaBlock);

            float vent = IsBad(ventAboveBaseMetres) || ventAboveBaseMetres < 0f
                       ? 0f : ventAboveBaseMetres;

            uint key = unchecked((uint)(blastIndex * 61u + 1u) * 0x9E3779B1u
                                 + (uint)blockIndex);

            float azimuth = 6.2831853f * DeterministicRandom.Unit(seed, key);
            float sizeUnit = DeterministicRandom.Unit(seed, key ^ 0x51ED270Bu);
            float angleUnit = DeterministicRandom.Unit(seed, key ^ 0x2545F491u);
            float jitterUnit = DeterministicRandom.Unit(seed, key ^ 0x1B873593u);

            float u = Clamp01(unit);

            // ★ The bigger the further (the owner's request). The strength only ever works
            //   in the direction of shrinking the whole thing.
            float fraction = MinRangeFraction
                             + (MaxRangeFraction - MinRangeFraction) * sizeUnit;
            fraction *= 1f + RangeJitter * (jitterUnit * 2f - 1f);
            fraction *= WeakRangeScale + (1f - WeakRangeScale) * u;

            float range = fraction * radiusMetres;
            if (!(range > 0f)) return default(EjectaBlock);

            float elevation = (MinElevationDegrees
                               + (MaxElevationDegrees - MinElevationDegrees) * angleUnit)
                              * 0.0174532925f;

            float sin2 = (float)Math.Sin(2.0 * elevation);
            if (!(sin2 > 0.02f)) sin2 = 0.02f;

            // The initial speed as if it landed on the flat. In practice it leaves the
            // crater floor and lands on the flanks, so the March below correctly stops it
            // whether that is nearer (on the slope) or further (beyond the foot).
            float speed = (float)Math.Sqrt(range * GravityMetresPerSecondSquared / sin2);

            float horizontal = speed * (float)Math.Cos(elevation);
            float vertical = speed * (float)Math.Sin(elevation);

            float dirX = (float)Math.Cos(azimuth);
            float dirZ = (float)Math.Sin(azimuth);

            float flight = March(form, radiusMetres, heightMetres, vent, horizontal, vertical);
            if (!(flight > 0f)) return default(EjectaBlock);

            return new EjectaBlock(dirX, dirZ, horizontal, vertical, sizeUnit,
                                   flight, horizontal * flight);
        }

        /// <summary>
        /// The block's position as seen from the vent (m). <paramref name="t"/> is seconds
        /// since launch.
        /// **It still computes a value when <c>t</c> is past the flight time** (the caller
        /// cuts it off with <see cref="EjectaBlock.FlightSeconds"/>).
        /// </summary>
        public static void OffsetAt(EjectaBlock block, float t,
                                    out float dx, out float dy, out float dz)
        {
            dx = 0f; dy = 0f; dz = 0f;
            if (!block.Valid) return;
            if (IsBad(t) || t < 0f) return;

            dx = block.DirX * block.HorizontalSpeed * t;
            dz = block.DirZ * block.HorizontalSpeed * t;
            dy = block.VerticalSpeed * t
                 - 0.5f * GravityMetresPerSecondSquared * t * t;
        }

        /// <summary>
        /// The seconds until the parabola meets the flanks (**the cone before the relief is
        /// applied**).
        /// If none is found, it is cut off at <see cref="MaxFlightSeconds"/>.
        /// </summary>
        private static float March(VolcanoForm form, float radiusMetres, float heightMetres,
                                   float ventAboveBase, float horizontal, float vertical)
        {
            float previous = 0f;
            for (float t = MarchSeconds; t <= MaxFlightSeconds; t += MarchSeconds)
            {
                if (Below(form, radiusMetres, heightMetres, ventAboveBase,
                          horizontal, vertical, t))
                {
                    float lo = previous;
                    float hi = t;
                    for (int i = 0; i < RefineSteps; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Below(form, radiusMetres, heightMetres, ventAboveBase,
                                  horizontal, vertical, mid)) hi = mid;
                        else lo = mid;
                    }
                    return hi;
                }
                previous = t;
            }
            return MaxFlightSeconds;
        }

        /// <summary>Whether the block is below the ground at time <paramref name="t"/>.</summary>
        private static bool Below(VolcanoForm form, float radiusMetres, float heightMetres,
                                  float ventAboveBase, float horizontal, float vertical, float t)
        {
            float altitude = ventAboveBase + vertical * t
                             - 0.5f * GravityMetresPerSecondSquared * t * t;
            float distance = horizontal * t;
            return altitude <= GroundAt(form, distance, radiusMetres, heightMetres);
        }

        /// <summary>
        /// The ground at <paramref name="distanceMetres"/> from the centre (**including the
        /// crater**, without the relief).
        /// It is the smaller of the same two expressions as <c>VolcanoCrater.ProfileAt</c>,
        /// and unlike that one it needs no <c>VolcanoRelief</c> (the relief only works in
        /// the direction of shaving away, so this is always at or above the real ground).
        /// </summary>
        public static float GroundAt(VolcanoForm form, float distanceMetres,
                                     float radiusMetres, float heightMetres)
        {
            float cone = VolcanoShape.ProfileAt(
                form, distanceMetres, radiusMetres,
                heightMetres * VolcanoCrater.SummitScale(form, radiusMetres));
            float ceiling = VolcanoCrater.CeilingMetres(distanceMetres, radiusMetres,
                                                        heightMetres);
            return cone < ceiling ? cone : ceiling;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
