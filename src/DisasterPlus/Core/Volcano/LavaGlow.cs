using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The lava's <b>glow</b>. **It is not a function of time.**
    ///
    /// ── Why it was rebuilt (2026-08-22, in-game report ④) ─────────────────────
    ///
    /// > The way the lava flow glows, flickering, is not realistic. (…)
    /// > Please fix it still glowing after the eruption is over.
    ///
    /// These are two separate defects.
    ///
    /// <b>1. The flicker.</b> The ribbon's texture used to have light and dark stripes
    /// baked into it (three cycles of a sinusoid), which were then scrolled at 0.35/second
    /// every frame with <c>SetTextureOffset</c>. There are only exactly three stripes
    /// across the ribbon's whole length, so **any one point on the ground goes bright →
    /// dark → bright in a little under a second**. That is what "flickering" actually was.
    /// Real lava flows are made of <b>a cooled black crust</b> and <b>glowing cracks</b>
    /// between the plates of it, and **the glow is a function of place, not of time**. What
    /// changes slowly does so because "that place is cooling", not because the brightness
    /// goes back and forth.
    ///
    /// <b>2. Still glowing after the eruption is over.</b> The old colour was
    /// <c>k = 0.15 + 0.85 × cool</c> with opacity <c>0.55 + 0.45 × cool</c>, so even at
    /// <c>cool = 0</c> (i.e. cooled right through) <b>k = 0.15 and α = 0.55</b> remained.
    /// **It was a formula that never went out, however long you waited.** And on top of
    /// that the drawing side keeps drawing for as long as the trail array is there, so even
    /// once the phase became <c>Done</c> the ribbon went on existing.
    ///
    /// ── Brightness as a function of place ─────────────────────────────────
    ///
    /// <c>u</c> runs across the ribbon (0 and 1 are the edges) and <c>v</c> runs along it,
    /// with **<c>v = 0</c> the vent and <c>v = 1</c> the advancing front**
    /// (<c>LavaRibbon</c> uses the polyline index directly as <c>v</c>). Only two places
    /// are bright:
    ///
    /// <list type="number">
    /// <item><b>The vent</b> (<c>v ≈ 0</c>) — it is continuously supplied, so it does not
    /// cool</item>
    /// <item><b>The advancing front</b> (<c>v ≈ 1</c>) — the crust breaks and the insides
    ///   come out, so it is the brightest of all</item>
    /// </list>
    ///
    /// Between them is <b>cooled crust</b>, where only the cracks glow.
    ///
    /// ★★ <b>This is also the implementation of "cooling with age".</b> As the lava
    ///   advances, points are added to the polyline and the <c>v</c> of a place already
    ///   laid down **shifts towards smaller values** — that is, it leaves the bright band
    ///   at the advancing front and moves into the crust.
    ///   A place cools without a single reference to time.
    ///
    /// ── The overall fade ───────────────────────────────────────
    ///
    /// <see cref="CoolFade"/> takes <c>VolcanoLava.CoolUnit</c> (1 = still hot, 0 = cooled
    /// right through) and **returns exactly 0 at 0**.
    /// Once <see cref="Visible"/> goes false, the drawing side folds the whole surface away.
    /// No room anywhere for "goes on glowing faintly".
    /// </summary>
    public static class LavaGlow
    {
        /// <summary>
        /// The side length of the texture we bake. **The Game side and
        /// tools/VolcanoPreview share this** — the fineness of the cracks
        /// (<see cref="CrackAlong"/> / <see cref="CrackAcross"/>) is chosen so that they
        /// read as lines at this resolution, so changing one without the other makes the
        /// in-game and offline pictures disagree. The actual cost is 128² × 4 B = 64 KB.
        /// </summary>
        public const int TextureSize = 128;

        /// <summary>The fraction that glows at the vent end (relative to the ribbon's whole
        /// length).</summary>
        public const float VentGlowFraction = 0.10f;

        /// <summary>The fraction that glows at the advancing front (likewise). **This is the
        /// brightest part.**</summary>
        public const float FrontGlowFraction = 0.13f;

        /// <summary>How bright the vent's glow is (taking the advancing front as 1).</summary>
        public const float VentGlowStrength = 0.85f;

        /// <summary>The brightness of the crust itself. **Not 0** (residual heat makes it look
        /// a dark red).</summary>
        public const float CrustFloor = 0.05f;

        /// <summary>How bright the cracks are (the amount added on top of the crust).</summary>
        public const float CrackStrength = 0.55f;

        /// <summary>The fineness of the cracks (repeat count in the direction along the
        /// ribbon).</summary>
        public const float CrackAlong = 10f;

        /// <summary>The fineness of the cracks (repeat count in the direction across the
        /// ribbon).</summary>
        public const float CrackAcross = 2.4f;

        /// <summary>The width counted as a crack. The narrower it is, the more it reads as
        /// "the gap between two plates".</summary>
        public const float CrackWidth = 0.25f;

        /// <summary>
        /// The exponent of the cooling. **The brightness on screen goes as the square of
        /// this value** (it multiplies both the colour and the opacity), so keep it below 1
        /// to get the shape "stays red for a while, then darkens sharply towards the end".
        /// </summary>
        public const float CoolFadePower = 0.75f;

        /// <summary>At or below this much heat left, not one surface is drawn (i.e. it
        /// disappears completely).</summary>
        public const float InvisibleBelow = 0.02f;

        /// <summary>
        /// The glow <c>[0,1]</c> at position <paramref name="v"/> along the ribbon
        /// (0 = the vent, 1 = the advancing front).
        /// **Time does not enter into it at all.**
        /// </summary>
        public static float AlongFlowUnit(float v)
        {
            if (IsBad(v)) return 0f;
            float t = v < 0f ? 0f : (v > 1f ? 1f : v);

            // The advancing front. The crust breaks and the insides come out, so it is the
            // brightest.
            float front = 0f;
            float toFront = 1f - t;
            if (FrontGlowFraction > 0f && toFront < FrontGlowFraction)
            {
                front = 1f - toFront / FrontGlowFraction;
            }

            // The vent. It is continuously supplied, so it does not cool.
            float vent = 0f;
            if (VentGlowFraction > 0f && t < VentGlowFraction)
            {
                vent = (1f - t / VentGlowFraction) * VentGlowStrength;
            }

            float glow = front > vent ? front : vent;
            // Smooth the rise at the ends (a band with sharp corners reads as "paint").
            return glow * glow * (3f - 2f * glow);
        }

        /// <summary>
        /// The brightness <c>[0,1]</c> of a crack (the gap between two plates). **A function
        /// of place alone**, giving irregularly spaced thin lines running diagonally across
        /// the ribbon.
        ///
        /// Using a sinusoid directly as the brightness gives "corrugated iron", so we use
        /// the same **zero crossings** as <see cref="VolcanoRelief"/>'s radial gullies —
        /// which makes the cracks narrow and the plates broad and flat.
        /// </summary>
        public static float CrackUnit(float u, float v)
        {
            if (IsBad(u) || IsBad(v)) return 0f;

            double a = 2.0 * Math.PI * (v * CrackAlong + u * 0.7);
            double b = 2.0 * Math.PI * (u * CrackAcross - v * 2.3);
            float series = (float)(Math.Sin(a) + 0.6 * Math.Sin(b) + 0.35 * Math.Sin(a * 0.37 + b));

            float abs = series < 0f ? -series : series;
            if (abs >= CrackWidth) return 0f;

            float k = 1f - abs / CrackWidth;
            return k * k;
        }

        /// <summary>
        /// The glow <c>[0,1]</c> at that one point. Crust + cracks + the hot bands at each
        /// end. **It never exceeds 1.**
        /// </summary>
        public static float GlowUnit(float u, float v)
        {
            float ends = AlongFlowUnit(v);

            // ★ Evaluate the cracks only once (three trigonometric calls). Call it twice
            //   and baking a 128² texture runs sin close to a hundred thousand times.
            float crack = CrackUnit(u, v);

            float crust = CrustFloor + CrackStrength * crack;
            float glow = ends > crust ? ends : crust;

            // In the hot bands at the ends, the crack pattern brightens along with them
            // (the plates themselves are melting).
            glow += ends * CrackStrength * crack * 0.5f;

            if (glow < 0f) return 0f;
            return glow > 1f ? 1f : glow;
        }

        /// <summary>
        /// The opacity <c>[0,1]</c> in the direction across the ribbon. A peaked shape going
        /// to 0 at the edges, so that **the lava's edges blur** (rather than being the
        /// straight edges of cut paper).
        /// </summary>
        public static float AcrossFalloff(float u)
        {
            if (IsBad(u)) return 0f;
            float t = u < 0f ? 0f : (u > 1f ? 1f : u);
            float k = 1f - Math.Abs(t * 2f - 1f);
            return k * k;
        }

        /// <summary>
        /// The overall fade. In <paramref name="coolUnit"/>, 1 is "still hot" and 0 is
        /// "cooled right through".
        /// **Returns exactly 0 at 0** — no room left for "goes on glowing faintly"
        /// (report ④).
        /// </summary>
        public static float CoolFade(float coolUnit)
        {
            if (IsBad(coolUnit) || coolUnit <= 0f) return 0f;
            float c = coolUnit > 1f ? 1f : coolUnit;
            // ★ The brightness that reaches the screen is **the square of this value**
            //   (because it multiplies both the colour and the opacity). So an exponent of
            //   0.75 gives "stays red for a while, then darkens sharply towards the end" —
            //   a shape close to how real lava looks.
            return (float)Math.Pow(c, CoolFadePower);
        }

        /// <summary>Should it still be drawn? Once this goes false, the drawing side folds the
        /// whole surface away.</summary>
        public static bool Visible(float coolUnit)
        {
            return !IsBad(coolUnit) && coolUnit > InvisibleBelow;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
