using System;
using System.Collections.Generic;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <see cref="WaterField"/> のうち<b>水を動かす 2 パス</b>。
    /// ここが <c>SimulateWater</c> の心臓部で、IL_0670-0FE7（流速）と
    /// IL_0FEC-1537（輸送）に対応する。
    /// </summary>
    internal sealed partial class WaterField
    {
        // ── 減衰の定数（IL_0305-0316）─────────────────────────────
        //   loc41 = 11、loc42 = 1 << (11 & 31) = 2048、loc43 = 2047。
        //   毎フレーム 2047/2048 ≒ 0.9995 倍。**ほとんど減らない**ので、
        //   一度立った波は数千フレーム走り続ける。
        private const int DampShift = 11;
        private const int DampOne = 1 << DampShift;   // 2048
        private const int DampKeep = DampOne - 1;     // 2047

        // ── ★★ このソルバの波の速さ（このツールで実測して確かめた）─────────
        //
        //   流速の更新は  v += (水面の落差)/4     （IL_0B37-0B47、係数は 1/4 固定）
        //   輸送は        h -= v, h_next += v      （IL_11A6 / 11BE、係数なし）
        //
        //   セルとフレームを単位に取ると、これはそのまま波動方程式の食い違い格子で
        //
        //       c^2 = 1/4   ->   c = 0.5 セル / フレーム
        //
        //   ★ **水深に依らない。** 実際の浅水波は sqrt(g*h) で深さに効くが、
        //     ゲームのソルバは深さを<b>振幅の頭打ち</b>（v <= m_height）にしか使わない。
        //
        //   ★ 実測（2026-08-30 に測り直した）。
        //     **前の測定（512 格子、frame 420 の 1.98 km -> frame 660 の 3.89 km）は
        //     使ってはいけない** —— 512 格子だと外周の輪は中心から 4.10 km しかなく、
        //     3.89 km の「輪」は境界に貼り付いた反射であって自由に走る波頭ではない。
        //
        //     実機と同じ 1081 格子（境界まで 8.64 km）で、波頭が 3.63 km から
        //     7.57 km まで走る frame 720 -> 1199 を測る:
        //       3940 m / 479 フレーム = 8.22 m/フレーム = **0.514 セル/フレーム**。
        //     閾値（+0.5 / +1.0 / +2.0 m）と外力（drive 800 / 3200 / 12800）を変えても
        //     2 km -> 3 km の区間は 0.51 - 0.55 セル/フレームで動かない。
        //     水深 40 m と 150 m は**到達フレームまで完全に一致**する。
        //
        //   ゲーム速度 1（60 フレーム/実秒）なら 8 m/フレーム = **480 m/s**。
        //   水深 40 m の本物の津波は 20 m/s なので、**ゲームの水は約 24 倍速い**。
        //   津波の到達時間を「実物どおり」に語ってはいけない。

        /// <summary>
        /// <b>流速パス</b>（1 行ぶん）。IL_0670-0FE7。
        ///
        /// <c>src</c> を読んで <c>dst</c> に書く。ただし<b>上隣（z-1）だけは
        /// <c>dst</c> から読む</b>（IL_0CEB）—— その行はもう書き終わっているからで、
        /// 流量制限が上隣の流速を<b>遡って書き換える</b>ために必要。
        ///
        /// 1 セルあたりの流れ:
        /// <list type="number">
        /// <item>蒸発と汚染の減衰（IL_07C8-0823）</item>
        /// <item>外力（TYPE_IMPACT）を accX / accZ に積む（IL_0845-0A49）</item>
        /// <item>X の流速更新（IL_0ACD-0BAC）</item>
        /// <item>Z の流速更新（IL_0BB1-0CD7）</item>
        /// <item>流出／流入の頭打ち（IL_0D00-0FB2）</item>
        /// </list>
        /// </summary>
        private void VelocityRow(int z, Cell[] src, Cell[] dst, List<Impulse> impulses,
                                 ushort evaporation, ushort pollutionDecay,
                                 int mask0, int mask1, int mask2, int mask3)
        {
            int waveCount = (impulses == null) ? 0 : impulses.Count;

            int idx = z * _n;

            // 左隣（x-1）。行頭では中身ゼロ（IL_07A9 の initobj）。
            Cell left = new Cell();

            // セル A（x, z）と、その地形。**行のあいだ 1 セルずつずらして持ち回る**ので、
            // 各セルは 1 フレームに 1 回しか src から読まれず、減衰も 1 回しか掛からない。
            Cell a = src[idx];
            int terrainA = _terrain[idx];

            // 行頭ぶんの減衰（IL_07C8-0823）。以降のセルは B として読むときに掛ける。
            if (a.Height != 0) { a.Height = (ushort)(a.Height - evaporation); EvapRemoved += evaporation; }
            if (a.Pollution != 0) a.Pollution = (ushort)(a.Pollution - pollutionDecay);
            if (a.Pollution > a.Height) a.Pollution = a.Height;

            for (int x = 0; x <= _last; x++)
            {
                Cell b = new Cell();        // セル B（x+1, z）
                int terrainB = 0;
                int accX = 0;               // loc73: X の傾きに足す外力
                int accZ = 0;               // loc74: Z の傾きに足す外力

                // ── 外力（IL_0845-0A49）────────────────────────────
                for (int w = 0; w < waveCount; w++)
                {
                    Impulse imp = impulses[w];

                    // bbox。**上側は +1 まで**（差分を取るぶんの余白、IL_0862 / 089E）。
                    if (x < imp.MinX) continue;
                    if (x > imp.MaxX + 1) continue;
                    if (z < imp.MinZ) continue;
                    if (z > imp.MaxZ + 1) continue;

                    // R は X 方向の張り出しだけで決まる（IL_08BD-091E）。Z は見ない。
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

                    // ★ 3 つの項は**それぞれ独立に** d2 < R2 で守られる（IL_0990 / 09CC / 0A01）。
                    //   外れた項は「対を飛ばす」のではなく**その項だけ 0 になる**。
                    if (d00 < r2)
                    {
                        // f = delta - delta*d2/R2。割り算は C# の int 除算＝0 方向へ切り捨て。
                        // delta が負のときここの丸めの向きが効くので、shift で代用しないこと。
                        int f = delta - delta * d00 / r2;             // IL_09B1-09BC
                        accX += f;                                    // IL_09BE
                        accZ += f;                                    // IL_09C5（同じ値を両方へ）
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

                // ── X の流速（IL_0A4E-0BAC）────────────────────────
                if (x != _last)
                {
                    b = src[idx + 1];
                    terrainB = _terrain[idx + 1];

                    // B にも同じ減衰を掛ける。次の x で B がそのまま A になるので、
                    // これで各セルちょうど 1 回（IL_0A72-0ACD）。
                    if (b.Height != 0) { b.Height = (ushort)(b.Height - evaporation); EvapRemoved += evaporation; }
                    if (b.Pollution != 0) b.Pollution = (ushort)(b.Pollution - pollutionDecay);
                    if (b.Pollution > b.Height) b.Pollution = b.Height;

                    // 水面の落差（1/64 m）。**外力はここに足される** ——
                    // だから「体積を足さずに水の山があるように見せる」ことになる。
                    int diff = terrainA + accX + a.Height - terrainB - b.Height;   // IL_0ACD-0AE5

                    int v = a.VelocityX;
                    if (v > 0) v = (_rng.Int32(DampOne) + v * DampKeep) >> DampShift;        // IL_0AF8-0B0D
                    if (v < 0) v = -((_rng.Int32(DampOne) - v * DampKeep) >> DampShift);     // IL_0B17-0B2D

                    // ★ 落差の 1/4 を足す。**乱数は端数を確率的に丸めるためだけ**にあり、
                    //   期待値は diff/4 ちょうど。1 フレームの加速度がこれ。
                    if (diff > 0) v += (diff + _rng.Int32(4)) >> 2;                          // IL_0B37-0B47
                    else if (diff < 0) v -= (_rng.Int32(4) - diff) >> 2;                     // IL_0B56-0B66

                    // ★★ **水深の頭打ち。** 無い水は動かせないし、相手を掘ることもできない。
                    //   浅い海で津波が伸びない原因はここ（IL_0B68-0B96）。
                    if (v > a.Height) v = a.Height;
                    if (v < -b.Height) v = -b.Height;

                    a.VelocityX = (short)Clamp(v, -32766, 32766);                            // IL_0B98-0BAC
                }

                // ── Z の流速（IL_0BB1-0CD7）。X と命令単位で同型 ──────
                if (z != _last)
                {
                    Cell down = src[idx + _n];               // (x, z+1)
                    int terrainDown = _terrain[idx + _n];

                    // ★ こちらは**高さの蒸発だけ**。汚染の減衰も clamp も無い（IL_0BDB-0BF8）。
                    //   この写しは使い捨てで書き戻さないので、実機もそうなっている。
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

                // ── 流出／流入の頭打ち（IL_0CDC-0FB2）──────────────
                //   ★ 4 方向の合計が水深を超えないように**全部まとめて按分する**。
                //     ここを省くとセルから水深以上の水が出ていき、質量が壊れる。
                Cell up = new Cell();                        // (x, z-1)
                if (z != 0) up = dst[idx - _n];              // ★ dst 側から読む（IL_0CEB）

                int outflow = 0;   // loc107
                int inflow = 0;    // loc108

                if (left.VelocityX < 0) outflow -= left.VelocityX; else inflow += left.VelocityX;
                if (up.VelocityZ < 0) outflow -= up.VelocityZ; else inflow += up.VelocityZ;
                if (a.VelocityX > 0) outflow += a.VelocityX; else inflow -= a.VelocityX;
                if (a.VelocityZ > 0) outflow += a.VelocityZ; else inflow -= a.VelocityZ;

                if (outflow > a.Height)                                                       // IL_0DAE
                {
                    // ★★ 按分は単純な v*h/out ではない。<c>((out-1) & maskN)</c> という
                    //   **ステップ位相で切り上げ／切り捨てを切り替える項**が入る（IL_0DCD-0DCF ほか）。
                    //   4 方向を 1 ステップずつ順に切り上げることで、按分の丸めで
                    //   水が一方向へ偏って減るのを打ち消している。
                    if (x != 0 && left.VelocityX < 0)
                    {
                        left.VelocityX = (short)(-((((outflow - 1) & mask0)
                                                    - left.VelocityX * a.Height) / outflow));
                        dst[idx - 1] = left;                  // 既に確定した左隣を**遡って**直す
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

                // 窓を 1 セルずらす（IL_0FC2-0FD8）。
                left = a;
                a = b;
                terrainA = terrainB;
                idx++;
            }
        }

        /// <summary>
        /// <b>質量の輸送パス</b>（1 行ぶん）。IL_0FEC-1537。
        ///
        /// ★★ <b>流速 v は「1 フレームに動く水柱そのもの」</b>である。
        ///   IL_1137 で <c>m = a.m_velocityX</c> をそのまま読み、IL_11A6 / IL_11BE で
        ///   <c>a.m_height - m</c> / <c>b.m_height + m</c> にする。**係数も時間刻みも無い。**
        ///   だから流速の単位は高さと同じ 1/64 m で、上限 32766 は「1 フレームに
        ///   最大 512 m の水柱を隣へ渡せる」を意味する（実際は水深で頭打ちされる）。
        ///
        /// ★ このパスは<b>その場更新</b>（Gauss-Seidel）である。x では 1 つ前の結果を、
        ///   z では 1 行前が押し込んだ水を見る。別配列へ書く Jacobi にすると別物になる。
        ///
        /// ★ 流速は輸送後も<b>ゼロにされない</b>。次フレームまで残り、そこで減衰と
        ///   落差が乗る —— これが慣性であり、波が走り続ける理由。
        /// </summary>
        private void TransferRow(int z, Cell[] buf, int maskPollution)
        {
            int i = z * _n;

            for (int x = 0; x <= _last; x++, i++)
            {
                Cell a = buf[i];

                // ── X 方向（IL_110F-12F4）──────────────────────────
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

                // ── Z 方向（IL_12F9-14EA）。隣は i + N（実機は i + 1081）─────
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

        /// <summary>Mathf.Clamp(int,int,int) の代役。</summary>
        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
