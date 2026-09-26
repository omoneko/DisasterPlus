using ICities;
using DisasterPlus.Core.FireWhirl;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description { get { return Strings.ModDescription; } }

        /// <summary>
        /// Called exactly once while the main menu is up (and re-run on a language change).
        /// Never build the options out of information that is only known after a level load.
        ///
        /// ── ★★ Read this before adding prose to this screen ────────────────────
        ///
        /// The owner's instruction is "the Options screen has far too much explanation. Make
        /// it simpler." The line was drawn like this:
        ///
        /// | goes here | example |
        /// |---|---|
        /// | **what you need in order to choose** | a knob's label, its range, what the default means |
        /// | **why something is unavailable** | "the Natural Disasters DLC is required" |
        /// | **what cannot be undone** | the volcano's terrain change is irreversible |
        /// | **notice that a setting has gone** | retired entries (the .cgs is a public contract) |
        ///
        /// | does not go here | where it goes instead |
        /// |---|---|
        /// | an explanation of "this is what vanilla does" | the diagnostic dump (<c>WriteNotes</c>) |
        /// | an explanation of how a feature works | that feature's panel (the top-left shortcut) |
        /// | facts a tester uses to narrow things down | the diagnostic dump |
        ///
        /// **What was dropped is explanation, not information.** When deleting a line of
        /// explanation, first confirm whether its content lives in the dump or in a panel.
        ///
        /// ★ Fold into the label whatever can be folded into the label. Writing it in the name
        ///   of the thing being chosen is shorter and surer than a line of annotation
        ///   (for example, which side the dangerous semicircle is on).
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            // This method is re-run on a language change too. Re-reading here keeps the
            // options screen in step. Note that the tooltips on the in-game buttons are set
            // once at level load, so they stay in the previous language until the next load.
            LocaleLoader.Apply();
            ModSettings.Ensure();

            // Assumptions runs after a level load, so it is still empty on first startup.
            // That is, the warnings appear "once a city has been loaded, the next time the
            // options are opened". OnSettingsUI only runs once, when the main menu comes up,
            // so this is unavoidable.
            //
            // This works because Assumptions.Reset() (on level unload) does not clear the
            // results. This always runs after the unload, so clearing in Reset() would make
            // LastResults always empty and make these warnings impossible in principle.
            // ★ **Do not show the five that FAIL normally in an environment without the DLC**
            //   (overall review). Show them and a plain vanilla environment displays that
            //   block forever, so when a real broken assumption turns up, that one entry is
            //   lost in a block people have long since stopped reading.
            //   What is excluded is described in the Assumptions.UnexpectedFailures doc.
            var failures = Assumptions.UnexpectedFailures();

            if (failures.Count > 0)
            {
                var warn = helper.AddGroup(Strings.AssumptionsFailedTitle);
                for (int i = 0; i < failures.Count; i++)
                {
                    warn.AddGroup("- " + failures[i].Impact);
                }
                warn.AddGroup(Strings.AssumptionsFailedHint);
            }

            if (ModCompat.NaturalDisastersOwned)
            {
                var fw = helper.AddGroup(Strings.GroupFireWhirl);
                fw.AddCheckbox(Strings.FireWhirlEnabled, ModSettings.FireWhirlEnabled.value,
                    v => ModSettings.FireWhirlEnabled.value = v);

                // ★★ **The ranges were widened (2026-08-22).** With the defaults raised to
                //    450 m / 108 buildings but the sliders still stopping at 400 / 40, the
                //    value **drops below the default the moment you touch it** (and nothing
                //    on screen says it went down).
                //    Whenever a setting's default changes, make sure the range contains it.
                OptionsSlider.Add(fw, Strings.DetectRadius, null, 50f, 900f, 25f, ModSettings.DetectRadius.value,
                    v => ModSettings.DetectRadius.value = (int)v);

                OptionsSlider.Add(fw, Strings.DetectCount, null, 4f, 200f, 4f, ModSettings.DetectCount.value,
                    v => ModSettings.DetectCount.value = (int)v);

                // ★ Stops vanilla's (the DLC's) tornado. Has no effect on the fire whirl's
                //   vortex (see the VanillaTornadoSuppressor class doc).
                fw.AddCheckbox(Strings.NoVanillaTornado, ModSettings.NoVanillaTornado.value,
                    v => ModSettings.NoVanillaTornado.value = v);

                OptionsSlider.Add(fw, Strings.MaxLifetime, Strings.MaxLifetimeTip, 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
                    v => ModSettings.MaxLifetimeMinutes.value = (int)v);

                OptionsSlider.Add(fw, Strings.SpreadStrength, null, 0f, IgnitionSpread.MaxStrength, 1f,
                    ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);

                OptionsSlider.Add(fw, Strings.MinSeparation, null, 100f, 800f, 25f, ModSettings.MinSeparation.value,
                    v => ModSettings.MinSeparation.value = (int)v);
            }
            else
            {
                helper.AddGroup(Strings.GroupFireWhirl).AddSpace(4);
                // Put the reason under the group name, so the settings do not look as though
                // they have "gone".
                helper.AddGroup(Strings.FireWhirlNeedsDlc);
            }

            var forecast = helper.AddGroup(Strings.GroupForecast);
            forecast.AddCheckbox(Strings.ForecastEnabled, ModSettings.ForecastEnabled.value,
                v => ModSettings.ForecastEnabled.value = v);
            // ★ "Reset button position" was removed for all four of ①②④⑤. The buttons are
            //    now placed inside vanilla's disaster panel, and the panel's autolayout
            //    decides their positions (DisasterPanelBar), so it would be a dead button
            //    that does nothing when pressed. The saved keys (forecastButtonX/Y and so on)
            //    are part of the .cgs public contract and remain on the ModSettings side, but
            //    **they must not be reused for a different meaning** (see the relevant
            //    comment in ModSettings).

            // The weather and trend rows of the forecast panel work correctly without the DLC,
            // so the feature itself is not hidden. The two hazard rows (the "show on map" for
            // lightning/tornado and the value at the cursor) are different: without the DLC
            // neither the prefab nor the weather radar exists, so they are an empty view and 0
            // forever. The panel already omits those rows entirely
            // (ForecastPanel._hazardRowsBuilt), but without the reason on the settings screen
            // too it looks as though "part of the feature is quietly missing". The same
            // treatment as FireWhirlNeedsDlc.
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.ForecastHazardNeedsDlc);
            }

            var earthquake = helper.AddGroup(Strings.GroupEarthquake);
            earthquake.AddCheckbox(Strings.EarthquakeEnabled, ModSettings.EarthquakeEnabled.value,
                v => ModSettings.EarthquakeEnabled.value = v);
            earthquake.AddCheckbox(Strings.EarthquakeShakeBoost, ModSettings.EarthquakeShakeBoost.value,
                v => ModSettings.EarthquakeShakeBoost.value = v);

            // ── The second layer (behaviour Disaster + added. Not in vanilla) ──────────
            //
            // **OFF by default.** Raising a tsunami from an undersea earthquake is not
            // vanilla behaviour, so turning it on by default would leave the player with no
            // way of realising that "a tsunami arrives by itself after an earthquake" comes
            // from a mod (see the ModSettings.EarthquakeTsunamiChain doc).
            //
            // Not shown in an environment without the DLC. The TsunamiAI prefab does not exist
            // (§B-5), so the setting would be a dead checkbox controlling nothing.
            if (ModCompat.NaturalDisastersOwned)
            {
                // ★★ The trench earthquake tile (2026-08-22, the owner's request).
                //    **This is the only earthquake that comes with a tsunami**, so it goes
                //    before the "tsunami" checkbox below — the order is the explanation.
                earthquake.AddCheckbox(Strings.TrenchQuakeEnabled,
                    ModSettings.TrenchQuakeEnabled.value,
                    v => ModSettings.TrenchQuakeEnabled.value = v);

                earthquake.AddCheckbox(Strings.EarthquakeTsunamiChain,
                    ModSettings.EarthquakeTsunamiChain.value,
                    v => ModSettings.EarthquakeTsunamiChain.value = v);
                OptionsSlider.Add(earthquake, Strings.EarthquakeTsunamiDelay, Strings.EarthquakeTsunamiDelayTip, 5f, 120f, 5f,
                    ModSettings.EarthquakeTsunamiDelayMinutes.value,
                    v => ModSettings.EarthquakeTsunamiDelayMinutes.value = (int)v);

                // ★ Long-period ground motion. **OFF by default.** Unlike the tsunami, this
                //    "collapses buildings that vanilla would have left standing", so the fact
                //    is written into the checkbox label itself
                //    (Strings.EarthquakeLongPeriodEnabled).
                OptionsSlider.Add(earthquake, Strings.EarthquakeLongPeriodStrength, Strings.EarthquakeLongPeriodStrengthTip, 0f, 10f, 1f,
                    ModSettings.EarthquakeLongPeriodStrength.value,
                    v => ModSettings.EarthquakeLongPeriodStrength.value = (int)v);

                // ★ The trench quake's distant damage. **There is no checkbox** — the trench
                //   earthquake is itself a feature of this mod, so being able to set its
                //   strength to 0 is enough.
                OptionsSlider.Add(earthquake, Strings.EarthquakeTrenchDamageStrength, Strings.EarthquakeTrenchDamageStrengthTip, 0f, 10f, 1f,
                    ModSettings.EarthquakeTrenchDamageStrength.value,
                    v => ModSettings.EarthquakeTrenchDamageStrength.value = (int)v);

                // ★ The synthetic seismogram (P wave, S wave, coda). **OFF by default.**
                //   This destroys not one building, but it **changes the shape of the camera
                //   shake from vanilla's**, so it too is second layer. EarthquakeShakeBoost
                //   above can be ON by default because at intensity 55 its addition is
                //   exactly 0 (ShakeWaveform.IntensityFactor); this one has no such escape.
                earthquake.AddCheckbox(Strings.EarthquakeSeismogramEnabled,
                    ModSettings.EarthquakeSeismogram.value,
                    v => ModSettings.EarthquakeSeismogram.value = v);
            }

            // ★ ②'s three explanations (shake correction, long-period, synthetic seismogram)
            //   were taken off this screen.
            //   - what each setting does is stated by **the checkbox label** ("adds damage
            //     vanilla does not have", and so on)
            //   - the "this is what vanilla does" explanation is in the diagnostic dump
            //     (EarthquakeFeature.WriteNotes)
            //   - the long-period note appears verbatim in ②'s panel too
            //     (EarthquakeLayer2Rows)
            //   What was deleted is explanation, not information (see the table in this
            //   method's doc).

            // ② depends on the DLC as a feature (the EarthquakeAI prefab does not exist).
            // Give the reason in the same shape as ForecastHazardNeedsDlc / FireWhirlNeedsDlc.
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.EarthquakeNeedsDlc);
            }

            var typhoon = helper.AddGroup(Strings.GroupTyphoon);
            typhoon.AddCheckbox(Strings.TyphoonEnabled, ModSettings.TyphoonEnabled.value,
                v => ModSettings.TyphoonEnabled.value = v);
            OptionsSlider.Add(typhoon, Strings.TyphoonIntensity, Strings.TyphoonIntensityTip, 10f, 255f, 5f,
                ModSettings.TyphoonIntensity.value,
                v => ModSettings.TyphoonIntensity.value = (int)v);
            // ★★ **No checkbox here.** A strength of 0 is "off"
            //    (ModSettings.MigrateEnableFlagsIntoStrength). Flooding, local damage and
            //    long-period are all shaped the same way.
            OptionsSlider.Add(typhoon, Strings.TyphoonWindStrength, Strings.TyphoonWindStrengthTip, 0f, 10f, 1f,
                ModSettings.TyphoonWindStrength.value,
                v => ModSettings.TyphoonWindStrength.value = (int)v);
            // ★ Which side the dangerous semicircle is on. The default is the northern
            //   hemisphere (i.e. stronger to the right of the direction of travel).
            typhoon.AddCheckbox(Strings.TyphoonSouthernHemisphere,
                ModSettings.TyphoonSouthernHemisphere.value,
                v => ModSettings.TyphoonSouthernHemisphere.value = v);
            // ★ River flooding is ON by default too. It is **the only feature that touches
            //    state which gets burnt into the save**, so the water level is put back when
            //    the typhoon ends, when the city is left, and on every save.
            OptionsSlider.Add(typhoon, Strings.TyphoonFloodStrength, Strings.TyphoonFloodStrengthTip, 0f, 10f, 1f,
                ModSettings.TyphoonFloodStrength.value,
                v => ModSettings.TyphoonFloodStrength.value = (int)v);
            // ★ Tornado-strength local damage. **It creates not one actual tornado**
            //    (ON by default). The accompanying tornado (the feature that borrowed
            //    vanilla's tornado disaster) was removed — this is what replaced it.
            //    A strength of 0 disables it completely.
            OptionsSlider.Add(typhoon, Strings.TyphoonGustStrength, Strings.TyphoonGustStrengthTip, 0f, 10f, 1f,
                ModSettings.TyphoonGustStrength.value,
                v => ModSettings.TyphoonGustStrength.value = (int)v);
            // ★ The cloud is ON by default. It is **a purely visual feature**, and switching
            //    it off leaves the other five elements running unchanged (see the
            //    independence described in the TyphoonCloud class doc).
            typhoon.AddCheckbox(Strings.TyphoonCloudEnabled,
                ModSettings.TyphoonCloudEnabled.value,
                v => ModSettings.TyphoonCloudEnabled.value = v);
            typhoon.AddCheckbox(Strings.TyphoonVanillaCloudBoost,
                ModSettings.TyphoonVanillaCloudBoost.value,
                v => ModSettings.TyphoonVanillaCloudBoost.value = v);
            // ★ The storm visuals. A separate knob from wind damage (see the
            //   ModSettings.TyphoonStormFx doc).
            typhoon.AddCheckbox(Strings.TyphoonStormFx,
                ModSettings.TyphoonStormFx.value,
                v => ModSettings.TyphoonStormFx.value = v);
            // ★ The sound of the wind. It goes through EffectGroup, so the effects volume
            //   slider and mute both apply.
            typhoon.AddCheckbox(Strings.TyphoonStormSound,
                ModSettings.TyphoonStormSound.value,
                v => ModSettings.TyphoonStormSound.value = v);
            // ★ ④'s five explanations came off this screen too.
            //   - the guide to intensity (a vanilla storm is 55) was folded into **the
            //     slider's label**
            //   - which side the dangerous semicircle is on is stated by **the checkbox
            //     label**
            //   - the explanations of wind damage, local damage and flooding appear verbatim
            //     in ④'s panel (TyphoonEffectRows. One click from the top-left shortcut)
            //   - the reasoning behind the dangerous semicircle is in the diagnostic dump
            //     (TyphoonFeatureDiagnostics.WriteNotes)
            //
            // ④ depends on the DLC as a feature (the ThunderStormAI prefab does not exist).
            // Give the reason in the same shape as FireWhirlNeedsDlc / EarthquakeNeedsDlc.
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.TyphoonNeedsDlc);
            }

            // ★★ **Do not put** a "Natural Disasters is required" block on the ⑤ volcano.
            //    ⑤ does not need the DLC (design doc §1.4) — it does not occupy a disaster
            //    slot, it writes RawHeights itself, and neither MakeCrater nor BurnGround has
            //    a DLC gate (IL findings document §C-8 / §B-7b). The only thing that branches
            //    is igniting trees (TreeManager.BurnTree, §B-7c), and T8 states that through
            //    FeatureHost.NoteDegraded. A DLC note here would be a lie.
            var volcano = helper.AddGroup(Strings.GroupVolcano);
            volcano.AddCheckbox(Strings.VolcanoEnabled, ModSettings.VolcanoEnabled.value,
                v => ModSettings.VolcanoEnabled.value = v);

            // The label array must not be static readonly: it would freeze in the language as
            // of type initialisation. Rebuilding it every time keeps it in step with a
            // language change (the same as the other two places in this file).
            string[] volcanoShapes =
            {
                Strings.VolcanoFormShield, Strings.VolcanoFormStrato, Strings.VolcanoFormDome
            };
            int currentShape = ModSettings.VolcanoShapeSetting.value;
            if (currentShape < 0 || currentShape >= volcanoShapes.Length)
            {
                currentShape = ModSettings.VolcanoShapeStrato;
            }
            volcano.AddDropdown(Strings.VolcanoShapeSetting, volcanoShapes, currentShape,
                v => ModSettings.VolcanoShapeSetting.value = v);

            // ★★ **The radius and final-height sliders were removed** (2026-08-22).
            //    The only knob that decides the size is **the disaster panel's intensity
            //    slider**. The baseline is the recommended value per form
            //    (<c>VolcanoShape.DefaultRadiusOf</c> / <c>DefaultHeightOf</c>). The history
            //    is in the <c>VolcanoSizeScale</c> class doc.
            //    volcanoRadius / volcanoHeight in the .cgs are **retired** and must not be
            //    recycled for another meaning (see the retirement notes in
            //    <c>ModSettings</c>).

            // ★ How far the clearing runs ahead of the uplift (T5). **The lower bound is
            //    16 m, not 0** — at 0 you cannot escape the loop of "nothing is cleared →
            //    nothing rises → progress does not move" (VolcanoClearing.LeadMetres).
            //    The consuming side clamps to the same lower bound, so editing the .cgs by
            //    hand cannot stall it either.
            OptionsSlider.Add(volcano, Strings.VolcanoClearingLead, Strings.VolcanoClearingLeadTip, 16f, 400f, 16f,
                ModSettings.VolcanoClearingLeadMetres.value,
                v => ModSettings.VolcanoClearingLeadMetres.value = (int)v);

            // ★ The in-game minutes spent on the uplift (T6). Even with an excessive value,
            //    UpliftSchedule.TotalTicksFor trims it at the ceiling of "the summit moves at
            //    least 1/64 m per tick", so **the uplift never stops silently** (trap 2).
            OptionsSlider.Add(volcano, Strings.VolcanoUpliftMinutes, Strings.VolcanoUpliftMinutesTip, 5f, 240f, 5f,
                ModSettings.VolcanoUpliftMinutes.value,
                v => ModSettings.VolcanoUpliftMinutes.value = (int)v);

            // ★ The strength of the relief on the mountainside (%). **At 0 it returns to
            //   today's smooth cone.** The character of each form (the number of gullies,
            //   the wavelength, the roughness) lives in Core/Volcano/VolcanoRelief, and this
            //   is the single overall multiplier on it (do not add more knobs).
            //   The relief never exceeds the radius R or the final height H — it is built
            //   from multiplication alone, and the effective radius only ever moves in the
            //   direction of shrinking (see the VolcanoRelief class doc).
            OptionsSlider.Add(volcano, Strings.VolcanoReliefStrength, Strings.VolcanoReliefStrengthTip,
                0f, VolcanoRelief.MaxStrengthUnit * 100f, 10f,
                ModSettings.VolcanoReliefStrength.value,
                v => ModSettings.VolcanoReliefStrength.value = (int)v);

            // ★ Whether to draw the eruption plume (T7). **Drawing is a main-thread-only
            //    feature**, so switching it off leaves the uplift progressing just the same
            //    (this is also an item on the playtest checklist).
            volcano.AddCheckbox(Strings.VolcanoEruptionFx, ModSettings.VolcanoEruptionFx.value,
                v => ModSettings.VolcanoEruptionFx.value = v);
            // ★ Volcanic lightning. It only flashes inside the plume, so switching the plume
            //   off removes this as well. It is a separate setting so that someone who
            //   dislikes the flashes does not have to switch off the plume with it.
            volcano.AddCheckbox(Strings.VolcanoLightningSetting,
                ModSettings.VolcanoLightningFx.value,
                v => ModSettings.VolcanoLightningFx.value = v);

            // ★ The band of dust running down the slope (the stand-in for a "pyroclastic
            //   flow"). **Switching it off changes neither the eruption nor the lava** —
            //   the band destroys nothing, and the game has no pyroclastic flow effect at all
            //   (Strings.VolcanoPyroclasticNote says as much).
            //   It is a separate knob from the eruption visuals because a single band uses a
            //   lot of particles, and it is the sort of effect some people want to switch off
            //   on its own.
            volcano.AddCheckbox(Strings.VolcanoPyroclasticSetting,
                ModSettings.VolcanoPyroclasticFx.value,
                v => ModSettings.VolcanoPyroclasticFx.value = v);

            // ★ The sound of the eruption. **Switching it off changes neither the uplift, the
            //   lava, nor the plume** (sound is also a main-thread-only feature). No volume
            //   knob goes here — the game's own effects slider and mute apply directly, and a
            //   second one would leave nobody able to tell "which of the two is in effect".
            volcano.AddCheckbox(Strings.VolcanoEruptionSound,
                ModSettings.VolcanoEruptionSound.value,
                v => ModSettings.VolcanoEruptionSound.value = v);

            // ★ The number of lava flows (T8). **0 disables it completely** (no lava and no
            //    ignition). The ceiling is 8, the same as VolcanoLava.MaxFlows — the work per
            //    tick is "flows × 2 steps", so this is the cost ceiling itself.
            OptionsSlider.Add(volcano, Strings.VolcanoLavaFlowsSetting, Strings.VolcanoLavaFlowsSettingTip, 0f, VolcanoLava.MaxFlows, 1f,
                ModSettings.VolcanoLavaFlows.value,
                v => ModSettings.VolcanoLavaFlows.value = (int)v);

            // ★ Switching ignition off still leaves the lava flowing (it becomes purely visual).
            volcano.AddCheckbox(Strings.VolcanoLavaFireSetting, ModSettings.VolcanoLavaFire.value,
                v => ModSettings.VolcanoLavaFire.value = v);

            // ★ Whether to draw the lava surface (T9). **Switch it off and the lava still
            //    flows, still scorches the ground and still sets buildings on fire** — no
            //    other task depends on T9.
            volcano.AddCheckbox(Strings.VolcanoLavaRenderSetting,
                ModSettings.VolcanoLavaRender.value,
                v => ModSettings.VolcanoLavaRender.value = v);

            // ★ Volcanic tremors. They apply independently of ②'s settings (see the
            //   VolcanoTremorShake class doc).
            //   "It only shakes" is folded into the label — writing it in the name of the
            //   thing being chosen is shorter and surer than a line of explanation (the
            //   discipline in this method's doc).
            volcano.AddCheckbox(Strings.VolcanoQuakeSetting,
                ModSettings.VolcanoQuake.value,
                v => ModSettings.VolcanoQuake.value = v);

            // ★★ State the irreversibility on the settings screen too (design doc §7.1).
            //    The warning in the panel is only read by people who open the panel.
            helper.AddGroup(Strings.VolcanoIrreversibleWarning);

            var general = helper.AddGroup(Strings.GroupGeneral);
            general.AddCheckbox(Strings.IntensityUnlock, ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);

            // Shown only when a conflicting mod is present. Without one the setting means
            // nothing, and the value is kept while behaviour returns to the default
            // (Disaster + is in charge).
            if (ModCompat.NdrPresent)
            {
                // State why the intensity unlock is OFF by default (spec 3.3(a)).
                // Silently OFF looks like "the setting is not working".
                helper.AddGroup(Strings.IntensityUnlockHandledByOther);

                var compat = helper.AddGroup(Strings.NdrDetected);

                // The label array must not be static readonly: it would freeze in the language
                // as of type initialisation. Rebuilding it every time keeps it in step with a
                // language change.
                string[] owners = { Strings.EarthquakeOwnerOther, Strings.EarthquakeOwnerSelf };

                int current = ModSettings.EarthquakeDamageOwner.value;
                if (current < 0 || current >= owners.Length) current = ModSettings.EarthquakeOwnerOther;

                compat.AddDropdown(Strings.EarthquakeDamageOwner, owners, current,
                    v => ModSettings.EarthquakeDamageOwner.value = v);
            }

            var dbg = helper.AddGroup(Strings.GroupDebug);
            dbg.AddCheckbox(Strings.OverlayEnabled, ModSettings.OverlayEnabled.value,
                v => ModSettings.OverlayEnabled.value = v);

            // Do not make the label array static: it would freeze in the language as of type
            // initialisation.
            string[] keys = { "F9", "F10", "F11", "F12" };
            int[] codes = { (int)UnityEngine.KeyCode.F9, (int)UnityEngine.KeyCode.F10,
                            (int)UnityEngine.KeyCode.F11, (int)UnityEngine.KeyCode.F12 };
            int current2 = 2;
            for (int i = 0; i < codes.Length; i++)
                if (codes[i] == ModSettings.OverlayHotkey.value) current2 = i;

            dbg.AddDropdown(Strings.OverlayHotkey, keys, current2,
                v => ModSettings.OverlayHotkey.value = codes[v]);
            // ★ How to produce the diagnostic dump is no longer written here. There is
            //   **a button you can press** on the "Diagnostics" tab of the top-left shortcut
            //   (DiagnosticsPanel) — reducing the explanation does not mean hiding the way to
            //   do it as well.

            // Assembly-CSharp also has a LogChannel of the same name (a different thing on the
            // game's side), so adding a using would make resolution clash. Always reference it
            // fully qualified.
            //
            // Only put a channel here when some feature actually emits logs on it.
            // Log.Diag(key, msg) delegates to General, so General is a real switch, but there
            // is not one single call carrying the FireWhirl channel (design doc 5.2 forbids
            // migrating in this phase: migrating would turn them OFF by default and break the
            // procedure in docs/playtest-checklist.md). A checkbox on its own would be a dead
            // setting where "switching it off changes nothing", so it is kept out of the UI
            // until ② to ⑤ emit logs with a channel.
            // The bit and the saved key are a public contract and are not removed
            // (LogChannel.FireWhirl stays as it is).
            var channels = dbg.AddGroup(Strings.LogChannels);
            channels.AddCheckbox(Strings.LogChannelGeneral,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.General, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.General)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.General));

            // Unlike FireWhirl, calls to Log.Diag carrying the Forecast channel really do exist
            // (ForecastFeature.OnSimulationTick). This checkbox is not a dead setting.
            channels.AddCheckbox(Strings.LogChannelForecast,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Forecast, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Forecast)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Forecast));

            // As with Forecast, calls to Log.Diag carrying the Earthquake channel really do
            // exist (EarthquakeFeature.OnSimulationTick). Not a dead setting.
            channels.AddCheckbox(Strings.LogChannelEarthquake,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Earthquake)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Earthquake));

            // As with Forecast / Earthquake, calls to Log.Diag carrying the Typhoon channel
            // really do exist (TyphoonFeature.OnSimulationTick). Not a dead setting.
            channels.AddCheckbox(Strings.LogChannelTyphoon,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Typhoon));

            // The Volcano channel (= 32) was **defined but unused** up to ⑤'s Task 2.
            // VolcanoFeature.OnSimulationTick emits Log.Diag carrying this channel, so as of
            // now it stops being a dead setting.
            channels.AddCheckbox(Strings.LogChannelVolcano,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Volcano, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Volcano)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Volcano));
        }
    }
}
