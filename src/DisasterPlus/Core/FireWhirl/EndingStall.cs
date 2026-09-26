namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// Detects "a whirl that entered its teardown but never finishes".
    ///
    /// Why it is needed: one of the IL assumptions ③'s implementation got wrong was
    /// "m_targetPos0 is a distant destination", when it is in fact the centre of the fire
    /// itself. If that mix-up comes back, a whirl that has entered its teardown never
    /// reaches ArriveAtDestination and hangs around without dissipating. And still no
    /// exception is raised and nothing is logged. An existence check (is the patch applied?)
    /// does not catch it; it is the kind of failure you only see from "what happened when it
    /// ran", so we check it here as behaviour.
    ///
    /// It lives in Core as a pure, engine-free function, pinned down by unit tests.
    /// </summary>
    public static class EndingStall
    {
        /// <summary>
        /// How many sim frames pass before one vehicle gets a single
        /// VehicleAI.SimulationStep.
        ///
        /// Measured from the IL (VehicleManager.SimulationStepImpl):
        ///     int bucket = SimulationManager.m_currentFrameIndex &amp; 15;
        ///     for (int i = bucket * 1024; i &lt;= (bucket + 1) * 1024 - 1; i++)
        ///         info.m_vehicleAI.SimulationStep(i, ref buffer[i], refPos);
        /// The 16,384 slots are split across 16 buckets, so one vehicle only steps once per
        /// 16 sim frames. Misread this as "one step = one frame" and you underestimate the
        /// teardown time by a factor of 16 (which is exactly why this constant exists).
        /// </summary>
        public const int FramesPerVehicleStep = 16;

        /// <summary>
        /// How many vehicle steps a healthy teardown takes.
        ///
        /// Measured from the IL (VortexAI.SimulationStep(6 args) /
        /// VortexAI.ArriveAtDestination):
        ///   - Once the distance to the destination drops to 1.0 or below,
        ///         m_angleVelocity = Mathf.Max(0f, m_angleVelocity - 0.05f)
        ///   - ArriveAtDestination is only consulted while m_angleVelocity &lt; 0.05f
        ///         (IL: ldc.r4 0.05 / skipped over by bge.un)
        ///   - VortexAI.ArriveAtDestination is return ++m_waitCounter &gt; 4
        ///
        /// A pinned whirl has its destination 1,000 m away, so m_angleVelocity is held at
        /// 1.0f by Mathf.Min(1f, av + 0.05f) and is always 1.0f when the teardown begins.
        /// Subtracting 0.05f from there, float32 rounding makes the 19th subtraction give
        /// 0.0499998443f, which is the first time it falls below 0.05f (not the 20th — we
        /// actually computed 1.0f minus 0.05f nineteen times in float to check).
        /// That 19th step is also ArriveAtDestination's first, so m_waitCounter reaches 5
        /// (&gt; 4) on step 19 + 4 = 23.
        /// </summary>
        public const int TeardownVehicleSteps = 23;

        /// <summary>How many sim frames a healthy teardown takes. 23 × 16 = 368.</summary>
        public const int TeardownFrames = TeardownVehicleSteps * FramesPerVehicleStep;

        /// <summary>
        /// How much headroom to leave on top of the measured teardown cost.
        ///
        /// A factor of 4 is 92 vehicle steps. The teardown proper needs 23, leaving 69 steps
        /// of slack, and what that slack mainly absorbs is "the run-up before the spin-down
        /// starts". Just after the teardown begins the vehicle still has travel speed;
        /// m_velocity decays by roughly 0.85× per step, but the spin-down does not begin
        /// until the distance to the destination drops to 1.0 or below (the vortex prefab's
        /// m_maxSpeed is not in the IL, so the initial speed cannot be pinned down).
        /// 0.85^69 ≒ 1/76,000, so even if the initial speed were four orders of magnitude
        /// above what the cap assumes, it still passes as healthy. On top of that, the same
        /// slack absorbs the phase difference between our tick and the vehicle's bucket (at
        /// most one step).
        ///
        /// A false positive costs more. The failure we want to detect (never reaching
        /// ArriveAtDestination) is "it never finishes", not "it is slow", so however far we
        /// stretch the threshold we will not miss it.
        /// </summary>
        public const float TeardownSafetyFactor = 4f;

        /// <summary>
        /// The measured vanilla figure for sim frames per in-game minute
        /// (SimulationManager.DAYTIME_FRAMES = 65536, 1 day = 1440 minutes).
        ///
        /// Core does not touch the game API, so it keeps a default here, but the game side
        /// should pass FeatureHost.FramesPerMinute (which divides the real DAYTIME_FRAMES
        /// each time). That way a game update changing this value cannot silently put us
        /// out.
        /// </summary>
        public const float VanillaFramesPerMinute = 65536f / 1440f;

        /// <summary>
        /// "How many times the maximum lifetime we are willing to wait".
        ///
        /// On its own this is not a floor. The settings slider (1-60 minutes) is a value
        /// unrelated to the teardown cost, so if it is set low the threshold falls below the
        /// healthy teardown time. Always use it together with the measurement-derived floor
        /// (<see cref="MinimumMinutes"/>).
        /// </summary>
        public const float LifetimeMultiplier = 2f;

        /// <summary>
        /// The floor for the threshold (in-game minutes), derived from the measured teardown
        /// cost. At the default 45.51 frames/minute that is 368 / 45.51 × 4 ≒ 32.3 minutes
        /// (the healthy teardown itself being ≒ 8.09 minutes).
        /// </summary>
        public static float MinimumMinutes(float framesPerMinute)
        {
            float fpm = framesPerMinute > 0f ? framesPerMinute : VanillaFramesPerMinute;
            return TeardownFrames / fpm * TeardownSafetyFactor;
        }

        /// <summary>The threshold actually used (in-game minutes). The wording written to
        /// the log should use this too.</summary>
        public static float ThresholdMinutes(int maxLifetimeMinutes, float framesPerMinute)
        {
            // When the setting is broken (zero or below) we drop the lifetime term and judge
            // on the floor alone. The floor is 4× a healthy teardown, so this does not
            // produce false positives.
            float fromLifetime = maxLifetimeMinutes > 0 ? maxLifetimeMinutes * LifetimeMultiplier : 0f;
            float floor = MinimumMinutes(framesPerMinute);
            return fromLifetime > floor ? fromLifetime : floor;
        }

        /// <param name="endingMinutes">In-game minutes elapsed since the teardown
        /// began.</param>
        /// <param name="maxLifetimeMinutes">The configured maximum lifetime (in-game
        /// minutes).</param>
        /// <param name="framesPerMinute">Sim frames per in-game minute.</param>
        public static bool IsStuck(float endingMinutes, int maxLifetimeMinutes, float framesPerMinute)
        {
            return endingMinutes > ThresholdMinutes(maxLifetimeMinutes, framesPerMinute);
        }
    }
}
