using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Forecast;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The weather forecast panel. Main thread only.
    ///
    /// **What vanilla's hazard map actually is (the full review overturned our premise):**
    /// the design document's §1.3 originally described it as "a static risk derived from
    /// the terrain, the buildings and the services", and that was wrong. Reading the IL
    /// established that UpdateHazardMap on both ThunderStormAI and TornadoAI opens with a
    /// two-stage gate on Located(4096) and Emerging|Active(12), never looks at the terrain
    /// or the buildings at all, and only paints a disc around m_targetPosition if it gets
    /// through. So this map is "**where the storms that have been located and are under
    /// way are going to strike next**", and for thunderstorms and tornadoes the only thing
    /// that can set that Located flag is the weather radar. With no such storm every cell
    /// is 0, so in that case we show no number at all (see the doc on
    /// RefreshCursorHazard). Explaining that gate is in fact the real answer to the user's
    /// complaint that "the idea of a forecast is hard to get a feel for".
    ///
    /// We follow the layout in §4 of the design document, but **with the §4.2 correction
    /// applied**: hazard values live only in a single shared grid, m_hazardAmount, which
    /// always holds exactly one submode's worth of data — the one on display (see the
    /// class doc on HazardMapReader). So "the hazard under the cursor" never shows
    /// lightning and tornado at the same time. We look at which submode is on display and
    /// show only the figure for that label. If no hazard view is up, we show
    /// ForecastSwitchHazardView (a hint that names the cause) instead of a number — which
    /// is kept separate from the case where we merely cannot work out the cursor position,
    /// ForecastUnavailable (see the comments in RefreshCursorHazard; the review found we
    /// were reusing ForecastUnavailable for both and it was fixed). Never, ever show a
    /// figure under the label of a type that is not on display.
    ///
    /// For the same reason, the lightning and tornado heading lines carry no "trend" or
    /// "level" figure. DisasterManager.m_randomDisastersProbability /
    /// m_randomDisasterCooldown are single city-wide values that do not depend on the
    /// disaster type, and printing the same value under each of the lightning and tornado
    /// headings would read as "two separately measured values that happen to agree" — the
    /// same class of misreading as the confidently wrong numbers this task warns against.
    /// So the probability goes in a position that belongs to neither heading, shown once,
    /// with a ForecastProbability label — a review finding: a bare unlabelled number gets
    /// read from its context as "chance of rain" or similar, and that was judged to be the
    /// same defect as mislabelling a hazard figure, arrived at by forgetting a label. When
    /// DisasterManager is absent and we could not read it, we do not print a fabricated
    /// 0.0%; the line is left empty (see WeatherSnapshot.DisasterInfoAvailable).
    ///
    /// We never assert "it arrives in N hours" (design document 4.1). All we show is the
    /// trend (Strings.TrendRising/Falling/Steady — words, not arrow glyphs, for the same
    /// reason as HazardLevel's bar: nothing guarantees CS's UI font has an arrow glyph)
    /// and the relative high or low (the probability as a percentage). Whether we are in a
    /// cooldown is stated only in WriteDiagnostics (developer-facing text, not localised) —
    /// once again we have not reserved an extra localisation key for the "in cooldown"
    /// state, so it is not put into words on the user-facing panel.
    ///
    /// Measured against the real API (Task 5, ColossalManaged.dll checked with
    /// docs/tools/ilload.ps1): UIPanel / UILabel / UIButton inherit
    /// width/height/relativePosition/isVisible/Show()/Hide() from UIComponent as they are.
    /// Only the non-generic UIView.AddUIComponent(Type) actually exists, so here too we
    /// cast the result (the same as where DisasterPanelBar falls back to). How
    /// backgroundSprite actually looks — whether "MenuPanel2" exists, whether it draws the
    /// way we intend — cannot be confirmed by reflecting over the assets. Only the game
    /// itself can tell you (see the check we added to docs/playtest-checklist.md).
    /// </summary>
    public static class ForecastPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "ForecastPanel";

        private const float PanelWidth = 380f;
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// How often the ray at the cursor is actually cast again (in rendered frames).
        ///
        /// <see cref="TryPickCursorGround"/> samples the height up to
        /// <c>MaxRayDistance / 16m</c> = **500 times** until it crosses the terrain, plus
        /// 20 more for the bisection if it hits. **The most expensive case is a miss** (a
        /// ray that grazes the horizon), and that happens routinely while the view is
        /// being moved.
        ///
        /// Feature ① deliberately left this cost unoptimised, on the grounds that it only
        /// runs while the hazard info view is open. Once feature ②'s <c>EarthquakePanel</c>
        /// held the same computation to once every 4 frames, this became the only
        /// unbounded sampling path left in the mod, so it is brought into **the same
        /// shape**.
        ///
        /// **The values shown do not change** (it is the same computation, just presented
        /// up to 3 frames late — under 50 ms at 60 fps).
        ///
        /// Set it to 1 and it casts every frame (i.e. this correction is switched off).
        /// Make it larger and the cursor visibly lags behind. Keep it in step with ②.
        /// </summary>
        private const int RepickIntervalFrames = 4;

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _temperatureLabel;
        private static UILabel _rainLabel;
        private static UILabel _cloudLabel;
        private static UILabel _fogLabel;
        private static UILabel _windLabel;
        private static UILabel _probabilityLabel;
        private static UILabel _ndrNoteLabel;
        private static UILabel _lightningLabel;
        private static UILabel _tornadoLabel;
        private static UILabel _cursorHeaderLabel;
        private static UILabel _cursorValueLabel;

        // ── The weather to come (unlocked by the weather radar) ─────────────────
        private static UILabel _comingHeaderLabel;
        private static UILabel _comingBodyLabel;

        // ── The three toggles that put the typhoon on the map ───────────────────
        private static UILabel _typhoonHeaderLabel;
        private static UIButton _trackButton;
        private static UIButton _galeButton;
        private static UIButton _windButton;
        private static UIButton _gotoButton;

        /// <summary>
        /// Whether the hazard-related rows (the lightning and tornado headings, the "show
        /// on map" buttons, the figure under the cursor) were built. On a setup without
        /// the Natural Disasters DLC we do not build them and print a single line giving
        /// the reason instead (<see cref="Strings.ForecastHazardNeedsDlc"/>).
        ///
        /// Why this is needed (full review finding I2): without the DLC neither the
        /// DisasterInfo prefabs for thunderstorms and tornadoes nor the weather radar
        /// exist, so "show on map" would forever just switch to an empty view and the
        /// figure under the cursor would forever be 0. Worse, every Assumptions check
        /// still passes — the AI **types** ship inside Assembly-CSharp whether or not you
        /// own the DLC, so the startup log gives no hint either. The weather and trend
        /// rows work correctly without the DLC, so they stay.
        /// </summary>
        private static bool _hazardRowsBuilt;

        /// <summary>
        /// The top-left corner. The default is only used while <see cref="InfoHub"/> has
        /// not yet decided the position.
        /// </summary>
        private static Vector3 _origin = new Vector3(200f, 150f);

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>
        /// This panel's width. <see cref="InfoHub"/> reads it to match the width of the
        /// tab strip.
        /// </summary>
        internal static float Width { get { return PanelWidth; } }

        /// <summary>
        /// Sets the top-left corner. **There is exactly one thing that decides position:
        /// <see cref="InfoHub"/>** (the same discipline as the class doc on
        /// <c>DisasterPanelBar</c>: "as long as more than one thing decides position, this
        /// accident will keep happening in new forms").
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

        /// <summary>Every frame, from the main thread. Only updates while visible.</summary>
        public static void Tick()
        {
            // Review finding: if the panel is left open when the setting is turned off,
            // it goes on forever showing the stale snapshot from before OnSimulationTick
            // stopped publishing — a panel that is frozen but looks alive — and the button
            // that would close it has already been removed from the strip, so it cannot be
            // dismissed. Put the same guard here as on the button side (DisasterPanelBar
            // checks ForecastEnabled and drops it from the strip).
            if (!ModSettings.ForecastEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>On level unload.</summary>
        public static void Destroy()
        {
            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _comingHeaderLabel = null;
            _comingBodyLabel = null;
            _typhoonHeaderLabel = null;
            _trackButton = null;
            _galeButton = null;
            _windButton = null;
            _gotoButton = null;
            _temperatureLabel = null;
            _rainLabel = null;
            _cloudLabel = null;
            _fogLabel = null;
            _windLabel = null;
            _probabilityLabel = null;
            _ndrNoteLabel = null;
            _lightningLabel = null;
            _tornadoLabel = null;
            _cursorHeaderLabel = null;
            _cursorValueLabel = null;
            _hazardRowsBuilt = false;
            // Make sure the next city never returns the previous city's cursor point,
            // not even once (the same as ②).
            _pickCached = false;
            _pickFrame = 0;
            _pickOk = false;
            _pickHit = new Vec3(0f, 0f, 0f);
            // Camera.main would be looked up again next time anyway via fake-null, but
            // this explicitly upholds this project's rule of never carrying a stale
            // reference across cities.
            _mainCameraCache = null;
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
                Log.Error("forecast panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; forecast panel not built");
                return;
            }

            // Review finding: the assignment to _panel used to be the last line of the
            // build, so if anything threw part-way through, the Destroy() called from
            // EnsureBuilt()'s catch saw _panel==null and did nothing, leaving the
            // GameObject already attached to UIView orphaned (one more stacking up with
            // every click). Here we hold it in a local instead, and on a failed build
            // this try/catch reliably destroys our own GameObject before rethrowing
            // outwards.
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("forecast panel built");
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
            // InfoHub decides the position (MoveTo). All that is here is the default.
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = AddLabel(panel, "Title", 10f, y, PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ There is no close button here. **The tab strip owns the one and only X**
            //   (InfoHub). Put an X on each panel and there are five things that close.
            y += 30f;

            _temperatureLabel = AddLabel(panel, "Temperature", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _rainLabel = AddLabel(panel, "Rain", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _cloudLabel = AddLabel(panel, "Cloud", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            // I5: Fog was already being read every tick and carried on the snapshot, but
            // it was never shown anywhere. If we read it, we show it.
            _fogLabel = AddLabel(panel, "Fog", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _windLabel = AddLabel(panel, "Wind", 12f, y, PanelWidth - 24f, 20f);
            y += 28f;

            BuildComingSection(panel, ref y);
            BuildTyphoonSection(panel, ref y);

            // The probability is a single value that does not depend on the disaster type.
            // Show it once, above both of them, rather than under the lightning and
            // tornado headings (see the class doc).
            _probabilityLabel = AddLabel(panel, "Probability", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;

            if (ModCompat.NdrPresent)
            {
                _ndrNoteLabel = AddLabel(panel, "NdrNote", 12f, y, PanelWidth - 24f, 32f);
                _ndrNoteLabel.wordWrap = true;
                _ndrNoteLabel.text = Strings.ForecastNdrNote;
                y += 36f;
            }

            y += 6f;

            // Half of the hazard side depends on the DLC. Without it, neither "show on
            // map" nor the figure under the cursor can mean anything in principle, so we
            // drop the rows entirely and write the reason instead (handled the same way
            // as FireWhirlNeedsDlc). See the doc on _hazardRowsBuilt.
            _hazardRowsBuilt = ModCompat.NaturalDisastersOwned;
            if (!_hazardRowsBuilt)
            {
                var dlcNote = AddLabel(panel, "HazardNeedsDlc", 12f, y, PanelWidth - 24f, 36f);
                dlcNote.wordWrap = true;
                dlcNote.text = Strings.ForecastHazardNeedsDlc;
                y += 40f;

                panel.height = y;
                ClampToView(panel);
                return;
            }

            _lightningLabel = AddLabel(panel, "Lightning", 12f, y, 170f, 24f);
            var lightningButton = AddShowOnMapButton(panel, "LightningShow", y,
                InfoManager.SubInfoMode.LightningHazard);
            lightningButton.tooltip = Strings.ForecastLightning;
            y += 30f;

            _tornadoLabel = AddLabel(panel, "Tornado", 12f, y, 170f, 24f);
            var tornadoButton = AddShowOnMapButton(panel, "TornadoShow", y,
                InfoManager.SubInfoMode.TornadoHazard);
            tornadoButton.tooltip = Strings.ForecastTornado;
            y += 34f;

            _cursorHeaderLabel = AddLabel(panel, "CursorHeader", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            // The "no storm detected" explanation does not fit on one line, so it wraps.
            // Left at the 20f from the days when only a number went here, the very
            // sentence this feature most needs people to read would be cut off part-way.
            _cursorValueLabel = AddLabel(panel, "CursorValue", 12f, y, PanelWidth - 24f, 54f);
            _cursorValueLabel.wordWrap = true;
            y += 60f;

            panel.height = y;
            ClampToView(panel);
        }

        /// <summary>
        /// Raises the panel until its bottom edge no longer runs off the view (the same
        /// as the method of this name in ②, ④ and ⑤).
        /// **Never moves it horizontally** — the horizontal position is
        /// <see cref="InfoHub"/>'s to decide.
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
                if (top + panel.height > viewHeight - Margin) top = viewHeight - Margin - panel.height;
                if (top < Margin) top = Margin;
                // ★★ **Never go above the position <c>InfoHub</c> gave us (i.e. directly
                //    below the tab strip).** (2026-08-22: this was what the in-game report
                //    "among the weather and earthquake tabs there are tabs that cannot be
                //    closed with the X" actually was.) The two clamps above raise the
                //    panel purely to keep its bottom edge on screen, so a tall panel
                //    **covered the whole tab strip** — the very means of closing it became
                //    unclickable. Whatever does not fit now runs off the bottom instead,
                //    and grabbing the left end of the strip moves it all together
                //    (<c>InfoHub</c>'s drag grip).
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("forecast panel clamp failed: " + e.GetType().Name);
            }
        }

        /// <summary>
        /// The weather to come. **Unlocked by building a weather radar** (the owner's
        /// request).
        ///
        /// ★★ We show only <b>the target values the game itself holds</b>, never a time
        ///   of arrival (the reason is in the ★★ above <c>Strings.ForecastComing</c>).
        ///
        /// ★ <b>Do not use the same wording</b> for "you do not have the DLC" and "you
        ///   have not built one yet" (see the doc on
        ///   <see cref="WeatherRadarWatch.PrefabKnown"/>).
        /// </summary>
        private static void BuildComingSection(UIPanel panel, ref float y)
        {
            _comingHeaderLabel = AddLabel(panel, "ComingHeader", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;

            _comingBodyLabel = AddLabel(panel, "ComingBody", 12f, y, PanelWidth - 24f, 76f);
            _comingBodyLabel.wordWrap = true;
            y += 82f;
        }

        /// <summary>
        /// The three toggles that put the typhoon on the map, plus the button that moves
        /// to the eye.
        ///
        /// ★★ **This is where "go to the gale area" belongs.** It was originally put on
        ///   ④'s panel, but that panel had no route that called <c>Show()</c>, so it
        ///   <b>could not be opened</b> (ever since D+'s tabs were cut down to two on
        ///   2026-08-22; discovered on 2026-09-02). Here it can actually be pressed.
        ///
        /// ★ The three switch independently. Turn them all on at once and the map becomes
        ///   unreadable.
        /// </summary>
        private static void BuildTyphoonSection(UIPanel panel, ref float y)
        {
            _typhoonHeaderLabel = AddLabel(panel, "TyphoonHeader", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;

            const float gap = 8f;
            float w = (PanelWidth - 24f - gap * 2f) / 3f;

            _trackButton = AddToggle(panel, "ShowTrack", Strings.ForecastShowTrack,
                Strings.ForecastTrackTooltip, 12f, y, w, ForecastOverlay.ToggleTrack);
            _galeButton = AddToggle(panel, "ShowGale", Strings.ForecastShowGale,
                Strings.ForecastGaleTooltip, 12f + w + gap, y, w, ForecastOverlay.ToggleGale);
            _windButton = AddToggle(panel, "ShowWind", Strings.ForecastShowWind,
                Strings.ForecastWindTooltip, 12f + (w + gap) * 2f, y, w,
                ForecastOverlay.ToggleWind);
            y += 28f;

            _gotoButton = AddToggle(panel, "GoToStorm", Strings.ForecastGoToStorm,
                Strings.ForecastGoToStormTooltip, 12f, y, PanelWidth - 24f, JumpToStorm);
            y += 34f;
        }

        /// <summary>
        /// Moves the camera to the eye of the typhoon. **Main thread (a click).**
        ///
        /// ★ We take the snapshot again here — the typhoon is moving, so whatever
        ///   <see cref="Refresh"/> is holding is one frame old.
        ///
        /// ★ While it is still approaching, the centre is off the map. The game pulls
        ///   that back itself in <c>GameAreaManager.ClampPoint</c>, so we end up at "the
        ///   edge of the map in the direction the typhoon is coming from"
        ///   (see <see cref="CameraJump"/>).
        /// </summary>
        private static void JumpToStorm()
        {
            var typhoon = TyphoonHub.Latest;
            if (typhoon == null || !typhoon.Active) return;

            CameraJump.To(new Vector3(typhoon.Centre.X, typhoon.Centre.Y, typhoon.Centre.Z),
                          typhoon.StormRadius);
        }

        private static UIButton AddToggle(UIPanel parent, string suffix, string text,
                                          string tooltip, float x, float y, float width,
                                          OnClick onClick)
        {
            var button = (UIButton)parent.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Forecast" + suffix;
            button.text = text;
            if (!string.IsNullOrEmpty(tooltip)) button.tooltip = tooltip;
            button.width = width;
            button.height = 24f;
            button.relativePosition = new Vector3(x, y);
            button.textScale = 0.85f;
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += (c, e) => onClick();
            return button;
        }

        private delegate void OnClick();

        /// <summary>
        /// A line break. **Do not write it as a literal** — this file gets rewritten by
        /// patch scripts, and the escape turns into a bare newline that breaks the string
        /// (which is exactly what happened on 2026-09-02). A constant cannot be mangled.
        /// </summary>
        private static readonly string Newline = ((char)10).ToString();

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y, float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Forecast" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = new Color32(255, 255, 255, 255);
            label.autoSize = false;
            return label;
        }

        private static UIButton AddShowOnMapButton(UIPanel parent, string suffix, float y,
            InfoManager.SubInfoMode subMode)
        {
            var button = (UIButton)parent.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Forecast" + suffix;
            button.text = Strings.ForecastShowOnMap;
            button.width = 150f;
            button.height = 24f;
            button.relativePosition = new Vector3(PanelWidth - 12f - 150f, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += (c, e) => InfoModeSwitch.ShowHazard(subMode);
            return button;
        }

        private static void Refresh()
        {
            var snapshot = ForecastHub.Latest;

            _titleLabel.text = Strings.ForecastTitle;
            RefreshComing(snapshot);
            RefreshTyphoon();

            if (_hazardRowsBuilt)
            {
                _lightningLabel.text = Strings.ForecastLightning;
                _tornadoLabel.text = Strings.ForecastTornado;
                _cursorHeaderLabel.text = Strings.ForecastAtCursor;
            }

            if (snapshot == null || !snapshot.Valid)
            {
                // Review finding: "we have not read anything yet" and "we read, but
                // WeatherManager is absent" must not share the same wording. The former
                // happens routinely if you stay paused right after loading
                // (FeatureHost.SimulationTick does not call OnSimulationTick while
                // deltaMinutes<=0, and the first tick after a load is always 0). Saying
                // "cannot read" there makes it look broken when nothing is.
                string message = snapshot == null
                    ? Strings.ForecastWaiting
                    : Strings.ForecastUnavailable;

                _temperatureLabel.text = Strings.ForecastTemperature + ": " + message;
                _rainLabel.text = Strings.ForecastRain + ": " + message;
                _cloudLabel.text = Strings.ForecastCloud + ": " + message;
                _fogLabel.text = Strings.ForecastFog + ": " + message;
                _windLabel.text = Strings.ForecastWind + ": " + message;
                _probabilityLabel.text = Strings.ForecastProbability + ": " + message;
                RefreshCursorHazard(snapshot);
                return;
            }

            // No degree sign (deg, U+00B0). Same reason as keeping HazardLevel's bar
            // characters in ASCII: nothing guarantees CS's UI font has a glyph outside
            // the ASCII range.
            _temperatureLabel.text = Strings.ForecastTemperature + ": "
                + snapshot.Temperature.Current.ToString("F1")
                + "  " + TrendWord(snapshot.Temperature.Trend);
            _rainLabel.text = Strings.ForecastRain + ": "
                + snapshot.Rain.Current.ToString("F2")
                + "  " + TrendWord(snapshot.Rain.Trend);
            _cloudLabel.text = Strings.ForecastCloud + ": "
                + snapshot.Cloud.Current.ToString("F2")
                + "  " + TrendWord(snapshot.Cloud.Trend);
            _fogLabel.text = Strings.ForecastFog + ": "
                + snapshot.Fog.Current.ToString("F2")
                + "  " + TrendWord(snapshot.Fog.Trend);
            _windLabel.text = Strings.ForecastWind + ": " + WindDirection.LabelOf(snapshot.WindDegrees);

            // A city-wide value. It belongs to neither the lightning nor the tornado
            // heading (see the class doc). The boolean state of being in a cooldown is
            // not put into words here (no localisation key reserved; it does go into the
            // diagnostics).
            //
            // Review finding: a bare unlabelled "50.0%" placed right after rain and cloud
            // gets misread as "chance of rain" or similar. That is the same error as
            // showing a hazard figure under the label of a type that is not on display,
            // arrived at here by forgetting the label. Make it explicit with the
            // ForecastProbability label.
            //
            // The *100 is backed by the IL (Task 5).
            // DefaultSettings.randomDisastersProbability is 0.5 (=50%), and vanilla's own
            // PopsTelemetryEventFormatting.DisasterProbability performs exactly the same
            // conversion on the same value for telemetry:
            // `ldc.r4 100 / mul / Mathf.RoundToInt`. So m_randomDisastersProbability is a
            // fraction in 0.0-1.0, and multiplying by 100 to show a percentage is not an
            // interpretation of our own but agrees with how the game itself treats it.
            // Note, though, that in the IL of DisasterManager.SimulationStepImpl the
            // actual per-tick decision to spawn does not use this value as it stands; it
            // goes through a more involved expression (squared, corrected for the city's
            // area, then compared against a random number). The distinction — that what
            // we show here is "the configured probability", not a direct figure for "the
            // chance of one happening this instant" — is written into the comments on the
            // WriteDiagnostics side too.
            //
            // Review finding: when DisasterManager was absent this used to display "0.0%"
            // from the untouched 0f. That was a fabricated zero, indistinguishable from
            // "it really was 0%" and from "we could not read it". When
            // DisasterInfoAvailable is false we leave the line out entirely (no figure at
            // all).
            if (snapshot.DisasterInfoAvailable)
            {
                float probabilityPercent = snapshot.DisasterProbability * 100f;
                _probabilityLabel.text = Strings.ForecastProbability + ": "
                    + probabilityPercent.ToString("F1") + "%";
            }
            else
            {
                _probabilityLabel.text = "";
            }

            RefreshCursorHazard(snapshot);
        }

        /// <summary>
        /// The weather to come. **Target values only** (no time of arrival — the reason is
        /// above <c>Strings.ForecastComing</c>).
        /// </summary>
        private static void RefreshComing(WeatherSnapshot snapshot)
        {
            if (_comingHeaderLabel == null) return;

            _comingHeaderLabel.text = Strings.ForecastComing;

            // ★ Do not conflate "no DLC", "not built yet" and "built but not running".
            if (!WeatherRadarWatch.PrefabKnown)
            {
                _comingBodyLabel.text = Strings.ForecastComingNeedsDlc;
                return;
            }

            if (!WeatherRadarWatch.HasWorkingRadar)
            {
                _comingBodyLabel.text = Strings.ForecastComingLocked;
                return;
            }

            if (snapshot == null || !snapshot.Valid)
            {
                _comingBodyLabel.text = snapshot == null
                    ? Strings.ForecastWaiting : Strings.ForecastUnavailable;
                return;
            }

            string arrow = "  " + Strings.ForecastComingHeading + " ";
            _comingBodyLabel.text =
                Strings.ForecastTemperature + " " + snapshot.Temperature.Current.ToString("F1")
                + arrow + snapshot.Temperature.Target.ToString("F1") + Newline
                + Strings.ForecastRain + " " + snapshot.Rain.Current.ToString("F2")
                + arrow + snapshot.Rain.Target.ToString("F2") + Newline
                + Strings.ForecastCloud + " " + snapshot.Cloud.Current.ToString("F2")
                + arrow + snapshot.Cloud.Target.ToString("F2") + Newline
                + Strings.ForecastFog + " " + snapshot.Fog.Current.ToString("F2")
                + arrow + snapshot.Fog.Target.ToString("F2");
        }

        /// <summary>
        /// How the map toggles look. **The ones that are on change colour** — pressing a
        /// toggle changes nothing on this side of the screen, so if the state is not
        /// visible it gets read as "it did not work".
        /// </summary>
        private static void RefreshTyphoon()
        {
            if (_typhoonHeaderLabel == null) return;

            var typhoon = TyphoonHub.Latest;
            bool live = typhoon != null && typhoon.Active;

            _typhoonHeaderLabel.text = live
                ? Strings.ForecastTyphoonSection
                : Strings.ForecastTyphoonSection + "  -  " + Strings.ForecastNoTyphoon;

            SetToggleLook(_trackButton, ForecastOverlay.ShowTrack);
            SetToggleLook(_galeButton, ForecastOverlay.ShowGale);
            SetToggleLook(_windButton, ForecastOverlay.ShowWind);

            // ★ With no typhoon about, leave the toggles pressable (so you can set them
            //   up ready for the next one). **Only the go-to button is disabled** — if it
            //   stayed pressable with nowhere to go, it would be a button that does
            //   nothing when you press it.
            if (_gotoButton != null) _gotoButton.isEnabled = live;
        }

        private static void SetToggleLook(UIButton button, bool on)
        {
            if (button == null) return;
            button.normalBgSprite = on ? "ButtonMenuFocused" : "ButtonMenu";
            button.textColor = on
                ? new Color32(255, 220, 120, 255)
                : new Color32(255, 255, 255, 255);
        }

        /// <summary>
        /// The hazard value under the cursor. Only the submode currently on display is
        /// attempted. Never, ever show a figure under the label of a type that is not on
        /// display (HazardMapReader.SampleAt guarantees this by returning ok=false).
        ///
        /// Review finding: ForecastUnavailable ("cannot read the weather data") used to be
        /// reused for the case where no hazard view is up, and that was wrong. The weather
        /// data itself is alive (temperature, rain, cloud, fog and wind are all being
        /// shown); the only thing we cannot show is the hazard figure, and we have
        /// identified the cause as "no hazard view is up" (§4.2). On top of that, while
        /// the panel is being read the mouse is almost certainly over the panel itself
        /// (UIView.IsInsideUI()==true) and picking a cursor point on the terrain always
        /// fails — which makes this the line the user sees most often of all. So we check
        /// "is there a hazard view on display?" before asking about the cursor position,
        /// and if there is not we immediately show the hint that names the cause
        /// (ForecastSwitchHazardView). ForecastUnavailable (= the cursor position is
        /// unknown) is used only for the case where some hazard view is up but the cursor
        /// position cannot be worked out (over the UI, or off the terrain).
        ///
        /// **The most important finding of the full review (it changed what this method
        /// means):** vanilla's hazard map is not a static risk surface but the predicted
        /// damage area of "storms the radar has located (Located) and that are under way
        /// (Emerging|Active)" (the IL evidence is in the doc on
        /// WeatherSnapshot.LocatedLightningStorms). With not a single such storm,
        /// UpdateTexture refills the grid with zeroes every time and then nobody writes
        /// into it, so **the whole city** reads 0. This method used to display that as
        /// plainly as "Lightning: 0". SampleAt returns ok=true — the submode does match
        /// and the grid really is there, its contents are just all zero. As a number it is
        /// genuine, but the meaning the player takes from it ("this city has no lightning
        /// risk") is a lie. The truth is "no storm is detected right now", and this was
        /// **a confidently wrong number reached not from a wrong label but from a wrong
        /// premise**. So when the located count for the type on display is 0, we show no
        /// figure at all and write the reason it is empty (namely, you need a weather
        /// radar).
        /// </summary>
        /// <param name="snapshot">
        /// Where the located counts come from. When it is null, or Valid=false, or
        /// DisasterInfoAvailable=false, the counts are **unknown**, so we must not state
        /// flatly that "no storm is detected" (that would itself be an assertion hiding
        /// the fact that we could not read anything). Fall back to the generic unknown.
        /// </param>
        private static void RefreshCursorHazard(WeatherSnapshot snapshot)
        {
            // On a setup without the DLC the hazard rows were never built at all (I2).
            if (!_hazardRowsBuilt) return;

            if (!InfoModeSwitch.IsShowingHazard)
            {
                _cursorValueLabel.text = Strings.ForecastSwitchHazardView;
                return;
            }

            // Pin down the submode on display first. We look at this before the cursor
            // coordinates because deciding "there are zero located storms" does not need
            // a cursor position at all. While the panel is being read the mouse is over
            // the panel and the cursor pick fails, so asking for coordinates first would
            // mean never reaching the one explanation we most want to give — that you
            // need a radar.
            bool showingLightning =
                InfoModeSwitch.IsShowingHazardFor(InfoManager.SubInfoMode.LightningHazard);
            bool showingTornado =
                InfoModeSwitch.IsShowingHazardFor(InfoManager.SubInfoMode.TornadoHazard);

            if (!showingLightning && !showingTornado)
            {
                // IsShowingHazard is true, but the submode on display is neither
                // lightning nor tornado (flood, meteor, sinkhole, earthquake or forest
                // fire hazard, say). That is outside the two types this feature covers,
                // so asserting the cause as "please switch views" would be inaccurate.
                // Stop at the generic unknown.
                _cursorValueLabel.text = Strings.ForecastUnavailable;
                return;
            }

            // If the counts could not be read, assert nothing (see the parameter's doc).
            if (snapshot == null || !snapshot.Valid || !snapshot.DisasterInfoAvailable)
            {
                _cursorValueLabel.text = Strings.ForecastUnavailable;
                return;
            }

            int located = showingLightning
                ? snapshot.LocatedLightningStorms
                : snapshot.LocatedTornadoes;

            if (located <= 0)
            {
                // The grid is all zeroes. Show no figure; show the reason it is empty.
                _cursorValueLabel.text = Strings.ForecastNoStormDetected;
                return;
            }

            Vec3 hit;
            if (!TryPickCursorGround(out hit))
            {
                _cursorValueLabel.text = Strings.ForecastUnavailable;
                return;
            }

            var worldPos = new Vector3(hit.X, hit.Y, hit.Z);
            var subMode = showingLightning
                ? InfoManager.SubInfoMode.LightningHazard
                : InfoManager.SubInfoMode.TornadoHazard;

            bool ok;
            byte value = HazardMapReader.SampleAt(worldPos, subMode, out ok);
            if (!ok)
            {
                _cursorValueLabel.text = Strings.ForecastUnavailable;
                return;
            }

            _cursorValueLabel.text = (showingLightning ? Strings.ForecastLightning : Strings.ForecastTornado)
                + ": " + value + "  [" + HazardLevel.BarOf(value) + "]";
        }

        /// <summary>
        /// Camera.main in Unity 5.6 is a tag lookup, and this path can be called every
        /// frame while the panel is up, so we cache it (review finding). We leave the
        /// check to Unity's own == null so that a fake-null (a destroyed camera) is
        /// caught, rather than doing a raw reference comparison.
        /// </summary>
        private static Camera _mainCameraCache;

        /// <summary>The frame (<c>Time.frameCount</c>) in which the ray was last actually
        /// cast, and the result of it.</summary>
        private static int _pickFrame;
        private static bool _pickCached;
        private static Vec3 _pickHit;
        private static bool _pickOk;

        /// <summary>
        /// The ground directly under the cursor. The computation itself is unchanged;
        /// **only how often we cast** is held down by
        /// <see cref="RepickIntervalFrames"/> (the same shape as ②'s
        /// <c>EarthquakePanel.TryPickCursorGround</c>).
        ///
        /// The one line of <c>UIView.IsInsideUI()</c> sits **outside** the throttle. While
        /// the panel is being read — while the mouse is over the panel — no sampling
        /// happens at all, so this is the early-out that pays best and we want it decided
        /// before the cache. It also lets this path report "the cursor has become
        /// invalid" without delay.
        /// </summary>
        private static bool TryPickCursorGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // With the mouse over the panel or any other UI there is no meaningful point.
            if (UIView.IsInsideUI())
            {
                // Throw the cache away so that when the cursor next returns to the
                // terrain we do not simply hand back the stale point from before it went
                // over the UI.
                _pickCached = false;
                return false;
            }

            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            var cam = _mainCameraCache;
            if (cam == null)
            {
                _pickCached = false;
                return false;
            }

            // ★ The up-to-501 height samples only run once every
            //    RepickIntervalFrames frames.
            int frame = Time.frameCount;
            if (_pickCached && frame - _pickFrame < RepickIntervalFrames)
            {
                hit = _pickHit;
                return _pickOk;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            _pickOk = RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out _pickHit);
            _pickFrame = frame;
            _pickCached = true;

            hit = _pickHit;
            return _pickOk;
        }

        private static string TrendWord(Trend trend)
        {
            switch (trend)
            {
                case Trend.Rising: return Strings.TrendRising;
                case Trend.Falling: return Strings.TrendFalling;
                default: return Strings.TrendSteady;
            }
        }
    }
}
