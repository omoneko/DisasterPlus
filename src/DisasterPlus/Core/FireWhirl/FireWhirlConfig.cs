namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// ③ The fire whirl's settings. Repacked from the Game layer's ModSettings and handed
    /// to Core. Core knows nothing about SavedInt, so this holds plain values only.
    /// </summary>
    public class FireWhirlConfig
    {
        /// <summary>The spawn-test radius (metres). A whirl spawns if DetectCount buildings
        /// are burning within this distance.</summary>
        public float DetectRadius;

        /// <summary>The building count for the spawn test.</summary>
        public int DetectCount;

        /// <summary>The minimum separation (metres) used to merge candidates and to stop
        /// several whirls spawning on top of each other.</summary>
        public float MinSeparation;

        /// <summary>The absolute lifetime cap (in-game minutes). Past this it is always cut
        /// off.</summary>
        public float MaxLifetimeMinutes;

        /// <summary>
        /// The grace period (in-game minutes) between the spawn condition being unmet and
        /// the whirl dissipating.
        ///
        /// 1 in-game minute = 65536 / 1440 ≒ 45.5 sim frames
        /// (SimulationManager.DAYTIME_FRAMES). The sweep over burning buildings completes
        /// one round in 8 ticks, and at game speed 3 one tick is 9 frames, so a round takes
        /// at worst 72 frames ≒ 1.6 minutes. If the grace period is shorter than that, the
        /// whirl dies purely on results that are one sweep out of date. The default leaves
        /// close to twice one sweep's worth of headroom.
        /// </summary>
        public float ConditionGraceMinutes;

        /// <summary>The strength of the fire spread. 0 means no spread at all, 10 is the
        /// maximum.</summary>
        public int SpreadStrength;

        public static FireWhirlConfig Defaults()
        {
            return new FireWhirlConfig
            {
                DetectRadius = 150f,
                DetectCount = 12,
                MinSeparation = 300f,
                MaxLifetimeMinutes = 10f,
                ConditionGraceMinutes = 3f,
                SpreadStrength = 3,
            };
        }
    }
}
