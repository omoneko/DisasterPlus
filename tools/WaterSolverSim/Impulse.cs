namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <c>WaterWave</c> の <c>m_type == 2</c>（TYPE_IMPACT）ぶんだけを写したもの。
    ///
    /// ソルバはこれを<b>仮想の水の山</b>として水面の傾きに足す（IL_0845-0A49）。
    /// <b>体積は増えない</b> —— 海底が隆起したのと同じで、津波の発生源そのもの。
    ///
    /// <code>
    ///   R  = 1 + max(maxX - origX, origX - minX)      // ★ X しか見ない（IL_08BD-091E）
    ///   R2 = R*R
    ///   f(a,b) = delta - delta*(a*a + b*b)/R2         // ただし a*a+b*b &lt; R2 のときだけ
    ///   accX += f(dx,dz) - f(dx+1,dz)
    ///   accZ += f(dx,dz) - f(dx,dz+1)
    /// </code>
    ///
    /// ★ bbox の判定は <c>x &gt;= minX &amp;&amp; x &lt;= maxX+1 &amp;&amp; z &gt;= minZ &amp;&amp; z &lt;= maxZ+1</c>
    ///   （IL_0845 / 0862 / 0881 / 089E）。**+1 は差分を取るぶんの余白**で、書き間違いではない。
    /// </summary>
    internal struct Impulse
    {
        public int OrigX;
        public int OrigZ;
        public int MinX;
        public int MinZ;
        public int MaxX;
        public int MaxZ;

        /// <summary><c>m_delta</c>。**Int16**。正が山（水を外へ）、負が窪み（水を中へ）。</summary>
        public short Delta;

        /// <summary>
        /// 原点と半径から作る。半径 <paramref name="radiusCells"/> セルの箱を張ると
        /// IL の式では <c>R = radiusCells + 1</c> になる —— MOD 側（TsunamiSource）の
        /// 説明文にある「R = 121 セル」はこの +1 込みの値である。
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

            // m_delta は Int16。**溢れさせると符号が反転して海が爆発する**ので詰める。
            if (delta > 32767) delta = 32767;
            if (delta < -32768) delta = -32768;
            w.Delta = (short)delta;
            return w;
        }
    }
}
