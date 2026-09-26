namespace DisasterPlus.Core.Earthquake
{
    /// <summary>How far along a disaster is.</summary>
    public enum EarthquakePhase
    {
        Unknown,
        Emerging,
        Active,
        Clearing,
        Finished,
    }

    /// <summary>
    /// The bits of DisasterData.m_flags, and what can be derived from them.
    ///
    /// This lives in Core because it is integer arithmetic that depends on none of the
    /// game's types, and because **get this wrong and everything breaks silently**, so we
    /// want it pinned by unit tests. The Game side only passes in
    /// <c>(int)data.m_flags</c>, so the meaning of the flags is never written down in two
    /// places.
    ///
    /// Sources: IL findings doc §A-1 (the phase machine) / §A-6 (the hazard map's two-stage gate).
    /// </summary>
    public static class DisasterPhases
    {
        public const int Created = 1;
        public const int Deleted = 2;
        public const int Emerging = 4;
        public const int Active = 8;
        public const int Clearing = 16;
        public const int Finished = 32;

        /// <summary>
        /// Without this bit set, EarthquakeAI.StartDisaster / TsunamiAI.StartDisaster return
        /// immediately, m_activationFrame stays 0 and the disaster **sticks in Emerging for
        /// ever** (§A-1). Whoever starts a disaster (Task 9) must always set it. Whoever
        /// reads one (Task 3) treats m_activationFrame == 0 as "not decided yet".
        /// </summary>
        public const int SelfTrigger = 64;

        public const int Significant = 256;

        /// <summary>
        /// Located. For an earthquake this only gets set when EarthquakeCoverage at the
        /// epicentre is != 0, which means **seismometers and nothing else** (§A-2 / §C-2).
        /// </summary>
        public const int Located = 4096;

        /// <summary>Checked in order, furthest-along first.</summary>
        public static EarthquakePhase PhaseOf(int flags)
        {
            if ((flags & Finished) != 0) return EarthquakePhase.Finished;
            if ((flags & Clearing) != 0) return EarthquakePhase.Clearing;
            if ((flags & Active) != 0) return EarthquakePhase.Active;
            if ((flags & Emerging) != 0) return EarthquakePhase.Emerging;
            return EarthquakePhase.Unknown;
        }

        /// <summary>IL: (m_flags &amp; 3) == 1. A straight copy of vanilla's own sweep
        /// condition.</summary>
        public static bool IsAlive(int flags)
        {
            return (flags & (Created | Deleted)) == Created;
        }

        public static bool IsLocated(int flags)
        {
            return (flags & Located) != 0;
        }

        /// <summary>
        /// Whether this disaster is painting anything onto the hazard map right now.
        /// **Byte-for-byte the same gate as the storms (ThunderStormAI / TornadoAI)** (§A-6).
        /// When this is false, a grid reading of 0 must not be shown to the player as "safe".
        /// </summary>
        public static bool PaintsHazardMap(int flags)
        {
            return IsLocated(flags) && (flags & (Emerging | Active)) != 0;
        }

        /// <summary>
        /// The same gate, seen from a reading that has already been folded down to a phase
        /// (<c>EarthquakeReading</c>).
        ///
        /// **This is an addition that was not on the plan's list of files to change.** It
        /// is here anyway because its callers (Task 4's panel and diagnostics) do not hold
        /// the raw m_flags, and copying the same condition into two places on the Game side
        /// would simply reproduce the very defect ① has only just fixed: reading the gate
        /// somewhere else and reporting a number off the back of it.
        ///
        /// That it agrees with <see cref="PaintsHazardMap(int)"/> rests on the phase bits
        /// being **mutually exclusive** (IL findings doc §A-1:
        /// <c>ActivateDisaster</c> does <c>(m_flags &amp; ~4) | 8</c> and
        /// <c>DeactivateDisaster</c> does <c>(m_flags &amp; ~12) | 16</c>, so every
        /// transition drops the previous phase bit).
        /// That assumption is pinned by unit tests.
        /// </summary>
        public static bool PaintsHazardMap(bool located, EarthquakePhase phase)
        {
            return located && (phase == EarthquakePhase.Emerging || phase == EarthquakePhase.Active);
        }
    }
}
