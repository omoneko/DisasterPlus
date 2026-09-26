using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The earthquake panel. **Main thread only.**
    ///
    /// The framework follows ①'s <see cref="ForecastPanel"/> exactly (the non-generic
    /// <c>UIView.AddUIComponent(Type)</c> overload, the try/catch that leaves no orphan
    /// GameObject when construction throws part way, <c>backgroundSprite = "MenuPanel2"</c>
    /// and the <c>Camera.main</c> cache). The only new machinery ② introduces is
    /// **the layer separation**.
    ///
    /// ── The layer separation (this feature's core guarantee of honesty) ──────
    ///
    /// Layer 1 = quantities derived purely from vanilla's own formulae and constants.
    /// Layer 2 = physics this mod invented. Mix the two and this feature loses its reason
    /// to exist. The guarantee itself has been moved into
    /// <see cref="EarthquakeRows"/> (only that type can make a label or write
    /// <c>.text</c>). **This file contains not one <c>UILabel</c> creation and not one
    /// assignment to <c>.text</c>.**
    ///
    /// ── The vertical budget and tabs (this task's structural change) ────────
    ///
    /// By the time layer 1 was finished the panel had stacked up to about 1050, leaving
    /// almost nothing against UIView's height of 1080. Layer 2 adds three sections
    /// (Tasks 9-11), so simply appending rows would push the explanatory text off screen.
    /// Vertical space was made by splitting layer 1's content across two tabs
    /// (<see cref="EarthquakePanelTabs"/>) and putting **layer 2 outside the tabs, below
    /// them**. Why tabs were chosen and the other options rejected is in
    /// <see cref="EarthquakePanelTabs"/>'s doc.
    ///
    /// Not one character of the rows' content was changed. So that the references to
    /// "above" and "below" in the text do not break, **rows that refer to each other are
    /// kept on the same tab**:
    ///   - <c>EarthquakeShakeNote</c> refers to "the destruction radius above" → same tab
    ///     as the destruction-factor row
    ///   - <c>EarthquakeCursorModelsNote</c> refers to "the destruction factor above" and
    ///     "the overlay below" → all three on the same tab
    ///   - <c>EarthquakeOverlayLegend</c> refers to "the same 10 steps as the bar above" →
    ///     likewise
    ///
    /// ── An empty hazard map does not mean "safe" ──────────────────────
    ///
    /// The earthquake hazard map has **the same two-stage gate, byte for byte**, as ①'s
    /// storms (<c>Located(4096)</c> and <c>Emerging|Active(12)</c>, IL facts doc §A-6).
    /// And **only a seismograph** can set <c>Located</c> on an earthquake (§A-2 / §C-2).
    /// So in a city with no seismographs this view is **permanently blank** even with an
    /// earthquake in progress, and that is the correct state.
    ///
    /// ① got this premise wrong and displayed an all-zero grid as "Lightning: 0"; the
    /// whole-feature review judged it "not a wrong label but a confidently wrong number,
    /// arrived at from a wrong premise", and it was rebuilt. ② copies the behaviour
    /// **after** that fix: when no earthquake is painting anything, show **no number at
    /// all, and write why it is empty and that a seismograph is what changes it**
    /// (<c>Strings.EarthquakeNotLocated</c>).
    ///
    /// ── The whole-quake disc and the fault band are different models ──────────
    ///
    /// Vanilla runs two kinds of destruction per step (§A-3). The whole-quake disc is a
    /// linear ramp at <c>probability = 0.02</c> (i.e. <see cref="SeismicIntensity"/>'s s);
    /// the four fault discs run at <c>probability = 1</c> and are re-positioned every
    /// step. **Never present the fault band as a state where s is high.** Put it on its
    /// own row and always attach the note that it is where damage could fall, not where
    /// it will (<see cref="EarthquakeDamageRows"/>).
    /// </summary>
    public static class EarthquakePanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "EarthquakePanel";

        private static UIPanel _panel;
        private static EarthquakePanelTabs _tabs;
        private static UILabel _titleLabel;
        private static UILabel _countLabel;
        private static UILabel _intensityLabel;
        private static UILabel _phaseLabel;
        private static UILabel _timeLabel;
        private static UILabel _cursorLabel;
        private static UILabel _shakeLabel;

        /// <summary>
        /// Whether the earthquake rows were built. Without the Natural Disasters DLC they
        /// are not built and a one-line reason is shown instead
        /// (<see cref="Strings.EarthquakeNeedsDlc"/>).
        ///
        /// Treated the same way as ①'s <c>ForecastPanel._hazardRowsBuilt</c>. Without the
        /// DLC there is neither an <c>EarthquakeAI</c> DisasterInfo prefab nor a
        /// seismograph, so not one earthquake can happen in principle. Yet
        /// <c>Assumptions</c>' type-existence check passes anyway (the AI's **type** ships
        /// in Assembly-CSharp regardless of the DLC), so the startup log gives no hint
        /// either.
        /// </summary>
        private static bool _bodyBuilt;

        /// <summary>
        /// The validity flag last passed to <see cref="EarthquakeHub.PublishCursor"/>.
        ///
        /// There is no point re-publishing "invalid" every frame while the panel is
        /// closed, so the lock is only taken when the state changes. **Conversely, the
        /// fact that it has become invalid must always be communicated once** — without
        /// that, the sim side goes on probing the last position it saw forever, sweeping
        /// the building grid every tick on behalf of a closed panel.
        /// </summary>
        private static bool _cursorPublishedValid;

        /// <summary>The top-left corner. The default is only used before <see cref="InfoHub"/> sets the position.</summary>
        private static Vector3 _origin = new Vector3(600f, 150f);

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>This panel's width. <see cref="InfoHub"/> reads it to match the tab strip's width.</summary>
        internal static float Width { get { return EarthquakeRows.PanelWidth; } }

        /// <summary>
        /// Sets the top-left corner. **<see cref="InfoHub"/> is the single authority on
        /// position** (the same discipline as <c>DisasterPanelBar</c>'s class doc: "as
        /// long as more than one thing decides the position, this accident will keep
        /// happening in new forms"). Never invent coordinates here.
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
            // Stop the sim side's building sweep the instant it closes.
            PublishCursor(new Vec3(0f, 0f, 0f), false);
            // ★ Turn the intensity overlay off with it. The legend exists only inside
            //    this panel, so if the picture stays on the map with the panel closed,
            //    the only thing saying what quantity you are looking at disappears from
            //    the screen (see EarthquakeOverlay.Disable's doc).
            EarthquakeOverlay.Disable();
        }

        /// <summary>Every frame, from the main thread. Updates the content only while visible.</summary>
        public static void Tick()
        {
            // If the panel is left open when the feature is disabled in the settings, it
            // becomes a panel that is frozen but looks alive — forever showing the stale
            // snapshot from when OnSimulationTick stopped publishing — and it cannot be
            // dismissed, because the button that would close it has already been removed
            // (raised in ①'s review). The same guard as on the button side goes here too.
            if (!ModSettings.EarthquakeEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible)
            {
                PublishCursor(new Vec3(0f, 0f, 0f), false);
                return;
            }
            Refresh();
        }

        /// <summary>
        /// Passes the cursor position to the sim side. **This is the only place that
        /// publishes.** Repeated "invalid" calls that do not change the state are
        /// swallowed (<see cref="_cursorPublishedValid"/>).
        /// </summary>
        private static void PublishCursor(Vec3 pos, bool valid)
        {
            if (!valid && !_cursorPublishedValid) return;
            _cursorPublishedValid = valid;
            EarthquakeHub.PublishCursor(pos, valid);
        }

        /// <summary>On level unload. **Carry over no session state whatsoever.**</summary>
        public static void Destroy()
        {
            // ★ Before the panel's GameObject. A Texture2D is not a Component, so
            //    destroying the parent does not take it with it and one is left behind
            //    every time a city is reloaded (the leak version of the "static cache
            //    goes rotten across cities" shape that actually bit us in ③).
            //    WaveformView.Destroy() calls Object.Destroy itself.
            WaveformView.Destroy();
            // Just drop the references; the objects go with the panel's GameObject.
            EarthquakeMapRows.Destroy();
            EarthquakeDamageRows.Destroy();
            EarthquakeSensorRows.Destroy();
            EarthquakeLayer2Rows.Destroy();
            // The panel goes, and the legend with it, so turn the picture off too (the
            // same reason as in Hide()).
            EarthquakeOverlay.Disable();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _tabs = null;
            _titleLabel = null;
            _countLabel = null;
            _intensityLabel = null;
            _phaseLabel = null;
            _timeLabel = null;
            _cursorLabel = null;
            _shakeLabel = null;
            _bodyBuilt = false;
            _cursorPublishedValid = false;
            // Make sure the next city never once returns the previous city's cursor
            // position.
            EarthquakeCursorPicker.Reset();
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
                Log.Error("earthquake panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; earthquake panel not built");
                return;
            }

            // Guarding against a recurrence of what ①'s review found: if the assignment
            // to _panel is the last line of construction, then when something throws part
            // way, the Destroy() called by EnsureBuilt()'s catch sees _panel==null and
            // does nothing, leaving the GameObject already attached to UIView orphaned
            // (one more piling up with every click).
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("earthquake panel built");
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
            panel.width = EarthquakeRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // The position is decided by InfoHub (MoveTo). Only the default lives here —
            // only one panel is ever shown at a time, so coordinates chosen to avoid each
            // other are no longer needed.
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = EarthquakeRows.AddTitleRow(panel, "Title", 10f, y,
                EarthquakeRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ There is no close button here. **The tab strip's single X owns that**
            //   (InfoHub).
            y += 30f;

            // Without the DLC, earthquakes do not exist at all. Show no rows and write
            // the reason.
            _bodyBuilt = ModCompat.NaturalDisastersOwned;
            if (!_bodyBuilt)
            {
                EarthquakeRows.AddPlainRow(panel, "NeedsDlc", ref y, Strings.EarthquakeNeedsDlc, 36f);
                panel.height = y;
                return;
            }

            _tabs = new EarthquakePanelTabs(panel, ref y);

            var quakePage = _tabs.AddPage("Quake", Strings.EarthquakeTabQuake);
            float pageY = 0f;
            BuildQuakePage(quakePage, ref pageY);
            _tabs.FinishPage(quakePage, pageY);

            var damagePage = _tabs.AddPage("Damage", Strings.EarthquakeTabDamage);
            pageY = 0f;
            BuildDamagePage(damagePage, ref pageY);
            _tabs.FinishPage(damagePage, pageY);

            _tabs.Finish(ref y);

            // ★ Layer 2 (Tasks 9-11) is added **outside** the tabs, below this point
            //    (the plan's shared rule: "layer 2 is built below layer 1, with no
            //    rearranging at runtime"). It is in the same place whichever tab you are
            //    looking at.
            EarthquakeLayer2Rows.Build(panel, ref y);

            panel.height = y;
            ClampToView(panel);
        }

        /// <summary>
        /// Rebuilds the panel's height. Layer 2's sections disappear entirely depending on
        /// the settings (<see cref="EarthquakeLayer2Rows"/>), so this is reached only at
        /// the instant one of them is toggled. **Never call it every frame** —
        /// <see cref="ClampToView"/> rewrites <c>relativePosition</c>, so running it every
        /// frame makes the panel drift about slightly.
        /// </summary>
        internal static void Relayout()
        {
            if (_panel == null || !_bodyBuilt) return;
            _panel.height = EarthquakeLayer2Rows.SectionTop + EarthquakeLayer2Rows.VisibleHeight;
            ClampToView(_panel);
        }

        /// <summary>
        /// Tab 1, "Earthquake, cursor and maps": the values of the earthquake itself, the
        /// numbers two models give for the single point under the cursor, and the two
        /// kinds of map.
        ///
        /// **The notes' wording demands this arrangement.**
        /// <c>EarthquakeShakeNote</c> says "the destruction radius above is a different
        /// quantity", <c>EarthquakeCursorModelsNote</c> points at both "the destruction
        /// factor above" and "the overlay below", and <c>EarthquakeOverlayLegend</c> says
        /// "the same 10 steps as the bar above". Split these across tabs and the notes
        /// point at rows that are not there.
        /// </summary>
        private static void BuildQuakePage(UIPanel p, ref float y)
        {
            // ★★ **The permanent explanations were removed** (2026-08-22, at the owner's
            //    request: "anything that does not bear on gameplay is unnecessary").
            //    The content lives in <c>EarthquakeFeature.WriteDiagnostics</c>'
            //    diagnostic dump.

            EarthquakeRows.AddSectionHeader(p, "Layer1Header", ref y, Strings.EarthquakeLayer1Header);

            _countLabel = EarthquakeRows.AddLayer1Row(p, "Count", ref y);
            _intensityLabel = EarthquakeRows.AddLayer1Row(p, "Intensity", ref y);
            _phaseLabel = EarthquakeRows.AddLayer1Row(p, "Phase", ref y);
            _timeLabel = EarthquakeRows.AddLayer1Row(p, "TimeToShock", ref y);
            _cursorLabel = EarthquakeRows.AddLayer1Row(p, "AtCursor", ref y);

            // ★ The shaking is a different quantity from the collapse ramp (§A-7). The s
            //    row used to call itself "shaking at the cursor" and write "not shaking"
            //    outside radius R, but vanilla's shaking formula has no radius cut-off
            //    at all, and on the same frame CameraShakeBooster is adding shake and
            //    SeismographRecorder is writing non-zero displacement. Three components
            //    were making contradictory claims about the same physical quantity, so
            //    the shaking gets a row of its own, as shaking.
            _shakeLabel = EarthquakeRows.AddLayer1Row(p, "ShakeAtCursor", ref y);

            EarthquakeMapRows.Build(p, ref y);
        }

        /// <summary>
        /// Tab 2, "Building damage and seismographs": the fault band and each building's
        /// headroom, what a seismograph does, and the waveforms.
        ///
        /// **The heading repeats tab 1's "what the game actually computes".** That way
        /// both tabs declare that they are layer 1, whichever one you open.
        /// </summary>
        private static void BuildDamagePage(UIPanel p, ref float y)
        {
            EarthquakeRows.AddSectionHeader(p, "Layer1HeaderDamage", ref y,
                Strings.EarthquakeLayer1Header);

            EarthquakeDamageRows.Build(p, ref y);
            EarthquakeSensorRows.Build(p, ref y);
        }

        /// <summary>
        /// Raises the panel until its bottom edge no longer runs off the view.
        ///
        /// **Every row added makes the panel taller.** Leave the position hard-coded and
        /// the bottom rows — the notes and the "why it is empty" lines — quietly go off
        /// screen. **Writing an explanation that cannot be read** is as bad as not
        /// writing it, or worse.
        ///
        /// Measured in the IL: <c>ColossalFramework.UI.UIView.fixedHeight</c> really is a
        /// readable and writable <c>Int32</c> property (default 1080, in the same
        /// normalised coordinate system as <c>relativePosition</c>). Where it cannot be
        /// read, or where the content is taller than the view, we align to the top.
        ///
        /// **This warning stays even after the move to tabs.** Tabs made vertical room,
        /// but row heights are reserved without measuring the actual wrapping, so
        /// depending on the language and font it can still overflow. Never let it be cut
        /// off silently.
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
                    // ★ Getting here means **the content is taller than the view**: even
                    //    aligned to the top, the bottom rows go off screen.
                    //    **Never let it be cut off silently.** This happens once at
                    //    construction, so no throttling is needed.
                    top = Margin;
                    Log.Warn("earthquake panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
                // ★★ **Never let it rise above the position <c>InfoHub</c> specified
                //    (i.e. directly below the tab strip).** (2026-08-22; this was the real
                //    cause of the in-game report "there are tabs inside the weather and
                //    earthquake tabs that the X will not close".) The two adjustments
                //    above raise the panel purely to bring its bottom edge on screen, so
                //    a tall panel **covered the whole tab strip** — the means of closing
                //    it became unclickable. Whatever does not fit now runs off the bottom
                //    instead, and grabbing the strip's left edge moves both together
                //    (<c>InfoHub</c>'s drag grip).
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                // Do not let a positional tweak fail the whole construction (it happens
                // once at construction, so no throttling is needed).
                Log.Warn("earthquake panel clamp failed: " + e.GetType().Name);
            }
        }

        // ── Updating the content ──────────────────────────────

        private static void Refresh()
        {
            EarthquakeRows.SetPlain(_titleLabel, Strings.EarthquakeTitle);

            // Without the DLC, only the one explanatory row was built (see _bodyBuilt's doc).
            if (!_bodyBuilt) return;

            var snapshot = EarthquakeHub.Latest;
            if (snapshot == null || !snapshot.Valid)
            {
                // While nothing can be read, do not have the sim side looking for buildings.
                PublishCursor(new Vec3(0f, 0f, 0f), false);
                ClearQuakeRows();
                EarthquakeDamageRows.Clear();
                EarthquakeSensorRows.ClearSensor();
                EarthquakeSensorRows.ClearWaveform();
                // Do not give "we have not read once yet" and "we read and it could not
                // be read" the same wording (raised in ①'s review). The first happens
                // routinely if the game is left paused right after a load (the first
                // tick's deltaMinutes is always 0).
                EarthquakeRows.SetPlain(_countLabel, snapshot == null
                    ? Strings.ForecastWaiting
                    : Strings.EarthquakeUnavailable);
                // Bring only the button's appearance into line with reality (the legend
                // is not cleared).
                EarthquakeMapRows.ShowUnavailable();
                EarthquakeLayer2Rows.Refresh(snapshot);
                return;
            }

            // The cursor position is worked out once per frame. A ray that grazes the
            // terrain and misses runs 501 height samples, so it must never be cast twice
            // in the same frame (it is shared by the intensity row, the hazard row and
            // the seismograph row). The ray is only actually cast once every few frames,
            // and the remaining frames return the previous result (see
            // EarthquakeCursorPicker's class doc).
            //
            // **It is cast even with no earthquake at all.** The seismograph coverage at
            // the cursor means "does a seismograph reach here", which has nothing to do
            // with whether there is an earthquake (design doc §3.4). The value of this
            // row is that you can use it to scout where to build a seismograph, and it is
            // no use for scouting if it can only be read while a quake is happening.
            // Until Task 6's throttle went in, this "always cast" was not acceptable.
            bool hazardViewOn =
                InfoModeSwitch.IsShowingHazardFor(InfoManager.SubInfoMode.EarthquakeHazard);
            Vec3 cursor;
            bool haveCursor = EarthquakeCursorPicker.TryPick(out cursor);

            // ★ Never touch the building buffers from the main thread. Pass only the
            //    position to the sim side and receive what stands under it in the next
            //    tick's snapshot (see BuildingProbe's class doc; the one-tick lag is the
            //    designed-in price of that).
            PublishCursor(cursor, haveCursor);

            var primary = RefreshQuakeRows(snapshot, haveCursor, cursor);
            EarthquakeSensorRows.RefreshSensor(snapshot, primary, haveCursor);
            EarthquakeSensorRows.RefreshWaveform(snapshot);
            EarthquakeMapRows.Refresh(snapshot, hazardViewOn, haveCursor, cursor);
            // ★ Called last. Write what this mod added only after all of layer 1 is
            //   written.
            EarthquakeLayer2Rows.Refresh(snapshot);
        }

        private static void ClearQuakeRows()
        {
            EarthquakeRows.SetPlain(_countLabel, "");
            EarthquakeRows.SetPlain(_intensityLabel, "");
            EarthquakeRows.SetPlain(_phaseLabel, "");
            EarthquakeRows.SetPlain(_timeLabel, "");
            EarthquakeRows.SetPlain(_cursorLabel, "");
            EarthquakeRows.SetPlain(_shakeLabel, "");
            // The note only appears while the row does (RefreshShakeRow puts it back).
        }

        /// <summary>
        /// Writes the earthquake rows and returns the earthquake the rows below should
        /// refer to (<see cref="SelectPrimary"/>'s result). Null when there are no
        /// earthquakes at all.
        /// </summary>
        private static EarthquakeReading RefreshQuakeRows(EarthquakeSnapshot snapshot,
                                                          bool haveCursor, Vec3 cursor)
        {
            var quakes = snapshot.Quakes;
            if (quakes.Count == 0)
            {
                ClearQuakeRows();
                EarthquakeDamageRows.Clear();
                // Say it in a sentence rather than as a bare "0". It is the result of a
                // sweep, so it is layer 1.
                EarthquakeRows.SetLayer1(_countLabel, Strings.EarthquakeNoneActive);
                return null;
            }

            var primary = SelectPrimary(quakes, haveCursor, cursor);

            // With several running at once (§E-1 settles that this is possible), state
            // which earthquake the rows below belong to. It is an index into the disaster
            // buffer, so it is not localised.
            string count = Strings.EarthquakeCount + ": " + quakes.Count;
            if (quakes.Count > 1) count += "   (#" + primary.DisasterId + ")";
            EarthquakeRows.SetLayer1(_countLabel, count);

            // The intensity is displayed at 1/10, matching the disaster panel (§A-2b's
            // m_label.text = (value / 10).ToString("F1")). Show the raw byte and it
            // disagrees with every other display in the game by a factor of 10.
            EarthquakeRows.SetLayer1(_intensityLabel,
                Strings.EarthquakeIntensity + ": " + (primary.Intensity / 10f).ToString("F1")
                + "    " + Strings.EarthquakeRadius + ": " + primary.Radius.ToString("F0") + " m");

            RefreshPhaseRow(primary);
            RefreshTimeRow(snapshot, primary);
            RefreshCursorRow(primary, haveCursor, cursor);
            RefreshShakeRow(snapshot, primary, haveCursor, cursor);
            EarthquakeDamageRows.Refresh(snapshot, primary, haveCursor, cursor);
            return primary;
        }

        private static void RefreshPhaseRow(EarthquakeReading primary)
        {
            string word = null;
            switch (primary.Phase)
            {
                case EarthquakePhase.Emerging: word = Strings.EarthquakePhaseEmerging; break;
                case EarthquakePhase.Active: word = Strings.EarthquakePhaseActive; break;
                case EarthquakePhase.Clearing: word = Strings.EarthquakePhaseClearing; break;
            }

            // There is no wording for Finished / Unknown. Giving them a name would make
            // them look like "one of the phases in progress", so the whole row is omitted.
            EarthquakeRows.SetPlain(_phaseLabel, "");
            if (word != null)
            {
                EarthquakeRows.SetLayer1(_phaseLabel, Strings.EarthquakePhase + ": " + word);
            }
        }

        /// <summary>
        /// "Time to the main shock". **This is reading out a schedule, not a prediction.**
        ///
        /// ① forbids "it arrives in N hours" (because whether it happens is a random
        /// draw). ②'s row rests on something different: <c>m_activationFrame</c> is a
        /// **settled value written** by <c>StartDisaster</c> as
        /// <c>m_startFrame + m_emergingDuration</c> (§A-1), and <c>IsStillEmerging</c>
        /// merely compares it against the current frame. Design doc §7-2 requires this
        /// distinction.
        ///
        /// Note, though, that <c>m_activationFrame == 0</c> means "nothing is scheduled",
        /// not "now". An earthquake without <c>SelfTrigger(64)</c> stays at 0 here and
        /// sits in Emerging forever, so subtracting naively gives a number like "4,739
        /// years from now".
        /// </summary>
        private static void RefreshTimeRow(EarthquakeSnapshot snapshot, EarthquakeReading primary)
        {
            EarthquakeRows.SetPlain(_timeLabel, "");

            if (primary.Phase != EarthquakePhase.Emerging) return;

            if (!primary.ActivationScheduled)
            {
                EarthquakeRows.SetLayer1(_timeLabel,
                    Strings.EarthquakeTimeToShock + ": " + Strings.EarthquakeTimeUnknown);
                return;
            }

            if (primary.ActivationFrame <= snapshot.CurrentFrame) return;

            // Always derive the conversion from FeatureHost.FramesPerMinute (we once
            // hard-coded the constant and came out a factor of 4 wrong: ③, mixing up
            // DAYTIME_FRAMES).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            float minutes = (primary.ActivationFrame - snapshot.CurrentFrame) / framesPerMinute;
            EarthquakeRows.SetLayer1(_timeLabel, Strings.EarthquakeTimeToShock + ": "
                + minutes.ToString("F0") + " " + Strings.EarthquakeMinutes);
        }

        /// <summary>
        /// The local factor s at the cursor. It is **the whole-quake disc's
        /// (probability = 0.02) collapse and fire ramp**, and it is neither the shaking
        /// nor the fault band (both of which get rows of their own).
        ///
        /// ── What this row may and may not claim (whole-feature review C3) ───────
        ///
        /// The number (<c>s = 1 - d/R</c>) is vanilla's <c>fD</c> exactly, and it is
        /// correct. But this row used to call itself "shaking at the cursor" and write
        /// "outside the range of the shaking" beyond R. Vanilla's shaking is
        /// <c>amp = 0.3/(1 + dist*0.001)</c> with **no radius cut-off whatsoever** (§A-7),
        /// so that was a flatly contradictory claim about a point where, on the same
        /// frame, <c>CameraShakeBooster</c> was adding shake and
        /// <c>SeismographRecorder</c> was writing non-zero displacement.
        ///
        /// **Nor does it give a band name (weak / strong / …).** Those are names this mod
        /// invented; vanilla is only computing a coefficient on a probability. Put them
        /// under <c>[measured]</c> and it means the game is making that judgement (see
        /// the comment near <c>EarthquakeBandWeak</c> in <c>Strings</c>).
        ///
        /// Outside radius R the factor is not 0; vanilla is not even making the check, so
        /// we write "out of range" rather than <c>0.0</c> (see
        /// <see cref="SeismicIntensity.At"/>'s doc).
        /// </summary>
        private static void RefreshCursorRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            // ★ A subsiding (Clearing) earthquake runs no destruction check. The
            //    whole-quake disc's DestroyBuildings exists only in SimulationStep's
            //    Active branch (§A-3), so printing a factor here would be stating the
            //    strength of something that can no longer happen. The sim side
            //    (QuakeSelection.SelectDamaging) excludes Clearing for that same reason,
            //    so this removes the disagreement where only the display side included it.
            if (!QuakeSelection.RunsDamage(primary.Phase))
            {
                EarthquakeRows.SetPlain(_cursorLabel,
                    Strings.EarthquakeAtCursor + ": " + Strings.EarthquakeNoDamageInPhase);
                return;
            }

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_cursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            float distance = DistanceXZ(cursor, primary.Epicentre);
            if (!SeismicIntensity.IsInside(distance, primary.Intensity))
            {
                EarthquakeRows.SetLayer1(_cursorLabel,
                    Strings.EarthquakeAtCursor + ": " + Strings.EarthquakeOutOfRange);
                return;
            }

            float s = SeismicIntensity.At(distance, primary.Intensity);
            EarthquakeRows.SetLayer1(_cursorLabel, Strings.EarthquakeAtCursor + ": "
                + s.ToString("F2") + "  [" + SeismicScale.BarOf(s) + "]");
        }

        /// <summary>
        /// **How hard the ground is actually shaking at the cursor.** A different quantity
        /// from the collapse ramp above, and this is what the request meant by "the
        /// shaking".
        ///
        /// What is shown is <c>EarthquakeAI.RenderInstance</c>'s <c>amp</c> exactly
        /// (§A-7 IL_0069, before the envelope is applied). Vanilla measures that distance
        /// **from the camera**, whereas here it is evaluated at the distance from the
        /// epicentre — exactly the same substitution as the waveform graph, a different
        /// evaluation of the same formula rather than an approximation (design doc §3.5).
        ///
        /// **There is no radius cut-off.** Even 10 km away it is shaking at 9% of the
        /// epicentre's amplitude. That is what the permanent note
        /// (<c>EarthquakeShakeNote</c>) says.
        ///
        /// No number is shown unless the window (<c>Emerging|Active</c> and
        /// <c>0 &lt; e &lt; m_activeDuration</c>) is open. <c>m_activeDuration</c> is a
        /// prefab value nobody has yet measured, so no number is shown when it cannot be
        /// read either (the same judgement as <c>CameraShakeBooster</c> /
        /// <c>SeismographRecorder</c>).
        /// </summary>
        private static void RefreshShakeRow(EarthquakeSnapshot snapshot, EarthquakeReading primary,
                                            bool haveCursor, Vec3 cursor)
        {
            // "There is no radius cut-off" is most easily misread precisely when no number
            // is shown (because it sits next to the collapse ramp above saying "out of
            // range").

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_shakeLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                EarthquakeRows.SetPlain(_shakeLabel,
                    Strings.EarthquakeShakeAtCursor + ": " + Strings.EarthquakeUnavailable);
                return;
            }

            long e = (long)snapshot.CurrentFrame - primary.ActivationFrame
                     + ShakeWaveform.FrameOffset;
            if (!primary.ActivationScheduled
                || !QuakeSelection.RunsDamage(primary.Phase)
                || !ShakeWaveform.IsShaking(e, snapshot.Prefab.ActiveDuration))
            {
                EarthquakeRows.SetPlain(_shakeLabel,
                    Strings.EarthquakeShakeAtCursor + ": " + Strings.EarthquakeNotShaking);
                return;
            }

            float amplitude = ShakeWaveform.PeakAmplitudeAt(
                DistanceXZ(cursor, primary.Epicentre));
            EarthquakeRows.SetLayer1(_shakeLabel, Strings.EarthquakeShakeAtCursor + ": "
                + amplitude.ToString("F3") + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                + "  [" + SeismicScale.BarOf(
                    ShakeWaveform.NormalisedDisplacement(amplitude)) + "]");
        }

        /// <summary>
        /// Picks the one earthquake the display refers to. **So that every row refers to
        /// the same earthquake**, preventing the crossed wires of "the intensity is
        /// earthquake A's and the shaking at the cursor is earthquake B's" (§E-1 settles
        /// that several can run at once). In the ordinary case of a single earthquake,
        /// nothing happens here.
        ///
        /// Priority: in progress &gt; acting strongly at the cursor &gt; higher intensity
        /// &gt; lower index. "In progress" is checked first so that the remains of a
        /// Finished quake never become the subject.
        ///
        /// ── Including <c>Clearing</c> here is deliberate (whole-feature review I2) ────
        ///
        /// The sim side's <see cref="QuakeSelection.SelectDamaging"/> does not include
        /// <c>Clearing</c> (the destruction check exists only in the <c>Active</c> branch,
        /// §A-3). This selection is for showing **the count, the intensity and the
        /// phase**, so a subsiding earthquake can be the subject too — not being able to
        /// display "aftershocks subsiding" would be a loss of information.
        ///
        /// **Instead, rows derived from destruction are not shown in phases where no
        /// destruction runs.** <see cref="RefreshCursorRow"/> and
        /// <see cref="EarthquakeDamageRows"/> bow out of their own accord via
        /// <see cref="QuakeSelection.RunsDamage"/>. Those two rows used to print a factor
        /// and "fault band: inside" for a subsiding earthquake.
        /// </summary>
        private static EarthquakeReading SelectPrimary(IList<EarthquakeReading> quakes,
                                                       bool haveCursor, Vec3 cursor)
        {
            EarthquakeReading best = null;
            bool bestInProgress = false;
            float bestFactor = 0f;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                bool inProgress = q.Phase == EarthquakePhase.Emerging
                                  || q.Phase == EarthquakePhase.Active
                                  || q.Phase == EarthquakePhase.Clearing;
                float factor = haveCursor
                    ? SeismicIntensity.At(DistanceXZ(cursor, q.Epicentre), q.Intensity)
                    : 0f;

                bool better;
                if (best == null) better = true;
                else if (inProgress != bestInProgress) better = inProgress;
                else if (factor != bestFactor) better = factor > bestFactor;
                else better = q.Intensity > best.Intensity;

                if (!better) continue;
                best = q;
                bestInProgress = inProgress;
                bestFactor = factor;
            }

            return best;
        }

        private static float DistanceXZ(Vec3 a, Vec3 b)
        {
            return Mathf.Sqrt(a.ToVec2().DistanceSquaredTo(b.ToVec2()));
        }

        // The helper that turned band names (weak / moderate / strong / very strong) into
        // strings here was removed by the whole-feature review (C3). Those are **names
        // this mod invented**, whereas what vanilla computes is only a coefficient on a
        // probability. Put them under the [measured] prefix and it means the game is
        // making that judgement. SeismicScale.BandOf and Strings.EarthquakeBand* are kept
        // for when layer 2 declares them as names of its own.
    }
}
