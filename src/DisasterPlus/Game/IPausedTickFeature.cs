namespace DisasterPlus.Game
{
    /// <summary>
    /// A marker for features that want <see cref="IDisasterFeature.OnSimulationTick"/> even
    /// while the game is paused.
    ///
    /// Why it is needed (raised in the overall review): <see cref="FeatureHost.SimulationTick"/>
    /// returns without calling OnSimulationTick when 0 in-game minutes have elapsed. The very
    /// first tick after a load is always 0 minutes, so if you load and stay paused,
    /// **every row of the forecast panel sits there reading "unavailable"**. Nothing is
    /// actually broken, yet it looks broken — the kind of output this project hates most.
    /// FeatureHost has already made the same call for the same reason ("keep collecting
    /// diagnostics while paused" — see the comment over there), and display-only features
    /// deserve the same treatment.
    ///
    /// **Only apply this marker to features that just read and display, never advancing game
    /// state.** What the pause guard is really protecting is "do not advance simulation state
    /// on a 0-minute step", and that has not changed. Do not apply it to something like the
    /// fire whirl, which advances lifetimes, spread and spawning (apply it and disasters
    /// would carry on progressing while paused).
    ///
    /// When called through this path deltaMinutes is always 0f. It is up to the implementation
    /// to guarantee that it does not use deltaMinutes to advance state.
    ///
    /// It is a member-less marker interface because adding a member to
    /// <see cref="IDisasterFeature"/> would push the obligation to implement it onto every
    /// existing and future feature that does not need this treatment.
    /// </summary>
    public interface IPausedTickFeature
    {
    }
}
