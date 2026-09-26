namespace DisasterPlus.Game
{
    /// <summary>
    /// ① The weather forecast tab. We do not draw vanilla's hazard heatmap ourselves but
    /// borrow it (InfoModeSwitch / HazardMapReader); what this feature adds is the time
    /// axis (the trend), turning the hazard values into numbers, and **an explanation of
    /// what the hazard map actually means** (design document 2, plus the correction to our
    /// premise made during the full review — see the class doc on ForecastPanel).
    ///
    /// It implements IPausedTickFeature so that the panel does not fill up with "cannot
    /// read" when you stay paused right after loading. All this feature does is read via
    /// WeatherReader and publish to ForecastHub; it never advances any game state, so it
    /// meets the conditions for claiming that marker (see its doc).
    /// </summary>
    public class ForecastFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Forecast";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            // ★ The map overlays (track, gale area, wind). There is one registration,
            //   shared with ②, and every city load starts with all toggles off.
            ForecastOverlay.EnsureRegistered();
            WeatherRadarWatch.Reset();

            ForecastHub.Clear();
            // Do not attempt to install the button here. UIView may not be ready yet at
            // this point, so — as with ③'s panel button — leave it to the throttled work
            // in OnMainThreadUpdate.
        }

        /// <summary>
        /// Sim thread. Always read WeatherManager / DisasterManager here. Touch them
        /// directly from the main thread and an IndexOutOfRangeException with no stack
        /// trace turns up later (see the class doc on WeatherReader).
        ///
        /// Also called while paused (deltaMinutes == 0), via IPausedTickFeature. We do
        /// not use deltaMinutes, so that changes nothing about the behaviour.
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.ForecastEnabled.value) return;

            // ★ Whether there is a weather radar (the unlock condition for "the weather
            //   to come"). It throttles itself to once per in-game minute, so it is fine
            //   to call every tick.
            WeatherRadarWatch.Poll(deltaMinutes);

            var snapshot = WeatherReader.Read();
            ForecastHub.Publish(snapshot);

            // The Forecast channel is off by default. Without this if, the five ToString
            // calls and the string concatenation below would run every sim tick (around
            // 50 times a second at normal speed) only to be thrown away by Log.Diag — C#
            // evaluates the arguments in full before the call, so the mask check inside
            // Diag comes too late (raised in the full review).
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Forecast)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Forecast, "forecast",
                snapshot.Valid
                    ? "temp=" + snapshot.Temperature.Current.ToString("F1")
                      + " rain=" + snapshot.Rain.Current.ToString("F2")
                      + " cloud=" + snapshot.Cloud.Current.ToString("F2")
                      + " fog=" + snapshot.Fog.Current.ToString("F2")
                      + " trend=" + snapshot.Temperature.Trend
                      + " locatedStorms=" + snapshot.LocatedLightningStorms
                      + "/" + snapshot.LocatedTornadoes
                    : "snapshot invalid");
        }

        /// <summary>Main thread. Installing the panel and button, and updating the contents
        /// only while visible, all happen from here.</summary>
        public void OnMainThreadUpdate()
        {
            // DisasterPanelBar owns all four buttons together (FeatureHost calls it).
            ForecastPanel.Tick();
        }

        public void OnLevelUnloading()
        {
            // ★ The registration cannot be removed, so we guarantee nothing is drawn
            //   through our own state instead.
            ForecastOverlay.Reset();
            WeatherRadarWatch.Reset();

            ForecastHub.Clear();
            ForecastPanel.Destroy();
        }

        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.ForecastEnabled.value ? "yes" : "no");

            var snapshot = ForecastHub.Latest;
            b.Line(1, "snapshot", snapshot == null ? "none yet" : (snapshot.Valid ? "valid" : "INVALID"));

            if (snapshot != null && snapshot.Valid)
            {
                b.Line(2, "temperature", snapshot.Temperature.Current.ToString("F1")
                    + " -> " + snapshot.Temperature.Target.ToString("F1")
                    + "  " + snapshot.Temperature.Trend);
                b.Line(2, "rain", snapshot.Rain.Current.ToString("F2")
                    + " -> " + snapshot.Rain.Target.ToString("F2")
                    + "  " + snapshot.Rain.Trend);
                b.Line(2, "cloud", snapshot.Cloud.Current.ToString("F2")
                    + " -> " + snapshot.Cloud.Target.ToString("F2")
                    + "  " + snapshot.Cloud.Trend);
                b.Line(2, "fog", snapshot.Fog.Current.ToString("F2")
                    + " -> " + snapshot.Fog.Target.ToString("F2")
                    + "  " + snapshot.Fog.Trend);

                // Let a tester tell "the hazard map is empty" apart from "the risk really
                // is low". At 0/0 the grid is 0 wherever you put the cursor, and that is
                // the normal state (see the doc on
                // WeatherSnapshot.LocatedLightningStorms).
                b.Line(2, "located storms (lightning/tornado)", snapshot.DisasterInfoAvailable
                    ? snapshot.LocatedLightningStorms + " / " + snapshot.LocatedTornadoes
                    : "unavailable (DisasterManager not present)");
                // This is "the configured probability" (m_randomDisastersProbability, a
                // fraction in 0.0-1.0; the *100 is justified by the IL showing it is the
                // same conversion vanilla's PopsTelemetryEventFormatting.DisasterProbability
                // performs — see the comments on the ForecastPanel side). It is not the
                // value DisasterManager.SimulationStepImpl actually uses for the per-tick
                // spawn decision (it squares this, corrects for the area, then compares
                // against a random number).
                //
                // When DisasterInfoAvailable is false, the untouched 0f stands for "could
                // not read it", and printing just "0.0%" would be indistinguishable from
                // a genuine reading of zero (review finding). This is developer-facing
                // text, so we write unavailable explicitly.
                b.Line(2, "disaster probability (configured)", snapshot.DisasterInfoAvailable
                    ? (snapshot.DisasterProbability * 100f).ToString("F1") + "%"
                    : "unavailable (DisasterManager not present)");
                b.Line(2, "disaster cooldown", !snapshot.DisasterInfoAvailable
                    ? "unavailable"
                    : (snapshot.DisasterCooldown > 0 ? "active (" + snapshot.DisasterCooldown + ")" : "none"));
            }

            // ★ The tiles for ① and ② were removed from the disaster panel (things you
            //   only read are opened from the shortcut in the top left; see the class doc
            //   on InfoHub). All we report is whether the button is there and where it
            //   is. **This is the sim thread, but all we read is a native-pointer
            //   comparison on a Unity object plus a string; we do not touch the UI**
            //   (handled the same way as DisasterPanelBar.IsInstalled).
            b.Line(1, "info button", (InfoHub.IsInstalled ? "installed" : "not installed")
                + "  (" + InfoHub.Placement + ")");

            // This reads something the main thread owns from the sim thread, but it is the
            // one exception the class doc on InfoModeSwitch explicitly permits, with the
            // IL to back it up (get_CurrentMode is a single field read, and at worst the
            // value is one tick old).
            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");

            // Half of the hazard side depends on the DLC (I2). Without it, neither "show
            // on map" nor the figure under the cursor appears on the panel, so make it
            // visible from the dump whether that is as intended.
            b.Line(1, "hazard rows", ModCompat.NaturalDisastersOwned
                ? "shown"
                : "hidden (Natural Disasters DLC not owned)");
        }
    }
}
