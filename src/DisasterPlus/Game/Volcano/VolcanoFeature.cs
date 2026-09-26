using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤ Volcano. At the spot the player picks, the ground rises while the roads and buildings
    /// inside the range are destroyed in stages, then it erupts from the summit crater and lava
    /// flows down the flanks.
    ///
    /// **⑤ does not occupy a vanilla disaster slot** (design doc §2). Without the ND DLC there is
    /// not a single vanilla disaster prefab (§D-11), and registering our own prefab at runtime
    /// bakes the prefab name into the save. So ⑤ runs on its own phase machine
    /// (T4's <c>VolcanoState</c>). **It does not touch the disaster-related types at all.**
    ///
    /// > **The guarantee is a grep.** Search <c>src/DisasterPlus/Game/Volcano/</c>,
    /// > <c>src/DisasterPlus/Core/Volcano/</c> and <c>Game/UI/Volcano*.cs</c> for the API names of
    /// > the disaster manager, the disaster prefab, the disaster data and disaster
    /// > creation/lookup/detection, and expect **0 hits**. **Do not write those names in doc
    /// > comments either** — write them and the guarantee can no longer be read as "0 hits".
    /// > When the names are needed, point at the facts doc §D-11.
    ///
    /// **Unlike ①, ② and ④, ⑤ has no vanilla material to build on whatsoever.** Volcanoes do not
    /// exist in vanilla, and there is not one prefab, material or shader for lava, magma or melt —
    /// not even in the DLL's string heap (§B-5). So the numbers ⑤ shows are
    /// **in principle all this mod's own**, and the panel says so once, in its heading
    /// (design doc §7.4).
    ///
    /// At this task (Task 2) it is **a feature with neither a panel nor a volcano**. All it does
    /// is read on the sim thread and publish to <see cref="VolcanoHub"/>, and
    /// **report in the diagnostic dump whether the terrain API can be resolved**. Without knowing
    /// that, not one line of T3 onwards means anything
    /// (<see cref="VolcanoTerrainFacts.Usable"/>).
    ///
    /// It implements <see cref="IPausedTickFeature"/> for the same reason as ①, ② and ④
    /// (open the panel while still paused right after a load and every row would read
    /// "cannot be read").
    /// **But ⑤ advances the game state from T4 onwards. And what ⑤ advances is terrain, which
    /// cannot be undone.** The machinery that keeps that contract is inside
    /// <see cref="OnSimulationTick"/>.
    /// </summary>
    public class VolcanoFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Volcano";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            VolcanoHub.Clear();
            VolcanoReader.Reset();
            VolcanoState.Reset();

            // ★★ **Re-register on every level load.** ToolController.m_tools is built once in
            //    Awake and ToolsModifierControl.SetTool<T> merely looks up a static dictionary,
            //    so without registering, SetTool<T>() **silently does nothing**
            //    (firestorm appendix A; the class doc of VolcanoPlacementTool).
            //    ToolController is rebuilt per city, so the previous city's registration is
            //    useless.
            ToolRegistration.Register<VolcanoPlacementTool>();
        }

        /// <summary>
        /// Sim thread. All reads and writes of <c>TerrainManager</c> / <c>TerrainModify</c> /
        /// <c>SimulationManager</c> must happen here.
        ///
        /// It is also called while paused (deltaMinutes == 0) (<see cref="IPausedTickFeature"/>).
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.VolcanoEnabled.value)
            {
                // ★ When the feature is turned off, discard the queued request and the phase
                //   (plan §4.1). Without discarding them, a request queued while it was off
                //   **fires the instant it is turned back on**, and a mountain starts growing at
                //   a spot the player has forgotten about.
                //   **Terrain already changed does not come back.** All that is discarded is
                //   "what was going to happen next".
                //   The condition is there so we do not hold the lock the whole time it is off.
                if (VolcanoState.Phase != VolcanoPhase.Idle
                    || VolcanoHub.PendingRequest.Kind != VolcanoRequest.None)
                {
                    VolcanoHub.TakeRequest();
                    VolcanoState.Reset();
                }
                return;
            }

            // Everything up to here is "just read and publish". This is reached while paused too.
            var snapshot = VolcanoReader.Read();
            VolcanoHub.Publish(snapshot);

            // The Volcano channel is off by default. Without this if, the string concatenation
            // below would run on every sim tick (about 50 times a second at normal speed) before
            // being thrown away by Log.Diag — C# evaluates the arguments fully before the call,
            // so the mask test inside Diag is too late.
            //
            // Unlike ①'s ForecastFeature, this must not be an early return. That would skip the
            // pause guard below and everything after it
            // (② and ④ carry the same note).
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "volcano",
                    snapshot.Valid
                        ? "terrain=" + (snapshot.Terrain.Usable ? "usable" : "UNUSABLE")
                          + " raw=" + snapshot.Terrain.RawArrayLength
                          // ★ The volcanic-earthquake row. **Always emit it, including when
                          //   nothing is shaking** — otherwise "the feature is dead" and "this is
                          //   simply a phase that does not shake" become indistinguishable in the
                          //   log (which is what actually happened in ③).
                          + " tremor=" + (VolcanoTremorTrace.Active
                              ? VolcanoTremorTrace.ActivityUnit.ToString("F2")
                              : "off")
                        : "snapshot invalid");
            }

            // ★★ **Taking the request sits above the pause guard** (whole-project review I1).
            //    Taking it changes not one building, road or terrain cell — a placement request
            //    only sets the phase to Clearing, and what actually destroys things is
            //    VolcanoState.Tick below. **Not answering would mean "failing silently"** — a
            //    player who paused before making a mountain and then clicked the ground got
            //    nothing at all. **Call it exactly once per tick.**
            VolcanoState.HandleRequest(snapshot);

            // ★ Everything below advances state. What ⑤ advances is **terrain that cannot be
            //    undone**. Never let it through while paused (deltaMinutes == 0).
            //    Anything T4–T9 adds must go below this line.
            //    Delete this comment and you get "the mountain grows, buildings vanish and lava
            //    flows while paused".
            if (deltaMinutes <= 0f) return;

            // T5–T9 are called from the phase branches inside this. **Do not add to them here
            // directly.**
            VolcanoState.Tick(snapshot, frameIndex, deltaMinutes);

            // ★ **After** the phase has advanced, bring the volcanic-earthquake recording side
            //   into line (②'s seismograph reads from here; <see cref="VolcanoTremorTrace"/>).
            //   Put it before and you record against a phase that is one tick stale.
            //   The camera shake (main) is a separate path and is not touched here.
            VolcanoTremorTrace.Update(frameIndex);
        }

        /// <summary>
        /// Main thread. **Do not call the sim-side types from here.**
        /// The only thing read is the snapshot in <see cref="VolcanoHub.Latest"/>.
        /// </summary>
        public void OnMainThreadUpdate()
        {
            // The buttons are held four at a time by DisasterPanelBar (FeatureHost calls it).
            // ★ This is the only window ⑤ opens. The confirmation window was removed on
            //   2026-08-21 — tile → slider → click the map and the volcano happens.
            VolcanoPanel.Tick();

            // ★ Only while ⑤ is armed, relabel the intensity slider as "the size of the
            //   mountain" (the class doc of VolcanoSizeReadout). On frames when it is not armed
            //   it writes the vanilla text back and does nothing.
            VolcanoSizeReadout.Update();

            // ★ Drawing the eruption is a main-thread-only feature. It is never called from the
            //   sim side. It folds itself away the moment the setting is turned off (so no plume
            //   is left behind while it is off).
            //   What it draws is **the game's own particle effects** (VolcanoEruptionFx).
            bool eruptionFx = ModSettings.VolcanoEruptionFx.value;
            if (eruptionFx)
            {
                VolcanoEruptionFx.Update(VolcanoHub.Latest);
            }
            else
            {
                VolcanoEruptionFx.Destroy();
                // ★ The crater's magma pool, light and lightning are called from inside the
                //   plume drawing, so when the plume is turned off this folds itself away too
                //   (it is not kept hanging around).
                VolcanoCraterFx.Destroy();
            }

            // ★ The dust of the pyroclastic-flow "lookalike". **It can be turned off
            //   independently, with its own setting** — the particle count per lobe is large and
            //   some people will want it gone.
            //   This is not a reproduction of a pyroclastic flow (the class doc of
            //   VolcanoPyroclasticFx).
            bool pyroclasticFx = ModSettings.VolcanoPyroclasticFx.value;
            if (pyroclasticFx) VolcanoPyroclasticFx.Update(VolcanoHub.Latest);
            else VolcanoPyroclasticFx.Destroy();

            // ★ While both are off, let go of the borrowed clones too (do not keep holding them
            //   while off). They are rebuilt on the frame they are turned back on.
            if (!eruptionFx && !pyroclasticFx) VolcanoVanillaFx.Destroy();

            // ★ The eruption sound is a main-thread-only feature too. It is never called from the
            //   sim side. **Call it exactly once per frame** (stack two and one sound occupies a
            //   whole vanilla sound-effect slot; the class doc of VolcanoEruptionAudio).
            //   Release the clip the moment it is turned off (do not hold several MB while off).
            if (ModSettings.VolcanoEruptionSound.value)
            {
                VolcanoEruptionAudio.Update(VolcanoHub.Latest);
            }
            else
            {
                VolcanoEruptionAudio.Destroy();
            }

            // ★ Drawing the lava is a main-thread-only feature too. It is never called from the
            //   sim side (the substance of T9's independence; the grep in the class doc of
            //   VolcanoLavaFx).
            if (ModSettings.VolcanoLavaRender.value) VolcanoLavaFx.Update(VolcanoHub.Latest);
            else VolcanoLavaFx.Destroy();

            // ★ The volcanic-earthquake shake. **It does not look at a single one of ②'s
            //   settings** (the class doc of VolcanoTremorShake). Stop adding to it the moment it
            //   is turned off and it is gone on the next frame
            //   (CameraController.LateUpdate resets it to 0 every frame).
            if (ModSettings.VolcanoQuake.value) VolcanoTremorShake.Update(VolcanoHub.Latest);
            else VolcanoTremorShake.Reset();
        }

        public void OnLevelUnloading()
        {
            // ★★ Do not let the player leave the city with the placement tool still selected.
            //    If the next city starts with the cursor still on "place a volcano", the player
            //    could pick a spot unintentionally (and terrain cannot be undone).
            //    **It does nothing when not active**, so it never reaches in and reverts a tool
            //    another mod had selected.
            VolcanoPlacementTool.Deactivate();

            // ★ Fold the UI away first, so that the second city starts with **one button and one
            //    panel** (leave them and one more piles up on every city load).
            //    Removing the button is done by FeatureHost.LevelUnloading via
            //    DisasterPanelBar.Remove.
            VolcanoPanel.Destroy();

            // ★ For the slider label, **just drop the reference without touching it** (it has
            //   already been destroyed). Writing the text back when leaving the tool has already
            //   been done by VolcanoPlacementTool.Deactivate (the line above calls it).
            VolcanoSizeReadout.Reset();

            // ★ Reset the clock on the eruption's drawing side.
            VolcanoEruptionFx.Destroy();
            // ★ The crater's Mesh / Material / Texture2D are Object.Destroy'd by us too
            //   (none of them is a Component, so they do not go down with the GameObject).
            VolcanoCraterFx.Destroy();
            VolcanoPyroclasticFx.Destroy();

            // ★★ The borrowed clones (the GameObject and the particle systems created inside it)
            //    are destroyed by us. The inner clones hang under the "Particle Effects" root
            //    (DontDestroyOnLoad) and **do not go down with the outer object**, so skip this
            //    and one more set of particle systems is left behind every time the player enters
            //    and leaves a city.
            VolcanoVanillaFx.Destroy();
            // ★ The eruption sound's AudioClip and AudioInfo are Object.Destroy'd by us too
            //   (neither is a Component, so they do not go down with the GameObject).
            //   Skip this and one more multi-MB clip is left behind every time the player enters
            //   and leaves a city.
            VolcanoEruptionAudio.Destroy();
            // ★ The lava's Mesh / Material / Texture2D are Object.Destroy'd by us too
            //   (none of them is a Component, so they do not go down with the GameObject).
            VolcanoLavaFx.Destroy();
            // ★ Do not carry over the volcanic-earthquake clock or the camera reference either.
            VolcanoTremorShake.Reset();

            VolcanoHub.Clear();
            // ★ Do not carry the terrain measurements (the length of RawHeights) across cities.
            //    Carry them and the second city would be quoting the previous city's facts.
            VolcanoReader.Reset();
            // ★ Do not carry over the phase or the survey result either. Carry them and a volcano
            //    at the previous city's location keeps growing in the next one.
            VolcanoState.Reset();
        }

        /// <summary>
        /// **The main purpose of this task.** Report whether ⑤ can write terrain, with the
        /// presence or absence of each row carrying meaning.
        ///
        /// **An item that could not be resolved is reported as <c>NOT RESOLVED</c>, with one line
        /// below it saying what becomes impossible.** That we do not display guessed values is
        /// shown by the presence or absence of the rows themselves (design doc §6).
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.VolcanoEnabled.value ? "yes" : "no");

            var snapshot = VolcanoHub.Latest;
            b.Line(1, "snapshot", snapshot == null
                ? "none yet"
                : (snapshot.Valid ? "valid" : "INVALID"));

            if (snapshot == null || !snapshot.Valid) return;

            WriteTerrain(b, snapshot.Terrain);
            WriteBudgets(b);
            WriteMode(b, snapshot.GameMode);
            WriteDlc(b, snapshot.Terrain);
            WriteUiState(b);
            WriteAudio(b);
            WriteState(b, snapshot);
            WriteNotes(b);
        }

        /// <summary>
        /// **Where the explanations taken off the screen went.**
        ///
        /// The owner's instruction was "you don't need to show all these explanations" and
        /// "make it happen on a click like the other disasters". ⑤ removed the confirmation
        /// window entirely, so all the explanation is here and on the volcano tab.
        /// **What was dropped is where the explanation lives, not the information.**
        /// This file is what testers and bug reports read; it is not where someone trying to
        /// build a mountain reads.
        ///
        /// ★ This is the sim thread (the class doc of <c>DiagnosticDump</c>).
        ///   Keep it to constant lines that touch neither the game's buffers nor the UI.
        /// </summary>
        private static void WriteNotes(DiagnosticBuilder b)
        {
            b.Line(1, "note: counts",
                "the building and road counts on the volcano tab are what the survey "
                + "counted at the moment of the click. The city keeps changing while the "
                + "ground is cleared, so the real number will differ. That is why the row "
                + "says \"approx.\"");
            b.Line(1, "note: no confirmation",
                "clicking the map places the volcano straight away - there is no "
                + "confirmation step. The terrain change is still permanent and is saved; "
                + "the only way out after the click is the Stop button on the volcano tab, "
                + "and that only cancels what has not happened yet");
            b.Line(1, "note: why clear first",
                "raising the ground without destroying the roads and buildings first does "
                + "not work: the game pins the terrain back to the height of every road and "
                + "building on every update, so the mountain would end up full of flat "
                + "trenches and bowls (design section 1.2)");
            b.Line(1, "note: unfinished volcano",
                "Disaster + does not store an unfinished volcano. After loading a save made "
                + "mid-build the mountain stays exactly as far as it got - no crater, no "
                + "eruption, no lava - and there is no way to finish or remove it. Placing a "
                + "new volcano on the same spot piles a second mountain on top of it");
            b.Line(1, "note: buildable ground",
                "the buildable ground and the water level do not follow the visible terrain "
                + "straight away; they catch up at 2 m per 64 simulation frames "
                + "(design section 7.3). This is not a bug");
        }

        /// <summary>
        /// The two or three lines for the eruption sound.
        ///
        /// **This is the only place to narrow down why there is no sound.** "Turned off in the
        /// settings", "the bundled wav is missing or corrupt" and "the game-side path does not
        /// resolve" all look like the same silence to the player. Give the three their own lines.
        ///
        /// ★★ <b>This is the sim thread</b> (the class doc of <c>DiagnosticDump</c>: main asks
        ///   via a hotkey and sim assembles it). So **do not call <c>ScanAudioFacts</c> from
        ///   here** — that touches <c>File.Exists</c> and <c>PluginManager.GetInstances</c>,
        ///   which must not be brought onto the sim thread. All that is read is the
        ///   <c>LastFacts</c> cache that main (<c>Assumptions.Run</c>) scans and stores on every
        ///   level load.
        /// </summary>
        private static void WriteAudio(DiagnosticBuilder b)
        {
            if (!ModSettings.VolcanoEruptionSound.value)
            {
                b.Line(1, "eruption sound", "off (setting)");
                return;
            }

            if (!VolcanoEruptionAudio.FactsScanned)
            {
                // Do not mix "not scanned yet" with "there is no path".
                b.Line(1, "eruption sound", "not scanned yet");
                return;
            }

            VolcanoAudioFacts facts = VolcanoEruptionAudio.LastFacts;

            if (!facts.Usable)
            {
                b.Line(1, "eruption sound", "NO USABLE AUDIO PATH (effectGroup="
                    + (facts.EffectGroupResolved ? "ok" : "missing") + ", addEvent="
                    + (facts.AddEventResolved ? "ok" : "missing") + ", clipApi="
                    + (facts.ClipApiResolved ? "ok" : "missing") + ")");
                b.Line(2, "consequence",
                    "the eruption is silent. Nothing else is affected: the mountain, the "
                    + "plume and the lava do not depend on the audio path");
                return;
            }

            b.Line(1, "eruption sound", facts.FileFound
                ? "file present (" + facts.FileBytes + " bytes)"
                : "NO FILE - " + VolcanoEruptionAudio.AudioFolderName + "\\"
                  + VolcanoEruptionAudio.FileName + " is not in the mod folder; "
                  + "the eruption is silent");
            b.Line(2, "clip", VolcanoEruptionAudio.Detail);
        }

        /// <summary>
        /// The phase, and the latest survey result.
        ///
        /// **Always report <c>refusal</c>.** This is the only way to tell "it was refused" from
        /// "nothing is happening" (handled the same way as ④'s <c>TyphoonSnapshot.Refusal</c>).
        ///
        /// **Roads that could not be counted are reported as <c>not counted</c>. Do not mix that
        /// with 0** (the doc of <see cref="VolcanoFootprint.SegmentCount"/>).
        /// </summary>
        private static void WriteState(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            b.Line(1, "phase", snapshot.Phase.ToString());
            b.Line(1, "placement tool", VolcanoPlacementTool.IsActive ? "active" : "idle");

            if (!string.IsNullOrEmpty(snapshot.Refusal))
            {
                b.Line(1, "refusal", snapshot.Refusal);
            }

            VolcanoFootprint f = snapshot.Footprint;
            if (!f.Valid)
            {
                b.Line(1, "survey", "none yet");
                return;
            }

            b.Line(1, "survey", "(" + f.Centre.X.ToString("F0") + ","
                                + f.Centre.Z.ToString("F0") + ")  ground "
                                + f.GroundHeightMetres.ToString("F1") + " m");
            b.Line(2, "shape", f.Form + "  r=" + f.RadiusMetres.ToString("F0")
                               + " m  h=" + f.HeightMetres.ToString("F0") + " m"
                               + (f.HeightLimitedByCeiling ? "  (LIMITED by the 1024 m ceiling)" : ""));
            b.Line(2, "counts", "buildings " + f.BuildingCount + ", segments "
                                + (f.SegmentCount < 0 ? "not counted" : f.SegmentCount.ToString())
                                + (f.Capped ? "  (CAPPED: these are a lower bound)" : ""));
            b.Line(2, "uplift tiles", f.TileCount.ToString());
            b.Line(2, "block height catch-up", f.BlockHeightCatchUpFrames
                                               + " sim frames (this is not a bug)");

            WriteClearing(b, snapshot);
        }

        /// <summary>
        /// The clearing's (T5) tally. **Report it even when the number destroyed is 0** — so that
        /// "the feature is dead" and "there is nothing in range" can be told apart in the
        /// diagnostics (which is what actually happened in ③).
        ///
        /// Always report <c>refused</c>. If it is not 0, the cells under them stay pinned at
        /// their original height and are left behind by the uplift (point 4 in the class doc of
        /// <see cref="VolcanoClearing"/>).
        /// </summary>
        private static void WriteClearing(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (!snapshot.ClearingPathAvailable)
            {
                b.Line(1, "clearing", "NO USABLE DESTRUCTION PATH (roads and/or buildings)");
                b.Line(2, "consequence",
                    "no volcano is built at all: raising the ground without removing the roads "
                    + "and buildings first leaves flat trenches and bowls where they stood");
                return;
            }

            b.Line(1, "clearing", "swept " + snapshot.ClearedRadiusMetres.ToString("F0")
                                  + " m of " + snapshot.Footprint.RadiusMetres.ToString("F0")
                                  + " m" + (snapshot.ClearingComplete ? " (complete)" : "")
                                  + (snapshot.ClearingCapped
                                        ? "  (CAPPED: the front was not reached this pass)"
                                        : ""));
            b.Line(2, "removed", "buildings " + snapshot.BuildingsDestroyed
                                 + ", roads " + snapshot.SegmentsDestroyed
                                 + ", refused " + snapshot.BuildingsRefused);

            if (!string.IsNullOrEmpty(VolcanoClearing.LastFailure))
            {
                b.Line(2, "clearing failure", VolcanoClearing.LastFailure);
            }

            WriteUplift(b, snapshot);
        }

        /// <summary>
        /// The uplift's (T6) tally.
        ///
        /// ★ <c>cells written</c> and <c>active radius</c> are the only two things that let you
        /// tell trap 1 and trap 2 apart in the live game —
        /// if <c>cells written</c> is 0, either one tick's increment is vanishing in the rounding
        /// or the target has already been reached. If <c>active radius</c> is not growing, the
        /// clearing has stalled.
        ///
        /// ★ <c>refused buildings</c> is reported here too because it *is* the scale of the
        /// feedback loop of §A-3 (every time a building with <c>m_flattenTerrain == false</c>
        /// moves, one extra <c>UpdateArea</c> is added).
        /// </summary>
        private static void WriteUplift(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            b.Line(1, "uplift", "tick " + VolcanoUplift.Ticks + "/" + VolcanoUplift.TotalTicks
                                + "  progress " + (snapshot.ProgressUnit * 100f).ToString("F0")
                                + "%  summit +" + snapshot.SummitMetres.ToString("F1") + " m"
                                + (snapshot.UpliftComplete ? " (complete)" : ""));
            b.Line(2, "active radius", snapshot.ActiveRadiusMetres.ToString("F0")
                                       + " m (as far as the clearing has reached)");
            // ★ flush 1/1 means "the whole area changed this tick made it on screen in the same
            //   tick" = the smoothest state. 2 or more means it has been split and fallen back to
            //   round-robin tiling, and the visible step is the rise over that many flushes
            //   (the class doc of VolcanoUplift).
            //   **This one line is the only way to narrow down "it heaves up in steps".**
            b.Line(2, "cells written", VolcanoUplift.CellsWrittenLastTick
                                       + " last tick; flush " + snapshot.UpliftTileCursor
                                       + "/" + snapshot.UpliftTileCount
                                       + " (1/1 = the whole change reached the screen this tick)"
                                       + "; footprint " + VolcanoUplift.FootprintTileCount
                                       + " tile(s)");
            b.Line(2, "summit crater", snapshot.CraterFormed
                ? "at full depth" : "still shallower than its final depth");
            // The relief on the flanks. At 0% it is a smooth cone exactly (the diagnostics state
            // the meaning of the setting too).
            b.Line(2, "flank relief", ModSettings.VolcanoReliefStrength.value
                                      + "% (0 = a smooth cone)"
                                      + (VolcanoUplift.Complete
                                         ? ""
                                         : "; rising "
                                           + VolcanoUplift.RiseMetresPerTick.ToString("F2")
                                           + " m per uplift tick ("
                                           + VolcanoUplift.RiseMetresPerFrame.ToString("F3")
                                           + " m per sim frame)"));

            // The lag of the buildable ground and the water level. **This is not a bug**
            // (design doc §7.3).
            // Derive the conversion from FeatureHost.FramesPerMinute (never hard-code the constant).
            int frames = snapshot.Footprint.BlockHeightCatchUpFrames;
            float framesPerMinute = FeatureHost.FramesPerMinute;
            string catchUp = frames + " frames";
            if (framesPerMinute > 0f)
            {
                catchUp += " (about " + (frames / framesPerMinute / 60f).ToString("F1")
                           + " in-game hours)";
            }
            b.Line(2, "block heights catch-up", catchUp + " - this is not a bug");

            b.Line(2, "refused buildings still pinning",
                snapshot.BuildingsRefused
                + " (each one keeps its cell at the original height and adds a terrain "
                + "update of its own)");

            if (!string.IsNullOrEmpty(VolcanoUplift.LastFailure))
            {
                b.Line(2, "uplift failure", VolcanoUplift.LastFailure);
            }

            WriteEruption(b, snapshot);
        }

        /// <summary>
        /// The eruption (T7). **Borrowed effects being unusable is not a bug** — the eruption
        /// works on ⑤'s own ejecta alone. That is why
        /// <c>not available in this environment</c> comes with a note saying so.
        ///
        /// ★ The four borrowed items get a line each because this is the only material available
        ///   for narrowing down "I can't see anything" in the live game. <c>NOT resolved</c> only
        ///   means that one thing is not drawn; the eruption, the mountain and the lava all keep
        ///   going.
        ///
        /// ★★ <b>This is the sim thread</b> (the class doc of <c>DiagnosticDump</c>: main asks
        ///   via a hotkey and sim assembles it). So **do not call <c>VolcanoVanillaFx</c>'s
        ///   resolution path from here** — that touches <c>Object.Instantiate</c> and
        ///   <c>ParticleSystem</c>. All that is read is the <c>bool</c> / <c>int</c> /
        ///   <c>string</c> cache the main thread wrote while drawing
        ///   (<c>WriteAudio</c> has the same shape for the same reason).
        /// </summary>
        private static void WriteEruption(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (!snapshot.EruptionActive && VolcanoEruption.BurstsSoFar == 0) return;

            b.Line(1, "eruption", (snapshot.EruptionActive ? "active" : "finished")
                                  + " (intensity "
                                  + snapshot.EruptionIntensityUnit.ToString("F2")
                                  + ", " + VolcanoEruption.BurstsSoFar + " bursts"
                                  + (VolcanoEruption.Building
                                     ? ", still building the mountain" : "")
                                  + (VolcanoEruption.InClimax
                                     ? ", THE CALDERA IS COLLAPSING (climactic blast)" : "")
                                  + ")");
            var facts = VolcanoEruptionFx.Facts;

            // ★★ **The swarm of plume puffs** (the owner's request for "chaotic smoke").
            //    It is a different thing from the game particles' ash column, so it gets its own
            //    line — merge them and "the plume is showing" hides the fact that the puffs have
            //    disappeared.
            b.Line(2, "plume puffs", VolcanoPlumePuffFx.Drawing
                ? VolcanoPlumePuffFx.PuffsPlaced + " cloud puffs (own ParticleSystem, "
                  + "the MissileDisaster mushroom-cloud technique)"
                : "not drawing"
                  + (VolcanoPlumePuffFx.LastFailure != null
                     ? " (" + VolcanoPlumePuffFx.LastFailure + ")" : ""));

            // ★ A blast's size is decided not by "once" but by "how many bursts it was split
            //   into" (Core.Volcano.BlastCluster). **Without the number you cannot tell why it
            //   looks feeble.**
            b.Line(2, "blast bursts", VolcanoBlastFx.BurstsLastBlast > 0
                ? VolcanoBlastFx.BurstsLastBlast + " per blast"
                  + (snapshot.RingFissureRadiusMetres > 0f
                     ? " (some along the ring fissure at r="
                       + snapshot.RingFissureRadiusMetres.ToString("F0") + " m)"
                     : "")
                : "none yet");

            b.Line(2, "crater effects", VolcanoEruptionFx.Drawing
                ? "drawing (the game's own particle effects)"
                : (ModSettings.VolcanoEruptionFx.value ? "not drawing" : "off (setting)"));

            // ★ Always state what could be looked up and what could be cloned. It is the only
            //   clue if a future game update makes everything silently stop appearing.
            b.Line(3, "borrowed effects", VolcanoEruptionFx.Detail);
            b.Line(3, "ash plume", facts.AshResolved
                ? VolcanoVanillaFx.AshName + " (no DLC needed)" : "NOT resolved");
            // ★ The plume is emitted not once but as "segments of a column"
            //   (Core/Volcano/EruptionColumn). 0 segments means not one column is standing —
            //   "it resolved" and "it is showing" are different things.
            // ★ The crater's magma pool, the light on the plume, and volcanic lightning
            //   (2026-08-22). **Report it even when nothing is being drawn** — otherwise
            //   "it is turned off", "the shader could not be looked up" and "it is not glowing
            //   right now" cannot be told apart in the log.
            b.Line(3, "crater glow", VolcanoCraterFx.MaterialResolved
                ? (VolcanoCraterFx.Drawing ? "drawing" : "idle (not erupting this frame)")
                : "NO MATERIAL - the magma pool, the light and the lightning are not drawn");
            b.Line(3, "volcanic lightning", ModSettings.VolcanoLightningFx.value
                ? VolcanoCraterFx.BoltsDrawn + " bolt(s) lit this frame"
                : "off (setting)");
            b.Line(3, "eruption column", VolcanoEruptionFx.PlumeSegments + " of "
                + EruptionColumn.MaxSegments + " segment(s) this frame, "
                + VolcanoEruptionFx.PlumeHeightMetres.ToString("F0") + " m tall");
            b.Line(3, "flames", facts.FlameResolved
                ? VolcanoVanillaFx.FlameName + " (the game's own building fire, no DLC needed)"
                : "NOT resolved");
            b.Line(3, "ejecta", facts.EjectaResolved
                ? VolcanoVanillaFx.EjectaName + " (no DLC needed)" : "NOT resolved");

            if (!facts.CameraInfoResolved)
            {
                b.Line(3, "camera info", "NOT resolved - nothing is drawn this frame");
            }

            // ★★ Pyroclastic flows **do not exist in vanilla**. The diagnostics state that this
            //    is a stand-in.
            b.Line(2, "pyroclastic flow", ModSettings.VolcanoPyroclasticFx.value
                ? (VolcanoPyroclasticFx.DustResolved
                    ? VolcanoPyroclasticFx.BandsDrawn + " of "
                      + PyroclasticSurge.LobeCount + " lobe(s) of "
                      + VolcanoVanillaFx.DustName
                      + " fanning down the flanks, NOT a real pyroclastic flow; the game has "
                      + "no such effect. It damages nothing"
                    : "NOT resolved")
                : "off (setting)");

            // ★ The particle drawing path emits no sound. **Measured in IL** (RenderEffect never
            //   touches m_soundEffect; the sound is on the PlayEffect path).
            //   ⑤'s eruption sound is a separate path in VolcanoEruptionAudio.
            b.Line(2, "sound", "the particle path is silent by design; the eruption sound is "
                               + "Disaster +'s own file on a separate audio path");

            if (!string.IsNullOrEmpty(VolcanoEruption.LastFailure))
            {
                b.Line(2, "eruption failure", VolcanoEruption.LastFailure);
            }

            WriteLava(b, snapshot);
        }

        /// <summary>
        /// The lava (T8). **When the flow count is 0 (disabled in the settings) the rows are not
        /// emitted at all** — do not mix "0 flows ran" with "it is turned off".
        ///
        /// ★ <c>slope sign</c> is the most important line in this feature.
        ///   Get the sign of the gradient wrong and the lava climbs the mountain, with not one
        ///   exception thrown (§8.1). It has been settled in IL, so if this does not move on from
        ///   <c>NOT VERIFIED</c>, a game update has changed the behaviour.
        ///
        /// ★ The two lines on trees and roads are **explanations of what cannot be done**.
        ///   Neither is a corner cut by ⑤ but a constraint on the game side, and the diagnostics
        ///   say so.
        /// </summary>
        private static void WriteLava(DiagnosticBuilder b, VolcanoSnapshot snapshot)
        {
            if (snapshot.LavaFlowCount <= 0)
            {
                if (snapshot.Phase == VolcanoPhase.Flowing
                    || snapshot.Phase == VolcanoPhase.Cooling)
                {
                    b.Line(1, "lava", "off (the number of flows is set to 0)");
                }
                return;
            }

            b.Line(1, "lava", snapshot.LavaAliveCount + "/" + snapshot.LavaFlowCount
                              + " flows alive, longest "
                              + snapshot.LavaLongestMetres.ToString("F0") + " m, cooling "
                              + (snapshot.LavaCoolUnit * 100f).ToString("F0") + "% left");

            b.Line(2, "slope sign", VolcanoLava.SlopeSignVerified
                ? "verified at runtime (the first steps of a flow lost altitude)"
                : "NOT VERIFIED YET (a flow has not finished its observation window)");

            // ★ refused is **the number of calls**, not the number of buildings (the doc of
            //   VolcanoLava). The same building is hit repeatedly, so it is normal for it to be
            //   far larger than the number that burned.
            b.Line(2, "ignited", "buildings " + snapshot.LavaBuildingsIgnited
                                 + " (refused calls " + VolcanoLava.BuildingsRefused
                                 + "; mostly re-hits on buildings that are already burning)"
                                 + ", trees " + snapshot.LavaTreesIgnited);

            b.Line(2, "trees", snapshot.LavaTreesAvailable
                ? "burnable (Natural Disasters DLC is owned)"
                : "not burnable without the Natural Disasters DLC - the game itself refuses, "
                  + "so Disaster + leaves them standing (this is normal)");

            b.Line(2, "roads", "never burn - the game has no API for it at all; only the roads "
                               + "inside the footprint are removed, during the clearing phase");

            b.Line(2, "trail points", snapshot.LavaTrailPoints == null
                ? "0" : snapshot.LavaTrailPoints.Length.ToString());

            if (VolcanoLava.OutsidePurchasedArea)
            {
                b.Line(2, "outside the purchased area",
                    "the lava left the tiles you own; terrain sampling drops from 4 m detail to "
                    + "16 m interpolation there (this is normal, not a bug)");
            }

            if (!string.IsNullOrEmpty(VolcanoLava.LastFailure))
            {
                b.Line(2, "lava failure", VolcanoLava.LastFailure);
            }

            // ★ Drawing the lava (T9). **This one line is T9's only diagnostic output**
            //   (only four files reference that type; the grep in its class doc).
            //   Always state which shader it resolved with — it is the only clue if a future game
            //   update makes it silently invisible.
            b.Line(1, "lava surface", VolcanoLavaFx.Drawing
                ? VolcanoLavaFx.DrawCalls + " draw call/frame, "
                  + VolcanoLavaFx.PointsDrawn + " points"
                : (ModSettings.VolcanoLavaRender.value ? "not drawing" : "off (setting)"));
            b.Line(2, "material", VolcanoLavaFx.ShaderDetail);
        }

        /// <summary>
        /// The UI state. The same shape as ①②③④ (it just asks <see cref="DisasterPanelBar"/>).
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            // The button is not ⑤'s own; DisasterPanelBar places all four together. This mod no
            // longer decides the coordinates, so all that is reported is "is it there" and
            // "where is it".
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdVolcano)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");
            b.Line(1, "panel body", VolcanoPanel.IsVisible ? "shown" : "hidden");
        }

        /// <summary>
        /// The four terrain-API lines. **Whether ⑤ can run at all is decided here and nowhere else.**
        /// </summary>
        private static void WriteTerrain(DiagnosticBuilder b, VolcanoTerrainFacts terrain)
        {
            if (!terrain.HeightsResolved)
            {
                b.Line(1, "terrain", "NOT RESOLVED (TerrainManager.RawHeights is unavailable)");
                b.Line(2, "consequence",
                    "no volcano can be built at all: this array is the ground itself");
            }
            else if (terrain.RawArrayLength != VolcanoTerrainFacts.ExpectedRawArrayLength)
            {
                b.Line(1, "terrain", "UNUSABLE: RawHeights holds " + terrain.RawArrayLength
                                     + " cells, expected "
                                     + VolcanoTerrainFacts.ExpectedRawArrayLength + " (1081^2)");
                b.Line(2, "consequence",
                    "no volcano can be built: every cell index is z*1081+x, so a different "
                    + "length would raise unrelated parts of the map");
            }
            else
            {
                b.Line(1, "terrain", "RawHeights " + terrain.RawArrayLength
                                     + " cells (1081^2), "
                                     + VolcanoShape.RawCellSizeMetres.ToString("F0")
                                     + " m per cell, 1/64 m quantum");
            }

            if (terrain.UpdateAreaResolved)
            {
                b.Line(1, "update path",
                    "resolved (TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool))");
            }
            else
            {
                b.Line(1, "update path", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "no volcano can be built: written heights would never reach the game");
            }

            if (terrain.BurnGroundResolved)
            {
                b.Line(1, "lava scorch", "resolved (DisasterHelpers.BurnGround)");
            }
            else
            {
                b.Line(1, "lava scorch", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "the ground is not scorched along the lava; the mountain, the summit "
                    + "crater and the lava themselves still work");
            }

            if (terrain.SlopeSampleResolved)
            {
                b.Line(1, "slope sampling",
                    "resolved (TerrainManager.SampleDetailHeight(Vector3, out, out))");
            }
            else
            {
                b.Line(1, "slope sampling", "NOT RESOLVED");
                b.Line(2, "consequence",
                    "the lava cannot find its way downhill, so no lava flows at all. The "
                    + "mountain, the clearing and the eruption are unaffected");
            }
        }

        /// <summary>
        /// The two limits ⑤ imposes on itself (trap 3), and the terrain height ceiling (§C-10).
        /// **Both are this mod's numbers, not values the game computed.**
        /// </summary>
        private static void WriteBudgets(DiagnosticBuilder b)
        {
            b.Line(1, "tile budget",
                TileSplit.CoreTileSide + " core + " + TileSplit.Margin + " margin = "
                + TileSplit.MaxPassedSide + " per side, " + TileSplit.MaxPassedCells
                + " cells (limits: 128 side / 10000 cells)");

            b.Line(1, "ceiling", VolcanoShape.MaxTerrainMetres.ToString("F2") + " m absolute");
        }

        /// <summary>
        /// Game mode or editor. **Do not keep two constants; pick from the actual mode.**
        /// The catch-up speed of <c>m_blockHeights</c> changes (the table in §A-2), which directly
        /// changes the estimated lag of the buildable ground and the water level.
        /// </summary>
        private static void WriteMode(DiagnosticBuilder b, bool gameMode)
        {
            // 2 m in game, 8 m in the editor (§A-2). UpliftSchedule only holds the game-mode
            // constant, so in the editor we state that it is four times faster.
            float gameMetres = UpliftSchedule.BlockHeightRiseRawPerCycle
                               / UpliftSchedule.RawUnitsPerMetre;
            float metres = gameMode ? gameMetres : gameMetres * 4f;

            b.Line(1, "mode", (gameMode ? "game" : "editor")
                              + " (block heights rise " + metres.ToString("F0")
                              + " m per " + UpliftSchedule.BlockHeightCycleFrames
                              + " sim frames)");
        }

        /// <summary>
        /// **Not owning the DLC is not a FAIL.** ⑤ does not need Natural Disasters
        /// (design doc §1.4). The only thing that branches on it is igniting trees (§B-7c).
        /// </summary>
        private static void WriteDlc(DiagnosticBuilder b, VolcanoTerrainFacts terrain)
        {
            if (!terrain.NaturalDisastersOwned)
            {
                b.Line(1, "Natural Disasters DLC",
                    "not owned (trees will not burn; everything else works)");
                return;
            }

            // ★ Do not mix "measured as owned" with "the check failed so we assumed owned"
            //   (whole-project review M11). Printing "owned" for the latter hands a false clue to
            //   someone trying to work out why the trees do not burn.
            b.Line(1, "Natural Disasters DLC", ModCompat.NaturalDisastersOwnedKnown
                ? "owned"
                : "ASSUMED owned - the DLC check itself failed. If the trees do not burn, "
                  + "this is why: the game refuses BurnTree without the DLC and says nothing");
        }
    }
}
