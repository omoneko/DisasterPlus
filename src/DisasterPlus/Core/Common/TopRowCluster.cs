using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Pure arithmetic that decides one thing only: where the **run that continues from the
    /// left** along the top row of the screen ends.
    /// <b>This is Core, so it never touches the engine.</b>
    ///
    /// ── Why it is needed (2026-08-22, got it wrong twice on real hardware) ────────
    ///
    /// The owner's instruction was the same from the start:
    ///
    /// > Line them up at the top left in the order WF button ＞ ! button ＞ D+ button
    ///
    /// The first attempt was "search the top row **from the left edge**" —
    /// **whoever gets there first takes the leftmost slot**, so when this mod was first
    /// it stuck to the left edge of the screen ("still too far left").
    ///
    /// The second attempt was "place it to the right of **the rightmost edge** on the top
    /// row" — CS also has **vanilla UI at the top right** (the settings button and so on),
    /// so the rightmost edge turned out to be that, and **it overlapped the vanilla buttons
    /// at the right edge of the screen**.
    ///
    /// What is correct is <b>"the right edge of the run that continues from the left"</b>.
    /// Walk from the left edge and stop at any gap wider than
    /// <see cref="DefaultMaxGapPixels"/> — there is a screen-width-sized gap before the
    /// vanilla UI at the top right, so the walk is guaranteed to stop there.
    ///
    /// ★ **If there is no run at all, return 0.** The caller then starts from the left edge
    ///   (with no other mods present, the left edge is the right answer).
    /// </summary>
    public static class TopRowCluster
    {
        /// <summary>
        /// A gap wider than this counts as **a different run** (px).
        ///
        /// The convention at the top left is 32-44 px buttons spaced 8 px apart, so the
        /// cut-off is 96 px, which is less than two buttons' worth.
        /// The gap before the vanilla UI at the top right is more than half the screen
        /// width, so the walk reliably stops there.
        /// </summary>
        public const float DefaultMaxGapPixels = 96f;

        /// <summary>
        /// The **right edge** of the run that stays connected going right from
        /// <paramref name="fromX"/> while each gap is at most <paramref name="maxGap"/>.
        /// 0 if nothing connects at all.
        ///
        /// <paramref name="starts"/> / <paramref name="ends"/> are the left and right edges
        /// of the elements in the row, and **the order does not matter** (we pick them out
        /// in here; the caller is not required to sort). Only the first
        /// <paramref name="count"/> entries are used.
        ///
        /// A bad value (NaN, infinity, end &lt;= start) makes us **skip that one entry
        /// only** — one bad entry must not make us give up the whole search.
        /// </summary>
        public static float RightEdge(float[] starts, float[] ends, int count,
                                      float fromX, float maxGap)
        {
            if (starts == null || ends == null) return 0f;
            if (count > starts.Length) count = starts.Length;
            if (count > ends.Length) count = ends.Length;
            if (count <= 0) return 0f;

            if (IsBad(fromX)) fromX = 0f;
            if (IsBad(maxGap) || maxGap < 0f) maxGap = 0f;

            float edge = 0f;
            bool any = false;

            // Keep extending the reach from the edge. Stop once a pass grows nothing.
            // The count is the number of elements on the top row (a few dozen), so O(n²)
            // is quick enough.
            for (int pass = 0; pass < count; pass++)
            {
                bool grew = false;

                for (int i = 0; i < count; i++)
                {
                    float s = starts[i];
                    float e = ends[i];
                    if (IsBad(s) || IsBad(e) || e <= s) continue;

                    // Anything ending left of the right edge has already been swallowed.
                    float reach = any ? edge : fromX;
                    if (e <= reach) continue;

                    // Starting right of the reach (reach + maxGap) means it is still a
                    // different run.
                    if (s > reach + maxGap) continue;

                    edge = e;
                    any = true;
                    grew = true;
                }

                if (!grew) break;
            }

            return any ? edge : 0f;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
