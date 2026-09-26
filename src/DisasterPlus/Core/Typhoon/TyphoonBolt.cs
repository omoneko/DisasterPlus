using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>One point of the polyline (relative to the typhoon's eye as the origin,
    /// in m).</summary>
    public struct TyphoonBoltPoint
    {
        public readonly float X;

        /// <summary>Height above the cloud base (m).</summary>
        public readonly float Y;

        public readonly float Z;

        public TyphoonBoltPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// <b>Lightning flashing inside the typhoon's clouds.</b> **Pure, engine-free functions
    /// only.**
    ///
    /// ── The owner's instruction (2026-08-25) ─────────────────────────────
    ///
    /// &gt; The lightning is still spawning above the typhoon's clouds. It might work better
    /// &gt; to drop the "thunderstorm" altogether and have just "rain", with lightning
    /// &gt; appearing from inside the typhoon's clouds now and then.
    ///
    /// ── ★★ Why vanilla's lightning appears above the cloud (measured from the IL) ──
    ///
    /// Vanilla drops lightning from the sky in **exactly one place**,
    /// <c>WeatherManager.SimulationStepImpl</c>, under the condition
    /// <c>m_currentRain &gt; 0.8</c> (IL_09D3).
    /// The height it falls from is decided by the game's lightning renderer and **cannot be
    /// moved from a mod.** Even raising ④'s cloud to 1,200 m, it still came down from above.
    ///
    /// So <b>we do not let the game drop lightning at all</b> (we cap the rain at 0.8;
    /// <c>TyphoonWeather.MaxRainWithoutLightning</c>). Instead <b>we draw it ourselves inside
    /// the cloud</b> — the same road we took with the lightning inside ⑤'s plume
    /// (<c>Core.Volcano.PlumeLightning</c>).
    ///
    /// ── The shape differs from the plume's lightning ───────────────────────────
    ///
    /// <code>
    /// Plume:   runs **almost straight up** through the column (a discharge inside a narrow column)
    /// Typhoon: the cloud is **a flat disc**, so the lightning runs mainly **sideways** (cloud-to-cloud)
    /// </code>
    ///
    /// So here we build a polyline that is <b>long horizontally and thin vertically</b>.
    /// We bias where it appears towards the eyewall and the rainbands — that is where the
    /// convection is strongest.
    ///
    /// ★ Exactly one bolt is decided per slot (<see cref="SlotSeconds"/>).
    ///   It is determined by the seed and the slot, so **the same typhoon flashes the same
    ///   way however many times you run it.**
    /// </summary>
    public static class TyphoonBolt
    {
        /// <summary>The discharge slot (seconds). **Exactly one bolt per slot.**</summary>
        public const float SlotSeconds = 2.4f;

        /// <summary>How long one bolt stays lit (seconds). **Short.**</summary>
        public const float FlashSeconds = 0.22f;

        /// <summary>How many can be visible at once (i.e. how many slots we look back
        /// over).</summary>
        public const int MaxBolts = 3;

        /// <summary>The number of points in the polyline. **With 2 it is straight and does
        /// not read as lightning.**</summary>
        public const int PointCount = 7;

        /// <summary>The lower bound on a bolt's horizontal length (as a ratio of the vortex's
        /// radius).</summary>
        public const float LengthMinFraction = 0.16f;

        /// <summary>The upper bound on the same.</summary>
        public const float LengthMaxFraction = 0.42f;

        /// <summary>How far the intermediate points swing sideways (as a ratio of that bolt's
        /// length).</summary>
        public const float JitterFraction = 0.14f;

        /// <summary>How far it swings vertically (as a ratio of the cloud's thickness).
        /// **Never out of the cloud.**</summary>
        public const float VerticalFraction = 0.30f;

        /// <summary>The value at its brightest.</summary>
        public const float PeakBrightness = 1f;

        /// <summary>
        /// Whether this slot flashes (i.e. produces a bolt). Flashing in every slot is
        /// restless; without quiet stretches it does not read as "now and then".
        /// </summary>
        public const float StrikeChance = 0.55f;

        /// <summary>Which slot we are in now.</summary>
        public static int SlotAt(float clockSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0;
            return (int)(clockSeconds / SlotSeconds);
        }

        /// <summary>
        /// The current brightness <c>[0,1]</c> of <paramref name="slot"/>'s bolt.
        /// 0 if it is not lit (the slot did not flash, or the time has passed).
        ///
        /// <paramref name="intensityUnit"/> is the typhoon's strength <c>[0,1]</c> — in a
        /// weak typhoon there is simply less lightning.
        /// </summary>
        public static float BrightnessAt(uint seed, int slot, float clockSeconds,
                                         float intensityUnit)
        {
            if (slot < 0) return 0f;
            if (IsBad(clockSeconds)) return 0f;

            float unit = Clamp01(intensityUnit);
            if (unit <= 0f) return 0f;

            // Is this a slot that flashes?
            if (DeterministicRandom.Unit(seed, (uint)slot * 5u + 1u) > StrikeChance * unit)
            {
                return 0f;
            }

            float start = slot * SlotSeconds
                          + DeterministicRandom.Unit(seed, (uint)slot * 5u + 2u)
                            * (SlotSeconds - FlashSeconds);

            float age = clockSeconds - start;
            if (age < 0f || age > FlashSeconds) return 0f;

            // ★ The rise is instantaneous and the decay trails off a little (how a discharge
            //    looks).
            float w = age / FlashSeconds;
            float shape = w < 0.12f ? w / 0.12f : (1f - w) / 0.88f;
            if (shape < 0f) shape = 0f;

            return PeakBrightness * shape * (0.45f + 0.55f * unit);
        }

        /// <summary>
        /// Writes <paramref name="slot"/>'s polyline into <paramref name="into"/>.
        /// Returns the number of points written (<see cref="PointCount"/>, or 0 on failure).
        ///
        /// <paramref name="radiusMetres"/> is the vortex's outer radius and
        /// <paramref name="thicknessMetres"/> is the cloud's thickness.
        /// </summary>
        public static int PathInto(TyphoonBoltPoint[] into, uint seed, int slot,
                                   float radiusMetres, float thicknessMetres)
        {
            if (into == null || into.Length < PointCount) return 0;
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 0;

            float thickness = IsBad(thicknessMetres) || thicknessMetres <= 0f
                ? radiusMetres * 0.16f
                : thicknessMetres;

            uint draw = (uint)slot * 17u + 3u;

            // ── Where it flashes. Biased towards the eyewall and the rainbands ────────
            //   Never inside the eye (it is clear in there).
            float band = TyphoonCloudParcels.EyewallFraction
                         + (1f - TyphoonCloudParcels.EyewallFraction)
                           * DeterministicRandom.Unit(seed, draw) * 0.85f;

            // ★★ **Pull it inwards to allow for the bolt's own length.** (A test caught this.)
            //    Allow band up to 0.89 and place a bolt 0.42R long, and its end lands at
            //    1.10R — flashing <b>outside the vortex</b>. Pull the centre band inwards by
            //    "half the length + the sideways swing".
            float reach = (LengthMaxFraction * 0.5f)
                          * (1f + JitterFraction);
            float maxBand = 1f - reach;
            if (maxBand < TyphoonCloudParcels.EyewallFraction)
            {
                maxBand = TyphoonCloudParcels.EyewallFraction;
            }
            if (band > maxBand) band = maxBand;

            float angle = DeterministicRandom.Unit(seed, draw + 1u) * 6.2831853f;
            float cx = (float)Math.Cos(angle) * band * radiusMetres;
            float cz = (float)Math.Sin(angle) * band * radiusMetres;

            // Around the middle of the cloud's thickness. **Never at the top or bottom**
            // (it would be seen sticking out of the cloud).
            float cy = thickness * (0.30f + 0.40f * DeterministicRandom.Unit(seed, draw + 2u));

            // ── The direction it runs. **Horizontal** (cloud-to-cloud). ────────────────
            float heading = DeterministicRandom.Unit(seed, draw + 3u) * 6.2831853f;
            float length = radiusMetres
                           * (LengthMinFraction
                              + (LengthMaxFraction - LengthMinFraction)
                                * DeterministicRandom.Unit(seed, draw + 4u));

            float dx = (float)Math.Cos(heading);
            float dz = (float)Math.Sin(heading);

            // The perpendicular direction (for the sideways swing).
            float px = -dz;
            float pz = dx;

            for (int i = 0; i < PointCount; i++)
            {
                float t = i / (float)(PointCount - 1);

                // The ends do not swing (if they did, both ends would look frayed).
                float taper = 1f - Math.Abs(t * 2f - 1f);

                uint js = draw + 10u + (uint)i * 3u;
                float side = (DeterministicRandom.Unit(seed, js) * 2f - 1f)
                             * JitterFraction * length * taper;
                float lift = (DeterministicRandom.Unit(seed, js + 1u) * 2f - 1f)
                             * VerticalFraction * thickness * taper;

                float along = (t - 0.5f) * length;

                into[i] = new TyphoonBoltPoint(
                    cx + dx * along + px * side,
                    cy + lift,
                    cz + dz * along + pz * side);
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
