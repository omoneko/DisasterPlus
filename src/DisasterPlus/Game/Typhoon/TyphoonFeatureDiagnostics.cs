using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④ Typhoon's **diagnostics dump lines** (what comes out under <c>Ctrl+F11</c>).
    ///
    /// It is a partial of <see cref="TyphoonFeature"/>. The split was made because we hit
    /// the 800-line limit, and the boundary is drawn between "things that advance game
    /// state" and "things that only read and list" — <b>there is not one line in this file
    /// that changes state</b>.
    ///
    /// **Main thread only.** <c>DiagnosticsHub</c> calls it from main.
    /// There are places that read ints and bools from sim-side statics
    /// (<c>TyphoonGust</c> / <c>TyphoonCloud</c> …), but that is the shape ④ has always
    /// taken (an aligned 32-bit read does not tear, and nobody minds if the display is one
    /// tick out of date).
    /// **Do not write sim-side state from here.**
    /// </summary>
    public partial class TyphoonFeature
    {
        /// <summary>
        /// List everything about how ④ is doing. **Print it all even when 0 collapsed and
        /// even when there is no typhoon** — on screen, "switched off in the settings",
        /// "there are no buildings nearby", "the ceiling stopped it before it reached the
        /// outer rim" and "the game refused all of them" all look identical (nothing
        /// happens), so this is the only place they can be told apart.
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.TyphoonEnabled.value ? "yes" : "no");

            var snapshot = TyphoonHub.Latest;
            b.Line(1, "snapshot", snapshot == null
                ? "none yet"
                : (snapshot.Valid ? "valid" : "INVALID"));

            WriteUiState(b);

            if (snapshot == null || !snapshot.Valid) return;

            WriteStormPrefab(b, snapshot.Prefab);
            WriteWeather(b, snapshot);
            WriteTyphoon(b, snapshot);
            WriteNotes(b);
        }

        /// <summary>
        /// **Where the explanations taken off the settings screen went** (the table in
        /// <c>Mod.OnSettingsUI</c>'s doc).
        ///
        /// The explanations for wind damage, local damage and flooding appear word for word
        /// on ④'s panel (<c>TyphoonEffectRows</c>). What goes here is only **the reasoning
        /// behind the dangerous semicircle**, which is on neither the panel nor the
        /// settings screen.
        ///
        /// ★ This is the sim thread (<c>DiagnosticDump</c>'s class doc).
        ///   Keep it to lines of constants that touch neither the game's buffers nor the UI.
        /// </summary>
        private static void WriteNotes(DiagnosticBuilder b)
        {
            b.Line(1, "note: dangerous side",
                "a real typhoon is not symmetric: on one side the spin and the storm's own "
                + "travel add up. That side gets a slightly wider and slightly more likely "
                + "damage footprint - the right of the track in the northern hemisphere, the "
                + "left in the southern one. Which side is used is a setting");
        }

        /// <summary>
        /// The UI state. The same shape as ①②③⑤ (it only asks
        /// <see cref="DisasterPanelBar"/>).
        ///
        /// **"Does it overlap ① and ②'s buttons" is no longer a diagnostic item.** The
        /// positions of the four are decided by a single loop over a single ordering, so no
        /// route exists in which they overlap (DisasterPanelBar's class doc). All we look
        /// at here is "is ④'s button actually there" and "where is it (inside vanilla's
        /// panel, or on the fallback floating bar)".
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            // The button is not ④'s alone; DisasterPanelBar places all four together. This
            // mod no longer decides the coordinates, so all we print is "is it there" and
            // "where is it".
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdTyphoon)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");
            b.Line(1, "panel body", TyphoonPanel.IsVisible ? "shown" : "hidden");
            // ★ The tile arms a placement cursor (the same contract as vanilla's disaster
            //   buttons). This tells "armed but nothing pointed at yet" from "pointed at
            //   something and nothing happened".
            b.Line(1, "placement tool", TyphoonPlacementTool.IsActive ? "active" : "idle");
        }

        /// <summary>
        /// The typhoon itself.
        ///
        /// **Always print <c>refusal</c>.** This is the only means of telling "it could not
        /// be raised" from "nothing is happening" (plan §3 Step 5).
        /// </summary>
        private static void WriteTyphoon(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.Active)
            {
                b.Line(1, "typhoon", "idle");
                if (!string.IsNullOrEmpty(snapshot.Refusal))
                {
                    b.Line(2, "refusal", snapshot.Refusal);
                }
                WriteShutdown(b, snapshot);
                return;
            }

            b.Line(1, "typhoon", "active  #" + snapshot.TyphoonId
                                 + "  phase=" + snapshot.Phase
                                 + "  intensity=" + snapshot.Intensity);

            b.Line(2, "centre", "(" + snapshot.Centre.X.ToString("F0")
                                + ", " + snapshot.Centre.Y.ToString("F0")
                                + ", " + snapshot.Centre.Z.ToString("F0")
                                + ")  heading=" + DegreesOf(snapshot.HeadingRadians).ToString("F1")
                                + " deg");

            b.Line(2, "elapsed", ElapsedText(snapshot.ElapsedFrames, snapshot.TotalFrames));

            b.Line(2, "radius", "storm " + snapshot.StormRadius.ToString("F0")
                                + " m / gale " + snapshot.GaleRadius.ToString("F0") + " m");

            // Do not mix "could not be read" up with "in 0 minutes".
            // ★ Do not claim more digits than we have (whole-project review). The estimate
            //   of the landfall frame is only stepped in units of 256 frames
            //   (LandfallStepFrames ≈ 5.6 in-game minutes), so F1 (0.1 minutes) is
            //   precision we do not have.
            b.Line(2, "landfall", snapshot.OverLand
                ? "already over land"
                : (snapshot.LandfallKnown
                    ? "in about " + snapshot.MinutesToLandfall.ToString("F0")
                      + " in-game minutes (sampled every "
                      + TyphoonController.LandfallStepMinutesText() + ")"
                    : "not within the forecast window (it may pass over water only)"));

            b.Line(2, "over land", snapshot.OverLand ? "yes" : "no");

            if (!string.IsNullOrEmpty(snapshot.Refusal))
            {
                b.Line(2, "last refusal", snapshot.Refusal);
            }

            WriteWeatherDriving(b, snapshot);
            WriteLightning(b, snapshot);
            WriteWind(b, snapshot);
            WriteFlood(b, snapshot);
            WriteGusts(b, snapshot);
            WriteCloud(b);
            WriteStormFx(b);
            WriteStormSound(b);
        }

        /// <summary>
        /// **The proof that ④ is holding nothing once the typhoon has gone.**
        ///
        /// This is the means of confirming from the screen that it "really did stop", in
        /// answer to the owner's note "when the typhoon leaves, the storm and the tornado
        /// damage should stop".
        /// **When all is well, all five lines read <c>released</c> / <c>0</c>.**
        /// If even one does not, that line is naming the fault.
        ///
        /// ★ The <c>host thunderstorm</c> line is **not a fault.** We decided not to take
        /// the host <c>ThunderStormAI</c> disaster out of the save (design doc §4.2) — the
        /// only way to do so would be to call <c>DeactivateNow</c> before saving, and that
        /// would mean "saving alone destroys your typhoon".
        /// **Save mid-typhoon and reopen it, and a motionless thunderstorm stays there.**
        /// It is an empty shell driving neither rain, nor wind, nor damage, and vanilla
        /// packs it away itself once <c>m_activeDuration</c> runs out.
        /// Without this line, the next person to see it reads it as ④ forgetting to clean
        /// up.
        /// </summary>
        private static void WriteShutdown(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            b.Line(2, "weather override", snapshot.WeatherDriving
                ? "STILL APPLIED WITH NO TYPHOON (m_targetRain / m_targetCloud were not "
                  + "released; this is a bug)"
                : "released");

            b.Line(2, "wind damage", snapshot.WindLastCollapsed == 0
                ? "stopped (0 collapsed in the last sweep)"
                : "STILL COLLAPSING BUILDINGS WITH NO TYPHOON ("
                  + snapshot.WindLastCollapsed + " in the last sweep; this is a bug)");

            b.Line(2, "tornado-strength damage", snapshot.GustActive == 0
                    && snapshot.GustLastCollapsed == 0
                ? "stopped (0 patches, 0 collapsed)"
                : "STILL RUNNING WITH NO TYPHOON (" + snapshot.GustActive + " patches, "
                  + snapshot.GustLastCollapsed + " collapsed; this is a bug)");

            b.Line(2, "river flooding", snapshot.FloodTouched == 0
                ? "restored (0 water sources held)"
                : "STILL HOLDING " + snapshot.FloodTouched + " WATER SOURCE(S) WITH NO "
                  + "TYPHOON (this is a bug; the map would stay flooded)");

            b.Line(2, "host thunderstorm",
                "left in the city on purpose. A typhoon rides on a vanilla ThunderStormAI "
                + "disaster, and Disaster + deliberately does not remove it from saves - "
                + "the only way to do that would be to deactivate it before writing, which "
                + "would mean saving the game destroyed your typhoon. After a mid-storm "
                + "reload you may see a stationary thunderstorm: it drives no rain, no wind "
                + "and no damage, and vanilla ends it when m_activeDuration runs out");
        }

        /// <summary>
        /// The huge rotating cloud (T9). **There is not one vanilla cloud we can reuse**,
        /// so everything that appears here is something ④ built itself (§C-1 / §C-2).
        ///
        /// <c>vanilla sky boost</c> reading <c>not available</c> is **not a fault** —
        /// <c>DayNightDynamicCloudsProperties</c> does not exist under some DLC and graphics
        /// settings (§C-2, PARTIAL). ④'s own cloud does not depend on it.
        /// </summary>
        private static void WriteCloud(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonCloudEnabled.value)
            {
                b.Line(2, "cloud", "off (setting)");
                return;
            }

            b.Line(2, "cloud", CloudStateText());

            // ★★ **Always name which route is drawing it.** The main route is cloud puffs
            //    borrowed from vanilla's particle effects, and the mesh is the fallback for
            //    when those could not be got. This is the only way to tell them apart.
            b.Line(3, "cloud puffs", TyphoonCloudFx.EffectDetail);

            // ★ The fallback route's shader. Whether we fell back to Standard or borrowed
            //   one can only be seen here (treated the same way as ③ and ⑤).
            b.Line(3, "fallback mesh material", TyphoonCloud.ShaderDetail);

            if (!ModSettings.TyphoonVanillaCloudBoost.value)
            {
                b.Line(3, "vanilla sky boost", "off (setting)");
                return;
            }

            b.Line(3, "vanilla sky boost", TyphoonCloud.VanillaBoostApplied
                ? "applied"
                : "not available in this environment (this is normal on some DLC/graphics "
                  + "settings; Disaster + draws its own cloud regardless)");
        }

        /// <summary>
        /// The storm presentation (sideways spray and the blow-away).
        /// **None of it touches buildings, roads or trees**, so it is printed separately
        /// from the damage lines.
        /// </summary>
        private static void WriteStormFx(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonStormFx.value)
            {
                b.Line(2, "storm effects", "off (setting)");
                return;
            }

            b.Line(2, "storm effects", SquallStateText());
            b.Line(3, "driving rain", TyphoonSquallFx.Detail);
            b.Line(3, "gusts pushing citizens", TyphoonWind.GalePushes
                   + " push(es) so far (every 64 frames; citizens and vehicles only)");
        }

        /// <summary>
        /// The wind sound. **Always name what we borrowed** — it is the only clue should a
        /// future game update make the sound disappear.
        /// </summary>
        private static void WriteStormSound(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonStormSound.value)
            {
                b.Line(2, "storm sound", "off (setting)");
                return;
            }

            b.Line(2, "storm sound", TyphoonStormAudio.Detail);
            b.Line(3, "volume this frame", TyphoonStormAudio.LastVolume > 0f
                   ? TyphoonStormAudio.LastVolume.ToString("F2")
                     + " (the player's effect volume slider and mute are applied by the game "
                     + "on top of this)"
                   : "silent (no typhoon, or the camera is outside the storm)");
        }

        private static string SquallStateText()
        {
            switch (TyphoonSquallFx.State)
            {
                case TyphoonSquallState.Emitting:
                    return "driving rain (" + TyphoonSquallFx.LastRenderCalls
                           + " RenderEffect/frame, strength "
                           + TyphoonSquallFx.LastStrength.ToString("F2") + ")";

                case TyphoonSquallState.OutsideStorm:
                    return "the camera is outside the storm, so no spray is drawn "
                           + "(this is normal)";

                case TyphoonSquallState.Idle:
                    return "idle (no typhoon)";

                case TyphoonSquallState.NoEffect:
                    return "NOT DRAWN: no vanilla water particle effect could be borrowed. "
                           + "The rain, the wind damage and the vortex are unaffected";

                case TyphoonSquallState.Failed:
                    return "NOT DRAWN: the spray path threw (see output_log.txt)";

                default:
                    return "off";
            }
        }

        private static string CloudStateText()
        {
            switch (TyphoonCloud.State)
            {
                case TyphoonCloudState.Puffs:
                    // ★ **Distinguish by name** between our own white cloud (the default)
                    //   and the borrowed particles (the fallback). Without knowing which is
                    //   drawing, "it still looks like smoke" cannot be narrowed down.
                    if (TyphoonVortexPuffFx.Drawing)
                    {
                        return "own white cloud (" + TyphoonVortexPuffFx.PuffsPlaced
                               + " puffs placed/frame, radius "
                               + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m; "
                               + (CloudParticleAssets.Detail ?? "material not described") + ")";
                    }

                    return "BORROWED vanilla particles (" + TyphoonCloudFx.LastRenderCalls
                           + " RenderEffect/frame, radius "
                           + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m) - the own "
                           + "white cloud could not be built, so this falls back to steam";

                case TyphoonCloudState.Drawing:
                    return "fallback spiral mesh (" + TyphoonCloud.LastDrawCalls
                           + " draw call/frame, radius "
                           + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m)";

                case TyphoonCloudState.ShaderMissing:
                    return "NOT DRAWN: no usable shader resolved. Disaster + refuses to borrow "
                           + "a Cities material - that renders invisible or black in a "
                           + "hand-rolled DrawMesh";

                case TyphoonCloudState.BuildFailed:
                    return "NOT DRAWN: the mesh or material could not be built";

                default:
                    return "idle (no typhoon to draw)";
            }
        }

        /// <summary>
        /// Tornado-grade local damage (the patches). **Not one actual tornado is
        /// created.**
        ///
        /// <c>active 0</c> is not a fault — one patch is born every
        /// <c>GustPatchPlan.SpawnIntervalFrames</c> and dies after
        /// <c>LifetimeFrames</c>, so there are moments when there are none.
        /// **This is the only place "there are none right now" and "the feature is dead"
        /// can be told apart**, so the sweep count and the number alive are always printed,
        /// even when 0 collapsed.
        /// </summary>
        private static void WriteGusts(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (ModSettings.TyphoonGustStrength.value <= 0)
            {
                b.Line(2, "tornado-strength damage", "off (setting)");
                return;
            }

            int gustStrength = ModSettings.TyphoonGustStrength.value;
            if (gustStrength <= 0)
            {
                b.Line(2, "tornado-strength damage", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "tornado-strength damage",
                "pass " + TyphoonGust.Passes
                + " / " + snapshot.GustActive + " patch(es) alive"
                + " / collapsed " + snapshot.GustLastCollapsed
                + " (total " + snapshot.GustTotalCollapsed + ")"
                + " / refused " + snapshot.GustLastRefused
                + " / strength " + gustStrength);

            // ★ This is the only place "there are none right now" and "the feature is dead"
            //   can be told apart.
            b.Line(3, "patches",
                "no tornado disaster and no funnel is created; up to "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.MaxActivePatches
                + " patches of "
                + (int)DisasterPlus.Core.Typhoon.GustPatchPlan.MinRadiusMetres + "-"
                + (int)DisasterPlus.Core.Typhoon.GustPatchPlan.MaxRadiusMetres
                + " m live at once, one born every "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.SpawnIntervalFrames
                + " storm frames and gone after "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.LifetimeFrames
                + ". 0 alive is normal between spawns");

            // Do not let "nothing broke" be mistaken for "it cannot break things" (same as
            // wind damage).
            if (snapshot.GustLastRefused > 0)
            {
                b.Line(3, "refused",
                    snapshot.GustLastRefused
                    + " (shelters / vaults / dams / decoration / tsunami buoys refuse "
                    + "demolish:false; that is the game answering correctly, not a failure)");
            }

            if (TyphoonGust.LastCapped)
            {
                b.Line(3, "capped",
                    "the per-pass building budget ran out; some patches were not rolled "
                    + "this pass");
            }

            // ★★ Even with NDR present, **these patches are unaffected**. The old
            //    accompanying tornado went through DisasterHelpers.DestroyStuff and so was
            //    replaced wholesale by NDR, but the patches call
            //    BuildingAI.CollapseBuilding directly (i.e. the same route as ④'s wind
            //    damage). **Say so** — leave a warning about a retired feature in place and
            //    the next person reads it as "NDR is still eating this".
            if (ModCompat.NdrPresent)
            {
                b.Line(3, "Natural Disasters Renewal",
                    "present, but it does not affect these patches: they call "
                    + "BuildingAI.CollapseBuilding directly and never go through "
                    + "DisasterHelpers, which is the surface NDR replaces. The accompanying "
                    + "vanilla tornadoes that did go through it have been retired");
            }
        }

        /// <summary>
        /// River flooding (T8).
        ///
        /// **Always print the number of <c>natural sources</c>.** It is map-dependent and
        /// unknown (§D-4 / design doc §6), so it is a figure only learned in the game.
        /// **0 is not a fault.**
        /// </summary>
        private static void WriteFlood(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (ModSettings.TyphoonFloodStrength.value <= 0)
            {
                b.Line(2, "river flooding", "off (setting)");
                return;
            }

            int strength = ModSettings.TyphoonFloodStrength.value;
            if (strength <= 0)
            {
                b.Line(2, "river flooding", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "river flooding",
                snapshot.FloodState
                + "  raised " + snapshot.FloodTouched
                + " / peak +" + snapshot.FloodPeakRiseMetres.ToString("F2") + " m"
                + " / strength " + strength);

            // ★ A map-dependent, unknown figure. Always needed in an in-game report.
            b.Line(3, "natural sources", snapshot.FloodNaturalSources
                + (snapshot.FloodNaturalSources == 0
                    ? " (this map has none; no river can rise and NOTHING IS WRONG - the game "
                      + "has no flood disaster of its own and Disaster + only raises water "
                      + "sources the map already has)"
                    : " (TYPE_NATURAL water sources on the whole map)"));

            if (snapshot.FloodState == TyphoonFloodState.Failed)
            {
                b.Line(3, "failure", TyphoonFlood.LastFailure ?? "unknown");
            }
        }

        /// <summary>
        /// Wind damage (T7). **Print it all even when 0 collapsed.** On screen, "switched
        /// off in the settings", "there are no buildings nearby", "the ceiling stopped it
        /// before it reached the outer rim" and "the game refused all of them" all look
        /// identical (nothing falls), so this is the only place they can be told apart.
        /// </summary>
        private static void WriteWind(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (ModSettings.TyphoonWindStrength.value <= 0)
            {
                b.Line(2, "wind damage", "off (setting)");
                return;
            }

            int strength = ModSettings.TyphoonWindStrength.value;
            if (strength <= 0)
            {
                b.Line(2, "wind damage", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "wind damage",
                "pass " + snapshot.WindPasses
                + " / collapsed " + snapshot.WindLastCollapsed
                + " (total " + snapshot.WindTotalCollapsed + ")"
                + " / examined " + snapshot.WindLastScanned
                + " / refused " + snapshot.WindLastRefused
                + " / strength " + strength);

            // ★★ Props (signs and the like) are a separate sweep from buildings. **Give
            //    them their own line** — roll them together and "wind damage is happening"
            //    hides the fact that not one sign has blown away.
            b.Line(3, "props blown away",
                TyphoonPropDamage.ScannedLastTick > 0
                    ? TyphoonPropDamage.TakenLastTick + " this pass (total "
                      + TyphoonPropDamage.TakenTotal + "), "
                      + TyphoonPropDamage.ScannedLastTick + " grid cells swept"
                    : "not sweeping"
                      + (TyphoonPropDamage.LastFailure != null
                         ? " (" + TyphoonPropDamage.LastFailure + ")"
                         : " this frame (the sweep runs every 64 frames)"));

            b.Line(4, "note: props",
                   "signs, bins and parasols go first; anything over "
                   + DisasterPlus.Core.Typhoon.PropGaleModel.SturdySizeMetres.ToString("F0")
                   + " m is never taken. Trees are NOT included - they belong to "
                   + "TreeManager, not PropManager. Released props do not come back");

            // ★★ Which side the dangerous semicircle is on. **The player cannot tell if we
            //    have left and right the wrong way round**, so we name the side and the size
            //    of the uplift here. The numbers are the very constants TrackBias holds, not
            //    a separate copy made here.
            b.Line(3, "dangerous side",
                (ModSettings.TyphoonSouthernHemisphere.value
                    ? "left of the track (southern hemisphere)"
                    : "right of the track (northern hemisphere)")
                + " - radius x" + (1f + TrackBias.MaxRadiusBoost).ToString("F2")
                + ", collapse chance x" + (1f + TrackBias.MaxChanceBoost).ToString("F2")
                + " at its strongest; the other side is unchanged");

            // Do not let "nothing broke" be mistaken for "it cannot break things" (§F-2).
            // ★ Power poles and cable-car pylons do not belong here (whole-project review).
            //   They return false to the dry run and then perform the real collapse, so they
            //   only add to collapsed. This line used to attribute the whole of refused to
            //   disaster response facilities.
            b.Line(3, "refused", snapshot.WindLastRefused == 0
                ? "0"
                : snapshot.WindLastRefused
                  + " (shelters / vaults / dams / decoration / tsunami buoys refuse "
                  + "demolish:false; that is the game answering correctly, not a failure. "
                  + "Power poles and cable-car pylons are NOT counted here - they refuse the "
                  + "dry run and then collapse anyway, so they land in 'collapsed')");

            // The height is a coefficient, not a cut-off (a different decision from ②'s
            // long-period damage).
            b.Line(3, "unknown height", snapshot.WindLastUnknownHeight == 0
                ? "0"
                : snapshot.WindLastUnknownHeight
                  + " (these buildings stayed eligible at the base chance; the height bonus "
                  + "was declined, not guessed)");

            if (snapshot.WindLastCapped)
            {
                // ★ Do not write "it continues from there on the next sweep" (whole-project
                //   review I1). The eye moves 64-1536 m during one sweep (256 frames) and a
                //   grid cell is 64 m, so the centre cell changes almost every time and the
                //   sweep position resets to 0. **The next sweep starts from the eye
                //   again.** Truncation errs on the safe side (not tested = not knocked
                //   down).
                b.Line(3, "capped",
                    "the sweep was truncated this pass, so the outer edge was not rolled. It "
                    + "does NOT resume where it stopped: the eye moves 64-1536 m per pass "
                    + "against a 64 m grid, so the next pass restarts at the eye");
            }
        }

        /// <summary>
        /// The **target** weather values ④ is writing. The <c>weather (measured)</c> above
        /// is <c>m_current*</c> (vanilla's measurements); this is <c>m_target*</c> (this
        /// mod's quantities).
        /// **Do not confuse the two.**
        ///
        /// In an environment with the weather switched off we print a note. **Do not create
        /// a state that silently does nothing.**
        /// </summary>
        private static void WriteWeatherDriving(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.WeatherDriving)
            {
                b.Line(2, "weather driving", "off");
                return;
            }

            b.Line(2, "weather driving",
                "on   target rain=" + snapshot.DrivenRain.ToString("F2")
                + " cloud=" + snapshot.DrivenCloud.ToString("F2")
                + " fog=0.00"
                + " dir=" + snapshot.DrivenDirectionDegrees.ToString("F1") + " deg");

            if (!snapshot.WeatherEnabled)
            {
                b.Line(3, "note",
                    "the player has weather disabled; m_forceWeatherOn=2 is written every "
                    + "tick to keep the storm visible");
            }

            // Rainfall above 0.8 calls up the game's own ambient lightning. **It is a price
            // we chose to pay**, so we do not hide it (TyphoonWeather's class doc, 6.).
            if (snapshot.DrivenRain > 0.8f)
            {
                b.Line(3, "note",
                    "target rain is above 0.8: once m_currentRain passes it the game queues "
                    + "its own lightning. While this typhoon is Active the game reuses this "
                    + "very disaster instead of creating another one (measured); a separate "
                    + "vanilla thunderstorm can only appear before it activates or after it ends");
            }
        }

        /// <summary>
        /// Lightning (T6). **Always print all of it, even when 0 were fired** (so as not to
        /// repeat ③'s failure where "you could not tell from the diagnostics at all whether
        /// fire spread was running"). On screen, "being thrown away against the ceiling",
        /// "yielding everything to the host" and "never scattering any in the first place"
        /// all look identical (little lightning), so this is the only place they can be told
        /// apart.
        /// </summary>
        private static void WriteLightning(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            b.Line(2, "lightning",
                "in flight " + snapshot.LightningInFlight
                + " / total " + snapshot.LightningTotal
                + " / vanilla reserve " + snapshot.LightningVanillaReserve
                + " / cap " + DisasterPlus.Core.Typhoon.LightningBudget.QueueCapacity);

            // ★ Anything other than 0 is a sign of a fault. Hit the ceiling and the host
            //   storm's and other mods' lightning is thrown away just the same (IL facts
            //   document §A-3).
            b.Line(3, "dropped by the game", snapshot.LightningRejected == 0
                ? "0 (the queue cap was never hit)"
                : snapshot.LightningRejected
                  + " — THE 20-STRIKE CAP WAS HIT; the host storm's own strikes are being "
                  + "thrown away too");

            // ★ Name the case "we are yielding everything to the host and ④ has not fired a
            //   single bolt" (whole-project review I4). At intensity 170 and above this
            //   becomes the permanent state: T6's bias towards the eyewall disappears and
            //   only the host's uniform disc is left.
            //   Look at **the host's share**, not at the stock on hand (which can be 0
            //   temporarily).
            if (DisasterPlus.Core.Typhoon.LightningBudget.YieldsCompletely(
                    snapshot.LightningVanillaReserve))
            {
                b.Line(3, "share",
                    "0 — the host storm's reserve alone uses the whole queue at this intensity "
                    + "(>= " + DisasterPlus.Core.Typhoon.LightningBudget.IntensityWithNoShareAtPeak
                    + " at the ramp peak). Disaster + queues nothing, so the eye-wall placement "
                    + "is gone and only the host storm's uniform disc remains. This is the "
                    + "designed yield, not a failure");
            }

            // Whether ambient lightning (rain > 0.8 and an empty queue) is being suppressed.
            b.Line(3, "environmental lightning", snapshot.LightningInFlight > 0
                ? "suppressed (the queue is not empty)"
                : "possible (the queue may be empty this tick; the game reuses this very "
                  + "disaster rather than creating another one)");
        }

        private static float DegreesOf(float radians)
        {
            return radians * 57.29578f;
        }

        private static string ElapsedText(uint elapsed, uint total)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return elapsed + " / " + total + " frames";

            float elapsedHours = elapsed / framesPerMinute / 60f;
            float totalHours = total / framesPerMinute / 60f;
            return elapsed + " / " + total + " frames (= "
                   + elapsedHours.ToString("F2") + " / " + totalHours.ToString("F2")
                   + " in-game hours)";
        }

        /// <summary>
        /// The storm prefab's three measured values, and the two derived from them.
        ///
        /// **When they cannot be read, do not print the two derived lines.** The presence or
        /// absence of the lines is itself evidence that we are "not displaying a guessed
        /// radius or speed" (design doc §6).
        /// </summary>
        private static void WriteStormPrefab(DiagnosticBuilder b, TyphoonPrefabFacts prefab)
        {
            if (!prefab.StormResolved)
            {
                // In an environment without the DLC this is normal. Treated the same way as
                // the impact sentence on the Assumptions side.
                b.Line(1, "prefab (ThunderStormAI)",
                    "NOT RESOLVED (expected when the Natural Disasters DLC is not owned)");
                return;
            }

            b.Line(1, "prefab (ThunderStormAI)", prefab.Usable
                ? "resolved"
                : "resolved, but UNUSABLE (m_radius or m_activeDuration is 0; no typhoon "
                  + "can be started, and the mod will not guess them)");
            b.Line(2, "m_radius", prefab.StormRadius.ToString("F2"));
            b.Line(2, "m_emergingDuration", FramesWithHours(prefab.EmergingDuration));
            b.Line(2, "m_activeDuration", FramesWithHours(prefab.ActiveDuration));

            if (!prefab.Usable) return;

            // Intensity 100 was chosen as the reference because it is the point at which
            // vanilla's disc is exactly m_radius
            // (R = m_radius * (0.25 + i * 0.0075), §A-1 / §A-2).
            b.Line(2, "derived storm radius",
                TyphoonProfile.StormRadiusOf(100, prefab.StormRadius).ToString("F0")
                + " m at intensity 100  [Disaster + model]");

            // ★ The speed is derived by dividing by **④'s lifetime** (the host's duration ×
            //   LifetimeMultiplier). Divide by the prefab's duration and we would be naming
            //   a figure four times faster than reality (when the lifetime was extended on
            //   2026-08-22 we nearly forgot to fix this).
            b.Line(2, "derived travel speed",
                   TravelSpeedText(TyphoonTrack.LifetimeFramesFor(prefab.ActiveDuration)));
            b.Line(2, "lifetime", TyphoonTrack.LifetimeFramesFor(prefab.ActiveDuration)
                   + " frames (host m_activeDuration " + prefab.ActiveDuration + " x "
                   + TyphoonTrack.LifetimeMultiplier
                   + "; the host is kept alive by TyphoonSlot.KeepAlive)");
        }

        /// <summary>
        /// The weather. **The only line in ④ allowed to claim <c>(measured)</c>** (design
        /// doc §7-1). Do not line up zeroes when it could not be read.
        /// </summary>
        private static void WriteWeather(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.WeatherReadable)
            {
                b.Line(1, "weather (measured)",
                    "unread (WeatherManager is not available; the values are NOT 0, they are unknown)");
                return;
            }

            b.Line(1, "weather (measured)",
                "rain=" + snapshot.Rain.ToString("F2")
                + " cloud=" + snapshot.Cloud.ToString("F2")
                + " fog=" + snapshot.Fog.ToString("F2")
                + " windDir=" + snapshot.WindDirectionDegrees.ToString("F1")
                + " enableWeather=" + (snapshot.WeatherEnabled ? "on" : "off"));
        }

        /// <summary>
        /// The travel speed. If <c>TyphoonTrack.SpeedFor</c> returns 0, **that is the
        /// answer** — do not guess, and write plainly that not one typhoon can be raised
        /// (design doc §6).
        /// </summary>
        private static string TravelSpeedText(uint activeDuration)
        {
            float speed = TyphoonTrack.SpeedFor(activeDuration);
            if (speed <= 0f)
            {
                return "unknown (m_activeDuration is unreadable; no typhoon can be started)";
            }

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return speed.ToString("F3") + " m/frame";

            return speed.ToString("F3") + " m/frame (= "
                   + (speed * framesPerMinute).ToString("F0") + " m per in-game minute)";
        }

        /// <summary>
        /// Print a frame count as "the raw value plus in-game time".
        ///
        /// Always derive the conversion from <see cref="FeatureHost.FramesPerMinute"/>.
        /// We have form for writing a constant inline and being out by a factor of four
        /// (③, mistaking DAYTIME_FRAMES).
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
