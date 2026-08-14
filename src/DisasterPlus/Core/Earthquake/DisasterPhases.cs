namespace DisasterPlus.Core.Earthquake
{
    /// <summary>災害の進行位相。</summary>
    public enum EarthquakePhase
    {
        Unknown,
        Emerging,
        Active,
        Clearing,
        Finished,
    }

    /// <summary>
    /// DisasterData.m_flags のビットと、そこから導ける判定。
    ///
    /// Core に置いているのは、これがゲームの型に一切依存しない整数演算であり、
    /// **ここを取り違えると全てが静かに壊れる**からユニットテストで固定したいため。
    /// Game 側は <c>(int)data.m_flags</c> を渡すだけにして、フラグの意味を
    /// 2 箇所に書かない。
    ///
    /// 典拠: IL 事実文書 §A-1（位相機械）/ §A-6（ハザードマップの 2 段ゲート）。
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
        /// これが立っていないと EarthquakeAI.StartDisaster / TsunamiAI.StartDisaster は
        /// 即 return し、m_activationFrame が 0 のまま **Emerging で永久に固まる**（§A-1）。
        /// 災害を起こす側（Task 9）は必ず立てる。読む側（Task 3）は
        /// m_activationFrame == 0 を「未定」として扱う。
        /// </summary>
        public const int SelfTrigger = 64;

        public const int Significant = 256;

        /// <summary>
        /// 測位済み。地震にこれを立てるのは、震央の EarthquakeCoverage != 0、
        /// すなわち**地震計だけ**（§A-2 / §C-2）。
        /// </summary>
        public const int Located = 4096;

        /// <summary>進んでいる方から順に見る。</summary>
        public static EarthquakePhase PhaseOf(int flags)
        {
            if ((flags & Finished) != 0) return EarthquakePhase.Finished;
            if ((flags & Clearing) != 0) return EarthquakePhase.Clearing;
            if ((flags & Active) != 0) return EarthquakePhase.Active;
            if ((flags & Emerging) != 0) return EarthquakePhase.Emerging;
            return EarthquakePhase.Unknown;
        }

        /// <summary>IL: (m_flags &amp; 3) == 1。バニラ自身の走査条件をそのまま写す。</summary>
        public static bool IsAlive(int flags)
        {
            return (flags & (Created | Deleted)) == Created;
        }

        public static bool IsLocated(int flags)
        {
            return (flags & Located) != 0;
        }

        /// <summary>
        /// この災害が今ハザードマップに何かを塗るか。
        /// **嵐（ThunderStormAI / TornadoAI）とバイト単位で同一のゲート**（§A-6）。
        /// これが false なのにグリッドが 0 だからといって「安全」と読ませてはいけない。
        /// </summary>
        public static bool PaintsHazardMap(int flags)
        {
            return IsLocated(flags) && (flags & (Emerging | Active)) != 0;
        }
    }
}
