using ColossalFramework.Plugins;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Detection of conflicting mods. Evaluated once at startup and cached.
    ///
    /// PluginManager is already populated by the time the main menu comes up, so it is fine to
    /// consult from OnSettingsUI (the same treatment as SteamHelper.IsDLCOwned).
    /// </summary>
    public static class ModCompat
    {
        /// <summary>Natural Disasters Renewal's Workshop ID.</summary>
        private const ulong NdrWorkshopId = 2957578256UL;

        /// <summary>That mod's assembly name. A local or development copy has no Workshop ID, so use both.</summary>
        private const string NdrAssemblyName = "NaturalDisastersRenewal";

        private static bool _evaluated;
        private static bool _ndrPresent;

        private static bool _dlcEvaluated;
        private static bool _naturalDisastersOwned;
        private static bool _naturalDisastersOwnedKnown;

        public static bool NdrPresent
        {
            get
            {
                if (!_evaluated) Evaluate();
                return _ndrPresent;
            }
        }

        /// <summary>
        /// Whether the Natural Disasters DLC is owned.
        ///
        /// SteamHelper.IsDLCOwned works from startup, so it is fine to consult from
        /// OnSettingsUI (which runs once, on the main menu).
        /// Never build the options out of information that is only known after a level load.
        ///
        /// Like NdrPresent, evaluated once and cached. DLC ownership does not change within
        /// the process, and this is called every tick from the top of the sim tick, so without
        /// the cache a Log.Error on failure would fill output_log.txt at tick frequency.
        /// </summary>
        public static bool NaturalDisastersOwned
        {
            get
            {
                if (!_dlcEvaluated) EvaluateDlc();
                return _naturalDisastersOwned;
            }
        }

        /// <summary>
        /// Whether the value above is **an answer we actually measured** (false means the
        /// check failed and it was simply assumed to be "owned").
        ///
        /// ★★ Overall review M11. In an environment where the check fell over with an
        /// exception, <see cref="NaturalDisastersOwned"/> returns true, but that is an
        /// assumption, not a fact. **If the diagnostics called that "owned", it would hand a
        /// false lead to someone hunting for why the trees will not burn**
        /// (<c>TreeManager.BurnTree</c> quietly returns false without the DLC). The value
        /// itself stays as assumed — assume false and features ①②③④ vanish wholesale in an
        /// environment where the check merely failed.
        /// **Branch as before; be honest only about what we claim.**
        /// </summary>
        public static bool NaturalDisastersOwnedKnown
        {
            get
            {
                if (!_dlcEvaluated) EvaluateDlc();
                return _naturalDisastersOwnedKnown;
            }
        }

        private static void EvaluateDlc()
        {
            _dlcEvaluated = true;

            try
            {
                _naturalDisastersOwned = SteamHelper.IsDLCOwned(SteamHelper.DLC.NaturalDisastersDLC);
                _naturalDisastersOwnedKnown = true;
            }
            catch (System.Exception e)
            {
                // When it cannot be determined, assume "owned".
                // A false positive that gives up at runtime does less harm than a false
                // negative that hides the feature forever (if no DisasterInfo is found,
                // Task 10 warns and stops quietly).
                // ★ But **remember that it was assumed** (NaturalDisastersOwnedKnown).
                Log.Error("DLC check failed; assuming owned", e);
                _naturalDisastersOwned = true;
                _naturalDisastersOwnedKnown = false;
            }

            Log.Info("Natural Disasters DLC owned: " + _naturalDisastersOwned
                     + (_naturalDisastersOwnedKnown ? "" : " (ASSUMED; the check failed)"));
        }

        private static void Evaluate()
        {
            _evaluated = true;
            _ndrPresent = false;

            try
            {
                foreach (var p in PluginManager.instance.GetPluginsInfo())
                {
                    if (p == null || !p.isEnabled) continue;

                    if (p.publishedFileID.AsUInt64 == NdrWorkshopId) { _ndrPresent = true; break; }

                    bool matched = false;
                    try
                    {
                        foreach (var asm in p.GetAssemblies())
                        {
                            if (asm != null && asm.GetName().Name == NdrAssemblyName) { matched = true; break; }
                        }
                    }
                    catch { /* do not fall over enumerating a broken mod's assemblies */ }

                    if (matched) { _ndrPresent = true; break; }
                }
            }
            catch (System.Exception e)
            {
                // If the check fails, assume "not present".
                // A false negative that shows a redundant setting does less harm than a false
                // positive that hides a feature.
                Log.Error("plugin scan failed; assuming NDR absent", e);
                _ndrPresent = false;
            }

            Log.Info("Natural Disasters Renewal detected: " + _ndrPresent);
        }
    }
}
