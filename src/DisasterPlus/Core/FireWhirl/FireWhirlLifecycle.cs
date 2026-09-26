namespace DisasterPlus.Core.FireWhirl
{
    public enum FireWhirlVerdict
    {
        Continue,
        Dissipate,
    }

    /// <summary>
    /// The lifetime of one fire whirl. It dies in three stages.
    ///   1. It lives on as long as the fire that spawned it keeps burning
    ///   2. It dissipates once the spawn condition has been unmet for longer than the grace
    ///      period
    ///   3. Once the absolute cap is reached it is always cut off, even if the fire
    ///      continues
    ///
    /// Stage 3 is essential. Fire spread creates a self-reinforcing loop — "more fire
    /// spread → the condition keeps being met → the whirl lives longer" — so the cap is
    /// the only safety valve.
    /// </summary>
    public struct FireWhirlLifecycle
    {
        public readonly float ElapsedMinutes;
        public readonly float ConditionBrokenMinutes;

        private FireWhirlLifecycle(float elapsed, float broken)
        {
            ElapsedMinutes = elapsed;
            ConditionBrokenMinutes = broken;
        }

        public static FireWhirlLifecycle Start()
        {
            return new FireWhirlLifecycle(0f, 0f);
        }

        /// <param name="conditionMet">Whether the spawn condition (N buildings within R) is
        /// still met at this point.</param>
        public FireWhirlLifecycle Advance(float deltaMinutes, bool conditionMet)
        {
            if (deltaMinutes <= 0f) return this;

            // Reset the grace counter once the condition comes back, so a flicker in the
            // fire's strength does not kill the whirl.
            float broken = conditionMet ? 0f : ConditionBrokenMinutes + deltaMinutes;
            return new FireWhirlLifecycle(ElapsedMinutes + deltaMinutes, broken);
        }

        public FireWhirlVerdict Evaluate(FireWhirlConfig config)
        {
            if (ElapsedMinutes >= config.MaxLifetimeMinutes) return FireWhirlVerdict.Dissipate;
            if (ConditionBrokenMinutes >= config.ConditionGraceMinutes) return FireWhirlVerdict.Dissipate;
            return FireWhirlVerdict.Continue;
        }
    }
}
