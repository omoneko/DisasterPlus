using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>One pixel of an icon. Four components in <c>[0,255]</c>.</summary>
    public struct IconPixel
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly byte A;

        public IconPixel(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Transparent.</summary>
        public static IconPixel None { get { return new IconPixel(0, 0, 0, 0); } }
    }

    /// <summary>
    /// **Holds the artwork for the disaster panel's tiles as pixel formulas.**
    /// <b>This is Core, so it never touches the engine</b> (no <c>Texture2D</c> appears
    /// here).
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; I'd like the volcano and typhoon tab icons turned into illustrations.
    ///
    /// The ⑤ and ④ tiles sitting in the disaster panel used to be **text only**.
    /// Every vanilla tile is a picture, so that row had two lots of text mixed into it.
    ///
    /// ── ★★ Why we draw them (rather than using vanilla sprites) ───────────────
    ///
    /// <c>DisasterPanelBar</c>'s class doc forbids it —
    /// **never name a foreground sprite**. Sprite names are atlas data and cannot be read
    /// from the assembly, so guessing at a name risks "an invisible tile". On top of that
    /// ⑤ (volcano) and ④ (typhoon) are <b>disasters that do not exist in vanilla</b>, so
    /// there is no picture to guess at in the first place.
    ///
    /// So <b>we draw them ourselves</b>. The siren mod's <c>WarningIcon</c> does the same
    /// thing, and it takes nothing more than pushing our own <c>Texture2D</c> into a
    /// <c>UITextureSprite</c>.
    ///
    /// ── ★★ They must read at a small size ────────────────────────────
    ///
    /// A tile is 109×100 and the picture is about 70% of that. It <b>has to make sense at
    /// around 70 px</b>. So:
    ///
    ///   - The form is decided by <b>silhouette</b> (thin lines vanish at 70 px)
    ///   - Only <b>two or three colours</b>. Gradation is only used inside the outline
    ///   - Put a dark outline at least 1 px wide around the edge — a tile's background can
    ///     be either light or dark
    ///
    /// We actually rendered them at 70 px and checked (<c>tools/IconPreview</c>).
    /// </summary>
    public static class DisasterIconArt
    {
        // ── Colours ────────────────────────────────────────────

        /// <summary>The volcano's cone. Dark basalt.</summary>
        private static readonly IconPixel Rock = new IconPixel(58, 52, 58, 255);

        /// <summary>The sunlit side of the volcano's cone.</summary>
        private static readonly IconPixel RockLit = new IconPixel(92, 84, 90, 255);

        /// <summary>Lava. An orange in the same direction as <c>VolcanoLavaFx</c>'s
        /// tint.</summary>
        private static readonly IconPixel Lava = new IconPixel(255, 122, 32, 255);

        /// <summary>The heart of the crater.</summary>
        private static readonly IconPixel LavaHot = new IconPixel(255, 214, 130, 255);

        /// <summary>The plume.</summary>
        private static readonly IconPixel Ash = new IconPixel(146, 142, 148, 255);

        /// <summary>The typhoon's cloud. The same sunlit white as
        /// <c>TyphoonVortexPuffFx</c>.</summary>
        private static readonly IconPixel Cloud = new IconPixel(242, 244, 248, 255);

        /// <summary>The shaded side of the typhoon's cloud.</summary>
        private static readonly IconPixel CloudShade = new IconPixel(176, 186, 204, 255);

        /// <summary>The typhoon's background (the sea).</summary>
        private static readonly IconPixel Sea = new IconPixel(28, 58, 96, 255);

        /// <summary>The outline. **The silhouette stands out whether the background is light
        /// or dark.**</summary>
        private static readonly IconPixel Outline = new IconPixel(16, 16, 20, 255);

        // ── The volcano ───────────────────────────────────────────

        /// <summary>The height of the cone's summit (<c>v</c>).</summary>
        private const float SummitV = 0.50f;

        /// <summary>The spread of the foot (one side, in <c>x</c>).</summary>
        private const float BaseHalf = 0.94f;

        /// <summary>The flat part of the summit (one side, in <c>x</c>).</summary>
        private const float SummitHalf = 0.17f;

        /// <summary>
        /// The volcano icon. <paramref name="u"/> and <paramref name="v"/> are in
        /// <c>[0,1]</c>, and <b><paramref name="v"/> is 0 at the bottom (the ground) and 1 at
        /// the top (the sky)</b>.
        ///
        /// ── ★★ The plume is "billowing", not "a trapezoid" ────────────────────
        ///
        /// The first version drew the plume as <b>a trapezoid widening upwards</b>. At 70 px
        /// **it looks like nothing but a funnel** — a straight edge is not a smoke edge
        /// (the first version in <c>tools/IconPreview</c>). Now it is <b>four overlapping
        /// circles</b>.
        ///
        /// For the same reason the cone dropped its straight lines too, in favour of
        /// <b>a concave ridge line</b> (the shape of a stratovolcano; a straight trapezoid
        /// reads as a plinth, not a mountain).
        /// </summary>
        public static IconPixel Volcano(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;              // -1 .. 1 (0 at the centre)

            // The plume first. It is **behind the cone**, so the cone beats it (overwritten
            // below).
            IconPixel plume = Plume(x, v);

            IconPixel cone = Cone(x, v);
            if (cone.A != 0) return cone;

            return plume;
        }

        /// <summary>
        /// The cone. The ridge line is concave (<c>(1-t)^0.72</c>), so the foot is wide and
        /// the shoulders are tight.
        /// The crater scoops a round hollow out of the centre of the summit, and only there
        /// do we use the lava colour.
        /// </summary>
        private static IconPixel Cone(float x, float v)
        {
            if (v > SummitV) return IconPixel.None;

            float ax = x < 0f ? -x : x;
            float t = v / SummitV;                                   // 0 = foot, 1 = summit

            // The concave ridge line.
            float half = SummitHalf + (BaseHalf - SummitHalf) * (float)Math.Pow(1f - t, 0.88);
            if (ax > half) return IconPixel.None;

            // ── The crater. Scoop a round hollow out of the centre of the summit.
            float craterLip = SummitV - 0.045f;
            if (v > craterLip)
            {
                float bowl = SummitHalf * 0.70f
                             * (float)Math.Sqrt(1f - (v - craterLip) / 0.05f + 0.0001f);
                if (ax < bowl)
                {
                    return v > craterLip + 0.022f ? LavaHot : Lava;
                }
            }

            // The outline. Laid along the ridge line at a constant thickness.
            if (half - ax < 0.05f) return Outline;

            // Two streaks of lava. **From the crater downwards**, meandering slightly.
            if (LavaStreak(x, v, 0.34f) || LavaStreak(x, v, -0.48f)) return Lava;

            // The light comes from the right.
            return x > 0.05f ? RockLit : Rock;
        }

        /// <summary>
        /// The plume. **Four circles**, overlapping, getting larger the higher they go and
        /// leaning slightly downwind.
        /// The point is not to create a single straight edge (see the class doc).
        /// </summary>
        private static IconPixel Plume(float x, float v)
        {
            if (v < SummitV - 0.06f) return IconPixel.None;

            // (centre x, centre v, radius)
            float[] cx = { 0.00f, 0.13f, -0.10f, 0.20f, -0.02f };
            float[] cv = { 0.59f, 0.70f, 0.79f, 0.86f, 0.88f };
            float[] cr = { 0.14f, 0.22f, 0.25f, 0.24f, 0.28f };

            float best = 999f;
            for (int i = 0; i < cx.Length; i++)
            {
                float dx = x - cx[i];
                float dv = v - cv[i];
                float d = (float)Math.Sqrt(dx * dx + dv * dv) - cr[i];
                if (d < best) best = d;
            }

            if (best > 0f) return IconPixel.None;
            if (best > -0.035f) return Outline;
            return Ash;
        }

        /// <summary>
        /// One streak of lava. It runs from the crater to the foot, meandering slightly.
        /// A positive <paramref name="lean"/> makes it flow to the right.
        /// </summary>
        private static bool LavaStreak(float x, float v, float lean)
        {
            if (v > SummitV - 0.05f) return false;

            float t = 1f - v / SummitV;                              // 0 = summit, 1 = foot
            float centre = lean * t * t + 0.05f * (float)Math.Sin(7.0 * t);
            float width = 0.030f + 0.040f * t;

            float d = x - centre;
            if (d < 0f) d = -d;
            return d <= width;
        }

        // ── The typhoon ───────────────────────────────────────────

        /// <summary>
        /// The typhoon icon. **Three arms with an eye left open**, over a circle of sea.
        /// The arms are logarithmic spirals, packed more tightly towards the centre.
        /// </summary>
        public static IconPixel Typhoon(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;
            float y = v * 2f - 1f;

            float r = (float)Math.Sqrt(x * x + y * y);
            if (r > 0.97f) return IconPixel.None;
            if (r > 0.90f) return Outline;

            // ── The eye. **Left as sea inside** (this gap is what makes it read as a
            //    typhoon).
            const float Eye = 0.17f;
            if (r < Eye) return Sea;
            if (r < Eye + 0.035f) return CloudShade;   // the inside of the eyewall

            float angle = (float)Math.Atan2(y, x);

            // ── The arms. Three logarithmic spirals θ = k ln r, 120 degrees apart.
            const int Arms = 3;
            const float Twist = 2.6f;

            float spiral = Twist * (float)Math.Log(r / Eye);
            float phase = angle - spiral;

            // Fold phase by one arm's spacing.
            float step = 6.2831853f / Arms;
            float local = phase - step * (float)Math.Floor(phase / step + 0.5);

            // The arms' thickness. They thin out towards the edge and trail off.
            float width = 0.62f - 0.30f * r;

            float d = local < 0f ? -local : local;
            if (d <= width)
            {
                // The arms' edges are shaded and their hearts are white.
                return d > width - 0.16f ? CloudShade : Cloud;
            }

            return Sea;
        }

        // ── The trench earthquake ──────────────────────────────────────

        /// <summary>The seabed.</summary>
        private static readonly IconPixel SeaBed = new IconPixel(64, 58, 52, 255);

        /// <summary>The fault's rupture surface. **This alone glows.**</summary>
        private static readonly IconPixel Rupture = new IconPixel(255, 196, 72, 255);

        /// <summary>The heart of the wave.</summary>
        private static readonly IconPixel Foam = new IconPixel(238, 244, 250, 255);

        /// <summary>The sky above the sea surface.</summary>
        private static readonly IconPixel Sky = new IconPixel(96, 124, 156, 255);

        /// <summary>
        /// The trench earthquake icon (2026-08-22, at the owner's request: "a new icon for
        /// this one too").
        /// <paramref name="v"/> is <b>0 at the bottom (the seabed) and 1 at the top (the
        /// sky)</b>.
        ///
        /// ── What reads at 70 px ────────────────────────────────
        ///
        /// A tile is 109×100 px, so the picture only ever appears at about 70 px square.
        /// **More than three elements will not read.** What goes in is:
        ///
        ///   1. <b>The sea</b> (so you can see at a glance that this disaster belongs to the
        ///      sea)
        ///   2. <b>The tsunami wave</b> (this is the point)
        ///   3. <b>The V-shaped trench on the seabed, with the rupture glowing in it</b>
        ///      (the cause)
        ///
        /// ★ Just as we learnt with the volcano icon (see its doc),
        ///   <b>straight lines do not read as natural objects</b>. The wave's crest is a sine
        ///   and the trench is a rounded V.
        /// </summary>
        public static IconPixel TrenchQuake(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return IconPixel.None;

            float x = u * 2f - 1f;

            // ── The round frame (built the same way as the other two) ──────────────
            float y = v * 2f - 1f;
            float r = (float)Math.Sqrt(x * x + y * y);
            if (r > 0.97f) return IconPixel.None;
            if (r > 0.90f) return Outline;

            // ── The seabed (0-0.36 from the bottom), with a V-shaped trench in the middle ──
            //   The valley is rounded into a bell shape (a sharp V reads as a crack, not a
            //   trench).
            float trench = 0.36f - 0.21f / (1f + 22f * x * x);
            if (v < trench)
            {
                // ★★ **The rupture is a single fissure running down from the valley floor.**
                //
                //   We originally drew it as "a band of constant depth below the valley
                //   surface", but that <b>traces the valley's shape</b>, and at 70 px
                //   **it looked like nothing but two yellow horns sprouting**
                //   (the first version in tools/IconPreview).
                //   Now it is a wedge running straight down from one point on the valley
                //   floor, widening as it goes.
                float depth = trench - v;
                float halfWidth = 0.055f + 0.55f * depth;
                if (x > -halfWidth && x < halfWidth) return Rupture;

                return SeaBed;
            }

            // ── The sea surface. The tsunami's crest breaks, rising to the right ─────────
            //   Two sines stacked rather than one, to make the crest asymmetric left to right
            //   (a symmetric wave reads as a hill, not a wave).
            float crest = 0.62f
                          + 0.17f * (float)Math.Sin(2.1f * x + 0.6f)
                          + 0.05f * (float)Math.Sin(5.3f * x + 1.9f);

            if (v > crest + 0.05f) return Sky;

            // White along the crest's edge. **Without some thickness it reads as a line.**
            if (v > crest - 0.10f) return Foam;

            // Underwater. Only directly below the crest is lightened, so the wave looks like
            // it is rearing up.
            float lift = crest - 0.10f - v;
            if (lift < 0.16f) return CloudShade;
            return Sea;
        }

        private static bool IsBad(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }
    }
}
