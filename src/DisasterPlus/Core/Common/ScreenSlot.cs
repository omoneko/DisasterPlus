namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Pure arithmetic holding one judgement only: "put it inside the screen". <b>This is
    /// Core, so it touches the engine not at all</b> (it uses neither
    /// <c>UnityEngine.Rect</c> nor <c>Mathf</c>).
    ///
    /// ── Why it was lifted into Core ─────────────────────────────
    ///
    /// <c>Game/UI/FreeSlotFinder</c> only ever wrote "search downwards for a free spot" and
    /// **never once checked whether the candidate was on the screen at all.** The
    /// output_log from a real run still has
    /// <c>Disaster + info button installed at (8,1094)</c> in it —
    /// y = 1094 on a screen 1080 tall. In other words the search walked off the bottom edge
    /// and out of the screen, and then correctly judged that spot "free".
    /// **It was free because it was off the screen.**
    ///
    /// Previously it was broken the other way round (four buttons stacked at the same
    /// coordinates). Overlapping and invisible are <b>both unusable</b>, but at least you
    /// can still click the overlapping one. That ordering of preference lives here, in a
    /// form the tests can reach.
    /// </summary>
    public static class ScreenSlot
    {
        /// <summary>
        /// When a rectangle of width and height <paramref name="size"/> is placed at
        /// <paramref name="pos"/>, does it fit entirely within <c>[0, extent]</c>?
        ///
        /// If <paramref name="extent"/> is 0 or less (i.e. the screen size could not be
        /// read), it <b>returns true</b> — deciding "off screen" from a dimension you could
        /// not read would mean this function alone stops a single button from being placed.
        /// The caller distinguishes "could not read" beforehand with
        /// <see cref="IsUsableExtent"/>.
        /// </summary>
        public static bool FitsWithin(float pos, float size, float extent)
        {
            if (!IsUsableExtent(extent)) return true;
            if (size <= 0f) return pos >= 0f && pos <= extent;
            return pos >= 0f && pos + size <= extent;
        }

        /// <summary>Is this a meaningful value for a screen size (positive, and neither NaN
        /// nor infinite)?</summary>
        public static bool IsUsableExtent(float extent)
        {
            return extent > 0f && !float.IsNaN(extent) && !float.IsInfinity(extent);
        }

        /// <summary>
        /// Rounds <paramref name="pos"/> into <c>[0, extent - size]</c>.
        ///
        /// **When the rectangle is larger than the screen it returns 0** (favouring the top
        /// left) — putting it at a negative position leaves the smallest clickable area.
        /// When <paramref name="extent"/> cannot be read, <paramref name="pos"/> comes back
        /// as it stands.
        /// </summary>
        public static float ClampInto(float pos, float size, float extent)
        {
            if (!IsUsableExtent(extent)) return pos;
            if (float.IsNaN(pos) || float.IsInfinity(pos)) return 0f;

            float last = extent - (size > 0f ? size : 0f);
            if (last <= 0f) return 0f;
            if (pos < 0f) return 0f;
            if (pos > last) return last;
            return pos;
        }

        /// <summary>
        /// Stepping by <c>(stepX, stepY)</c> from <paramref name="startX"/> /
        /// <paramref name="startY"/>, **how many candidates can be examined while still
        /// fitting on the screen on both axes**. This is the two-axis version of
        /// <see cref="CandidatesInside(float,float,float,float,int)"/>, which is the case
        /// <c>stepX = 0</c>.
        ///
        /// ── Why we need to go sideways (2026-08-22, the owner's request) ───────────────
        ///
        /// > The D+ button sits where it overlaps the left side menu, so it gets in the way
        /// > when operating vanilla's side menu. Please make it line up at the same height
        /// > as the CSWARFRONT button and the SIREN Alert button.
        ///
        /// Those two live in **a single horizontal row at the very top of the screen**. A
        /// search that can only go downwards cannot line up in that row — if the first step
        /// is blocked, the next candidate is already a row lower.
        ///
        /// If one of the two axes is 0 or less, the search still advances as long as the
        /// other is positive. **If both are 0 or less there is a single candidate**
        /// (examining the same point over and over is not a search).
        /// </summary>
        public static int CandidatesInside(float startX, float startY,
                                           float sizeX, float sizeY,
                                           float stepX, float stepY,
                                           float extentX, float extentY,
                                           int maxTries)
        {
            if (maxTries <= 0) return 0;
            if (!FitsWithin(startX, sizeX, extentX)) return 0;
            if (!FitsWithin(startY, sizeY, extentY)) return 0;

            bool movesX = stepX > 0f;
            bool movesY = stepY > 0f;
            if (!movesX && !movesY) return 1;

            int alongX = movesX
                ? StepsInside(startX, sizeX, stepX, extentX, maxTries)
                : maxTries;
            int alongY = movesY
                ? StepsInside(startY, sizeY, stepY, extentY, maxTries)
                : maxTries;

            int count = alongX < alongY ? alongX : alongY;
            return count >= maxTries ? maxTries : count;
        }

        /// <summary>
        /// One axis' worth. If <paramref name="extent"/> cannot be read, returns
        /// <paramref name="maxTries"/> (i.e. this axis imposes no limit).
        /// </summary>
        private static int StepsInside(float start, float size, float step,
                                       float extent, int maxTries)
        {
            if (!IsUsableExtent(extent)) return maxTries;

            float last = extent - (size > 0f ? size : 0f);
            float room = last - start;
            if (room < 0f) return 0;

            long steps = (long)(room / step);
            if (steps < 0L) return 0;

            long count = steps + 1L;
            return count >= maxTries ? maxTries : (int)count;
        }

        /// <summary>
        /// Stepping down by <paramref name="stepY"/> from <paramref name="startY"/>,
        /// **how many candidates can be examined while still fitting on the screen**.
        ///
        /// <paramref name="maxTries"/> is the cap, and the result is always in
        /// <c>[0, maxTries]</c>.
        /// A 0 comes back when "even the first candidate does not fit on the screen", and
        /// in that case the search itself is pointless (going further down only takes it
        /// further off).
        ///
        /// If <paramref name="stepY"/> is 0 or less there is only one candidate
        /// (examining the same point over and over is not a search).
        /// </summary>
        public static int CandidatesInside(float startY, float sizeY, float stepY,
                                           float extentY, int maxTries)
        {
            if (maxTries <= 0) return 0;
            if (!FitsWithin(startY, sizeY, extentY)) return 0;
            if (!IsUsableExtent(extentY)) return maxTries;
            if (!(stepY > 0f)) return 1;

            float last = extentY - (sizeY > 0f ? sizeY : 0f);
            // startY has already been confirmed to fit above. How many more steps down is
            // there room for?
            float room = last - startY;
            if (room < 0f) return 0;

            long steps = (long)(room / stepY);
            if (steps < 0L) return 0;

            long count = steps + 1L;
            return count >= maxTries ? maxTries : (int)count;
        }
    }
}
