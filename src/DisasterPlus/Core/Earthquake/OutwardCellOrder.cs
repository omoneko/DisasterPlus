namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The ordering used to walk a rectangular grid **outwards from the epicentre's cell**.
    /// Integer arithmetic only, and a **one-way map** rather than a bidirectional one
    /// between an ordinal <c>n</c> and a relative cell <c>(dx, dz)</c> (being able to get
    /// the relative cell from the ordinal is enough).
    ///
    /// ── Why invent an ordering (second-layer review I1) ─────────────────────
    ///
    /// The long-period sweep has a cap on the work it does per pass; when it hits the cap
    /// it stops and resumes where it left off next time. But if the cut-off order is
    /// **row-major**, the first cell it looks at is the rectangle's <c>(minX, minZ)</c>,
    /// i.e. **the corner furthest from the epicentre**. The additional collapse probability
    /// is proportional to <c>(1 - d/range)</c>, so that is a place where the probability is
    /// nearly 0. At intensity 55 (range 6200 m) the rectangle is roughly 194×194 cells, and
    /// if a mature city has three buildings per cell, the 2048-building cap is used up in
    /// three and a half rows. Reaching the epicentre's row (row 97) would take 28 sweeps,
    /// around 7,200 frames, and if <c>m_activeDuration</c> is shorter than that **the
    /// earthquake ends without the neighbourhoods at the epicentre taking any damage at
    /// all**. A few distant tower blocks fall and everything around the epicentre is
    /// untouched.
    ///
    /// So it goes by **rings of Chebyshev distance** instead. Ordinal 0 is the epicentre's
    /// cell, and from there it goes round one ring at a time in increasing order of
    /// <c>max(|dx|,|dz|)</c>. When the cap cuts it off, what gets dropped is **the outside,
    /// where the probability is lowest**, and the area around the epicentre is always
    /// evaluated on the very first sweep.
    ///
    /// It is **Chebyshev distance rather than Euclidean** because the relative cell can then
    /// be derived from the ordinal with integer arithmetic alone. The difference from a
    /// true distance ordering is one ring's worth (at most 64 m of reordering), which is
    /// plenty for the goal of "look at the near ones first", and **the selection itself
    /// does not depend on the order at all** (<c>IsSelected</c> is decided by the
    /// earthquake ID and the building ID alone, mixing in neither the frame nor the cell
    /// order), so the ordering can never change the conclusion.
    ///
    /// ── Anything outside the rectangle is "skipped" ──────────────────────────────
    ///
    /// The rings are squares centred on the epicentre, so they stick out past the clamped
    /// rectangle. Cells that stick out are <b>simply skipped</b> and are not counted
    /// against the per-sweep cap (which is a cell count). A skip is two or three integer
    /// operations, and at worst it is <see cref="OrdinalCount"/> = 290 thousand of them (if
    /// the epicentre is at the edge of the map), but the sweeps are 256 frames apart, so
    /// this is not enough work to warrant measuring. Count them and the cap is eroded by
    /// however many were skipped, giving the unreadable diagnostic "fewer buildings were
    /// actually examined than the cap".
    ///
    /// **④'s typhoon wind-damage sweep uses this ordering too** (<c>Game/Typhoon/TyphoonWind</c>).
    /// Leaving the namespace as Earthquake is deliberate: moving the type would move every
    /// ② test and every already-reviewed doc reference with it. The ordering itself does not
    /// depend on the disaster.
    /// </summary>
    public static class OutwardCellOrder
    {
        /// <summary>
        /// The ring radius needed, seen from the centre cell, to cover every cell in this
        /// rectangle.
        ///
        /// It is correct even when the centre lies outside the rectangle (for any
        /// <c>(x, z)</c> in the rectangle,
        /// <c>|x - cx| ≤ max(|minX - cx|, |maxX - cx|)</c> holds).
        /// </summary>
        public static int RingRadiusFor(int centreX, int centreZ,
                                        int minX, int maxX, int minZ, int maxZ)
        {
            int r = Abs(minX - centreX);
            r = Max(r, Abs(maxX - centreX));
            r = Max(r, Abs(minZ - centreZ));
            r = Max(r, Abs(maxZ - centreZ));
            return r;
        }

        /// <summary>
        /// The total number of ordinals needed to visit everything out to radius
        /// <paramref name="ringRadius"/> once each: <c>(2r+1)²</c>. A negative radius is
        /// treated as 0 and returns 1 (the centre cell alone).
        ///
        /// At the callers' real limits (a grid 270 cells on a side) the maximum is
        /// 541² = 292,681, comfortably inside the range of an <c>int</c>. The overflow is
        /// blocked anyway — run <c>while (ordinal &lt; count)</c> on an overflowed value and
        /// **the sweep does not run over a single cell**, which is the hardest kind of
        /// breakage to spot.
        /// </summary>
        public static int OrdinalCount(int ringRadius)
        {
            if (ringRadius <= 0) return 1;
            if (ringRadius > 23169) return int.MaxValue;   // 46339² < int.MaxValue
            int side = 2 * ringRadius + 1;
            return side * side;
        }

        /// <summary>
        /// The cell, relative to the centre, corresponding to the ordinal
        /// <paramref name="ordinal"/>.
        /// False if the ordinal is negative (in which case <paramref name="dx"/> /
        /// <paramref name="dz"/> are 0).
        ///
        /// In the ordering, ordinal 0 is the centre, and from there each ring
        /// <c>r = 1, 2, ...</c> is walked once round: east side (south→north) → north side
        /// (east→west) → west side (north→south) → south side (west→east).
        /// **The order taken within one ring carries no meaning** — all that matters is
        /// "the outside of a ring always comes after the inside".
        /// </summary>
        public static bool Offset(int ordinal, out int dx, out int dz)
        {
            dx = 0;
            dz = 0;
            if (ordinal < 0) return false;
            if (ordinal == 0) return true;

            // The r satisfying (2r-1)² ≤ ordinal < (2r+1)². Derived with an integer square
            // root (with a double Sqrt there could be environments that come out one off
            // just below (2r+1)²).
            int r = (IntegerSqrt(ordinal) + 1) / 2;
            int inner = 2 * r - 1;
            int k = ordinal - inner * inner;   // 0 .. 8r-1
            int sideLength = 2 * r;
            int side = k / sideLength;
            int t = k - side * sideLength;

            if (side == 0) { dx = r; dz = -r + t; return true; }
            if (side == 1) { dx = r - t; dz = r; return true; }
            if (side == 2) { dx = -r; dz = r - t; return true; }
            dx = -r + t;
            dz = -r;
            return true;
        }

        /// <summary>This relative cell's Chebyshev distance (i.e. the radius of the ring it
        /// belongs to).</summary>
        public static int RingOf(int dx, int dz)
        {
            return Max(Abs(dx), Abs(dz));
        }

        /// <summary>
        /// <c>floor(sqrt(n))</c>. <c>n &lt;= 0</c> gives 0.
        ///
        /// **The initial guess is built from the bit length.** Newton's method with
        /// <c>x₀ = n</c> divides <c>log₂(sqrt(n))</c> times (nine times at n ≈ 290
        /// thousand), whereas <c>x₀ = 2^⌈bits/2⌉</c> settles in about three.
        /// <see cref="Offset"/> comes through here once per cell, including on the path
        /// that skips cells sticking out, so in the worst sweep (epicentre in a corner of
        /// the map, the range covering everything, a map with barely any buildings) it is
        /// called some 130 thousand times in a single sweep.
        /// </summary>
        private static int IntegerSqrt(int n)
        {
            if (n <= 0) return 0;
            if (n < 4) return 1;

            int bits = 0;
            for (int v = n; v != 0; v >>= 1) bits++;

            int x = 1 << ((bits + 1) / 2);   // ≧ sqrt(n)
            int y = (x + n / x) / 2;
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }
            return x;
        }

        private static int Abs(int v)
        {
            return v < 0 ? -v : v;
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
    }
}
