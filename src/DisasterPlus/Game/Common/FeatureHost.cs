using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>Feature registration, plus dispatch that confines an exception to one feature.</summary>
    public static class FeatureHost
    {
        private static readonly List<IDisasterFeature> _features = new List<IDisasterFeature>();
        private static uint _lastFrame;
        private static bool _hasLastFrame;

        // Per-feature record of exceptions. Reset on level unload.
        // NoteFailure is called from both the main thread (OnMainThreadUpdate and the like)
        // and the sim thread (OnSimulationTick), and BuildReport reads it on the sim thread.
        // These two Dictionaries are guarded by their own gate, separate from DiagnosticsHub's
        // lock (FeatureHost also touches DiagnosticsHub here, so the gates are kept apart to
        // avoid a lock-ordering problem).
        private static readonly object _errorGate = new object();
        private static readonly Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _lastErrors = new Dictionary<string, string>();

        // A record of "misbehaviour" that comes with no exception. Breaking quietly with no
        // exception is the shape of failure that actually happened three times in this
        // project, so features are given an explicit inlet to declare themselves Degraded.
        // Guarded by the same gate as _errorCounts and cleared along with it on level unload.
        //
        // Two levels: feature name → (note key → reason). One feature can be Degraded for
        // several reasons at once (for ③, "the finishing sequence is stuck" and "the spread
        // came up empty"), so a single feature-name key would be overwritten last-wins, and
        // withdrawing one on recovery would take the other declaration down with it.
        private static readonly Dictionary<string, Dictionary<string, string>> _degradeNotes =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// Whether LevelLoaded() has been passed.
        ///
        /// _features is a static that survives across cities, yet the entry to the sim tick
        /// (SimulationManager.SimulationStep) only looks at
        /// LoadingManager.m_simulationDataLoaded, which goes up before the coroutine that
        /// raises OnLevelLoaded.
        /// Without this flag, for the few seconds right after loading a second city the
        /// features keep turning while still holding the previous city's scanner cursors and
        /// prefab caches.
        /// The same path would also turn in the asset / map editor (where
        /// DisasterPlusLoading.OnLevelLoaded returns early), so this closes that off too.
        /// </summary>
        private static bool _levelReady;

        public static IList<IDisasterFeature> Features { get { return _features; } }

        /// <summary>
        /// Sim frames per in-game minute.
        ///
        /// Measured from the IL: SimulationManager.DAYTIME_FRAMES is 65536 (public static
        /// UInt32). A day is 1440 minutes, so one minute = 65536 / 1440 ≈ 45.51 frames.
        /// Dividing from this value each time, rather than hard-coding a constant, means it
        /// will not drift silently if a game update changes it.
        /// </summary>
        public static float FramesPerMinute
        {
            get { return SimulationManager.DAYTIME_FRAMES / 1440f; }
        }

        public static void Register(IDisasterFeature feature)
        {
            if (feature != null && !_features.Contains(feature)) _features.Add(feature);
        }

        public static void LevelLoaded()
        {
            _hasLastFrame = false;
            IntensityUnlock.Apply();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelLoaded(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnLevelLoaded", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            // Only allow ticks once every feature has finished resetting.
            _levelReady = true;
        }

        public static void SimulationTick()
        {
            if (!_levelReady) return;

            uint frame = SimulationManager.instance.m_currentFrameIndex;

            // Work out the in-game time elapsed (minutes). While paused the frame does not
            // advance, so this comes out 0.
            float framesPerMinute = FramesPerMinute;

            float deltaMinutes = 0f;
            if (_hasLastFrame && frame > _lastFrame)
            {
                deltaMinutes = (frame - _lastFrame) / framesPerMinute;
            }
            _lastFrame = frame;
            _hasLastFrame = true;

            // Pick up requests from the main thread (Ctrl+hotkey) before this pause guard.
            // OnAfterSimulationTick goes on being called while paused (what stops is the
            // advance of in-game time; the sim thread itself does not stop). What this guard
            // protects is "do not advance feature state on a 0-minute step", which has nothing
            // to do with thread ownership. A dump modifies no feature's state, so calling
            // BuildReport() before the guard breaks nothing the guard is protecting.
            //
            // ConsumeRequest() is called exactly once here (call it twice in this method and
            // whichever side misses the request goes out of sync). On both the paused and the
            // unpaused path, this single dumpRequested is what is passed to CollectAndPublish.
            bool dumpRequested = DiagnosticDump.ConsumeRequest();

            if (deltaMinutes <= 0f)
            {
                // Keep collecting even while paused. OnSimulationTick is not called for
                // features that advance state (i.e. feature state does not advance), so the
                // pause guard's original purpose is preserved.
                //
                // Collection must not be limited to when a dump is requested. The most natural
                // way to use this is for the player to stop, pause and open the overlay to
                // look inside, and an overlay whose box sits there empty and unrefreshed at
                // that moment is pointless.
                //
                // The same reasoning applies to display-only features (raised in the overall
                // review). The first tick after a load is always 0 minutes, so returning
                // uniformly here means that while you load and stay paused, the forecast panel
                // never receives data once and every row reads "unavailable". Let through only
                // those features that declare IPausedTickFeature (features that guarantee for
                // themselves that they do not advance state).
                TickPausedFeatures(frame);
                CollectAndPublish(dumpRequested);
                return;   // While paused, do not advance feature state. Never go negative either.
            }

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnSimulationTick(frame, deltaMinutes); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnSimulationTick", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            CollectAndPublish(dumpRequested);
        }

        /// <summary>
        /// Turns only those features that take a tick while paused (0 in-game minutes
        /// elapsed). Sim thread only.
        ///
        /// deltaMinutes is passed as 0f. A feature that declares
        /// <see cref="IPausedTickFeature"/> guarantees as part of the contract that it does
        /// not advance state from deltaMinutes (see the doc over there).
        /// Never pass a different value here.
        /// </summary>
        private static void TickPausedFeatures(uint frame)
        {
            for (int i = 0; i < _features.Count; i++)
            {
                if (!(_features[i] is IPausedTickFeature)) continue;

                try { _features[i].OnSimulationTick(frame, 0f); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnSimulationTick (paused)", e);
                    NoteFailure(_features[i].Name, e);
                }
            }
        }

        /// <summary>
        /// Collects the diagnostics once and publishes them. Sim thread only.
        /// BuildReport() only reads feature internal state through WriteDiagnostics and never
        /// writes it, so it is safe to call from the paused path that does not call
        /// OnSimulationTick.
        ///
        /// Does nothing if the overlay is closed and no dump was requested (zero collection
        /// cost).
        /// </summary>
        private static void CollectAndPublish(bool dumpRequested)
        {
            if (!DiagnosticsHub.CollectionEnabled && !dumpRequested) return;

            try
            {
                var report = BuildReport();
                DiagnosticsHub.Publish(report);
                // No file I/O happens here. It only hands the immutable report to the main
                // thread's DiagnosticDump.FlushPendingWrite().
                if (dumpRequested) DiagnosticDump.SubmitReport(report);
            }
            catch (System.Exception e) { Log.Error("diagnostics collection failed", e); }
        }

        public static void MainThreadUpdate()
        {
            if (!_levelReady) return;

            // Creating and destroying the overlay follows "the current setting", not "the
            // setting as of load time". Back when it was looked at once at load time,
            // switching it ON later did nothing — and because writing the dump rode along on
            // the overlay's Update, even Ctrl+hotkey stopped responding.
            SyncOverlay();

            // The dump the sim thread finished assembling is written out here (main thread).
            // Do not make it depend on whether the overlay exists (do that and the accident
            // above comes back).
            DiagnosticDump.FlushPendingWrite();

            // The disaster panel may not be there yet right after a load. Retry at intervals
            // until it is found.
            IntensityUnlock.Tick();

            // The ④⑤ tiles (the side that raises disasters) are owned by this one place. Have
            // each feature call Tick and the number of things deciding positions grows again
            // (see the DisasterPanelBar class doc).
            DisasterPanelBar.Tick();

            // The reading side (①②④⑤ and the diagnostics) opens from a single button in the
            // top-left. **This too is one place.**
            InfoHub.Tick();
            DiagnosticsPanel.Tick();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnMainThreadUpdate(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnMainThreadUpdate", e);
                    NoteFailure(_features[i].Name, e);
                }
            }
        }

        public static void LevelUnloading()
        {
            // Stop ticking before dismantling begins.
            _levelReady = false;

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelUnloading(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnLevelUnloading", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            _hasLastFrame = false;
            IntensityUnlock.Reset();
            // Remove it after the features have been dismantled. Making sure the next city
            // always starts "one of each, no duplicates" is the responsibility of this one
            // place.
            DisasterPanelBar.Remove();
            // ★ Destroy the tile artwork (two Texture2Ds) ourselves too. They are not
            //   Components, so the GameObject does not take them with it — skip this and
            //   128 KB is left behind every time you enter and leave a city.
            DisasterTileIcons.Destroy();
            // ★ Do not carry over the DisasterInfo reference used for the marker either.
            PlacementMarker.Reset();
            InfoHub.Remove();
            DiagnosticsPanel.Destroy();
            Log.Reset();

            lock (_errorGate)
            {
                _errorCounts.Clear();
                _lastErrors.Clear();
                _degradeNotes.Clear();
            }
            DiagnosticsHub.Clear();
            // Do not carry a dump request raised during teardown, or an assembled report, over
            // to the next city.
            DiagnosticDump.Reset();
        }

        /// <summary>Call from an existing catch clause. Does not stop the calls. Called from both the main and sim threads.</summary>
        private static void NoteFailure(string featureName, System.Exception e)
        {
            lock (_errorGate)
            {
                int n;
                _errorCounts.TryGetValue(featureName, out n);
                _errorCounts[featureName] = n + 1;
                _lastErrors[featureName] = e == null ? "unknown" : e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// The inlet by which a feature declares for itself that, while no exception has been
        /// raised, its behaviour is wrong.
        /// Calling it several times for the same (featureName, noteKey) overwrites with the
        /// most recent reason. Different noteKeys are listed alongside each other.
        /// It persists until ClearDegraded or level unload.
        /// Safe to call from either the main or the sim thread.
        /// </summary>
        /// <param name="noteKey">
        /// The declaration's identifier. On recovery, pass the same value to
        /// <see cref="ClearDegraded"/> to withdraw "only what I raised".
        /// </param>
        public static void NoteDegraded(string featureName, string noteKey, string reason)
        {
            if (string.IsNullOrEmpty(featureName) || string.IsNullOrEmpty(noteKey)) return;
            lock (_errorGate)
            {
                Dictionary<string, string> notes;
                if (!_degradeNotes.TryGetValue(featureName, out notes))
                {
                    notes = new Dictionary<string, string>();
                    _degradeNotes[featureName] = notes;
                }
                notes[noteKey] = string.IsNullOrEmpty(reason) ? "degraded" : reason;
            }
        }

        /// <summary>
        /// Withdraws a self-declared Degraded. Does nothing if there was none.
        ///
        /// Without a recovery path, the badge alone stays Degraded after the symptom has gone,
        /// giving a self-contradictory overlay where the body (the symptom line) has
        /// disappeared but the heading is still red. The purpose of this infrastructure is not
        /// to cry wolf, so whoever raises one must also have a path to lower it.
        /// Safe to call from either the main or the sim thread.
        /// </summary>
        public static void ClearDegraded(string featureName, string noteKey)
        {
            if (string.IsNullOrEmpty(featureName) || string.IsNullOrEmpty(noteKey)) return;
            lock (_errorGate)
            {
                Dictionary<string, string> notes;
                if (!_degradeNotes.TryGetValue(featureName, out notes)) return;
                if (!notes.Remove(noteKey)) return;
                if (notes.Count == 0) _degradeNotes.Remove(featureName);
            }
        }

        /// <summary>
        /// Collects the diagnostics of every feature into one immutable report. Call from the
        /// sim thread.
        /// </summary>
        public static DiagnosticReport BuildReport()
        {
            var header = new List<DiagnosticLine>();
            // The version goes first. For infrastructure built on the premise that "the game
            // changed underneath us", a dump that does not say which game build it records is
            // missing its single most important field (design doc 8). The overlay draws the
            // same header, so it appears in both.
            header.Add(new DiagnosticLine(0, "Mod", ModVersion()));
            header.Add(new DiagnosticLine(0, "Game", GameVersion()));
            header.Add(new DiagnosticLine(0, "DLC:ND",
                ModCompat.NaturalDisastersOwned ? "owned" : "MISSING"));
            header.Add(new DiagnosticLine(0, "NDR",
                ModCompat.NdrPresent ? "detected" : "absent"));
            header.Add(new DiagnosticLine(0, "Harmony",
                HarmonyBootstrap.Installed ? "patched" : "NOT PATCHED"));
            header.Add(new DiagnosticLine(0, "Level", _levelReady ? "ready" : "not ready"));

            var sections = new List<DiagnosticSection>();
            var builder = new DiagnosticBuilder();

            // Snapshot _errorCounts / _lastErrors wholesale first, then leave the lock.
            // f.WriteDiagnostics is arbitrary feature code, and the chance of it calling back
            // into FeatureHost is not zero (something via Log.Diag, say). Call it while
            // holding the lock and that code path could deadlock the moment it tries to take
            // the same _errorGate, so it is absolutely avoided here.
            // The names are obtained outside the lock beforehand too. IDisasterFeature.Name is
            // by contract "arbitrary feature code" (② to ⑤ are written with this file as the
            // model), and calling it while holding _errorGate would be the same breach of
            // discipline. So that a throwing property does not bring down the whole
            // diagnostics, each is caught individually here.
            var names = new string[_features.Count];
            for (int i = 0; i < _features.Count; i++)
            {
                try { names[i] = _features[i].Name; }
                catch { names[i] = "feature#" + i; }
                if (string.IsNullOrEmpty(names[i])) names[i] = "feature#" + i;
            }

            var healths = new FeatureHealth[_features.Count];
            var notes = new string[_features.Count];
            lock (_errorGate)
            {
                for (int i = 0; i < _features.Count; i++)
                {
                    healths[i] = FeatureHealth.Healthy;
                    notes[i] = "";

                    int errors;
                    if (_errorCounts.TryGetValue(names[i], out errors) && errors > 0)
                    {
                        healths[i] = FeatureHealth.Degraded;
                        string last;
                        _lastErrors.TryGetValue(names[i], out last);
                        notes[i] = errors + " errors, last: " + last;
                    }

                    // Self-declarations with no exception (NoteDegraded). If there is also an
                    // exception record, both are listed.
                    string degraded = JoinDegradeNotes(names[i]);
                    if (degraded.Length > 0)
                    {
                        healths[i] = FeatureHealth.Degraded;
                        notes[i] = notes[i].Length == 0 ? degraded : notes[i] + "; " + degraded;
                    }
                }
            }

            for (int i = 0; i < _features.Count; i++)
            {
                var health = healths[i];
                var note = notes[i];

                try
                {
                    _features[i].WriteDiagnostics(builder);
                }
                catch (System.Exception e)
                {
                    // Do not let one feature's diagnostics failure take the others' with it.
                    // So the badge does not contradict the body, mark this feature's health
                    // Degraded as well.
                    builder.Line(1, "diagnostics failed", e.GetType().Name);
                    health = FeatureHealth.Degraded;
                }

                sections.Add(new DiagnosticSection(names[i], health, note, builder.Take()));
            }

            return new DiagnosticReport(header, Assumptions.LastResults, sections);
        }

        /// <summary>
        /// Joins one feature's self-declarations into a single sentence. Call while holding
        /// _errorGate.
        ///
        /// They are sorted by note key before being joined. A Dictionary's enumeration order
        /// can change once a withdrawal (ClearDegraded) is involved, so without the sort a
        /// line in the overlay would appear to swap around for no reason.
        /// </summary>
        private static string JoinDegradeNotes(string featureName)
        {
            Dictionary<string, string> notes;
            if (!_degradeNotes.TryGetValue(featureName, out notes) || notes.Count == 0) return "";

            var keys = new List<string>(notes.Keys);
            keys.Sort(System.StringComparer.Ordinal);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(notes[keys[i]]);
            }
            return sb.ToString();
        }

        /// <summary>The mod version. Prints the assembly version as-is (AssemblyInfo.cs is the single source).</summary>
        private static string ModVersion()
        {
            try
            {
                var v = typeof(FeatureHost).Assembly.GetName().Version;
                return v == null ? "unknown" : v.ToString();
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// The game version.
        ///
        /// Measured from the IL: BuildConfig.applicationVersion is a public static String
        /// property whose body just returns BuildConfig.VersionToString(APPLICATION_VERSION,
        /// false) (use applicationVersionFull when the full version is needed). Being static,
        /// it needs neither an instance nor a singleton and can be read at any moment.
        /// </summary>
        private static string GameVersion()
        {
            try { return BuildConfig.applicationVersion; }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Creates or destroys the overlay to match the setting. Main thread only.
        /// Both Create and Destroy return immediately if already in that state, so this is
        /// safe to call every frame.
        /// </summary>
        private static void SyncOverlay()
        {
            try
            {
                ModSettings.Ensure();
                if (ModSettings.OverlayEnabled.value) DiagnosticOverlay.Create();
                else DiagnosticOverlay.Destroy();
            }
            catch (System.Exception e) { Log.Error("overlay sync failed", e); }
        }
    }
}
