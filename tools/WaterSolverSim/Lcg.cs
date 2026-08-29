namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <c>ColossalFramework.Math.Randomizer</c> の<b>写し</b>（代役ではない）。
    ///
    /// ★★ <b>ColossalManaged.dll を逆アセンブルして確定させた。</b>
    ///   <c>ilload.ps1</c> は ColossalManaged を先読みしているので、
    ///   <c>$global:preloaded['ColossalManaged'].GetType('ColossalFramework.Math.Randomizer')</c>
    ///   から IL が取れる。実体は 64 bit LCG（Knuth / MMIX の定数）1 個:
    ///
    /// <code>
    ///   struct Randomizer { ulong seed; }
    ///
    ///   .ctor(int s)     : seed = 6364136223846793005 * (ulong)(long)s + 1442695040888963407;
    ///
    ///   int Int32(uint m):                                   // ★ 値を先に作り、種は後で進める
    ///       r    = (int)(((seed >> 32) * (ulong)m) >> 32);
    ///       seed = 6364136223846793005 * seed + 1442695040888963407;
    ///       return r;
    /// </code>
    ///
    /// ★ <b>値域は <c>[0, max)</c></b>。<c>(seed&gt;&gt;32)</c> は <c>[0, 2^32)</c> なので
    ///   <c>((seed&gt;&gt;32) * m) &gt;&gt; 32</c> は <c>[0, m)</c> —— 上限は取らない。
    ///   <c>(rnd + v*2047) &gt;&gt; 11</c> や <c>(diff + rnd) &gt;&gt; 2</c> の丸めは
    ///   これを前提に読む（IL_0AF8 / 0B37 ほか）。
    ///
    /// ★ 剰余ではなく<b>上位 32 bit の乗算</b>で範囲へ落とすので、
    ///   <c>max</c> が 2 の冪でなくても偏らない。<c>%</c> で代用してはいけない ——
    ///   数列がずれてビット一致しなくなる。
    ///
    /// ★ 本物と同じく <b>1 フレームに 1 個</b>だけ作り、<c>m_stepIndex</c> の
    ///   <b>増やす前</b>の値を種にする（IL_02EA-0300）。フレームを跨いで
    ///   同じ列を使い回さないこと。
    /// </summary>
    internal struct Lcg
    {
        private const ulong Mul = 6364136223846793005UL;
        private const ulong Inc = 1442695040888963407UL;

        private ulong _state;

        /// <summary><c>Randomizer::.ctor(Int32)</c>。種を 1 段回してから持つ。</summary>
        public Lcg(ulong seed)
        {
            _state = Mul * seed + Inc;
        }

        /// <summary>
        /// <c>Randomizer::Int32(UInt32)</c> の写し。<c>[0, max)</c>。
        ///
        /// ★ <b>返す値は「進める前」の種から作る。</b>順序を入れ替えると
        ///   1 個ずれた数列になる（本物と一致しなくなる）。
        /// ★ <c>max == 0</c> でも本物は種を進めるので、ここでも進める。
        /// </summary>
        public int Int32(int max)
        {
            int r = (int)(((_state >> 32) * (uint)max) >> 32);
            _state = Mul * _state + Inc;
            return r;
        }
    }
}
