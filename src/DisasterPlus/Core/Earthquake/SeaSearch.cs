namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// The order and the step size used when searching for <b>the nearest sea</b> from the
    /// point that was clicked.
    /// **Pure, engine-free functions only.** (Asking whether there is water is the Game
    /// side's job.)
    ///
    /// ── The owner's instruction (2026-08-22) ─────────────────────────────
    ///
    /// &gt; For spawning: click the icon, then have it spawn at the sea nearest to where
    /// &gt; you left-clicked.
    ///
    /// ── Why the ordering lives in Core ──────────────────────────────
    ///
    /// "Search nearest first" is <b>something you cannot confirm by eye</b>. Even with the
    /// order broken, a sea is found somewhere, so <b>all that happens is a slightly more
    /// distant sea gets picked</b>, and nobody can spot it on real hardware. So we pin the
    /// ordering itself down with tests.
    ///
    /// We use <see cref="OutwardCellOrder"/>'s concentric rings (Chebyshev distance). It is
    /// not a strict nearest-neighbour search — the rings are squares, so a diagonal cell is
    /// √2 times further away than an on-axis cell in the same ring. <b>That is fine.</b>
    /// Differences finer than the step (<see cref="StepMetres"/>) are indistinguishable as
    /// "the nearest sea" anyway.
    ///
    /// ★★ <b>Do not return "some sea or other" when nothing was found.</b>
    ///   On an inland map, "there is no sea" is the correct answer.
    ///   The caller should decline and say why.
    /// </summary>
    public static class SeaSearch
    {
        /// <summary>
        /// The distance of one ring (m). Too fine and the search never reaches a distant
        /// sea; too coarse and it steps straight over a narrow inlet.
        /// </summary>
        public const float StepMetres = 96f;

        /// <summary>
        /// The largest ring radius searched. <see cref="StepMetres"/> × this is how far the
        /// search reaches: 96 × 96 = 9,216 m — **a little wider than half the map's side
        /// (8,640 m)** — so wherever on the map you point, if there is a sea the search is
        /// guaranteed to reach it.
        /// </summary>
        public const int MaxRing = 96;

        /// <summary>
        /// The total number of points searched. Callers loop <see cref="At"/> over this.
        /// </summary>
        public static int Count { get { return CountUpTo(MaxRing); } }

        /// <summary>The number of points up to ring <paramref name="ring"/>.</summary>
        public static int CountUpTo(int ring)
        {
            if (ring < 0) return 0;
            int side = ring * 2 + 1;
            return side * side;
        }

        /// <summary>
        /// The offset (m) from the clicked point of the <paramref name="ordinal"/>-th point
        /// searched. <b>Nearest first</b> (the 0th is the clicked point itself).
        ///
        /// Returns false when out of range. **It does not return a plausible-looking 0.**
        /// </summary>
        public static bool At(int ordinal, out float offsetX, out float offsetZ)
        {
            offsetX = 0f;
            offsetZ = 0f;

            int dx, dz;
            if (!OutwardCellOrder.Offset(ordinal, out dx, out dz)) return false;
            if (dx > MaxRing || dx < -MaxRing || dz > MaxRing || dz < -MaxRing) return false;

            offsetX = dx * StepMetres;
            offsetZ = dz * StepMetres;
            return true;
        }

        /// <summary>
        /// How many metres that point is from the clicked point (for diagnostics, and to be
        /// able to say "a further-off sea than you expected was picked").
        /// </summary>
        public static float DistanceMetres(float offsetX, float offsetZ)
        {
            return (float)System.Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
        }
    }
}
