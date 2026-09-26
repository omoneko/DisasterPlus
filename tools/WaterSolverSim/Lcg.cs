namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// A <b>copy</b> of <c>ColossalFramework.Math.Randomizer</c> (not a stand-in).
    ///
    /// ★★ <b>Pinned down by disassembling ColossalManaged.dll.</b>
    ///   <c>ilload.ps1</c> preloads ColossalManaged, so the IL can be obtained from
    ///   <c>$global:preloaded['ColossalManaged'].GetType('ColossalFramework.Math.Randomizer')</c>.
    ///   It is really just a single 64-bit LCG (the Knuth / MMIX constants):
    ///
    /// <code>
    ///   struct Randomizer { ulong seed; }
    ///
    ///   .ctor(int s)     : seed = 6364136223846793005 * (ulong)(long)s + 1442695040888963407;
    ///
    ///   int Int32(uint m):                        // ★ build the value first, advance the seed after
    ///       r    = (int)(((seed >> 32) * (ulong)m) >> 32);
    ///       seed = 6364136223846793005 * seed + 1442695040888963407;
    ///       return r;
    /// </code>
    ///
    /// ★ <b>The range is <c>[0, max)</c></b>. <c>(seed&gt;&gt;32)</c> is <c>[0, 2^32)</c>, so
    ///   <c>((seed&gt;&gt;32) * m) &gt;&gt; 32</c> is <c>[0, m)</c> —— the upper bound is never hit.
    ///   Read the rounding in <c>(rnd + v*2047) &gt;&gt; 11</c> and <c>(diff + rnd) &gt;&gt; 2</c>
    ///   with that in mind (IL_0AF8 / 0B37 and others).
    ///
    /// ★ The range is reached by a <b>multiply of the top 32 bits</b> rather than a remainder,
    ///   so there is no bias even when <c>max</c> is not a power of two. Do not substitute
    ///   <c>%</c> —— the sequence would drift and no longer match bit for bit.
    ///
    /// ★ As in the real thing, create <b>exactly one per frame</b> and seed it with the value of
    ///   <c>m_stepIndex</c> <b>before</b> it is incremented (IL_02EA-0300). Do not reuse the
    ///   same sequence across frames.
    /// </summary>
    internal struct Lcg
    {
        private const ulong Mul = 6364136223846793005UL;
        private const ulong Inc = 1442695040888963407UL;

        private ulong _state;

        /// <summary><c>Randomizer::.ctor(Int32)</c>. Advances the seed one step before storing it.</summary>
        public Lcg(ulong seed)
        {
            _state = Mul * seed + Inc;
        }

        /// <summary>
        /// A copy of <c>Randomizer::Int32(UInt32)</c>. Returns <c>[0, max)</c>.
        ///
        /// ★ <b>The returned value is built from the seed as it was *before* advancing.</b>
        ///   Swapping the order gives a sequence shifted by one (no longer matching the real thing).
        /// ★ The real thing advances the seed even when <c>max == 0</c>, so we do too.
        /// </summary>
        public int Int32(int max)
        {
            int r = (int)(((_state >> 32) * (uint)max) >> 32);
            _state = Mul * _state + Inc;
            return r;
        }
    }
}
