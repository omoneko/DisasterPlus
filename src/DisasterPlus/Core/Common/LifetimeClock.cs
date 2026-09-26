namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// An immutable clock that accumulates elapsed in-game time (minutes).
    /// Not real time. While paused, Advance(0) is all that gets called and nothing moves on.
    /// </summary>
    public struct LifetimeClock
    {
        public readonly float ElapsedMinutes;

        private LifetimeClock(float elapsed)
        {
            ElapsedMinutes = elapsed;
        }

        public static LifetimeClock Start()
        {
            return new LifetimeClock(0f);
        }

        /// <summary>
        /// Returns a new clock with the elapsed time added. A negative delta is ignored
        /// (loading a save or unpausing can make the frame difference run backwards).
        /// </summary>
        public LifetimeClock Advance(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return this;
            return new LifetimeClock(ElapsedMinutes + deltaMinutes);
        }
    }
}
