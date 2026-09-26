namespace DisasterPlus.Game
{
    /// <summary>
    /// ④ Typhoon. Built on top of one vanilla thunderstorm disaster slot, ④ drives the
    /// track, weather, lightning, wind damage, river flooding and a huge rotating cloud
    /// every tick — **a typhoon that moves**.
    ///
    /// **Unlike ① and ②, ④ has no source material in vanilla.** The typhoon phenomenon
    /// does not exist in vanilla, and neither does a wind destruction mechanism, a cloud
    /// with world coordinates, or a flood disaster (IL facts document §B5 / §C7 / §D9).
    /// So the numbers ④ shows are **as a rule all of them this mod's own**; the panel
    /// says so once in a heading and puts no marker on individual rows (design doc §1.2 /
    /// §7). The only exception is the rainfall and cloud cover read from
    /// <c>WeatherManager</c>.
    ///
    /// As of this task (Task 2) it is **a feature with neither a panel nor a typhoon**.
    /// All it does is read on the sim thread and publish to <see cref="TyphoonHub"/>, and
    /// **put the six prefab values into the diagnostics dump**. Those six values
    /// (<c>ThunderStormAI</c>'s <c>m_radius</c> / <c>m_emergingDuration</c> /
    /// <c>m_activeDuration</c>, plus <c>VortexAI</c>'s <c>m_destructionRadiusMin</c> /
    /// <c>m_destructionRadiusMax</c> and <c>VehicleInfo.m_maxSpeed</c>) **have no actual
    /// values in the DLL** (§A-0 / §B-1, both PARTIAL), and everything ④ does later —
    /// duration, number of strikes, destruction radius, travel speed — rides on top of
    /// them, so we measure them once in the game first.
    ///
    /// It implements <see cref="IPausedTickFeature"/> for the same reason ① and ② do
    /// (open the panel while still paused right after a load and every row reads "cannot
    /// be read"). **④ does, however, advance game state from T3 onwards.** The machinery
    /// that keeps that contract is inside <see cref="OnSimulationTick"/>.
    /// </summary>
    public partial class TyphoonFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Typhoon";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            TyphoonHub.Clear();
            TyphoonReader.Reset();
            TyphoonController.Reset();
            TyphoonWeather.Reset();

            // ★★ **Register again on every level load.** ToolController.m_tools is built
            //    once in Awake and ToolsModifierControl.SetTool<T> only looks up a static
            //    dictionary, so without registering it **silently does nothing** (a kind
            //    of breakage with no exception: "the tile presses but the cursor never
            //    changes"). ToolController is rebuilt per city, so the previous city's
            //    registration is no use.
            ToolRegistration.Register<TyphoonPlacementTool>();
        }

        /// <summary>
        /// Sim thread. Always read <c>DisasterManager</c> / <c>WeatherManager</c> /
        /// <c>SimulationManager</c> here.
        ///
        /// It is also called while paused (deltaMinutes == 0)
        /// (<see cref="IPausedTickFeature"/>).
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.TyphoonEnabled.value)
            {
                // ★★ Even with the feature switched off, **give back everything we
                //    touched.** If the player switches this setting off in the middle of a
                //    typhoon, not one line of this tick runs afterwards, so anything not
                //    given back here has no chance of being given back (until they leave
                //    the city or save). All three return in a single instruction when
                //    their ledgers are empty, so running them every tick is fine (no log,
                //    no allocation).
                //
                //    This sits above the pause guard, but **all of it is in the direction
                //    of "let go of what ④ was holding"** rather than progress, so it does
                //    not break the IPausedTickFeature contract (indeed, being able to let
                //    go when switched off while paused is the correct behaviour). The
                //    tornadoes' StopAll calls vanilla's ending routes (DeactivateNow /
                //    ReleaseDisaster) so **state does change**, but the only change is
                //    "what ④ made gets packed away"; the game does not move forward.
                TyphoonFlood.RestoreAll();

                // ★★ Give back the local-damage counters and the weather too
                //    (whole-project review C2). This used to restore only the water level,
                //    so the weather stayed held while the typhoon alone stopped (the rain
                //    would never end). Reset and Release are both idempotent.
                TyphoonGust.Reset();
                TyphoonWeather.Release();

                // ★ Pack away the wind sweep's cursor and counters as well. The sweep
                //   itself is no longer called, but **if the diagnostics keep holding the
                //   last sweep's numbers it reads as "we switched it off and it is still
                //   destroying things"** (the same reason as the local damage). Reset is
                //   idempotent and holds no ledger, so it is fine every tick.
                TyphoonWind.Reset();
                TyphoonTreeWindPatch.Clear();
            // ★ Props that were blown away do not come back. All we pack away here is the
            //   sweep cursor and the diagnostic counts.
            TyphoonPropDamage.Reset();
                return;
            }

            // Everything to this point is "just read and publish". This runs while paused
            // too.
            var snapshot = TyphoonReader.Read();
            TyphoonHub.Publish(snapshot);

            // The Typhoon channel is off by default. Without this if, the ToString calls
            // and string concatenation below would run on every sim tick (about 50 times
            // a second at normal speed) only to be thrown away by Log.Diag — C# evaluates
            // the arguments fully before the call, so the mask check inside Diag is far
            // too late.
            //
            // Unlike ①'s ForecastFeature, this must not be an early return. That would
            // skip the pause guard below and all the per-element processing along with it
            // (②'s EarthquakeFeature carries the same note).
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "typhoon",
                    snapshot.Valid
                        ? "prefab=" + (snapshot.Prefab.Usable ? "usable" : "UNUSABLE")
                          + " rain=" + snapshot.Rain.ToString("F2")
                          + " cloud=" + snapshot.Cloud.ToString("F2")
                        : "snapshot invalid");
            }

            // ★ Everything below this advances state. It must never run while paused
            //    (deltaMinutes == 0). Anything T3 to T10 adds must go below this line.
            //    Delete this comment and you get "the typhoon moves, buildings fall and
            //    rivers burst their banks while the game is paused".
            if (deltaMinutes <= 0f) return;

            TyphoonController.Tick(snapshot, frameIndex, deltaMinutes);
            if (TyphoonController.Active)
            {
                TyphoonWeather.Drive(snapshot, deltaMinutes);
                // ★★ **We no longer queue vanilla lightning.** (2026-08-25, the owner's
                //    instruction.)
                //
                //    &gt; It might work better to drop the "thunderstorm" and have just
                //    &gt; "rain", with lightning occasionally coming out of the typhoon's
                //    &gt; clouds
                //
                //    The height at which <c>WeatherManager.QueueLightningStrike</c> drops
                //    a bolt is decided by the game's renderer and **cannot be moved from
                //    a mod.** Even with the cloud raised to 1200 m it still came down from
                //    above (in-game report).
                //
                //    Now, like ⑤'s ash plume, we <b>draw it inside the cloud ourselves</b>
                //    (<c>TyphoonBoltFx</c>, main thread). The game's own automatic
                //    lightning does not happen either, because <c>TyphoonWeather</c> stops
                //    the rain at 0.8 (IL_09D3: <c>m_currentRain &gt; 0.8</c> is the only
                //    condition).
                //
                //    ★ <c>TyphoonLightning</c> has not been deleted — the budget
                //      calculation for vanilla's queue and the measured diagnostics are
                //      written there, and **if the day comes when we use it again we would
                //      have to redo that investigation from scratch**. We simply do not
                //      call it.

                // ★ Wind damage can be switched off in the settings (on by default; a
                //   strength of 0 also disables it completely). Not calling Apply when it
                //   is off is the same shape as ②'s second layer, so that the sweep never
                //   starts at all.
                if (ModSettings.TyphoonWindStrength.value > 0)
                {
                    TyphoonWind.Apply(snapshot, deltaMinutes);

                    // ★★ **Blow away props such as signs too** (2026-08-22, the owner's
                    //    request "destruction of billboard props"). Put it on the same
                    //    setting as wind damage — do not split "things the wind breaks"
                    //    across two knobs.
                    //    ★ ReleaseProp cannot be undone, so the thresholds are mean
                    //      (PropGaleModel's class doc).
                    TyphoonPropDamage.Tick(snapshot, frameIndex);
                }

                // ★ Tornado-grade local damage (on by default; a strength of 0 also
                //   disables it completely). **Not one actual tornado is created**
                //   (TyphoonGust's class doc).
                if (ModSettings.TyphoonGustStrength.value > 0)
                {
                    TyphoonGust.Tick(snapshot, frameIndex, deltaMinutes);
                }

                // ★ The storm's blow-away (citizens and vehicles only). **A separate
                //   setting from wind damage**, and it touches neither buildings, roads
                //   nor trees (the doc on ModSettings.TyphoonStormFx). It is not a sweep,
                //   so it blows even with wind damage switched off.
                if (ModSettings.TyphoonStormFx.value)
                {
                    TyphoonWind.Gale(snapshot, deltaMinutes);
                }

                // ★★ **Shake the trees.** (2026-09-02, the owner: "make the trees sway
                //    much harder".) In vanilla, tree sway is decided by <b>the shelter
                //    height alone</b> and does not react to the weather at all. On top of
                //    that <c>GetWindSpeed</c> ends with <c>Clamp(…, 0, 2)</c>, so lowering
                //    the shelter <b>tops out at twice the normal sway</b> (we implemented
                //    that first, and it was not enough). <c>TyphoonTreeWindPatch</c>
                //    multiplies <b>outside that clamp</b>. It does not touch a single byte
                //    of <c>m_windGrid</c>, which is burnt into the save.
                TyphoonTreeWindPatch.SetStorm(
                    snapshot.Centre.X, snapshot.Centre.Z, snapshot.GaleRadius,
                    TreeSwayGainFor(snapshot.Intensity));
            }

            // ★ River flooding is called even with no typhoon. **Putting back the water
            //    level we raised is part of this route's job too** (TyphoonController.
            //    Forget already calls RestoreAll, but we catch the leftovers here).
            //    If it stopped being called the moment the setting went OFF, the rivers
            //    would stay burst, so even when OFF we go as far as "restore if the ledger
            //    is not empty".
            if (ModSettings.TyphoonFloodStrength.value > 0)
            {
                TyphoonFlood.Tick(snapshot, frameIndex, deltaMinutes);
            }
            else
            {
                TyphoonFlood.RestoreAll();
            }

            // ★★ When there is no typhoon, or the setting is off, **always come through
            //    here and pack the counters away**. The patches themselves are a function
            //    of the typhoon's elapsed frames, so with no typhoon they do not exist —
            //    but **if the diagnostics keep holding the "last sweep" numbers it reads
            //    as "the typhoon has gone and it is still destroying things"**.
            //    Reset holds no ledger, so it returns in a single instruction (fine every
            //    tick).
            if (!TyphoonController.Active || ModSettings.TyphoonGustStrength.value <= 0)
            {
                TyphoonGust.Reset();
            }
        }

        /// <summary>
        /// Work out the tree-sway multiplier from the intensity.
        ///
        /// ★★ **Calm is 1.0 and vanilla's ceiling is 2.0** (the clamp at the end of
        ///   <c>GetWindSpeed</c>). <c>TyphoonTreeWindPatch</c> multiplies outside that, so
        ///   the value returned here is directly "how many times the calm sway".
        ///
        /// ★★ **It only affects nearby trees.** (2026-09-02, settled from the IL.)
        ///   There are two tree rendering paths, and the <b>batched draw</b> used for
        ///   distant trees <b>packs it into a byte</b> as
        ///   <c>Color32.a = Clamp(round(wind * 128), 0, 255)</c>
        ///   (<c>TreeInstance.PopulateGroupData</c> IL_00CE-00E5), so whatever we return,
        ///   that path tops out at 1.99. The <c>RenderInstance</c> path that nearby trees
        ///   take passes <c>Color.a</c> (a float) to a <c>MaterialPropertyBlock</c>, and
        ///   <b>there is no ceiling there</b>.
        ///
        ///   The owner's instruction was "make it dramatic at least within view", so
        ///   <b>we put everything into the nearby trees</b>.
        ///
        /// ★ 7× at the default slider (55), 12× at the ceiling (100 and above).
        ///   Distant trees stop at 2×, so near and far will differ visibly.
        ///   **If the shader has a further ceiling of its own, that is where it will top
        ///   out** — and that can only be found out in the game, so we swing big first and
        ///   ask them to look.
        /// </summary>
        private static float TreeSwayGainFor(byte intensity)
        {
            float t = intensity > 100 ? 1f : intensity / 100f;
            return 1f + 11f * t;
        }

        /// <summary>
        /// Main thread. **Do not call the sim-side types (<see cref="TyphoonController"/> /
        /// <see cref="TyphoonWeather"/>) from here.** All we read is the snapshot from
        /// <see cref="TyphoonHub.Latest"/>.
        /// </summary>
        public void OnMainThreadUpdate()
        {
            // ★★ **Camera.main is only ever touched on the main thread** (CameraFocus's
            //    doc). The sim thread's blow-away reads the value we put here.
            CameraFocus.Update();

            // The buttons are all four held by DisasterPanelBar (FeatureHost calls it).
            TyphoonPanel.Tick();

            // ★ The cloud is a main-thread-only feature and **is never once called from
            //   the sim side**. That is what makes T9 independent of ④'s other elements
            //   (TyphoonCloud's class doc). TyphoonCloud.Update also does its own cleanup
            //   when the typhoon ends — do not add this type to
            //   TyphoonController.Forget's cleanup list.
            //   ★ Look at TyphoonEnabled as well. Switching the feature itself off makes
            //     OnSimulationTick return early so TyphoonHub.Latest stops being updated,
            //     and the last published "Active" snapshot stays there for ever — without
            //     this check **a frozen cloud stays pasted on the screen**
            //     (TyphoonPanel has the same guard).
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonCloudEnabled.value)
            {
                TyphoonCloud.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonCloud.Destroy();
            }

            // ★ The driving spray is also a main-thread-only feature (treated exactly like
            //   the cloud). It is never once called from the sim side, so do not add this
            //   type to TyphoonController.Forget's cleanup list. On the frame the typhoon
            //   ends it is handed a snapshot that is not Active and stops drawing itself.
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonStormFx.value)
            {
                TyphoonSquallFx.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonSquallFx.Destroy();
            }

            // ★ The wind sound is main thread only too (all of Unity's audio assets are).
            //   Queue AddEvent once per frame and no more — call it from the sim thread
            //   and at speed 3 it queues several times in one frame and eats the
            //   EffectGroup's seats.
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonStormSound.value)
            {
                TyphoonStormAudio.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonStormAudio.Destroy();
            }
        }

        public void OnLevelUnloading()
        {
            // ★★ Do not let them leave the city with the placement tool still selected.
            //    If the next city starts with the cursor still on "place a typhoon", the
            //    player could point at a spot without meaning to. **It does nothing when
            //    it is not active**, so it never reaches in and undoes a tool another mod
            //    had selected (the same as ⑤).
            TyphoonPlacementTool.Deactivate();

            // ★ Pack up the UI first, so the second city starts with **one button and one
            //    panel** (leave them and one more piles up every time a city is loaded).
            //    Removing the button is done by FeatureHost.LevelUnloading through
            //    DisasterPanelBar.Remove.
            TyphoonPanel.Destroy();
            // ★ Neither Mesh nor Material is a Component, so destroying the GameObject
            //    does not take them with it. **Object.Destroy them ourselves**
            //    (TyphoonCloud's class doc). The vanilla sky's cloud settings are put back
            //    here too.
            TyphoonCloud.Destroy();
            // ★ The spray clones do not cross cities either (same as the cloud; they would
            //   go and fire a destroyed particle system).
            TyphoonSquallFx.Destroy();
            // ★ The sound clips and AudioInfo do not cross cities either (we do not
            //   destroy borrowed clips; TyphoonStormAudio's class doc).
            TyphoonStormAudio.Destroy();

            TyphoonHub.Clear();
            TyphoonReader.Reset();
            // Neither a booking nor a typhoon in progress survives across cities.
            TyphoonController.Reset();
            // ★ Always put the weather overrides back here as well. Even if the typhoon
            //    disappears the moment they leave the city, do not stay holding
            //    m_targetRain.
            TyphoonWeather.Reset();
            // ★ Do not carry the lightning stock over either. Carry it over and the next
            //    city's typhoon sees a queue that is actually free as "full" and never
            //    fires.
            TyphoonLightning.Reset();
            // ★ The wind sweep's cursor and counters do not cross cities either. Carry them
            //    over and the next city starts sweeping from the previous city's ordinal
            //    (i.e. the area around the centre is never checked once).
            TyphoonWind.Reset();
            TyphoonTreeWindPatch.Clear();
            CameraFocus.Reset();
            // ★★ Always put the river water levels back (the second of the two restore
            //    routes for trap 4). Forget this and the next city you open will go and
            //    restore **the previous city's handles**, rewriting the level of an
            //    unrelated river. TyphoonFlood.Reset calls RestoreAll internally before
            //    throwing the ledger away.
            TyphoonFlood.Reset();
            // ★ The local-damage counters and the references to borrowed effects do not
            //   cross cities either. Carry them over and the next city goes and fires
            //   **the previous city's particle effect** (already destroyed).
            TyphoonGust.Reset();
        }
    }
}
