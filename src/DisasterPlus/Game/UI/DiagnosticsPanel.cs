using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The diagnostics tab. **Main thread only.**
    ///
    /// ── Why it goes on screen ────────────────────────────────
    ///
    /// Until now the diagnostic dump could only be produced with <c>Ctrl + hotkey</c>.
    /// How to press it was written only in the prose on the options screen, and
    /// **deleting that prose deletes the way to produce it**. So it becomes a button you can
    /// press, on one of the tabs — reducing the explanation does not mean hiding the feature.
    ///
    /// ── Only four rows are shown ────────────────────────────────
    ///
    /// The state itself is inside the dump file. Pile a summary up here and **the same content
    /// exists in two places and one of them goes stale** (the same reason this mod shares
    /// <c>DiagnosticFormatter</c> between the overlay and the dump).
    /// All that goes here is "how to produce it" and "where it appears".
    /// </summary>
    internal static class DiagnosticsPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "DiagnosticsPanel";

        /// <summary>640, the same as ①②④⑤. <see cref="InfoHub"/> uses it for the tab strip's width.</summary>
        internal const float PanelWidth = 640f;

        private const float RowLeft = 12f;
        private const float RowWidth = PanelWidth - 2f * RowLeft;

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _overlayLabel;
        private static UILabel _hintLabel;
        private static UIButton _dumpButton;

        /// <summary>The top-left. The default is used only before <see cref="InfoHub"/> decides the position.</summary>
        private static Vector3 _origin = new Vector3(8f, 120f);

        internal static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        internal static float Width { get { return PanelWidth; } }

        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
            _panel.relativePosition = _origin;
            ClampToView(_panel);
        }

        internal static void Show()
        {
            EnsureBuilt();
            if (_panel == null) return;
            _panel.Show();
            Refresh();
        }

        internal static void Hide()
        {
            if (_panel != null) _panel.Hide();
        }

        /// <summary>Every frame from the main thread. Refreshes the contents only while displayed.</summary>
        internal static void Tick()
        {
            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>On level unload. **Do not carry over a single piece of session state.**</summary>
        internal static void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);

            _panel = null;
            _titleLabel = null;
            _overlayLabel = null;
            _hintLabel = null;
            _dumpButton = null;
        }

        private static void EnsureBuilt()
        {
            if (_panel != null) return;
            try { Build(); }
            catch (System.Exception e)
            {
                Log.Error("diagnostics panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; diagnostics panel not built");
                return;
            }

            // ★ Do not make the assignment to _panel the last line of construction (the same
            //    reason as the other panels — an exception part-way would leave an orphaned
            //    GameObject behind).
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
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
            panel.width = PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = AddLabel(panel, "Title", y, 24f);
            _titleLabel.textScale = 1.1f;
            y += 30f;

            _overlayLabel = AddLabel(panel, "Overlay", y, 20f);
            y += 26f;

            _dumpButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            _dumpButton.name = FreeSlotFinder.SelfPrefix + "DiagnosticsDumpButton";
            _dumpButton.text = Strings.DiagnosticsDumpButton;
            _dumpButton.width = 260f;
            _dumpButton.height = 28f;
            _dumpButton.relativePosition = new Vector3(RowLeft, y);
            _dumpButton.normalBgSprite = "ButtonMenu";
            _dumpButton.hoveredBgSprite = "ButtonMenuHovered";
            _dumpButton.pressedBgSprite = "ButtonMenuPressed";
            // ★ Do not write the file here. Three stages: main requests, sim assembles, main
            //   writes (see the DiagnosticDump class doc).
            _dumpButton.eventClick += (c, e) => DiagnosticDump.RequestDump();
            y += 34f;

            _hintLabel = AddLabel(panel, "Hint", y, 36f);
            _hintLabel.wordWrap = true;
            y += 42f;

            panel.height = y;
            ClampToView(panel);
        }

        private static void Refresh()
        {
            SetText(_titleLabel, Strings.DiagnosticsTitle);
            SetText(_overlayLabel, Strings.DiagnosticsOverlayRow + ": "
                + (ModSettings.OverlayEnabled.value ? Strings.DiagnosticsOn : Strings.DiagnosticsOff)
                + "    " + Strings.DiagnosticsHotkeyRow + ": "
                + ((KeyCode)ModSettings.OverlayHotkey.value));
            SetText(_hintLabel, Strings.DiagnosticsDumpHint + "  (" + DiagnosticDump.FileName + ")");

            if (_dumpButton != null && _dumpButton.text != Strings.DiagnosticsDumpButton)
            {
                _dumpButton.text = Strings.DiagnosticsDumpButton;
            }
        }

        private static UILabel AddLabel(UIPanel parent, string suffix, float y, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Diagnostics" + suffix;
            label.relativePosition = new Vector3(RowLeft, y);
            label.width = RowWidth;
            label.height = height;
            label.autoSize = false;
            return label;
        }

        private static void SetText(UILabel label, string text)
        {
            if (label == null) return;
            string value = text == null ? "" : text;
            if (label.text != value) label.text = value;
        }

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
                if (top + panel.height > viewHeight - Margin) top = viewHeight - Margin - panel.height;
                if (top < Margin) top = Margin;
                // ★★ **Never rise above the position <c>InfoHub</c> specified (i.e. directly
                //    below the tab strip).**
                //    (2026-08-22; this was the real cause of the report from the game, "inside
                //    the weather tab and the earthquake tab there's a tab you can't close with
                //    the X".) The two nudges above raise the panel purely to fit its bottom
                //    edge on screen, so a tall panel **covered the tab strip entirely** — and
                //    the very means of closing it became unpressable. Whatever does not fit
                //    now overflows downwards, but grabbing the strip's left edge moves it
                //    along with it (<c>InfoHub</c>'s drag grip).
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("diagnostics panel clamp failed: " + e.GetType().Name);
            }
        }
    }
}
