using System;
using System.Collections.Generic;
using System.Reflection;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Checks the assumptions in design doc appendix A against the real game, at runtime.
    ///
    /// Why it is needed: during ③'s implementation, three assumptions read from the IL turned
    /// out to be wrong (the factor-of-4 error in DAYTIME_FRAMES, the meaning of m_targetPos0,
    /// and writing m_fireIntensity directly). What they had in common is that "nothing happens
    /// when the assumption breaks". The game carries on unperturbed and only the behaviour is
    /// quietly different. Naming it in the log here is the only breakwater there is.
    ///
    /// ── How the files are arranged (split in overall review I6) ─────────────────────
    ///
    /// It was once a single file of 1159 lines. It was gathered together so it could be
    /// audited against appendix A, but it grew until **the comment explaining the breakdown
    /// and the thing it describes were 900 lines apart**, so one of the two would go stale.
    /// It is now split per feature with <c>partial</c>:
    ///
    /// <code>
    /// Assumptions.cs             the foundation (this file) — totals, logging,
    ///                            Check/SetResult, the HasField family, the slider check,
    ///                            and 1 assumption that belongs to no feature
    /// Assumptions.FireWhirl.cs   ③ fire whirl   4 checks
    /// Assumptions.Forecast.cs    ① forecast     7 checks
    /// Assumptions.Earthquake.cs  ② earthquake  11 checks
    /// Assumptions.Typhoon.cs     ④ typhoon      9 checks
    /// Assumptions.Volcano.cs     ⑤ volcano      (VolcanoCheckCount over there states the count)
    /// </code>
    ///
    /// **Not a single visibility was changed.** <c>Check</c>, <c>_gate</c> and
    /// <c>_results</c> all stay private, and partial makes them reachable.
    /// <c>DisasterPlus.csproj</c> globs <c>Game\**\*.cs</c>, so the split did not touch the
    /// csproj.
    ///
    /// **Do not add assumptions for ⑤ onwards to this file.** Make a partial for the feature
    /// and declare a <c>…CheckCount</c> inside it, adding to the sum in
    /// <see cref="TotalCheckCount"/>.
    ///
    /// Thread safety: Run() / Reset() / ReportSliderOutcome() are expected to be called only
    /// from the main thread, but LastResults is read every tick from the sim thread through
    /// FeatureHost.BuildReport() once DiagnosticsHub.CollectionEnabled is set (from Task 5
    /// onwards). To avoid a race reading a List while it is being written, every access to
    /// _results is serialised through the single _gate. So as not to create a lock-ordering
    /// relationship with FeatureHost._errorGate or DiagnosticsHub's gate, no code in another
    /// class is ever called while holding this lock.
    /// </summary>
    public static partial class Assumptions
    {
        private const string SliderCheckName = "Disasters panel intensity slider is reachable";
        private const string SliderCheckImpact = "disaster intensity cannot be unlocked to 25.5";

        /// <summary>
        /// The number of checks that will eventually be filled in over one level load. Report()
        /// uses it to say "out of how many" the totals are.
        ///
        /// ★ **The breakdown is no longer written here** (overall review I6). There used to be
        ///   a 34-line breakdown table here, but the checks it described lived up to 900 lines
        ///   away, so adding a check updated only one of the two (a doc saying "31 checks" and
        ///   a constant of 32 did in fact coexist).
        ///   Now **each feature's partial declares its own count** and this merely sums them —
        ///   whoever adds a check only has to look at the constant in the file they added to.
        /// </summary>
        private const int TotalCheckCount = GeneralCheckCount + FireWhirlCheckCount
                                            + ForecastCheckCount + EarthquakeCheckCount
                                            + TyphoonCheckCount + VolcanoCheckCount
                                            + SliderCheckCount;

        /// <summary>The number of checks this file holds (foundation assumptions belonging to no feature).</summary>
        private const int GeneralCheckCount = 1;

        /// <summary>The single slider-reachability check (<see cref="ReportSliderOutcome"/>).</summary>
        private const int SliderCheckCount = 1;

        private static readonly object _gate = new object();
        private static readonly List<AssumptionResult> _results = new List<AssumptionResult>();

        /// <summary>
        /// The names of checks for which FAIL is the normal outcome in an environment without
        /// the Natural Disasters DLC. Registered by
        /// <see cref="Check(string,string,Func{bool},bool)"/>.
        ///
        /// **Do not write the names out a second time as a separate table.** Do that and the
        /// correspondence silently breaks when a check name is corrected, and a normal FAIL
        /// starts coming out as a warning again.
        /// <see cref="Reset"/> does not clear it (it is a fact about the game build, not
        /// per-city state).
        /// </summary>
        private static readonly List<string> _expectedWithoutDlc = new List<string>();

        private static bool _ran;

        /// <summary>
        /// Whether the slider check has settled as "not applicable in this environment".
        ///
        /// In an environment where the intensity unlock is switched off in the settings, there
        /// is no assumption to check in the first place. That is the default in an environment
        /// where NDR was detected, so leaving it "pending" means those users see an unsettled
        /// total forever. The choice here is to take it out of the denominator and honestly
        /// say "4 of 4".
        /// Main thread only, like Run() / Reset() / ReportSlider* (treated the same as _ran).
        /// </summary>
        private static bool _sliderNotApplicable;

        /// <summary>
        /// Whether _results belongs to the previous city. Set by Reset() and dropped by
        /// SetResult when the first result for this city is written (i.e. a deferred clear).
        ///
        /// It cannot be "clear at the top of Run()". ReportSliderOutcome() can run before
        /// Assumptions.Run() along the path FeatureHost.LevelLoaded() →
        /// IntensityUnlock.Apply() (see DisasterPlusLoading's call order), and clearing at the
        /// top of Run() would silently lose that one result.
        /// With "clear on the next write", neither order accumulates, and the previous city's
        /// results still survive on the main menu.
        /// Touch it only inside _gate.
        /// </summary>
        private static bool _stale;

        /// <summary>Always returns a defensive copy, so that a caller holding on to the list is
        /// protected from later changes to _results.</summary>
        public static IList<AssumptionResult> LastResults
        {
            get
            {
                lock (_gate) { return new List<AssumptionResult>(_results); }
            }
        }

        /// <summary>
        /// Call on level unload. It only makes Run() runnable again on the next load; it does
        /// not clear the results.
        ///
        /// _results must not be cleared here. OnSettingsUI (one of the three output
        /// destinations design doc 4.4 requires) runs on the main menu, i.e. always after this
        /// Reset(), so clearing here would make LastResults always empty and make the
        /// assumption warnings on the settings screen impossible in principle (the very
        /// procedure Strings.AssumptionsFailedHint points to would become a procedure that
        /// displays nothing).
        /// The previous city's results are carried through to the main menu. So they do not
        /// accumulate, SetResult drops them wholesale the moment the first result is written
        /// in the next city (see _stale).
        /// </summary>
        public static void Reset()
        {
            _ran = false;
            _sliderNotApplicable = false;
            lock (_gate) { _stale = true; }
        }

        /// <summary>
        /// Call exactly once after the level has finished loading. Not at startup, because it
        /// needs to see whether Harmony was applied and whether the prefabs resolved.
        ///
        /// Only things that can be decided conclusively are looked at here
        /// (<see cref="TotalCheckCount"/> minus <see cref="SliderCheckCount"/> checks).
        /// **Do not write an actual number here** — do that and one of the two goes stale
        /// every time a feature is added (this doc has done exactly that once).
        /// Whether the intensity slider is reachable cannot yet be told apart from "just not
        /// built yet" at this point (so much so that IntensityUnlock itself retries 100 times
        /// at 120-frame intervals), so deciding immediately and emitting a FAIL here would
        /// misreport cases that in fact become reachable perfectly well later.
        /// That one check is filled in separately by ReportSliderOutcome() once IntensityUnlock
        /// has settled.
        /// </summary>
        public static void Run()
        {
            if (_ran) return;
            _ran = true;

            // ★ The checks themselves live in the per-feature partials
            //   (Assumptions.<feature>.cs). This holds only the order and the foundation for
            //   totals and logging. **Do not add a new feature's checks to this file** —
            //   growing to 1159 lines, with the breakdown comment 900 lines from what it
            //   described, is the reason for the split.
            RunGeneral();
            RunFireWhirl();
            RunForecast();
            RunEarthquake();
            RunTyphoon();
            RunVolcano();

            Report();
        }

        /// <summary>The foundation assumptions belonging to no feature (<see cref="GeneralCheckCount"/> of them).</summary>
        private static void RunGeneral()
        {
            Check("SimulationManager.DAYTIME_FRAMES == 65536",
                  "all in-game durations will be wrong",
                  delegate { return SimulationManager.DAYTIME_FRAMES == 65536; });
        }



        private static bool HasUpdateHazardMap(Type aiType)
        {
            return aiType.GetMethod("UpdateHazardMap",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(ushort), typeof(DisasterData).MakeByRefType(), typeof(byte[]) },
                null) != null;
        }

        /// <summary>
        /// The name-only version. **Do not use it for new checks** (overall review).
        ///
        /// A matching name is no guarantee that "a field with the same meaning is still there".
        /// Whether the type changed from <c>UInt16</c> to <c>UInt32</c>, or a <c>float</c>
        /// became a <c>double</c>, this function goes on returning true —
        /// and this mod's reads and writes happen **with the compiled type**, so what really
        /// results is either a type-load exception or silently reading a different value.
        /// Where the type is known, always use <see cref="HasField(Type,string,Type)"/>.
        ///
        /// It is kept solely for cases where the type cannot be named (an internal field of a
        /// <c>FastList&lt;T&gt;</c> and the like, where the match must hold across generic
        /// arguments).
        /// </summary>
        private static bool HasField(Type declaringType, string fieldName)
        {
            return declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
        }

        /// <summary>
        /// The version that checks the type as well. **This is the default.**
        ///
        /// Every expected type was settled in this task by reflecting over the real game
        /// assembly (<c>WeatherManager</c>'s weather values are all <c>Single</c>,
        /// <c>DisasterData.m_intensity</c> is a <c>Byte</c>,
        /// <c>m_activationFrame</c> / <c>m_startFrame</c> are <c>UInt32</c>,
        /// <c>WaterSource.m_type</c> / <c>m_target</c> are <c>UInt16</c>, and
        /// <c>ThunderStormAI</c>'s / <c>EarthquakeAI</c>'s durations are <c>UInt32</c>).
        /// </summary>
        private static bool HasField(Type declaringType, string fieldName, Type fieldType)
        {
            var f = declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null && f.FieldType == fieldType;
        }

        /// <summary>
        /// The static-field version. <see cref="HasField"/> only looks at Instance, so passing
        /// a static readonly such as SimulationManager.DAYTIME_FRAME_TO_HOUR to that one
        /// always comes back false (i.e. a false FAIL).
        /// </summary>
        private static bool HasStaticField(Type declaringType, string fieldName, Type fieldType)
        {
            var f = declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f != null && f.FieldType == fieldType;
        }

        /// <summary>
        /// Call when IntensityUnlock has settled whether the slider is reachable (_applied on
        /// success, or _gaveUp having exhausted MaxAttempts). Not making it a one-shot right
        /// after the load, but leaving a named result and a log line "at the moment it is
        /// known", is the only way to avoid a misreport (a false FAIL) on this check. When the
        /// feature itself was disabled in the settings (_gaveUp but
        /// ModSettings.IntensityUnlock.value == false) no assumption has been broken, so call
        /// <see cref="ReportSliderNotApplicable"/> instead of this.
        /// </summary>
        public static void ReportSliderOutcome(bool reachable)
        {
            var result = new AssumptionResult(
                SliderCheckName, reachable, reachable ? "" : SliderCheckImpact);
            SetResult(result);
            LogResult(result);
        }

        /// <summary>
        /// Settles the slider check as not applicable in this environment, because the
        /// intensity unlock is switched off in the settings.
        ///
        /// Do not publish a PASS (that would claim an assumption held when it was never
        /// checked). Take it out of the denominator instead and say so on the totals line.
        /// </summary>
        public static void ReportSliderNotApplicable()
        {
            _sliderNotApplicable = true;
            Log.Info("  n/a   " + SliderCheckName + " (intensity unlock is off in settings)");
        }

        private static void Check(string name, string impact, Func<bool> predicate)
        {
            Check(name, impact, predicate, false);
        }

        /// <param name="expectedWithoutDlc">
        /// Whether **FAIL is the normal outcome for this check** in an environment without the
        /// Natural Disasters DLC.
        ///
        /// For a check passed true, <see cref="LogResult"/> emits it with <c>Log.Info</c>
        /// rather than <c>Log.Warn</c> in an environment without the DLC, and it drops out of
        /// the warnings on the settings screen (<see cref="UnexpectedFailures"/>).
        /// **The result itself stays FAIL** and appears as FAIL in the diagnostic dump as
        /// before — "unusable because the DLC is absent" is never rephrased as PASS.
        ///
        /// This is needed because in an environment without the DLC five checks FAIL **on
        /// every level load**. Each one produces two lines of Log.Warn, so a normal vanilla
        /// environment has its log filled with ten warning lines every time, and the settings
        /// screen carries a permanent block of "some features are unavailable" that never goes
        /// away. This is the treatment that stops it crying wolf; other FAILs keep standing out
        /// as Warn, as before.
        /// </param>
        private static void Check(string name, string impact, Func<bool> predicate,
                                  bool expectedWithoutDlc)
        {
            if (expectedWithoutDlc)
            {
                lock (_gate)
                {
                    if (!_expectedWithoutDlc.Contains(name)) _expectedWithoutDlc.Add(name);
                }
            }

            bool passed;
            string detail = impact;
            try
            {
                passed = predicate();
            }
            catch (Exception e)
            {
                // Do not break startup even if the check itself falls over. Treat it as FAIL.
                passed = false;
                detail = impact + " (check threw " + e.GetType().Name + ")";
            }
            SetResult(new AssumptionResult(name, passed, passed ? "" : detail));
        }

        /// <summary>Replaces an existing result of the same name, if there is one. So that even
        /// when Run()'s checks and ReportSliderOutcome()'s single check arrive asynchronously,
        /// only the latest, single result per Name ever survives.
        ///
        /// The previous city's results are dropped wholesale here (on this city's first write).
        /// See the explanation of _stale.</summary>
        private static void SetResult(AssumptionResult result)
        {
            lock (_gate)
            {
                if (_stale)
                {
                    _results.Clear();
                    _stale = false;
                }

                for (int i = 0; i < _results.Count; i++)
                {
                    if (_results[i].Name == result.Name) { _results.RemoveAt(i); break; }
                }
                _results.Add(result);
            }
        }

        private static void LogResult(AssumptionResult a)
        {
            if (a.Passed)
            {
                Log.Info("  PASS  " + a.Name);
                return;
            }

            // ★ Stops a "normal FAIL" becoming two lines of warning every time in an
            //   environment without the DLC (overall review). The line always appears —
            //   this changes only the weighting, it does not silence anything.
            if (IsExpectedFailure(a.Name))
            {
                Log.Info("  FAIL  " + a.Name
                         + "  (expected: the Natural Disasters DLC is not owned)");
                return;
            }

            Log.Warn("  FAIL  " + a.Name);
            Log.Warn("        -> " + a.Impact);
        }

        /// <summary>
        /// Whether this FAIL is "normal in this environment". Only true for a DLC-dependent
        /// check when the DLC is not owned.
        /// </summary>
        private static bool IsExpectedFailure(string name)
        {
            if (ModCompat.NaturalDisastersOwned) return false;
            lock (_gate) { return _expectedWithoutDlc.Contains(name); }
        }

        /// <summary>
        /// Returns only those FAILs that ought to be shown on the settings screen as "some
        /// features are unavailable". **Main thread only** (it reads
        /// <c>ModCompat.NaturalDisastersOwned</c>).
        ///
        /// It exists solely to take out the five checks that FAIL normally in an environment
        /// without the DLC. Without that, a plain vanilla environment shows the block of
        /// warnings **forever** — and when a real broken assumption does turn up, that one
        /// entry is lost in a block people have long since stopped reading.
        /// </summary>
        public static IList<AssumptionResult> UnexpectedFailures()
        {
            var all = LastResults;
            var failures = new List<AssumptionResult>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Passed) continue;
                if (IsExpectedFailure(all[i].Name)) continue;
                failures.Add(all[i]);
            }
            return failures;
        }

        private static void Report()
        {
            // ReportSliderOutcome may already have added one result before Run() (within the
            // same OnLevelLoaded, when the first call to IntensityUnlock.Apply() settled
            // immediately), so count the snapshot as it stands at this moment.
            var snapshot = LastResults;

            int passed = 0, failed = 0;
            bool sliderSettled = false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Passed) passed++; else failed++;
                if (snapshot[i].Name == SliderCheckName) sliderSettled = true;
            }

            // Printing just "4 passed, 0 FAILED" while a check is still unsettled reads as
            // "everything passed". What this infrastructure exists to remove is exactly that
            // "output you believed and that turned out to be otherwise", so always state the
            // denominator.
            //
            // "Not applicable" is not unsettled. Take it out of the denominator and treat it
            // as settled. Without that, an environment where NDR was detected (intensity
            // unlock OFF by default) would show "slider check pending" forever.
            int total = _sliderNotApplicable ? TotalCheckCount - 1 : TotalCheckCount;
            string summary;
            if (sliderSettled || _sliderNotApplicable)
            {
                summary = "ASSUMPTIONS  " + passed + " passed, " + failed + " FAILED";
                if (_sliderNotApplicable)
                {
                    summary += "  (" + total + " checks; slider check n/a: intensity unlock is off)";
                }
            }
            else
            {
                summary = "ASSUMPTIONS  " + snapshot.Count + " of " + total
                          + " checks: " + passed + " passed, " + failed
                          + " FAILED  (slider check pending)";
            }
            Log.Info(summary);
            for (int i = 0; i < snapshot.Count; i++)
            {
                LogResult(snapshot[i]);
            }
        }
    }
}
