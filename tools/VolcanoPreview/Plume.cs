using System;
using System.Globalization;
using System.IO;
using System.Text;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// **Draws the eruption column from the side** (checking point 3 from the in-game report).
    /// The game is not launched.
    ///
    /// What is drawn is the 9 segments returned by <c>Core/Volcano/EruptionColumn</c> and the
    /// particle properties of <c>Core/Volcano/EruptionAshProfile</c> **themselves**.
    /// How particles are spawned is copied straight from the IL measurements of vanilla's
    /// <c>ParticleEffect.EmitParticles</c> (§B-4):
    ///
    /// <code>
    /// position = segment centre + disc(radius r) + up × [0, halfHeight)
    /// velocity = segment drift + (up·cos a + sideways·sin a) × speed     a = spawn angle
    /// count    ∝ max(100, π r²) × magnitude × rateOverTime × lifetime    (steady state)
    /// </code>
    ///
    /// ★ In the game the automatic throttling of <c>maxParticles</c>
    ///   (<c>pps ×= 1 − fill²</c>) kicks in and **always caps it**, so here too the total
    ///   particle count is fixed at <c>EruptionAshProfile.MaxParticles</c> and only the
    ///   distribution across segments is decided by the ratio of <c>magnitude × r²</c>.
    ///   This is the game's steady state.
    ///
    /// ★ The only randomness is <see cref="DeterministicRandom"/> (the same discipline as
    ///   Core). The same picture comes out every time.
    /// </summary>
    internal static class Plume
    {
        /// <summary>Width of the window drawn (m). The plume leans downwind, so more room is
        /// left to the right of the centre.</summary>
        private const float WindowWidthMetres = 4200f;

        /// <summary>Height of the window drawn (m).</summary>
        private const float WindowHeightMetres = 2600f;

        /// <summary>Metres per pixel.</summary>
        private const float MetresPerPixel = 3.2f;

        /// <summary>Position of the vent on screen (as a fraction of the window width).</summary>
        private const float VentXFraction = 0.24f;

        /// <summary>Opacity of a single particle. They overlap and darken.</summary>
        private const float ParticleAlpha = 0.16f;

        /// <summary>Colour of the column (dark grey-brown; the midpoint of <c>CloneAsh</c>'s
        /// startColor).</summary>
        private static readonly float[] ColumnColour = { 0.17f, 0.14f, 0.12f };

        /// <summary>Colour of the umbrella (pale grey; the midpoint of
        /// <c>CloneAshUmbrella</c>'s startColor).</summary>
        private static readonly float[] UmbrellaColour = { 0.31f, 0.30f, 0.29f };

        private static readonly float[] Sky = { 0.62f, 0.72f, 0.86f };

        /// <summary>Colour of the mountain.</summary>
        private static readonly float[] Mountain = { 0.30f, 0.28f, 0.24f };

        /// <summary>
        /// The eruption envelope (the same shape as
        /// <c>Game/Volcano/VolcanoEruption.Envelope</c>).
        /// **That one lives on the Game side, so the shape is copied here** (the numbers are
        /// the same 0.08 / 0.65).
        /// </summary>
        private const float RiseFraction = 0.08f;
        private const float DecayFraction = 0.65f;

        internal static void Report(string dir, StringBuilder log)
        {
            const VolcanoForm form = VolcanoForm.Strato;
            float radius = VolcanoShape.DefaultRadiusOf(form);
            float height = VolcanoShape.DefaultHeightOf(form);
            float crater = VolcanoShape.CraterRadiusOf(radius);

            log.AppendLine("## eruption column (strato, crater r = "
                           + crater.ToString("F0", CultureInfo.InvariantCulture) + " m)");
            log.AppendLine("  t/total | intensity | column m | umbrella r m | bend m | vent r m");

            float[] times = { 0.04f, 0.10f, 0.30f, 0.55f, 0.80f, 0.95f };
            int w = (int)(WindowWidthMetres / MetresPerPixel);
            int h = (int)(WindowHeightMetres / MetresPerPixel);

            var sheet = new byte[w * times.Length * h * 3];
            int sheetW = w * times.Length;

            for (int i = 0; i < times.Length; i++)
            {
                float t = times[i];
                float unit = Envelope(t);
                var column = new EruptionColumn(crater, unit, 1f, 0f,
                                                EruptionColumn.ReferenceWindMetresPerSecond);

                log.AppendLine("    " + t.ToString("F2", CultureInfo.InvariantCulture)
                               + "  |    " + unit.ToString("F2", CultureInfo.InvariantCulture)
                               + "   |   " + column.HeightMetres.ToString("F0", CultureInfo.InvariantCulture)
                               + "    |     " + column.UmbrellaRadiusMetres.ToString("F0", CultureInfo.InvariantCulture)
                               + "      |  " + column.BendMetres.ToString("F0", CultureInfo.InvariantCulture)
                               + "   |  " + column.RadiusAt(0f).ToString("F0", CultureInfo.InvariantCulture));

                byte[] frame = Draw(column, radius, height, w, h, (uint)(0x9E3779B1u + i));

                for (int y = 0; y < h; y++)
                {
                    Array.Copy(frame, y * w * 3, sheet, (y * sheetW + i * w) * 3, w * 3);
                }

                string name = "plume-strato-t" + ((int)(t * 100)).ToString("D3") + ".png";
                Png.Write(Path.Combine(dir, name), w, h, frame);
                Console.WriteLine("wrote " + Path.Combine(dir, name));
            }

            Png.Write(Path.Combine(dir, "plume-strato-sequence.png"), sheetW, h, sheet);
            Console.WriteLine("wrote " + Path.Combine(dir, "plume-strato-sequence.png"));
            log.AppendLine();
        }

        /// <summary>The same rise -&gt; sustain -&gt; decay as
        /// <c>VolcanoEruption.Envelope</c>.</summary>
        private static float Envelope(float t)
        {
            if (t <= 0f || t >= 1f) return 0f;
            if (t < RiseFraction) return t / RiseFraction;
            if (t > DecayFraction) return (1f - t) / (1f - DecayFraction);
            return 1f;
        }

        /// <summary>One frame. Draws the mountain's section first, then stacks the particles
        /// on top of it.</summary>
        private static byte[] Draw(EruptionColumn column, float mountainRadius,
                                   float mountainHeight, int w, int h, uint seed)
        {
            var cover = new float[w * h];
            var colour = new float[w * h * 3];

            Splat(column, EruptionAshProfile.Column, ColumnColour, false, cover, colour, w, h,
                  DeterministicRandom.Hash(seed, 0xC01u));
            Splat(column, EruptionAshProfile.Umbrella, UmbrellaColour, true, cover, colour, w, h,
                  DeterministicRandom.Hash(seed, 0x4B8u));

            var rgb = new byte[w * h * 3];
            float ventX = w * VentXFraction;
            // The vent is the crater floor, so the summit (the rim) is depth above it.
            float depth = VolcanoShape.CraterDepthOf(mountainHeight);

            for (int y = 0; y < h; y++)
            {
                float metresY = (h - 1 - y) * MetresPerPixel;   // height with the vent as 0
                for (int x = 0; x < w; x++)
                {
                    float metresX = (x - ventX) * MetresPerPixel;

                    float r = Sky[0], g = Sky[1], b = Sky[2];

                    // The mountain (a triangular section), measured from the vent's height.
                    float d = metresX < 0f ? -metresX : metresX;
                    if (d < mountainRadius)
                    {
                        float surface = mountainHeight * (1f - d / mountainRadius) - mountainHeight;
                        surface += depth;   // the crater floor is 0, so shift the mountain to it
                        if (metresY <= surface)
                        {
                            r = Mountain[0]; g = Mountain[1]; b = Mountain[2];
                        }
                    }

                    int c = y * w + x;
                    float a = cover[c];
                    if (a > 0f)
                    {
                        float k = a > 1f ? 1f : a;
                        float pr = colour[c * 3] / a;
                        float pg = colour[c * 3 + 1] / a;
                        float pb = colour[c * 3 + 2] / a;
                        r = r + (pr - r) * k;
                        g = g + (pg - g) * k;
                        b = b + (pb - b) * k;
                    }

                    int o = (y * w + x) * 3;
                    rgb[o] = Byte(r); rgb[o + 1] = Byte(g); rgb[o + 2] = Byte(b);
                }
            }

            return rgb;
        }

        /// <summary>Stacks one particle system's worth (either the column or the
        /// umbrella).</summary>
        private static void Splat(EruptionColumn column, EruptionAshProfile profile,
                                  float[] tint, bool umbrella,
                                  float[] cover, float[] colour, int w, int h, uint seed)
        {
            // The distribution across segments (the ratio of magnitude x r^2). In the game it
            // is capped by maxParticles, so the total is fixed at MaxParticles and only the
            // ratio is used.
            var share = new float[column.SegmentCount];
            float total = 0f;
            for (int i = 0; i < column.SegmentCount; i++)
            {
                EruptionColumnSegment s = column.SegmentAt(i);
                if (s.Umbrella != umbrella) continue;
                float area = s.RadiusMetres * s.RadiusMetres;
                share[i] = s.Magnitude * area;
                total += share[i];
            }
            if (!(total > 0f)) return;

            float ventX = w * VentXFraction;
            float meanLife = (profile.LifeMinSeconds + profile.LifeMaxSeconds) * 0.5f;
            uint draw = 0u;

            for (int i = 0; i < column.SegmentCount; i++)
            {
                if (!(share[i] > 0f)) continue;
                EruptionColumnSegment s = column.SegmentAt(i);

                int count = (int)(profile.MaxParticles * (share[i] / total));
                for (int n = 0; n < count; n++)
                {
                    draw++;
                    float u1 = DeterministicRandom.Unit(seed, draw * 7u + 1u);
                    float u2 = DeterministicRandom.Unit(seed, draw * 7u + 2u);
                    float u3 = DeterministicRandom.Unit(seed, draw * 7u + 3u);
                    float u4 = DeterministicRandom.Unit(seed, draw * 7u + 4u);
                    float u5 = DeterministicRandom.Unit(seed, draw * 7u + 5u);
                    float u6 = DeterministicRandom.Unit(seed, draw * 7u + 6u);

                    // Spawn position: disc x [0, halfHeight) (the same as EmitParticles as
                    // measured from the IL).
                    double theta = 2.0 * Math.PI * u1;
                    float radial = (float)Math.Sqrt(u2) * s.RadiusMetres;
                    float px = s.OffsetX + (float)Math.Cos(theta) * radial;
                    float py = s.OffsetY + u3 * s.HalfHeightMetres;

                    // Initial velocity: the axis (up) tilted by the spawn angle.
                    float angle = (profile.SpawnAngleMinDegrees
                                   + (profile.SpawnAngleMaxDegrees - profile.SpawnAngleMinDegrees) * u4)
                                  * (float)(Math.PI / 180.0);
                    float speed = profile.SpeedMin + (profile.SpeedMax - profile.SpeedMin) * u5;
                    double side = 2.0 * Math.PI * u6;
                    float vy = (float)Math.Cos(angle) * speed + s.DriftY;
                    float vx = (float)(Math.Sin(angle) * Math.Cos(side)) * speed + s.DriftX;

                    // Age is uniform over [0, lifetime), i.e. the steady-state distribution.
                    float age = DeterministicRandom.Unit(seed, draw * 7u) * meanLife * 2f;

                    px += vx * age;
                    py += vy * age - 0.5f * 9.81f * profile.GravityModifier * age * age;

                    float sx = ventX + px / MetresPerPixel;
                    float sy = (h - 1) - py / MetresPerPixel;
                    float sr = profile.SizeMetres * 0.5f / MetresPerPixel;

                    Disc(cover, colour, w, h, sx, sy, sr, tint);
                }
            }
        }

        /// <summary>Stacks one soft disc.</summary>
        private static void Disc(float[] cover, float[] colour, int w, int h,
                                 float cx, float cy, float radius, float[] tint)
        {
            if (radius < 0.6f) radius = 0.6f;

            int x0 = (int)(cx - radius), x1 = (int)(cx + radius) + 1;
            int y0 = (int)(cy - radius), y1 = (int)(cy + radius) + 1;
            if (x1 < 0 || y1 < 0 || x0 >= w || y0 >= h) return;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > w) x1 = w;
            if (y1 > h) y1 = h;

            float r2 = radius * radius;
            for (int y = y0; y < y1; y++)
            {
                float dy = y - cy;
                for (int x = x0; x < x1; x++)
                {
                    float dx = x - cx;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= r2) continue;

                    float fall = 1f - d2 / r2;
                    float a = ParticleAlpha * fall * fall;

                    int c = y * w + x;
                    cover[c] += a;
                    colour[c * 3] += a * tint[0];
                    colour[c * 3 + 1] += a * tint[1];
                    colour[c * 3 + 2] += a * tint[2];
                }
            }
        }

        private static byte Byte(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }
    }
}
