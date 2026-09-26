namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// A copy of <c>WaterSimulation.Cell</c>. **Field order and types are exactly as in the game.**
    ///
    /// The declaration confirmed by disassembly (Marshal.OffsetOf agrees):
    /// <code>
    ///   struct Cell { ushort m_height @0; ushort m_pollution @2;
    ///                 short m_velocityX @4; short m_velocityZ @6; }   // 8 bytes
    /// </code>
    ///
    /// ★ <b>Units</b>: <see cref="Height"/> is a water column in 1/64 m.
    ///   <see cref="VelocityX"/> / <see cref="VelocityZ"/> use the <b>same unit</b> and are
    ///   literally "the amount of water column that crosses the cell boundary in one frame"
    ///   (at IL_1137 <c>m = a.m_velocityX</c> is used <b>as is</b> to add to and subtract from
    ///   the height; no coefficient and no time step are applied). Despite the name "velocity",
    ///   what it really holds is a <b>flow rate</b>.
    /// </summary>
    internal struct Cell
    {
        /// <summary>Height of the water column (1/64 m). The amount sitting on top of the
        /// terrain, not an elevation.</summary>
        public ushort Height;

        /// <summary>Pollution. **Has no effect whatsoever on the motion of the water** (in the
        /// IL, pollution never writes m_height).</summary>
        public ushort Pollution;

        /// <summary>Amount flowing out in the +X direction (1/64 m). Negative means it flows in
        /// from x+1.</summary>
        public short VelocityX;

        /// <summary>Amount flowing out in the +Z direction (1/64 m). Negative means it flows in
        /// from z+1.</summary>
        public short VelocityZ;
    }
}
