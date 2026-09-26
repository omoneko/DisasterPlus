using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The typhoon panel. **Main thread only.**
    ///
    /// The frame follows ②'s <see cref="EarthquakePanel"/> exactly (the **non-generic**
    /// overload of <c>UIView.AddUIComponent(Type)</c>, the try/catch that leaves no
    /// orphan GameObject if construction throws part-way,
    /// <c>backgroundSprite = "MenuPanel2"</c>, the <see cref="_bodyBuilt"/> flag that
    /// skips building the body without the DLC, and the guard at the top of
    /// <c>Tick()</c> that looks at the setting).
    ///
    /// ── What ④ brings that is new: the provenance heading ────────────────
    ///
    /// ② put <c>[measured]</c> / <c>[Disaster + model]</c> on every row.
    /// **④ does not.** Vanilla has no source material, so nearly all of ④'s numbers are
    /// this mod's own, and if everything has the same provenance then a per-row marker
    /// carries no information (design doc §1.2 / §7-1). Instead it names itself
    /// <b>once, in a heading</b> — those are the two lines
    /// <c>Strings.TyphoonModelHeader</c> and <c>Strings.TyphoonModelNote</c>, and
    /// **they are the two most important lines on this panel**. Whoever adds rows must
    /// not delete them.
    ///
    /// The guarantee itself lives in <see cref="TyphoonRows"/>.
    /// **There is not a single <c>UILabel</c> creation or <c>.text</c> assignment in this
    /// file.**
    ///
    /// ── Raising one starts with "point at a spot" ─────────────────────────
    ///
    /// ★★ Both ④'s tile on the disaster panel and this panel's "raise" button
    ///    <b>arm a placement cursor</b> when pressed
    ///    (<see cref="TyphoonPlacementTool"/>). Same contract as vanilla's disaster
    ///    buttons: the typhoon begins at the point you click on the map.
    ///    <see cref="ArmPlacement"/> is the entry point, and **the tile calls it**.
    ///
    /// The "stop" button alone needs no point, so as before it just queues a request on
    /// <see cref="TyphoonHub.Request"/>.
    /// Neither one **touches <c>DisasterManager</c> from the main thread**. What actually
    /// creates the disaster is <see cref="TyphoonController"/> on the next sim tick.
    /// So there is **a one-tick delay** between pointing and anything changing on screen.
    /// To stop it looking as though pointing twice raised two, "waiting for the first
    /// simulation update" is shown in the meantime
    /// (<see cref="TyphoonStatusRows"/> looks at
    /// <see cref="TyphoonHub.PendingRequest"/>). The sim side only ever creates one at a
    /// time anyway (<c>TyphoonController.Start</c>).
    ///
    /// ── The vertical budget ───────────────────────────────────────
    ///
    /// T6 to T10 add rows to <c>TyphoonEffectRows</c>. The panel height is **decided from
    /// the contents, with no fixed value burnt in** (<see cref="ClampToView"/> warns when
    /// it sticks out past the view).
    /// </summary>
    public static class TyphoonPanel
    {
        /// <summary>What one button does. This is net35, so a hand-written delegate
        /// rather than <c>Action</c>.</summary>
        private const string PanelName = FreeSlotFinder.SelfPrefix + "TyphoonPanel";

        /// <summary>The size of one button. Raise and stop sit side by side.</summary>


        private static UIPanel _panel;
        private static UILabel _titleLabel;

        /// <summary>
        /// Whether the typhoon rows have been built. In an environment without the
        /// Natural Disasters DLC the <c>ThunderStormAI</c> prefab does not exist and a
        /// typhoon is impossible in principle, so we skip the rows and show a single line
        /// giving the reason (the same as ②'s <c>EarthquakePanel._bodyBuilt</c>).
        /// </summary>
        private static bool _bodyBuilt;

        /// <summary>Top-left corner. The default is only used before
        /// <see cref="InfoHub"/> has decided the position.</summary>
        private static Vector3 _origin = new Vector3(1260f, 120f);

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>This panel's width. <see cref="InfoHub"/> reads it to match the tab
        /// strip's width.</summary>
        internal static float Width { get { return TyphoonRows.PanelWidth; } }

        /// <summary>
        /// Set the top-left corner. **<see cref="InfoHub"/> is the one and only thing
        /// that decides positions** (the same discipline as <c>DisasterPanelBar</c>'s
        /// class doc: "as long as more than one thing decides the position, this accident
        /// will keep happening in new forms").
        /// Do not invent coordinates here.
        /// </summary>
        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
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

        /// <summary>Every frame from the main thread. Only updates the contents while
        /// visible.</summary>
        public static void Tick()
        {
            // If the panel is left open when the setting is disabled, OnSimulationTick
            // stops publishing and the panel shows the same stale snapshot for ever — a
            // "frozen but looks alive" panel, and the button that would close it has
            // already been removed, so it cannot be dismissed (①'s review finding; ② has
            // the same guard).
            if (!ModSettings.TyphoonEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>On level unload. **Do not carry over a single piece of session
        /// state.**</summary>
        public static void Destroy()
        {
            // Only drops the references. The objects themselves go with the panel's
            // GameObject.
            TyphoonStatusRows.Destroy();
            TyphoonEffectRows.Destroy();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _bodyBuilt = false;

            // ★ Do not carry a reference to a destroyed camera into the next city.
            CameraJump.Reset();
            CameraFocus.Reset();
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
                Log.Error("typhoon panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; typhoon panel not built");
                return;
            }

            // ★ Do not make the assignment to _panel the last line of construction. If
            //    something part-way through throws, the Destroy() that EnsureBuilt()'s
            //    catch calls sees _panel==null and does nothing, leaving the GameObject
            //    already attached to UIView orphaned (one more piles up on every click;
            //    ①'s review finding).
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("typhoon panel built");
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
            panel.width = TyphoonRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // InfoHub decides the position (MoveTo). All there is here is the default —
            // only one panel is ever on screen at a time, so coordinates that dodge each
            // other are no longer needed.
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = TyphoonRows.AddTitleRow(panel, "Title", 10f, y,
                TyphoonRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ There is no close button here. **The tab strip's X is the only one**
            //    (InfoHub).
            y += 30f;

            // Without the DLC the typhoon itself does not exist. Write the reason instead
            // of the rows.
            _bodyBuilt = ModCompat.NaturalDisastersOwned;
            if (!_bodyBuilt)
            {
                var needsDlc = TyphoonRows.AddRow(panel, "NeedsDlc", ref y, 36f);
                TyphoonRows.SetPlain(needsDlc, Strings.TyphoonNeedsDlc);
                panel.height = y + 8f;
                return;
            }

            // ★★ The two lines that name ④'s display convention. **In place of per-row
            //     markers, this says where everything comes from, once** (class doc /
            //     design doc §7-1).
            // ★★ **The two provenance lines have been taken out** (2026-08-22).
            //    That ④'s numbers are not the game's own measurements is still named by
            //    the [measured] marker on the rainfall and cloud rows, and by the
            //    diagnostics dump.
            //    The marker machinery itself (<c>TyphoonRows</c>) is unchanged, to the
            //    byte.

            AddActionButtons(panel, ref y);

            TyphoonStatusRows.Build(panel, ref y);
            // ★ The rows for each element (lightning, wind damage, flooding, clouds,
            //   tornadoes) go below the state of the typhoon itself. T7 to T10 must be
            //   added inside TyphoonEffectRows (do not change the order here).
            TyphoonEffectRows.Build(panel, ref y);

            panel.height = y + 8f;
            ClampToView(panel);
        }

        /// <summary>
        /// **There is not one button.** This panel is <b>a place to read</b>.
        ///
        /// ★★ **The button that raises one is not here.** The only thing that raises a
        ///    typhoon is ④'s tile on the disaster panel (the same three steps as
        ///    vanilla's disaster buttons) — with two ways to raise one, the contract
        ///    "press, then click the map" would have two entrances.
        ///
        /// ★★ **"Stop the typhoon" has been removed too** (2026-09-02, the owner:
        ///    "Nobody can stop a natural disaster. Delete the very concept of
        ///    stopping.")
        ///
        /// ★★ **"Jump to the storm" has been removed as well.** On the same day it turned
        ///    out that this panel <b>cannot be opened at all</b> — there is no route
        ///    anywhere that calls <c>Show()</c> (not since 2026-08-22, when D+'s tabs were
        ///    narrowed to the two of forecast and earthquake). The jump-to-the-map feature
        ///    moved to <b>the forecast panel</b>.
        ///
        /// ★ The method itself is kept. It is a foothold for when **somewhere to add rows
        ///   is needed**; calling it now does nothing.
        /// </summary>
        private static void AddActionButtons(UIPanel panel, ref float y)
        {
        }

        /// <summary>
        /// Raise the panel until its bottom edge no longer sticks out of the view. Same
        /// as ②'s <c>EarthquakePanel.ClampToView</c>: **the panel grows every time a row
        /// is added**. Do not quietly let the bottom row — a note, or "why nothing is
        /// happening" — slip off screen.
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
                    // ★ Getting here means **the contents are taller than the view**.
                    //    Even pushed to the top edge, the bottom row goes off screen.
                    //    **Do not let it be cut off silently.** This happens once at build
                    //    time, so no throttling is needed.
                    top = Margin;
                    Log.Warn("typhoon panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
                // ★★ **Never go above the position <c>InfoHub</c> specified (i.e. just
                //    below the tab strip).** (2026-08-22; this was the cause of the
                //    in-game report "there is a tab inside the weather and earthquake tabs
                //    that the X will not close".) The two clamps above only ever move the
                //    panel up to fit its bottom edge on screen, so a tall panel **covered
                //    the whole tab strip** — the very means of closing it became
                //    unclickable. Whatever does not fit now sticks out below, and you can
                //    move the lot by grabbing the left end of the strip (<c>InfoHub</c>'s
                //    drag grip).
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("typhoon panel clamp failed: " + e.GetType().Name);
            }
        }

        private static void Refresh()
        {
            TyphoonRows.SetPlain(_titleLabel, Strings.TyphoonTitle);

            // Without the DLC only the one explanatory line was built (the doc on
            // _bodyBuilt).
            if (!_bodyBuilt) return;

            // ★ Take the snapshot once per frame (do not take the lock twice).
            var snapshot = TyphoonHub.Latest;

            TyphoonStatusRows.Refresh(snapshot);
            TyphoonEffectRows.Refresh(snapshot);
        }
    }
}
