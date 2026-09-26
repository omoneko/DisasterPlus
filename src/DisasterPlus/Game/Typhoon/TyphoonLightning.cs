using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The typhoon's lightning. <b>Sim thread only.</b>
    ///
    /// ── This is real damage, not decoration ───────────────────────────────
    ///
    /// The strikes <c>WeatherManager.QueueLightningStrike</c> queues are **real**:
    /// <c>StrikeNow</c> triggers <c>BuildingAI.BurnBuilding</c> (a building catches fire),
    /// <c>TreeManager.BurnTree</c> (a tree catches fire) and <c>NetAI.CollapseSegment</c>
    /// (a power line comes down) (IL facts document §A-3). It is not ornament.
    ///
    /// The district policy <c>CityPlanning.LightningRods (4096)</c> removes 70% of
    /// building strikes. **④ does not compensate for that** — damage being lower in a
    /// district that put up lightning rods is the correct behaviour.
    ///
    /// A building set alight by lightning raises the disaster group's <c>m_refCount</c>
    /// and extends <c>DisasterAI.IsStillClearing</c> (the base) (§A-1 item 5).
    /// **The storm does not end until the fires are out.** This is correct behaviour and
    /// is consistent with <see cref="TyphoonSlot.Deactivate"/> not releasing the disaster
    /// slot.
    ///
    /// ── The real job is "do not hit the ceiling of 20" ────────────────────
    ///
    /// All the judgement lives in <see cref="LightningBudget"/> (Core, 8 tests). This
    /// class holds only **the ledger of what ④ fired** and the actual scattering. The
    /// ledger is a fixed-length array of <see cref="LightningBudget.QueueCapacity"/>
    /// elements, where 0 is a free slot. **Do not build a <c>List</c> every tick** — this
    /// route runs on every sim tick.
    ///
    /// An entry is dropped from the stock <b>not when it fires</b> but at
    /// <see cref="LightningBudget.HasExpired"/> (scheduled + 45 frames), because that is
    /// when the game frees the slot (§A-3).
    ///
    /// ── We came down on "keep scattering" for ambient lightning (design doc §4.2) ──
    ///
    /// When <c>m_currentRain &gt; 0.8 &amp;&amp; m_lightningQueue.m_size == 0</c>, the game
    /// queues lightning of its own (§A-3). **The condition is "the queue is empty", so if
    /// ④ has even one entry queued, ambient lightning stops completely.** ④ keeps the
    /// rain at or above 0.8 for the whole life of the typhoon
    /// (<see cref="TyphoonWeather"/>), so we come down on the side of scattering
    /// continuously — if <c>inFlight == 0</c> we always top up with one strike without
    /// waiting for the interval.
    ///
    /// **Except that we do not top up when the budget is 0.** At high intensities the host
    /// storm's share alone uses the whole budget up (from around intensity 170,
    /// <see cref="LightningBudget.Allowance"/> is 0). Forcing an entry in there means the
    /// host storm's own strikes get thrown away — and that is worse than one ambient
    /// strike coming back. **What is more, in that state the host is queueing plenty, so
    /// the queue is not actually empty.** The diagnostics show both the budget and the
    /// stock precisely so these two states can be told apart afterwards.
    ///
    /// ── Bias the strike points towards the eyewall ────────────────────────
    ///
    /// Lightning in a real typhoon happens not in the eye but in the eyewall outside it
    /// (design doc §4.2). Avoiding the inside of the eye also has the practical benefit
    /// that **you can read the eye on screen**.
    /// The only randomness used is <see cref="DeterministicRandom"/> — what we decide here
    /// is a judgement ④ invented, not a value vanilla draws
    /// (do not use <c>VanillaRandomizer</c>).
    ///
    /// Note that ambient lightning strikes at a **uniformly random point over the map**
    /// (the one-argument overload, IL_000F-004D), so do not count a bolt falling far away
    /// from the typhoon as one of ④'s.
    /// </summary>
    public static class TyphoonLightning
    {
        /// <summary>
        /// The firing interval at full strength (frames). We queue at most one per tick,
        /// so this is "the ceiling on the rate of fire". In practice
        /// <see cref="LightningBudget.Allowance"/> usually bites first.
        /// </summary>
        private const uint MinIntervalFrames = 30u;

        /// <summary>The firing interval at the weakest (frames).</summary>
        private const uint MaxIntervalFrames = 240u;

        /// <summary>Maximum intensity (<c>DisasterData.m_intensity</c> is a
        /// byte).</summary>
        private const float MaxIntensity = 255f;

        /// <summary>How far outside the eyewall to scatter (a multiplier on
        /// <c>WallFraction</c>).</summary>
        private const float WallSpread = 1.3f;

        /// <summary>How many random draws one strike uses. Advance the salt by this much
        /// and the sequences do not overlap.</summary>
        private const uint DrawsPerStrike = 5u;

        private const float TwoPi = 6.2831855f;

        /// <summary>
        /// The scheduled frames for the strikes ④ queued. 0 is a free slot.
        /// **Fixed length** (no <c>List</c> allocation on a route that runs every sim
        /// tick).
        /// </summary>
        private static readonly uint[] _scheduled = new uint[LightningBudget.QueueCapacity];

        private static int _inFlight;
        private static int _lastQueued;
        private static int _lastRejected;
        private static int _totalQueued;
        private static int _totalRejected;
        private static int _lastVanillaReserve;
        private static int _lastAllowance;
        private static uint _nextStrikeFrame;
        private static uint _salt;
        private static bool _errorLogged;

        /// <summary>
        /// Whether "dropped because the queue was full" has been reported once through
        /// <c>Log.Warn</c>. It is separate from <see cref="_errorLogged"/> because if one
        /// were raised, the other's first report would silently disappear. Neither is
        /// reset by <see cref="Reset"/> (so that no <c>Log.Warn</c> sits on an
        /// every-tick route).
        /// </summary>
        private static bool _rejectionLogged;

        /// <summary>How many ④ currently has in the queue (they drop at scheduled + 45
        /// frames).</summary>
        public static int InFlight { get { return _inFlight; } }

        /// <summary>How many were queued on the most recent tick (0 or 1).</summary>
        public static int LastQueued { get { return _lastQueued; } }

        /// <summary>How many **the game threw away** on the most recent tick. **A value
        /// that should always be 0.**</summary>
        public static int LastRejected { get { return _lastRejected; } }

        /// <summary>The cumulative count this typhoon queued.</summary>
        public static int TotalQueued { get { return _totalQueued; } }

        /// <summary>
        /// The cumulative count the game threw away during this typhoon. **Anything other
        /// than 0 means we are hitting the ceiling of 20**, i.e. the host storm's and
        /// other mods' lightning is being wiped out too (<see cref="LightningBudget"/>'s
        /// doc).
        /// </summary>
        public static int TotalRejected { get { return _totalRejected; } }

        /// <summary>The most recent estimate of the host storm's share.</summary>
        public static int LastVanillaReserve { get { return _lastVanillaReserve; } }

        /// <summary>④'s most recent share. 0 is not an anomaly (class doc).</summary>
        public static int LastAllowance { get { return _lastAllowance; } }

        /// <summary>
        /// Sim thread. Call it below the pause guard in
        /// <c>TyphoonFeature.OnSimulationTick</c>, right after
        /// <c>TyphoonWeather.Drive</c>, and only while a typhoon is running.
        ///
        /// <paramref name="snapshot"/> is not used. That holds **the previous tick's
        /// state** (the note in <see cref="TyphoonSnapshot"/>'s T3 section), so both
        /// position and intensity are read directly from <see cref="TyphoonController"/>'s
        /// statics on the same thread. It is kept as a parameter only to give every
        /// element the same call shape (treated the same way as
        /// <c>TyphoonWeather.Drive</c>).
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame)
        {
            try
            {
                Step(frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon lightning failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLightning",
                             "typhoon lightning failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(uint frame)
        {
            _lastQueued = 0;
            _lastRejected = 0;

            Expire(frame);

            _lastVanillaReserve = LightningBudget.VanillaMaxStrikes(
                LightningBudget.VanillaRampCount(frame, TyphoonController.ActivationFrame,
                                                 TyphoonController.TotalFrames,
                                                 TyphoonController.Intensity));
            _lastAllowance = LightningBudget.Allowance(_inFlight, _lastVanillaReserve);

            // ★ Never let the queue go empty (class doc). With a stock of 0 we do not wait
            //   for the interval.
            bool due = _inFlight == 0 || frame >= _nextStrikeFrame;
            if (due && _lastAllowance > 0) QueueOne(frame);

            WriteDiag(frame);
        }

        /// <summary>
        /// Drop from the ledger the entries whose slots the game has freed (§A-3,
        /// scheduled + 45 frames).
        /// </summary>
        private static void Expire(uint frame)
        {
            int inFlight = 0;
            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (_scheduled[i] == 0u) continue;
                if (LightningBudget.HasExpired(_scheduled[i], frame)) _scheduled[i] = 0u;
                else inFlight++;
            }
            _inFlight = inFlight;
        }

        private static void QueueOne(uint frame)
        {
            float stormRadius = TyphoonController.StormRadius;
            // 0 if the prefab radius could not be read. **Do not fall back to a guessed
            // radius** (design doc §6).
            if (!(stormRadius > 0f)) return;

            if (!Singleton<WeatherManager>.exists) return;

            var centre = TyphoonController.Centre;
            if (float.IsNaN(centre.X) || float.IsNaN(centre.Z)) return;

            int slot = FreeSlot();
            if (slot < 0) return;   // the ledger is full = contradicts the budget calc, so bow out quietly

            uint seed = TyphoonController.DisasterId;
            uint salt = _salt;
            _salt += DrawsPerStrike;

            // Bias the strike points towards the eyewall (the structure of a real typhoon;
            // design doc §4.2).
            float angle = DeterministicRandom.Unit(seed, salt) * TwoPi;
            float band = TyphoonProfile.EyeFraction
                       + DeterministicRandom.Unit(seed, salt + 1u)
                         * (TyphoonProfile.WallFraction * WallSpread - TyphoonProfile.EyeFraction);
            float d = stormRadius * band;

            var p = new Vector3(centre.X + Mathf.Cos(angle) * d, 0f,
                                centre.Z + Mathf.Sin(angle) * d);
            // The height lookup §A-1 shows ThunderStormAI itself using. Sim thread.
            if (Singleton<TerrainManager>.exists)
            {
                p.y = Singleton<TerrainManager>.instance
                      .SampleRawHeightSmoothWithWater(p, false, 0f);
            }

            // The same way of building the orientation as in §A-1.
            var q = Quaternion.AngleAxis(DeterministicRandom.Unit(seed, salt + 2u) * 360f,
                                         Vector3.up)
                  * Quaternion.AngleAxis(DeterministicRandom.Unit(seed, salt + 3u) * 30f - 15f,
                                         Vector3.right);

            // ★ We add the delay ourselves. The game rounds anything earlier than
            //   EarliestFrame up (§A-3), so without adding it the ledger's scheduled frame
            //   and the actual firing frame drift apart.
            uint when = LightningBudget.EarliestFrame(frame)
                      + (uint)(DeterministicRandom.Unit(seed, salt + 4u)
                               * LightningBudget.MaxDelayFrames);

            var group = GroupOf(TyphoonController.DisasterId);

            // ★ Always look at the return value. false is the only signal for "dropped
            //   because the queue is full" (§A-3).
            if (Singleton<WeatherManager>.instance.QueueLightningStrike(when, p, q, group))
            {
                _scheduled[slot] = when;
                _inFlight++;
                _lastQueued++;
                _totalQueued++;
                _nextStrikeFrame = frame + IntervalFor(TyphoonController.Intensity);
            }
            else
            {
                _lastRejected++;
                _totalRejected++;
                // **Do not let it be dropped silently.** Warn on the first one only, then
                // drop to a throttled Diag (Log.Warn cannot sit on an every-tick route).
                if (!_rejectionLogged)
                {
                    _rejectionLogged = true;
                    Log.Warn("typhoon lightning was dropped by the game: the strike queue is "
                             + "full (cap " + LightningBudget.QueueCapacity + "). The host "
                             + "storm's own strikes are being thrown away too.");
                }
                // If we hit the ceiling, we will hit it next tick too. Do not keep
                // hammering at it on a shorter interval.
                _nextStrikeFrame = frame + MaxIntervalFrames;
            }
        }

        /// <summary>The stronger the shorter. Maps 0-255 onto
        /// <see cref="MaxIntervalFrames"/>-<see cref="MinIntervalFrames"/>.</summary>
        private static uint IntervalFor(byte intensity)
        {
            float t = intensity / MaxIntensity;
            float interval = MaxIntervalFrames - (MaxIntervalFrames - MinIntervalFrames) * t;
            if (interval < MinIntervalFrames) interval = MinIntervalFrames;
            return (uint)interval;
        }

        private static int FreeSlot()
        {
            for (int i = 0; i < _scheduled.Length; i++)
            {
                if (_scheduled[i] == 0u) return i;
            }
            return -1;
        }

        /// <summary>
        /// Bundle the strike into the typhoon's disaster group. The same shape as ②'s
        /// <c>LongPeriodDamage.GroupOf</c>. <c>QueueLightningStrike</c> works with null
        /// too (the strike simply does not join a group).
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // Singleton<T>.instance runs FindObjectOfType and new GameObject when
            // sInstance is null, which makes it a main thread only API. This is the sim
            // thread.
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **Written every time, even when nothing was fired** (so as not to repeat ③'s
        /// failure where "you could not tell from the diagnostics at all whether fire
        /// spread was running"). <c>Log.Diag</c> thins the same key down to once per 512
        /// sim frames, but **the string concatenation in the arguments would still run
        /// every tick**, so we bail out first with <c>DiagEnabled</c> (C# evaluates the
        /// arguments fully before the call).
        /// </summary>
        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLightning",
                "lightning: inFlight=" + _inFlight
                + " queued=" + _lastQueued + " total=" + _totalQueued
                + " rejected=" + _lastRejected + "/" + _totalRejected
                + " vanillaReserve=" + _lastVanillaReserve
                + " allowance=" + _lastAllowance
                + (_inFlight > 0
                    ? "  (queue kept non-empty: environmental lightning suppressed)"
                    : "  (queue may be empty: the game can queue its own strike)"));
        }

        /// <summary>
        /// Call when letting go of a typhoon (<c>TyphoonController.Forget</c>) and on
        /// level unload. **Do not carry the stock over to another city or to the next
        /// typhoon.** Carry it over and the next typhoon sees a queue that is actually
        /// free as "full" and never fires.
        ///
        /// It is idempotent (calling it repeatedly is fine).
        /// </summary>
        public static void Reset()
        {
            for (int i = 0; i < _scheduled.Length; i++) _scheduled[i] = 0u;

            _inFlight = 0;
            _lastQueued = 0;
            _lastRejected = 0;
            _totalQueued = 0;
            _totalRejected = 0;
            _lastVanillaReserve = 0;
            _lastAllowance = 0;
            _nextStrikeFrame = 0u;
            _salt = 0u;
            // ★ _errorLogged is not reset (it is a fact about the game build, not
            //    per-city state. Treated the same way as TyphoonWeather / TyphoonReader).
        }
    }
}
