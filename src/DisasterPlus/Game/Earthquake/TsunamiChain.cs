using ColossalFramework;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Where the tsunami chain currently stands. **The display must always state this.**
    /// </summary>
    public enum TsunamiChainState
    {
        /// <summary>Nothing scheduled and nothing raised (including an inland hypocentre). No row is shown.</summary>
        Idle,

        /// <summary>An undersea hypocentre was found and we are waiting for the delay to elapse.</summary>
        Scheduled,

        /// <summary>The tsunami was triggered and a wave really was raised (<c>m_waveIndex != 0</c>).</summary>
        Raised,

        /// <summary>
        /// It was triggered, but <c>FindSea</c> could not find a sea-side stretch and no
        /// wave was raised. **This is not a failure.** On an inland map it is the correct
        /// outcome (§B-3).
        /// </summary>
        NoSea,

        /// <summary>The Natural Disasters DLC is not owned and there is no <c>TsunamiAI</c> prefab (§B-5).</summary>
        NoDlc,

        /// <summary>The disaster slots are full, or similar. The reason goes to the diagnostics; no row is shown in the panel.</summary>
        Failed
    }

    /// <summary>
    /// **The first part of layer 2. When an earthquake happens under the sea, raise a
    /// tsunami after a delay.** <b>Sim thread only.</b> Off by default.
    ///
    /// ── What is and is not possible (it decides the UI's wording) ──────────
    ///
    /// The request was "trigger an earthquake under the sea and no plate-boundary tsunami
    /// arrives". **"The wave spreads from the hypocentre" is literally impossible with
    /// <c>TsunamiAI</c>** (§B-3):
    ///
    ///   - <c>FindSea</c> **considers only cells on the map's border**
    ///     (1080 &lt;&lt; 3 = 4320 of them)
    ///   - <c>m_targetPosition</c> is no more than a hint about which border stretch to
    ///     pick, and at start it is **overwritten** with the origin cell's position
    ///     (IL_03D3-0426)
    ///   - <c>m_angle</c> is likewise derived from the stretch's inward normal and
    ///     **overwritten** (IL_042B-0438)
    ///
    /// So what can be delivered is "**the tsunami arrives from the sea-side border
    /// nearest the hypocentre**", and the panel says exactly that
    /// (<c>Strings.EarthquakeTsunamiFromShore</c>).
    ///
    /// ★★ **As of 2026-08-29 that no longer applies.** <c>TsunamiAI</c> is not used;
    ///    <c>TsunamiWave</c> places a <c>TYPE_IMPACT</c> water wave at the hypocentre and
    ///    the wave spreads out in circles from it.
    /// **Never write "the wave spreads from the hypocentre".**
    ///
    /// ── Four traps (all pinned down in the IL) ─────────────────────────
    ///
    /// 1. <c>m_flags |= SelfTrigger (64)</c> is **mandatory**.
    ///    <c>TsunamiAI.StartDisaster</c> checks that bit at IL_000E and returns
    ///    immediately if it is not set (§B-2). No wave is created at all in that case.
    ///    <b>Unlike an earthquake, though, a tsunami does not "get stuck in
    ///    Emerging"</b> — re-measured as part of this task,
    ///    <c>TsunamiAI.IsStillEmerging</c>, <c>IsStillActive</c> and
    ///    <c>IsStillClearing</c> **never once read** <c>m_activationFrame</c> (they use
    ///    only <c>m_startFrame</c>, <c>m_angle</c> and <c>m_targetPosition</c>). Design
    ///    doc §4.1 says "without it the disaster is stuck in Emerging forever (both
    ///    earthquake and tsunami)", but **the second half does not apply to tsunamis**.
    ///    The conclusion (always set it) is unchanged.
    /// 2. **Always check <c>CreateDisaster</c>'s return value.** On false the output is 0,
    ///    and writing to it anyway **overwrites somebody else's disaster slot**
    ///    (§E-1; the limit is 256).
    /// 3. If <c>FindDisasterInfo&lt;TsunamiAI&gt;()</c> is null, the DLC is not owned
    ///    (§B-5). <c>ModCompat.NaturalDisastersOwned</c> is only a pre-check;
    ///    **this scan is the authority at the moment of use.**
    /// 4. <c>FindSea</c> returns false when the sea-side stretch is under 10 cells, and no
    ///    tsunami happens (§B-3).
    ///    **On an inland map, nothing happening is correct. Do not treat it as a failure.**
    ///
    /// ── Cleaning up after <c>FindSea</c> fails (the conclusion of plan Step 1) ─────
    ///
    /// The plan said to decide only after pinning the clean-up down in the IL. Measured:
    ///
    /// ```
    /// TsunamiAI.StartDisaster   IL_0003 base.StartDisaster (★ before the SelfTrigger check)
    ///                           IL_000E if ((m_flags & 64) == 0) return
    ///                           IL_003D if (!FindSea(...)) return      ← we bail out here
    /// DisasterAI.StartDisaster  m_flags = (m_flags & 0xFFFC88C7) | Emerging(4)
    ///                           m_startFrame = m_currentFrameIndex     ★ always populated
    ///                           (it never writes m_activationFrame at all)
    /// TsunamiAI.IsStillEmerging elapsed = currentFrame - m_startFrame
    ///                           travel  = elapsed * 0.125
    ///                           dir     = (-sin(m_angle), 0, cos(m_angle))
    ///                           corner  = the ±4800 on the side away from the direction of travel
    ///                           return dot(corner - m_targetPosition, dir) > travel
    /// IsStillActive / IsStillClearing have the same shape (Active subtracts 3000 from
    /// travel, and Clearing takes the corner on the side it is travelling towards).
    /// **None of the three reads m_activationFrame.**
    /// ```
    ///
    /// So even with no wave raised, the phase advances naturally relative to
    /// <c>m_startFrame</c>, and once it reaches <c>Finished</c>,
    /// <c>DisasterManager.SimulationStepImpl</c> calls <c>ReleaseDisaster</c> and frees
    /// the slot (§E-1). At worst it is gone within 107,520 frames (about 39 game hours).
    /// → We take the first row of the plan's decision table: **do nothing (log only)**.
    ///
    /// <c>DisasterManager.ReleaseDisaster(ushort)</c> was also confirmed to be public,
    /// but **we do not use it**. <c>base.StartDisaster</c> has already called
    /// <c>DisasterWrapper.OnDisasterStarted(id)</c>, and deleting the slot straight after
    /// that looks, to a mod implementing <c>IDisastersExtension</c>, like "a disaster that
    /// started and never announced an ending". Since we know it will not get stuck, there
    /// is no reason to add an unverified side effect.
    ///
    /// **Either way, always check <c>m_waveIndex == 0</c>** (<c>DisasterData.m_waveIndex</c>
    /// is a public UInt16, measured as part of this task). The fact that no wave was
    /// raised is reported to the UI, with a reason, as
    /// <see cref="TsunamiChainState.NoSea"/>.
    ///
    /// ── Only one is tracked ──────────────────────────────────────
    ///
    /// We never raise tsunamis from several earthquakes at once. This limit stops ② from
    /// eating into the 256-slot disaster limit.
    /// </summary>
    public static class TsunamiChain
    {
        /// <summary>
        /// How long after a trench earthquake the tsunami arrives (in game minutes).
        ///
        /// ★ If the <c>eqTsunamiDelay</c> setting is longer than this, it is shortened to
        ///   this value — but for trench quakes only. **A shorter value set by the player
        ///   is respected** (we only ever shorten).
        /// </summary>
        internal const int TrenchDelayMinutes = 3;

        /// <summary>
        /// Below this intensity no tsunami is scheduled. A tsunami at intensity 0 has a
        /// wave height of 0 (<c>m_delta = m_height * 1024 * i / 55</c>, §B-3), giving the
        /// inexplicable state "it was triggered and nothing happened".
        ///
        /// ★★ **For a long time nobody looked at this.** (2026-08-31, fourth round of
        ///   verification) The doc said it was there "so we never create a state where it
        ///   is triggered and nothing arrives", and yet nothing referenced it. Even
        ///   intensity 1 got scheduled, the cap fell to its lower bound of 1 m, and
        ///   <b>genuinely nothing arrived</b>.
        /// </summary>
        private const byte MinIntensity = 10;

        /// <summary>
        /// <b>The number of the earthquake whose wave has been raised.</b>
        ///
        /// ★★ <c>_quakeId</c> goes back to 0 in <c>Forget()</c> once the earthquake's
        ///   phase ends, but the wave travels for over 10 minutes after that. Watching
        ///   only that made the panel look like it was <b>winding back from "tsunami
        ///   raised" to "expected"</b> (2026-08-31, sixth round of verification).
        ///   **Remember the fact that one was raised separately.**
        /// </summary>
        private static ushort _raisedQuakeId;

        private static ushort _quakeId;
        private static bool _havePhase;
        private static EarthquakePhase _lastPhase;
        private static uint _dueFrame;
        private static TsunamiChainState _state;

        /// <summary>Whether an exception has been reported once with <c>Log.Error</c> (after which we drop to Diag).</summary>
        private static bool _errorLogged;

        /// <summary>
        /// The current state. **Written by the sim thread and read on that same thread by
        /// <see cref="EarthquakeReader"/>, which puts it on the snapshot.** The main
        /// thread never reads it directly.
        /// </summary>
        public static TsunamiChainState State { get { return _state; } }

        /// <summary>The frame the schedule comes due. Only meaningful while <see cref="State"/> is Scheduled.</summary>
        public static uint DueFrame { get { return _dueFrame; } }

        /// <summary>Whether a wave has already been raised for that earthquake. **Needed to choose the panel's wording.**</summary>
        public static bool HasRaisedFor(ushort quakeId)
        {
            return quakeId != 0 && _raisedQuakeId == quakeId;
        }

        /// <summary>
        /// Whether we <b>still owe this earthquake a tsunami</b>.
        ///
        /// ★★ **This is the check that refuses a second trench quake.**
        ///   The reason is "only one tsunami can be tracked", not the fact that the
        ///   earthquake's slot lives for 17-35 real minutes (see the ★★ in
        ///   <c>TrenchQuakeSlot.RaiseCore</c>).
        ///
        /// ★★ <b>"Is it scheduled or is a wave out?" is not enough.</b>
        ///   (2026-08-30, Codex P1) While the earthquake is Emerging we have not picked it
        ///   up yet, so the state is Idle. Let a second one through there and
        ///   <b>the first one's marker is stolen and the second is never picked up
        ///   either</b>. So we count it as owed <b>until it is settled (Raised / NoSea /
        ///   NoDlc / Failed)</b>.
        ///
        /// ★ Once the disaster slot is freed, <c>IsTrenchQuake</c> forgets it, so this
        ///   lock can never outlive the earthquake.
        /// </summary>
        public static bool StillOwes(ushort quakeId)
        {
            if (quakeId == 0) return false;
            if (TsunamiRing.Running) return true;

            // Not picked up yet (mid-Emerging, say). We owe it.
            if (_quakeId != quakeId) return true;

            return _state == TsunamiChainState.Idle
                   || _state == TsunamiChainState.Scheduled;
        }

        /// <summary>
        /// **Call this when a new trench earthquake starts (sim thread).**
        /// It drops whatever we were tracking so that the next tick can pick up afresh.
        ///
        /// ★★ Without it, while the previous earthquake is still alive in Clearing,
        ///   <c>PickCandidate</c> <b>keeps tracking the old one</b> and the new trench
        ///   quake's Emerging→Active moment is missed, so <b>its tsunami is dropped</b>
        ///   (2026-08-30, Codex P1). The previous tsunami has already been delivered
        ///   (<see cref="StillOwes"/> guarantees that), so it is safe to drop.
        /// </summary>
        public static void Retarget()
        {
            Forget();
        }

        /// <summary>The earthquake being tracked (an index into the disaster buffer). 0 means none.</summary>
        public static ushort QuakeId { get { return _quakeId; } }

        /// <summary>
        /// **Always call this on level unload.** A schedule never survives from one city
        /// to the next (it is session state and is not put in the save either).
        /// </summary>
        public static void Reset()
        {
            Forget();
            // ★ _errorLogged is not reset (layer-2 review M4).
            //    "This path throws" is a fact about the game build this DLL is
            //    referencing, not per-city state, so changing city changes nothing.
            //    The precedents are EarthquakeReader._readErrorLogged and
            //    LongPeriodDamage._errorLogged, neither of which resets on level unload.
            //    Resetting it here alone was an oversight, and it left two opposite
            //    answers to the same question written down as documentation.
        }

        /// <summary>
        /// Stops tracking and folds the display away. <see cref="_errorLogged"/> is left
        /// alone — it is **a fact about the game build**, there to stop the same exception
        /// burying output_log, and winding it back every time an earthquake ends would
        /// let <c>Log.Error</c> fire over and over.
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _raisedQuakeId = 0;
            _havePhase = false;
            _lastPhase = EarthquakePhase.Unknown;
            _dueFrame = 0u;
            _state = TsunamiChainState.Idle;
        }

        /// <summary>
        /// For the assumption checks. Returns **without side effects** whether a
        /// <c>TsunamiAI</c> prefab exists, and nothing else. It does not delegate to a
        /// function that writes a cache (the same reason as
        /// <c>FireWhirlSpawner.HasTornadoPrefab</c>, which used to wind back the sim
        /// thread's cache from the main thread).
        /// </summary>
        public static bool HasTsunamiPrefab()
        {
            return FindTsunamiInfo() != null;
        }

        /// <summary>
        /// Sim thread. **Always call it below the pause guard in
        /// <c>EarthquakeFeature.OnSimulationTick</c>** (let the schedule advance while
        /// paused and the tsunami arrives during game time that is supposed to be
        /// stopped). When the setting is off, the caller does not call it.
        /// </summary>
        public static void Tick(EarthquakeSnapshot snapshot, uint frame)
        {
            if (snapshot == null || !snapshot.Valid) return;

            try
            {
                Step(snapshot, frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("tsunami chain failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqTsunami", "tsunami chain failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, uint frame)
        {
            // ★★ Confirm by seed as well that the earthquake being tracked <b>really is
            //    the same one</b> (2026-08-30, fifth round of verification). Disaster
            //    numbers are reused, so tracking by number alone <b>risks attaching the
            //    tsunami to a different earthquake</b>. This only applies while tracking
            //    a trench quake (vanilla earthquakes are never picked up anyway).
            if (_quakeId != 0 && _quakeId == TrenchQuakeSlot.LastId
                && !TrenchQuakeSlot.IsTrenchQuake(_quakeId))
            {
                Forget();
            }

            var quake = FindTracked(snapshot);
            if (quake == null)
            {
                // What we were tracking is gone (Finished, or removed from the buffer).
                // Fold the displayed result away here too — going on saying "tsunami in
                // 30 minutes" about an earthquake that has ended is worse.
                if (_quakeId != 0) Forget();
                quake = PickCandidate(snapshot);
                if (quake == null) return;

                _quakeId = quake.DisasterId;
                _lastPhase = quake.Phase;
                _havePhase = true;
                _state = TsunamiChainState.Idle;
                return;
            }

            if (_state == TsunamiChainState.Scheduled)
            {
                // A uint wrap-around (about 4,739 years) does not happen in game, but
                // write it as a comparison rather than a subtraction anyway.
                if (frame >= _dueFrame) Raise(quake);
                _lastPhase = quake.Phase;
                return;
            }

            if (_state == TsunamiChainState.Idle
                && _havePhase
                && _lastPhase == EarthquakePhase.Emerging
                && quake.Phase == EarthquakePhase.Active)
            {
                Schedule(quake, frame);
            }

            _lastPhase = quake.Phase;
            _havePhase = true;
        }

        /// <summary>Looks the tracked earthquake up in the snapshot. Null once its phase has ended.</summary>
        private static EarthquakeReading FindTracked(EarthquakeSnapshot snapshot)
        {
            if (_quakeId == 0) return null;

            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].DisasterId != _quakeId) continue;
                var q = quakes[i];
                bool alive = q.Phase == EarthquakePhase.Emerging
                             || q.Phase == EarthquakePhase.Active
                             || q.Phase == EarthquakePhase.Clearing;
                return alive ? q : null;
            }
            return null;
        }

        /// <summary>
        /// Picks one new earthquake to track. **Only ones in Emerging** — without
        /// observing the moment of the main shock (Emerging → Active) there is no
        /// starting point for the chain. Never assume "the main shock is now" about an
        /// earthquake we started watching part way through.
        /// </summary>
        private static EarthquakeReading PickCandidate(EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].Phase != EarthquakePhase.Emerging) continue;

                // ★★ **Only trench earthquakes get a tsunami** (2026-08-22, the owner's
                //    instruction).
                //
                //    > Do not raise a tsunami for vanilla earthquakes; raise one only for
                //    > the new trench earthquake (with a new icon too).
                //
                //    They are told apart by <b>disaster ID</b>
                //    (<c>TrenchQuakeSlot.IsTrenchQuake</c>).
                //    ★ **Never decide it from whether the hypocentre is over water.** The
                //      player can place an earthquake out at sea from vanilla's disaster
                //      panel, and that was placed as a fault quake. Go by position and
                //      those get a tsunami too.
                if (!TrenchQuakeSlot.IsTrenchQuake(quakes[i].DisasterId)) continue;

                return quakes[i];
            }
            return null;
        }

        /// <summary>
        /// The moment of the main shock. Schedules the delay if the hypocentre is
        /// underwater.
        ///
        /// For an inland hypocentre it stays <see cref="TsunamiChainState.Idle"/> and
        /// shows nothing — "it was on land, so there is no tsunami" is self-evident, and
        /// declaring it for every earthquake buries the thing that really does need
        /// saying (it was at sea, and yet there is no sea).
        /// </summary>
        private static void Schedule(EarthquakeReading quake, uint frame)
        {
            // The authority on the DLC is whether the prefab exists (§B-5). ModCompat is
            // only a pre-check for whether to show the UI.
            if (FindTsunamiInfo() == null)
            {
                _state = TsunamiChainState.NoDlc;
                Log.Info("tsunami NOT raised: the Natural Disasters DLC is not owned, "
                         + "so there is no TsunamiAI prefab to name the disaster after");
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiNoDlc",
                    "no TsunamiAI DisasterInfo found; the Natural Disasters DLC is required "
                    + "for the tsunami chain");
                return;
            }

            if (!IsUnderWater(quake.Epicentre)) return;

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            // The value in the .cgs is a public contract so it is not discarded, but a
            // negative value cast to uint becomes an enormous frame count and the
            // schedule effectively never comes due. The slider constrains the range to
            // 5-120, but this must not fall apart for a hand-edited settings file either.
            // ★★ **A trench earthquake's tsunami is "soon".** (2026-08-25, the owner's
            //    instruction)
            //
            //    <c>eqTsunamiDelay</c> defaulted to 30 game minutes. That value was
            //    chosen with "how long a distant tsunami takes to arrive" in mind, but a
            //    trench quake happens <b>just offshore</b>, so in reality the first wave
            //    arrives within minutes.
            //    For anyone who has not touched the setting we use
            //    <see cref="TrenchDelayMinutes"/>.
            int minutes = ModSettings.EarthquakeTsunamiDelayMinutes.value;
            if (TrenchQuakeSlot.IsTrenchQuake(quake.DisasterId)
                && minutes > TrenchDelayMinutes)
            {
                minutes = TrenchDelayMinutes;
            }
            if (minutes < 0) minutes = 0;

            uint delay = (uint)(minutes * framesPerMinute);
            uint baseFrame = quake.ActivationScheduled ? quake.ActivationFrame : frame;
            _dueFrame = baseFrame + delay;
            // ★★ Do not schedule for an earthquake that is too weak (see MinIntensity's doc).
            if (quake.Intensity < MinIntensity)
            {
                _state = TsunamiChainState.NoSea;
                TsunamiRing.NoteRefusal(TsunamiRing.Refusal.TooWeak,
                    "the earthquake is too weak to raise a wave anyone would see");
                Log.Info("tsunami NOT scheduled for quake #" + quake.DisasterId
                         + ": intensity " + quake.Intensity + " is below " + MinIntensity
                         + ", which would raise a wave too small to see. This is not a "
                         + "fault - a weak quake makes a weak sea.");
                return;
            }

            _state = TsunamiChainState.Scheduled;

            // ★★ **For a trench quake, always report it.** (third round of verification)
            //    With Diag alone, LogChannel.DefaultMask is only General, so by default
            //    "when is the wave coming?" is recorded nowhere at all.
            Log.Info("tsunami scheduled for quake #" + quake.DisasterId + " at frame "
                     + _dueFrame + " (" + minutes
                     + " in-game minutes from now). After that the source runs for "
                     + TsunamiRing.TotalSteps + " water steps = "
                     + (TsunamiRing.TotalSteps * 64 / 3600f).ToString("F0")
                     + " real minutes, and the wave then needs about as long again to "
                     + "cross the map. **It is a slow disaster by design - do not call it "
                     + "broken until the sea watch lines in the log stop moving.**");
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiSchedule",
                "undersea quake #" + quake.DisasterId + "; tsunami scheduled for frame "
                + _dueFrame);
        }

        /// <summary>
        /// Whether the hypocentre is underwater. <c>TerrainManager.HasWater(Vector2)</c>
        /// is a public instance method whose argument is a **world XZ position**
        /// (measured in the IL as part of this task):
        ///
        /// ```
        /// x = FloorToInt((position.x + 8640) * 16) >> 8   // = a 16 m cell, clamped to [0,1080]
        /// z = FloorToInt((position.y + 8640) * 16) >> 8
        /// Read the four corner cells from WaterSimulation.BeginRead()'s array; if every
        /// m_height is 0, return false (there is no water at all).
        /// Otherwise bilinearly interpolate the water surface and the terrain height, and
        /// return true if the difference is at least 8 (i.e. 0.125 m in units of 1/64 m).
        /// ```
        ///
        /// It takes <c>BeginRead()</c> / <c>EndRead()</c>, so **call it from the sim thread**.
        /// </summary>
        private static bool IsUnderWater(DisasterPlus.Core.Common.Vec3 epicentre)
        {
            if (!Singleton<TerrainManager>.exists) return false;
            // The same conversion as VectorUtils.XZ(Vector3) (x, z). Written out directly
            // to avoid pulling in one more type.
            return Singleton<TerrainManager>.instance.HasWater(
                new Vector2(epicentre.X, epicentre.Z));
        }

        /// <summary>
        /// Raises the tsunami. The procedure is copied straight from IL_0071-01E8 of
        /// <c>DisasterTool.&lt;CreateDisaster&gt;c__Iterator0.MoveNext</c> (§A-1).
        /// </summary>
        private static void Raise(EarthquakeReading quake)
        {
            // ★★ **The DLC's tsunami (TsunamiAI) is no longer used.** (2026-08-25, the
            //    owner's instruction)
            //
            //    > Let's stop using the DLC tsunami. Instead, raise a sustained rise in
            //    > sea level over a region centred near the trench earthquake's
            //    > hypocentre (2-3 successive hills centred on the hypocentre: the real
            //    > tsunami mechanism).
            //
            //    <c>TsunamiAI</c> <b>cannot emit a wave from the hypocentre</b> —
            //    <c>FindSea</c> only considers cells on the map's border, and
            //    <c>m_targetPosition</c> is overwritten at start with the origin cell's
            //    position (§B-3, IL_03D3-0426). So "a wave spreading out in circles from
            //    an offshore hypocentre" was impossible in principle.
            //
            //    Now <c>TsunamiWave</c> places one <c>TYPE_IMPACT</c> water wave at the
            //    hypocentre and rewrites its force every tick using
            //    <c>Core.Earthquake.TsunamiSource</c>'s formula. IMPACT can be placed
            //    anywhere on the map and <b>adds a slope to the water surface as though
            //    there were a hill of water there</b> — the same force as the sea floor
            //    rising (measured in the IL,
            //    docs/superpowers/specs/2026-08-29-tsunami-il-facts.md).
            //    **The concentric wall of water is then built by the game's own
            //    shallow-water solver** — exactly the same path as vanilla's tsunami wall.
            uint frame = 0u;
            if (Singleton<SimulationManager>.exists)
            {
                frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            }

            // ★ Where the note above talks about <c>TYPE_IMPACT</c>, it is out of date.
            //   What is raised now is <see cref="TsunamiRing"/> (a WaterSource placed at
            //   the hypocentre).
            if (!TsunamiRing.Begin(quake.Epicentre, quake.Intensity, frame))
            {
                // ★ There is no sea, or the water simulation could not be read. **It is
                //   not always a failure**, so carry the reason back as it is
                //   (TsunamiRing.Detail).
                _state = TsunamiChainState.NoSea;
                Log.Info("tsunami NOT raised: "
                         + (TsunamiRing.Detail ?? "the wave could not be raised"));
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiNoSea",
                    TsunamiRing.Detail ?? "the tsunami could not be raised");
                return;
            }

            _state = TsunamiChainState.Raised;
            _raisedQuakeId = quake.DisasterId;
        }

        /// <summary>
        /// The disaster prefab that has a <c>TsunamiAI</c>. **Never cached.**
        /// <c>FindDisasterInfo&lt;T&gt;</c> is a public static scan that does nothing but
        /// walk <c>PrefabCollection</c> (§B-5), and it is only called by the assumption
        /// checks and at the moment a tsunami is raised. Keeping a cache would mean
        /// sharing it with the main thread's assumption checks.
        /// </summary>
        private static DisasterInfo FindTsunamiInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<TsunamiAI>();
            }
            catch
            {
                // A failure in the prefab scan must not take the feature down with it
                // (a broken mod's DisasterInfo, for instance).
                return null;
            }
        }
    }
}
