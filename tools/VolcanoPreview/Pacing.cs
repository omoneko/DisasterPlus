using System;
using System.Globalization;
using System.IO;
using System.Text;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.VolcanoPreview
{
    /// <summary>
    /// **隆起が画面に出る刻み**をゲームを起動せずに測って描く。
    ///
    /// 実機の指摘⑤「噴火のアニメーションをもっとスムーズに（現在は断続的な
    /// せり上がりです）」への確認。**目に見える 1 段は「1 tick の上昇量」ではない** ——
    /// <c>RawHeights</c> に書いても <c>UpdateArea</c> が流すまで画面は変わらないので、
    /// 見える 1 段は「そのセルが再び流されるまでの上昇量」である。
    ///
    /// ここでは <c>UpliftSchedule</c> / <c>VolcanoRelief</c> / <c>TileSplit</c> /
    /// <c>UpliftFlushPlan</c> という**⑤が実際に使っている Core の実物**をそのまま回し、
    /// セルごとに「最後に流された高さ」を持って、フレーム単位で追う。
    /// 書き直した近似ではない。
    /// </summary>
    internal static class Pacing
    {
        /// <summary>1 ゲーム内分あたりの sim フレーム数（DAYTIME_FRAMES 65536 / 1440）。</summary>
        private const float FramesPerMinute = 65536f / 1440f;

        /// <summary>隆起にかけるゲーム内分（<c>ModSettings.VolcanoUpliftMinutes</c> の既定値）。</summary>
        private const int UpliftMinutes = 30;

        private const int BeforeIntervalFrames = 16;   // 2026-08-20 より前
        private const int AfterIntervalFrames = 4;     // 現在

        private const float RawUnitsPerMetre = UpliftSchedule.RawUnitsPerMetre;

        /// <summary>1 回の実行の結果。</summary>
        private sealed class Run
        {
            public int IntervalFrames;
            public int TotalTicks;
            public float RiseMetresPerTick;

            /// <summary>フレームごとの「画面に出ている」山頂の高さ（m）。</summary>
            public float[] SummitByFrame;

            /// <summary>フレームごとの「画面に出ている」中腹（0.55R）の高さ（m）。</summary>
            public float[] FlankByFrame;

            public float MaxSummitStep;
            public float MaxFlankStep;
            public int SummitUpdates;
            public int FlankUpdates;
            public long TotalFlushedCells;
            public int Flushes;
        }

        internal static void Report(string dir, StringBuilder log)
        {
            const VolcanoForm form = VolcanoForm.Strato;
            const float radius = 1200f;    // ModSettings.VolcanoRadius の既定値
            const float height = 600f;     // ModSettings.VolcanoHeight の既定値

            Run before = Simulate(form, radius, height, BeforeIntervalFrames, true);
            Run after = Simulate(form, radius, height, AfterIntervalFrames, false);

            int frames = (int)(UpliftMinutes * FramesPerMinute);

            log.AppendLine("## uplift pacing (strato, R = " + radius + " m, H = " + height
                           + " m, " + UpliftMinutes + " in-game minutes)");
            log.AppendLine("  in-game duration is UNCHANGED: " + UpliftMinutes
                           + " in-game minutes = " + frames + " sim frames");
            log.AppendLine("  (1 in-game minute = 45.51 sim frames; wall clock depends on the game "
                           + "speed and the fixed-update rate)");
            log.AppendLine();
            log.AppendLine("                        | before | after");
            log.AppendLine("  uplift interval frames| " + Pad(before.IntervalFrames)
                           + " | " + Pad(after.IntervalFrames));
            log.AppendLine("  ticks                 | " + Pad(before.TotalTicks)
                           + " | " + Pad(after.TotalTicks));
            log.AppendLine("  rise per tick (m)     | " + Pad(before.RiseMetresPerTick)
                           + " | " + Pad(after.RiseMetresPerTick));
            log.AppendLine("  VISIBLE step, summit  | " + Pad(before.MaxSummitStep)
                           + " | " + Pad(after.MaxSummitStep) + "   <- the complaint");
            log.AppendLine("  VISIBLE step, flank   | " + Pad(before.MaxFlankStep)
                           + " | " + Pad(after.MaxFlankStep));
            log.AppendLine("  visible updates, summit| " + Pad(before.SummitUpdates)
                           + " | " + Pad(after.SummitUpdates));
            log.AppendLine("  visible updates, flank | " + Pad(before.FlankUpdates)
                           + " | " + Pad(after.FlankUpdates));
            log.AppendLine("  UpdateArea calls      | " + Pad(before.Flushes)
                           + " | " + Pad(after.Flushes));
            log.AppendLine("  flushed cells / frame | "
                           + Pad(before.TotalFlushedCells / (float)frames)
                           + " | " + Pad(after.TotalFlushedCells / (float)frames));
            log.AppendLine("  biggest single flush  | " + Pad(TileSplit.MaxPassedCells)
                           + " | " + Pad(TileSplit.MaxPassedCells)
                           + "   (cap 10000, side cap 128)");
            log.AppendLine();

            // ★ 間隔をいくつにするかは費用と刻みの取引である。**選んだ理由を数字で残す。**
            //   「流したセル数 / フレーム」がそのまま UpdateArea の平均負荷で、
            //   1 回の UpdateArea は対象矩形を detail 解像度（raw 1 セルにつき 4x4）で
            //   走査して 1 セルあたり SmoothSample を 5 回呼ぶ（IL facts A-1）。
            log.AppendLine("  interval | ticks | rise/tick m | VISIBLE step m | flushed cells/frame");
            int[] candidates = { 16, 8, 4, 2, 1 };
            for (int i = 0; i < candidates.Length; i++)
            {
                Run r = Simulate(form, radius, height, candidates[i], false);
                log.AppendLine("  " + Pad(candidates[i]) + " | " + Pad(r.TotalTicks)
                               + " | " + Pad(r.RiseMetresPerTick)
                               + " | " + Pad(r.MaxSummitStep)
                               + " | " + Pad(r.TotalFlushedCells / (float)frames));
            }
            log.AppendLine();

            Draw(dir, before, after, height, frames);
        }

        /// <summary>
        /// 1 通りぶん回す。<paramref name="wholeFootprint"/> が true なら以前の挙動
        /// （フットプリント全体をタイル総当たり）、false なら今の挙動
        /// （<see cref="UpliftFlushPlan"/> ＝ 変わった矩形だけ）。
        /// </summary>
        private static Run Simulate(VolcanoForm form, float radius, float height,
                                    int intervalFrames, bool wholeFootprint)
        {
            int minX, minZ, maxX, maxZ;
            if (!TileSplit.CellRangeFor(0f, 0f, radius, out minX, out minZ, out maxX, out maxZ))
            {
                throw new InvalidOperationException("the footprint covers no cell");
            }

            int width = maxX - minX + 1;
            int depth = maxZ - minZ + 1;

            // ⑤と同じ焼き方（VolcanoUplift.BakeProfile）。
            uint seed = DeterministicRandom.Hash(0u, 0u);
            var relief = VolcanoRelief.For(form, seed, 1f);

            var profile = new float[width * depth];
            for (int z = 0; z < depth; z++)
            {
                float worldZ = (minZ + z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                for (int x = 0; x < width; x++)
                {
                    float worldX = (minX + x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    if (worldX * worldX + worldZ * worldZ > radius * radius) continue;
                    profile[z * width + x] = relief.ProfileAt(worldX, worldZ, radius, height);
                }
            }

            int requestedTicks = (int)(UpliftMinutes * FramesPerMinute / intervalFrames);
            int totalTicks = UpliftSchedule.TotalTicksFor(height, requestedTicks);

            var run = new Run
            {
                IntervalFrames = intervalFrames,
                TotalTicks = totalTicks,
                RiseMetresPerTick = height / totalTicks,
            };

            int frames = (int)(UpliftMinutes * FramesPerMinute);
            // 目標に届いたあとの流し切りも見たいので、少し長めに回す。
            int simFrames = frames + intervalFrames * 8;
            run.SummitByFrame = new float[simFrames + 1];
            run.FlankByFrame = new float[simFrames + 1];

            var raw = new ushort[width * depth];        // 書いた値（まだ画面に出ていない）
            var shown = new ushort[width * depth];      // 最後に UpdateArea で流された値

            int summit = SummitIndex(minX, minZ, width, depth);
            int flank = FlankIndex(minX, minZ, width, depth, radius, 0.55f);

            var plan = new UpliftFlushPlan();
            int footprintTiles = TileSplit.TileCountFor(minX, minZ, maxX, maxZ);
            int footprintCursor = 0;

            float lastSummit = 0f;
            float lastFlank = 0f;
            int tick = 0;

            for (int frame = 0; frame <= simFrames; frame++)
            {
                if (frame % intervalFrames == 0)
                {
                    float progress = UpliftSchedule.ProgressAt(tick, totalTicks);

                    bool dirtyValid = false;
                    int dMinX = 0, dMinZ = 0, dMaxX = 0, dMaxZ = 0;

                    for (int z = 0; z < depth; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int cell = z * width + x;
                            if (profile[cell] <= 0f) continue;

                            float grown = UpliftSchedule.GrowthMetresAt(profile[cell], height,
                                                                        progress);
                            ushort target = UpliftSchedule.RawTargetAt(0, grown, 1f);
                            if (raw[cell] == target) continue;

                            raw[cell] = target;

                            int cx = minX + x;
                            int cz = minZ + z;
                            if (!dirtyValid)
                            {
                                dirtyValid = true;
                                dMinX = cx; dMaxX = cx; dMinZ = cz; dMaxZ = cz;
                            }
                            else
                            {
                                if (cx < dMinX) dMinX = cx;
                                if (cx > dMaxX) dMaxX = cx;
                                if (cz < dMinZ) dMinZ = cz;
                                if (cz > dMaxZ) dMaxZ = cz;
                            }
                        }
                    }

                    int pMinX, pMinZ, pMaxX, pMaxZ;
                    bool flush;
                    if (wholeFootprint)
                    {
                        // 以前の挙動: 書いた範囲に関わらずフットプリントのタイルを総当たり。
                        flush = TileSplit.TileAt(footprintCursor, minX, minZ, maxX, maxZ,
                                                 out pMinX, out pMinZ, out pMaxX, out pMaxZ);
                        footprintCursor++;
                        if (footprintCursor >= footprintTiles) footprintCursor = 0;
                    }
                    else
                    {
                        flush = plan.Next(dirtyValid, dMinX, dMinZ, dMaxX, dMaxZ,
                                          out pMinX, out pMinZ, out pMaxX, out pMaxZ);
                    }

                    if (flush)
                    {
                        run.Flushes++;
                        run.TotalFlushedCells += (long)(pMaxX - pMinX + 1) * (pMaxZ - pMinZ + 1);
                        Copy(raw, shown, minX, minZ, width, depth, pMinX, pMinZ, pMaxX, pMaxZ);
                    }

                    if (tick < totalTicks) tick++;
                }

                float summitMetres = shown[summit] / RawUnitsPerMetre;
                float flankMetres = shown[flank] / RawUnitsPerMetre;

                run.SummitByFrame[frame] = summitMetres;
                run.FlankByFrame[frame] = flankMetres;

                float ds = summitMetres - lastSummit;
                if (ds > 0.0001f)
                {
                    run.SummitUpdates++;
                    if (ds > run.MaxSummitStep) run.MaxSummitStep = ds;
                }
                float df = flankMetres - lastFlank;
                if (df > 0.0001f)
                {
                    run.FlankUpdates++;
                    if (df > run.MaxFlankStep) run.MaxFlankStep = df;
                }
                lastSummit = summitMetres;
                lastFlank = flankMetres;
            }

            return run;
        }

        private static void Copy(ushort[] raw, ushort[] shown, int minX, int minZ,
                                 int width, int depth,
                                 int pMinX, int pMinZ, int pMaxX, int pMaxZ)
        {
            for (int cz = pMinZ; cz <= pMaxZ; cz++)
            {
                int z = cz - minZ;
                if (z < 0 || z >= depth) continue;
                for (int cx = pMinX; cx <= pMaxX; cx++)
                {
                    int x = cx - minX;
                    if (x < 0 || x >= width) continue;
                    shown[z * width + x] = raw[z * width + x];
                }
            }
        }

        private static int SummitIndex(int minX, int minZ, int width, int depth)
        {
            int x = TileSplit.CellOffset - minX;
            int z = TileSplit.CellOffset - minZ;
            if (x < 0) x = 0;
            if (x >= width) x = width - 1;
            if (z < 0) z = 0;
            if (z >= depth) z = depth - 1;
            return z * width + x;
        }

        private static int FlankIndex(int minX, int minZ, int width, int depth,
                                      float radius, float fraction)
        {
            int offset = (int)(radius * fraction / TileSplit.RawCellSizeMetres);
            int x = TileSplit.CellOffset + offset - minX;
            int z = TileSplit.CellOffset - minZ;
            if (x < 0) x = 0;
            if (x >= width) x = width - 1;
            if (z < 0) z = 0;
            if (z >= depth) z = depth - 1;
            return z * width + x;
        }

        // ── 描画 ────────────────────────────────────────────────

        private static void Draw(string dir, Run before, Run after, float height, int frames)
        {
            const int chartWidth = 1000;
            const int chartHeight = 300;
            const int gap = 20;

            int w = chartWidth;
            int h = chartHeight * 2 + gap;
            var rgb = new byte[w * h * 3];
            for (int i = 0; i < rgb.Length; i += 3) { rgb[i] = 16; rgb[i + 1] = 18; rgb[i + 2] = 24; }

            Chart(rgb, w, 0, chartWidth, chartHeight, before.SummitByFrame, after.SummitByFrame,
                  height, frames);
            Chart(rgb, w, chartHeight + gap, chartWidth, chartHeight,
                  before.FlankByFrame, after.FlankByFrame, height, frames);

            Png.Write(Path.Combine(dir, "uplift-pacing.png"), w, h, rgb);
        }

        /// <summary>白 = 以前（断続）、橙 = 現在。薄い灰 = 理想の直線。</summary>
        private static void Chart(byte[] rgb, int imageWidth, int top, int width, int height,
                                  float[] before, float[] after, float maxMetres, int frames)
        {
            float peak = 0f;
            for (int i = 0; i < before.Length; i++) if (before[i] > peak) peak = before[i];
            for (int i = 0; i < after.Length; i++) if (after[i] > peak) peak = after[i];
            if (peak <= 0f) peak = maxMetres;

            int span = frames + 1;

            // 理想（連続に上がったとき）の直線。
            for (int x = 0; x < width; x++)
            {
                int frame = (int)((long)x * span / width);
                float ideal = peak * frame / (float)frames;
                if (ideal > peak) ideal = peak;
                int y = top + height - 1 - (int)(ideal / peak * (height - 1));
                Set(rgb, imageWidth, x, y, 60, 64, 76);
            }

            Series(rgb, imageWidth, top, width, height, before, peak, span, 235, 235, 235);
            Series(rgb, imageWidth, top, width, height, after, peak, span, 255, 158, 66);
        }

        private static void Series(byte[] rgb, int imageWidth, int top, int width, int height,
                                   float[] values, float peak, int span,
                                   byte r, byte g, byte b)
        {
            int previousY = -1;
            for (int x = 0; x < width; x++)
            {
                int frame = (int)((long)x * span / width);
                if (frame >= values.Length) frame = values.Length - 1;

                float v = values[frame];
                int y = top + height - 1 - (int)(v / peak * (height - 1));

                if (previousY >= 0)
                {
                    int lo = Math.Min(previousY, y);
                    int hi = Math.Max(previousY, y);
                    for (int yy = lo; yy <= hi; yy++) Set(rgb, imageWidth, x, yy, r, g, b);
                }
                else
                {
                    Set(rgb, imageWidth, x, y, r, g, b);
                }
                previousY = y;
            }
        }

        private static void Set(byte[] rgb, int width, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || x >= width || y < 0) return;
            int i = (y * width + x) * 3;
            if (i < 0 || i + 2 >= rgb.Length) return;
            rgb[i] = r; rgb[i + 1] = g; rgb[i + 2] = b;
        }

        private static string Pad(float v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture).PadLeft(6);
        }

        private static string Pad(int v)
        {
            return v.ToString(CultureInfo.InvariantCulture).PadLeft(6);
        }

        private static string Pad(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture).PadLeft(6);
        }
    }
}
