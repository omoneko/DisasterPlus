using ColossalFramework.UI;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Unlocks the ceiling on the intensity slider in vanilla's disaster panel.
    ///
    /// Measured from the IL (design doc appendix A-2):
    ///   DisastersOptionPanel.OnSliderValueChanged(c, value):
    ///       m_label.text = (value / 10).ToString("F1")
    ///       m_disasterTool.m_intensity = (int)value
    /// So the slider's raw value is the byte intensity as-is, and only the display is /10.
    /// There is no call to set_maxValue anywhere in the assembly; the ceiling lives on the
    /// UI prefab side. So there is nothing to patch — just write maxValue at runtime.
    /// </summary>
    public static class IntensityUnlock
    {
        /// <summary>DisasterData.m_intensity is a Byte. 255 is the true ceiling, displayed as 25.5.</summary>
        public const float MaxIntensityByte = 255f;

        /// <summary>Retry interval (in main-thread updates). FindObjectOfType is too heavy to run every frame.</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>Retry limit. Do not search forever in an environment where the panel never appears.</summary>
        private const int MaxAttempts = 100;

        private static bool _applied;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        public static void Reset()
        {
            _applied = false;
            _gaveUp = false;
            _framesSinceTry = 0;
            _attempts = 0;
        }

        /// <summary>
        /// Called every frame from the main thread. The actual attempt happens every
        /// RetryIntervalFrames.
        ///
        /// The disaster panel may not have been built yet at the time the level loads.
        /// Make it a single attempt at load time and the ceiling would never go up again in
        /// that city.
        /// </summary>
        public static void Tick()
        {
            if (_applied || _gaveUp) return;
            if (_framesSinceTry++ < RetryIntervalFrames) return;
            _framesSinceTry = 0;
            Apply();
        }

        /// <summary>
        /// Only reports whether the slider can be reached (does not change the value).
        /// No side effects.
        ///
        /// Assumptions.Run() does not call this directly. The panel may not have been built
        /// yet right after the load (the very reason this class retries Apply() up to 100
        /// times), and a single call right after the load could report "it is just not there
        /// yet" as "an assumption is broken". The settled result is reported through
        /// Assumptions.ReportSliderOutcome() once Apply() reaches _applied / _gaveUp.
        /// This function itself is kept for a one-shot liveness check from, say, a future
        /// overlay.
        /// </summary>
        public static bool SliderReachable()
        {
            var panel = SceneObjects.FindInScene<DisastersOptionPanel>();
            if (panel == null) return false;
            return panel.Find<ColossalFramework.UI.UISlider>("Slider") != null;
        }

        /// <summary>Call from the main thread.</summary>
        public static void Apply()
        {
            if (_applied || _gaveUp) return;

            ModSettings.Ensure();
            if (!ModSettings.IntensityUnlock.value)
            {
                // The feature is switched off in the settings = no assumption has been broken.
                // Even so, do not give up in silence. In an environment where NDR was
                // detected this is the default (in ModSettings, IntensityUnlock defaults to
                // !NdrPresent), so reporting nothing would leave "the slider check is
                // pending" there forever and skew the total number of checks as well.
                // Settle it as "not applicable".
                _gaveUp = true;
                Assumptions.ReportSliderNotApplicable();
                return;
            }

            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                Log.Warn("gave up looking for the disaster intensity slider after "
                         + MaxAttempts + " attempts; cap not raised");
                // This is the case where an assumption really is broken (unlike the return
                // just above, which is only the feature being switched off in the settings).
                // Leave it in Assumptions as a settled result too.
                Assumptions.ReportSliderOutcome(false);
                return;
            }

            try
            {
                // FindObjectOfType does not return inactive GameObjects (see SceneObjects).
                var panel = SceneObjects.FindInScene<DisastersOptionPanel>();
                if (panel == null)
                {
                    // Not built yet. Tick will retry later.
                    Log.Diag("intensityUnlock", "DisastersOptionPanel not found yet; will retry");
                    return;
                }

                var slider = panel.Find<UISlider>("Slider");
                if (slider == null)
                {
                    Log.Warn("intensity slider not found; cap not raised");
                    return;
                }

                if (slider.maxValue >= MaxIntensityByte)
                {
                    _applied = true;
                    Assumptions.ReportSliderOutcome(true);
                    return;   // another mod has already raised it
                }

                Log.Info("raising intensity slider cap " + slider.maxValue + " -> " + MaxIntensityByte);
                slider.maxValue = MaxIntensityByte;
                _applied = true;
                Assumptions.ReportSliderOutcome(true);
            }
            catch (System.Exception e)
            {
                Log.Error("intensity unlock failed", e);
            }
        }
    }
}
