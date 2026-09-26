using CitiesHarmony.API;
using HarmonyLib;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The container for the Harmony patches. **Every other feature gets by with reads
    /// and its own AI; the patch surface is deliberately kept to a minimum.**
    ///
    /// There are only three at the moment:
    ///
    /// <list type="number">
    /// <item>③ a Postfix on <c>VortexAI.SimulationStep</c> (holds the vortex in place)</item>
    /// <item>② a Prefix/Postfix on <c>EarthquakeAI.SimulationStep</c>
    ///   (raises a flag for the duration of a trench quake only)</item>
    /// <item>② a Prefix on <c>DisasterHelpers.MakeCrack</c>
    ///   (a trench quake does not split the ground; see the class doc on
    ///   <c>TrenchQuakeStepPatch</c>)</item>
    /// </list>
    ///
    /// ★★ <b>This type does not belong to ③.</b> The file lives in <c>FireWhirl/</c>
    ///   because the first patch was ③'s, but **② depends on it too** —
    ///   <see cref="Install"/> is called from the <c>OnLevelLoaded</c> of both ③ and ②
    ///   (it is idempotent). Call it from only one and, on the day that feature is
    ///   removed, <b>the other one's patch quietly stops working</b>.
    /// </summary>
    public static class HarmonyBootstrap
    {
        /// <summary>internal: Assumptions uses it to cross-check the patch owner.</summary>
        internal const string HarmonyId = "jp.disasterplus.mod";

        private static Harmony _harmony;

        /// <summary>Guards against registering the callback with DoOnHarmonyReady
        /// twice.</summary>
        private static bool _requested;

        public static bool Installed { get { return _harmony != null; } }

        /// <summary>
        /// Requests that the patches be applied.
        ///
        /// Rather than calling PatchAll on the spot, we hand it to
        /// HarmonyHelper.DoOnHarmonyReady. Even when IsHarmonyInstalled is true the
        /// HarmonyLib assembly may not be loaded yet, and calling directly then dies in
        /// type initialisation. Other mods already on this machine use the same form.
        /// </summary>
        public static void Install()
        {
            if (_harmony != null || _requested) return;
            _requested = true;

            if (!HarmonyHelper.IsHarmonyInstalled)
            {
                Log.Warn("CitiesHarmony not installed; fire whirls will drift instead of staying put");
                return;
            }

            HarmonyHelper.DoOnHarmonyReady(PatchAll);
        }

        private static void PatchAll()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(HarmonyBootstrap).Assembly);
                Log.Info("Harmony patches installed");
            }
            catch (System.Exception e)
            {
                _harmony = null;
                Log.Error("Harmony patch failed", e);
            }
        }

        public static void Uninstall()
        {
            _requested = false;
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(HarmonyId); }
            catch (System.Exception e) { Log.Error("Harmony unpatch failed", e); }
            _harmony = null;
        }
    }
}
