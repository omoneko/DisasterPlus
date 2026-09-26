using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④'s logical object proper — **the typhoon model** (track, intensity, phase,
    /// landfall forecast) and the management of its lifetime. <b>Sim thread only.</b>
    ///
    /// Touching vanilla's disaster buffer is <see cref="TyphoonSlot"/>'s job, and
    /// **there is not one call to <c>DisasterManager</c> or <c>DisasterAI</c> in this
    /// file** (it merely takes the <see cref="DisasterData"/> array from
    /// <see cref="TyphoonSlot"/> and hands it back). Why they are separate is in that
    /// class's doc.
    ///
    /// ── Why ④ moves it itself ────────────────────────────────────
    ///
    /// <c>DisasterManager.SimulationStepImpl</c> advances only one disaster per call via
    /// <c>idx = m_currentFrameIndex &amp; 255</c>, so **disaster i's
    /// <c>SimulationStep</c> only runs once per 256 sim frames** (IL facts document
    /// §E-2). To move it smoothly, ④ has no option but to write the coordinates every tick
    /// from <c>OnAfterSimulationTick</c>.
    ///
    /// ── The grounds for writing <c>m_targetPosition</c> (re-measured for T3) ──
    ///
    /// Scanning every assembly for <c>stfld DisasterData::m_targetPosition</c> found 19
    /// methods that write it, but **not one of them writes it for a disaster that already
    /// exists**. They are all either "put an initial position into the slot we just made,
    /// immediately after <c>CreateDisaster</c>", or the save's <c>Deserialize</c>, or the
    /// player grabbing it with the move tool (<c>DefaultTool+&lt;EndMoving&gt;</c>). The
    /// <c>ThunderStormAI</c> side writes it neither in <c>SimulationStep</c> nor in
    /// <c>ActivateDisaster</c>.
    /// **In other words we are not fighting vanilla while Active.** What follows the
    /// position is the lightning scatter centre, the hazard disc, the disaster marker and
    /// <c>FindDisaster(Vector3)</c> (§E-1).
    ///
    /// ── Only one at a time ───────────────────────────────────────
    ///
    /// If one is already running, <see cref="TyphoonRequest.Start"/> is ignored and the
    /// reason left in <see cref="LastRefusal"/>. The same restriction as ②'s
    /// <see cref="TsunamiChain"/> / <c>LongPeriodDamage</c>, so that ④ cannot build a
    /// shape that eats through the 256-slot disaster cap.
    ///
    /// ── The true centre and the clamped copy ──────────────────────────────
    ///
    /// **④ keeps "the true centre" itself, and <see cref="TyphoonSlot.WriteTarget"/>
    /// writes a clamped copy into the disaster.** Always make the ending decision on the
    /// centre before clamping (the reason is in that class's doc).
    /// </summary>
    public static class TyphoonController
    {
        /// <summary>The floor on the intensity. Both the <c>.cgs</c> value and the tile
        /// slider's value are a public contract, so we do not discard out-of-range values
        /// but lift them here, at the point of use (treated the same way as ②'s
        /// <c>EarthquakeLongPeriodStrength</c>). The range itself belongs to Core's
        /// <see cref="TyphoonIntensity"/> and is pinned by tests.</summary>
        private const int MinIntensity = TyphoonIntensity.MinPeak;

        private const int MaxIntensity = TyphoonIntensity.MaxPeak;

        /// <summary>The ceiling on the number of look-ahead samples for the landfall
        /// forecast (plan §3.6).</summary>
        private const int LandfallSamples = 64;

        /// <summary>
        /// The sample interval for the landfall forecast (frames).
        ///
        /// ★ **This is the granularity of the forecast.** 256 frames is about 5.6 in-game
        ///   minutes, so <see cref="MinutesToLandfall"/>'s actual resolution is "about
        ///   5.6 minutes", not "0.1 minutes". The value itself decreases stably and
        ///   monotonically (the track is a closed-form expression), but **we do not claim a
        ///   precision in the display that we do not have** — the display rounds to F0 and
        ///   names the step through <see cref="LandfallStepMinutesText"/> (whole-project
        ///   review).
        /// </summary>
        private const uint LandfallStepFrames = 256u;

        /// <summary>
        /// The interval at which the landfall forecast is recomputed (frames).
        ///
        /// **The track is a closed-form function of elapsedFrames**
        /// (<see cref="TyphoonTrack.CentreAt"/>), so the predicted landfall "frame" does
        /// not change as ticks pass. Recomputing every tick would just mean taking
        /// <c>HasWater</c> 64 times over (i.e. taking the water simulation's lock 64 times)
        /// to get the same answer. The "how many minutes" we display is computed every tick
        /// from the difference between the cached landfall frame and the current frame, so
        /// the value still decreases smoothly, one frame at a time.
        /// </summary>
        private const uint LandfallRescanFrames = 256u;

        private static bool _active;

        /// <summary>
        /// **The point the player pointed at** (world XZ, before clamping).
        /// The track starts here (<c>TyphoonTrack.CentreAt</c>).
        /// </summary>
        private static Vec2 _origin;

        private static uint _seed;
        private static float _speed;
        private static byte _peakIntensity;
        private static uint _totalFrames;

        /// <summary>
        /// How many frames it takes to reach the clicked point.
        /// **The typhoon comes in from where it would have been this far back on the
        /// clock** (<c>TyphoonTrack.ApproachFramesFor</c>).
        /// </summary>
        private static uint _approachFrames;
        private static uint _elapsedFrames;
        private static uint _lastFrame;
        private static float _decay;
        private static float _prefabRadius;

        /// <summary>The true centre, before clamping. It can be off the map.</summary>
        private static Vec2 _centre;
        private static float _centreHeight;
        private static float _heading;
        private static byte _intensity;
        private static float _stormRadius;
        private static float _galeRadius;
        private static TyphoonPhase _phase;
        private static bool _overLand;

        /// <summary>Whether it has been inside the map even once. Used for the ending
        /// decision (<see cref="Advance"/>).</summary>
        private static bool _wasInsideMap;

        private static bool _landfallKnown;
        private static uint _landfallFrame;
        private static bool _landfallScanned;
        private static uint _landfallScanFrame;

        private static string _lastRefusal;

        /// <summary>Whether an exception has been reported once through <c>Log.Error</c>.
        /// Not reset by <see cref="Reset"/> (it is a fact about the game build, not
        /// per-city state).</summary>
        private static bool _errorLogged;

        public static bool Active { get { return _active; } }

        public static ushort DisasterId { get { return TyphoonSlot.Id; } }

        /// <summary>
        /// The activation frame <c>StartDisaster</c> scheduled.
        /// **Also the origin from which the lightning budget estimates vanilla's share**
        /// (T6).
        /// </summary>
        public static uint ActivationFrame { get { return TyphoonSlot.ActivationFrame; } }

        /// <summary>
        /// The true centre (before clamping). Y is a sample of the terrain height; off the
        /// map the terrain grid clamps at its edge, so it becomes "the height of the
        /// nearest edge".
        /// </summary>
        public static Vec3 Centre { get { return new Vec3(_centre.X, _centreHeight, _centre.Z); } }

        public static float HeadingRadians { get { return _heading; } }

        public static byte Intensity { get { return _intensity; } }

        public static float StormRadius { get { return _stormRadius; } }

        public static float GaleRadius { get { return _galeRadius; } }

        /// <summary>
        /// The <c>ThunderStormAI.m_radius</c> this typhoon is using (the measured prefab
        /// value). **0 means "could not be read"** (design doc §6. A typhoon cannot be
        /// raised at all when <c>TyphoonPrefabFacts.Usable</c> is false, so while Active
        /// this is always positive).
        ///
        /// <see cref="StormRadius"/> / <see cref="GaleRadius"/> are the **results** at the
        /// current intensity; this is the **input** passed to
        /// <c>TyphoonProfile.WindAt</c> / <c>StormRadiusOf</c>.
        /// T7's wind damage needs it to work out the wind speed equivalent at each distance
        /// — dividing back out of the result would divide by zero at intensity 0.
        /// </summary>
        public static float PrefabRadius { get { return _prefabRadius; } }

        public static TyphoonPhase Phase { get { return _phase; } }

        public static uint ElapsedFrames { get { return _elapsedFrames; } }

        public static uint TotalFrames { get { return _totalFrames; } }

        /// <summary>
        /// The complete set of values needed to draw the track onwards
        /// (<see cref="TyphoonTrackPlan"/>).
        ///
        /// ★★ **The rendering side (main thread) must not read this directly.**
        ///   It travels on the snapshot. They are gathered into one struct because
        ///   carrying the four separately allows <b>a combination where just one of them is
        ///   stale</b>, and a track drawn from that combination is the track of a typhoon
        ///   that does not exist.
        /// </summary>
        public static TyphoonTrackPlan TrackPlan
        {
            get
            {
                return new TyphoonTrackPlan(_origin, _seed, _speed,
                                            _approachFrames, _totalFrames);
            }
        }

        public static bool OverLand { get { return _overLand; } }

        /// <summary>Whether a landfall forecast exists. **false means "it will pass out at
        /// sea (or there is no land within the look-ahead)", not "in 0 minutes".** Do not
        /// mix it up with 0.</summary>
        public static bool LandfallKnown { get { return _landfallKnown; } }

        /// <summary>In-game minutes to landfall. Only meaningful when
        /// <see cref="LandfallKnown"/> is true.
        /// **The resolution is in steps of <see cref="LandfallStepFrames"/>** (its
        /// doc).</summary>
        public static float MinutesToLandfall { get; private set; }

        /// <summary>
        /// The landfall forecast's step (in-game minutes) in human-readable form. The
        /// display side uses it to say "this number is only computed at about this
        /// granularity".
        /// If <c>FramesPerMinute</c> cannot be read it names the frame count as it is
        /// (it does not convert into guessed minutes).
        /// </summary>
        public static string LandfallStepMinutesText()
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return LandfallStepFrames + " frames";
            return (LandfallStepFrames / framesPerMinute).ToString("F1") + " in-game minutes";
        }

        /// <summary>
        /// The most recent reason a typhoon could not be raised, or was let go of (English,
        /// for diagnostics).
        /// **This is the only means of telling "it could not be raised" from "nothing is
        /// happening".**
        /// </summary>
        public static string LastRefusal { get { return _lastRefusal; } }

        /// <summary>
        /// **Always call on level unload.** Neither a typhoon in progress nor a booking
        /// survives across cities. It does not touch the slot — the previous city's
        /// disaster buffer no longer exists.
        /// </summary>
        public static void Reset()
        {
            Forget();
            _lastRefusal = null;
            // ★ _errorLogged is not reset (class doc).
        }

        /// <summary>
        /// Sim thread. **Always call it from below the pause guard in
        /// <c>TyphoonFeature.OnSimulationTick</c>** (otherwise the typhoon moves while
        /// paused).
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step(snapshot, frame, deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon controller failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyCtl",
                             "typhoon controller failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            // ★ Exactly once per tick (the doc on TyphoonHub.TakeRequest).
            var request = TyphoonHub.TakeRequest();

            if (request.Kind == TyphoonRequest.Start)
            {
                // ★ The request **carries the point and the intensity**. Do not invent
                //   either of them here (re-read the intensity from the settings screen and
                //   it "starts at a different intensity from the one you pressed").
                Start(snapshot, frame, request.Point.ToVec2(), request.Intensity);
            }

            if (!_active) return;

            DisasterData[] buffer;
            string lostReason;
            if (!TyphoonSlot.TryGetBuffer(out buffer, out lostReason))
            {
                LoseSlot(lostReason);
                return;
            }

            Advance(buffer, frame, deltaMinutes);
        }

        /// <summary>
        /// Raise a typhoon. Exactly the procedure in
        /// <c>DisasterTool.&lt;CreateDisaster&gt;c__Iterator0.MoveNext</c> (§E-3), and the
        /// same shape as ②'s <c>TsunamiChain.Raise</c>.
        /// Taking and starting the slot belongs to <see cref="TyphoonSlot"/>; this only
        /// does **the decision about whether we may raise one, and the initialisation of
        /// ④'s model**.
        /// </summary>
        private static void Start(TyphoonSnapshot snapshot, uint frame, Vec2 origin,
                                  int requestedIntensity)
        {
            if (_active)
            {
                // Only one at a time (class doc). Do not ignore it silently.
                _lastRefusal = "a typhoon is already running; only one at a time";
                return;
            }

            if (snapshot == null || !snapshot.Valid)
            {
                _lastRefusal = "the simulation could not be read this tick";
                return;
            }

            var prefab = snapshot.Prefab;
            if (!prefab.Usable)
            {
                // Design doc §6: if it cannot be read, do nothing rather than guess.
                _lastRefusal = "ThunderStormAI prefab values are unreadable "
                             + "(m_radius / m_activeDuration); the mod will not guess them";
                return;
            }

            // ★★ **The lifetime is <c>LifetimeMultiplier</c> times the host storm's
            //    duration** (2026-08-22, in-game report "the effects disappear almost
            //    immediately / please reproduce it moving slowly").
            //    The host is kept alive by <c>TyphoonSlot.KeepAlive</c>.
            //    The speed divides the track length by this lifetime, so **extending the
            //    lifetime automatically makes it slower**.
            uint lifetime = TyphoonTrack.LifetimeFramesFor(prefab.ActiveDuration);
            float speed = TyphoonTrack.SpeedFor(lifetime);
            if (speed <= 0f)
            {
                _lastRefusal = "travel speed is unknown (m_activeDuration is 0)";
                return;
            }

            string refusal;
            if (!TyphoonSlot.Create(out refusal))
            {
                _lastRefusal = refusal;
                return;
            }

            // The seed is the disaster ID. **Point at the same spot in the same save and
            // you get the same track** (design doc §4.1).
            // The seed decides only the heading and the curvature; the starting point is
            // the player's.
            _seed = TyphoonSlot.Id;
            _origin = origin;
            _speed = speed;
            // ★ The intensity is **the value the tile's slider was pointing at** (what the
            //   request carried). The settings screen's value is the fallback for
            //   environments where the slider cannot be read, and that substitution has
            //   already been done on the placement tool side
            //   (TyphoonPlacementTool.OnToolUpdate).
            _peakIntensity = ClampIntensity(requestedIntensity);
            _totalFrames = lifetime;
            _prefabRadius = prefab.StormRadius;
            _elapsedFrames = 0u;
            _lastFrame = frame;
            _decay = 0f;
            _wasInsideMap = false;
            _landfallKnown = false;
            _landfallScanned = false;
            _landfallScanFrame = 0u;
            MinutesToLandfall = 0f;

            // ★★ **It comes in from the edge of the map** (2026-09-02).
            //    The clicked point is not the start but <b>the destination</b>.
            _approachFrames = TyphoonTrack.ApproachFramesFor(_origin, _seed, _speed,
                                                            _totalFrames);
            _centre = TyphoonTrack.CentreAt(_origin, _seed, 0u, _speed, _approachFrames);
            _heading = TyphoonTrack.HeadingAt(_origin, _seed, 0u, _speed, _approachFrames);
            _phase = TyphoonTrack.PhaseAt(0u, _totalFrames);
            _intensity = TyphoonTrack.IntensityAt(_peakIntensity, 0u, _totalFrames, 0f);
            _stormRadius = TyphoonProfile.StormRadiusOf(_intensity, _prefabRadius);
            _galeRadius = TyphoonProfile.GaleRadiusOf(_intensity, _prefabRadius);

            var pos = new Vector3(_centre.X, 0f, _centre.Z);
            if (!TyphoonSlot.Begin(ref pos, _heading, _intensity, out refusal))
            {
                _lastRefusal = refusal;
                Forget();
                return;
            }

            _centreHeight = pos.y;
            _active = true;
            _lastRefusal = null;

            // ★ **Write two intensities.** We used to print only the ramp's current value
            //   as "intensity=", and at frame 0 that is necessarily small — the first
            //   in-game test printed "intensity=0" and it was read as "the slider is not
            //   being read".
            //   **Name the chosen value (peak) and the current value separately.**
            Log.Info("typhoon started: disaster #" + TyphoonSlot.Id
                     + " at (" + _origin.X.ToString("F0") + "," + _origin.Z.ToString("F0") + ")"
                     + " peakIntensity=" + _peakIntensity
                     + " (requested " + requestedIntensity + ")"
                     + " rampIntensity=" + _intensity
                     + " activationFrame=" + TyphoonSlot.ActivationFrame
                     + " speed=" + _speed.ToString("F3") + " m/frame"
                     + " duration=" + _totalFrames + " frames");
        }

        /// <summary>
        /// **Do not let go silently.** Print one line with the reason and call
        /// <see cref="Forget"/>.
        /// Not <see cref="Stop"/> — **it is not ④'s any more, so we must not touch it.**
        /// </summary>
        private static void LoseSlot(string reason)
        {
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLost",
                     "typhoon released its disaster slot: " + reason);
            Forget();
            _lastRefusal = reason;
        }

        /// <summary>The per-tick body (plan §3.3).</summary>
        private static void Advance(DisasterData[] buffer, uint frame, float deltaMinutes)
        {
            if (frame > _lastFrame) _elapsedFrames += frame - _lastFrame;
            _lastFrame = frame;

            // The track is a closed-form function of elapsedFrames. Do not accumulate
            // (Core's doc).
            _centre = TyphoonTrack.CentreAt(_origin, _seed, _elapsedFrames, _speed,
                                           _approachFrames);
            // ★ Pass the starting point. **So that a direction across the map is chosen**
            //   (TyphoonTrack.BearingFrom's class doc). Without it, point at an edge and
            //   the typhoon leaves the map and disappears after a few hundred metres.
            _heading = TyphoonTrack.HeadingAt(_origin, _seed, _elapsedFrames, _speed,
                                              _approachFrames);

            bool inside = TyphoonTrack.IsInsideMap(_centre);
            if (inside) _wasInsideMap = true;

            _overLand = inside && !HasWaterAt(_centre);
            _decay = TyphoonTrack.DecayAfter(_decay, _overLand, deltaMinutes);
            _intensity = TyphoonTrack.IntensityAt(_peakIntensity, _elapsedFrames,
                                                  _totalFrames, _decay);
            _stormRadius = TyphoonProfile.StormRadiusOf(_intensity, _prefabRadius);
            _galeRadius = TyphoonProfile.GaleRadiusOf(_intensity, _prefabRadius);
            _phase = TyphoonTrack.PhaseAt(_elapsedFrames, _totalFrames);

            var pos = new Vector3(_centre.X, 0f, _centre.Z);
            TyphoonSlot.WriteTarget(buffer, ref pos, _heading, _intensity);
            _centreHeight = pos.y;

            UpdateLandfall(frame);

            // ── The ending decision. **Always made on the centre before clamping** (class doc). ──
            //
            // Plan §3.5 writes the first condition as "outside the map, and elapsed has
            // passed the number of frames needed to come in". Here we decide the same thing
            // **by observation rather than estimation**, from "has it ever been inside"
            // (_wasInsideMap). Estimate the frames needed to come in separately and, when
            // that estimate is wrong, you get "the typhoon ends before it arrives" — a
            // breakage with no exception.
            if (_wasInsideMap && !inside)
            {
                Log.Info("typhoon #" + TyphoonSlot.Id + " left the map");
                Stop();
                return;
            }

            if (_phase == TyphoonPhase.Gone)
            {
                Log.Info("typhoon #" + TyphoonSlot.Id + " used up its lifetime ("
                         + _totalFrames + " frames)");
                Stop();
            }
        }

        /// <summary>
        /// The landfall forecast (design doc §7.2). The track is a closed-form expression,
        /// so looking ahead is simply sampling.
        ///
        /// <c>HasWater</c> takes the water simulation's lock, so **recomputation is held to
        /// once per <see cref="LandfallRescanFrames"/> frames with a ceiling of
        /// <see cref="LandfallSamples"/> samples** (the docs on those constants).
        /// </summary>
        private static void UpdateLandfall(uint frame)
        {
            if (_overLand)
            {
                _landfallKnown = true;
                MinutesToLandfall = 0f;
                _landfallFrame = frame;
                _landfallScanned = false;
                return;
            }

            if (!_landfallScanned || frame - _landfallScanFrame >= LandfallRescanFrames)
            {
                _landfallScanFrame = frame;
                _landfallScanned = true;
                ScanLandfall(frame);
            }

            if (!_landfallKnown) return;

            if (_landfallFrame <= frame)
            {
                // The predicted time has passed and we are still over water. Take it again
                // on the next scan.
                _landfallKnown = false;
                MinutesToLandfall = 0f;
                return;
            }

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f)
            {
                _landfallKnown = false;
                MinutesToLandfall = 0f;
                return;
            }

            MinutesToLandfall = (_landfallFrame - frame) / framesPerMinute;
        }

        private static void ScanLandfall(uint frame)
        {
            _landfallKnown = false;

            for (int k = 1; k <= LandfallSamples; k++)
            {
                uint ahead = (uint)k * LandfallStepFrames;
                uint elapsed = _elapsedFrames + ahead;
                if (elapsed > _totalFrames) return;

                var c = TyphoonTrack.CentreAt(_origin, _seed, elapsed, _speed);
                if (!TyphoonTrack.IsInsideMap(c)) continue;
                if (HasWaterAt(c)) continue;

                _landfallKnown = true;
                _landfallFrame = frame + ahead;
                return;
            }
        }

        /// <summary>
        /// Whether the centre is over water. <c>TerrainManager.HasWater(Vector2)</c> takes
        /// <c>WaterSimulation.BeginRead()/EndRead()</c>, so it is **sim thread only**.
        /// The argument is **world XZ** (② measured this from the IL.
        /// <see cref="TsunamiChain"/>'s doc).
        /// </summary>
        private static bool HasWaterAt(Vec2 centre)
        {
            if (!Singleton<TerrainManager>.exists) return false;
            return Singleton<TerrainManager>.instance.HasWater(new Vector2(centre.X, centre.Z));
        }

        /// <summary>
        /// There are two ways it can end (it left the map, or it used up its duration), but
        /// **the cleanup always goes through this one path.**
        ///
        /// ★★ <b>There is no longer any route by which the player stops it</b>
        ///   (2026-09-02, the owner: "Nobody can stop a natural disaster"). The only things
        ///   that call this are the two terminating conditions inside
        ///   <see cref="Step"/>.
        ///   **Do not make it callable from a request again.**
        /// </summary>
        private static void Stop()
        {
            // 1. Put it on vanilla's ending route (the slot is not released. That class's
            //    doc).
            TyphoonSlot.Deactivate();
            // ★ Do not return here. Even if vanilla's ending route throws, the Forget()
            //    below always puts back the weather ④ is holding.

            // 2. Let go of everything we were holding (<see cref="Forget"/> owns the
            //    restoration too).
            Forget();
        }

        /// <summary>
        /// Let go of everything ④ was holding. **It does not touch the disaster slot**
        /// (only <see cref="Stop"/> calls vanilla's ending route).
        /// <see cref="LastRefusal"/> is left as it is (the caller overwrites the reason).
        ///
        /// ★ **Restoration of any other system ④ touched goes here, not in
        /// <see cref="Stop"/>.**
        /// <see cref="Stop"/> is not the only route that lets go of a typhoon —
        /// <see cref="LoseSlot"/> (the disaster slot was reused) and
        /// <see cref="Reset"/> (level unload) go through it too. Put the restoration on the
        /// <see cref="Stop"/> side and, the moment the slot is taken, **the typhoon alone
        /// disappears while the weather stays held**.
        ///
        /// ★ **Do not add <c>TyphoonCloud</c> here.** The cloud is a main-thread-only
        /// feature and is never once called from the sim side. That is what makes T9
        /// independent of everything else (<c>TyphoonCloud</c>'s class doc). The cloud's
        /// cleanup is done by <c>TyphoonFeature.OnMainThreadUpdate</c> itself, on "the
        /// frame where it stopped being Active".
        ///
        /// Every restoration must be idempotent (they are called repeatedly, both from
        /// <see cref="Stop"/> → <see cref="Forget"/> and from
        /// <c>TyphoonFeature.OnLevelUnloading</c>).
        /// </summary>
        private static void Forget()
        {
            // ★★ **Leave exactly one line saying what we gave back** (the main route by
            //    which the owner's note "when the typhoon leaves, the storm and the tornado
            //    damage should stop" is verified in the game).
            //    Forget runs once in a typhoon's life, so Log.Info is fine — this is not a
            //    per-tick or per-frame route.
            //    Read **whether we were holding it before we give it back** (read it after
            //    and everything says "was not held", and this line proves nothing).
            bool wasActive = _active;
            ushort releasedId = TyphoonSlot.Id;
            bool hadWeather = TyphoonWeather.Driving;
            int floodTouched = TyphoonFlood.TouchedCount;
            int windTotal = TyphoonWind.TotalCollapsed;
            int gustTotal = TyphoonGust.TotalCollapsed;

            // ★ Do not leave it to vanilla's DeactivateDisaster. DisasterAI.DeactivateNow
            //    does nothing without m_flags & Active(8) (measured from the IL in T3), so
            //    for a typhoon stopped while Emerging, m_targetRain = 0 never runs.
            TyphoonWeather.Release();

            // ★ Do not carry the lightning stock over to the next typhoon. Carry it over
            //    and the next typhoon sees a queue that is actually free as "full" and never
            //    fires — and then the queue stays empty, so the ambient lightning we were
            //    supposed to be suppressing comes back.
            TyphoonLightning.Reset();

            // ★ Do not carry the wind sweep's position over to the next typhoon either.
            //    Carry it over and the next typhoon starts from an ordinal based on the
            //    previous typhoon's centre.
            TyphoonWind.Reset();

            // ★★ Always put the river water levels back (the first of the two restore
            //    routes for trap 4). **It goes here, not in Stop** — Stop is not the only
            //    route that lets go of a typhoon; LoseSlot (the disaster slot was taken) and
            //    Reset (unload) go through it too. Put it on the Stop side and, the moment
            //    the slot is taken, **the typhoon alone disappears with the rivers still
            //    burst**. RestoreAll is idempotent, so calling it repeatedly is fine.
            TyphoonFlood.RestoreAll();

            // ★★ Pack away the tornado-grade local damage (the patches) here too. **Here,
            //    not in Stop** (the same reason as the three above). A patch is not a ledger
            //    entry but "a function of the typhoon's elapsed frames", so the moment the
            //    typhoon is gone not one of them exists — all we reset here are the counters
            //    and the sweep position. Idempotent.
            TyphoonGust.Reset();

            TyphoonSlot.Forget();

            _active = false;
            _seed = 0u;
            // ★ Do not carry the pointed-at location over either. Leave it and, when a route
            //   that does not carry a location is added later, it quietly starts from
            //   **the previous typhoon's location**.
            _origin = new Vec2(0f, 0f);
            _speed = 0f;
            _peakIntensity = 0;
            _totalFrames = 0u;
            _elapsedFrames = 0u;
            _lastFrame = 0u;
            _decay = 0f;
            _prefabRadius = 0f;
            _centre = new Vec2(0f, 0f);
            _centreHeight = 0f;
            _heading = 0f;
            _intensity = 0;
            _stormRadius = 0f;
            _galeRadius = 0f;
            _phase = TyphoonPhase.Idle;
            _overLand = false;
            _wasInsideMap = false;
            _landfallKnown = false;
            _landfallFrame = 0u;
            _landfallScanned = false;
            _landfallScanFrame = 0u;
            MinutesToLandfall = 0f;

            if (wasActive)
            {
                Log.Info("typhoon #" + releasedId + " released everything it was holding:"
                         + " weather override=" + (hadWeather ? "restored" : "was not held")
                         + ", water sources restored=" + floodTouched
                         + ", lightning queue cleared"
                         + ", wind sweep stopped (" + windTotal + " collapsed in total)"
                         + ", tornado-strength patches stopped (" + gustTotal + " collapsed"
                         + " in total)."
                         + " The host ThunderStormAI disaster is deliberately left in the"
                         + " city to expire on its own (design section 4.2); it no longer"
                         + " drives rain, wind or damage.");
            }
        }

        /// <summary>
        /// Both the <c>.cgs</c> value and the slider's value are a public contract, so we do
        /// not discard out-of-range values but lift them here into
        /// <see cref="TyphoonIntensity"/>'s range.
        /// **Clamp before the cast to <c>(byte)</c>** (cast an out-of-range value first and
        /// the weakest setting becomes the strongest typhoon).
        /// </summary>
        private static byte ClampIntensity(int value)
        {
            return TyphoonIntensity.PeakOf(value);
        }
    }
}
