using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The geometry of the region the four destruction discs along the fault **can land in**.
    ///
    /// The authority is §A-3 of the IL facts document. Four times per step it re-draws a
    /// normalised position t ∈ [-0.4, 0.4] along the fault, places a disc of width
    /// w = W(1 - 4t²) there and destroys buildings at probability = 1.
    ///
    /// ── One disc's reach is w (not 2w) ─────────────────────
    ///
    /// This band used to be built with destructionRadiusMax = 2w as the reach, and **that
    /// was 33% too wide.** The actual argument at the call site is
    /// <c>preRadius: w</c> (IL_0270-0298: ldloc.s 17 is w, and w goes into arg3 unchanged),
    /// and <c>DisasterHelpers.DestroyBuildings</c> puts
    /// <c>if (dist &gt;= preRadius) continue;</c> (the <c>bge.un</c> at IL_0130)
    /// **ahead of both the seed generation and the ramp calculation**.
    /// 2w only affects fD's numerator, and over the range <c>dist &lt; w</c> we have
    /// fD &gt; 1, which is then multiplied by probability = 1, so the comparison is
    /// unconditionally true.
    /// **In other words a disc's reach is exactly w, and 2w appears nowhere.**
    ///
    /// **This is "where it can hit", not "where it hits".**
    /// The positions are re-drawn every step, so a building inside the band may well never be
    /// hit. Callers must always state that alongside (Strings.EarthquakeFaultBandNote).
    /// Conversely, the only buildings about which we may assert "it will not fall" for the
    /// whole-quake disc are **those outside this band** (used by Task 5's BuildingMargin).
    ///
    /// **Do not mix this up with the whole-quake disc (SeismicIntensity).** That one is a
    /// linear ramp at probability = 0.02 compared against a threshold fixed per building — a
    /// deterministic test. This one is probability = 1, i.e. a building caught in the band is
    /// destroyed regardless of its threshold. **They are two different models**, so the fault
    /// band must not be displayed as "a region where s is high" (the final paragraph of
    /// design document §3.1).
    ///
    /// The meander (<c>p.x += dir.y * (s*w*0.5)</c> / <c>p.y -= dir.x * (s*w*0.5)</c>,
    /// IL_0201-023E) shifts a disc's centre **up to 0.5w perpendicular** to the fault line
    /// (s = sin(...) ∈ [-1, 1]). Combined with the reach w, **the perpendicular envelope is
    /// 1.5w**. The previous doc said the meander was "not counted because it fits inside
    /// 2w", but that 2w was itself wrong, so we do count the meander.
    ///
    /// The same reasoning applies along strike: the reach w is added to the 0.4·L limit on
    /// the disc centres.
    ///
    /// ── The reach is not determined by "a single t" ──────────────────────────
    ///
    /// <see cref="Contains"/> originally tested intersection with the circle at **exactly
    /// one** t — the one nearest the point (along/L, clamped). **That is wrong.**
    /// w tapers with t, so the t that minimises the along-strike residual is not necessarily
    /// the t that maximises the reach. **A fatter disc nearer the centre can reach the point
    /// even from a little further away along strike.**
    ///
    /// A counter-example (L = 1000, W = 100, point at along 300 / across 99):
    ///   t = 0.30 … w = 64.00, residual 0, across left over 99 - 32.00 = 67.00 → 67.00² &gt; 64.00², outside
    ///   t = 0.28 … w = 68.64, residual 20, across left over 99 - 34.32 = 64.68
    ///              → 20² + 64.68² = 4583 &lt; 68.64² = 4712, **inside**
    /// L : W = 10 : 1 is a typical ratio for a vanilla fault, so this is not some rare edge
    /// case. The direction of the error is the worst one too: **it calls a building inside
    /// the band "outside"** and then claims the whole-quake disc's "it will not collapse"
    /// about it — while a probability = 1 disc is judging it separately.
    ///
    /// So <see cref="Contains"/> searches the **whole range** of t. See its own doc for the
    /// details.
    ///
    /// When Length / Width are 0 (i.e. the four prefab values could not be read),
    /// <see cref="Known"/> is false and Contains always returns false.
    /// So as not to restate "we do not know" as "outside", callers must always check Known.
    /// </summary>
    public struct FaultBand
    {
        /// <summary>The limit on the normalised position a disc centre lands at
        /// (IL: Randomizer.Int32(-400, 400) * 0.001).</summary>
        public const float MaxOffset = 0.4f;

        public readonly Vec2 Centre;

        /// <summary>The fault's strike. IL: new Vector2(-sin(m_angle), cos(m_angle)).</summary>
        public readonly Vec2 Direction;

        /// <summary>L = m_crackLength * (0.5 + intensity * 0.005). The caller passes the
        /// already-computed value.</summary>
        public readonly float Length;

        /// <summary>W = m_crackWidth * (0.5 + intensity * 0.005). Likewise.</summary>
        public readonly float Width;

        public FaultBand(Vec2 centre, float angleRadians, float length, float width)
        {
            Centre = centre;
            double a = angleRadians;
            Direction = new Vec2(-(float)System.Math.Sin(a), (float)System.Math.Cos(a));
            Length = length > 0f && !float.IsNaN(length) ? length : 0f;
            Width = width > 0f && !float.IsNaN(width) ? width : 0f;
        }

        /// <summary>Whether the geometry is settled. If false, do not judge inside from
        /// outside.</summary>
        public bool Known { get { return Length > 0f && Width > 0f; } }

        public Vec2 EndA { get { return Centre + Direction * (-Length * 0.5f); } }
        public Vec2 EndB { get { return Centre + Direction * (Length * 0.5f); } }

        /// <summary>
        /// The **width** w = W(1 - 4t²) of one disc landing at normalised position t
        /// (IL_01D6-01EA). This is <c>preRadius</c>, and it is the reach directly.
        /// </summary>
        public float PatchRadiusAt(float t)
        {
            if (!Known || float.IsNaN(t)) return 0f;
            float taper = 1f - 4f * t * t;
            if (taper <= 0f) return 0f;
            return Width * taper;
        }

        /// <summary>
        /// The perpendicular reach from the fault line at normalised position t
        /// = **reach w + meander 0.5w = 1.5w** (derived in the class doc).
        /// </summary>
        public float HalfWidthAt(float t)
        {
            return 1.5f * PatchRadiusAt(t);
        }

        /// <summary>
        /// How many divisions we scan the disc centres' along-strike position in.
        ///
        /// What we are after is the minimum of <see cref="Gap"/> over
        /// <c>u ∈ [-0.4L, 0.4L]</c>, but <c>w(u)</c> is quadratic, so Gap is a piecewise
        /// quartic and **is not guaranteed to be unimodal** (neither ternary search nor
        /// golden-section search applies). We catch the trough with a uniform scan and then
        /// close in with <see cref="RefineSteps"/>.
        ///
        /// The step is <c>0.8L / 96</c>. At L : W = 10 : 1 the interval of u that satisfies
        /// the condition is about 2w ≒ 0.2L wide, so a step of <c>0.0083L</c> is more than
        /// 20 times finer. The verdict can only waver within half a step of the true
        /// boundary, and the refinement picks that up.
        /// </summary>
        private const int ScanSteps = 96;

        /// <summary>How many times we halve in on the trough the scan caught. 0.8L/96 shrinks
        /// by a factor of 2^-24.</summary>
        private const int RefineSteps = 24;

        /// <summary>
        /// Whether p is inside the region the destruction discs can land in. **Not "gets
        /// hit".**
        ///
        /// A disc centre lands at <c>(u, s·w(u)·0.5)</c> (<c>u = t·L ∈ [-0.4L, 0.4L]</c>,
        /// <c>s ∈ [-1, 1]</c>) and reaches out to radius <c>w(u)</c> from there.
        /// s is continuous, so the reach for a given u is **everything within <c>w(u)</c> of
        /// a vertical segment of length <c>w(u)</c>** (a stadium shape).
        /// The answer we want is whether p lies in the union of all of those over u.
        ///
        /// Looking at the single point <c>u = along</c> is not enough — as the counter-example
        /// in the class doc shows, **a fatter disc nearer the centre can reach**. The version
        /// that looked at one point alone misjudged buildings inside the band as outside and
        /// then claimed "it will not collapse" about them.
        ///
        /// When <c>u = along</c> is attainable (<c>|along| ≤ 0.4L</c>) the along-strike
        /// residual is 0 and the condition degenerates to <c>across ≤ 1.5·w</c>, which agrees
        /// exactly with <see cref="HalfWidthAt"/>.
        /// </summary>
        public bool Contains(Vec2 p)
        {
            if (!Known) return false;

            float dx = p.X - Centre.X;
            float dz = p.Z - Centre.Z;

            // Decompose into the component along the fault and the one perpendicular to it.
            float along = dx * Direction.X + dz * Direction.Z;
            float across = dx * Direction.Z - dz * Direction.X;
            if (across < 0f) across = -across;
            if (float.IsNaN(along) || float.IsNaN(across)) return false;

            float half = MaxOffset * Length;   // the along-strike limit on disc centres

            // Early rejection. No disc is ever fatter than W, so anything outside these two
            // is outside without needing the scan (the cursor normally drops out here).
            if (across > 1.5f * Width) return false;
            if (along > half + Width || along < -(half + Width)) return false;

            float step = 2f * half / ScanSteps;
            float bestU = -half;
            float bestGap = Gap(-half, along, across);
            if (bestGap <= 0f) return true;

            for (int i = 1; i <= ScanSteps; i++)
            {
                float u = -half + step * i;
                float gap = Gap(u, along, across);
                if (gap <= 0f) return true;
                if (gap < bestGap) { bestGap = gap; bestU = u; }
            }

            // Catch the case where the minimum falls between scan samples, halving in from
            // both sides.
            float h = step * 0.5f;
            for (int i = 0; i < RefineSteps; i++)
            {
                float lo = bestU - h;
                if (lo < -half) lo = -half;
                float hi = bestU + h;
                if (hi > half) hi = half;

                float gapLo = Gap(lo, along, across);
                if (gapLo <= 0f) return true;
                float gapHi = Gap(hi, along, across);
                if (gapHi <= 0f) return true;

                if (gapLo < bestGap) { bestGap = gapLo; bestU = lo; }
                if (gapHi < bestGap) { bestGap = gapHi; bestU = hi; }
                h *= 0.5f;
            }

            return false;
        }

        /// <summary>
        /// By how much the point **overshoots** the reach of a disc landing at along-strike
        /// position <paramref name="u"/>. Zero or below means the disc at that u (with the
        /// meander used to the full) reaches the point.
        ///
        ///   Gap(u) = (along - u)² + max(0, across - 0.5·w(u))² - w(u)²
        ///
        /// The <c>max</c> in the second term expresses that the meander can bring the centre
        /// up to 0.5w towards the point. When the point is directly beside the segment
        /// (<c>across ≤ 0.5w</c>) it is 0 and the condition degenerates to
        /// <c>|along - u| ≤ w</c>.
        /// </summary>
        private float Gap(float u, float along, float across)
        {
            float ratio = u / Length;
            float w = Width * (1f - 4f * ratio * ratio);

            // No disc lands here (at |t| ≧ 0.5 the width has tapered to 0).
            if (w <= 0f) return float.MaxValue;

            float da = along - u;

            float dacross = across - 0.5f * w;
            if (dacross < 0f) dacross = 0f;

            return da * da + dacross * dacross - w * w;
        }
    }
}
