using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// A type that **does nothing but assemble the strings** for the layer-2 rows
    /// (long-period ground motion and the time-of-day factor). Main thread only; it holds
    /// no labels and no state (<see cref="EarthquakeLayer2Rows"/> holds those).
    ///
    /// ── Why it is a separate file ───────────────────────────────
    ///
    /// <see cref="EarthquakeLayer2Rows"/> is the type that owns the section headings and
    /// the row layout. Put the text assembly for two features in there as well and it
    /// becomes hard to follow how rows are added and removed (the settings collapse whole
    /// sections). This file **only turns inputs into strings**, so it reads on its own.
    ///
    /// ── Don't mix up where the numbers come from ────────────────────
    ///
    /// Every single string built here is **a quantity this mod invented**. Vanilla uses
    /// building height for neither the shaking nor the damage (§A-7 / §A-3), and there is
    /// nowhere any basis for "damage is worse at night" (design doc §4.3). The caller
    /// must always write these through <c>EarthquakeRows.SetLayer2</c> (which always
    /// prefixes <c>Strings.SourceModel</c>).
    /// </summary>
    internal static class EarthquakeLongPeriodText
    {
        /// <summary>
        /// One row about the building under the cursor. Returns null when there is
        /// nothing to show (the caller then blanks the row).
        ///
        /// **Do not conflate "the height could not be read" with "it is too short to
        /// qualify".** The first is a failed read, the second a conclusion based on a
        /// measurement; put them in the same sentence and a failed read comes out wearing
        /// the face of a conclusion.
        ///
        /// ── Always say <i>when</i> a number applies (layer-2 review I1 / M3) ─────
        ///
        /// The earthquake this row uses is <c>EarthquakeSnapshot.CursorQuakeId</c>, i.e.
        /// whatever <c>QuakeSelection.SelectDamaging</c> picked (<b>Active or
        /// Emerging</b>). The extra damage, on the other hand, only runs for **Active**,
        /// because <c>LongPeriodDamage.Step</c> requires <c>Phase == Active</c>. Print
        /// "extra collapse risk 6.4%" with no qualification and, before the main shock,
        /// **a probability that is being applied to nothing** appears wearing the face of
        /// a settled figure (in-game test item 90 confirms the damage side behaves
        /// correctly, yet the display would contradict it).
        ///
        /// Say so too when the sweep was cut off at its cap. The sweep works outwards
        /// from the epicentre, so even when cut off the area around the epicentre has
        /// been evaluated — but the outer area has not.
        /// </summary>
        internal static string CursorRow(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return null;
            if (!snapshot.CursorBuilding.HasBuilding) return null;

            float height = snapshot.CursorBuildingHeight;
            if (height <= 0f) return Strings.EarthquakeLongPeriodNoHeight;

            var quake = FindQuake(snapshot, snapshot.CursorQuakeId);
            if (quake == null) return null;

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            if (strength < 0) strength = 0;

            float chance = LongPeriodResponse.ExtraCollapseChance(
                height, snapshot.CursorBuilding.Distance, quake.Intensity, strength);

            // ★ The time-of-day factor applies only to the extra damage (Task 11). Apply
            //    the same factor to the display too, or the panel's number and the
            //    probability actually used part company. The order of multiplication is
            //    kept the same as LongPeriodDamage.IsSelected as well.
            chance *= TimeOfDayFactor.Of(snapshot.HourOfDay);

            float resonance = LongPeriodResponse.Resonance(
                LongPeriodResponse.BuildingPeriodFrames(height));

            return Strings.EarthquakeLongPeriod + ": "
                   + Strings.EarthquakeBuildingHeight + " " + height.ToString("F0") + " m"
                   + " / " + Strings.EarthquakeResonance + " " + resonance.ToString("F2")
                   + " / " + Strings.EarthquakeLongPeriodRisk + " "
                   + (chance * 100f).ToString("F1") + "%"
                   + Caveat(snapshot, quake);
        }

        /// <summary>
        /// The caveat that goes alongside the probability. Empty string when there is none.
        ///
        /// **Say "before the main shock" first.** "It has not been applied even once yet"
        /// is a stronger reservation than "the sweep stopped part way", so where both
        /// apply only the former is shown (printing both would not change what the reader
        /// does).
        /// </summary>
        private static string Caveat(EarthquakeSnapshot snapshot, EarthquakeReading quake)
        {
            if (quake.Phase != EarthquakePhase.Active)
            {
                return "   (" + Strings.EarthquakeLongPeriodBeforeShock + ")";
            }
            if (snapshot.LongPeriodCapped)
            {
                return "   (" + Strings.EarthquakeLongPeriodCapped + ")";
            }
            return "";
        }

        /// <summary>
        /// The time-of-day row (Task 11). Shows only <c>HH:MM</c> and the factor.
        ///
        /// **The words "day" and "night" never appear.** <see cref="TimeOfDayFactor.Of"/>
        /// crosses the boundary smoothly over an hour, so there are stretches of time
        /// where it disagrees with <see cref="TimeOfDayFactor.IsNight"/>'s hard boundary
        /// (the game's own <c>hour &lt; 5 || hour &gt; 20</c>). Showing the bare number is
        /// more honest than printing 1.08 next to the word "day".
        /// </summary>
        internal static string TimeRow(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return null;

            float hour = snapshot.HourOfDay;
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return null;

            return Strings.EarthquakeTimeOfDay + ": " + Clock(hour)
                   + "   " + Strings.EarthquakeTimeFactor + " "
                   + TimeOfDayFactor.Of(hour).ToString("F2");
        }

        /// <summary>
        /// The note saying "the day/night cycle is switched off, so the factor is
        /// permanently 1.00". Empty string when it is not shown.
        ///
        /// **Leave this out and the feature silently does nothing** (the final handling
        /// of the trap in §F-1). The clock being pinned at 12.0 is neither a bug nor an
        /// inconsistency in the game but a legitimate player setting, so it is not a FAIL
        /// in <c>Assumptions</c>. It is declared here instead.
        /// </summary>
        internal static string TimeNote(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return "";
            return snapshot.DayNightEnabled ? "" : Strings.EarthquakeNoDayNight;
        }

        /// <summary><c>HH:MM</c>. The hour is folded into [0,24) before formatting.</summary>
        private static string Clock(float hour)
        {
            float h = hour % 24f;
            if (h < 0f) h += 24f;

            int hours = (int)h;
            int minutes = (int)((h - hours) * 60f);
            // Close off the path where rounding gives 60 minutes (never print 23:60).
            if (minutes > 59) minutes = 59;
            if (minutes < 0) minutes = 0;

            return (hours < 10 ? "0" : "") + hours + ":" + (minutes < 10 ? "0" : "") + minutes;
        }

        private static EarthquakeReading FindQuake(EarthquakeSnapshot snapshot, ushort id)
        {
            if (id == 0) return null;

            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].DisasterId == id) return quakes[i];
            }
            return null;
        }
    }
}
