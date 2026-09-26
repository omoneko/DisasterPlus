using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// The spawn test for fire whirls, based on density.
    /// It looks for points satisfying "N or more buildings burning at once within radius R".
    /// Looking at area alone would add up scattered fires out in the suburbs, so we judge by
    /// density.
    /// </summary>
    public static class FireWhirlDetector
    {
        /// <summary>
        /// The short form that only runs the test. **It exists solely for callers that do
        /// not need the diagnostics (the tests).**
        /// Production callers should use the overload that takes
        /// <see cref="FireWhirlProspect"/> — ③ has no path other than natural spawning, so
        /// in practice there is no path where it is acceptable to throw away "why nothing
        /// spawned".
        /// </summary>
        public static List<FireWhirlCandidate> Detect(
            IList<BurningBuilding> burning,
            FireWhirlConfig config,
            IList<Vec2> existingWhirls)
        {
            FireWhirlProspect ignored;
            return Detect(burning, config, existingWhirls, out ignored);
        }

        /// <summary>
        /// Runs the test and, **in the same single pass**, returns "why nothing spawned"
        /// (see <see cref="FireWhirlProspect"/>'s class doc).
        /// </summary>
        public static List<FireWhirlCandidate> Detect(
            IList<BurningBuilding> burning,
            FireWhirlConfig config,
            IList<Vec2> existingWhirls,
            out FireWhirlProspect prospect)
        {
            var result = new List<FireWhirlCandidate>();

            // The densest cluster. **Here, and only here, we also count clusters that fall
            // short of the threshold** — falling short is exactly "the reason nothing
            // spawned", so we note it down before the test discards it.
            int densest = 0;
            var densestCentre = new Vec2(0f, 0f);
            int suppressed = 0;

            if (burning == null || burning.Count == 0)
            {
                prospect = new FireWhirlProspect(0, 0, densestCentre,
                    config.DetectRadius, config.DetectCount, 0, 0);
                return result;
            }

            int burningTotal = burning.Count;

            // Make the cells the same size as the radius, so a neighbour search only needs
            // 3x3 cells.
            var grid = new GridVote(config.DetectRadius);
            for (int i = 0; i < burning.Count; i++) grid.Add(i, burning[i].Position);

            float r2 = config.DetectRadius * config.DetectRadius;
            var near = new List<int>();
            var raw = new List<FireWhirlCandidate>();

            for (int i = 0; i < burning.Count; i++)
            {
                grid.CollectNear(burning[i].Position, config.DetectRadius, near);

                int count = 0;
                float sx = 0f, sz = 0f;
                for (int k = 0; k < near.Count; k++)
                {
                    var other = burning[near[k]];
                    if (burning[i].Position.DistanceSquaredTo(other.Position) > r2) continue;
                    count++;
                    sx += other.Position.X;
                    sz += other.Position.Z;
                }

                var centre = new Vec2(sx / count, sz / count);

                // ★ Note it down before the test. Move this below the if and the one case
                //   we fail to count is the one that falls short of the threshold — i.e.
                //   exactly when the diagnostics are needed most.
                if (count > densest)
                {
                    densest = count;
                    densestCentre = centre;
                }

                if (count < config.DetectCount) continue;
                raw.Add(new FireWhirlCandidate(centre, count));
            }

            if (raw.Count == 0)
            {
                prospect = new FireWhirlProspect(burningTotal, densest, densestCentre,
                    config.DetectRadius, config.DetectCount, 0, 0);
                return result;
            }

            // Settle them in order of most burning buildings first, discarding candidates
            // that are too close. Ties are broken by index so the result does not depend on
            // the input order (for determinism).
            var order = new int[raw.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            SortByCountDescending(order, raw);

            float sep2 = config.MinSeparation * config.MinSeparation;

            for (int oi = 0; oi < order.Length; oi++)
            {
                var cand = raw[order[oi]];

                bool blocked = false;

                if (existingWhirls != null)
                {
                    for (int e = 0; e < existingWhirls.Count; e++)
                    {
                        if (cand.Center.DistanceSquaredTo(existingWhirls[e]) < sep2) { blocked = true; break; }
                    }
                }

                // ★ Count only those rejected by a live or cooling-down whirl.
                //   Do not count the ones rejected by a candidate already accepted in this
                //   same pass — in that case result is not empty, so it is not "the reason
                //   nothing spawned".
                if (blocked) suppressed++;

                if (!blocked)
                {
                    for (int a = 0; a < result.Count; a++)
                    {
                        if (cand.Center.DistanceSquaredTo(result[a].Center) < sep2) { blocked = true; break; }
                    }
                }

                if (!blocked) result.Add(cand);
            }

            prospect = new FireWhirlProspect(burningTotal, densest, densestCentre,
                config.DetectRadius, config.DetectCount, suppressed, result.Count);
            return result;
        }

        /// <summary>
        /// An insertion sort. The count is bounded by the number of simultaneously burning
        /// buildings, so O(n^2) is enough. List.Sort does not guarantee the order of equal
        /// elements, so we write our own for determinism.
        /// </summary>
        private static void SortByCountDescending(int[] order, List<FireWhirlCandidate> raw)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && IsBefore(key, order[j], raw))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
        }

        private static bool IsBefore(int a, int b, List<FireWhirlCandidate> raw)
        {
            if (raw[a].BurningCount != raw[b].BurningCount)
                return raw[a].BurningCount > raw[b].BurningCount;
            return a < b;   // Ties go by index, so the same input gives the same result.
        }
    }
}
