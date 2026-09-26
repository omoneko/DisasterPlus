using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Switching to vanilla's disaster hazard info view.
    ///
    /// We do not draw the heatmap ourselves. Reading the IL showed vanilla already has a
    /// complete implementation (ThunderStormAI.UpdateHazardMap is 237 instructions,
    /// TornadoAI's is 335; InfoMode.DisasterHazard, SubInfoMode.LightningHazard /
    /// TornadoHazard and so on). SetCurrentMode is public, so a thin wrapper is all that
    /// is needed here.
    ///
    /// From the IL (Task 4 Step 1):
    ///   Void InfoManager.SetCurrentMode(InfoMode mode, SubInfoMode subMode) -- public, non-static.
    ///   InfoManager.InfoMode.None does exist (the first element of the enum, value 0).
    ///   InfoManager.SubInfoMode.Default exists too.
    /// Both matched the sketch in the brief; there were no signature discrepancies.
    ///
    /// **Call this from the main thread** (InfoManager is UI/rendering state, and like the
    /// rest of CS1's InfoMode-switching code we treat it as main thread only).
    ///
    /// ── The one exception: reading <see cref="IsShowingHazard"/> from diagnostics ──
    ///
    /// <c>IDisasterFeature.WriteDiagnostics</c> is a sim-thread-only contract (see the
    /// class doc on <c>DiagnosticDump</c>), and there are two places that read
    /// <see cref="IsShowingHazard"/> from inside it (<c>ForecastFeature.WriteUiState</c>
    /// and <c>EarthquakeFeature.WriteUiState</c>). **This is deliberately allowed.** The
    /// grounds are the IL itself (re-confirmed during this review):
    ///
    /// <code>
    /// InfoManager::get_CurrentMode     IL_0000 ldarg.0 ; ldfld m_actualMode    ; ret
    /// InfoManager::get_CurrentSubMode  IL_0000 ldarg.0 ; ldfld m_actualSubMode ; ret
    /// </code>
    ///
    /// **Both are a single field read, with no array and no lazy initialisation.**
    /// What this mod fears at a thread boundary is an
    /// <c>IndexOutOfRangeException</c> (an indexed access into a buffer), and reading one
    /// enum unsynchronised can at worst give you a value that is one tick old. Its only
    /// use is a single line in the diagnostic dump (<c>showing hazard view: yes/no</c>),
    /// where a stale value does not break the meaning (handled the same way as
    /// <c>CameraShakeBooster.LastAdded</c> and <c>WaveformView.State</c>).
    ///
    /// **<see cref="ShowHazard"/> and <see cref="Clear"/> are not exceptions.** They call
    /// <c>SetCurrentMode</c> and so write UI state; they stay main thread only.
    /// </summary>
    public static class InfoModeSwitch
    {
        /// <summary>
        /// Whether the disaster hazard view is on display. **From diagnostics this may be
        /// read on the sim thread too** (the "one exception" in the class doc; reading the
        /// IL established it is a single field read).
        /// </summary>
        public static bool IsShowingHazard
        {
            get
            {
                try
                {
                    if (!Singleton<InfoManager>.exists) return false;
                    return Singleton<InfoManager>.instance.CurrentMode
                           == InfoManager.InfoMode.DisasterHazard;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Whether the DisasterHazard view is on display right now <i>and</i> the submode
        /// on display matches <paramref name="subMode"/>.
        ///
        /// Why this is needed (established by reading the IL, Task 4 follow-up):
        /// DisasterManager.UpdateTexture consults each disaster AI's GetHazardSubMode and
        /// writes into the single m_hazardAmount array only "the hazard for the submode
        /// currently on display". So the grid only ever holds one submode's worth of
        /// values. Read the grid while it does not match the submode on display and you
        /// return a value for an unrelated disaster type as though it were the value for
        /// the submode that was asked for — a confidently wrong number.
        /// HazardMapReader.SampleAt always goes through this check, so there is no room
        /// for a caller to forget it.
        ///
        /// From the IL: InfoManager.CurrentSubMode really does exist as a public
        /// read-only property (of type InfoManager.SubInfoMode).
        /// </summary>
        public static bool IsShowingHazardFor(InfoManager.SubInfoMode subMode)
        {
            try
            {
                if (!Singleton<InfoManager>.exists) return false;
                var im = Singleton<InfoManager>.instance;
                return im.CurrentMode == InfoManager.InfoMode.DisasterHazard
                       && im.CurrentSubMode == subMode;
            }
            catch { return false; }
        }

        public static void ShowHazard(InfoManager.SubInfoMode subMode)
        {
            try
            {
                if (!Singleton<InfoManager>.exists) { Log.Warn("InfoManager not ready"); return; }
                Singleton<InfoManager>.instance.SetCurrentMode(
                    InfoManager.InfoMode.DisasterHazard, subMode);
            }
            catch (System.Exception e)
            {
                Log.Error("failed to switch to the disaster hazard info view", e);
            }
        }

        /// <summary>Returns to the normal view.</summary>
        public static void Clear()
        {
            try
            {
                if (!Singleton<InfoManager>.exists) return;
                Singleton<InfoManager>.instance.SetCurrentMode(
                    InfoManager.InfoMode.None, InfoManager.SubInfoMode.Default);
            }
            catch (System.Exception e)
            {
                Log.Error("failed to clear the info view", e);
            }
        }
    }
}
