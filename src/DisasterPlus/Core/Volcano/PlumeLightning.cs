using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// One point of one bolt. Assembled by <see cref="PlumeLightning"/>.
    /// </summary>
    public struct LightningPoint
    {
        /// <summary>The horizontal offset with the crater as the origin (m).</summary>
        public readonly float X;

        /// <summary>Height above the crater (m).</summary>
        public readonly float Y;

        /// <summary>The same as above, on the other axis (m).</summary>
        public readonly float Z;

        public LightningPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// "Height fraction <c>[0,1]</c> → the column's radius (m) at that height".
    /// The one and only way <see cref="PlumeLightning.PathInto"/> learns the column's shape
    /// (Core is not allowed to touch <c>EruptionColumn</c>'s internals or the caller's state).
    /// </summary>
    public delegate float RadiusAtFraction(float heightFraction);

    /// <summary>
    /// **Lightning inside the ash plume.** <b>This is Core, so it touches the engine not at
    /// all.</b>
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// > I'd like you to reproduce the lightning that happens inside the ash plume (the kind
    /// > caused by the ejecta colliding with each other).
    ///
    /// This is volcanic lightning. Ash and ejecta collide inside the plume, the charge
    /// separates, and it discharges within the column — **a different phenomenon from
    /// ordinary cloud-to-ground lightning**, and one that <b>mostly completes inside the
    /// column</b>. So this does not build a path that reaches the ground.
    ///
    /// ── ★★ It holds no state at all ─────────────────────────────
    ///
    /// It is built the same way as <see cref="VolcanicTremor"/>. Time is cut into slots of
    /// <see cref="SlotSeconds"/>, **one bolt per slot**, and the shape and the timing are
    /// decided from the seed and the slot number alone.
    /// "Does it happen or not" is not decided by the activity level — decide it that way
    /// and <b>a flash already glowing in the past disappears the instant the activity
    /// changes</b>. Instead the activity scales <b>the brightness</b>, so that when it is
    /// weak you get <b>the occasional barely visible discharge</b>.
    ///
    /// ── The shape ───────────────────────────────────────
    ///
    /// A single polyline. **It joins two points inside the column** (volcanic lightning
    /// completes within it). The intermediate points are shaken radially by
    /// <see cref="JitterRatio"/> of the column's radius.
    /// No branching (it adds vertices without being distinguishable at that size).
    ///
    /// ── How it glows ───────────────────────────────────
    ///
    /// **The rise is instantaneous; it takes <see cref="FlashSeconds"/> to go out.**
    /// Just like a real discharge, there is no flash that brightens gradually.
    /// It relights once, weakly, as it fades (a multiple stroke).
    /// </summary>
    public static class PlumeLightning
    {
        /// <summary>The discharge slot (seconds). **Exactly one bolt per slot.**</summary>
        public const float SlotSeconds = 1.15f;

        /// <summary>How long one bolt glows (seconds).</summary>
        public const float FlashSeconds = 0.34f;

        /// <summary>How many can be visible at once (i.e. how many slots we look back
        /// over).</summary>
        public const int MaxBolts = 3;

        /// <summary>The number of points in the polyline. **At 2 it is straight and does not
        /// read as lightning.**</summary>
        public const int PointCount = 9;

        /// <summary>How far the intermediate points are shaken (as a fraction of the column's
        /// radius at that height).</summary>
        public const float JitterRatio = 0.55f;

        /// <summary>The lowest height at which a discharge occurs (as a fraction of the column
        /// height).</summary>
        public const float LowFraction = 0.10f;

        /// <summary>The highest height at which a discharge occurs (as a fraction of the
        /// column height).</summary>
        public const float HighFraction = 0.72f;

        /// <summary>The floor on one bolt's length (as a fraction of the column height).</summary>
        public const float MinSpanFraction = 0.10f;

        /// <summary>The brightest discharge at activity 1.</summary>
        public const float MaxBrightness = 1f;

        /// <summary>The brightness of the faintest discharge (at activity 1).</summary>
        public const float MinBrightness = 0.22f;

        private const uint TimeSalt = 0x4C544D45u;
        private const uint ShapeSalt = 0x4C545348u;
        private const uint PickSalt = 0x4C545049u;

        /// <summary>The slot the time <paramref name="seconds"/> falls in.</summary>
        public static int SlotAt(float seconds)
        {
            if (IsBad(seconds) || seconds < 0f) return 0;
            return (int)(seconds / SlotSeconds);
        }

        /// <summary>The time (seconds) at which slot <paramref name="slot"/>'s discharge
        /// begins.</summary>
        public static float StartOf(uint seed, int slot)
        {
            if (slot < 0) return 0f;

            // Where within the slot it happens. **Do not push it to the slot boundary**
            // (the beat becomes visible).
            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ TimeSalt));
            return slot * SlotSeconds + u * (SlotSeconds - FlashSeconds);
        }

        /// <summary>
        /// The brightness <c>[0,1]</c> of slot <paramref name="slot"/>'s discharge at the
        /// time <paramref name="seconds"/>. 0 if it is not glowing.
        ///
        /// <paramref name="activityUnit"/> is the eruption strength and **decides the
        /// brightness only** (it does not decide whether it happens; see the class doc).
        /// </summary>
        public static float BrightnessAt(uint seed, int slot, float seconds,
                                         float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (slot < 0) return 0f;
            if (IsBad(seconds)) return 0f;

            float age = seconds - StartOf(seed, slot);
            if (age < 0f || age >= FlashSeconds) return 0f;

            float t = age / FlashSeconds;

            // The rise is instantaneous. After that it only falls.
            float decay = 1f - t;
            decay *= decay;

            // The multiple stroke. It comes back once, weakly, as it fades.
            if (t > 0.55f && t < 0.72f) decay += 0.28f * (1f - t);

            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ PickSalt));
            float scale = MinBrightness + (MaxBrightness - MinBrightness) * u * u;

            return Clamp01(a * scale * decay);
        }

        /// <summary>
        /// Writes the polyline of slot <paramref name="slot"/>'s discharge into
        /// <paramref name="into"/>. Returns how many points were written (0 if
        /// <paramref name="into"/> is too short).
        ///
        /// <paramref name="plumeHeightMetres"/> is the column's total height, and
        /// <paramref name="radiusAtFraction"/> is "height fraction → the column's radius (m)
        /// at that height", which the caller supplies by wrapping
        /// <c>EruptionColumn.RadiusAt</c>.
        /// </summary>
        public static int PathInto(LightningPoint[] into, uint seed, int slot,
                                   float plumeHeightMetres,
                                   RadiusAtFraction radiusAtFraction)
        {
            if (into == null || into.Length < PointCount) return 0;
            if (slot < 0) return 0;
            if (IsBad(plumeHeightMetres) || plumeHeightMetres <= 0f) return 0;
            if (radiusAtFraction == null) return 0;

            uint shape = unchecked((uint)slot ^ ShapeSalt);

            float a = DeterministicRandom.Unit(seed, shape);
            float b = DeterministicRandom.Unit(seed, shape + 977u);

            float lowT = LowFraction + (HighFraction - LowFraction) * (a < b ? a : b);
            float highT = LowFraction + (HighFraction - LowFraction) * (a < b ? b : a);
            if (highT - lowT < MinSpanFraction)
            {
                highT = lowT + MinSpanFraction;
                if (highT > HighFraction) highT = HighFraction;
            }

            // Which way the polyline runs (the horizontal reference direction).
            float angle = 6.2831853f * DeterministicRandom.Unit(seed, shape + 31u);
            float dirX = (float)System.Math.Cos(angle);
            float dirZ = (float)System.Math.Sin(angle);

            for (int i = 0; i < PointCount; i++)
            {
                float s = i / (float)(PointCount - 1);
                float t = lowT + (highT - lowT) * s;

                float y = plumeHeightMetres * t;
                float radius = radiusAtFraction(t);
                if (IsBad(radius) || radius < 0f) radius = 0f;

                // The ends stay nearer the column's axis and the middle swings widest
                // (the discharge path bulges out).
                float bulge = 4f * s * (1f - s);

                float j1 = DeterministicRandom.Unit(seed, shape + (uint)(i * 131 + 7)) - 0.5f;
                float j2 = DeterministicRandom.Unit(seed, shape + (uint)(i * 197 + 53)) - 0.5f;

                float along = radius * JitterRatio * (2f * j1) * bulge;
                float across = radius * JitterRatio * (2f * j2) * bulge;

                into[i] = new LightningPoint(dirX * along - dirZ * across, y,
                                             dirZ * along + dirX * across);
            }

            return PointCount;
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
