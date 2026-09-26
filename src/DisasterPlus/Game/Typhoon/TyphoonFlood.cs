using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The state of river flooding. The panel shows no row when it is <see cref="Idle"/>,
    /// and **gives the reason, saying "this is not a fault"** when it is
    /// <see cref="NoSources"/> (design doc §7.4; treated the same way as ①'s "why is the
    /// hazard map empty").
    /// </summary>
    public enum TyphoonFloodState
    {
        /// <summary>Nothing has happened yet (the typhoon's gale radius has not reached
        /// anything).</summary>
        Idle,

        /// <summary>
        /// There is not one <c>TYPE_NATURAL</c> water source near the typhoon.
        /// **This is a correct result.** On a map with nothing but inland ponds, or one
        /// where no water sources were placed in the editor, this is right.
        /// </summary>
        NoSources,

        /// <summary>We are raising the water level.</summary>
        Raised,

        /// <summary>We have put the rise back. The water is drained naturally by the
        /// intake side.</summary>
        Restored,

        /// <summary>
        /// The route to it was unusable. **No row is shown on the panel**; the reason goes
        /// into the diagnostics (<see cref="TyphoonFlood.LastFailure"/>).
        /// </summary>
        Failed
    }

    /// <summary>
    /// The ledger entry for one water source ④ is raising.
    ///
    /// <see cref="Original"/> is **always the value from before ④ touched it**.
    /// <see cref="Raised"/> changes with every sweep (the rise grows as the typhoon
    /// approaches), but <see cref="Original"/> keeps the value we first grabbed —
    /// overwrite "the original value" with the raised one and restoring will not bring the
    /// water level back.
    ///
    /// <see cref="Handle"/> is a <c>WaterSimulation</c> handle and is **1-based**
    /// (measured from the IL in <see cref="TyphoonFlood"/>'s class doc).
    /// </summary>
    public struct TyphoonFloodedSource
    {
        public readonly ushort Handle;
        public readonly ushort Original;
        public readonly ushort Raised;

        public TyphoonFloodedSource(ushort handle, ushort original, ushort raised)
        {
            Handle = handle;
            Original = original;
            Raised = raised;
        }
    }

    /// <summary>
    /// River flooding from a typhoon. <b>Sim thread only.</b> On by default.
    ///
    /// **This feature alone can both freeze the game and corrupt a save.**
    ///
    /// ── Three routes ruled out up front (design doc §1.3) ─────────────────
    ///
    /// 1. **Vanilla has no flood disaster.** <c>GenericFloodAI</c> is an empty class with
    ///    0 fields and 0 methods (§D-1)
    /// 2. **Raising the sea level is no use.** There is no vanilla code that moves
    ///    <c>m_nextSeaLevel</c> during play, and it is uniform across the whole map, so it
    ///    cannot produce localised river flooding (§D-1)
    /// 3. **Putting a <c>TYPE_TSUNAMI</c> wave on a river does nothing.**
    ///    <c>GetSeaLevel</c> is **only evaluated on the map's outer ring in
    ///    <c>SimulateWater</c>** (§D-3(a)). The route the earthquake facts §B-3 explicitly
    ///    noted as "the way out" does not hold
    ///
    /// ── The one route that does work (§D-4) ───────────────────────────────
    ///
    /// A water source with <c>m_type == TYPE_NATURAL(1)</c> is **a self-regulating spring
    /// that pours until the target level <c>m_target</c> and sucks back once it is
    /// exceeded**. What is more, the discharge loop **skips** cells where
    /// <c>natural &amp;&amp; terrain[i] &gt;= m_target</c> (IL_1E5E / IL_1EBF), so
    /// **it puts no water on the hilltops and only the valleys get wet**.
    /// That is river flooding itself.
    ///
    /// **We create no new water sources** (design doc §2 chooses (i) with (ii) as the
    /// alternative). Never call <c>CreateWaterSource</c> and neither "false at the 65535
    /// ceiling" nor "the poured water never drains" can arise in the first place.
    /// **To whoever comes next: do not reach for <c>CreateWaterSource</c> to get "stronger
    /// flooding".** Put a new spring down and the only way left to drain the remaining
    /// water after stopping is "leave the intake-side source in place", which adds one more
    /// restore route.
    ///
    /// Layering <c>DisasterHelpers.SplashWater</c> (a <c>TYPE_IMPACT</c> wave) on top as
    /// presentation is fine, but **it adds no volume, so on its own it is not flooding**
    /// (§D-3(b)). **We do not add it in this task** — restoring <c>m_target</c> matters
    /// more than how the water surface looks.
    ///
    /// ── How to take the lock (trap 3. Drop it and the game stops responding) ──
    ///
    /// **Re-read directly from the IL and settled for this task** (a re-confirmation of
    /// §D-4's claims):
    ///
    /// ```
    /// WaterSimulation.LockWaterSource(ushort source)     // public, instance
    ///   IL_0000  br IL_0005
    ///   IL_0005  Monitor.TryEnter(m_waterSources, 0) ; brfalse IL_0005   // ★ spin lock
    ///   IL_0016  return m_waterSources.m_buffer[source - 1]              // ★ 1-based
    ///   -- no Monitor.Exit and no try/finally region (not one leave / endfinally) --
    ///
    /// WaterSimulation.UnlockWaterSource(ushort source, WaterSource data) // public, instance
    ///   IL_0000  m_waterSources.m_buffer[source - 1] = data              // ★ 1-based
    ///   IL_0019  Monitor.Exit(m_waterSources)                            // ★ the only release path
    /// ```
    ///
    /// So <c>UnlockWaterSource</c> **always goes in a <c>finally</c>**. Drop it and the
    /// dedicated water simulation thread spins for ever in the <c>TryEnter(_, 0)</c> loop
    /// and **the game stops responding.**
    ///
    /// **Do not add anything that can throw inside the <c>try</c>.** Both logging and
    /// updating the diagnostic counters happen **outside** the <c>finally</c>.
    /// <c>Log.Diag</c> takes a <c>lock</c> internally, so calling it while holding the
    /// water source's monitor would create a **lock-ordering** problem, a kind of problem
    /// this mod has never once had.
    ///
    /// ── ★ Validate the handle before calling (a danger newly found in the IL) ──
    ///
    /// <c>LockWaterSource</c> **does not check** <c>source</c>'s range.
    /// <c>Monitor.TryEnter</c> is at IL_000C and the array access at IL_0024, so
    /// **passing an out-of-range handle raises an <c>IndexOutOfRangeException</c> right
    /// after the monitor is taken and leaves without reaching `Monitor.Exit`** — a
    /// permanent deadlock. Handle 0 is especially dangerous because it becomes
    /// <c>m_buffer[-1]</c>.
    /// Never call <c>LockWaterSource</c> without going through
    /// <see cref="IsValidHandle"/> first.
    ///
    /// ── The sweep happens outside the lock (§8.3) ─────────────────────────
    ///
    /// <c>m_waterSources</c> is a public <c>FastList</c>, so we read it directly.
    /// We take the lock only for **the one entry we are writing**. Loop over all of them
    /// inside the lock and the water thread stalls for a whole step.
    ///
    /// Read <c>m_buffer</c> first and <c>m_size</c> second, and **stop at the shorter of
    /// the two** — in the other order we could be caught between
    /// <c>FastList.Add</c>'s reallocation and grab "the new length with the old array",
    /// which goes out of range.
    ///
    /// ── Restoration is called from three places (trap 4) ──────────────────
    ///
    /// Water sources **burn into the save** through
    /// <c>WaterSimulation.Data.Serialize</c> (§D-4). <see cref="RestoreAll"/> is
    /// **idempotent** and is called from these three places:
    ///
    /// | Caller | Why it is needed |
    /// |---|---|
    /// | <c>TyphoonController.Forget</c> | The typhoon ended or the slot was lost (the normal route) |
    /// | <c>TyphoonFeature.OnLevelUnloading</c> | When leaving the city. Forget it and the next city tries to restore the previous city's handles |
    /// | <c>DisasterPlusSerialization.OnSaveData</c> | See below. Without it, the burst rivers burn into the save |
    ///
    /// ── On saving it is "put back, delay, put back again" (§8.5) ──────────
    ///
    /// If the player saves in the middle of a typhoon, that save contains the raised
    /// <c>m_target</c>. Remove the mod and open that save and **the rivers stay burst for
    /// ever**.
    ///
    /// This project **has shipped a failure of exactly this shape once already** (a
    /// temporary flag leaking into the save). What was established then was:
    ///
    /// > **A mod's <c>OnSaveData</c> runs before vanilla writes its arrays.** So
    /// > "clear inside <c>OnSaveData</c> and re-apply in the <c>finally</c>" **leaks**.
    /// > The re-apply must be **delayed** with <c>SimulationManager.AddAction</c>.
    ///
    /// <see cref="SnapshotAndRestoreForSave"/> does "put back what is currently raised and
    /// return its contents", and <see cref="ReapplyAfterSave"/> does "raise it again if the
    /// typhoon is still Active". The latter **runs on the sim thread** by
    /// <c>AddAction</c>'s contract.
    /// <c>SimulationManager.AddAction(System.Action)</c> is **a public instance method
    /// returning <c>AsyncAction</c>** (measured from the IL in this task).
    ///
    /// ── Give the reason when nothing happened (design doc §7.4) ───────────
    ///
    /// **If the map in question has not one <c>TYPE_NATURAL</c> water source, nothing
    /// happening is correct.** We raise <see cref="TyphoonFloodState.NoSources"/> and
    /// display the reason.
    /// **Do not log it as a warning** — it is not a fault.
    /// <see cref="NaturalSourceCount"/> is map-dependent and unknown, so it always goes in
    /// the diagnostics.
    /// </summary>
    public static class TyphoonFlood
    {
        /// <summary>The sweep interval (in-game time equivalent to a frame count). 256, the
        /// same as wind damage.</summary>
        private const int IntervalFrames = 256;

        /// <summary>
        /// The ceiling on how many water sources are **newly grabbed** in one sweep.
        ///
        /// We take the water simulation's monitor for each one, so with no ceiling we would
        /// keep stalling the water thread intermittently for a whole tick. Whatever is left
        /// over is picked up on the next sweep (updating entries already in the ledger does
        /// not count against this ceiling — count them and updates stop running the moment
        /// the ledger grows past the ceiling).
        /// </summary>
        private const int MaxNewSourcesPerPass = 64;

        /// <summary><c>WaterSource.TYPE_NATURAL</c> (§D-4).</summary>
        private const ushort TypeNatural = 1;

        /// <summary>
        /// The ledger of water sources ④ is raising (handle → the original value and the
        /// current one).
        /// **Touched only from the sim thread.**
        /// </summary>
        private static readonly Dictionary<ushort, TyphoonFloodedSource> _raised =
            new Dictionary<ushort, TyphoonFloodedSource>();

        /// <summary>
        /// The handles that were in range on this sweep (used to drop the ones that have
        /// moved out of range).
        /// **We do not build it every tick**, so it is reused.
        ///
        /// ★ A <c>HashSet</c>, not a <c>List</c> (whole-project review).
        ///   <see cref="DropOutOfRange"/> asks "was this in range this time" for every
        ///   element of the ledger, and with a List that <c>Contains</c> becomes a linear
        ///   scan, making it **the ledger times the in-range set**. The ledger can grow to
        ///   the number of water sources on a map with big rivers, so even at once per 256
        ///   frames we do not leave this O(n^2). .NET 3.5 does have
        ///   HashSet&lt;T&gt; (System.Core).
        /// </summary>
        private static readonly HashSet<ushort> _inRange = new HashSet<ushort>();

        /// <summary>Scratch space for the handles to drop from the ledger.
        /// <c>Clear()</c>ed and reused each time.</summary>
        private static readonly List<ushort> _toDrop = new List<ushort>();

        private static float _minutesSincePass;
        private static TyphoonFloodState _state = TyphoonFloodState.Idle;
        private static int _naturalSourceCount;
        private static float _lastPeakRiseMetres;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>The current state (the five states in design doc §7.4).</summary>
        public static TyphoonFloodState State { get { return _state; } }

        /// <summary>
        /// The number of <c>TYPE_NATURAL</c> water sources across the whole map.
        /// **Map-dependent and unknown** (§D-4 / design doc §6), so it always goes in the
        /// diagnostics. 0 is not a fault.
        /// </summary>
        public static int NaturalSourceCount { get { return _naturalSourceCount; } }

        /// <summary>How many water sources ④ is currently raising.</summary>
        public static int TouchedCount { get { return _raised.Count; } }

        /// <summary>The rise applied at the centre in the most recent sweep (m).</summary>
        public static float LastPeakRiseMetres { get { return _lastPeakRiseMetres; } }

        /// <summary>
        /// Why the route was unusable (English, for diagnostics; null if there is none).
        /// **This is the only means of telling "it was unusable" from "nothing is
        /// happening".**
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// **Always call on level unload.** It finishes <see cref="RestoreAll"/> and then
        /// throws the session state away. It is idempotent.
        /// </summary>
        public static void Reset()
        {
            RestoreAll();

            // Even if RestoreAll could not empty the ledger (because the water simulation
            // was unreachable), do not carry the handles into the next city. Going to
            // restore with the previous city's handles **rewrites the level of an unrelated
            // river.**
            _raised.Clear();

            _minutesSincePass = 0f;
            _state = TyphoonFloodState.Idle;
            _naturalSourceCount = 0;
            _lastPeakRiseMetres = 0f;
            _lastFailure = null;
            // ★ _errorLogged is not reset (it is a fact about the game build, not per-city
            //    state. The same decision as TyphoonLightning / TyphoonWind).
        }

        /// <summary>
        /// Sim thread. **Always call it from below the pause guard in
        /// <c>TyphoonFeature.OnSimulationTick</c>** (otherwise rivers burst while the game
        /// is paused).
        /// When the setting is OFF the caller does not call it.
        ///
        /// We read **only the rainfall** from <paramref name="snapshot"/>
        /// (<c>WeatherManager.m_currentRain</c>; it is one tick old, but rainfall only moves
        /// at 0.0002/step so there is no difference). The position and the intensity are
        /// read directly from <c>TyphoonController</c>'s statics on the same thread.
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step(snapshot, deltaMinutes);
            }
            catch (System.Exception e)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = e.GetType().Name + ": " + e.Message;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon river flooding failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFlood",
                             "typhoon river flooding failed: " + e.GetType().Name);
                }

                // ★ Do not leave things raised after a failure. Skip this and the water
                //    sources grabbed by the sweep that threw are never put back by anybody
                //    and burn into the save.
                RestoreAll();
            }
        }

        private static void Step(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            // ★ Advance the interval accumulator **before looking at the typhoon** (the
            //   same as wind damage; ②'s I3).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (!TyphoonController.Active)
            {
                // If the typhoon has ended, always put things back. **This is not the normal
                // restore route** (normally it is TyphoonController.Forget → RestoreAll),
                // but it catches anything that slipped through.
                if (_raised.Count > 0) RestoreAll();
                return;
            }

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;
            _minutesSincePass = 0f;

            int strength = ModSettings.TyphoonFloodStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;

            if (strength == 0)
            {
                // The rivers must recede the moment the slider goes to 0. Do not leave them
                // raised.
                if (_raised.Count > 0) RestoreAll();
                return;
            }

            Sweep(snapshot, strength);
        }

        /// <summary>One sweep.</summary>
        private static void Sweep(TyphoonSnapshot snapshot, int strength)
        {
            var sim = WaterSim();
            if (sim == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "TerrainManager.WaterSimulation is not reachable";
                return;
            }

            var sources = sim.m_waterSources;
            if (sources == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "WaterSimulation.m_waterSources is null";
                return;
            }

            // ★ Read m_buffer first and m_size second, and stop at the shorter (class doc).
            var buffer = sources.m_buffer;
            if (buffer == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "WaterSimulation.m_waterSources.m_buffer is null";
                return;
            }
            int size = sources.m_size;
            if (size > buffer.Length) size = buffer.Length;

            float gale = TyphoonController.GaleRadius;
            var centre = TyphoonController.Centre;

            float rain = snapshot != null && snapshot.WeatherReadable ? snapshot.Rain : 0f;
            float peak = FloodTarget.RiseMetresOf(TyphoonController.Intensity, rain, strength);
            _lastPeakRiseMetres = peak;

            _inRange.Clear();
            int natural = 0;
            int newlyTaken = 0;
            int applied = 0;

            for (int i = 0; i < size; i++)
            {
                if (buffer[i].m_type != TypeNatural) continue;
                natural++;

                // ★ Handles are 1-based (measured from the IL in the class doc).
                ushort handle = (ushort)(i + 1);

                float dx = buffer[i].m_outputPosition.x - centre.X;
                float dz = buffer[i].m_outputPosition.z - centre.Z;
                float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

                float rise = FloodTarget.RiseAt(distance, gale, peak);
                if (!(rise > 0f)) continue;

                TyphoonFloodedSource entry;
                bool known = _raised.TryGetValue(handle, out entry);
                if (!known && newlyTaken >= MaxNewSourcesPerPass) continue;

                ushort original, actual;
                if (!WriteTarget(sim, handle, size, known, entry.Original, rise,
                                 out original, out actual))
                {
                    // The type had changed (it was released and reused). Drop it from the
                    // ledger.
                    if (known) _raised.Remove(handle);
                    continue;
                }

                if (!known) newlyTaken++;

                // ★ Original keeps **the value we first grabbed** (WriteTarget returns the
                //   argument unchanged when known is true).
                //   Make the raised value "the original" and restoring will not bring the
                //   water level back.
                _raised[handle] = new TyphoonFloodedSource(handle, original, actual);
                _inRange.Add(handle);
                applied++;
            }

            _naturalSourceCount = natural;

            DropOutOfRange(sim, size);

            if (applied > 0) _state = TyphoonFloodState.Raised;
            else if (_raised.Count > 0) _state = TyphoonFloodState.Raised;
            else _state = TyphoonFloodState.NoSources;

            _lastFailure = null;

            WriteDiag(natural, applied, peak, gale);
        }

        /// <summary>
        /// Put back and drop from the ledger the water sources that are in it but did not
        /// fall in range this time.
        /// The obvious behaviour that the rivers recede once the typhoon moves away lives
        /// here.
        /// </summary>
        private static void DropOutOfRange(WaterSimulation sim, int size)
        {
            if (_raised.Count == 0) return;

            _toDrop.Clear();
            foreach (var entry in _raised)
            {
                // It is a HashSet, so this is constant time per entry (_inRange's doc).
                if (!_inRange.Contains(entry.Key)) _toDrop.Add(entry.Key);
            }

            for (int i = 0; i < _toDrop.Count; i++)
            {
                ushort handle = _toDrop[i];
                TyphoonFloodedSource s;
                if (_raised.TryGetValue(handle, out s)) RestoreOne(sim, handle, size, s.Original);
                _raised.Remove(handle);
            }
        }

        /// <summary>
        /// Write one water source's <c>m_target</c>. **This is the only place that takes
        /// the lock.**
        ///
        /// **Put nothing that can throw inside the <c>try</c>** (logging and diagnostics go
        /// outside). The <c>UnlockWaterSource</c> in the <c>finally</c> is the only release
        /// path (class doc).
        /// </summary>
        /// <param name="known">Whether it is in the ledger. If false we read the original
        /// value here.</param>
        /// <param name="knownOriginal">The "value from before ④ touched it" held in the
        /// ledger.</param>
        /// <param name="original">
        /// The "value from before ④ touched it". If <paramref name="known"/> is true,
        /// <paramref name="knownOriginal"/> comes straight back. **The raised value is never
        /// returned.**
        /// </param>
        /// <param name="actual">The value actually written.</param>
        /// <returns>Whether we could write (false if it is not <c>TYPE_NATURAL</c>).</returns>
        private static bool WriteTarget(WaterSimulation sim, ushort handle, int size,
                                        bool known, ushort knownOriginal, float rise,
                                        out ushort original, out ushort actual)
        {
            original = knownOriginal;
            actual = 0;

            // ★ Validate the handle first. LockWaterSource does not check the range and
            //   reads m_buffer[handle - 1] **after** taking the monitor, so an out-of-range
            //   handle leaves without reaching Monitor.Exit = a permanent deadlock.
            if (!IsValidHandle(handle, size)) return false;

            ushort seen = knownOriginal;
            bool ok = false;

            // ★ LockWaterSource returns while still holding the Monitor (§D-4; there is no
            //   Monitor.Exit in IL_0005-002E). UnlockWaterSource is the only release path.
            //   **Always pair them with try/finally.** Drop it and the dedicated water
            //   simulation thread seizes up in its spin lock and the game stops responding.
            WaterSource src = sim.LockWaterSource(handle);
            try
            {
                if (src.m_type == TypeNatural)
                {
                    if (!known) seen = src.m_target;
                    src.m_target = FloodTarget.RaisedTarget(seen, rise);
                    ok = true;
                }
            }
            finally
            {
                // ★ Whether we return or throw, we always come through here.
                sim.UnlockWaterSource(handle, src);
            }

            if (!ok) return false;

            original = seen;
            actual = src.m_target;
            return true;
        }

        /// <summary>
        /// Put one water source back to its original value. The same lock shape as
        /// <see cref="WriteTarget"/>.
        /// **Do not let an exception escape even if it cannot be restored** — restoration is
        /// called from three places, and one failure must not stop the rest of it.
        /// </summary>
        private static void RestoreOne(WaterSimulation sim, ushort handle, int size,
                                       ushort original)
        {
            if (!IsValidHandle(handle, size)) return;

            try
            {
                WaterSource src = sim.LockWaterSource(handle);
                try
                {
                    // If the type has changed (released and reused) leave it alone.
                    // **Do not write ④'s value into a water source that belongs to somebody
                    // else.**
                    if (src.m_type == TypeNatural) src.m_target = original;
                }
                finally
                {
                    sim.UnlockWaterSource(handle, src);
                }
            }
            catch (System.Exception e)
            {
                // The logging is outside the lock (this is outside the finally).
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                         "could not restore water source #" + handle + ": " + e.GetType().Name);
            }
        }

        /// <summary>
        /// Whether the handle is valid as <c>m_buffer[handle - 1]</c>. It is **1-based**, so
        /// 0 is invalid.
        /// Always go through this before calling <c>LockWaterSource</c> (the danger in the
        /// class doc).
        /// </summary>
        private static bool IsValidHandle(ushort handle, int size)
        {
            return handle >= 1 && handle <= size;
        }

        /// <summary>
        /// **Put every raise back. Idempotent** (called twice, it does nothing if it is
        /// already empty).
        ///
        /// It is called from three places (the table in the class doc), so overlapping is
        /// normal.
        /// </summary>
        public static void RestoreAll()
        {
            if (_raised.Count == 0)
            {
                if (_state == TyphoonFloodState.Raised) _state = TyphoonFloodState.Restored;
                return;
            }

            var sim = WaterSim();
            if (sim == null)
            {
                // The water simulation is unreachable (the city has already gone, etc.).
                // **Throw the ledger away** — keeping it only means rewriting an unrelated
                // river in the next city.
                //
                // ★ State first, logging second. The other way round, if the logging throws
                //   the ledger survives — which is exactly the state this feature most wants
                //   to avoid, "going to restore the previous city's handles in the next
                //   city".
                int lost = _raised.Count;
                _raised.Clear();
                _state = TyphoonFloodState.Restored;

                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                         "the water simulation is gone; " + lost
                         + " raised water source(s) could not be restored");
                return;
            }

            int size = SizeOf(sim);
            int restored = 0;

            foreach (var entry in _raised)
            {
                RestoreOne(sim, entry.Key, size, entry.Value.Original);
                restored++;
            }

            _raised.Clear();
            _state = TyphoonFloodState.Restored;

            // The logging goes somewhere we hold not one lock.
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                     "restored " + restored + " water source target(s) to their original level");
        }

        /// <summary>
        /// **Call immediately before saving** (at the top of
        /// <c>DisasterPlusSerialization.OnSaveData</c>).
        /// It puts back what is currently raised and returns its contents.
        /// <c>null</c> if nothing is raised.
        ///
        /// The caller must pass the return value to <see cref="ReapplyAfterSave"/> through
        /// <c>SimulationManager.AddAction</c>. **Put it back immediately here (or in a
        /// <c>finally</c>) and we would be raising it again before vanilla writes its
        /// arrays, and the leak comes back** (class doc §8.5).
        /// </summary>
        public static List<TyphoonFloodedSource> SnapshotAndRestoreForSave()
        {
            if (_raised.Count == 0) return null;

            var copy = new List<TyphoonFloodedSource>(_raised.Count);
            foreach (var entry in _raised) copy.Add(entry.Value);

            RestoreAll();

            Log.Info("typhoon flooding: lowered " + copy.Count
                     + " water source(s) to their original level before saving");
            return copy;
        }

        /// <summary>
        /// **Call on the sim thread after the save has finished**
        /// (<c>SimulationManager.AddAction</c>'s contract). If the typhoon is still running,
        /// raise them again. If it has ended, do nothing.
        ///
        /// In the meantime a sweep may have grabbed the same water source again. In that
        /// case **the ledger wins** — that side has a value computed from the typhoon's
        /// current position, while this side holds a stale value from the moment of the
        /// save.
        /// </summary>
        public static void ReapplyAfterSave(List<TyphoonFloodedSource> raised)
        {
            if (raised == null || raised.Count == 0) return;

            try
            {
                if (!TyphoonController.Active) return;

                var sim = WaterSim();
                if (sim == null) return;

                int size = SizeOf(sim);
                int reapplied = 0;

                for (int i = 0; i < raised.Count; i++)
                {
                    var s = raised[i];
                    if (_raised.ContainsKey(s.Handle)) continue;   // the ledger has already regrabbed it

                    if (WriteRaw(sim, s.Handle, s.Raised, size))
                    {
                        _raised[s.Handle] = s;
                        reapplied++;
                    }
                }

                if (reapplied > 0)
                {
                    _state = TyphoonFloodState.Raised;
                    Log.Info("typhoon flooding: re-raised " + reapplied
                             + " water source(s) after the save");
                }
            }
            catch (System.Exception e)
            {
                // Even if they cannot be raised again, **the rivers stay at their original
                // level**, so there is no harm.
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodReapply",
                         "could not re-raise water sources after the save: " + e.GetType().Name);
            }
        }

        /// <summary>
        /// Write a value straight into <c>m_target</c> (for re-application only).
        /// The same lock shape as <see cref="WriteTarget"/>.
        ///
        /// The caller has already validated the handle, but **we validate here too** —
        /// calling <c>LockWaterSource</c> with an out-of-range handle throws right after the
        /// Monitor is taken, never reaches <c>Monitor.Exit</c> and becomes a permanent
        /// deadlock (class doc). This is the kind of failure where that one line is worth
        /// having twice.
        /// </summary>
        private static bool WriteRaw(WaterSimulation sim, ushort handle, ushort target,
                                     int size)
        {
            if (!IsValidHandle(handle, size)) return false;

            bool ok = false;

            WaterSource src = sim.LockWaterSource(handle);
            try
            {
                if (src.m_type == TypeNatural)
                {
                    src.m_target = target;
                    ok = true;
                }
            }
            finally
            {
                sim.UnlockWaterSource(handle, src);
            }

            return ok;
        }

        /// <summary>
        /// The route to <c>WaterSimulation</c>. **Measured from the IL in this task**:
        /// <c>TerrainManager.WaterSimulation</c> is **a public instance property**
        /// (backed by the private field <c>m_waterSimulation</c>).
        ///
        /// <c>Singleton&lt;T&gt;.instance</c> runs <c>FindObjectOfType</c> and
        /// <c>new GameObject</c> when <c>sInstance</c> is null, which makes it a main thread
        /// only API, so we check <c>exists</c> first.
        /// </summary>
        private static WaterSimulation WaterSim()
        {
            if (!Singleton<TerrainManager>.exists) return null;

            var tm = Singleton<TerrainManager>.instance;
            return tm == null ? null : tm.WaterSimulation;
        }

        /// <summary>
        /// The ceiling on valid handles. Read in the same order as <see cref="Sweep"/>
        /// (<c>m_buffer</c> first, <c>m_size</c> second, taking the shorter).
        /// </summary>
        private static int SizeOf(WaterSimulation sim)
        {
            var sources = sim.m_waterSources;
            if (sources == null) return 0;

            var buffer = sources.m_buffer;
            if (buffer == null) return 0;

            int size = sources.m_size;
            return size > buffer.Length ? buffer.Length : size;
        }

        /// <summary>
        /// **Written every time, even when 0 were raised.** "There are no water sources",
        /// "nothing fell in range", "it is switched off in the settings" and "the route is
        /// broken" all look identical on screen (the rivers do not rise), so this is the
        /// only place they can be told apart.
        ///
        /// <c>Log.Diag</c> thins the same key out, but **the string concatenation in the
        /// arguments still runs every time**, so we bail out first with
        /// <c>DiagEnabled</c>.
        /// </summary>
        private static void WriteDiag(int natural, int applied, float peak, float gale)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFlood",
                "flood: naturalSources=" + natural
                + " raised=" + _raised.Count
                + " appliedThisPass=" + applied
                + " peakRise=" + peak.ToString("F2") + " m"
                + " galeRadius=" + gale.ToString("F0")
                + " state=" + _state
                + (natural == 0
                    ? "  (this map has no natural water sources; nothing is wrong)"
                    : ""));
        }
    }
}
