namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The typhoon's **left/right bias relative to its direction of travel** (the dangerous
    /// semicircle). <b>This is Core, so it touches the engine not at all.</b>
    ///
    /// ── What it models ──────────────────────────────────
    ///
    /// A real typhoon is not left-right symmetric. The rotation speed of the vortex and the
    /// typhoon's own translation speed **add up on one side** and **cancel on the other**,
    /// and the side where they add is called the dangerous semicircle. In the northern
    /// hemisphere (an anticlockwise vortex) that is **the right-hand side of the direction
    /// of travel**; in the southern hemisphere (a clockwise vortex) it is the left.
    ///
    /// The owner's instruction said "the left-hand side of the direction of travel", but
    /// **was corrected to the right afterwards**. The correction is right (the northern
    /// hemisphere's dangerous semicircle is the right-hand side). Left is the southern
    /// hemisphere's story, so passing <c>southernHemisphere</c> to
    /// <see cref="RadiusFactor"/> / <see cref="ChanceFactor"/> moves it to the left (it
    /// switches on a single setting).
    ///
    /// ── The coordinate system (get this wrong and the bias lands on the wrong side) ─────
    ///
    /// ④'s heading φ is in the mathematical convention, with <c>(cos φ, sin φ)</c> as
    /// (X, Z) (see <c>TyphoonTrack.ArcPosition</c>). In the CS world X is east and Z is
    /// north, so "the right of the direction of travel" seen from above is φ turned by −90°:
    ///
    /// <code>
    /// forward = ( cos φ,  sin φ)
    /// right   = ( sin φ, -cos φ)      // turning (a, b) by −90° gives (b, −a)
    /// </code>
    ///
    /// Check it: travelling north (φ = 90°) gives <c>forward = (0, 1) = +Z</c> and
    /// <c>right = (1, 0) = +X = east</c>. **Face north and your right is east**, which is
    /// correct. That check is pinned by the tests.
    ///
    /// ── Why it follows the track round a bend ──────────────────────────
    ///
    /// This type **only takes the heading as an argument** and remembers not one bit of it
    /// itself. The caller passes <c>TyphoonController.HeadingRadians</c> every tick (the
    /// value <c>TyphoonTrack.HeadingAt</c> re-derives from the elapsed frame count with a
    /// closed-form expression), so **when the track bends, the bias turns with it on the
    /// spot**. Do not cache "the heading at the origin" anywhere — that is exactly how you
    /// build a bias that does not follow a bending track.
    ///
    /// ── Keeping it modest ────────────────────────────────
    ///
    /// The caps are +<see cref="MaxRadiusBoost"/> on the radius and
    /// +<see cref="MaxChanceBoost"/> on the probability, and **both are "noticeable", not
    /// "a different typhoon".** The left-hand side is exactly 1× (it is not weakened). The
    /// instruction was "strengthen the right-hand side slightly", not "weaken the left".
    /// </summary>
    public static class TrackBias
    {
        /// <summary>How far the damage radius is stretched on the right (1.0 =
        /// unchanged).</summary>
        public const float MaxRadiusBoost = 0.18f;

        /// <summary>How far the collapse probability is raised on the right (1.0 =
        /// unchanged).</summary>
        public const float MaxChanceBoost = 0.30f;

        /// <summary>
        /// Position left or right of the direction of travel, [-1, 1]. **+1 is dead right**,
        /// −1 dead left, and dead ahead and dead astern are 0.
        ///
        /// <paramref name="dx"/> / <paramref name="dz"/> are the offset **as seen from the
        /// eye** (world XZ). The centre itself (length 0) and broken input return 0.
        /// </summary>
        public static float SideOf(float headingRadians, float dx, float dz)
        {
            if (float.IsNaN(headingRadians) || float.IsNaN(dx) || float.IsNaN(dz)) return 0f;

            float length = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (!(length > 0f)) return 0f;

            float cos = (float)System.Math.Cos(headingRadians);
            float sin = (float)System.Math.Sin(headingRadians);

            // right = (sin φ, -cos φ). Exactly as checked in the class doc.
            float side = (dx * sin - dz * cos) / length;

            if (float.IsNaN(side)) return 0f;
            if (side < -1f) return -1f;
            if (side > 1f) return 1f;
            return side;
        }

        /// <summary>
        /// How far into the dangerous semicircle, [0, 1]. The other side, dead ahead and
        /// dead astern are 0.
        /// If <paramref name="southernHemisphere"/> is true, the left becomes the dangerous
        /// semicircle.
        /// </summary>
        public static float DangerousSideOf(float headingRadians, float dx, float dz,
                                            bool southernHemisphere)
        {
            float side = SideOf(headingRadians, dx, dz);
            if (southernHemisphere) side = -side;
            return side > 0f ? side : 0f;
        }

        /// <summary>
        /// The multiplier on the damage radius, [1, 1 + <see cref="MaxRadiusBoost"/>].
        ///
        /// The way to use it is "stretch the wind field out to the right", i.e.
        /// <c>WindAt(distance / RadiusFactor(...), ...)</c>.
        /// **Do not rewrite the radius itself** — widen the sweep rectangle separately
        /// (without that, the buildings on the outer edge of the stretched side never enter
        /// the sweep in the first place).
        /// </summary>
        public static float RadiusFactor(float headingRadians, float dx, float dz,
                                         bool southernHemisphere)
        {
            return 1f + MaxRadiusBoost
                        * DangerousSideOf(headingRadians, dx, dz, southernHemisphere);
        }

        /// <summary>
        /// The multiplier on the collapse probability, [1, 1 + <see cref="MaxChanceBoost"/>].
        /// </summary>
        public static float ChanceFactor(float headingRadians, float dx, float dz,
                                         bool southernHemisphere)
        {
            return 1f + MaxChanceBoost
                        * DangerousSideOf(headingRadians, dx, dz, southernHemisphere);
        }
    }
}
