namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <c>WaterSimulation.Cell</c> の写し。**フィールドの順も型もゲームのまま。**
    ///
    /// 逆アセンブルで確認した宣言（Marshal.OffsetOf でも一致）:
    /// <code>
    ///   struct Cell { ushort m_height @0; ushort m_pollution @2;
    ///                 short m_velocityX @4; short m_velocityZ @6; }   // 8 bytes
    /// </code>
    ///
    /// ★ <b>単位</b>: <see cref="Height"/> は 1/64 m の水柱。
    ///   <see cref="VelocityX"/> / <see cref="VelocityZ"/> は<b>同じ単位</b>で、
    ///   「1 フレームにセル境界を越えて動く水柱の量」そのものである
    ///   （IL_1137 で <c>m = a.m_velocityX</c> を<b>そのまま</b>高さの増減に使う。
    ///   係数も時間刻みも掛からない）。速度と言いながら実体は<b>流量</b>。
    /// </summary>
    internal struct Cell
    {
        /// <summary>水柱の高さ（1/64 m）。地形の上に乗っている量で、標高ではない。</summary>
        public ushort Height;

        /// <summary>汚染。**水の運動には一切影響しない**（IL 上、汚染は m_height を書かない）。</summary>
        public ushort Pollution;

        /// <summary>+X 方向へ流し出す量（1/64 m）。負なら x+1 から流れ込む。</summary>
        public short VelocityX;

        /// <summary>+Z 方向へ流し出す量（1/64 m）。負なら z+1 から流れ込む。</summary>
        public short VelocityZ;
    }
}
