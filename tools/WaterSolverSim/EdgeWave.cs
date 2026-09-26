using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b>The DLC tsunami itself.</b> A reproduction of <c>WaterWave.GetSeaLevel</c>
    /// (<c>m_type == 1</c>).
    ///
    /// ── Why port it (2026-08-31) ────────────────────────────
    ///
    /// Our wave is <c>TYPE_IMPACT</c>, i.e. <b>a virtual hill placed in the sea</b>.
    /// A hill only displaces water, it <b>does not create any</b> —— whatever it pushes must
    /// come from somewhere else. So it can only apply a net-zero external force; what it
    /// produces is a "dipole", not a "wall of water".
    ///
    /// The DLC tsunami is <b>an entirely different trick</b>. It is only evaluated inside the
    /// outermost-ring loop of <c>SimulateWater</c>, and there it <b>rewrites the sea surface
    /// of the outermost cells themselves</b>. That ring is a Dirichlet boundary —— if there
    /// is not enough water it <b>creates some</b>.
    ///
    /// ★★ **So the DLC tsunami is an infinite water source, whereas our wave recycles the
    ///   water it already has.** Until the two are compared on the same footing, there is no
    ///   telling what "not enough power" really means. This class exists solely to create
    ///   that footing.
    ///
    /// ── The IL (docs/superpowers/specs/2026-08-29-tsunami-il-facts.md §3) ────
    ///
    /// <code>
    /// phase = ((x - origX)*dirX + (z - origZ)*dirZ) &gt;&gt; 8   // dir length 32768 -&gt; 1 cell = 128
    /// t     = currentTime - phase
    /// if (t &lt;= 0 || t &gt;= duration) return original
    /// amp   = delta * (65536 - currentTime) / 65536
    /// arg   = amp - amp*cos(2*pi * t / duration)             // the (1-cos) envelope
    /// off   = arg * sin(2*pi * 1.5 * t / duration) / 2       // 1.5 cycles
    /// return original - off
    /// </code>
    ///
    /// The crest of the leading wave is at <c>t = duration/2</c> and reaches
    /// <c>original + amp</c>. <c>currentTime</c> advances by +64 per water step and
    /// <c>duration = 16384</c>, i.e. **it is over after 256 water steps.**
    /// </summary>
    public sealed class EdgeWave
    {
        /// <summary><c>m_duration</c>. Vanilla uses <c>256 &lt;&lt; 6 = 16384</c>.</summary>
        public const int VanillaDuration = 16384;

        /// <summary>How far <c>m_currentTime</c> advances per water step
        /// (IL_03B3-03C6).</summary>
        public const int TimePerStep = 64;

        /// <summary>
        /// <c>m_delta</c> (1/64 m). <c>round(64 * 64 * intensity / 55)</c>.
        /// At intensity 100 that is 7447 (116.4 m), at 255 it is 18991 (296.7 m).
        /// </summary>
        public static int DeltaFor(int intensity)
        {
            return (int)Math.Round(64.0 * 64.0 * intensity / 55.0);
        }

        public int OrigX;
        public int OrigZ;
        public int DirX = 32768;   // pointing inwards, length 32768
        public int DirZ;
        public int MinX;
        public int MinZ;
        public int MaxX;
        public int MaxZ;
        public int Delta;
        public int Duration = VanillaDuration;
        public int CurrentTime;

        /// <summary>Whether it is still moving the sea surface.</summary>
        public bool Active { get { return CurrentTime < Duration; } }

        /// <summary>Advance by one water step.</summary>
        public void Step() { CurrentTime += TimePerStep; }

        /// <summary>
        /// The sea surface at this outermost cell (1/64 m). Outside the bbox and outside the
        /// time window, <paramref name="original"/> is returned unchanged.
        /// </summary>
        public int LevelAt(int x, int z, int original)
        {
            if (x < MinX || x > MaxX || z < MinZ || z > MaxZ) return original;

            int phase = ((x - OrigX) * DirX + (z - OrigZ) * DirZ) >> 8;
            int t = CurrentTime - phase;
            if (t <= 0 || t >= Duration) return original;

            double amp = (double)Delta * (65536 - CurrentTime) / 65536.0;
            double arg = amp - amp * Math.Cos(2.0 * Math.PI * t / Duration);
            double off = arg * Math.Sin(2.0 * Math.PI * 1.5 * t / Duration) / 2.0;

            return original - (int)off;
        }
    }
}
