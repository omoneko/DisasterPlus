namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// A copy of just the <c>m_type == 2</c> (TYPE_IMPACT) part of <c>WaterWave</c>.
    ///
    /// The solver adds this to the slope of the water surface as a <b>virtual mound of
    /// water</b> (IL_0845-0A49). <b>The volume does not increase</b> —— it is the same as the
    /// sea floor being uplifted, which is exactly what generates a tsunami.
    ///
    /// <code>
    ///   R  = 1 + max(maxX - origX, origX - minX)      // ★ only X is considered (IL_08BD-091E)
    ///   R2 = R*R
    ///   f(a,b) = delta - delta*(a*a + b*b)/R2         // but only when a*a+b*b &lt; R2
    ///   accX += f(dx,dz) - f(dx+1,dz)
    ///   accZ += f(dx,dz) - f(dx,dz+1)
    /// </code>
    ///
    /// ★ The bbox test is <c>x &gt;= minX &amp;&amp; x &lt;= maxX+1 &amp;&amp; z &gt;= minZ &amp;&amp; z &lt;= maxZ+1</c>
    ///   (IL_0845 / 0862 / 0881 / 089E). **The +1 is the margin needed for taking the
    ///   difference**, not a typo.
    /// </summary>
    internal struct Impulse
    {
        public int OrigX;
        public int OrigZ;
        public int MinX;
        public int MinZ;
        public int MaxX;
        public int MaxZ;

        /// <summary><c>m_delta</c>. **Int16**. Positive is a mound (water pushed outwards),
        /// negative is a depression (water drawn inwards).</summary>
        public short Delta;

        /// <summary>
        /// Builds one from an origin and a radius. Spanning a box of
        /// <paramref name="radiusCells"/> cells gives <c>R = radiusCells + 1</c> in the IL's
        /// formula —— the "R = 121 cells" in the mod-side description (TsunamiSource) is the
        /// value with that +1 included.
        /// </summary>
        public static Impulse Round(int origX, int origZ, int radiusCells, int delta)
        {
            Impulse w = new Impulse();
            w.OrigX = origX;
            w.OrigZ = origZ;
            w.MinX = origX - radiusCells;
            w.MaxX = origX + radiusCells;
            w.MinZ = origZ - radiusCells;
            w.MaxZ = origZ + radiusCells;

            // m_delta is Int16. **Let it overflow and the sign flips and the sea explodes**,
            // so clamp it.
            if (delta > 32767) delta = 32767;
            if (delta < -32768) delta = -32768;
            w.Delta = (short)delta;
            return w;
        }
    }
}
