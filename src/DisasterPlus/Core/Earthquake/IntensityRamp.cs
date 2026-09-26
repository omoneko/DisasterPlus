namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The arrangement for putting the whole-quake disc's ramp <c>s = 1 - d/R</c> on screen
    /// as **concentric overpainting**.
    ///
    /// ── Why "overpainting" ──────────────────────────────
    ///
    /// All the CS1 overlay API can draw is <b>filled</b> circles, quads and bands
    /// (<c>RenderManager.OverlayEffect</c>: a circle is one pass handing
    /// <c>(x, z, -r, +r)</c> to <c>ID_CenterPos</c>, a quad is one pass handing the four
    /// corners, and neither takes a thickness argument, i.e. neither is an outline).
    /// There is no way to produce a continuous-tone ramp in a single draw, so we
    /// **overpaint concentric circles largest-first, semi-transparently**, to build a
    /// stepped ramp.
    ///
    /// The step count is **the same 10** as <see cref="SeismicScale.Steps"/>. That is not a
    /// matter of looks but of honesty: the ten-step bar the panel's cursor row prints and
    /// the ten steps of shading on the map get **the same quantisation**. If one were ten
    /// steps and the other eight, two different "steps" for the same s would be on screen
    /// at once.
    ///
    /// ── Make the shade proportional to s (do not bend it) ─────────────────────────
    ///
    /// Disc <c>k</c> (0 the outermost, <c>Steps-1</c> the innermost) covers the region
    /// <c>s &gt; k/Steps</c>. So once discs 0..k have been painted, the annulus actually
    /// visible is <c>k/Steps &lt; s ≤ (k+1)/Steps</c>, whose representative value is the
    /// midpoint <c>(k+0.5)/Steps</c>. **The alpha of each individual disc is worked
    /// backwards so that the accumulated opacity there is exactly
    /// <c>MaxOpacity × s</c>** (see <see cref="DrawAlpha"/>). Simply stacking the same
    /// alpha gives the **saturating curve** <c>1-(1-a)^(k+1)</c>, which makes the weak end
    /// look stronger and the strong end weaker. That is nothing other than drawing a
    /// falloff different from the quantity you claim to be showing.
    ///
    /// ── Assumption: that <c>alphaBlend: true</c> really is alpha compositing ───────────
    ///
    /// <c>OverlayEffect</c> holds **two shaders**, <c>m_shapeShader</c> and
    /// <c>m_shapeShaderBlend</c>, and the <c>alphaBlend</c> argument alone decides which is
    /// used (measured from IL: <c>DrawCircle</c> IL_0127–013F / <c>DrawQuad</c>
    /// IL_014C–0164). There is no reason for there to be two other than "one of them does
    /// not composite", and vanilla itself has <c>DisasterTool.RenderOverlay</c> laying a
    /// circle over the district colours with <c>alphaBlend: true</c>. The shaders' blend
    /// equations themselves are not in the DLL (they are on the asset side), so this is the
    /// last step that the IL cannot pin down.
    ///
    /// **Even the ways it could break if we are wrong are pinned down.** There are two ways
    /// it could go wrong, and **in both of them <see cref="DrawAlpha"/> is monotonically
    /// increasing in k, so the direction in which the shading deepens towards the epicentre
    /// can never be reversed.** What changes is only the shape of the curve and the
    /// absolute darkness on screen.
    ///
    ///   1. **Overwrite (no compositing)** … the colour of the last disc drawn simply
    ///      stays. Each step then has the darkness of <see cref="DrawAlpha"/> itself, and
    ///      at the epicentre <c>a_9 ≈ 0.113</c>. The curve is distorted convexly.
    ///
    ///   2. **<c>Blend SrcAlpha OneMinusSrcAlpha</c> (i.e. Unity's default)**
    ///      … this is the more likely of the two. That equation composites colour
    ///      correctly, but **it multiplies the alpha channel by <c>srcA</c> a second
    ///      time**, giving <c>dstA' = srcA² + dstA(1 - srcA)</c>.
    ///      The outermost step is <c>a₀ = 0.0275</c>, so <c>0.0275² = 0.00076</c>, and even
    ///      stacked up the accumulation at the epicentre falls far short of
    ///      <see cref="MaxOpacity"/> = 0.55, making **the whole overlay a barely visible
    ///      tint**. The order of the steps is preserved, so the shading does not come out
    ///      backwards.
    ///
    /// The shaders' blend equations are not in the DLL (they are on the asset side), so
    /// **the IL can be pushed no further than this**. That means item 53 on the in-game
    /// checklist is not covered by "does it look like ten steps / like one sheet" alone —
    /// breakage 2 shows up as "ten steps but all of them faint", so
    /// **always check alongside it whether the epicentre is roughly half opaque or merely
    /// faintly tinted**. The former means the blend rule is as assumed; the latter means it
    /// is case 2.
    /// </summary>
    public static class IntensityRamp
    {
        /// <summary>
        /// The step count. **It must equal <see cref="SeismicScale.Steps"/>** (the "same
        /// quantisation" from the class doc). A unit test pins that agreement.
        /// </summary>
        public const int Steps = SeismicScale.Steps;

        /// <summary>
        /// The cap on the accumulated opacity at the epicentre (s = 1).
        ///
        /// Not 1.0. Under the overlay are the terrain, the roads and the buildings, and
        /// hiding them completely turns it from "a distribution laid over the map" into "a
        /// replacement for the map".
        /// </summary>
        public const float MaxOpacity = 0.55f;

        /// <summary>
        /// The radius of disc <paramref name="index"/>. 0 is the whole-quake disc itself
        /// (R), and <c>Steps-1</c> is the innermost (R/Steps). **Draw them largest-first.**
        /// </summary>
        public static float RadiusOf(int index, float radius)
        {
            if (index < 0 || index >= Steps) return 0f;
            if (float.IsNaN(radius) || radius <= 0f) return 0f;
            return radius * (Steps - index) / Steps;
        }

        /// <summary>
        /// The representative s (the midpoint) of the annulus visible once discs
        /// 0..<paramref name="index"/> have been painted.
        /// </summary>
        public static float RepresentativeS(int index)
        {
            if (index < 0) return 0f;
            if (index >= Steps) return 1f;
            return (index + 0.5f) / Steps;
        }

        /// <summary>
        /// The accumulated opacity we **want to see** in that annulus. Strictly
        /// proportional to s.
        /// </summary>
        public static float TargetOpacity(int index)
        {
            if (index < 0) return 0f;
            return MaxOpacity * RepresentativeS(index);
        }

        /// <summary>
        /// The alpha given to disc <paramref name="index"/> **on its own**.
        ///
        /// Chosen so that, alpha-composited largest-first, the accumulation over annulus k
        /// comes to <see cref="TargetOpacity"/>(k):
        /// <code>
        /// 1 - A_k = (1 - a_0)(1 - a_1)...(1 - a_k)
        /// a_k     = (A_k - A_{k-1}) / (1 - A_{k-1})
        /// </code>
        /// The numerator is the constant <c>MaxOpacity / Steps</c> and the denominator is
        /// less than 1, so a_k is monotonically increasing in k.
        /// </summary>
        public static float DrawAlpha(int index)
        {
            if (index < 0 || index >= Steps) return 0f;

            float previous = index == 0 ? 0f : TargetOpacity(index - 1);
            float rest = 1f - previous;
            if (rest <= 0f) return 0f;

            float a = (TargetOpacity(index) - previous) / rest;
            if (a < 0f) return 0f;
            if (a > 1f) return 1f;
            return a;
        }

        /// <summary>
        /// The result of alpha-compositing <see cref="DrawAlpha"/> largest-first.
        /// **Working backwards, for tests only**; the drawing side does not use it.
        /// </summary>
        public static float AccumulatedOpacity(int index)
        {
            float remaining = 1f;
            for (int k = 0; k <= index && k < Steps; k++)
            {
                remaining *= 1f - DrawAlpha(k);
            }
            return 1f - remaining;
        }
    }
}
