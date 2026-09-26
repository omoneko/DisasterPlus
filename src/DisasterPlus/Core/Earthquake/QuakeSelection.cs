using System.Collections.Generic;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// Decides, in one place, "which earthquake are we talking about".
    ///
    /// Several earthquakes can run at once (IL findings doc §E-1, up to 256). Let the
    /// ranking spread across several places and it breaks in a particular way: **two rows
    /// of the same panel end up pointing at different earthquakes.** And indeed this
    /// selection logic used to exist as three copies, two of which
    /// (<c>EarthquakeReader.SelectDamagingQuake</c> /
    ///  <c>SeismographRecorder.SelectRecordingQuake</c>) were byte-identical duplicates,
    /// while the remaining one (<c>EarthquakePanel.SelectPrimary</c>) also included
    /// <c>Clearing</c> and so reported "collapse factor under the cursor" and "fault band:
    /// inside" for an earthquake that was winding down — even though the
    /// <c>DestroyBuildings</c> call exists **only** in the <c>Active</c> branch (§A-3).
    ///
    /// **One ranking only; the sole difference is which phases are in scope.**
    /// </summary>
    public static class QuakeSelection
    {
        /// <summary>
        /// Picks the one earthquake whose destruction and shaking maths is **about to run,
        /// or is running now**. Scope is <c>Active</c> and <c>Emerging</c> only
        /// (<c>Clearing</c> is not included). Ties go to the greater intensity, and then to
        /// the lower index. Null if there is none.
        ///
        /// <c>Emerging</c> is in scope because the shaking window (§A-7's
        /// <c>e = frame - activation + 128</c>) opens on <c>Emerging|Active</c>, and
        /// because we want to show the margin before the main shock arrives.
        /// </summary>
        public static EarthquakeReading SelectDamaging(IList<EarthquakeReading> quakes)
        {
            if (quakes == null) return null;

            EarthquakeReading best = null;
            int bestRank = 0;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                if (q == null) continue;

                int rank = RankOf(q.Phase);
                if (rank == 0) continue;

                bool better;
                if (best == null) better = true;
                else if (rank != bestRank) better = rank > bestRank;
                else better = q.Intensity > best.Intensity;

                if (!better) continue;
                best = q;
                bestRank = rank;
            }

            return best;
        }

        /// <summary>
        /// Whether the destruction check and the shaking formula run in this phase.
        /// **The display side must not report local factors or fault-band inside/outside
        /// for an earthquake where this is false.**
        /// </summary>
        public static bool RunsDamage(EarthquakePhase phase)
        {
            return RankOf(phase) != 0;
        }

        private static int RankOf(EarthquakePhase phase)
        {
            if (phase == EarthquakePhase.Active) return 2;
            if (phase == EarthquakePhase.Emerging) return 1;
            return 0;
        }
    }
}
