using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The volcano panel. **Main thread only.**
    ///
    /// The framework follows ②'s <c>EarthquakePanel</c> and ④'s <see cref="TyphoonPanel"/>
    /// exactly (the **non-generic** overload of <c>UIView.AddUIComponent(Type)</c>, the try/catch
    /// that leaves no orphan GameObject when construction throws partway,
    /// <c>backgroundSprite = "MenuPanel2"</c>, <see cref="_bodyBuilt"/> for not building the body,
    /// and the guard that checks the setting at the top of <c>Tick()</c>).
    ///
    /// ── ★★ the provenance heading and the permanent irreversibility warning were removed (2026-08-22) ──
    ///
    /// Following the owner's request, "the detailed volcano explanations and debug info do not
    /// need showing in-game inside the D+ tab". Instead:
    ///
    ///   - provenance … the <c>[measured]</c> marker on the affected-range row (<b>the marker
    ///     machinery has not changed by a single byte</b>), plus the diagnostic dump and the
    ///     options screen
    ///   - irreversibility … the heading on the options screen
    ///     (<c>Strings.VolcanoIrreversibleWarning</c>) and the diagnostic dump
    ///
    /// **The discipline of not putting a marker on every row is still in force.**
    ///
    /// The substance of the guarantee lives in <see cref="VolcanoRows"/>.
    /// **There is not one <c>UILabel</c> construction or <c>.text</c> assignment in this file.**
    ///
    /// ── the condition for not building the body is precisely "can it write terrain" ────────
    ///
    /// ④ decided whether to build the body from whether the DLC was owned. ⑤ does not need the
    /// DLC (design doc §1.4).
    /// The only condition under which ⑤ cannot build is <b>the terrain write path not
    /// resolving</b>, and its predicate is <see cref="VolcanoTerrainFacts.Usable"/> —
    /// that is, <b>the same expression ⑤'s feature itself gates on</b>.
    /// Use "did the field resolve" as the predicate and in an environment where the value is
    /// unusable the panel alone appears to work (②'s and ④'s reviews found the same defect).
    ///
    /// ── the panel's position (stating explicitly where it overlaps ①, ② and ④) ───────────
    ///
    /// UIView's coordinate system is normalised to a height of 1080, so at 16:9 the width is
    /// about 1920. ① is at (200,150) with width 380, ② at (600,150) with width 640 and ④ at
    /// (1260,120) with width 640, so **there is no longer room to put a 640-wide panel
    /// alongside.** ⑤ goes at (620, 60):
    ///
    ///   - it does not overlap ① (200–580)
    ///   - it does not overlap ④ (1260–1900) either (its right edge just meets 1260)
    ///   - it does overlap ② (600–1240 / y 150 onwards) horizontally, but **its top 90 px sit
    ///     above ②**, so ⑤'s heading and close button can always be clicked even with ② open
    ///
    /// **Do not write the lie "it does not overlap".** The only thing it actually overlaps is ②,
    /// and the way it overlaps is written down too. <see cref="ClampToView"/> only nudges it
    /// vertically.
    /// </summary>
    public static class VolcanoPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "VolcanoPanel";

        /// <summary>
        /// The panel's top-left. **Only <see cref="InfoHub"/> decides it**; what is here is the
        /// default from before the position is decided
        /// (the same discipline as the class doc of <c>DisasterPanelBar</c>).
        /// <see cref="ClampToView"/> only nudges it vertically.
        /// </summary>
        private static Vector3 _origin = new Vector3(620f, 60f);

        private static UIPanel _panel;
        private static UILabel _titleLabel;

        /// <summary>The height <see cref="ApplyHeight"/> last set (<c>-1</c> = not set yet).</summary>
        private static float _appliedHeight = -1f;

        /// <summary>
        /// Whether the volcano rows were built. In an environment where the terrain write path
        /// does not resolve, ⑤ cannot raise the ground by a single metre, so the rows are not
        /// built and only one line of explanation is shown
        /// (the same as ②'s <c>EarthquakePanel._bodyBuilt</c> and ④'s
        /// <c>TyphoonPanel._bodyBuilt</c>).
        /// </summary>
        private static bool _bodyBuilt;

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>This panel's width. <see cref="InfoHub"/> reads it to match the tab bar's width.</summary>
        internal static float Width { get { return VolcanoRows.PanelWidth; } }

        /// <summary>
        /// Set the top-left. **There is exactly one authority on the position,
        /// <see cref="InfoHub"/>.** Do not invent coordinates here.
        /// </summary>
        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
            // ★ Go through the same path as re-applying the height (return to the default
            //   top-left, then nudge again).
            _panel.relativePosition = _origin;
            ClampToView(_panel);
        }

        public static void Show()
        {
            EnsureBuilt();
            if (_panel == null) return;
            _panel.Show();
            Refresh();
        }

        public static void Hide()
        {
            if (_panel != null) _panel.Hide();
        }

        /// <summary>Every frame from the main thread. Updates the content only while it is shown.</summary>
        public static void Tick()
        {
            // If the panel were left open when the feature is disabled in the settings, it would
            // show for ever the stale snapshot that OnSimulationTick stopped publishing — a panel
            // that is frozen but looks alive — and since the close button has already been
            // removed, there would be no way to get rid of it
            // (①'s review point. ② and ④ carry the same guard).
            if (!ModSettings.VolcanoEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>On level unload. **Do not carry over a single piece of session state.**</summary>
        public static void Destroy()
        {
            // Just drop the references. The objects themselves go with the panel's GameObject.
            VolcanoStatusRows.Destroy();
            VolcanoEffectRows.Destroy();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _bodyBuilt = false;
            _appliedHeight = -1f;
        }

        private static void EnsureBuilt()
        {
            if (_panel != null) return;
            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                Log.Error("volcano panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; volcano panel not built");
                return;
            }

            // ★ Do not make the assignment to _panel the last line of construction. If something
            //    throws partway, the Destroy() that EnsureBuilt()'s catch calls sees _panel==null
            //    and does nothing, leaving the GameObject already attached to UIView orphaned
            //    (one more piles up on every click. ①'s review point).
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("volcano panel built");
            }
            catch
            {
                if (panel != null) Object.Destroy(panel.gameObject);
                throw;
            }
        }

        private static void BuildContents(UIPanel panel)
        {
            panel.name = PanelName;
            panel.width = VolcanoRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // The position is decided by InfoHub (MoveTo). All that is here is the default —
            // only one panel is shown at a time, so coordinates that dodge each other are no
            // longer needed.
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = VolcanoRows.AddTitleRow(panel, "Title", 10f, y,
                VolcanoRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ There is no close button here. **The X on the tab bar is the only one** (InfoHub).
            y += 30f;

            // ★ The predicate is the same expression ⑤'s feature itself gates on (class doc).
            //   VolcanoReader.ScanTerrainFacts is a pure probe that touches no cache, so it may be
            //   called from the main thread (its own class doc).
            _bodyBuilt = VolcanoReader.ScanTerrainFacts().Usable;
            if (!_bodyBuilt)
            {
                var unavailable = VolcanoRows.AddRow(panel, "TerrainUnavailable", ref y, 56f);
                VolcanoRows.SetPlain(unavailable, Strings.VolcanoTerrainUnavailable);
                ApplyHeight(panel, y + 8f);
                return;
            }

            // ★★ **The two rows stating provenance were removed as well** (2026-08-22).
            //    That ⑤'s numbers are not measurements from the game is still stated by the
            //    [measured] marker on the affected-range row, and by the diagnostic dump and the
            //    options screen.
            //    The marker machinery itself (<c>VolcanoRows</c>) has not changed by a single byte.

            // ★★ **There is no button here for placing a volcano.** The only place it sits is the
            //    ⑤ tile on the disaster panel (the same three steps as a vanilla disaster button).
            //    This panel is <b>a place to read</b>, not a place to trigger things.
            VolcanoStatusRows.Build(panel, ref y);

            // ★ The confirmation set no longer exists anywhere (removed on 2026-08-21).
            //   The rows for each in-progress stage are at the bottom, and when they are not shown
            //   the panel is shrunk to just above them so no blank space is left.
            VolcanoEffectRows.Build(panel, ref y);

            ApplyHeight(panel, VolcanoEffectRows.BlockTop + 8f);
        }

        /// <summary>
        /// Set the height again and nudge it back to a position that fits in the view.
        /// **Always return to the default top-left before nudging** — nudge from the result of the
        /// last nudge and it creeps upwards every time the panel is opened and closed.
        /// </summary>
        private static void ApplyHeight(UIPanel panel, float height)
        {
            if (panel == null) return;

            // ★ Compare against **the value we last set ourselves**. Read <c>panel.height</c> back
            //   and compare and, if the UI rounded it, every frame is judged "different" and
            //   ClampToView runs every frame — and that has a Log.Warn in it, so in an environment
            //   where the content is taller than the view it would emit **one line per frame**
            //   (Log.Warn is not throttled).
            if (_appliedHeight == height) return;
            _appliedHeight = height;

            panel.height = height;
            panel.relativePosition = _origin;
            ClampToView(panel);
        }

        /// <summary>
        /// Raise the panel until its bottom edge no longer runs off the view. The same as the
        /// method of this name in ② and ④: **the panel grows every time a row is added**.
        /// Do not silently allow the bottom row — a warning or a confirmation button — to slip off
        /// the screen.
        /// </summary>
        private static void ClampToView(UIPanel panel)
        {
            try
            {
                var view = panel.GetUIView();
                float viewHeight = view != null ? view.fixedHeight : 0f;
                if (viewHeight <= 0f) return;

                const float Margin = 8f;
                var pos = panel.relativePosition;
                float top = pos.y;
                if (top + panel.height > viewHeight - Margin)
                {
                    top = viewHeight - Margin - panel.height;
                }
                if (top < Margin)
                {
                    // ★ Getting here means **the content is taller than the view**. Even pushed to
                    //    the top edge, the bottom row runs off the screen. **Do not let it be cut
                    //    off silently.** This happens once at build time, so no throttling is
                    //    needed.
                    top = Margin;
                    Log.Warn("volcano panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
                // ★★ **Never push it above the position <c>InfoHub</c> specified (= just below
                //    the tab bar).** (2026-08-22, the live report "there are tabs inside the
                //    weather and earthquake tabs that cannot be closed with the X" — this was it.)
                //    The two nudges above raise the panel purely to keep its bottom edge on
                //    screen, so a tall panel **was covering the whole tab bar** — the means of
                //    closing it became unclickable. Whatever does not fit now runs off the bottom,
                //    but grabbing the left end of the bar moves it all together
                //    (<c>InfoHub</c>'s drag grip).
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("volcano panel clamp failed: " + e.GetType().Name);
            }
        }

        private static void Refresh()
        {
            VolcanoRows.SetPlain(_titleLabel, Strings.VolcanoTitle);

            // In an environment where terrain cannot be written, only the one explanatory row was
            // built (the doc of _bodyBuilt).
            if (!_bodyBuilt) return;

            // ★ Take the snapshot only once per frame (do not take the lock twice).
            var snapshot = VolcanoHub.Latest;
            VolcanoStatusRows.Refresh(snapshot);
            VolcanoEffectRows.Refresh(snapshot);

            // When the in-progress rows are not shown, shrink the panel by that much.
            float bottom = VolcanoEffectRows.IsShowing
                ? VolcanoEffectRows.BlockBottom
                : VolcanoEffectRows.BlockTop;

            ApplyHeight(_panel, bottom + 8f);
        }
    }
}
