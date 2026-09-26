using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Forecast;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The two kinds of map (vanilla's hazard view and this mod's seismic-intensity
    /// overlay) and the numbers each of them gives for the single point under the cursor.
    /// **Main thread only.**
    ///
    /// It was split out of <see cref="EarthquakePanel"/> because that file had grown past
    /// the project's 800-line rule, and **not one character of the content was changed**.
    /// Neither row creation nor assignment to <c>.text</c> appears in this file; both can
    /// only go through <see cref="EarthquakeRows"/>.
    ///
    /// **The two maps paint different quantities.** Vanilla's hazard view uses distance
    /// to the crack **line segment**, quadratic falloff and <c>Rmax = R + 400</c> — and
    /// without <c>Located</c> (i.e. a seismograph) not a single cell is painted (§A-6).
    /// The overlay is a linear ramp from the epicentre and shows up with no seismograph.
    /// Putting them side by side and having the note and the legend name the difference
    /// is the surest way to stop one being mistaken for the other.
    /// </summary>
    internal static class EarthquakeMapRows
    {
        private static UILabel _hazardLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            y += 6f;

            var showButton = (UIButton)p.AddUIComponent(typeof(UIButton));
            showButton.name = FreeSlotFinder.SelfPrefix + "EarthquakeShowOnMap";
            showButton.text = Strings.EarthquakeShowOnMap;
            showButton.tooltip = Strings.EarthquakeTitle;
            showButton.width = 150f;
            showButton.height = 24f;
            showButton.relativePosition = new Vector3(EarthquakeRows.RowLeft, y);
            showButton.normalBgSprite = "ButtonMenu";
            showButton.hoveredBgSprite = "ButtonMenuHovered";
            showButton.pressedBgSprite = "ButtonMenuPressed";
            // ①'s wrapper is left completely untouched. ② just uses it as it is (plan 4.1).
            showButton.eventClick += (c, e) =>
                InfoModeSwitch.ShowHazard(InfoManager.SubInfoMode.EarthquakeHazard);
            y += 30f;

            // This single row shows either the number or the reason it is empty, never
            // both. Keeping it to one label rather than two is deliberate: it makes the
            // state "a reason was written but a number is still sitting next to it"
            // structurally impossible. It takes up three rows' worth of height.
            _hazardLabel = EarthquakeRows.AddPlainRow(p, "Hazard", ref y,
                Strings.EarthquakeSwitchHazardView, 42f);

            // ★ For one point under the cursor, numbers from two different models sit
            //    side by side (whole-feature review M3). The one above is a linear ramp
            //    from the epicentre (R = 2000+20i); this one is vanilla's hazard grid
            //    (distance to the crack **line segment**, quadratic falloff,
            //    Rmax = 2000+20i+400, §A-6). Both are measured values and yet they
            //    disagree, so the screen says why they disagree.

            // ── The seismic-intensity map overlay ───────────────────────
            // It goes **directly below** "Show on map" (vanilla's hazard view) above.
            // The content lives in EarthquakeOverlayRows (the rows themselves are made by
            // EarthquakeRows' helpers).
            y += 6f;
            EarthquakeOverlayRows.Build(p, ref y);
        }

        /// <summary>On level unload. Just drop the references (the objects go with the panel).</summary>
        internal static void Destroy()
        {
            _hazardLabel = null;
            EarthquakeOverlayRows.Destroy();
        }

        /// <summary>
        /// For when the snapshot could not be read. **The legend is not cleared** (how to
        /// read the picture is not a reading). Only the button's caption is brought into
        /// line with reality.
        /// </summary>
        internal static void ShowUnavailable()
        {
            EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
            EarthquakeOverlayRows.Clear();
        }

        internal static void Refresh(EarthquakeSnapshot snapshot, bool hazardViewOn,
                                     bool haveCursor, Vec3 cursor)
        {
            RefreshHazardRow(snapshot, hazardViewOn, haveCursor, cursor);
            // hazardViewOn is passed along so InfoManager is not queried twice in the
            // same frame.
            EarthquakeOverlayRows.Refresh(hazardViewOn);
        }

        /// <summary>
        /// Reading the hazard map. **There is exactly one path by which this method can
        /// print a number, and reaching it means passing all four gates.**
        ///
        ///   1. the earthquake hazard view must be on screen
        ///      (<c>m_hazardAmount</c> is a single grid and holds values for only the one
        ///       sub-mode currently displayed. See <see cref="HazardMapReader"/>'s class doc)
        ///   2. the snapshot must be valid (assert nothing if the counts could not be read)
        ///   3. there must be at least one earthquake satisfying
        ///      <c>Located &amp;&amp; (Emerging|Active)</c>
        ///      (§A-6. **If this is 0, the grid is correctly all zeros**)
        ///   4. the cursor must be over terrain and inside the grid
        ///
        /// Wherever it falls out, it prints **the reason it fell out instead of a
        /// number**. This is the direct fix for the flaw ①'s whole-feature review turned
        /// up (an all-zero grid was being displayed as "Lightning: 0"), and earthquakes
        /// have the same gate, so they need the same treatment.
        /// </summary>
        private static void RefreshHazardRow(EarthquakeSnapshot snapshot, bool hazardViewOn,
                                             bool haveCursor, Vec3 cursor)
        {
            if (!hazardViewOn)
            {
                // Don't fall back on "cannot be read". The cause is known, and it is one
                // click away from being fixed (treated the same way as ①'s
                // ForecastSwitchHazardView).
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeSwitchHazardView);
                return;
            }

            if (CountPaintingQuakes(snapshot.Quakes) <= 0)
            {
                // ★ The most important branch in this whole task. No number at all is shown.
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeNotLocated);
                return;
            }

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            bool ok;
            byte value = HazardMapReader.SampleAt(
                new Vector3(cursor.X, cursor.Y, cursor.Z),
                InfoManager.SubInfoMode.EarthquakeHazard, out ok);
            if (!ok)
            {
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // The label is shared with ① ("Hazard at cursor" does not name a disaster
            // type, and gate 1 above has already confirmed the earthquake hazard view is
            // the one on screen).
            EarthquakeRows.SetLayer1(_hazardLabel, Strings.ForecastAtCursor + ": " + value
                + "  [" + HazardLevel.BarOf(value) + "]");
        }

        /// <summary>
        /// How many earthquakes are painting anything on the hazard map right now (the
        /// two-stage gate of §A-6). The decision is left to
        /// <see cref="DisasterPhases.PaintsHazardMap(bool, EarthquakePhase)"/> — rewrite
        /// the gate here and its definition ends up split across two places.
        /// </summary>
        private static int CountPaintingQuakes(IList<EarthquakeReading> quakes)
        {
            int painting = 0;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (DisasterPhases.PaintsHazardMap(quakes[i].Located, quakes[i].Phase)) painting++;
            }
            return painting;
        }
    }
}
