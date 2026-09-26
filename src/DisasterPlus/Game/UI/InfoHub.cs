using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **One button in the top-left of the screen, and one tab strip that opens beneath it.**
    /// Main thread only.
    /// The "reading side" of ① forecast, ② earthquake, ④ typhoon, ⑤ volcano and the
    /// diagnostics all opens from this one place.
    ///
    /// ── The request ─────────────────────────────────────────
    ///
    /// "Please make the information screens open from a shortcut button in the top left, like
    /// the siren mod and CS:WARFRONT" — the owner.
    ///
    /// ── ★★ There is one button (do not make it four) ────────────────
    ///
    /// This mod used to have a floating button for each of ①②④⑤, and all four asked
    /// <see cref="FreeSlotFinder"/> for a free slot from the same preferred coordinates.
    /// The output_log.txt from the first playtest records the result verbatim —
    /// <c>no free UI slot found after 30 tries</c> four times, and all four piled up at
    /// (8,50). **The search was not wrong; having four things doing the searching was**
    /// (see the <see cref="FreeSlotFinder"/> class doc).
    ///
    /// So here too, <see cref="FreeSlotFinder"/> is called **once only**, and **for one button
    /// only**. The positions of the tab strip and the panels follow from there, relative to it
    /// — there is structurally no path by which two land in the same place.
    /// **Do not add a second search here.**
    ///
    /// ── One thing decides positions ──────────────────────────────
    ///
    /// The ①②④⑤ panels no longer decide their own positions (<c>MoveTo</c>).
    /// This type is what places them directly below the tab strip at the same left edge, and
    /// **only one panel is ever shown at a time**. So "coordinates that avoid each other" are
    /// no longer needed.
    /// Whatever overflows the bottom of the view is nudged vertically by each panel's
    /// <c>ClampToView</c> (that behaviour has not changed).
    ///
    /// ── One close button as well ──────────────────────────────
    ///
    /// The X that each panel used to have was removed. The X at the right end of the tab strip
    /// closes everything.
    ///
    /// ── Do not guess at icons ──────────────────────────────
    ///
    /// **Do not specify a single foreground sprite name.** Sprite names are atlas data and
    /// cannot be read from the assembly, so guessing at a name can give you "an invisible
    /// button" (see the <c>DisasterPanelBar</c> class doc).
    /// They are told apart by <c>text</c> (a short name) and the tooltip.
    ///
    /// ── Session state ────────────────────────────────
    ///
    /// <see cref="Remove"/> throws away the button, the tab strip and the open/closed state.
    /// The next city starts with **one button, no tab strip and no open panel**.
    /// Do not hold Unity objects in an array (a fake-null cannot be repaired through an array).
    /// </summary>
    public static class InfoHub
    {
        private delegate string TextSource();
        private delegate bool Gate();
        private delegate void Command();
        private delegate float FloatSource();

        /// <summary>The retry interval until the button can be placed (in main-thread updates).</summary>
        private const int SearchIntervalFrames = 120;

        /// <summary>The maintenance interval once installed. Refreshing labels, following setting changes.</summary>
        private const int MaintainIntervalFrames = 30;

        /// <summary>How many attempts before giving up. Do not search forever.</summary>
        private const int MaxAttempts = 100;

        private const float ButtonSize = 32f;
        private const float StripHeight = 34f;

        /// <summary>
        /// The **y of the screen's top row** the button goes on. The owner's request
        /// (2026-08-22):
        ///
        /// > The D＋ button sits where it overlaps the left side menu, which gets in the way
        /// > when operating vanilla's side menu. Please have it shown lined up at the same
        /// > height as the CSWARFRONT button and the SIREN Alert button (see each button's
        /// > script).
        ///
        /// This is the value arrived at by actually reading the two scripts:
        ///
        /// | mod | how it places | size | y | centre y |
        /// |---|---|---|---|---|
        /// | CS:WARFRONT (`MilitaryBuildPanel`) | fixed `new Vector3(150f, 10f)` | 36 | 10 | 28 |
        /// | SIREN Alert (`SirenButton`) | sweeps the top row from the left | 44 | 4 | 26 |
        /// | ⑤ this one | sweeps the top row from the left | 32 | **10** | 26 |
        ///
        /// ★ **Do not fix x.** CS:WARFRONT pins it at 150, but that assumes "no other mod is
        ///   there". This one searches for a free slot from the left, like SIREN Alert — it
        ///   avoids whoever got there first, so all three line up even with all of them
        ///   installed at once (<see cref="FreeSlotFinder"/>).
        /// </summary>
        private const float TopRowY = 10f;

        /// <summary>
        /// The lower bound on x for the top-row search. It only starts here **when there is
        /// not a single button already there**.
        ///
        /// ★★ <b>Normally it does not start here.</b> (2026-08-22, the owner's report from the
        ///   game: "the button is still too far to the left"). Searching from the left edge
        ///   alone means **whoever gets placed first takes the leftmost spot**, so if this mod
        ///   happens to be first it sticks to the edge of the screen (which is what happened).
        ///   Use <c>FreeSlotFinder.RightEdgeOfBand</c> to find **the right edge of the run
        ///   already there** and search from the right of that.
        /// </summary>
        private const float TopRowStartX = 8f;

        /// <summary>The separation from the neighbouring button (px). The same value as SIREN Alert's <c>Gap</c>.</summary>
        private const float TopRowGap = 8f;

        /// <summary>
        /// The height of the top-row band (px). Anything overlapping this band counts as a
        /// "neighbour".
        /// Kept in line with SIREN Alert's <c>bandBottom</c>
        /// (TopMargin 4 + Size 44 + Gap 8 = 56) — both their 44 px button and our 32 px button
        /// fall inside this band.
        /// </summary>
        private const float TopRowBandBottom = 56f;

        /// <summary>
        /// ★★ <b>The position is decided once and never moved again.</b> (2026-08-22, report
        /// from the game: "the D＋ button keeps moving when you press it. Please borrow the
        /// mechanism of the CW button and make the behaviour match.")
        ///
        /// ── What was happening ────────────────────────────────
        ///
        /// To handle other mods' buttons appearing late, the position was re-checked at
        /// 60/180/420/900 frames after being placed. But <b>the tab strip (640 wide) falls
        /// inside the top-row band</b> (y = 44, and the band's bottom is 56). So:
        ///
        ///   press → the tab strip opens → the next re-check **counts our own tab strip as a
        ///        "neighbour"** → it escapes to the right of it → press again → escape again
        ///
        /// CS:WARFRONT's button sets <c>new Vector3(150f, 10f)</c> once and never moves it
        /// again. **Match that behaviour.** The drag handle was removed too (they do not have
        /// one either, because "the drag hit area steals the click").
        ///
        /// The possibility of overlapping a mod that appears late remains, but **that beats a
        /// button that wanders.** If the position is not to your liking: as long as this is
        /// not loaded before the other mods, the ordering is stable.
        /// </summary>
        private const bool PlaceOnce = true;

        /// <summary>One step of the top-row search (px). The button width plus the gap.</summary>
        private const float TopRowStepX = ButtonSize + TopRowGap;

        /// <summary>The attempt limit for the top-row search. <see cref="ScreenSlot"/> stops it first at the right edge of the screen.</summary>
        private const int TopRowTries = 64;
        private const float TabHeight = 26f;
        private const float TabGap = 2f;
        private const float StripPad = 4f;
        private const float CloseWidth = 24f;

        /// <summary>The tab strip's default width (when no panel is open).</summary>
        private const float DefaultStripWidth = 640f;

        /// <summary>The description of one tab. **Do not hold displayed strings as values** (they would freeze on a language change).</summary>
        private sealed class Tab
        {
            public readonly string Id;
            public readonly TextSource Label;
            public readonly Gate Wanted;
            public readonly Command Show;
            public readonly Command Hide;
            public readonly FloatSource Width;

            public UIButton Button;

            public Tab(string id, TextSource label, Gate wanted,
                       Command show, Command hide, FloatSource width)
            {
                Id = id;
                Label = label;
                Wanted = wanted;
                Show = show;
                Hide = hide;
                Width = width;
            }
        }

        /// <summary>
        /// **This one list is the only thing that decides the order.** To add one, add a line
        /// here (do not invent coordinates). In numbered order (① forecast, ② earthquake,
        /// ④ typhoon, ⑤ volcano) plus the diagnostics.
        ///
        /// ★ There is no ③ fire whirl tab. ③ only arises on its own, and the state worth
        ///   reading exists only in the diagnostic dump (see the <c>FireWhirlFeature</c>
        ///   class doc).
        /// </summary>
        private static readonly List<Tab> Tabs = new List<Tab>
        {
            new Tab("forecast",
                    delegate { return Strings.ForecastTitle; },
                    delegate { return ModSettings.ForecastEnabled.value; },
                    ForecastPanel.Show, ForecastPanel.Hide,
                    delegate { return ForecastPanel.Width; }),

            new Tab("earthquake",
                    delegate { return Strings.EarthquakeTitle; },
                    delegate { return ModSettings.EarthquakeEnabled.value; },
                    EarthquakePanel.Show, EarthquakePanel.Hide,
                    delegate { return EarthquakePanel.Width; }),

            // ★★ **The ④ typhoon, ⑤ volcano and diagnostics tabs were taken out**
            //    (2026-08-22, the owner's request: "the D＋ button only needs to open the
            //    weather forecast and the earthquake prediction / intensity (waveform) graph.
            //    The rest is superfluous", "please delete the debug (F11) part").
            //
            //    ★ The diagnostic dump itself remains — **it can still be written with the
            //      F11 hotkey**. What was deleted is "one tab", not the means of reading the
            //      state.
            //
            //    ★★ <b>⑤'s [stop] button went with it.</b> The only route left for stopping a
            //      volcano in progress is "switch ⑤ off in the settings".
            //      If there is a preferred home for it, say so (moving it to a tile on the
            //      disaster panel would be the most natural).
        };

        private static UIButton _button;
        private static UIPanel _strip;
        private static UIButton _closeButton;


        /// <summary>Frames since the button was placed. -1 means "not placed yet".</summary>

        /// <summary>The index of the next <see cref="RecheckFrames"/> to re-check at.</summary>


        /// <summary>The <see cref="Tab.Id"/> of the currently selected tab. null means none is selected.</summary>
        private static string _activeId;

        /// <summary>
        /// The id of **the panel actually being shown**. The panels' <c>Show</c> /
        /// <c>Hide</c> / <c>MoveTo</c> only run while this disagrees with
        /// <see cref="_activeId"/>.
        ///
        /// ★ Without this one field, the maintenance pass (every 0.5 s) would call
        ///   <c>Hide()</c> on all four every time. ②'s <c>EarthquakePanel.Hide</c> also runs
        ///   <c>PublishCursor</c> and <c>EarthquakeOverlay.Disable</c>, so it ended up
        ///   **doing work every 0.5 seconds for a panel nobody is looking at**.
        /// </summary>
        private static string _shownId;

        private static bool _open;

        /// <summary>Stood down after an exception. This type does nothing from then on.</summary>
        private static bool _dead;

        /// <summary>The search for a position was abandoned.</summary>
        private static bool _gaveUp;

        private static int _frames;
        private static int _attempts;
        private static Vector2 _origin;
        private static bool _foundFreeSlot;

        /// <summary>The set of tabs last built (rebuilt when the settings change).</summary>
        private static string _builtSet;

        /// <summary>For diagnostics. Whether the button is on screen right now.</summary>
        public static bool IsInstalled { get { return _button != null; } }

        /// <summary>
        /// ★★ **The outlet for the second thing in this mod that floats something on screen.**
        ///
        /// The <see cref="FreeSlotFinder"/> class doc lays down "only one caller". The reason
        /// was demonstrated in the first playtest: **as long as more than one thing does the
        /// searching, they all fall back to the same preferred when none of them can find a
        /// free slot.**
        ///
        /// <c>DisasterPanelBar</c>'s fallback bar (which appears only in an environment where
        /// vanilla's disaster panel simply cannot be found) is the second such thing. So it is
        /// handed **a point to start searching from, below this button** — the search only
        /// ever moves downwards, so it is structurally impossible for the fallback bar to
        /// return this button's position.
        ///
        /// It returns <c>false</c> when "the button has not been placed yet (and it is not
        /// even known whether it can be)", and during that time **the fallback bar should
        /// wait**. After giving up (<see cref="_gaveUp"/>) the button does not exist, so the
        /// fallback bar may search from wherever it likes — <see cref="Abandoned"/> states that.
        /// </summary>
        public static bool TryGetBelowAnchor(out Vector2 point)
        {
            point = Vector2.zero;
            if (_button == null) return false;

            point = new Vector2(_origin.x, _origin.y + ButtonSize + 2f + StripHeight + 8f);
            return true;
        }

        /// <summary>
        /// Whether placing the button has been abandoned permanently. While this is
        /// <c>true</c> the button does not exist on screen, so there is no point waiting even
        /// though <see cref="TryGetBelowAnchor"/> returns <c>false</c>.
        /// </summary>
        public static bool Abandoned { get { return _gaveUp || _dead; } }

        /// <summary>For diagnostics. Where the button is right now, in one line.</summary>
        public static string Placement
        {
            get
            {
                if (_dead) return "disabled after an error (see the log)";
                if (_button == null)
                {
                    return _gaveUp ? "not installed (gave up)" : "not installed yet";
                }
                return "top-left at (" + _origin.x + "," + _origin.y + ") "
                       + (_foundFreeSlot ? "(free slot found)" : "(may overlap other mods)")
                       + (_open ? "; open on the " + (_activeId ?? "?") + " tab" : "; closed");
            }
        }

        /// <summary>
        /// Called every frame from the main thread. The real work is thinned out.
        ///
        /// **Do not emit <c>Log.Warn</c> / <c>Log.Error</c> every time on this path**
        /// (neither has a throttle). A permanent abandonment is closed off to a single
        /// occurrence by a state flag.
        /// </summary>
        public static void Tick()
        {
            if (_dead) return;

            int interval = _button != null ? MaintainIntervalFrames : SearchIntervalFrames;
            if (_frames++ < interval) return;
            _frames = 0;

            try { Maintain(); }
            catch (Exception e)
            {
                _dead = true;
                Log.Error("info hub failed", e);
            }
        }

        /// <summary>On level unload. Makes sure the next city always starts with "one button".</summary>
        public static void Remove()
        {
            DestroyTabButtons();

            if (_strip != null) UnityEngine.Object.Destroy(_strip.gameObject);
            if (_button != null) UnityEngine.Object.Destroy(_button.gameObject);

            _strip = null;
            _closeButton = null;
            _button = null;
            _activeId = null;
            _shownId = null;
            _open = false;
            _dead = false;
            _gaveUp = false;
            _frames = 0;
            _attempts = 0;
            _origin = Vector2.zero;
            _foundFreeSlot = false;
            _builtSet = null;
        }

        // ------------------------------------------------------------------
        // Maintenance
        // ------------------------------------------------------------------

        private static void Maintain()
        {
            if (_button == null)
            {
                if (_gaveUp) return;
                if (!CreateButton()) return;
            }

            // The button's label can change on a language switch.
            string tip = Strings.InfoButtonTooltip;
            if (_button.tooltip != tip) _button.tooltip = tip;

            if (!_open) return;

            string wanted = WantedSet();
            if (_builtSet != wanted)
            {
                RebuildStrip(wanted);
                // If the tab being viewed disappeared because of a setting, move to the first.
                // Never create a state where **it stays open with nothing inside** (just the
                // tab strip left).
                if (FindTab(_activeId) == null) _activeId = FirstWantedId();
            }

            ApplySelection();
        }

        private static bool CreateButton()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                _attempts++;
                if (_attempts > MaxAttempts)
                {
                    _gaveUp = true;
                    Log.Warn("gave up waiting for the UIView; the Disaster + info button was not placed");
                }
                else
                {
                    Log.Diag("infoHub", "no UIView yet (attempt " + _attempts + ")");
                }
                return false;
            }

            // ★★ This single line is the only call to FreeSlotFinder (see the class doc).
            //    The positions of the tab strip and the panels follow from here, relative to it.
            //
            // ★★ **Search horizontally. Do not descend a single step**
            //    (see <see cref="TopRowY"/>).
            //    It used to descend from (8,50), so **it always landed on top of vanilla's
            //    left side menu (a vertical column)** — exactly the owner's report from the
            //    game. Search the top row from left to right and it lines up next to
            //    CS:WARFRONT and SIREN Alert.
            bool foundFree;
            _origin = SearchTopRow(null, out foundFree);
            _foundFreeSlot = foundFree;

            var b = (UIButton)view.AddUIComponent(typeof(UIButton));
            b.name = FreeSlotFinder.SelfPrefix + "InfoButton";
            b.size = new Vector2(ButtonSize, ButtonSize);
            b.relativePosition = new Vector3(_origin.x, _origin.y);
            // ★ Do not specify a foreground sprite (see the class doc). The text always survives.
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.text = Strings.InfoButtonLabel;
            b.textScale = 0.8f;
            b.textHorizontalAlignment = UIHorizontalAlignment.Center;
            b.textVerticalAlignment = UIVerticalAlignment.Middle;
            b.tooltip = Strings.InfoButtonTooltip;
            b.eventClick += OnButtonClick;

            _button = b;
            Log.Info("Disaster + info button installed at (" + _origin.x + "," + _origin.y + ")"
                     + (foundFree ? "" : " (no free slot found; it may overlap another mod)"));
            return true;
        }

        /// <summary>
        /// Searches for a free slot in the top row. **Starts from the right of the run already
        /// there.**
        ///
        /// ★★ This is the answer to "the button is still too far to the left"
        ///   (2026-08-22). Back when it only searched from the left edge, this mod
        ///   **took the left edge of the screen** whenever it got there before the others.
        ///
        /// ★ Pass **our own button** as <paramref name="owner"/> (null on the first call).
        ///   Without it, every re-check would push us along by our own right edge and the
        ///   button would keep escaping to the right.
        /// </summary>
        private static Vector2 SearchTopRow(UIComponent owner, out bool foundFree)
        {
            float right = FreeSlotFinder.ClusterRightEdge(0f, TopRowBandBottom, owner);
            float startX = right > 0f ? right + TopRowGap : TopRowStartX;
            if (startX < TopRowStartX) startX = TopRowStartX;

            return FreeSlotFinder.Find(new Vector2(startX, TopRowY),
                                       new Vector2(ButtonSize, ButtonSize),
                                       TopRowStepX, 0f, TopRowTries, owner, out foundFree);
        }

        private static void OnButtonClick(UIComponent c, UIMouseEventParameter p)
        {
            try
            {
                if (p != null) p.Use();
                if (_open) CloseAll(); else Open();
            }
            catch (Exception e)
            {
                Log.Error("the Disaster + info button click failed", e);
            }
        }

        private static void Open()
        {
            _open = true;
            _builtSet = null;   // always rebuild before the next ApplySelection

            string wanted = WantedSet();
            RebuildStrip(wanted);

            // Select the tab previously viewed if it is still there, otherwise the first.
            if (FindTab(_activeId) == null) _activeId = FirstWantedId();

            ApplySelection();
        }

        /// <summary>Folds away the tab strip and every panel. **Leaves not one of the open ones behind.**</summary>
        private static void CloseAll()
        {
            _open = false;
            for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
            _shownId = null;
            if (_strip != null) _strip.isVisible = false;
        }

        /// <summary>
        /// Shows the panel of the selected tab only, and folds away the rest.
        ///
        /// ★ **<c>Show</c> / <c>Hide</c> on a panel only when the selection changed**
        ///   (see the <see cref="_shownId"/> doc). The tab strip's appearance is brought into
        ///   line every time — because this is where a language change is followed.
        /// </summary>
        private static void ApplySelection()
        {
            if (_strip == null) return;

            Tab active = FindTab(_activeId);

            for (int i = 0; i < Tabs.Count; i++) ApplyTabSprites(Tabs[i], Tabs[i] == active);

            if (active == null)
            {
                if (_shownId != null)
                {
                    for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
                    _shownId = null;
                }
                _strip.isVisible = false;
                return;
            }

            // ★ Match the width to the panel being shown. With only the tab strip wide, it
            //   does not read as one panel (① alone is 380 wide).
            float width = active.Width();
            if (width <= 0f) width = DefaultStripWidth;
            if (_strip.width != width) LayoutStrip(width);

            _strip.isVisible = true;

            if (_shownId == active.Id) return;

            // ★ Bring to front only when switching. Call it on every maintenance pass and it
            //   would go on cutting in front of other mods' UI every 0.5 seconds.
            _strip.BringToFront();

            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i] != active) Tabs[i].Hide();
            }

            // ★ Set the top-left before showing. The other way round it appears at the default
            //   position for one frame and then jumps.
            //   **This single line is the only thing that decides the position.**
            MoveActivePanel(active, new Vector3(_origin.x, _origin.y + ButtonSize + 2f + StripHeight));
            active.Show();
            _shownId = active.Id;

            // ★ Bring it to the front once more after showing. **Only the strip has the
            //   closing X**, so if a panel comes in front of the strip it can no longer be
            //   closed (the panels' <c>ClampToView</c> now keeps them from rising above the
            //   strip, so an overlap is unlikely in the first place, but never fall on the
            //   side of being unable to press it).
            _strip.BringToFront();
        }

        /// <summary>
        /// Sets the top-left of the panel being shown. Call <c>MoveTo</c> before <c>Show</c> —
        /// the other way round it appears at the default position for one frame and then jumps.
        /// </summary>
        private static void MoveActivePanel(Tab tab, Vector3 origin)
        {
            switch (tab.Id)
            {
                case "forecast": ForecastPanel.MoveTo(origin); break;
                case "earthquake": EarthquakePanel.MoveTo(origin); break;
            }
        }

        // ------------------------------------------------------------------
        // The tab strip
        // ------------------------------------------------------------------

        private static void RebuildStrip(string wantedSet)
        {
            HideAllPanels();
            DestroyTabButtons();

            if (_strip == null)
            {
                var view = UIView.GetAView();
                if (view == null) return;

                var strip = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                strip.name = FreeSlotFinder.SelfPrefix + "InfoTabStrip";
                strip.backgroundSprite = "MenuPanel2";
                strip.color = new Color32(255, 255, 255, 240);
                // This type decides the contents completely, so do not leave it to autolayout.
                strip.autoLayout = false;
                strip.height = StripHeight;
                strip.relativePosition = new Vector3(_origin.x, _origin.y + ButtonSize + 2f);
                _strip = strip;

                _closeButton = (UIButton)strip.AddUIComponent(typeof(UIButton));
                _closeButton.name = FreeSlotFinder.SelfPrefix + "InfoCloseButton";
                _closeButton.text = "X";
                _closeButton.height = TabHeight;
                _closeButton.width = CloseWidth;
                _closeButton.normalBgSprite = "ButtonMenu";
                _closeButton.hoveredBgSprite = "ButtonMenuHovered";
                _closeButton.pressedBgSprite = "ButtonMenuPressed";
                _closeButton.eventClick += (c, e) => CloseAll();
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                Tab t = Tabs[i];
                if (!t.Wanted()) continue;

                var b = (UIButton)_strip.AddUIComponent(typeof(UIButton));
                b.name = FreeSlotFinder.SelfPrefix + "InfoTab_" + t.Id;
                b.height = TabHeight;
                b.text = t.Label();
                b.tooltip = t.Label();
                // Japanese headings run wider than English. At the default scale, five tabs
                // can overflow.
                b.textScale = 0.8f;
                b.textHorizontalAlignment = UIHorizontalAlignment.Center;
                b.textVerticalAlignment = UIVerticalAlignment.Middle;

                Tab captured = t;
                b.eventClick += delegate(UIComponent c, UIMouseEventParameter p)
                {
                    if (p != null) p.Use();
                    Select(captured);
                };

                t.Button = b;
            }

            LayoutStrip(_strip.width > 1f ? _strip.width : DefaultStripWidth);
            _builtSet = wantedSet;
        }

        /// <summary>
        /// Divides the width evenly among the tabs. **They are placed in order by a single
        /// loop, so it is structurally impossible for two to land in the same place**
        /// (the same discipline as <c>DisasterPanelBar</c>).
        /// </summary>
        private static void LayoutStrip(float width)
        {
            if (_strip == null) return;

            _strip.width = width;

            int n = 0;
            for (int i = 0; i < Tabs.Count; i++) if (Tabs[i].Button != null) n++;

            if (_closeButton != null)
            {
                _closeButton.relativePosition =
                    new Vector3(width - StripPad - CloseWidth, StripPad);
            }

            if (n <= 0) return;

            float tabsLeft = StripPad;
            float available = width - tabsLeft - StripPad - CloseWidth - StripPad;
            float tabWidth = (available - TabGap * (n - 1)) / n;
            if (tabWidth < 20f) tabWidth = 20f;

            int placed = 0;
            for (int i = 0; i < Tabs.Count; i++)
            {
                UIButton b = Tabs[i].Button;
                if (b == null) continue;

                b.width = tabWidth;
                b.relativePosition = new Vector3(tabsLeft + placed * (tabWidth + TabGap), StripPad);
                placed++;
            }
        }

        private static void ApplyTabSprites(Tab t, bool active)
        {
            if (t.Button == null) return;
            // Limit the sprites used to the three this mod already uses. Add a name whose
            // existence has not been confirmed and the button goes transparent when the name
            // turns out to be wrong.
            t.Button.normalBgSprite = active ? "ButtonMenuPressed" : "ButtonMenu";
            t.Button.hoveredBgSprite = "ButtonMenuHovered";
            t.Button.pressedBgSprite = "ButtonMenuPressed";

            string label = t.Label();
            if (t.Button.text != label)
            {
                t.Button.text = label;
                t.Button.tooltip = label;
            }
        }

        private static void Select(Tab t)
        {
            if (t == null) return;
            _activeId = t.Id;
            ApplySelection();
        }

        /// <summary>
        /// When the tab strip is rebuilt, fold away every panel that was being shown, without
        /// exception.
        /// **Clear <see cref="_shownId"/> alone and forget the folding, and the tab disappears
        /// while the panel stays on screen.**
        /// </summary>
        private static void HideAllPanels()
        {
            for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
            _shownId = null;
        }

        private static void DestroyTabButtons()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                Tab t = Tabs[i];
                // ★ Test != null per element (a destroyed fake-null only shows up on the element).
                if (t.Button != null) UnityEngine.Object.Destroy(t.Button.gameObject);
                t.Button = null;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Expresses the set of tabs currently wanted as a single string (to decide on a rebuild).</summary>
        private static string WantedSet()
        {
            string s = "";
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Wanted()) s += Tabs[i].Id + ",";
            }
            return s;
        }

        private static Tab FindTab(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Id == id) return Tabs[i].Button != null ? Tabs[i] : null;
            }
            return null;
        }

        private static string FirstWantedId()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Wanted() && Tabs[i].Button != null) return Tabs[i].Id;
            }
            return null;
        }
    }
}
