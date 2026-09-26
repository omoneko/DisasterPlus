using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// A **polyline of half-widths** along the fault, used to draw
    /// <see cref="FaultBand"/> on the map.
    ///
    /// ── Why "measure the predicate" rather than "draw from a formula" ─────────────────
    ///
    /// The band's edge cannot be written in closed form. Its reach is the **union** of
    /// "discs of radius <c>w(u)</c> (whose centres meander by ±0.5·w(u))" over
    /// <c>u ∈ [-0.4L, 0.4L]</c>, and since <c>w</c> is quadratic in u,
    /// <c>FaultBand.Gap</c> comes out as a piecewise quartic that is not even unimodal
    /// (see the doc on <see cref="FaultBand.Contains"/>, and the addendum to whole-mod
    /// review C1). Build a separate "roughly this shape" here and you get the
    /// disagreement where **a building the panel calls "inside the band" is drawn outside
    /// the band on the map**.
    ///
    /// So the outline is measured by **bisecting <see cref="FaultBand.Contains"/> itself**.
    /// By definition, the shape drawn and the shape tested agree.
    ///
    /// ── Why it is not measured every frame ───────────────────────────
    ///
    /// One <see cref="Rebuild"/> is <c>(Segments+1) × (1 + BisectionSteps)</c> = 209 calls
    /// to <c>Contains</c>, and <c>Contains</c> itself performs up to 145 evaluations of
    /// <c>Gap</c>. **At most once per earthquake on the drawing path.**
    ///
    /// ── From "never call this" to "once is fine" (second-layer review M1) ──────────
    ///
    /// This doc originally said "never call this on the drawing path", but
    /// <c>EarthquakeOverlay.DrawQuake</c> does in fact call <c>Rebuild</c> from inside
    /// <c>OnPostRender</c> (narrowed to once per earthquake by <c>Matches</c>).
    /// **One of the two was wrong, so the amount of work was estimated and the doc was the
    /// one that got fixed:**
    ///
    ///   209 × 145 ≈ 30 thousand evaluations of <c>Gap</c>. <c>Gap</c> is one branch and a
    ///   few multiplications of float arithmetic, and it is paid **once per frame at most**,
    ///   on the first frame the earthquake appears. Paid every frame it would be orders of
    ///   magnitude too heavy, but once per earthquake is invisible even inside
    ///   <c>OnPostRender</c>.
    ///
    /// Moving it to the panel's tick was rejected. The overlay shows even when the panel is
    /// closed, so putting it on the panel's tick means **a player who never opens the panel
    /// never gets the band drawn at all**. Drawing it a frame late was rejected too,
    /// because the band would be missing for exactly one frame (i.e. indistinguishable from
    /// "the geometry is unknown").
    ///
    /// **So the rule is this: call it only when <see cref="Matches"/> is false. Do not give
    /// this type a path that gets called every frame.**
    ///
    /// Once is enough because this polyline is a function of <see cref="FaultBand.Length"/>
    /// and <see cref="FaultBand.Width"/> **alone** (it is held in local along-strike /
    /// across-strike coordinates, so it depends on neither the epicentre's position nor
    /// <c>m_angle</c>). L and W are <c>m_crackLength/Width × (0.5 + intensity×0.005)</c>
    /// and are constant for the duration of one earthquake. So measuring once per
    /// earthquake suffices (<see cref="Matches"/> being that test).
    ///
    /// The array is allocated once at construction and reused thereafter. **Rebuild
    /// allocates nothing.**
    /// </summary>
    public sealed class FaultBandOutline
    {
        /// <summary>
        /// The number of trapezia. There are <c>Segments + 1</c> sample points.
        ///
        /// The band narrows along strike as the quadratic <c>w(u) = W(1-4t²)</c>, so ten
        /// trapezia (i.e. a polyline approximation of a quadratic in ten pieces) produce no
        /// visible difference. Raising the count raises the draw calls one for one (see the
        /// breakdown of <c>MaxDrawCalls</c>).
        /// </summary>
        public const int Segments = 10;

        /// <summary>The bisection count. Relative error 2^-18 from the 1.5W upper bound (0.6
        /// mm at W=100 m).</summary>
        private const int BisectionSteps = 18;

        private readonly float[] _halfWidths = new float[Segments + 1];

        /// <summary>The L at the time of measurement. A key for <see cref="Matches"/>.</summary>
        public float Length { get; private set; }

        /// <summary>The W at the time of measurement. A key for <see cref="Matches"/>.</summary>
        public float Width { get; private set; }

        /// <summary>
        /// The one-sided extent along strike, <c>0.4L + w(0.4)</c>.
        /// The reach at that position is added to the 0.4L bound on the disc **centres**
        /// (see the end of design doc §3.1; the old implementation was dropping exactly
        /// one w's worth at each end).
        /// </summary>
        public float AlongExtent { get; private set; }

        /// <summary>
        /// Whether the geometry is settled and it is all right to draw.
        ///
        /// **When this is false, do not draw a single band.** The four prefab values
        /// (<c>m_crackLength</c> / <c>m_crackWidth</c> and the rest) are not in the DLL
        /// (IL findings doc §A-0), and if they cannot be read in-game then the band's size
        /// is unknown. Draw "unknown" as "probably about this much" and, on the map, it
        /// becomes indistinguishable from a measured value.
        /// </summary>
        public bool Known { get; private set; }

        /// <summary>Whether this polyline was the one measured for that L / W.</summary>
        public bool Matches(float length, float width)
        {
            return Known && Length == length && Width == width;
        }

        /// <summary>
        /// The along-strike position (distance from the band's centre; it can be negative).
        /// <c>i = 0</c> is one end.
        /// </summary>
        public float AlongAt(int sample)
        {
            if (!Known || sample < 0 || sample > Segments) return 0f;
            return -AlongExtent + 2f * AlongExtent * sample / Segments;
        }

        /// <summary>
        /// At that along-strike position, the greatest distance a disc reaches across
        /// strike from the fault line.
        /// At the middle it comes to <c>1.5W</c> (reach w plus meander 0.5w).
        /// </summary>
        public float HalfWidthAt(int sample)
        {
            if (!Known || sample < 0 || sample > Segments) return 0f;
            return _halfWidths[sample];
        }

        /// <summary>
        /// <b>At most once per earthquake</b> (see the class doc). Always check
        /// <see cref="Matches"/> before calling. That one call is the only thing the
        /// drawing path (<c>OnPostRender</c>) may do here.
        /// </summary>
        public void Rebuild(FaultBand band)
        {
            Known = false;
            Length = 0f;
            Width = 0f;
            AlongExtent = 0f;
            for (int i = 0; i <= Segments; i++) _halfWidths[i] = 0f;

            if (!band.Known) return;

            float extent = FaultBand.MaxOffset * band.Length + band.PatchRadiusAt(FaultBand.MaxOffset);
            if (extent <= 0f || float.IsNaN(extent)) return;

            // No disc is ever thicker than W, so 1.5W is enough as the across-strike bound
            // (the same value as FaultBand.Contains' early rejection).
            float ceiling = 1.5f * band.Width;

            for (int i = 0; i <= Segments; i++)
            {
                float u = -extent + 2f * extent * i / Segments;

                // If it does not reach even onto the fault line (across = 0), that position
                // is not part of the band.
                if (!Reaches(band, u, 0f)) { _halfWidths[i] = 0f; continue; }

                // Contains is monotone in across (Gap's second term is max(0, across-0.5w)²,
                // which is non-decreasing), so bisection can be used.
                float lo = 0f;
                float hi = ceiling;
                if (Reaches(band, u, hi)) { _halfWidths[i] = hi; continue; }

                for (int k = 0; k < BisectionSteps; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (Reaches(band, u, mid)) lo = mid; else hi = mid;
                }
                _halfWidths[i] = lo;
            }

            Length = band.Length;
            Width = band.Width;
            AlongExtent = extent;
            Known = true;
        }

        /// <summary>
        /// Takes local coordinates (along strike <paramref name="along"/> / across strike
        /// <paramref name="across"/>) back into the world and asks
        /// <see cref="FaultBand.Contains"/>.
        ///
        /// The across-strike basis is <c>(Direction.Z, -Direction.X)</c>. It matches
        /// <c>Contains</c>' <c>across = dx·Dir.Z - dz·Dir.X</c> right down to the sign (get
        /// that wrong and the meander's asymmetry is measured the wrong way round).
        /// </summary>
        private static bool Reaches(FaultBand band, float along, float across)
        {
            var p = band.Centre
                    + band.Direction * along
                    + new Vec2(band.Direction.Z, -band.Direction.X) * across;
            return band.Contains(p);
        }
    }
}
