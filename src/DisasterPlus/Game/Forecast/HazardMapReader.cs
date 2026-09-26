using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Reads the hazard value at a point. Vanilla only ever shows it as a colour, so
    /// turning it into a number is itself what this feature adds.
    ///
    /// Where reading the IL (Task 4 Step 1) demolished the brief's assumption:
    ///   the brief guessed: Byte DisasterManager.SampleDisasterHazardMap(Vector3, SubInfoMode)
    ///   the real signature: Color DisasterManager.SampleDisasterHazardMap(Vector3 pos)
    /// Vanilla's SampleDisasterHazardMap does not take a SubInfoMode at all (so calling it
    /// gives you no way to narrow things down). Nor does it return a byte: it returns a
    /// Color already interpolated for UI drawing. What it does inside is bilinearly
    /// interpolate the private field m_hazardAmount (a 256x256 byte grid, indexed by
    /// z*256+x) and blend that towards the neutral/active/target colours of
    /// InfoProperties.m_modeProperties[29] (the fixed index for DisasterHazard — the
    /// ldc.i4.s 29 in the IL); the number itself never reaches the caller. Working the
    /// number back out of the Color is impossible in general unless you know the actual
    /// RGBA of those three colours (they are data-driven and serialised, so the IL does
    /// not reveal them), so we do not approximate anything by way of the Color.
    ///
    /// The m_hazardAmount array is, however, a single grid — it is not split per submode
    /// (Task 4 follow-up, confirmed by reading the IL). DisasterManager.UpdateTexture
    /// consults each disaster AI's GetHazardSubMode and writes into this array only the
    /// hazard for "the submode InfoManager is showing right now". So the grid always holds
    /// exactly one submode's worth of data — the one on display — and SampleAt's subMode
    /// argument is not there to be passed on to a vanilla API, but is used as a guard that
    /// checks "does this match the submode currently on display?" (see SampleAt's doc
    /// below, and InfoModeSwitch.IsShowingHazardFor). Skip that check and you hand back a
    /// value for a different disaster type under the label that was asked for — a
    /// confidently wrong number.
    ///
    /// So this class reads m_hazardAmount directly. Reflective access to a private field
    /// is not new in this mod; it is the same trick as ToolRegistration.Register&lt;T&gt;()
    /// (if the field is not found we warn and return zero rather than throwing — so if a
    /// game update renames the field we just break quietly instead of crashing).
    ///
    /// Thread safety (**the reasoning here was corrected during the full review**. The
    /// conclusion is unchanged, but what used to be written here — "the sim thread writes
    /// and the main thread reads" — had it backwards and was wrong. Rewritten so that
    /// feature ② and everything after it do not inherit a wrong thread model):
    ///
    /// The only thing that **writes** m_hazardAmount is DisasterManager.UpdateTexture,
    /// which fills the grid with zeroes itself before calling each disaster AI's
    /// UpdateHazardMap (from the IL: the opening IL_0000-IL_003D is a nested loop of
    /// 256x256 stelem.i1). Sweeping the whole IL shows only two routes reach that
    /// UpdateTexture:
    ///   - DisasterManager.LateUpdate
    ///   - DisasterManager.UpdateHazardMapping, from set_HazardMapVisible / Awake
    /// and both are on the **main thread** (LateUpdate is a Unity message,
    /// set_HazardMapVisible is the info-view switch, i.e. the UI path). On top of that
    /// UpdateTexture updates a Texture2D, so Unity's own API restrictions mean it cannot
    /// run anywhere but the main thread.
    ///
    /// In other words both the writer and the reader are on the main thread, and as long
    /// as this class is called directly from the main thread there is no race (no need for
    /// the sim-thread-and-snapshot route that WeatherReader uses). Put the other way
    /// round: **do not call this class from the sim thread**. That is what would introduce
    /// a race.
    ///
    /// For completeness: the IL body of SampleDisasterHazardMap (vanilla's public API)
    /// only does ldfld and ldelem.u1 against m_hazardAmount with no stfld, confirming it
    /// is read-only.
    /// </summary>
    public static class HazardMapReader
    {
        // DisasterManager.m_hazardAmount: a private byte[], a 256x256 grid (index z*256+x).
        // From the IL: allocated in DisasterManager.Awake(); SampleDisasterHazardMap and
        // UpdateTexture only read it.
        private static readonly FieldInfo HazardAmountField =
            typeof(DisasterManager).GetField("m_hazardAmount",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Keep the warning about a missing field down to once per launch (this is called
        // often enough that a Log.Warn every time would flood the log).
        //
        // Deliberately not reset on level unload. "Not found" is a fact about the build of
        // the game this DLL is running against, not per-city state (it cannot change for
        // the lifetime of the process, i.e. from game launch to exit). Applying this
        // project's "session state is reset on unload" rule mechanically here and adding a
        // Reset() would give you resetting for its own sake — the same single line would
        // simply reappear every time you switch cities.
        private static bool _missingFieldWarned;

        // Unexpected exceptions inside SampleAt() (a type mismatch out of GetValue, say)
        // are sounded once with Log.Error for the same reason, and after that dropped to
        // Log.Diag (once per 512 sim frames, per key). This path can be called every
        // frame, so leaving an unconditional Log.Error here would bury the log whenever
        // the fault is permanent rather than a one-off — a future game update changing the
        // type of m_hazardAmount so GetValue keeps throwing InvalidCastException, for
        // instance. Make the first one impossible to miss, then throttle it, without
        // silencing it completely.
        private static bool _sampleErrorLogged;

        // Take the grid's resolution and cell size from vanilla's public consts (from the
        // IL: HAZARDMAP_RESOLUTION=256 Int32, HAZARDMAP_CELL_SIZE=38.4 Single, both public
        // static literals). The origin (the centre of the grid) is derived as half the
        // resolution, so we do not keep a third number of our own.
        //
        // **Correction (raised in the full review)**: this used to say that picking up the
        // const from the other assembly avoided the hazard of a hand-copied number going
        // stale. That is wrong. A C# const (a literal in the IL) is **baked into the
        // calling site at compile time**, so inside the shipped mod DLL these are the
        // immediate values 256 and 38.4. If the game changes them, nothing follows unless
        // this mod is rebuilt — which is to say it is exactly as safe at runtime as
        // hand-copying the numbers would have been.
        // The only way to catch a mismatch without a rebuild is to **read the metadata of
        // the loaded game at runtime**, so that check was added on the Assumptions side
        // ("DisasterManager hazard grid geometry is 256 x 38.4").
        private const int GridSize = DisasterManager.HAZARDMAP_RESOLUTION;
        private const float WorldUnitsPerCell = DisasterManager.HAZARDMAP_CELL_SIZE;
        private const float GridOrigin = DisasterManager.HAZARDMAP_RESOLUTION / 2f;

        /// <summary>
        /// Returns the hazard intensity at worldPos, 0-255.
        ///
        /// There are three ways to get ok=false: DisasterManager is absent / the subMode
        /// asked for is not the one on display (see below) / worldPos is outside the grid
        /// (±4915.2 m). All three mean "could not read it", never "it was 0".
        ///
        /// **Note that even ok=true does not guarantee the value means anything.** The
        /// grid is all zeroes whenever there is not a single located, under-way storm (see
        /// the doc on ForecastPanel.RefreshCursorHazard). Deciding not to let that 0 be
        /// read as "this place is safe" is not this class's job; it belongs to the caller,
        /// which looks at the located counts on WeatherSnapshot.
        ///
        /// subMode names the hazard type you want to read; it is not a token argument.
        /// m_hazardAmount is a single grid and only ever holds the hazard for the submode
        /// InfoManager is currently showing (see the class doc), so we check with
        /// <see cref="InfoModeSwitch.IsShowingHazardFor"/> whether subMode matches the
        /// submode on display, and if it does not we return ok=false rather than the value
        /// from the grid. Without that rejection the caller would unknowingly display a
        /// figure for an unrelated disaster type under the label it asked for — precisely
        /// the confidently wrong number this mod exists to avoid.
        ///
        /// Note that we read only the one cell on the bottom-left (floor) side of the
        /// lattice. Vanilla's SampleDisasterHazardMap returns a Color bilinearly
        /// interpolated from the four corners; this returns the raw lattice value with no
        /// interpolation (working a number back out of the Color would be inaccurate, so
        /// as the class doc above says we deliberately do not do it). As a result, near a
        /// cell boundary this return value can be out of step with the look of vanilla's
        /// heatmap by up to one cell. That is not a bug but the result of the trade-off:
        /// a raw lattice value beats working backwards from an inaccurate colour.
        /// </summary>
        public static byte SampleAt(Vector3 worldPos, InfoManager.SubInfoMode subMode, out bool ok)
        {
            ok = false;
            try
            {
                if (!Singleton<DisasterManager>.exists) return 0;

                // The grid only holds values for "the submode currently on display" (see
                // the class doc). If the subMode asked for is not the one on display, the
                // grid's value belongs to an unrelated disaster type, so treat this as
                // "could not read it". This guard is mandatory: it is what stops a
                // confidently wrong number — label and figure disagreeing — from reaching
                // the caller.
                if (!InfoModeSwitch.IsShowingHazardFor(subMode)) return 0;

                if (HazardAmountField == null)
                {
                    if (!_missingFieldWarned)
                    {
                        _missingFieldWarned = true;
                        Log.Error("DisasterManager.m_hazardAmount field not found (game update?)", null);
                    }
                    return 0;
                }

                var map = (byte[])HazardAmountField.GetValue(Singleton<DisasterManager>.instance);
                if (map == null || map.Length == 0) return 0;

                int gx = Mathf.FloorToInt(worldPos.x / WorldUnitsPerCell + GridOrigin);
                int gz = Mathf.FloorToInt(worldPos.z / WorldUnitsPerCell + GridOrigin);

                // Outside the grid, report "could not read it". This used to clamp, but
                // that meant returning the edge cell's value as "the hazard under the
                // cursor", which is one of the confidently wrong numbers this feature
                // exists to avoid (raised in the full review). The grid covers
                // 256 * 38.4 / 2 = ±4915.2 m, and with an 81-tile mod installed the
                // cursor routinely goes outside that. The value coming back would then be
                // "the hazard at the nearest edge", not "the hazard there" — a lie in its
                // hardest-to-spot form, where only the label is right and the contents
                // belong to a different place.
                if (gx < 0 || gx >= GridSize || gz < 0 || gz >= GridSize) return 0;

                int index = gz * GridSize + gx;
                if (index < 0 || index >= map.Length) return 0;

                ok = true;
                return map[index];
            }
            catch (System.Exception e)
            {
                // SampleAt can be called every frame for as long as the cursor is over
                // the map. Make the first one impossible to miss (Log.Error), then drop
                // to Log.Diag's per-key throttle (once per 512 sim frames) to hold the
                // rate down — spacing it out rather than silencing it completely.
                if (!_sampleErrorLogged)
                {
                    _sampleErrorLogged = true;
                    Log.Error("hazard sample failed", e);
                }
                else
                {
                    Log.Diag("HazardSample", "hazard sample failed: " + e.GetType().Name);
                }
                return 0;
            }
        }
    }
}
