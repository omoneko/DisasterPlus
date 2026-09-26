using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The controls and legend for the seismic-intensity overlay. Part of
    /// <see cref="EarthquakePanel"/>, but in its own file **so that file does not grow
    /// any further** (the project's rule is 800 lines).
    ///
    /// ── The layer separation still holds ─────────────────────────────
    ///
    /// This file **never writes** <c>AddUIComponent(typeof(UILabel))</c> or an assignment
    /// to <c>UILabel.text</c>. Rows can only be made through
    /// <see cref="EarthquakeRows.AddPlainRow"/> /
    /// <see cref="EarthquakeRows.AddLayer1Row(UIPanel,string,ref float,float)"/>, and
    /// their contents can only be written through <see cref="EarthquakeRows.SetPlain"/> /
    /// <see cref="EarthquakeRows.SetLayer1"/> (<c>Strings.SourceVanilla</c> appears
    /// nowhere but inside those setters). So the guarantee that "one grep confirms it"
    /// is intact.
    ///
    /// ── Off by default, but not hidden ───────────────────────────────
    ///
    /// The overlay is not on permanently (an earthquake's frames are the heaviest in the
    /// game, and with the map painted over the whole time you cannot see the city at
    /// all). Nor is it a checkbox buried in the options screen — an answer to the very
    /// request "I want to see the distribution" is worthless somewhere nobody will find
    /// it. **It goes in the panel, next to the button for vanilla's hazard view.**
    ///
    /// ── Don't let it be confused with the button beside it ───────────
    ///
    /// "Show on map" just above it is vanilla's info view
    /// (<c>SubInfoMode.EarthquakeHazard</c>), and it paints a **different** shape:
    /// distance to the crack **line segment**, quadratic falloff, <c>Rmax = R + 400</c> —
    /// and without <c>Located</c> (i.e. a seismograph) not a single cell is painted
    /// (§A-6). This overlay, by contrast, is a linear ramp from the epicentre and shows
    /// up with no seismograph at all. There are three layers of defence against the
    /// confusion:
    ///   1. the legend (always present) names the difference between the two
    ///   2. when both are up at once, the status row says so
    ///   3. they use different hues (vanilla's hazard is yellow-to-red, this one is teal)
    /// </summary>
    internal static class EarthquakeOverlayRows
    {
        private static UIButton _button;
        private static UILabel _statusLabel;

        /// <summary>Once, when the panel is built. Advances <paramref name="y"/>.</summary>
        internal static void Build(UIPanel panel, ref float y)
        {
            _button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            _button.name = FreeSlotFinder.SelfPrefix + "EarthquakeOverlayToggle";
            _button.text = Strings.EarthquakeOverlayShow;
            _button.tooltip = Strings.EarthquakeOverlayRow;
            _button.width = EarthquakeRows.RowWidth;
            _button.height = 24f;
            _button.relativePosition = new Vector3(EarthquakeRows.RowLeft, y);
            _button.normalBgSprite = "ButtonMenu";
            _button.hoveredBgSprite = "ButtonMenuHovered";
            _button.pressedBgSprite = "ButtonMenuPressed";
            _button.eventClick += (c, e) => EarthquakeOverlay.Toggle();
            y += 30f;

            // The status declares itself as layer 1, because the quantity it shows is a
            // value measured straight out of vanilla.
            _statusLabel = EarthquakeRows.AddLayer1Row(panel, "OverlayStatus", ref y, 42f);

            // The legend is **always there**. It shows with no earthquake, and it shows
            // when the read failed (it is not "the current reading" but "how to read
            // this picture". Treated like Strings.EarthquakeSensorEffect: written once
            // and never rewritten from anywhere afterwards, so no reference need be
            // kept).
            EarthquakeRows.AddPlainRow(panel, "OverlayLegend", ref y,
                Strings.EarthquakeOverlayLegend, 72f);
        }

        /// <summary>
        /// Every frame. <paramref name="hazardViewOn"/> says whether vanilla's hazard info
        /// view is up; it is passed in because the caller has already worked it out once
        /// (so <c>InfoManager</c> is not queried twice in the same frame).
        /// </summary>
        internal static void Refresh(bool hazardViewOn)
        {
            if (_button == null || _statusLabel == null) return;

            _button.text = EarthquakeOverlay.Enabled
                ? Strings.EarthquakeOverlayHide
                : Strings.EarthquakeOverlayShow;

            if (!EarthquakeOverlay.Registered)
            {
                // We never got hold of the render path at all. Avoid silently showing
                // nothing.
                EarthquakeRows.SetLayer1(_statusLabel,
                    Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayUnavailable);
                return;
            }

            if (!EarthquakeOverlay.Enabled)
            {
                EarthquakeRows.SetLayer1(_statusLabel,
                    Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayOff);
                return;
            }

            string text;
            if (EarthquakeOverlay.DrawnQuakes == 0)
            {
                // ★ Never let 0 be read as "safe". Say why there is nothing to draw
                //    (treated the same way as ①'s ForecastNoStormDetected).
                text = Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayNothingToDraw;
            }
            else
            {
                text = Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayOn
                       + ", " + EarthquakeOverlay.DrawnQuakes + " "
                       + Strings.EarthquakeOverlayQuakes;
            }

            if (EarthquakeOverlay.FaultGeometryMissing)
            {
                text += "  " + Strings.EarthquakeOverlayFaultUnknown;
            }
            if (EarthquakeOverlay.BudgetExhausted)
            {
                text += "  " + Strings.EarthquakeOverlayCapped;
            }
            if (hazardViewOn)
            {
                // The state that causes the most confusion: two pictures up at once.
                text += "  " + Strings.EarthquakeOverlayBothOn;
            }

            EarthquakeRows.SetLayer1(_statusLabel, text);
        }

        /// <summary>
        /// For when the snapshot could not be read. **The legend is not cleared** (how to
        /// read the picture is not a reading). Only the button's caption is brought into
        /// line with reality.
        /// </summary>
        internal static void Clear()
        {
            if (_button != null)
            {
                _button.text = EarthquakeOverlay.Enabled
                    ? Strings.EarthquakeOverlayHide
                    : Strings.EarthquakeOverlayShow;
            }
            EarthquakeRows.SetPlain(_statusLabel, "");
        }

        /// <summary>
        /// On level unload. The panel's <c>GameObject</c> is destroyed along with
        /// everything on it, so dropping the references is all that is needed (called
        /// from <see cref="EarthquakePanel.Destroy"/>).
        /// </summary>
        internal static void Destroy()
        {
            _button = null;
            _statusLabel = null;
        }
    }
}
