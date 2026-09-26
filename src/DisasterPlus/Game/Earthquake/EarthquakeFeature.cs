using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Feature ②, earthquakes. It **visualises as it stands** the deterministic intensity
    /// model vanilla already has (a linear ramp in epicentral distance, plus a random
    /// threshold fixed per building).
    ///
    /// As of this task (Task 3) it is **a feature with no panel**. All it does is read on
    /// the sim thread, publish to <see cref="EarthquakeHub"/>, and **print the four prefab
    /// values in the diagnostic dump**. Those four (<c>m_crackLength</c>,
    /// <c>m_crackWidth</c>, <c>m_emergingDuration</c>, <c>m_activeDuration</c>) have
    /// **no actual values anywhere in the DLL** (IL facts doc §A-0), and every later
    /// duration design in ② rests on them, so we measure them once in the running game
    /// first.
    ///
    /// It implements <see cref="IPausedTickFeature"/> for the same reason as ① (opening
    /// the panel while still paused right after a load would make every row say "cannot
    /// be read"). **But ② will eventually advance game state** (Task 9's tsunami and
    /// Task 10's extra damage). The machinery that keeps that contract is inside
    /// <see cref="OnSimulationTick"/>.
    /// </summary>
    public class EarthquakeFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Earthquake";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            EarthquakeHub.Clear();
            EarthquakeReader.Reset();
            CameraShakeBooster.Reset();
            SeismographRecorder.Reset();
            // ★ A schedule never survives from one city to the next (layer 2 is session
            //   state and is not put in the save either).
            TsunamiChain.Reset();
            // ★★ Do not carry the trench earthquake's disaster ID over either. Carry it
            //    and in the next city a vanilla earthquake that takes the same number is
            //    mistaken for a trench quake and gets a tsunami.
            TrenchQuakeSlot.Reset();
            // ★★ **Always release any water waves placed.** A WaterWave has a Serialize
            //    and is held by DisasterData.m_waveIndex, i.e. it is baked into the save,
            //    so one left behind stays in the city even after the mod is removed (see
            //    TsunamiWave's class doc).
            TsunamiWave.Reset();
            TsunamiRing.Reset();
            SeaWatch.Reset();
            LongPeriodDamage.Reset();
            TrenchQuakeDistantDamage.Reset();

            // ★★ **ToolController is rebuilt for every city**, so re-register on every
            //    level load. Forget to and you get a breakage with no exception: "the
            //    tile can be clicked but the cursor never changes" (see
            //    <c>TrenchQuakePlacementTool</c> / ⑤'s class doc).
            ToolRegistration.Register<TrenchQuakePlacementTool>();

            // ★★ The trench earthquake depends on a Harmony patch
            //    (<c>TrenchQuakeNoCrackPatch</c>: do not crack the ground).
            //    <c>Install</c> is idempotent, so calling it here as well as in ③ is fine
            //    — **we call it here so that ② does not silently break the day ③ is
            //    removed.**
            HarmonyBootstrap.Install();

            // The seismic-intensity overlay. **Main thread.** Registering adds to a static
            // list on RenderManager, and there is no API to remove one (see
            // OverlayRenderable's class doc), so this call only has an effect once per
            // process. In later cities it only re-raises "drawing is allowed in this
            // session".
            EarthquakeOverlay.EnsureRegistered();
        }

        /// <summary>
        /// Sim thread. Reads of <c>DisasterManager</c>,
        /// <c>ImmaterialResourceManager</c> and <c>SimulationManager</c> always happen
        /// here.
        ///
        /// It is called while paused too (deltaMinutes == 0; see
        /// <see cref="IPausedTickFeature"/>).
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            // ★★ **The tsunami's force alone is always advanced before the settings are
            //    consulted.** (2026-08-30, final verification) Put it below this point and
            //    <c>TsunamiWave.Tick</c> stops being called if the setting is switched off
            //    mid-tsunami. Once the rewriting stops, the solver releases the water wave
            //    within 3 water steps, leaving our register alone holding what it thinks
            //    is a live handle — and that slot gets reused by <c>SplashWater</c> (the
            //    water plumes from meteors and earthquakes), so the next write <b>tramples
            //    somebody else's wave</b>.
            //    It returns immediately when nothing is running, so it is a no-op.
            // ★ Nobody calls Begin on the old TYPE_IMPACT wave, so its Tick would swing at
            //   nothing forever. We stop calling it (see TsunamiWave's class doc: all that
            //   is kept there is DepthAt, and Reset for cities where the old version's
            //   waves survive).

            // ★★ **The real thing that rises in circles from the hypocentre.**
            //    (2026-08-31, the owner's instruction) Unlike <c>TsunamiWave</c>
            //    (TYPE_IMPACT's hill), this one <b>creates water</b>, so the wave does not
            //    collapse before it gets far. It is advanced before the settings for the
            //    same reason as above — stop it and the water source is left in place,
            //    <b>baked into the save and pouring water out forever</b> (see
            //    TsunamiRing's class doc).
            TsunamiRing.Tick(frameIndex);

            // ★★ **The ruler for the whole sea.** (2026-08-31, the owner's suggestion)
            //    It prints <b>the same one line</b> for vanilla's tsunami and for ours.
            //    It is tied to no setting and no feature — comparison is the whole point.
            SeaWatch.Tick(frameIndex);

            // ★★ **Forget a trench quake that has ended.** (2026-08-30, fourth round of
            //    verification) <c>IsTrenchQuake</c> forgets of its own accord once the
            //    slot is free, but the only callers are the Harmony prefix and
            //    <c>TsunamiChain</c>, and <b>both of them only look at live disasters</b>.
            //    So <c>LastId</c> never returned to 0 for the rest of the session, and the
            //    diagnostics went on saying "running for a trench quake".
            //    Check it once per tick (one array read, so effectively free).
            TrenchQuakeSlot.IsTrenchQuake(TrenchQuakeSlot.LastId);

            // ★★ **Once a trench quake has been placed, the panel's setting must not
            //    stop it.** (third round of verification) <c>EarthquakeEnabled</c> is the
            //    setting for "show the earthquake panel", but returning early here also
            //    skips right past <c>TsunamiChain.Tick</c>. The tile
            //    (<c>DisasterPanelBar</c>), meanwhile, only looks at
            //    <c>TrenchQuakeEnabled</c>, so you got an unexplained breakage: <b>the
            //    tile works, the earthquake happens, the fault is suppressed — and the
            //    tsunami alone never comes.</b>
            if (!ModSettings.EarthquakeEnabled.value && TrenchQuakeSlot.LastId == 0) return;

            // Everything up to here is "just read and publish". This is reached while
            // paused too.
            var snapshot = EarthquakeReader.Read();
            EarthquakeHub.Publish(snapshot);

            // The Earthquake channel is off by default. Without this `if`, the ToString
            // calls and string concatenation below run every sim tick (roughly 50 times a
            // second at normal speed) and are then thrown away by Log.Diag — C# evaluates
            // the arguments fully before the call, so the mask check inside Diag comes too
            // late.
            //
            // Unlike ①'s ForecastFeature, this must not be an early return. That would
            // skip the pause guard below and all of layer 2's processing.
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "earthquake",
                    snapshot.Valid
                        ? "quakes=" + snapshot.Quakes.Count
                          + " hour=" + snapshot.HourOfDay.ToString("F1")
                          + " dayNight=" + (snapshot.DayNightEnabled ? "on" : "off")
                        : "snapshot invalid");
            }

            // ★ Everything below this advances state. Never let it through while paused
            //    (deltaMinutes == 0). Anything Tasks 9 and 10 add must go below this line.
            //    Delete this comment and you get "earthquake damage advances while the
            //    game is paused".
            if (deltaMinutes <= 0f) return;

            // Take one sample at each seismograph's position. **Always below the pause
            // guard.** No game time passes while paused, so no ground motion does either,
            // and accumulating here would make the waveform alone grow — a lie.
            SeismographRecorder.Sample(snapshot, frameIndex);

            // ★ Layer 2. **Off by default** (see ModSettings.EarthquakeTsunamiChain's
            //    doc). Checking the setting before calling means that when it is off,
            //    TsunamiChain's state stays Idle and never advances at all, so no section
            //    appears in the panel either.
            // ★★ **A trench earthquake always chains, regardless of the setting.**
            //    (2026-08-25, from the game: "I want the tsunami to happen right after a
            //    trench earthquake, but it does not")
            //
            //    <c>eqTsunamiChain</c> is <b>off by default</b>. That dates from when it
            //    was the setting for "should vanilla earthquakes get a tsunami too", and
            //    leaving it as it was meant **it was silently stopping the newly added
            //    trench earthquake as well.**
            //
            //    A trench earthquake is <b>a disaster that exists solely to bring a
            //    tsunami</b>. Put a switch that is off by default in front of that and the
            //    default behaviour becomes "click the tile and nothing happens" — which is
            //    broken as a design.
            //
            //    ★ The old setting **still has no effect on vanilla earthquakes**
            //      (TsunamiChain's PickCandidate only picks trench quakes). It is kept as
            //      a way out for anyone who wants to turn the tsunami off even for trench
            //      quakes.
            if (ModSettings.EarthquakeTsunamiChain.value
                || TrenchQuakeSlot.LastId != 0)
            {
                TsunamiChain.Tick(snapshot, frameIndex);
            }

            // ★ Layer 2, part 2. **Off by default** (see
            //    ModSettings.EarthquakeLongPeriod's doc). Unlike the tsunami, this
            //    **actually brings down buildings that would have survived in vanilla**.
            //    The setting is checked before calling, so when it is off the sweep never
            //    runs even once.
            if (ModSettings.EarthquakeLongPeriodStrength.value > 0)
            {
                LongPeriodDamage.Apply(snapshot, deltaMinutes);
            }

            // ★★ The trench quake's distant damage. **It only affects trench
            //    earthquakes**, so it has no checkbox of its own and is controlled by the
            //    strength slider alone (see
            //    ModSettings.EarthquakeTrenchDamageStrength's doc).
            //    At 0 the sweep never runs even once.
            TrenchQuakeDistantDamage.Apply(snapshot, deltaMinutes);
        }

        /// <summary>Main thread. Creating the panel and buttons, and updating content only while visible, all start here.</summary>
        public void OnMainThreadUpdate()
        {
            // The buttons are owned as a set of four by DisasterPanelBar (called by
            // FeatureHost).
            EarthquakePanel.Tick();

            // ★ Always call this, even with the panel closed. The camera shake is not
            //    something the panel displays; it is a value the game consumes and resets
            //    every frame (§A-7: the last line of CameraController.LateUpdate writes
            //    Vector3.zero), so it has no effect unless we keep adding every frame.
            CameraShakeBooster.Update();
        }

        public void OnLevelUnloading()
        {
            EarthquakeHub.Clear();
            EarthquakeReader.Reset();
            // Stop adding shake. Vanilla resets m_cameraShake to zero every frame, so
            // stopping here leaves no residual offset (§A-7).
            CameraShakeBooster.Reset();
            // The waveforms are **carried over neither into the save nor into the next
            // city** (design doc §3.5).
            SeismographRecorder.Reset();
            // ★ Do not carry a tsunami that was scheduled and never fired across cities.
            //    Forget this and the second city gets a tsunami from an earthquake that
            //    never happened.
            TsunamiChain.Reset();
            // ★ Clear the sweep's partial state, the diagnostic counters, and the
            //   self-reported degraded state.
            LongPeriodDamage.Reset();
            TrenchQuakeDistantDamage.Reset();
            // ★ Stop the overlay. The registration cannot be removed, so we guarantee it
            //    draws nothing through our own state (see EarthquakeOverlay.Reset's doc).
            //    Forget this and the previous city's epicentre is drawn on the map for
            //    the first few frames after leaving it.
            EarthquakeOverlay.Reset();
            // ★ Do not carry the trench earthquake's disaster ID over. Carry it and in the
            //   next city a vanilla earthquake that takes the same number is mistaken for
            //   a trench quake and gets a tsunami.
            TrenchQuakeSlot.Reset();
            // ★★ **This is the last line of defence.** Without releasing the water waves
            //    placed, they stay in the save.
            TsunamiWave.Reset();
            // ★★ The water source is worse still — unlike a WaterWave it **has no
            //    lifetime**, so one left behind pours water into that city forever (see
            //    TsunamiRing's class doc §3).
            TsunamiRing.Reset();
            SeaWatch.Reset();
            // Make sure the second city starts with one button and one panel.
            // EarthquakePanel.Destroy() also destroys the waveform texture (a Texture2D) —
            // unlike a GameObject, Unity does not collect it by itself, so forgetting this
            // leaves one more 320x80 texture behind with every city change.
            // The button is removed by FeatureHost.LevelUnloading via
            // DisasterPanelBar.Remove.
            EarthquakePanel.Destroy();
        }

        /// <summary>
        /// **This task's main purpose.** Prints the four prefab values, the sim thread's
        /// clock and the raw values of the earthquakes in progress, all as they stand.
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.EarthquakeEnabled.value ? "yes" : "no");

            // ★★ Say in the diagnostics that **only a trench quake brings a tsunami**.
            //    Without saying so, "I raised an earthquake but no tsunami came" is read
            //    as a fault.
            b.Line(1, "trench quake", ModSettings.TrenchQuakeEnabled.value
                ? (TrenchQuakeSlot.LastId != 0
                    ? "last raised as disaster " + TrenchQuakeSlot.LastId + " at ("
                      + TrenchQuakeSlot.Epicentre.X.ToString("F0") + ","
                      + TrenchQuakeSlot.Epicentre.Z.ToString("F0") + "), "
                      + TrenchQuakeSlot.SearchDistanceMetres.ToString("F0")
                      + " m from the point that was clicked"
                    : "tile shown; none raised yet")
                  + (TrenchQuakeSlot.Detail != null
                     ? "  (last refusal: " + TrenchQuakeSlot.Detail + ")" : "")
                : "off (setting)");

            // ★ Not cracking the ground is **deliberate**. Without saying so, "no fault
            //   line appears" is read as a fault (and conversely, when one does appear,
            //   this number stays at 0).
            b.Line(2, "terrain crack", HarmonyBootstrap.Installed
                ? "suppressed for trench quakes (" + TrenchQuakeStepPatch.SuppressedCracks
                  + " skipped so far); the game's own earthquakes still crack normally"
                : "NOT SUPPRESSED - Harmony is not installed, so a trench quake will "
                  + "open a fissure like a fault quake");

            // ★★ The tsunami is not the DLC's TsunamiAI but a WaterSource (TYPE_NATURAL)
            //    placed on the hypocentre. Without saying **which of the two is running**,
            //    nobody can investigate it.
            b.Line(2, "tsunami", TsunamiRing.Running
                ? "the sea over the epicentre is being held "
                  + TsunamiRing.OffsetMetres.ToString("F1")
                  + " m from normal (" + TsunamiRing.ElapsedSteps + " of "
                  + TsunamiRing.TotalSteps + " water steps); water is "
                  + TsunamiRing.DepthMetres.ToString("F1")
                  + " m deep at the epicentre; the highest it has been held is "
                  + TsunamiRing.PeakRiseMetres.ToString("F1") + " m"
                : "not running"
                  + (TsunamiRing.Detail != null
                     ? " (" + TsunamiRing.Detail + ")" : ""));

            b.Line(2, "note: tsunami",
                   "ONLY a trench quake brings a tsunami. The game's own (fault) "
                   + "earthquakes never do - that is deliberate, not a fault. "
                   + "The DLC TsunamiAI is NOT used: it can only start a wave from the "
                   + "map edge, never from an offshore epicentre (IL: WaterWave."
                   + "GetSeaLevel is called from the outer-ring loop only). Instead a "
                   + "WaterSource of the kind that feeds the map's own rivers is placed "
                   + "ON the epicentre, and its target sea level is driven with the DLC's "
                   + "own waveform - retreat, crest, retreat. That kind of source MAKES "
                   + "water rather than pushing existing water around, which is why the "
                   + "wave still stands up when it reaches the coast. The source stops "
                   + "after " + TsunamiRing.TotalSteps + " water steps ("
                   + (TsunamiRing.TotalSteps * 64 / 3600f).ToString("F0")
                   + " real minutes); everything after that is the game's own water "
                   + "solver, the same one that carries the DLC tsunami");

            var snapshot = EarthquakeHub.Latest;
            b.Line(1, "snapshot", snapshot == null ? "none yet" : (snapshot.Valid ? "valid" : "INVALID"));

            // The UI state is printed whether or not there is a snapshot. Investigating
            // "the panel does not open" or "the button ended up on top of the forecast
            // button" does not need an earthquake to be happening.
            WriteUiState(b, snapshot);

            if (snapshot == null || !snapshot.Valid) return;

            WritePrefabFacts(b, snapshot.Prefab);
            WriteShakeBoost(b, snapshot);
            WriteSimClock(b, snapshot);
            WriteSensorCoverage(b, snapshot);
            WriteWaveform(b, snapshot);
            WriteTsunamiChain(b, snapshot);
            WriteLongPeriod(b, snapshot);
            WriteTrenchDistant(b);
            WriteQuakes(b, snapshot);
            WriteNotes(b);
        }

        /// <summary>
        /// **Where the explanations taken off the settings screen ended up** (the table in
        /// <c>Mod.OnSettingsUI</c>'s doc).
        ///
        /// The owner's instruction was "the Options screen has too much explanatory text
        /// as well". What a setting does is already named by the checkbox's label, so only
        /// **the facts of the form "this is what vanilla does"** come here. This file is
        /// what testers and fault reports read; it is not where somebody choosing a
        /// setting reads.
        ///
        /// ★ This is the sim thread (<c>DiagnosticDump</c>'s class doc). Touch neither the
        ///   game's buffers nor the UI — keep it to lines of constants only.
        /// </summary>
        private static void WriteNotes(DiagnosticBuilder b)
        {
            b.Line(1, "note: camera shake",
                "vanilla ignores intensity in the camera shake, so a 25.5 quake shakes "
                + "exactly as much as a 5.5 one. At the vanilla default intensity (55 raw, "
                + "shown as 5.5) the mod's addition is exactly zero, which is why the option "
                + "can default to on (design appendix A-7)");
            b.Line(1, "note: seismogram",
                "vanilla shakes the camera with two fixed sine waves that never arrive, never "
                + "build and never decay. The seismogram option replaces that pattern with a "
                + "synthesized record. It is Disaster +'s own model, not anything the game "
                + "computes, so it is off by default");
            b.Line(1, "note: long-period",
                "vanilla ignores building height entirely, both in the shaking and in the "
                + "damage. Long-period motion is a model Disaster + invented and it collapses "
                + "buildings vanilla would not, so it is off by default");
        }

        /// <summary>
        /// The trench quake's distant damage. **Printing every number when both the
        /// collapses and the fires are 0** is the reason this section exists — "the
        /// feature is dead" and "there are no buildings in range" both wear the same face
        /// on screen: "nothing happens".
        /// </summary>
        private static void WriteTrenchDistant(DiagnosticBuilder b)
        {
            int strength = ModSettings.EarthquakeTrenchDamageStrength.value;
            if (strength <= 0)
            {
                b.Line(1, "trench distant damage",
                    "off (strength slider is 0; trench quakes fall back to the vanilla disc, "
                    + "which is centred out at sea and therefore barely reaches the city)");
                return;
            }

            b.Line(1, "trench distant damage",
                "on, strength " + strength + " of 10  (trench quakes only; "
                + "fault quakes and vanilla quakes are untouched)");

            b.Line(2, "model", "reach = " + DistantDamage.ReachFactor.ToString("F0")
                + "x the vanilla disc, floor "
                + (DistantDamage.FloorFraction * 100f).ToString("F0")
                + "% of the near field, magnitude (i/255)^2, ceilings "
                + (DistantDamage.MaxCollapseChance * 100f).ToString("F0") + "% collapse / "
                + (DistantDamage.MaxFireChance * 100f).ToString("F0") + "% fire");

            b.Line(2, "passes", TrenchQuakeDistantDamage.Passes.ToString());
            b.Line(2, "last pass",
                "scanned=" + TrenchQuakeDistantDamage.LastScanned
                + " collapsed=" + TrenchQuakeDistantDamage.LastCollapsed
                + " ignited=" + TrenchQuakeDistantDamage.LastIgnited
                + " refused=" + TrenchQuakeDistantDamage.LastRefused
                + (TrenchQuakeDistantDamage.LastCapped
                    ? "  (capped; resumes next pass)" : ""));
            b.Line(2, "total", "collapsed=" + TrenchQuakeDistantDamage.TotalCollapsed
                + " ignited=" + TrenchQuakeDistantDamage.TotalIgnited);
        }

        /// <summary>
        /// The state of layer 2 (long-period ground motion). **Telling "did extra
        /// buildings come down" apart can only be done here.** There are six reasons
        /// nothing falls (the setting is off / strength 0 / the quake in progress is not
        /// Active / there are no tall buildings in range / the height cannot be read /
        /// vanilla refused the collapse), and on screen every one of them wears the same
        /// face: "nothing happens".
        ///
        /// **Always print every number, including when the collapse count is 0** (so as
        /// not to repeat ③'s failure, where "is the fire spread working" was completely
        /// invisible from the diagnostics).
        /// </summary>
        private static void WriteLongPeriod(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            if (ModSettings.EarthquakeLongPeriodStrength.value <= 0)
            {
                b.Line(1, "long period", "off (setting; this is the default)");
                return;
            }

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            b.Line(1, "long period", strength <= 0
                ? "on, but the strength slider is 0 (nothing is added; this is a valid way to "
                  + "disable it without losing the setting)"
                : "on, strength " + strength + " of 10");

            b.Line(2, "model", "wave period "
                + LongPeriodResponse.WavePeriodFrames.ToString("F0")
                + " frames, resonance peak at height "
                + (LongPeriodResponse.WavePeriodFrames
                   / LongPeriodResponse.PeriodFramesPerMetre).ToString("F0")
                + " m, range = " + LongPeriodResponse.RangeFactor.ToString("F0")
                + "x the vanilla disc, ceiling "
                // ★ Do not write just 0.25. The time-of-day factor is applied **after**
                //    the model's clamp, so the ceiling actually used is 0.25 x 1.15
                //    (LongPeriodResponse.MaxExtraChance's doc / layer 2 review M8).
                + (LongPeriodResponse.MaxExtraChance * 100f).ToString("F1") + "% x up to "
                + TimeOfDayFactor.NightFactor.ToString("F2") + " time-of-day = "
                + (LongPeriodResponse.MaxExtraChance * TimeOfDayFactor.NightFactor * 100f)
                    .ToString("F2") + "% per pass"
                + "  [Disaster + model, not measured]");

            b.Line(2, "passes", LongPeriodDamage.Passes.ToString());
            b.Line(2, "last pass",
                "scanned=" + LongPeriodDamage.LastScanned
                + " selected=" + LongPeriodDamage.LastSelected
                + " attempted=" + LongPeriodDamage.LastAttempted
                + " refused=" + LongPeriodDamage.LastRefused
                + " collapsed=" + LongPeriodDamage.LastCollapsed
                + (LongPeriodDamage.LastCapped ? "  (capped; resumes next pass)" : ""));
            b.Line(2, "total collapsed", LongPeriodDamage.TotalCollapsed.ToString());

            // Nothing is done to a building whose height cannot be read. That this is not
            // 0 is itself the signal.
            b.Line(2, "unreadable height", LongPeriodDamage.LastUnknownHeight
                + (LongPeriodDamage.LastUnknownHeight > 0
                    ? "  (these buildings were skipped entirely; the mod never guesses a height)"
                    : ""));

            b.Line(2, "cursor building height", snapshot.CursorBuildingHeight > 0f
                ? snapshot.CursorBuildingHeight.ToString("F1") + " m"
                : "unread (no building under the cursor, or its prefab height is unusable)");

            WriteTimeOfDay(b, snapshot);
        }

        /// <summary>
        /// The time-of-day factor (layer 2, part 3). It applies only to the long-period
        /// extra damage, so it is called from inside <see cref="WriteLongPeriod"/> (it has
        /// no setting of its own).
        ///
        /// **Do not hide the day/night cycle being off.** With that setting the sim
        /// thread's hour is pinned at 12.0 forever (§F-1) and the factor quietly becomes a
        /// constant 1.00. That is not a broken assumption but a valid player setting, so
        /// it is not made an <c>Assumptions</c> FAIL — but **always say that it has
        /// silently been disabled**. Print "factor 1.00" on its own and it looks like a
        /// value that was read.
        /// </summary>
        private static void WriteTimeOfDay(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            float hour = snapshot.HourOfDay;
            b.Line(2, "time of day factor",
                TimeOfDayFactor.Of(hour).ToString("F2")
                + "  (hour=" + hour.ToString("F1")
                + " night=" + (TimeOfDayFactor.IsNight(hour) ? "yes" : "no")
                + ", day " + TimeOfDayFactor.DayFactor.ToString("F2")
                + " -> night " + TimeOfDayFactor.NightFactor.ToString("F2")
                + "  [Disaster + model, vanilla has no basis for this])");

            if (!snapshot.DayNightEnabled)
            {
                b.Line(3, "day/night",
                    "OFF: the game pins the hour at 12.0 every sim frame, so this factor is "
                    + "permanently 1.00 and the time of day changes nothing. This is a valid "
                    + "player setting, not a broken assumption");
            }
        }

        /// <summary>
        /// The state of layer 2 (the tsunami chain). **Telling "no tsunami came" apart can
        /// only be done here.** There are five reasons it does not come (the setting is
        /// off / the hypocentre is on land / no DLC / not enough sea on the outer ring /
        /// the disaster slots are full), and on screen every one of them wears the same
        /// face: "nothing happens".
        ///
        /// Say here too, in words, that **<c>NoSea</c> on an inland map is not a failure**
        /// (§B-3).
        /// </summary>
        private static void WriteTsunamiChain(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            // ★★ **Decide it with the same expression as the tick.** (2026-08-30, third
            //    round of verification) Here alone the setting was the only thing looked
            //    at, so <b>in the default configuration it wrote "off" while it was
            //    actually running</b> — because a trench earthquake chains regardless of
            //    the setting (the ★★ at :132).
            //    "Writing off while it is running" is the output this mod hates most.
            if (!ModSettings.EarthquakeTsunamiChain.value
                && TrenchQuakeSlot.LastId == 0)
            {
                b.Line(1, "tsunami chain",
                       "off (setting; this is the default). A trench earthquake would "
                       + "still bring a tsunami - that path ignores this setting");
                return;
            }

            if (!ModSettings.EarthquakeTsunamiChain.value)
            {
                b.Line(1, "tsunami chain source",
                       "running for the trench earthquake even though the setting is off "
                       + "- a trench quake exists only to bring a tsunami");
            }

            string state;
            switch (snapshot.TsunamiState)
            {
                case TsunamiChainState.Scheduled:
                    state = "scheduled for frame " + snapshot.TsunamiDueFrame;
                    break;
                case TsunamiChainState.Raised:
                    state = "raised (a wave was actually created)";
                    break;
                case TsunamiChainState.NoSea:
                    // ★★ **Always name the reason.** (2026-08-31, cross-verification)
                    //    For a long time this read <c>TsunamiWave.Detail</c>, but nobody
                    //    calls that path any more, so it was <b>always null</b> and it
                    //    went on saying "this is an inland map" whatever the cause — the
                    //    worst line in the file, sending somebody who was refused out in
                    //    deep water offshore in exactly the opposite direction.
                    state = "no wave: " + (TsunamiRing.Detail
                            ?? "the wave could not be raised")
                            + ". Only 'the epicentre is not in the sea' means an inland "
                            + "map; the other reasons are real faults";
                    break;
                case TsunamiChainState.NoDlc:
                    state = "no TsunamiAI prefab (the Natural Disasters DLC is not owned)";
                    break;
                case TsunamiChainState.Failed:
                    state = "FAILED (see the EqTsunami* diagnostic lines; the disaster buffer "
                            + "may be full)";
                    break;
                default:
                    state = "idle (no undersea main shock has been observed)";
                    break;
            }

            b.Line(1, "tsunami chain", state);
            b.Line(2, "watching quake", snapshot.TsunamiQuakeId == 0
                ? "none"
                : "#" + snapshot.TsunamiQuakeId);
            b.Line(2, "delay setting",
                ModSettings.EarthquakeTsunamiDelayMinutes.value
                + " in-game minutes - but a TRENCH quake CAPS it at "
                + TsunamiChain.TrenchDelayMinutes
                + " (the epicentre is just offshore, so the first wave is minutes away, "
                + "not half an hour). A trench quake is the only kind that gets a "
                + "tsunami, so any setting above " + TsunamiChain.TrenchDelayMinutes
                + " changes nothing; below it the setting is used as-is");
        }

        /// <summary>
        /// The waveform's recording state. **Telling "no graph appears" apart can only be
        /// done here.** There are four reasons it does not appear (there is no earthquake
        /// to record / there are 0 seismographs / there are still 0 samples / the drawing
        /// path is unusable), and on screen every one of them wears the same face: "there
        /// is no picture".
        ///
        /// The <c>rendering</c> line reads here (on the sim thread) a value the main
        /// thread wrote. It is a bool that is decided once at build time and never
        /// changes again, so it is not put on the snapshot path (the same judgement as
        /// <c>CameraShakeBooster.LastAdded</c>).
        /// </summary>
        private static void WriteWaveform(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            var traces = snapshot.Traces;

            b.Line(1, "waveform", snapshot.WaveformQuakeId == 0
                ? "not recording (no Emerging/Active quake)"
                : "recording quake #" + snapshot.WaveformQuakeId
                  + ", " + traces.Count + " observation point(s)"
                  + (traces.Count == 0
                      ? "  (no Earthquake Sensor exists; the game keeps no ground-motion history "
                        + "of its own, so there is nothing else to plot)"
                      : ""));

            // ★ Whether there is a second line. **Telling "the orange line does not
            //   appear" apart can only be done here.** There are two reasons (the setting
            //   is off / it is recording but there are still 0 samples), and on screen
            //   both wear the same face: "there is only one line".
            b.Line(2, "synthesized line",
                !ModSettings.EarthquakeSeismogram.value
                    ? "off (setting)"
                    : (traces.Count > 0 && traces[0].HasModel
                        ? "on, " + traces[0].Count + " sample(s)  [Disaster + model, not measured]"
                        : "on, but nothing recorded yet"));

            // ★ Do not write "not built yet" as "unusable" (whole-mod review I6).
            //    It used to be a single bool, so a dump taken right after start-up, with
            //    the panel never once opened, claimed "cannot draw (falls back to the peak
            //    amplitude row)".
            //    This one line is the only clue for telling them apart, so all four states
            //    are printed as they are.
            string rendering;
            switch (WaveformView.State)
            {
                case WaveformViewState.Ready:
                    rendering = "UITextureSprite + Texture2D";
                    break;
                case WaveformViewState.BuildFailed:
                    rendering = "build failed (falls back to the peak amplitude row)";
                    break;
                case WaveformViewState.RenderFailed:
                    rendering = "drawing stopped after a runtime error "
                                + "(falls back to the peak amplitude row)";
                    break;
                default:
                    rendering = "not built yet (the earthquake panel has never been opened)";
                    break;
            }
            b.Line(2, "rendering", rendering);

            for (int i = 0; i < traces.Count; i++)
            {
                var t = traces[i];
                b.Line(2, "#" + t.BuildingId,
                    "distance=" + t.DistanceToEpicentre.ToString("F0") + "m"
                    + " samples=" + t.Count
                    + " newestFrame=" + t.NewestFrame
                    + " peak=" + t.PeakAbsolute.ToString("F3"));
            }
        }

        /// <summary>
        /// The seismograph coverage at the cursor. **It is a different thing from the
        /// coverage at the epicentre**, and it is the epicentre's that decides the warning
        /// lead time (§A-2). The epicentre's value, and the lead time that follows from
        /// it, are printed per earthquake by <see cref="WriteQuakes"/>.
        ///
        /// Do not mix "could not be read" in with 0. A coverage of 0 is a meaningful
        /// measured value — "no seismograph reaches here" — and it is the only grounds
        /// there are for explaining why the hazard map is empty.
        /// </summary>
        private static void WriteSensorCoverage(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            b.Line(1, "sensor coverage at cursor", snapshot.CursorCoverageValid
                ? snapshot.CursorCoverage.ToString()
                : "unread (no valid cursor point, or the resource could not be read)");
        }

        /// <summary>
        /// The state of the camera shake boost. **The only way to confirm it is working in
        /// the real game.** Watching the screen shake, the eye cannot tell "shaking with
        /// the intensity in it" from "vanilla's shaking", and on top of that, at intensity
        /// 55 the addition being exactly 0 is **correct** — that is, the state where
        /// "nothing happens" is the specification and the state where the feature is
        /// silently dead look completely identical. So it says so with a number.
        ///
        /// <c>added</c> reads here (on the sim thread) a value the main thread wrote. It
        /// is a single display-only float, and reading it late does not break its meaning,
        /// so it is not put on the snapshot path.
        /// </summary>
        private static void WriteShakeBoost(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            if (!ModSettings.EarthquakeShakeBoost.value)
            {
                b.Line(1, "camera shake boost", "off (setting)");
                return;
            }

            // DisasterManager belongs to the sim thread, so reading it here is right.
            string state = "on";
            if (ColossalFramework.Singleton<DisasterManager>.exists
                && ColossalFramework.Singleton<DisasterManager>.instance.m_disableCameraShake)
            {
                state = "on, but the game's 'disable camera shake' option wins (nothing is added)";
            }
            else if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                state = "on, but m_activeDuration is unreadable (nothing is added: the shaking "
                        + "window is unknown, and guessing it would keep shaking after the quake ends)";
            }

            b.Line(1, "camera shake boost", state);
            b.Line(2, "added last frame", CameraShakeBooster.LastAdded.ToString("F3"));

            // ★ The synthesized record replaces **the shape of the camera shake itself**
            //   (it cancels vanilla's term out and adds its own). Whether it is on or off
            //   changes what "added last frame" means, so always print it alongside — so
            //   that "it is not 0 at intensity 55" can be told to be this setting being
            //   on rather than a fault.
            b.Line(2, "seismogram model",
                ModSettings.EarthquakeSeismogram.value
                    ? "on: the camera follows a synthesized P/S/coda record instead of the "
                      + "game's two sine waves  [Disaster + model, not measured]"
                    : "off (setting): the camera follows the game's own two sine waves");
        }

        /// <summary>
        /// How the button came to be placed where it is, and the state of the hazard view.
        ///
        /// The <c>painting quakes</c> line is **the most important one in this section**.
        /// The earthquake hazard map also has the two-stage gate
        /// <c>Located &amp;&amp; (Emerging|Active)</c> (§A-6), and only a seismograph can
        /// raise <c>Located</c> on an earthquake (§A-2), so when the heat map is blank
        /// there is no other way to tell "there is no seismograph (normal)" from "there
        /// really is no earthquake".
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            // ★ ①'s and ②'s tiles were taken out of the disaster panel (things that only
            //   read are opened from the shortcut at the top left; InfoHub's class doc).
            //   All this prints is "is the button there" and "where is it".
            //   **This is the sim thread, but all it reads is a native-pointer comparison
            //   on a Unity object and a string; it does not touch the UI**
            //   (the same treatment as DisasterPanelBar.IsInstalled).
            b.Line(1, "info button", (InfoHub.IsInstalled ? "installed" : "not installed")
                + "  (" + InfoHub.Placement + ")");

            // This reads something the main thread owns from the sim thread, but it is the
            // one exception InfoModeSwitch's class doc explicitly permits, backed by IL
            // measurements (get_CurrentMode is a single field read, and at worst the value
            // is one tick old).
            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");

            // The seismic-intensity overlay. **Telling "no picture appears" apart can only
            // be done here.** There are four reasons it does not appear (registration
            // failed / the toggle is off / there is no earthquake to draw / the budget ran
            // out), and on screen every one of them wears the same face: "nothing
            // appears".
            //
            // The numbers are read here (on the sim thread) from what the main thread
            // (drawing) wrote. They are display-only ints and bools, and reading them late
            // does not break their meaning (the same treatment as
            // CameraShakeBooster.LastAdded / WaveformView.State).
            string overlay;
            if (!EarthquakeOverlay.Registered)
            {
                overlay = "NOT REGISTERED with RenderManager (nothing will ever be drawn)";
            }
            else if (!EarthquakeOverlay.Enabled)
            {
                overlay = "registered, toggled off";
            }
            else
            {
                overlay = "on, " + EarthquakeOverlay.DrawnQuakes + " quake(s), "
                          + EarthquakeOverlay.LastDrawCalls + " of "
                          + EarthquakeOverlay.MaxDrawCallsPerFrame + " draw calls last frame"
                          + (EarthquakeOverlay.FaultGeometryMissing
                              ? "  (fault zone omitted: prefab geometry unreadable)" : "")
                          + (EarthquakeOverlay.BudgetExhausted
                              ? "  (draw budget exhausted; some quakes omitted)" : "");
            }
            b.Line(1, "intensity overlay", overlay);

            // Where the DLC is not owned the panel body is never built
            // (EarthquakePanel._bodyBuilt).
            b.Line(1, "panel body", ModCompat.NaturalDisastersOwned
                ? "shown"
                : "hidden (Natural Disasters DLC not owned)");

            if (snapshot == null || !snapshot.Valid)
            {
                b.Line(1, "painting quakes", "unknown (no valid snapshot)");
                return;
            }

            int painting = 0;
            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (DisasterPhases.PaintsHazardMap(quakes[i].Located, quakes[i].Phase)) painting++;
            }
            b.Line(1, "painting quakes", painting + " of " + quakes.Count
                + (painting == 0
                    ? "  (the hazard map is legitimately empty; an Earthquake Sensor is what sets Located)"
                    : ""));
        }

        private static void WritePrefabFacts(DiagnosticBuilder b, EarthquakePrefabFacts prefab)
        {
            if (!prefab.Resolved)
            {
                // Where the DLC is not owned this is normal. The same treatment as the
                // impact sentence on the Assumptions side.
                b.Line(1, "prefab (EarthquakeAI)",
                    "NOT RESOLVED (expected when the Natural Disasters DLC is not owned)");
                return;
            }

            b.Line(1, "prefab (EarthquakeAI)", "resolved");
            b.Line(2, "m_crackLength", prefab.CrackLength.ToString("F2"));
            b.Line(2, "m_crackWidth", prefab.CrackWidth.ToString("F2"));
            b.Line(2, "m_emergingDuration", FramesWithHours(prefab.EmergingDuration));
            b.Line(2, "m_activeDuration", FramesWithHours(prefab.ActiveDuration));
        }

        private static void WriteSimClock(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            // The day/night cycle being off is not a broken assumption but a valid player
            // setting, so it is not made an Assumptions FAIL (no false FAILs). Instead,
            // always say here where the number 12.0 comes from.
            string clock = "hour=" + snapshot.HourOfDay.ToString("F1")
                + "  dayNight=" + (snapshot.DayNightEnabled ? "on" : "off");
            if (!snapshot.DayNightEnabled)
            {
                clock += " (hour is pinned at 12.0 by the game while day/night is off)";
            }
            b.Line(1, "sim clock", clock);
        }

        private static void WriteQuakes(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            b.Line(1, "active quakes", quakes.Count.ToString());

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];

                b.Line(2, "#" + q.DisasterId,
                    "intensity=" + q.Intensity
                    + " phase=" + q.Phase
                    + " located=" + (q.Located ? "yes" : "no")
                    + " coverage=" + (q.CoverageKnown ? q.CoverageAtEpicentre.ToString() : "unreadable")
                    + " R=" + q.Radius.ToString("F1"));

                // m_activationFrame == 0 means "not decided yet", not "now". An earthquake
                // without SelfTrigger(64) raised stays stuck here at 0, Emerging forever
                // (§A-1).
                string activation = q.ActivationScheduled
                    ? q.ActivationFrame.ToString()
                    : "0 (not scheduled - SelfTrigger was never set)";
                b.Line(3, "frames", "start=" + q.StartFrame + " activation=" + activation);

                // The warning lead time. Printing 1755 (i.e. coverage 0) when the coverage
                // could not be read would be asserting "there is no seismograph". If it
                // cannot be read, do not print it.
                // The conversion, guard included, is left to FramesWithHours (at coverage
                // 100 it should come to exactly 3.00 in-game hours, and this is the line
                // that shows whether that holds).
                b.Line(3, "warning lead", q.CoverageKnown
                    ? FramesWithHours((uint)WarningLeadTime.FramesFor(q.CoverageAtEpicentre))
                    : "unknown (the coverage at the epicentre could not be read)");

                b.Line(3, "fault (L/W)", q.CrackLength <= 0f && q.CrackWidth <= 0f
                    ? "unknown (prefab not resolved)"
                    : q.CrackLength.ToString("F1") + " / " + q.CrackWidth.ToString("F1"));
            }
        }

        /// <summary>
        /// Print a frame count as "the raw value plus the game time".
        ///
        /// Always derive the conversion from <see cref="FeatureHost.FramesPerMinute"/>.
        /// There is a previous conviction for writing the constant in by hand and being
        /// out by a factor of 4 (③, mistaking DAYTIME_FRAMES for something else).
        /// </summary>
        private static string FramesWithHours(uint frames)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return frames + " frames";

            float hours = frames / framesPerMinute / 60f;
            return frames + " frames (= " + hours.ToString("F2") + " in-game hours)";
        }
    }
}
