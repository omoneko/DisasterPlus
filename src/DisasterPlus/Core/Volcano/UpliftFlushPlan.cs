namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// Decides **which rectangle to hand to <c>UpdateArea</c>, and when** (the caller does
    /// the calling).
    ///
    /// ── Why it was carved out as a type ────────────────────────────────
    ///
    /// The real cause behind on-hardware observation ⑤, "make the eruption animation
    /// smoother (at the moment it rises in fits and starts)", was that **one visible step
    /// was not "one tick's worth of rise" but "the rise accumulated until that tile gets
    /// flushed again"**. We were splitting the whole footprint (151×151 cells at the default
    /// R=1200 m) into 4 tiles and flushing one tile per tick round-robin, so it took 4 ticks
    /// before a tile was flushed again, and one visible step was 4 ticks' worth.
    ///
    /// This decision is pure integer arithmetic, so it lives in Core. **Both the unit tests
    /// and <c>tools/VolcanoPreview</c> can run the same thing without launching the game.**
    /// This project's rule — "for a visual change, render and measure it yourself offline
    /// before asking for a test on real hardware" — is what requires this separation.
    ///
    /// ── What it does ──────────────────────────────────
    ///
    ///   1. Takes **the bounding rectangle of the cells actually rewritten** this tick.
    ///   2. Unions it into the rectangle that has not yet been fully flushed (<b>unions</b>;
    ///      it does not overwrite — overwriting would mean that when the rectangle shrinks
    ///      part-way through the round-robin, **tiles that have not been flushed yet
    ///      vanish**).
    ///   3. If it fits in one pass (<see cref="TileSplit.FitsSinglePass"/>), emit it as-is
    ///      and empty the backlog. The first half of the uplift goes through here, so
    ///      **everything that changed reaches the screen every tick**.
    ///   4. If it does not fit, work round the tiles one at a time and empty the backlog
    ///      once a full round is done.
    ///
    /// **Exactly one rectangle is returned per call** (trap 3: from the second one onwards,
    /// the <c>merged &gt; 10000</c> test makes it flush mid-way every time, §A-1 IL_00A8).
    /// The rectangle returned is one where <see cref="TileSplit"/> has respected both
    /// thresholds (each side under 128, area under 10,000), and **the caller must not add
    /// the margin back on.**
    ///
    /// The moment <see cref="HasPending"/> goes false, "everything written has reached the
    /// screen". You can use that directly to decide whether it is safe to carve the crater —
    /// **do not wait by counting tiles**. Count them and you drop the last write that
    /// arrived while you were waiting.
    ///
    /// Not thread-safe. Touch it from the sim thread only.
    /// </summary>
    public class UpliftFlushPlan
    {
        private int _minX, _minZ, _maxX, _maxZ;
        private bool _hasPending;
        private int _tileCount;
        private int _cursor;

        /// <summary>Whether there are rewrites left that have not reached the screen
        /// yet.</summary>
        public bool HasPending { get { return _hasPending; } }

        /// <summary>
        /// How many <c>UpdateArea</c> calls it takes to flush the accumulated rectangle.
        /// **1 means "everything that changed reaches the screen in one tick" = the
        /// smoothest possible state.**
        /// It also returns 1 when there is no backlog ("if something changes next, one call
        /// will do it").
        /// </summary>
        public int TileCount { get { return _hasPending ? _tileCount : 1; } }

        /// <summary>Which tile of the round-robin we are on. 0 while it fits in a single
        /// pass.</summary>
        public int Cursor { get { return _hasPending ? _cursor : 0; } }

        /// <summary>Discards the backlog. **The terrain is not reverted** (only the plan is
        /// folded away).</summary>
        public void Reset()
        {
            _hasPending = false;
            _tileCount = 0;
            _cursor = 0;
            _minX = 0;
            _minZ = 0;
            _maxX = 0;
            _maxZ = 0;
        }

        /// <summary>
        /// Unions in the rectangle rewritten this tick and returns **the rectangle to hand
        /// to <c>UpdateArea</c> this time round**. Returns false when there is nothing to
        /// emit (and then every out parameter is 0).
        ///
        /// A false <paramref name="dirtyValid"/> means "not a single cell changed this
        /// tick". Even then, if a backlog remains we keep flushing — this is the path that
        /// finishes emitting what has not reached the screen yet after the target height has
        /// been met.
        /// </summary>
        public bool Next(bool dirtyValid, int dirtyMinX, int dirtyMinZ, int dirtyMaxX, int dirtyMaxZ,
                         out int passMinX, out int passMinZ, out int passMaxX, out int passMaxZ)
        {
            passMinX = 0;
            passMinZ = 0;
            passMaxX = 0;
            passMaxZ = 0;

            if (dirtyValid && dirtyMinX <= dirtyMaxX && dirtyMinZ <= dirtyMaxZ)
            {
                if (!_hasPending)
                {
                    _hasPending = true;
                    _minX = dirtyMinX;
                    _minZ = dirtyMinZ;
                    _maxX = dirtyMaxX;
                    _maxZ = dirtyMaxZ;
                    _cursor = 0;
                }
                else
                {
                    if (dirtyMinX < _minX) _minX = dirtyMinX;
                    if (dirtyMinZ < _minZ) _minZ = dirtyMinZ;
                    if (dirtyMaxX > _maxX) _maxX = dirtyMaxX;
                    if (dirtyMaxZ > _maxZ) _maxZ = dirtyMaxZ;
                }

                _tileCount = TileSplit.TileCountFor(_minX, _minZ, _maxX, _maxZ);
                if (_cursor >= _tileCount) _cursor = 0;
            }

            if (!_hasPending) return false;

            // ★ Do not split when it fits in one pass. **Never skip the check on the
            //   grounds that it "roughly fits"** — anything that overflows raises no
            //   exception and simply stays un-updated (§A-1).
            if (TileSplit.FitsSinglePass(_minX, _minZ, _maxX, _maxZ))
            {
                bool ok = TileSplit.ExpandForPass(_minX, _minZ, _maxX, _maxZ,
                                                  out passMinX, out passMinZ,
                                                  out passMaxX, out passMaxZ);
                Reset();
                return ok;
            }

            if (!TileSplit.TileAt(_cursor, _minX, _minZ, _maxX, _maxZ,
                                  out passMinX, out passMinZ, out passMaxX, out passMaxZ))
            {
                // The index is out of range. Start again from 0 next tick (we only lose one
                // tick, and we do not discard the backlog — discarding it would leave the
                // un-emitted area behind forever).
                _cursor = 0;
                return false;
            }

            _cursor++;
            if (_cursor >= _tileCount)
            {
                // A full round is done = the whole accumulated area has reached the screen.
                _hasPending = false;
                _tileCount = 0;
                _cursor = 0;
            }
            return true;
        }
    }
}
