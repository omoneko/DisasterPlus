namespace DisasterPlus.Game
{
    /// <summary>
    /// Every displayed string. The English is held as the default, and LocaleLoader overwrites
    /// them by reflection, keyed by field name.
    ///
    /// These must be public static fields (make one const and it cannot be overwritten).
    /// Do not put an array for a dropdown here as static readonly: it would freeze in the
    /// language as of type initialisation. Make it a method and rebuild it each time if needed.
    /// </summary>
    public static class Strings
    {
        public static string ModDescription =
            "Adds realistic disaster phenomena: fire whirls, typhoons, volcanoes and hazard visualisation.";

        public static string GroupFireWhirl = "Fire whirl";
        public static string GroupGeneral = "General";

        public static string FireWhirlEnabled = "Enable fire whirls";
        // ★★ The trench earthquake (2026-08-22, the owner's request: "I'd like to add a trench
        //   earthquake… with a new icon too… and have it happen in the sea nearest to where
        //   you left-click").
        //
        //   ★ The label states that **it does not happen where you pressed**. Without that,
        //     no earthquake occurring on the mountain you pointed at reads as a fault.
        public static string TrenchQuakeEnabled =
            "Show the trench earthquake tile (the only quake that brings a tsunami)";

        public static string TrenchQuakeButtonLabel = "Trench quake";

        public static string TrenchQuakeButtonTooltip =
            "Raise a megathrust earthquake at the sea nearest to the point you click. "
            + "This is the only earthquake that brings a tsunami; the game's own "
            + "fault earthquakes do not.";

        public static string TrenchQuakeNeedsDlc =
            "The Natural Disasters DLC is required for the trench earthquake.";

        // ★ The fire whirl's suppression of vanilla tornadoes (2026-08-22, the owner's
        //   instruction: "please stop vanilla tornadoes from spawning").
        public static string NoVanillaTornado =
            "Stop the game from spawning its own tornadoes";

        public static string DetectRadius = "Detection radius (m)";
        public static string DetectCount = "Buildings required";
        public static string MaxLifetime = "Maximum lifetime (min)";
        public static string MaxLifetimeTip =
            "In-game minutes.";
        public static string SpreadStrength = "Fire spread (0 = off)";
        public static string MinSeparation = "Minimum separation (m)";

        public static string IntensityUnlock = "Unlock disaster intensity up to 25.5";
        public static string IntensityUnlockHandledByOther =
            "Handled by Natural Disasters Renewal. Enable only if you want Disaster + to control it.";

        public static string EarthquakeDamageOwner = "Earthquake damage is calculated by";
        public static string EarthquakeOwnerOther = "Natural Disasters Renewal";
        public static string EarthquakeOwnerSelf = "Disaster +";

        // ★★ **Two retired entries.** They were ③'s tile (the "Fire whirl" button on the
        //    disaster panel) and its tooltip, "Place a stationary, burning vortex".
        //    Fire whirls became something **you cannot raise deliberately; they only arise on
        //    their own during a great fire**, and the tile was removed along with the whole
        //    manual path, so neither is displayed.
        //
        //    **Do not delete the keys** — the key sets of Strings / Locales\en.txt /
        //    Locales\ja.txt must stay in agreement, and there is no reason to throw existing
        //    translations away (the same treatment as LogChannelFireWhirl and *ResetButton).
        //    **Nor may they be reused for another meaning** (the Tooltip in particular says
        //    you can "place" one. Now that you cannot, recycling that sentence elsewhere would
        //    bring the false explanation straight back to life).
        public static string FireWhirlName = "Fire whirl";
        public static string FireWhirlTooltip = "Place a stationary, burning vortex";

        // ★ Shortened to one sentence, but **"vanilla-side destruction follows its tornado
        //   settings" cannot be dropped** — that is a fact about behaviour, not an explanation.
        //   (The dropped "fire spread is unaffected" is stated by ③'s panel and the
        //    diagnostics.)
        public static string NdrDetected =
            "Natural Disasters Renewal detected: vanilla-side destruction follows its "
            + "tornado settings.";

        public static string FireWhirlNeedsDlc =
            "Fire whirls require the Natural Disasters DLC.";

        // --- The top-left shortcut, and the tab strip under it (InfoHub) ---
        //
        // ★ Buttons are told apart by **text**. Sprite names are atlas data and cannot be read
        //   from the assembly, so guessing at an icon name can give you "an invisible button"
        //   (see the InfoHub class doc).
        //   For that reason InfoButtonLabel **must be a short string** (it has to fit in a
        //   32 px square button).
        public static string InfoButtonLabel = "D+";
        public static string InfoButtonTooltip = "Disaster + information";
        public static string InfoDragTooltip = "Drag to move this window";
        public static string InfoTabDiagnostics = "Diagnostics";

        // --- The diagnostics tab ---
        public static string DiagnosticsTitle = "Diagnostics";
        public static string DiagnosticsOverlayRow = "Overlay";
        public static string DiagnosticsHotkeyRow = "Hotkey";
        public static string DiagnosticsOn = "on";
        public static string DiagnosticsOff = "off";
        public static string DiagnosticsDumpButton = "Write a diagnostics dump";
        public static string DiagnosticsDumpHint =
            "The dump is written into the mod folder.";

        public static string GroupDebug = "Debug";
        public static string OverlayEnabled = "Enable diagnostic overlay";
        public static string OverlayHotkey = "Overlay hotkey (Ctrl + key writes a dump file)";
        public static string LogChannels = "Verbose log channels";
        public static string LogChannelGeneral = "General";

        // Not currently shown in the UI. There is not one call to Log.Diag carrying the
        // FireWhirl channel, so the checkbox would control nothing (see Mod.OnSettingsUI).
        // Bring it back when ② to ⑤ emit logs with a channel. The key is not deleted, because
        // the three files (Strings / Locales\en.txt / Locales\ja.txt) must stay in agreement.
        public static string LogChannelFireWhirl = "Fire whirl";

        public static string AssumptionsFailedTitle = "Some features are unavailable";
        public static string AssumptionsFailedHint =
            "Load a city once, then reopen this page to refresh.";

        public static string GroupForecast = "Weather forecast";
        public static string ForecastEnabled = "Enable the forecast panel";

        // ★ Four entries not currently shown in the UI (*ResetButton). The buttons are now
        //   placed inside vanilla's disaster panel and their positions are decided by the
        //   panel's autolayout (DisasterPanelBar), so "reset the position" would be a dead
        //   button controlling nothing.
        //   **Do not delete the keys** — the key sets of Strings / Locales\en.txt /
        //   Locales\ja.txt must stay in agreement, and there is no reason to throw existing
        //   translations away (the same treatment as LogChannelFireWhirl and
        //   EarthquakeBandWeak).
        public static string ForecastResetButton =
            "Reset the forecast button position (takes effect next time you load a city)";
        public static string ForecastTitle = "Weather forecast";
        public static string ForecastTemperature = "Temperature";
        public static string ForecastRain = "Rain";
        public static string ForecastCloud = "Cloud";
        // Raised in the overall review (I5): Fog was read every tick and put into the snapshot
        // while being displayed nowhere. If you read it, show it; if you do not show it, do
        // not read it. We chose to show it (the same ForecastReading as rain and cloud, just
        // one more row).
        public static string ForecastFog = "Fog";
        public static string ForecastWind = "Wind";
        public static string ForecastLightning = "Lightning";
        public static string ForecastTornado = "Tornado";
        public static string ForecastShowOnMap = "Show on map";

        // ── ① forecast: the coming weather (unlocked by the weather radar) ─────────────────
        //
        // ★★ **Do not show a time of arrival.** (2026-09-02)
        //    The game moves current towards target with
        //    `speed = min(speed + 0.0001, |target-current| * 0.001)` /
        //    `current = MoveTowards(current, target, speed)`
        //    (measured from the IL of WeatherManager.SimulationStepImpl, IL_0777-07C2).
        //    The formula is completely determined, so **a number of steps** could be given
        //    exactly, but converting that to minutes needs "how many steps advance per frame".
        //    That depends on how SimulationManager runs its sub-steps, which has not been
        //    measured yet.
        //    <b>Assume a factor and write "40 minutes to go" and you have a confident error.</b>
        //    The target value itself is a settled value the game holds, so only that is shown.
        public static string ForecastComing = "Coming weather";
        public static string ForecastComingLocked =
            "Build a weather radar to unlock this. It is the only building that watches "
            + "the sky, so it is the one that earns you the forecast.";
        public static string ForecastComingNeedsDlc =
            "The coming-weather forecast needs the Natural Disasters DLC (the weather radar "
            + "comes with it).";
        public static string ForecastComingHeading = "heading for";

        // ── ① forecast: the three toggles that put the typhoon on the map ────────────────────
        public static string ForecastTyphoonSection = "Typhoon on the map";
        public static string ForecastShowTrack = "Track";
        public static string ForecastShowGale = "Storm area";
        public static string ForecastShowWind = "Wind field";
        public static string ForecastGoToStorm = "Go to the storm";
        public static string ForecastNoTyphoon = "No typhoon right now.";
        public static string ForecastTrackTooltip =
            "Draw the whole path across the map. The part still to come is bright; the part "
            + "it has already travelled is faint. The path is exact - it carries no randomness.";
        public static string ForecastGaleTooltip =
            "Draw the eyewall and the gale radius at the storm's position right now. "
            + "Future sizes are not drawn: they depend on whether the storm crosses land.";
        public static string ForecastWindTooltip =
            "Draw the wind field. This is the same field the mod uses to decide what breaks, "
            + "so the dark red is where the damage will be.";
        public static string ForecastGoToStormTooltip =
            "Move the camera to the eye. While the storm is still off the map, this takes you "
            + "to the edge it is coming from.";
        public static string ForecastAtCursor = "Hazard at cursor";
        public static string ForecastUnavailable = "Weather data unavailable";
        public static string ForecastNdrNote =
            "Natural Disasters Renewal governs disaster occurrence.";
        // Raised in review: an unlabelled bare percentage gets misread as "chance of rain" and
        // the like. It is in fact the configured probability value, and the actual per-tick
        // spawn decision goes through a squaring and a city-area correction (see the comments
        // in ForecastFeature / ForecastPanel).
        public static string ForecastProbability = "Disaster probability (setting)";
        // Raised in review: using the generic ForecastUnavailable when no hazard view is up
        // implies the wrong cause, "the weather data cannot be read". In fact the weather data
        // is alive and only the hazard view is not up — and it is one click away from being
        // fixed.
        public static string ForecastSwitchHazardView =
            "Switch a hazard view on to read a value here.";

        // Raised in the overall review (the most important one): vanilla's hazard map is not a
        // static risk surface but the predicted damage area of "a located, in-progress storm"
        // (the IL evidence is in the WeatherSnapshot.LocatedLightningStorms doc). When there
        // is no such storm, every cell of the grid is 0, and that used to be displayed as
        // "Lightning: 0". The number is genuine, but the meaning the player takes from it
        // ("it is safe here") is a lie.
        // At 0, show no number and instead write why it is empty and what would fill it.
        public static string ForecastNoStormDetected =
            "No storm detected right now. This map shows where a detected storm will hit, "
            + "so it stays empty until a Weather Radar finds one.";

        // Raised in the overall review (I2): without the DLC neither the thunderstorm/tornado
        // prefabs nor the weather radar exist, so the two hazard rows go on showing an empty
        // view and 0 forever. Give the reason in the same shape as FireWhirlNeedsDlc (the
        // weather and trend rows do not need the DLC, so they stay).
        public static string ForecastHazardNeedsDlc =
            "Lightning and tornado hazard maps require the Natural Disasters DLC.";

        // Raised in the overall review: if you are paused right after a load, the first
        // snapshot does not exist yet. This used to be displayed as "the weather data cannot
        // be read", which made "not read yet" indistinguishable from "there is no
        // WeatherManager".
        public static string ForecastWaiting = "Waiting for the first simulation update.";

        public static string TrendRising = "up";
        public static string TrendFalling = "down";
        public static string TrendSteady = "steady";
        public static string LogChannelForecast = "Forecast";

        // --- ② earthquake (Task 3) ---
        // The existing EarthquakeDamageOwner / EarthquakeOwnerOther / EarthquakeOwnerSelf are
        // for ③'s NDR-compatibility dropdown and are a different thing. Do not let the names
        // collide.
        public static string GroupEarthquake = "Earthquake";
        public static string EarthquakeEnabled = "Enable the earthquake panel";
        // ★ Not currently shown in the UI (see the ForecastResetButton doc).
        public static string EarthquakeResetButton =
            "Reset the earthquake button position (takes effect next time you load a city)";
        public static string EarthquakeNeedsDlc =
            "Earthquakes require the Natural Disasters DLC.";
        public static string LogChannelEarthquake = "Earthquake";

        // --- ② earthquake (Task 4: the panel) ---

        public static string EarthquakeTitle = "Earthquake";

        // The prefixes that tell the first layer from the second. Never reference them from
        // anywhere but EarthquakePanel's SetLayer1 / SetLayer2 (leave the caller free to pick
        // which one it claims and this separation is bound to break eventually).
        public static string SourceVanilla = "[measured]";
        public static string SourceModel = "[Disaster + model]";

        /// <summary>
        /// The token meaning "the measured-value marker goes here" inside a translated string.
        /// It is replaced with <see cref="SourceVanilla"/> immediately before display
        /// (<c>TyphoonRows.SetModelNote</c> is the only place that does the replacement).
        ///
        /// ★ <b>It must not be a <c>public static string</c>.</b>
        ///   <c>LocaleLoader</c> treats every string from <c>GetFields(Public | Static)</c> as
        ///   "a translatable key", and it uses the same enumeration to generate
        ///   <c>en.txt</c>. Make this public and the token itself is emitted as something to
        ///   translate, so the replacement stops working the moment a translator translates it
        ///   (and as a const there is an even worse breakage, where <c>SetValue</c> throws).
        /// </summary>
        internal const string MeasuredToken = "{measured}";

        public static string EarthquakeLayer1Header = "What the game actually computes";
        public static string EarthquakeLayer2Header =
            "Added by Disaster + (not vanilla behaviour)";

        // The tab headings (added when the panel's structure changed). Not one row's contents
        // changed, so these two are the only added strings. **Both tabs are the first layer**,
        // and the second-layer section is permanently outside and below the tabs (see the
        // EarthquakePanelTabs doc).
        public static string EarthquakeTabQuake = "Quake, cursor and maps";
        public static string EarthquakeTabDamage = "Buildings and seismographs";

        public static string EarthquakeNoneActive = "No earthquake in progress.";
        public static string EarthquakeCount = "Earthquakes in progress";
        public static string EarthquakeIntensity = "Intensity";
        public static string EarthquakeRadius = "Affected radius";
        public static string EarthquakePhase = "Phase";
        public static string EarthquakePhaseEmerging = "before the main shock";
        public static string EarthquakePhaseActive = "shaking";
        public static string EarthquakePhaseClearing = "aftermath";

        // "Time to the main shock" is not a prediction but **reading out a timetable**.
        // m_activationFrame is a settled value that StartDisaster wrote as
        // m_startFrame + m_emergingDuration (§A-1), and it rests on something entirely
        // different from the "a storm arrives in N hours" that ① forbids (a random spawn
        // decision). Design doc §7-2 requires this distinction.
        public static string EarthquakeTimeToShock = "Time to the main shock";
        // Note that m_activationFrame == 0 means "not scheduled", not "now".
        public static string EarthquakeTimeUnknown = "not scheduled";
        public static string EarthquakeMinutes = "min";

        // ★ These two strings were corrected in the overall review (C3). They used to be
        //    "Shaking at cursor" / "outside the shaken area", but s = 1 - d/R is **the ramp
        //    for collapse and ignition**, not the shaking. Vanilla's shaking is
        //    amp = 0.3/(1 + dist*0.001), with **no cut-off by radius whatsoever** (§A-7).
        //    In other words, the old wording said "it is not shaking" about a spot where, on
        //    the same frame, CameraShakeBooster was still adding shake and SeismographRecorder
        //    was still writing non-zero displacement. The shaking is shown separately by
        //    EarthquakeShakeAtCursor.
        public static string EarthquakeAtCursor = "Destruction factor at cursor";
        public static string EarthquakeOutOfRange = "outside the destruction radius";

        // The amplitude of vanilla's own shaking (the amp at §A-7 IL_0069, before the envelope
        // is applied). A different quantity from the collapse ramp, so it gets its own row.
        public static string EarthquakeShakeAtCursor = "Ground shaking at cursor";
        // The shaking window (Emerging|Active, and e inside m_activeDuration) is not open.
        public static string EarthquakeNotShaking = "not shaking right now";

        // For a quake in its aftermath (Clearing), DestroyBuildings is never called at all
        // (§A-3: the call exists only in the Active branch). Give the reason instead of a
        // number.
        public static string EarthquakeNoDamageInPhase =
            "this quake is past its shaking phase; the game runs no destruction for it any more";

        // At the cursor position, values from two different models appear side by side.
        // Different numbers are the normal outcome, so say on screen why they differ
        // (overall review M3).

        public static string EarthquakeFaultBand = "Fault zone";
        public static string EarthquakeFaultInside = "inside";
        public static string EarthquakeFaultOutside = "outside";

        // ★ Renamed from "Show on map" (when the intensity distribution overlay was added).
        //    With two buttons side by side, leaving one as "Show on map" means **the names do
        //    not say which one produces which picture**.
        //    This one is vanilla's info view (the grid in §A-6); the one beside it is this
        //    mod's ramp from the epicentre, and they differ in the shape they paint and in
        //    whether they need a seismograph.
        public static string EarthquakeShowOnMap = "Show the game's own hazard view";

        // The same structure and the same register as ①'s ForecastNoStormDetected. The
        // earthquake hazard map likewise has a two-stage gate, Located && (Emerging|Active)
        // (§A-6), and only a seismograph can set Located on an earthquake (§A-2 / §C-2).
        // So without one, this view is permanently empty, and that is correct.
        // **Do not let empty be read as "safe".**
        public static string EarthquakeNotLocated =
            "No earthquake is located right now. This map only shows a located, in-progress "
            + "quake, and an Earthquake Sensor is what locates one.";
        public static string EarthquakeSwitchHazardView =
            "Switch the earthquake hazard view on to read a value here.";

        // Two additions beyond the plan's 29 (a deliberate deviation).
        // ① covered both of these situations with ForecastUnavailable ("the weather data
        // cannot be read"), but doing the same in ② would give "the earthquake data cannot be
        // read" and "the cursor is not over the terrain" the same wording. The latter happens
        // almost constantly while reading the panel (the mouse is over the panel), so the row
        // seen most often would go on naming the wrong cause. Split them by cause.
        public static string EarthquakeUnavailable = "Earthquake data cannot be read right now.";
        public static string EarthquakeCursorUnknown =
            "Move the cursor over the terrain to read a value here.";

        // A 10-step bar but only 5 named bands (see the SeismicScale doc).
        // **Do not use the names of a real seismic intensity scale** (design doc §3.1, §7-4).
        //
        // ★ Since the overall review (C3), these four are displayed from nowhere.
        //    weak / moderate / strong / very strong are **names this mod gave them**, whereas
        //    vanilla is merely computing a coefficient for probability. Putting them under the
        //    [measured] prefix would be a lie — that the game judged it "very strong".
        //    The first layer shows only the number and the bar, and the band names stay hidden
        //    until the second layer (Task 9 onwards) claims them.
        //    **Do not delete the keys** — the key sets of Strings / en.txt / ja.txt must stay
        //    in agreement, and SeismicScale.BandOf is still in service (the same treatment as
        //    LogChannelFireWhirl).
        public static string EarthquakeBandWeak = "weak";
        public static string EarthquakeBandModerate = "moderate";
        public static string EarthquakeBandStrong = "strong";
        public static string EarthquakeBandSevere = "very strong";

        // --- ② earthquake (Task 5: the per-building margin) ---
        //
        // Three additions beyond the plan's 7 (a deliberate deviation. The same reasoning as
        // Task 4: they exist to avoid "recycling an existing key because there is no wording
        // for it").
        // The plan's Step 7 says "reuse existing keys for the AlreadyDown / Unknown wording",
        // but in fact no existing key carried those meanings:
        //   - AlreadyDown  … it is neither "out of range" nor "inside the fault zone". Reuse
        //                    one and you would name the wrong reason over a pile of rubble.
        //   - Unknown      … "no verdict can be given because the four prefab values could not
        //                    be read" is the very wording that avoids the lie (i.e. the
        //                    assertion) this feature must never make, so no substitute works.
        //   - SurviveAnyDistance … the table in plan 5.2 explicitly requires separate wording:
        //                    "if X ≦ 0, 'will not collapse however close it is'".
        // InsideFaultZone reuses EarthquakeFaultBand / EarthquakeFaultInside and the permanent
        // EarthquakeFaultBandNote, as planned (no new key is added).

        public static string EarthquakeBuildingUnderCursor = "Building under the cursor";
        public static string EarthquakeNoBuilding = "no building under the cursor";
        // Do not give "we looked and there was none" and "we could not look" the same wording
        // (see the BuildingProbeOutcome doc). The former is a measured result, the latter a
        // failure to read.
        public static string EarthquakeProbeFailed =
            "the buildings under the cursor could not be read right now";
        public static string EarthquakeCollapseWithin = "Collapses within";
        public static string EarthquakeCurrentDistance = "current distance";
        public static string EarthquakeVerdictCollapse = "will collapse";
        public static string EarthquakeVerdictSurvive = "will not collapse";
        public static string EarthquakeVerdictSurviveAnyDistance =
            "will not collapse at any distance at this intensity";
        public static string EarthquakeAlreadyDown = "already collapsed or burning";

        // --- Ignition (overall review M1) ---
        //
        // The answer to "fires caused by the shaking", which the request named explicitly. The
        // material (the second draw) was in BuildingMargin.BurnThresholdValue from the start,
        // was in the snapshot, and even had unit tests — and yet **it was displayed nowhere**.
        public static string EarthquakeBurnLabel = "Catches fire";
        public static string EarthquakeBurnWithin = "Catches fire within";
        public static string EarthquakeVerdictBurn = "will catch fire";
        public static string EarthquakeVerdictNoBurn = "will not catch fire";
        public static string EarthquakeVerdictNoBurnAnyDistance =
            "will not catch fire at any distance at this intensity";
        // The IL is else if (hitB && ...), so if the collapse draw hit, the ignition branch is
        // never reached.
        public static string EarthquakeBurnAfterCollapse =
            "(the collapse happens first; the same draw becomes the burn damage of the rubble)";

        // --- When the destruction code has been replaced by another mod (overall review C2) ---
        //
        // NDR replaces DisasterHelpers.DestroyBuildings wholesale with a Prefix that returns
        // false, and uses probability == 0.02f as the marker of a vanilla earthquake, applying
        // 0.04 instead (§E-2).
        // ②'s policy of "never going through DisasterHelpers" concerns **the side that writes
        // the damage** and **does nothing whatsoever for the side that reads it**. For a
        // building at intensity 55 with a threshold of 300, this mod says "will not collapse
        // at any distance" while NDR says "collapses out to 775 m".
        public static string EarthquakeVerdictNdr =
            "no verdict (another mod replaces the game's destruction code)";
        public static string EarthquakeNdrNote =
            "Natural Disasters Renewal replaces the routine that destroys and ignites buildings and "
            + "doubles the city-wide probability, so the collapse and fire verdicts are withheld. "
            + "Every other row here is read straight from the game and is unaffected.";
        // The row that stops "we do not know" being rephrased as "outside". Give the reason no
        // verdict is offered.
        public static string EarthquakeVerdictUnknown =
            "no verdict (the fault geometry could not be read from the EarthquakeAI prefab)";

        // The single most important sentence in this feature. A collapse is not a matter of
        // chance; it was already decided the moment the earthquake started (§A-3: the seed is
        // constant for the (building, disaster) pair). But that can only be said of the
        // city-wide disc.
        public static string EarthquakeGlobalDiscOnly =
            "This is decided for the city-wide disc, and it was already decided the moment the "
            + "quake started. Inside the fault zone the four rupture patches judge separately.";

        // --- ② earthquake (Task 6: camera shake) ---
        //
        // The last sentence of the note is the essential part. "At the default intensity this
        // changes nothing" is not an excuse but the very grounds on which this setting may be
        // ON by default (at intensity 55 the addition is exactly 0).
        public static string EarthquakeShakeBoost =
            "Scale camera shake with intensity and distance";
        // ★ **Taken off the settings screen.** What the setting does is stated by the
        //   EarthquakeShakeBoost label, and the "this is what vanilla does" explanation is in
        //   the diagnostic dump (see the table in the Mod.OnSettingsUI doc).
        //   **Do not delete the key. Do not reuse it either.**
        public static string EarthquakeShakeBoostNote =
            "Vanilla ignores intensity here, so a 25.5 quake shakes exactly as much as a 5.5 one. "
            + "At the vanilla default intensity (5.5) this option changes nothing.";

        // --- ② earthquake (Task 7: what a seismograph already does) ---
        //
        // Nowhere in the game does it say what changes when you build an earthquake sensor.
        // Only two things change, and both are vanilla measurements (the first layer)
        // (§A-2 / §C-2):
        //   1. the warning lead time goes from 1755 to at most 8192 frames (38.6 minutes to
        //      exactly 3.0 hours)
        //   2. located is set, and **the earthquake appears on the hazard map at all**
        //
        // The table in the plan's Step 5 lists 5 entries, but EarthquakeNoSensor, referenced
        // in Step 4's body, was left out of it. So that coverage 0 (i.e. the explanation of
        // this feature's headline) is not left as a bare "0", it is added as a sixth entry,
        // following the plan's body text.
        public static string EarthquakeSensorSection = "Earthquake sensors";
        // The cap of 100 goes into the key. Write the English "max" next to the value and that
        // one word falls out of the translation when the display is in Japanese.
        public static string EarthquakeCoverageAtEpicentre =
            "Sensor coverage at the epicentre (capped at 100)";
        // It is the same quantity as the epicentre row, so writing "(capped at 100)" on only
        // one of them makes this one look like a percentage. It is in fact the raw cell value
        // of an immaterial resource (a ushort), and exceeding 100 is perfectly ordinary
        // (overall review M9).
        public static string EarthquakeCoverageAtCursor =
            "Sensor coverage at cursor (raw cell value, not a percentage)";
        public static string EarthquakeWarningLead = "Warning lead time";
        public static string EarthquakeNoSensor = "no Earthquake Sensor reaches the epicentre";
        // Shown **always**, whether or not an earthquake is happening. This explains what the
        // sensor building is, not an observation at this instant.
        public static string EarthquakeSensorEffect =
            "An Earthquake Sensor extends the warning from 38.6 minutes to up to 3 hours, and "
            + "makes the quake appear on the hazard map at all. Only sensors whose range covers "
            + "the epicentre count.";

        // --- ② earthquake (Task 8: the waveform graph) ---
        //
        // **All four of these exist to state who measured it.**
        // EarthquakeSensorAI holds no time-series data whatsoever (§C-1, ABSENT), so the line
        // shown here is **not** a value the in-game sensor measured. It is vanilla's own
        // shaking formula (§A-7, the same formula that moves the camera), evaluated at the
        // seismograph's position instead of the camera's. Hide that one point and the feature
        // degenerates into "a plausible graph of unknown provenance".
        public static string EarthquakeWaveform = "Ground motion at the sensor";
        public static string EarthquakeWaveformNeedsSensor =
            "Build an Earthquake Sensor to record ground motion. The game itself keeps no "
            + "ground-motion history at all, so Disaster + samples it at the sensor.";
        public static string EarthquakeWaveformUnavailable =
            "Waveform drawing is unavailable on this build; showing the peak amplitude instead.";
        // The state where construction succeeded but drawing threw and nothing is drawn any
        // more. This row used to be created only when the panel was built, so a runtime
        // failure left **a silently blank space** (exactly the breakage the WaveformView class
        // doc forbids; overall review I6).
        public static string EarthquakeWaveformDrawFailed =
            "Waveform drawing stopped after an error; showing the peak amplitude instead.";

        // --- ② earthquake (second layer: the synthetic seismogram, P/S/coda) ---
        //
        // ★★ **All five entries here are second layer. Do not attach [measured] to them.**
        //    The player's remark ("the same waveform repeats over and over") is correct:
        //    vanilla's shaking is two fixed sine waves (§A-7) with no arrival, no build-up and
        //    no decay.
        //    Fixing that means **this mod has to invent a waveform** — i.e. it stops being the
        //    "game's own formula" that EarthquakeWaveformNote claims.
        //    So instead of fixing it, **vanilla's line is kept and drawn alongside**, and
        //    which is which is stated three ways: the legend, the prefix and the colour.
        //
        //    EarthquakeWaveformModelNote names the colour because there is nowhere in the
        //    graph to write a prefix (EarthquakeRows attaches the row prefixes, but no text is
        //    placed inside the texture).
        public static string EarthquakeSeismogramEnabled =
            "Synthesize a realistic seismogram (also changes how the camera shakes)";
        // ★ **Taken off the settings screen** (the same treatment as EarthquakeShakeBoostNote).
        public static string EarthquakeSeismogramNote =
            "Vanilla shakes the camera with two fixed sine waves that never arrive, never build "
            + "and never decay. This option replaces that pattern with a synthesized record and "
            + "adds a second line to the waveform graph. It is Disaster +'s own model, not "
            + "anything the game computes, so it is off by default.";
        public static string EarthquakeWaveformModel =
            "Synthesized seismogram (orange line)";
        // ── The third layer = volcanic tremor (2026-08-22, the owner's request) ─────────────────
        //    Treated exactly like the second layer. This too is **this mod's model**, not a
        //    value the game computes nor the displacement the camera actually added
        //    (see the Game/Volcano/VolcanoTremorTrace class doc).
        public static string EarthquakeWaveformTremor =
            "Volcanic tremor (teal line)";
        // The heading for when only the volcano is shaking. **It is not a distance from an
        // epicentre**, so presenting it without saying so makes it look like "a record of an
        // earthquake that is not happening".
        public static string EarthquakeWaveformTremorSource = "volcano";

        // The P-S interval. **Widening with the hypocentral distance** is this model's
        // headline, and without a number it is merely "a picture that looks about right".
        public static string EarthquakeWaveformSMinusP = "P-S interval";
        public static string EarthquakeFrames = "frames";

        // --- ② earthquake (the intensity distribution map overlay) ---
        //
        // The part that answers the request's "there is also no notion of intensity
        // distributed by distance from the hypocentre within the city" **as a map**. The
        // overall review's verdict was that "a number at one cursor point and a ten-character
        // bar is not a distribution", and that is right.
        //
        // The most important wording here is the second half of EarthquakeOverlayLegend —
        // this overlay and vanilla's hazard view, produced by the button right beside it, are
        // **different quantities** (§A-6: distance to the crack segment, quadratic falloff,
        // Rmax = R+400, needs a seismograph / this one: a linear ramp from the epicentre, no
        // seismograph needed).
        // Not letting the two be read as the same thing is what makes this feature honest.

        public static string EarthquakeOverlayShow = "Show the intensity distribution on the map";
        public static string EarthquakeOverlayHide = "Hide the intensity distribution";
        public static string EarthquakeOverlayRow = "Intensity distribution overlay";
        public static string EarthquakeOverlayOff = "off";
        public static string EarthquakeOverlayOn = "on";
        public static string EarthquakeOverlayQuakes = "earthquake(s) drawn";
        // There is no earthquake to draw. **This is not "safe"**, so give the reason.
        public static string EarthquakeOverlayNothingToDraw =
            "on, but nothing to draw: no earthquake is in its pre-shock or shaking phase. "
            + "The game only runs its destruction pass while a quake is shaking.";
        // Why the fault zone alone does not appear. It is also the explanation for not drawing
        // a guessed size.
        public static string EarthquakeOverlayFaultUnknown =
            "The fault zone is not drawn: the four EarthquakeAI prefab values could not be read, "
            + "so its size is unknown. Drawing a guessed size on the map would be indistinguishable "
            + "from a measured one.";
        // Out of budget. Up to 256 earthquakes can exist at once (§E-1).
        public static string EarthquakeOverlayCapped =
            "More earthquakes are in progress than the overlay draws at once; the rest are omitted "
            + "rather than drawn partially.";
        public static string EarthquakeOverlayUnavailable =
            "The map overlay could not be registered with the game's renderer on this build.";
        // The caution for when both are on. It is the state most open to misreading, so name it.
        public static string EarthquakeOverlayBothOn =
            "The game's hazard info view is on at the same time. The two pictures are different "
            + "quantities - see the legend below.";

        // The legend is **only about how to read the colours**. "This is a different quantity
        // from vanilla's hazard view" is already said by EarthquakeCursorModelsNote just above
        // (that row explains the difference between the two quantities itself), and writing
        // the same content in two places leaves no vertical room.
        // Instead, "what the overlay below draws is the former" was appended to the end of
        // that one.
        public static string EarthquakeOverlayLegend =
            "Legend - blue-green: the destruction factor, densest at the epicentre. Magenta: the fault zone. White: the epicentre and the fault strike";

        // --- ② earthquake (Task 9: the tsunami chain from an undersea epicentre = the first
        //     second-layer item) ---
        //
        // **Everything from here on is behaviour that does not exist in vanilla.** All of it
        // is OFF by default, and in the panel it appears under the EarthquakeLayer2Header
        // section with the [Disaster + model] prefix.
        //
        // ★★ EarthquakeTsunamiFromShore was rewritten (2026-08-29).
        //    **The previous sentence said "the game can only raise a tsunami from the map
        //    border", and that is no longer true.**
        //
        //    It is true that the DLC's <c>TsunamiAI</c> can only raise one from the border
        //    (<c>WaterWave.GetSeaLevel</c> is only called from the loop over the outermost
        //     map cells. IL_16C7-16F9). But the same solver has <c>TYPE_IMPACT</c>,
        //    **an external force that can be placed anywhere**, and that is now placed at the
        //    epicentre
        //    (docs/superpowers/specs/2026-08-29-tsunami-il-facts.md).
        //    **The wave spreads out from the epicentre in concentric rings.**
        //
        //    ★ The principle behind the wording has not changed — "do not write something we
        //      cannot do as though we could". We can do it now, so it was rewritten.
        //
        // EarthquakeTsunamiNoSea is **not a failure message**. On an inland map there is no
        // sea to place it in at the epicentre, and nothing happening is the correct result.
        // The same treatment as ①'s ForecastNoStormDetected, and the converse of "do not let
        // 0 be read as safe" — do not let "nothing happened" be read as "it is broken".

        // ★ This setting has <b>no effect on the trench earthquake</b> (the trench quake
        //   exists solely to raise a tsunami, so it always brings one regardless of the
        //   setting). All it affects is whether the panel row is always shown. Say so in the
        //   label (2026-08-31, fourth review round: the old label read as though it did have
        //   an effect).
        public static string EarthquakeTsunamiChain =
            "Always show the tsunami row (trench earthquakes raise one either way)";
        public static string EarthquakeTsunamiDelay = "Tsunami delay (min)";
        public static string EarthquakeTsunamiDelayTip =
            "In-game minutes between the quake and the wave.";
        public static string EarthquakeTsunamiPending = "Tsunami expected in";

        /// <summary>The phrasing for when no number can be attached (<c>Pending</c> needs one after it).</summary>
        public static string EarthquakeTsunamiSoon = "Tsunami on the way";
        public static string EarthquakeTsunamiRaised = "Tsunami raised";
        public static string EarthquakeTsunamiFromShore =
            "The wave spreads out from the epicentre in rings. The sea is drawn in over the "
            + "epicentre first, then pushed back out - the wall of water after that is the "
            + "game's own water simulation, the same one that carries the DLC tsunami.";
        public static string EarthquakeTsunamiNoSea =
            "The epicentre is not on open water, so no tsunami was raised. This is normal on "
            + "an inland map.";

        // ★★ Because these two were missing, the single line above appeared **for every
        //    reason**. Both "there is not enough open sea" and "the previous wave is still
        //    running" were explained as "this is an inland map" (2026-08-31, cross-review).
        public static string EarthquakeTsunamiTravelling = "the wave is on its way";
        public static string EarthquakeTsunamiTooWeak =
            "The earthquake was too weak to raise a wave anyone would see, so none was "
            + "raised. Use a stronger one.";
        public static string EarthquakeTsunamiNoSlot =
            "The game had no room left to create the wave. Too many disasters are running "
            + "at once - wait for some to finish.";
        public static string EarthquakeTsunamiSourceLeft = "source ends in";
        public static string EarthquakeRealMinutes = "real min";
        public static string EarthquakeTsunamiNoRoom =
            "There is sea at the epicentre, but not enough of it. The wave needs open water "
            + "all around the epicentre; in a narrow bay or channel it would just fill the "
            + "low ground instead of travelling. Try further out to sea.";
        public static string EarthquakeTsunamiBusy =
            "The tsunami from the previous trench earthquake has not finished yet. Only one "
            + "is tracked at a time - wait for the wave to arrive.";

        // --- ② earthquake (Task 10: long-period ground motion = the second second-layer item) ---
        //
        // **This goes further than the tsunami.** The tsunami merely raised one vanilla
        // disaster, whereas this **collapses buildings vanilla would have left standing**.
        // Therefore:
        //
        //   - the setting's own label (EarthquakeLongPeriodStrength) says "adds damage vanilla
        //     does not have". **This is the only place it can be read before the knob is
        //     moved.** Do not shorten it — when the checkboxes were folded away (2026-09-02)
        //     this warning was nearly lost, by failing to move it to the slider's label
        //   - EarthquakeLongPeriodNote states that vanilla uses a building's height for
        //     neither the shaking nor the damage (§A-7 / §A-3). Without that one sentence,
        //     players take "taller buildings shake more" to be a feature of the game
        //   - EarthquakeLongPeriodNoHeight is **a seventh entry, not in the plan's table of 6
        //     keys**. The plan's Step 6 requires "in an environment where the height cannot be
        //     read, show only a one-line reason", but the key carrying that wording was
        //     missing from the table. Reusing an existing key would give "could not be read"
        //     and "too short to qualify" the same sentence, so a new one was added

        public static string EarthquakeLongPeriod = "Long-period ground motion";
        public static string EarthquakeLongPeriodStrength = "Long-period (0 = off)";
        public static string EarthquakeLongPeriodStrengthTip =
            "Long-period ground motion. Adds damage the game never does: tall buildings come down while short ones nearby stand.";
        public static string EarthquakeTrenchDamageStrength = "Distant damage (0 = off)";
        public static string EarthquakeTrenchDamageStrengthTip =
            "Trench quakes start fires and collapse buildings far from the epicentre, scaled to the magnitude.";
        public static string EarthquakeLongPeriodNoHeight =
            "This building's height could not be read, so no long-period damage is applied to it. "
            + "The mod never guesses a height.";
        public static string EarthquakeBuildingHeight = "Building height";
        public static string EarthquakeResonance = "Resonance";
        // An eighth entry, not in the plan's table of 6 keys. It is the word needed for the
        // sample row the plan itself gives ("extra collapse risk 6.4%") and was missing from
        // the table. Show a bare "+3.1%" on its own and, in any language, nobody can tell what
        // the probability is of.
        public static string EarthquakeLongPeriodRisk = "extra collapse risk";

        // Two keys added in the second-layer review, I1 / M3. Both exist purely to avoid
        // showing "a confidently wrong number".
        //
        //   - EarthquakeLongPeriodBeforeShock … the extra damage only runs while Active
        //     (LongPeriodDamage.Step), but the cursor row uses the selection from
        //     QuakeSelection.SelectDamaging, which also covers Emerging. Showing just "extra
        //     collapse risk 6.4%" before the main shock presents a probability that has been
        //     applied to nothing yet with the face of a settled value
        //   - EarthquakeLongPeriodCapped … the sweep works outwards from the epicentre, so
        //     even when truncated the area around the epicentre has been evaluated while the
        //     outside has not. Do not quietly mix "the draw has already happened" with
        //     "it is still to come"
        //
        // **Keep them short.** Both are appended to the end of the cursor row, so if they run
        // long the row exceeds the wrapped height and gets cut off mid-way (the shape
        // EarthquakeLayer2Rows once hit on the tsunami row). They are sized against the row
        // height of 72f.
        public static string EarthquakeLongPeriodBeforeShock =
            "not applied until the main shock";
        public static string EarthquakeLongPeriodCapped =
            "sweep truncated this pass; not rolled yet";

        // --- ② earthquake (Task 11: the time-of-day factor = the third second-layer item) ---
        //
        // **No setting of its own was created.** The only thing it multiplies is the extra
        // damage from long-period ground motion, so an independent switch would be a dead
        // switch controlling nothing while long-period is OFF. The row therefore comes and
        // goes together with long-period.
        //
        // EarthquakeNoDayNight is the single most important sentence in this feature. With the
        // day/night cycle switched off, the in-game hour is **pinned at 12.0 forever** (§F-1.
        // m_dayTimeOffsetFrames is reset every sim frame), so the factor quietly becomes a
        // constant 1.00. It is shown alongside so as **not to hide the fact that it is
        // disabled**.
        // No words for "day" and "night" are provided — Of() takes an hour to cross the
        // boundary, so there are periods that do not agree with the game's own hard test
        // (hour < 5 || hour > 20).

        public static string EarthquakeTimeOfDay = "Time of day";
        public static string EarthquakeTimeFactor = "factor";
        public static string EarthquakeNoDayNight =
            "The day/night cycle is off, so the in-game hour is pinned at 12:00 and the "
            + "time-of-day factor never changes.";

        // --- ④ typhoon (Task 2: the skeleton, prefab measurements, assumption checks) ---
        //
        // At this task there is **neither a panel nor a button** (they arrive in T5). All that
        // is added here is the four settings-screen strings and the log channel's name.
        //
        // ④'s display convention: **as a rule every number ④ shows is this mod's own**, so no
        // per-row provenance prefix is attached (design doc §1.2 / §7). The only exceptions
        // are the rain and cloud amounts read from WeatherManager, and SourceVanilla is
        // attached to those alone. Therefore
        // **Strings.SourceModel must never be referenced from ④'s display code.**
        public static string GroupTyphoon = "Typhoon";
        public static string TyphoonEnabled = "Enable the typhoon panel";
        // ★ Not currently shown in the UI (see the ForecastResetButton doc).
        public static string TyphoonResetButton =
            "Reset the typhoon button position (takes effect next time you load a city)";
        public static string TyphoonNeedsDlc =
            "Typhoons require the Natural Disasters DLC.";
        public static string LogChannelTyphoon = "Typhoon";

        // --- ④ typhoon (Task 3: the logical object and track following) ---
        // ★ **This is the "default".** The intensity actually used is chosen on the slider
        //   that appears when the ④ tile on the disaster panel is pressed (this value is that
        //   slider's starting value).
        //   Unless the label says "default", an intensity changed on the slider looks as
        //   though it is not obeying this setting.
        public static string TyphoonIntensity = "Default intensity";
        public static string TyphoonIntensityTip =
            "From 10 to 255. The game own thunderstorms use 55.";
        // ★ **Taken off the settings screen.** "A vanilla storm is 55" is folded into the
        //   slider's label (TyphoonIntensity).
        public static string TyphoonIntensityNote =
            "The game's own storms use 55. Above 100 is beyond anything vanilla generates.";

        // --- ④ typhoon (Task 5: the panel, the button, the display convention) ---
        //
        // ★ Every row label from here down goes into **a row with no prefix**. The provenance
        //   is stated once, in the heading, by TyphoonModelHeader / TyphoonModelNote
        //   (design doc §7-1). The only rows that carry [measured] are TyphoonRainRow and
        //   TyphoonCloudRow, and the prefix is attached by TyphoonRows.SetMeasured.
        //
        // **Do not add a string that prints m/s** (design doc §7-3). ④'s "wind-speed
        // equivalent" is a coefficient multiplied into the game's collapse probability, not a
        // real wind speed. For the same reason ② refused to name the JMA intensity scale,
        // naming a unit makes people think it has a real-world meaning.
        public static string TyphoonTitle = "Typhoon";

        // ★★ **Do not write the marker string itself into this sentence** (overall review I5).
        //    TyphoonModelNote used to embed the English "[measured]" in its body, and ja.txt
        //    kept "[measured]" in the Japanese translation too. But ja.txt's SourceVanilla is
        //    "[実測]", so **Japanese players were left hunting for a string that never appears
        //    on screen** (in English the two happened to match, which is the kind of breakage
        //    nobody notices).
        //    Design doc §7.1 decides that, in place of "no per-row prefix", "the heading
        //    carries the meaning", so if this one sentence breaks then ④'s display convention
        //    itself is never conveyed.
        //
        //    The marker is produced in exactly one place
        //    (TyphoonRows.SetMeasured / SetModelNote).
        //    The translated string carries **<see cref="MeasuredToken"/> rather than the
        //    marker itself**, and it is replaced with Strings.SourceVanilla immediately before
        //    display — **structurally, it can never drift again.**
        //
        //    Not using string.Format is a discipline of this mod, but that is to avoid
        //    positional arguments (get the number of {0}s wrong and it throws at runtime).
        //    Replacing a named token with String.Replace cannot throw — if a translation drops
        //    the token, that one sentence simply stops mentioning the marker, and build.ps1's
        //    locale check catches that.
        // ★ Both ④'s tile and this button **change the cursor to the placement cursor** when
        //   pressed (the same promise as a vanilla disaster button). The wording was changed
        //   from "raise it" to "pick a spot" because nothing happens at the moment you press —
        //   and when the text says something happens and it does not, the player decides it is
        //   broken and presses again.
        // ★ **Two retired entries.** A typhoon is now raised only from the ④ tile on the
        //   disaster panel (the same three steps as a vanilla disaster button), and the
        //   "raise" button inside the panel was removed.
        //   **Do not delete the keys** (the same treatment as LogChannelFireWhirl and
        //   *ResetButton). **Nor may they be reused for another meaning.**
        public static string TyphoonStart = "Choose where the typhoon forms";
        public static string TyphoonPlaceHint =
            "Click the map where the typhoon should form. Right-click to cancel.";
        public static string TyphoonButtonTooltip =
            "Typhoon: pick a strength on the slider, then click the map";
        public static string TyphoonStop = "Stop the typhoon";
        public static string TyphoonJump = "Go to the storm";
        public static string TyphoonJumpTooltip =
            "Move the camera to the eye of the typhoon. While the storm is still "
            + "approaching from off the map, this takes you to the edge it is coming from.";
        public static string TyphoonInactive = "No typhoon right now.";
        public static string TyphoonWaiting = "Waiting for the first simulation update.";
        public static string TyphoonUnavailable = "Typhoon data unavailable";
        public static string TyphoonPrefabUnreadable =
            "The game's thunderstorm prefab could not be read, so no typhoon can be started. "
            + "Disaster + will not guess its radius or its lifetime.";
        public static string TyphoonCentre = "Centre";
        public static string TyphoonHeading = "Heading";
        public static string TyphoonCoreStrength = "Core strength";
        public static string TyphoonStormRadius = "Storm radius";
        public static string TyphoonGaleRadius = "Gale radius";
        public static string TyphoonPhaseLabel = "Phase";
        public static string TyphoonPhaseApproaching = "approaching";
        public static string TyphoonPhasePeak = "at its peak";
        public static string TyphoonPhasePassing = "passing";
        public static string TyphoonPhaseGone = "gone";
        public static string TyphoonLandfall = "Landfall in";

        // The unit for in-game minutes. It is the same word as ②'s EarthquakeMinutes, but if
        // ④'s panel pulled ②'s key, correcting one translation would silently change the other.
        public static string TyphoonMinutes = "min";

        // **The word that stops us writing "landfall in 0 minutes".** When the centre is
        // already over land, 0 remaining means "it has already happened", not "it is about to".
        // ④'s version of "do not mix 0 with a state that is not 0", which ① and ② established
        // repeatedly.
        public static string TyphoonLandfallNow = "already over land";

        // ★ Design doc §7-2. "Landfall in N minutes" may be shown because ④ holds the track
        //   deterministically. ①'s forecast panel decides spawning at random and so cannot
        //   show the same thing. The job of this one line is **to write that difference into
        //   the panel**; without it, it reads as "the game is predicting this".

        // The wording that keeps this out of "0 minutes". On a track that passes over the sea,
        // never making landfall is the normal outcome.
        public static string TyphoonNoLandfall = "stays over water on its current track";
        public static string TyphoonRainRow = "Rain";
        public static string TyphoonCloudRow = "Cloud";

        // --- ④ typhoon (Task 6: lightning) ---
        //
        // ★ The lightning rows carry **no prefix** either (④'s display convention). None of
        //   the numbers is computed by the game; they are ④'s own ledger and a ceiling
        //   estimated from vanilla's formula.
        //
        // TyphoonLightningRow is shaped so that **the label states the order the four numbers
        // appear in**. This mod uses a format string (string.Format) in not one place — get
        // the {0}s in a translation wrong and it throws at runtime, so positions are explained
        // in words.
        public static string TyphoonEffectsHeader = "What the typhoon brings";
        public static string TyphoonLightningRow =
            "Lightning (in flight / total / left to the host storm / dropped)";

        // ★ Overall review I4. At intensity 170 and above the host storm's share alone uses up
        //   the queue of 20, so ④ adds not a single strike (the derivation is in
        //   LightningBudget's IntensityWithNoShareAtPeak). **That intensity is within the
        //   slider's range**, so the player can silently lose the very purpose of T6 (the bias
        //   towards the eyewall). Show it **in the panel too**, not only in the diagnostic dump.
        public static string TyphoonLightningYielded =
            "At this intensity the host thunderstorm is expected to use the whole 20-strike "
            + "queue, so Disaster + adds none of its own. The lightning you see is the host "
            + "storm's, spread evenly over its disc instead of around the eye wall. Lower the "
            + "typhoon intensity to get the eye-wall placement back.";

        // --- ④ typhoon (Task 7: wind damage) ---
        //
        // ★ No prefix here either (④'s display convention). **Above all, do not claim a "wind
        //   speed"** — ④'s wind-speed equivalent is a coefficient multiplied into the collapse
        //   probability, not m/s (design doc §7.3).
        //   TyphoonWindNote states that fact once.
        // The same shape as TyphoonLightningRow: **the label states the order in words**
        // (no format string. Get the {0}s in a translation wrong and it throws at runtime).
        public static string TyphoonWindRow =
            "Wind damage (collapsed this pass / total / examined / refused by the game)";
        public static string TyphoonWindStrength = "Wind damage (0 = off)";
        public static string TyphoonWindStrengthTip =
            "Collapses buildings the game would never collapse by itself.";
        public static string TyphoonWindCapped =
            "sweep truncated this pass; the outer edge has not been rolled yet";

        // ★ The dangerous semicircle. **The player cannot tell if left and right are swapped**,
        //   so state which side is stronger on both the settings screen and the panel.
        public static string TyphoonSouthernHemisphere =
            "Southern hemisphere (the dangerous side is the LEFT of the track)";
        // ★ **Taken off the settings screen.** Which side the dangerous semicircle is on is
        //   stated by the TyphoonSouthernHemisphere label, and the reasoning is in the
        //   diagnostic dump.
        public static string TyphoonDangerousSideNote =
            "A real typhoon is not symmetric: on one side the spin and the storm's own "
            + "travel add up. That side gets a slightly wider and slightly more likely "
            + "damage footprint - the right of the track in the northern hemisphere, the "
            + "left in the southern one.";
        public static string TyphoonDangerousSideRight = "stronger on the right of the track";
        public static string TyphoonDangerousSideLeft = "stronger on the left of the track";

        // --- ④ typhoon (Task 8: river flooding) ---
        //
        // ★ TyphoonFloodNoSources is the row that says "why nothing happens"
        //   (design doc §7.4. The same treatment as ①'s "why the hazard map is empty").
        //   **State explicitly that this is not a fault.**
        public static string TyphoonFloodRow = "River flooding";
        public static string TyphoonFloodStrength = "River flooding (0 = off)";
        public static string TyphoonFloodStrengthTip =
            "Raises the map own water sources. Put back when the storm ends.";
        public static string TyphoonFloodNoSources =
            "This map has no natural water sources near the storm, so no river can rise. "
            + "Nothing is wrong - the game has no flood disaster of its own, and Disaster + "
            + "only raises water sources the map already has.";
        public static string TyphoonFloodRaised = "raised";

        // --- ④ typhoon (tornado-strength local damage. The successor to the accompanying
        //     tornado) ---
        //
        // ★★ TyphoonGustNote **is the explanation of this feature**.
        //   "No tornado appears anywhere, yet a narrow area is wrecked as though one had"
        //   can only look like a fault without an explanation. **Do not shorten it.**
        //
        // As with the other rows, TyphoonGustRow has **the label state the order in words**
        // (no format string. Get the {0}s in a translation wrong and it throws at runtime).
        public static string TyphoonGustRow =
            "Tornado-strength damage (patches now / collapsed this pass / total / refused "
            + "by the game)";
        public static string TyphoonGustStrength = "Gust damage (0 = off)";
        public static string TyphoonGustStrengthTip =
            "Tornado-strength damage in small patches. No tornado is spawned.";

        // --- ④ typhoon (Task 9: the giant rotating cloud) ---
        //
        // ★ TyphoonCloudUnavailable is the row that says "why the whole sky does not change".
        //   DayNightDynamicCloudsProperties may not exist depending on the DLC and the
        //   graphics settings (IL findings document §C-2, PARTIAL). **This is not a fault**,
        //   so it also says that ④'s own cloud is still drawn as before.
        public static string TyphoonCloudEnabled = "Draw the typhoon's cloud spiral";
        public static string TyphoonVanillaCloudBoost =
            "Also thicken and speed up the game's own sky clouds";
        public static string TyphoonCloudUnavailable =
            "The game's sky cloud settings are not present in this environment, so only "
            + "Disaster +'s own cloud is drawn.";

        // --- ④ typhoon (the storm visuals) ---
        //
        // ★ State that this is not "damage". The spray is drawing only, the blowing about
        //   affects only citizens and vehicles, and it touches buildings, roads and trees not
        //   at all (a separate knob from wind damage).
        public static string TyphoonStormFx = "Show the storm at ground level";
        public static string TyphoonStormSound = "Play the storm's wind sound";

        // --- ⑤ volcano (Task 2: the skeleton, resolving the terrain API, assumption checks) ---
        //
        // At this task there is **neither a panel, nor a button, nor a volcano** (they arrive
        // from T3 onwards). All that is added here is the three settings-screen strings, the
        // log channel's name, and the one "cannot be read" line.
        //
        // ⑤'s display convention: **as a rule every number ⑤ shows is this mod's own**, so no
        // per-row provenance prefix is attached (design doc §7.4). The only exceptions are the
        // three rows for the terrain height at the chosen spot and the building and road
        // segment counts in the footprint, and SourceVanilla is attached to those alone.
        // Therefore
        // **Strings.SourceModel must never be referenced from ⑤'s display code.**
        //
        // ★ ⑤ has no key corresponding to TyphoonNeedsDlc. **⑤ does not need the ND DLC**
        //   (design doc §1.4). The only thing that needs the DLC is igniting trees, and T8
        //   states that through FeatureHost.NoteDegraded. Putting "the DLC is required" here
        //   would be a lie.
        public static string GroupVolcano = "Volcano";
        public static string VolcanoEnabled = "Enable volcanoes";
        // ★ Not currently shown in the UI (see the ForecastResetButton doc).
        public static string VolcanoResetButton =
            "Reset the volcano button position (takes effect next time you load a city)";
        public static string VolcanoUnavailable = "Volcano data unavailable";
        public static string LogChannelVolcano = "Volcano";

        // --- ⑤ volcano (Task 3: the panel, the button, the display convention) ---
        //
        // ★ Every row label from here down goes into **a row with no prefix**. The provenance
        //   is stated once, in the heading, by VolcanoModelHeader / VolcanoModelNote
        //   (design doc §7.4). In ⑤ the only row that may carry [measured] is **the single
        //   "Footprint" row on the volcano tab** (the building and road segment counts within
        //   the area), and the prefix is attached by VolcanoRows.SetMeasured.
        //
        // **Do not add a string that names a real physical unit** (design doc §7.5).
        // All ⑤ may show is distance (m), height (m), in-game time and a 0-10 step; ⑤ has
        // neither a "temperature" nor a "viscosity" for the lava.
        public static string VolcanoTitle = "Volcano";
        public static string VolcanoButtonLabel = "Volcano";
        public static string VolcanoButtonTooltip =
            "Volcano: pick a size on the slider, then click the map. The terrain change is permanent.";

        // ★★ **Do not write the marker string itself into this sentence** (the same trap as
        //    ④'s overall review I5). ja.txt's SourceVanilla is "[実測]", so embedding the
        //    English "[measured]" in the body **leaves Japanese players hunting for a string
        //    that never appears on screen**. The translated string carries MeasuredToken, and
        //    VolcanoRows.SetModelNote replaces it with SourceVanilla immediately before display.
        //    tools\CheckLocales.ps1 checks that the token is present in this key.

        // ★ **A permanent warning** (design doc §7.1). Shown even when there is no volcano.
        //   Do not make it conditional because "it is noisy" — the owner decided that
        //   irreversibility is acceptable, but that does not mean it may be kept from the
        //   player.
        public static string VolcanoIrreversibleWarning =
            "The terrain change is permanent: nothing can undo it and it is saved.";

        public static string VolcanoInactive = "No volcano right now.";
        public static string VolcanoWaiting = "Waiting for the first simulation update.";
        public static string VolcanoTerrainUnavailable =
            "Disaster + cannot reach the terrain height array in this build of the game, so "
            + "volcanoes are disabled. See the diagnostic dump for which call could not be "
            + "resolved.";

        public static string VolcanoFormRow = "Shape";
        public static string VolcanoFormShield = "Shield volcano";
        public static string VolcanoFormStrato = "Stratovolcano";
        public static string VolcanoFormDome = "Lava dome";
        public static string VolcanoRadiusRow = "Radius";
        public static string VolcanoHeightRow = "Final height";
        public static string VolcanoPhaseRow = "Phase";
        public static string VolcanoGroundHeightRow = "Ground at the chosen spot";
        public static string VolcanoMetres = "m";

        // --- ⑤ volcano (Task 4: the placement tool, surveying the footprint) ---
        //
        // ★★ All three assertions of design doc §7.1 / §7.2 / §7.3 are strings in this
        //   section: VolcanoIrreversibleWarning (§7.1), VolcanoEstimateNote (§7.2) and
        //   VolcanoBuildabilityNote (§7.3). **However much you want to shorten them, first
        //   read which assertion you would be dropping.**
        //
        // ★ **The only units named in strings are metres and in-game minutes.** ⑤ has no m/s,
        //   no degrees and no calories (design doc §7.5).
        // ★ **Two retired entries.** A volcano is now placed only from the ⑤ tile on the
        //   disaster panel (the same three steps as a vanilla disaster button), and the
        //   "place" button inside the panel was removed.
        //   **Do not delete the keys. Nor may they be reused for another meaning.**
        public static string VolcanoPlace = "Place a volcano";
        public static string VolcanoPlaceHint =
            "Click where the volcano should rise. Right-click to cancel.";

        /// <summary>
        /// The row shown for the one tick between the request being queued and sim picking it
        /// up. **It is not a phase** — <c>VolcanoPhase.Surveying</c> was retired (see the
        /// <c>VolcanoState</c> class doc).
        /// </summary>
        public static string VolcanoSurveying = "Surveying the area...";

        // ★★ **Five retired entries (2026-08-21).** These are the rows that stopped being
        //    displayed when the confirmation window was removed. The owner's instruction was
        //    "raise it like every other disaster: tile → slider → click", and the confirmation
        //    stage disappeared entirely.
        //
        //    **Do not delete the keys** — the key sets of Strings / Locales\en.txt /
        //    Locales\ja.txt must stay in agreement, and there is no reason to throw existing
        //    translations away (the same treatment as FireWhirlName / FireWhirlTooltip).
        //    **Nor may they be reused for another meaning.**
        //    ConfirmYes / ConfirmNo / SettingsChanged / PausedNote in particular claim
        //    behaviour that no longer exists: "pressing starts it" and "you will be asked to
        //    confirm again".
        public static string VolcanoConfirmHeader = "Build a volcano here?";
        public static string VolcanoConfirmYes = "Build the volcano here";
        public static string VolcanoConfirmNo = "Cancel";
        public static string VolcanoConfirmDestroyed = "Will be destroyed (approx.)";

        public static string VolcanoBuildingsRow = "buildings";
        public static string VolcanoSegmentsRow = "roads";

        /// <summary>
        /// The "Footprint (approx.)" row on the volcano tab. **The only row that came down
        /// from the confirmation window**, and the only place in ⑤ that carries the
        /// <c>[実測]</c> marker (grep 5 in <c>VolcanoRows</c>).
        /// The counts are from the moment of the survey, and the city keeps moving while the
        /// ground is being cleared, so the real number will differ either way — that is
        /// written in the diagnostic dump's <c>note: counts</c>.
        /// </summary>
        public static string VolcanoFootprintRow = "Footprint (approx.)";

        // ★ §7.2's "state explicitly that it is approximate". Show the raw number and the
        //   player reads it as "exactly that many will be destroyed" (see the
        //   ClearanceEstimate class doc).
        //
        // ★ **Two retired entries.** The two lines of caveat about the approximation came off
        //   the screen and moved into the row heading's "(approx.)" (VolcanoFootprintRow) and
        //   the diagnostic dump (the owner's instruction: "you don't need to put out all that
        //   explanation").
        //   **Do not delete the keys** (the same treatment as LogChannelFireWhirl and
        //   *ResetButton). **Nor may they be reused for another meaning.**
        public static string VolcanoEstimateApprox = "About this many will be removed";
        public static string VolcanoEstimateNote =
            "The two rows above are what the survey counted at that moment. The city keeps "
            + "changing while the ground is cleared, so the real number will differ.";

        // ★★ Overall review I6. **A volcano in progress does not survive a save**
        //   (design doc §1.3). Save part-way and reload and you are left with a stump of a
        //   mountain, with no crater, no eruption and no lava, which can neither be finished
        //   nor removed. Place another in the same spot and **it piles on top of that one**
        //   (VolcanoUplift re-records "the terrain as it is now" as the original height).

        // ★★ **Retired (2026-08-21).** It went with the confirmation window. Clicking the map
        //    while paused still commits the volcano, and **it starts moving the moment you
        //    unpause** (the same as a vanilla disaster).
        //    **Do not delete the key. Nor may it be reused for another meaning.**
        public static string VolcanoPausedNote = "The game is paused. Unpause to start.";

        // ★ For when the sweep was truncated at one tick's ceiling. **The footprint estimate
        //   then becomes a lower bound.** Keep quiet about that and it presents "a number
        //   smaller than the reality" as a settled value, not even an estimate.

        public static string VolcanoSegmentsUnknown =
            "(roads could not be counted, but they are destroyed too)";

        // ★ §1.2 itself. It writes **why "raise the ground without clearing it" is not an
        //   option** — without that, the destruction looks like a heavy-handed choice by the
        //   mod.
        // ★ **One retired entry.** "Why it has to be destroyed" is an explanation of the
        //   design, not a decision made at the moment of raising it. The same content is in
        //   the diagnostic dump (VolcanoFeature.WriteNotes). The key stays, but do not reuse it.
        public static string VolcanoClearingWarning =
            "The roads and buildings inside the footprint will be destroyed. Raising the "
            + "ground without clearing them first does not work: the game pins the terrain "
            + "back to the height of every road and building on every update, so the "
            + "mountain would end up full of flat trenches and bowls.";

        // ★ §7.3. **State explicitly that this is not a fault** (the same treatment as ①'s
        //   "why the hazard map is empty").

        // ★ §C-10. Hitting the ceiling raises no exception and **silently flattens the summit
        //   into a plateau**, so it is stated alongside the footprint row on the volcano tab
        //   (with the confirmation window gone, this is no longer "a warning before placing"
        //   but the explanation of "why it is lower than asked").
        public static string VolcanoHeightLimited =
            "The 1024 m terrain ceiling makes this volcano lower than asked.";

        // ★★ **Retired (2026-08-21).** With the confirmation window gone, the whole path of
        //    "re-survey if the settings changed just before confirming" disappeared (the
        //    survey runs once, using the settings and the slider value as of the click).
        //    **Do not delete the key. Nor may it be reused for another meaning.**
        public static string VolcanoSettingsChanged =
            "The settings changed, so the area was surveyed again.";

        /// <summary>
        /// In-game hours. **The delay for "ground you can build on" is given in these**
        /// (overall review M13) — in minutes the number exceeds 400, and the player ends up
        /// dividing by 60 at the moment of an irreversible decision.
        /// </summary>
        public static string VolcanoHours = "in-game hours";

        public static string VolcanoShapeSetting = "Volcano shape";

        // --- ⑤ volcano (Task 5: clearing — the staged destruction of roads and buildings) ---
        //
        // ★ These rows carry no [measured] either. Both the radius swept and the number
        //   destroyed are **⑤'s own tally of what it did**, not values the game computed
        //   (design doc §7.4. The only exceptions are the three confirmation rows, which are
        //   in T4's section).
        public static string VolcanoClearingRow = "Clearing";
        public static string VolcanoClearedRadius = "Radius swept";

        // ★ **The content was changed** from the plan's wording. The plan said "shelters,
        //   doomsday vaults and dams cannot be destroyed", but that came from reading only the
        //   behaviour of ④ with demolish:false; measured from the IL in T5 Step 1, all five of
        //   ShelterAI / DoomsdayVaultAI / DamPowerHouseAI / DecorationBuildingAI /
        //   TsunamiBuoyAI **do accept demolish:true**
        //   (point 4 of the VolcanoClearing class doc). Naming them would be a lie, so it is a
        //   conditional, general form: "if anything was refused".

        // ★ Design doc §1.2 itself. It writes why **"give up on the roads alone and raise the
        //   ground" is not chosen**.
        //
        // ★ In overall review M9 the check widened from "the road path" to "the clearing path
        //   (roads and buildings)". **Widen the wording along with it** — saying "no way to
        //   remove the roads was found" in an environment where the building side cannot be
        //   resolved is a lie.
        public static string VolcanoClearingPathUnavailable =
            "Disaster + could not find a usable way to remove the roads and buildings inside "
            + "the footprint in this build of the game, so it will not build a volcano at all. "
            + "Raising the ground without removing them first does not work - the game pins "
            + "the terrain back to the height of every road and building on every update, and "
            + "the mountain would come out full of flat trenches and bowls.";

        public static string VolcanoClearingLead = "Clearing lead (m)";
        public static string VolcanoClearingLeadTip =
            "How far the clearing runs ahead of the uplift.";

        // ★ VolcanoStopButton was retired on 2026-08-22 (the key was deleted with it).
        //   The owner's decision: "we don't need a stop button. You can't actually stop an
        //   eruption in real life, can you?"
        //   **Do not recycle that key for another meaning.**

        // --- ⑤ volcano (Task 6: the uplift) ---
        //
        // ★ No [measured] here either. The progress, the summit and the effective radius are
        //   all **numbers ⑤ decided**, not values the game computed (design doc §7.4).
        //
        // ★ **Annotate the effective radius with "how far the clearing reached"**
        //   (VolcanoActiveRadiusRow). This is the visualisation of trap 1, and the only row by
        //   which "if the clearing stalls, the uplift stalls" can be confirmed by eye in the
        //   game. **Do not shorten it.**
        public static string VolcanoUpliftRow = "Uplift";
        public static string VolcanoUpliftProgress = "Progress";
        public static string VolcanoSummitRow = "Summit";
        // ★ A row whose meaning changed on 2026-08-20 (remark ⑤ from the game). It used to be
        //   "which of the tiles covering the footprint this is", but ⑤ now publishes
        //   **only the area that actually changed on that tick**. What matters to the player
        //   is "how many terrain updates it takes for the change to reach the screen", and 1
        //   means it all appeared on the same tick, i.e. the smoothest possible state
        //   (see the VolcanoUplift class doc).
        public static string VolcanoUpliftMinutes = "Uplift time (min)";
        public static string VolcanoUpliftMinutesTip =
            "In-game minutes the uplift takes.";
        public static string VolcanoReliefStrength = "Flank relief (%)";
        public static string VolcanoReliefStrengthTip =
            "Relief on the mountain flanks. 0 = a smooth cone.";

        // ★★ The explanation of the intensity slider while ⑤ is armed. **This one line is the
        //    only prose**, and the label shows nothing but numbers (the multiplier and the
        //    real dimensions) (see the VolcanoSizeReadout class doc).
        //    The slider had been the size knob since 2026-08-21, but nowhere on screen said so
        //    — that was remark ④ of 2026-08-22.
        public static string VolcanoSizeSliderTooltip =
            "Volcano size: the slider scales the radius and height set in the options.";

        // ★ The estimate in design doc §7.3. VolcanoBuildabilityNote already says that
        //   **this is not a fault**, so this holds only the heading for the number.


        // --- ⑤ volcano (Task 7: the eruption) ---
        //
        // ★ No [measured] here either. The eruption strength is a 0-10 step that ⑤ decided,
        //   neither a value the game computed nor a real physical quantity (design doc §7.4 /
        //   point 5 of the plan's "the range of assertions we may make").
        public static string VolcanoEruptionRow = "Eruption";

        /// <summary>The settings checkbox (a different thing from the display row <c>VolcanoEruptionRow</c>).</summary>
        public static string VolcanoEruptionFx = "Draw the eruption plume";

        /// <summary>The settings checkbox. The sound itself is a wav bundled with the mod.</summary>
        public static string VolcanoEruptionSound = "Play the eruption sound";

        // ★ **Do not shorten it.** This is the only place that states that the game has
        //   neither lava nor an eruption (§B-5: zero hits in the string heap).
        //   Only the flame is the game's own, and that needs no DLC either.
        //
        // ★★ The version that said the sound was "silent" is **no longer correct**.
        //    It remains true that the borrowed effects make no sound (§H-17), but ⑤ now
        //    bundles its own audio and feeds it into vanilla's effects group.
        //    **Correct this to match the measurements** — going on saying "it does not" about
        //    something that does is exactly as harmful as saying "it does" about something
        //    that does not.

        // --- ⑤ volcano (the stand-in for a pyroclastic flow) ---
        //
        // ★★ **Do not shorten it.** This is the only place that states that the thing called
        //   a "pyroclastic flow" is not a pyroclastic flow. The game has no pyroclastic flow
        //   effect at all (all 277 EffectInfos in the shipped assets were checked).
        public static string VolcanoPyroclasticSetting =
            "Show a dust surge fanning out down the slopes";


        // ★ The message for when not one vanilla effect could be resolved. **The eruption
        //   carries on.**
        public static string VolcanoEffectsMissing =
            "Disaster + could not borrow the game's particle effects in this environment, so "
            + "the eruption is not drawn. The mountain still rises, the lava still flows and "
            + "it still sets buildings on fire.";

        // --- ⑤ volcano (Task 8: the lava's advance and ignition) ---
        //
        // ★ No [measured] here either. Both the distance flowed and the number ignited are
        //   **⑤'s own tally of what it did**, not values the game computed (design doc §7.4).
        public static string VolcanoLavaRow = "Lava";
        public static string VolcanoLavaFlowsSetting = "Lava flows (0 = off)";
        public static string VolcanoLavaFlowsSettingTip =
            "Number of flows from the crater.";
        public static string VolcanoLavaFireSetting = "Lava sets fire to what it touches";
        public static string VolcanoLavaLongest = "Longest flow";

        // ★ **Do not shorten it.** It is the only explanation that stops "the trees do not
        //   burn" being read as "a fault", and the place that states that not owning the ND
        //   DLC is a normal condition (§B-7c).

        // ★ The roads do not burn not because ⑤ cut a corner but because the game has no API
        //   for it (§B-7d).

        // --- ⑤ volcano (Task 9: drawing the lava) ---
        //
        // ★ No other task depends on T9. **Delete these three keys, one setting and four lines
        //   of VolcanoFeature and T9 can be dropped entirely with T1 to T8 still working.**
        // ★★ Volcanic earthquakes. **They apply independently of ②'s settings** (see the
        //    VolcanoTremorShake class doc).
        //    Always write "it destroys no buildings" — something called an earthquake doing no
        //    damage goes against the player's expectations, so unless it is said up front on
        //    the settings screen it looks like a fault.
        public static string VolcanoQuakeSetting = "Volcanic earthquakes (shaking only)";
        public static string VolcanoQuakeRow = "Volcanic earthquakes";

        // Volcanic lightning (2026-08-22). Can be switched off separately from drawing the plume.
        public static string VolcanoLightningSetting =
            "Lightning inside the ash plume";
        public static string VolcanoLavaRenderSetting = "Draw the lava surface";

        // ★ Do not keep quiet about it being invisible. Say at the same time that **the flow,
        //   the scorching and the ignition are all unchanged**.
        public static string VolcanoLavaNoMaterial =
            "Disaster + could not build a material for the lava in this environment, so the "
            + "lava is invisible. It still flows, scorches the ground and sets buildings on fire.";
    }
}
