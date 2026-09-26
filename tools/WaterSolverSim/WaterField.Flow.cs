using System;
using System.Collections.Generic;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// The <b>two passes that move the water</b> within <see cref="WaterField"/>.
    /// This is the heart of <c>SimulateWater</c>, corresponding to IL_0670-0FE7 (velocity)
    /// and IL_0FEC-1537 (transfer).
    /// </summary>
    internal sealed partial class WaterField
    {
        // ── Damping constants (IL_0305-0316) ─────────────────────────────
        //   loc41 = 11, loc42 = 1 << (11 & 31) = 2048, loc43 = 2047.
        //   Every frame it is multiplied by 2047/2048 ~= 0.9995. **It barely decays at all**,
        //   so once a wave is raised it keeps running for thousands of frames.
        private const int DampShift = 11;
        private const int DampOne = 1 << DampShift;   // 2048
        private const int DampKeep = DampOne - 1;     // 2047

        // ── ★★ The wave speed of this solver (measured with this very tool) ─────────
        //
        //   The velocity update is  v += (drop in water surface)/4
        //                                           (IL_0B37-0B47, the factor is fixed at 1/4)
        //   The transfer is         h -= v, h_next += v      (IL_11A6 / 11BE, no factor)
        //
        //   Taking cells and frames as the units, this is exactly a staggered grid for the
        //   wave equation:
        //
        //       c^2 = 1/4   ->   c = 0.5 cells / frame
        //
        //   ★ **It does not depend on the water depth.** A real shallow-water wave depends on
        //     depth as sqrt(g*h), but the game's solver only uses depth as a <b>cap on the
        //     amplitude</b> (v <= m_height).
        //
        //   ★ Measured (re-measured on 2026-08-30).
        //     **The earlier measurement (512 grid, 1.98 km at frame 420 -> 3.89 km at frame
        //     660) must not be used** —— on a 512 grid the outer ring is only 4.10 km from the
        //     centre, so the "ring" at 3.89 km is a reflection stuck to the boundary, not a
        //     freely running wave front.
        //
        //     On the same 1081 grid as the game (8.64 km to the boundary), measuring frames
        //     720 -> 1199, over which the front travels from 3.63 km to 7.57 km:
        //       3940 m / 479 frames = 8.22 m/frame = **0.514 cells/frame**.
        //     Varying the threshold (+0.5 / +1.0 / +2.0 m) and the forcing (drive 800 / 3200 /
        //     12800), the 2 km -> 3 km stretch stays at 0.51 - 0.55 cells/frame.
        //     Depths of 40 m and 150 m **agree exactly, right down to the arrival frame**.
        //
        //   At game speed 1 (60 frames per real second) 8 m/frame = **480 m/s**.
        //   A real tsunami in 40 m of water travels at 20 m/s, so **the game's water is about
        //   24 times faster**. Do not talk about tsunami arrival times as if they were real.

        /// <summary>
        /// The <b>velocity pass</b> (one row). IL_0670-0FE7.
        ///
        /// Reads <c>src</c> and writes <c>dst</c>. However, <b>the upper neighbour (z-1) alone
        /// is read from <c>dst</c></b> (IL_0CEB) —— that row has already been written, and it
        /// is needed because the flow limiting <b>reaches back and rewrites</b> the upper
        /// neighbour's velocity.
        ///
        /// The flow for a single cell:
        /// <list type="number">
        /// <item>evaporation and pollution decay (IL_07C8-0823)</item>
        /// <item>accumulate the external force (TYPE_IMPACT) into accX / accZ (IL_0845-0A49)</item>
        /// <item>update the X velocity (IL_0ACD-0BAC)</item>
        /// <item>update the Z velocity (IL_0BB1-0CD7)</item>
        /// <item>cap the outflow / inflow (IL_0D00-0FB2)</item>
        /// </list>
        /// </summary>
        private void VelocityRow(int z, Cell[] src, Cell[] dst, List<Impulse> impulses,
                                 ushort evaporation, ushort pollutionDecay,
                                 int mask0, int mask1, int mask2, int mask3)
        {
            int waveCount = (impulses == null) ? 0 : impulses.Count;

            int idx = z * _n;

            // Left neighbour (x-1). At the start of a row it is all zeros (the initobj at IL_07A9).
            Cell left = new Cell();

            // Cell A (x, z) and its terrain. **It is carried along the row, shifted by one cell
            // at a time**, so each cell is read from src only once per frame and the decay is
            // applied only once.
            Cell a = src[idx];
            int terrainA = _terrain[idx];

            // The decay for the first cell of the row (IL_07C8-0823). Later cells get it when
            // they are read as B.
            if (a.Height != 0) { a.Height = (ushort)(a.Height - evaporation); EvapRemoved += evaporation; }
            if (a.Pollution != 0) a.Pollution = (ushort)(a.Pollution - pollutionDecay);
            if (a.Pollution > a.Height) a.Pollution = a.Height;

            for (int x = 0; x <= _last; x++)
            {
                Cell b = new Cell();        // cell B (x+1, z)
                int terrainB = 0;
                int accX = 0;               // loc73: force added to the X slope
                int accZ = 0;               // loc74: force added to the Z slope

                // ── External force (IL_0845-0A49) ────────────────────────────
                for (int w = 0; w < waveCount; w++)
                {
                    Impulse imp = impulses[w];

                    // bbox. **The upper side goes to +1** (the margin needed for taking the
                    // difference, IL_0862 / 089E).
                    if (x < imp.MinX) continue;
                    if (x > imp.MaxX + 1) continue;
                    if (z < imp.MinZ) continue;
                    if (z > imp.MaxZ + 1) continue;

                    // R is set purely by the extent in X (IL_08BD-091E). Z is not considered.
                    int r = 1 + Math.Max(imp.MaxX - imp.OrigX, imp.OrigX - imp.MinX);
                    int r2 = r * r;                                   // IL_0920

                    int dx = x - imp.OrigX;                           // IL_0927
                    int dz = z - imp.OrigZ;                           // IL_0942
                    int dx1 = dx + 1;
                    int dz1 = dz + 1;

                    int d00 = dx * dx + dz * dz;                      // IL_0969
                    int d10 = dx1 * dx1 + dz * dz;                    // IL_0976
                    int d01 = dx * dx + dz1 * dz1;                    // IL_0983

                    int delta = imp.Delta;

                    // ★ The three terms are each guarded **independently** by d2 < R2
                    //   (IL_0990 / 09CC / 0A01). A term that falls outside does not "skip the
                    //   pair"; **only that one term becomes 0**.
                    if (d00 < r2)
                    {
                        // f = delta - delta*d2/R2. The division is C# integer division, i.e.
                        // truncation towards 0. The direction of that rounding matters when
                        // delta is negative, so do not substitute a shift.
                        int f = delta - delta * d00 / r2;             // IL_09B1-09BC
                        accX += f;                                    // IL_09BE
                        accZ += f;                                    // IL_09C5 (same value to both)
                    }
                    if (d10 < r2)
                    {
                        int f = delta - delta * d10 / r2;
                        accX -= f;                                    // IL_09FA
                    }
                    if (d01 < r2)
                    {
                        int f = delta - delta * d01 / r2;
                        accZ -= f;                                    // IL_0A2F
                    }
                }

                // ── X velocity (IL_0A4E-0BAC) ────────────────────────
                if (x != _last)
                {
                    b = src[idx + 1];
                    terrainB = _terrain[idx + 1];

                    // Apply the same decay to B. At the next x, B becomes A as it is, so this
                    // gives exactly one application per cell (IL_0A72-0ACD).
                    if (b.Height != 0) { b.Height = (ushort)(b.Height - evaporation); EvapRemoved += evaporation; }
                    if (b.Pollution != 0) b.Pollution = (ushort)(b.Pollution - pollutionDecay);
                    if (b.Pollution > b.Height) b.Pollution = b.Height;

                    // The drop in the water surface (1/64 m). **The external force is added
                    // here** —— which is how it "makes a mound of water appear without adding
                    // any volume".
                    int diff = terrainA + accX + a.Height - terrainB - b.Height;   // IL_0ACD-0AE5

                    int v = a.VelocityX;
                    if (v > 0) v = (_rng.Int32(DampOne) + v * DampKeep) >> DampShift;        // IL_0AF8-0B0D
                    if (v < 0) v = -((_rng.Int32(DampOne) - v * DampKeep) >> DampShift);     // IL_0B17-0B2D

                    // ★ Add a quarter of the drop. **The random number is there purely to round
                    //   the fractional part stochastically**; the expected value is exactly
                    //   diff/4. This is the acceleration for one frame.
                    if (diff > 0) v += (diff + _rng.Int32(4)) >> 2;                          // IL_0B37-0B47
                    else if (diff < 0) v -= (_rng.Int32(4) - diff) >> 2;                     // IL_0B56-0B66

                    // ★★ **The water-depth cap.** Water that is not there cannot be moved, nor
                    //   can it dig into the neighbour. This is why a tsunami does not grow in
                    //   shallow sea (IL_0B68-0B96).
                    if (v > a.Height) v = a.Height;
                    if (v < -b.Height) v = -b.Height;

                    a.VelocityX = (short)Clamp(v, -32766, 32766);                            // IL_0B98-0BAC
                }

                // ── Z velocity (IL_0BB1-0CD7). Identical to X instruction for instruction ──────
                if (z != _last)
                {
                    Cell down = src[idx + _n];               // (x, z+1)
                    int terrainDown = _terrain[idx + _n];

                    // ★ Here it is **only the evaporation of the height**. There is no pollution
                    //   decay and no clamp (IL_0BDB-0BF8). This copy is throwaway and never
                    //   written back, and the game does the same.
                    if (down.Height != 0) down.Height = (ushort)(down.Height - evaporation);

                    int diff = terrainA + accZ + a.Height - terrainDown - down.Height;       // IL_0BF8-0C10

                    int v = a.VelocityZ;
                    if (v > 0) v = (_rng.Int32(DampOne) + v * DampKeep) >> DampShift;
                    if (v < 0) v = -((_rng.Int32(DampOne) - v * DampKeep) >> DampShift);

                    if (diff > 0) v += (diff + _rng.Int32(4)) >> 2;
                    else if (diff < 0) v -= (_rng.Int32(4) - diff) >> 2;

                    if (v > a.Height) v = a.Height;
                    if (v < -down.Height) v = -down.Height;

                    a.VelocityZ = (short)Clamp(v, -32766, 32766);                            // IL_0CC3-0CD7
                }

                // ── Capping the outflow / inflow (IL_0CDC-0FB2) ──────────────
                //   ★ **All four directions are scaled down together** so that their sum does
                //     not exceed the water depth. Omit this and more water than the depth
                //     leaves the cell, and mass conservation breaks.
                Cell up = new Cell();                        // (x, z-1)
                if (z != 0) up = dst[idx - _n];              // ★ read from the dst side (IL_0CEB)

                int outflow = 0;   // loc107
                int inflow = 0;    // loc108

                if (left.VelocityX < 0) outflow -= left.VelocityX; else inflow += left.VelocityX;
                if (up.VelocityZ < 0) outflow -= up.VelocityZ; else inflow += up.VelocityZ;
                if (a.VelocityX > 0) outflow += a.VelocityX; else inflow -= a.VelocityX;
                if (a.VelocityZ > 0) outflow += a.VelocityZ; else inflow -= a.VelocityZ;

                if (outflow > a.Height)                                                       // IL_0DAE
                {
                    // ★★ The scaling is not a plain v*h/out. It includes the term
                    //   <c>((out-1) & maskN)</c>, **which switches between rounding up and
                    //   rounding down according to the step phase** (IL_0DCD-0DCF and others).
                    //   Rounding up one direction per step in turn cancels the tendency for
                    //   the rounding of the scaling to lose water preferentially in one
                    //   direction.
                    if (x != 0 && left.VelocityX < 0)
                    {
                        left.VelocityX = (short)(-((((outflow - 1) & mask0)
                                                    - left.VelocityX * a.Height) / outflow));
                        dst[idx - 1] = left;                  // reach **back** and fix the settled left neighbour
                    }
                    if (z != 0 && up.VelocityZ < 0)
                    {
                        up.VelocityZ = (short)(-((((outflow - 1) & mask2)
                                                  - up.VelocityZ * a.Height) / outflow));
                        dst[idx - _n] = up;
                    }
                    if (a.VelocityX > 0)
                        a.VelocityX = (short)(((((outflow - 1) & mask1))
                                               + a.VelocityX * a.Height) / outflow);
                    if (a.VelocityZ > 0)
                        a.VelocityZ = (short)(((((outflow - 1) & mask3))
                                               + a.VelocityZ * a.Height) / outflow);
                }

                int room = 65535 - a.Height;                                                  // IL_0EA1
                if (inflow > room)
                {
                    if (x != 0 && left.VelocityX > 0)
                    {
                        left.VelocityX = (short)(((((inflow - 1) & mask0))
                                                  + left.VelocityX * room) / inflow);
                        dst[idx - 1] = left;
                    }
                    if (z != 0 && up.VelocityZ > 0)
                    {
                        up.VelocityZ = (short)(((((inflow - 1) & mask2))
                                                + up.VelocityZ * room) / inflow);
                        dst[idx - _n] = up;
                    }
                    if (a.VelocityX < 0)
                        a.VelocityX = (short)(-((((inflow - 1) & mask1)
                                                 - a.VelocityX * room) / inflow));
                    if (a.VelocityZ < 0)
                        a.VelocityZ = (short)(-((((inflow - 1) & mask3)
                                                 - a.VelocityZ * room) / inflow));
                }

                dst[idx] = a;                                                                 // IL_0FB2

                // Shift the window by one cell (IL_0FC2-0FD8).
                left = a;
                a = b;
                terrainA = terrainB;
                idx++;
            }
        }

        /// <summary>
        /// The <b>mass transfer pass</b> (one row). IL_0FEC-1537.
        ///
        /// ★★ <b>The velocity v *is* "the water column moved in one frame"</b>.
        ///   At IL_1137 it reads <c>m = a.m_velocityX</c> as is, and at IL_11A6 / IL_11BE it
        ///   makes <c>a.m_height - m</c> / <c>b.m_height + m</c>. **There is no factor and no
        ///   time step.** So the unit of velocity is 1/64 m, the same as height, and the limit
        ///   of 32766 means "at most a 512 m water column can be handed to the neighbour in one
        ///   frame" (in practice it is capped by the water depth).
        ///
        /// ★ This pass is an <b>in-place update</b> (Gauss-Seidel). Along x it sees the result
        ///   of the previous cell, and along z it sees the water pushed in by the previous row.
        ///   Turning it into a Jacobi update that writes to a separate array makes it a
        ///   different thing entirely.
        ///
        /// ★ The velocity is <b>not zeroed</b> after the transfer. It survives into the next
        ///   frame, where the damping and the drop are applied on top of it —— that is the
        ///   inertia, and the reason the wave keeps running.
        /// </summary>
        private void TransferRow(int z, Cell[] buf, int maskPollution)
        {
            int i = z * _n;

            for (int x = 0; x <= _last; x++, i++)
            {
                Cell a = buf[i];

                // ── X direction (IL_110F-12F4) ──────────────────────────
                if (x != _last)
                {
                    if (a.VelocityX > 0)
                    {
                        Cell b = buf[i + 1];
                        int m = a.VelocityX;

                        if (a.Pollution != 0 && a.Height != 0)
                        {
                            int p = (m * a.Pollution + ((a.Height - 1) & maskPollution)) / a.Height;
                            a.Pollution = (ushort)(a.Pollution - p);
                            b.Pollution = (ushort)(b.Pollution + p);
                        }

                        if (a.Height < m) SatLost += m - a.Height;
                        if (b.Height + m > 65535) SatGained += b.Height + m - 65535;
                        a.Height = (ushort)Math.Max(a.Height - m, 0);
                        b.Height = (ushort)Math.Min(b.Height + m, 65535);

                        buf[i] = a;
                        buf[i + 1] = b;
                    }
                    else if (a.VelocityX < 0)
                    {
                        Cell b = buf[i + 1];
                        int m = -a.VelocityX;

                        if (b.Pollution != 0 && b.Height != 0)
                        {
                            int p = (m * b.Pollution + ((b.Height - 1) & maskPollution)) / b.Height;
                            a.Pollution = (ushort)(a.Pollution + p);
                            b.Pollution = (ushort)(b.Pollution - p);
                        }

                        if (b.Height < m) SatLost += m - b.Height;
                        if (a.Height + m > 65535) SatGained += a.Height + m - 65535;
                        a.Height = (ushort)Math.Min(a.Height + m, 65535);
                        b.Height = (ushort)Math.Max(b.Height - m, 0);

                        buf[i] = a;
                        buf[i + 1] = b;
                    }
                }

                // ── Z direction (IL_12F9-14EA). The neighbour is i + N (i + 1081 in the game) ─────
                if (z != _last)
                {
                    if (a.VelocityZ > 0)
                    {
                        Cell c = buf[i + _n];
                        int m = a.VelocityZ;

                        if (a.Pollution != 0 && a.Height != 0)
                        {
                            int p = (m * a.Pollution + ((a.Height - 1) & maskPollution)) / a.Height;
                            a.Pollution = (ushort)(a.Pollution - p);
                            c.Pollution = (ushort)(c.Pollution + p);
                        }

                        if (a.Height < m) SatLost += m - a.Height;
                        if (c.Height + m > 65535) SatGained += c.Height + m - 65535;
                        a.Height = (ushort)Math.Max(a.Height - m, 0);
                        c.Height = (ushort)Math.Min(c.Height + m, 65535);

                        buf[i] = a;
                        buf[i + _n] = c;
                    }
                    else if (a.VelocityZ < 0)
                    {
                        Cell c = buf[i + _n];
                        int m = -a.VelocityZ;

                        if (c.Pollution != 0 && c.Height != 0)
                        {
                            int p = (m * c.Pollution + ((c.Height - 1) & maskPollution)) / c.Height;
                            a.Pollution = (ushort)(a.Pollution + p);
                            c.Pollution = (ushort)(c.Pollution - p);
                        }

                        if (c.Height < m) SatLost += m - c.Height;
                        if (a.Height + m > 65535) SatGained += a.Height + m - 65535;
                        a.Height = (ushort)Math.Min(a.Height + m, 65535);
                        c.Height = (ushort)Math.Max(c.Height - m, 0);

                        buf[i] = a;
                        buf[i + _n] = c;
                    }
                }
            }
        }

        /// <summary>A stand-in for Mathf.Clamp(int,int,int).</summary>
        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
