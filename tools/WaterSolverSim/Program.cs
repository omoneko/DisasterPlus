using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Tools;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b>Runs the tsunami source through the solver without launching the game.</b>
    ///
    /// ── Why it is needed ─────────────────────────────────────────────
    ///
    /// All <c>tools/TsunamiPreview</c> could draw was <b>the input to the solver</b>.
    /// "How many metres that force raises the sea surface" <b>cannot be settled by reading
    /// the IL</b> —— the response depends on the water depth and the extent of the sea, as
    /// the comments in TsunamiSource also say (which is why it is built to measure in the
    /// game and raise the force accordingly).
    ///
    /// ★★ This tool <b>runs that "measure and raise" loop offline</b>.
    ///   Without launching the game and waiting 18 seconds, you get results immediately for
    ///   different forces and water depths.
    ///
    /// ── Usage ───────────────────────────────────────────────────
    ///
    /// <code>
    ///   dotnet run --project tools/WaterSolverSim -- docs/images/water
    ///   dotnet run --project tools/WaterSolverSim -- out --depth 60 --intensity 200 --frames 1800
    /// </code>
    ///
    /// ── How to read it ───────────────────────────────────────────────────
    ///
    /// <list type="bullet">
    /// <item><c>drive</c> ── the force currently applied (in <c>m_delta</c> units, unsigned).
    ///       <c>NextDrive</c> decides it for itself from the measurements.</item>
    /// <item><c>centre</c> ── the rise of the sea surface at the epicentre (m). This is
    ///       stage 1's target.</item>
    /// <item><c>peak</c> ── the radius and height of the highest <b>ring</b>. **This is the
    ///       wave that is running.** If the radius keeps growing it is propagating; if not,
    ///       it never got going.</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        /// <summary>Default grid side (cells). 512 x 16 m = 8.2 km square. The game uses
        /// 1081.</summary>
        private const int DefaultGridSize = 512;

        /// <summary>Sea level (m). The same as the game's default.</summary>
        private const float SeaLevelMetres = 40f;

        /// <summary>Interval at which the force is rebuilt (frames). The mod in the game does
        /// not touch it every frame either.</summary>
        private const int DriveInterval = 1;   // 1 Step() = 1 water step = the mod's rewrite interval

        /// <summary>Interval at which a table row is printed (frames).</summary>
        private const int PrintInterval = 60;

        /// <summary>A guide for game speed 1. 1 water step ~= 1 sim frame (measured from the
        /// IL).</summary>
        private const float FramesPerRealSecond = 60f / 64f;   // 1 water step = 64 sim frames

        private static int Main(string[] args)
        {
            string outDir = ".";
            float depth = 40f;
            byte intensity = 100;
            int frames = 1500;
            int pinnedDrive = -1;
            int shape = 0;             // 0 = use TsunamiSource.DriveAt as it is
            bool shelf = false;   // run on an offshore -> shelf -> shoreline -> land section
            float srcFrac = 0.5f;   // source X position (fraction of the grid). 0.1 is right at the map edge
            int radiusCells = TsunamiSource.RadiusCells;
            float totalSteps = TsunamiSource.TotalSteps;
            // ★ The outer ring is a rectangular Dirichlet boundary, so on a small board the
            //   reflections from the corners come back sooner. The game uses 1081 —— the only
            //   way to tell whether a structure you see is an artefact of the boundary is to
            //   change the grid and see whether the result is the same.
            int gridSize = DefaultGridSize;
            bool noPng = false;
            bool audit = false;
            int edgeIntensity = 0;   // >0 raises the DLC tsunami at the left edge (no impact force)
            int ringIntensity = 0;   // >0 puts a WaterSource at the epicentre and radiates concentrically
            int ringRadius = 80;     // radius of the epicentre circle (cells). 80 -> 1280 m
            long ringRate = 0;       // the flow rate needed to give that radius
            bool ringHold = false;   // keep the circle pinned to the target level at all times
            bool ringNoIn = false;   // never use intake (avoids the int32 overflow)
            int ringImpact = 0;      // >0 produces the drawback with TYPE_IMPACT (its maximum delta)
            float ringInR = 0f;      // >0 sets the intake circle's radius (m) separately
            int ringDurSteps = 256;  // length of the waveform (water steps). Vanilla is 256
            float ringCap = 0f;      // >0 caps the height of the crest (m)
            float landRise = 0.5f;   // slope of the land (m/cell). If it saturates, inundation distance cannot be measured
            int ringRepeat = 1;      // how many times to fire the waveform (second and third waves)
            int ringLine = 1;        // number of circles laid along the fault (1 = a point source)
            float ringDraw = 0f;     // >0 caps the depth of the drawback (m). Defaults to the same as ringCap
            float arrivalThreshold = float.NaN;   // ★ diagnostic. >0 measures the wave front's arrival time.         // ★ diagnostic. No counterpart in the game.

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--depth" && i + 1 < args.Length) { depth = ParseFloat(args[++i], depth); }
                else if (a == "--intensity" && i + 1 < args.Length) { intensity = (byte)ParseInt(args[++i], intensity); }
                else if (a == "--frames" && i + 1 < args.Length) { frames = ParseInt(args[++i], frames); }
                else if (a == "--drive" && i + 1 < args.Length) { pinnedDrive = ParseInt(args[++i], pinnedDrive); }
                else if (a == "--grid" && i + 1 < args.Length) { gridSize = ParseInt(args[++i], gridSize); }
                else if (a == "--shape" && i + 1 < args.Length) { shape = ParseInt(args[++i], shape); }
                else if (a == "--shelf") { shelf = true; }
                else if (a == "--srcx" && i + 1 < args.Length) { srcFrac = ParseFloat(args[++i], srcFrac); }
                else if (a == "--radius" && i + 1 < args.Length) { radiusCells = ParseInt(args[++i], radiusCells); }
                else if (a == "--steps" && i + 1 < args.Length) { totalSteps = ParseFloat(args[++i], totalSteps); }
                else if (a == "--audit") { audit = true; }
                else if (a == "--edge" && i + 1 < args.Length) { edgeIntensity = ParseInt(args[++i], 0); }
                else if (a == "--ring" && i + 1 < args.Length) { ringIntensity = ParseInt(args[++i], 0); }
                else if (a == "--ringr" && i + 1 < args.Length) { ringRadius = ParseInt(args[++i], ringRadius); }
                else if (a == "--ringhold") { ringHold = true; }
                else if (a == "--ringnoin") { ringNoIn = true; }
                else if (a == "--ringinr" && i + 1 < args.Length) { ringInR = ParseFloat(args[++i], ringInR); }
                else if (a == "--ringimpact" && i + 1 < args.Length) { ringImpact = ParseInt(args[++i], 0); }
                else if (a == "--ringdur" && i + 1 < args.Length) { ringDurSteps = ParseInt(args[++i], ringDurSteps); }
                else if (a == "--ringcap" && i + 1 < args.Length) { ringCap = ParseFloat(args[++i], ringCap); }
                else if (a == "--landrise" && i + 1 < args.Length) { landRise = ParseFloat(args[++i], landRise); }
                else if (a == "--ringrepeat" && i + 1 < args.Length) { ringRepeat = ParseInt(args[++i], ringRepeat); }
                else if (a == "--ringline" && i + 1 < args.Length) { ringLine = ParseInt(args[++i], ringLine); }
                else if (a == "--ringdraw" && i + 1 < args.Length) { ringDraw = ParseFloat(args[++i], ringDraw); }
                else if (a == "--arrival" && i + 1 < args.Length) { arrivalThreshold = ParseFloat(args[++i], arrivalThreshold); }
                else if (a == "--nopng") { noPng = true; }
                else if (!a.StartsWith("--")) { outDir = a; }
            }

            if (frames < 1) frames = 1;
            if (gridSize < 16) gridSize = 16;
            Directory.CreateDirectory(outDir);

            // ── Flat sea ──────────────────────────────────────────
            //
            // ★★ **A sea deeper than the sea level cannot be built.** The terrain is a ushort
            //   (1/64 m), so the sea bed bottoms out at elevation 0, and on a map with a sea
            //   level of 40 m the deepest sea that can be built is 40 m —— this is the game's
            //   own constraint (m_heightBuffer is a ushort).
            //   When the requested depth exceeds that, <b>raise the sea level along with it</b>.
            //   Silently running on a shallower sea leads to the misdiagnosis "I made it
            //   deeper but the wave does not grow".
            float seaLevel = Math.Max(SeaLevelMetres, depth);

            WaterField field = new WaterField(gridSize, seaLevel);

            // ★★ Placing a coast makes it possible to measure "large at the epicentre but only
            //    a storm surge at the coast". The shelf starts at 60% of the grid and the
            //    shoreline is at 85%.
            int shelfStart = (int)(gridSize * 0.60f);
            int shoreCell = (int)(gridSize * 0.85f);

            if (shelf) field.FillShelf(depth, shelfStart, shoreCell, landRise);
            else field.FillFlatSea(depth);

            // ★★ Allow the source to be moved towards the edge (2026-08-30). The outermost
            //    cells are a boundary that the solver pins to sea level, so **being close to
            //    it drains energy**.
            int centreX = (int)(gridSize * srcFrac);
            int centreZ = gridSize / 2;

            float target = 0f;
            int drive = (pinnedDrive >= 0) ? pinnedDrive : TsunamiSource.DriveUnitsFor(intensity, depth);

            // Initial total water volume. The baseline against which later rows show whether
            // "the solver created / destroyed water".
            // ★ By design the outer ring discards and creates water, so this will not be 0%.
            //   What we want to see is **whether it is running away**.
            //
            // ★★ The breakdown obtained with <c>--audit</c> (2026-08-30, drive 0-12800 /
            //   depths 5, 40 and 150 m / grids 512 and 1081 / up to 1500 frames):
            //   <b>dV is fully explained by "outer ring - evaporation", and resid is always
            //   0</b>. The saturation in <c>Math.Max(...,0)</c> / <c>Math.Min(...,65535)</c>
            //   <b>never fired once</b> (satLost = satGain = 0, and zero cells at 65535).
            //   The initial guess that "saturation is losing the water" is **wrong**.
            // ── The DLC tsunami (--edge) ──────────────────────────────
            //
            // ★★ **The mechanism is completely different from our own force** (the class doc
            //   of EdgeWave). It treats the entire left edge as the sea segment and raises the
            //   wave inwards along +X. The game's GetSeaSideLocation picks "the longest
            //   continuous sea segment", so on this section, where the whole left side is sea,
            //   that is exactly what happens.
            if (edgeIntensity > 0)
            {
                field.Edge = new EdgeWave
                {
                    OrigX = 0,
                    OrigZ = gridSize / 2,
                    DirX = 32768,
                    DirZ = 0,
                    MinX = 0,
                    MaxX = 0,
                    MinZ = 0,
                    MaxZ = gridSize - 1,
                    Delta = EdgeWave.DeltaFor(edgeIntensity),
                    Duration = EdgeWave.VanillaDuration,
                };

                drive = 0;   // no impact force. What we want to compare is the difference in mechanism.
                pinnedDrive = 0;

                Console.WriteLine("  ** DLC tsunami mode ** intensity " + edgeIntensity
                                  + " -> m_delta " + field.Edge.Delta + " units = "
                                  + (field.Edge.Delta / 64f).ToString("F1")
                                  + " m, driven along the WHOLE LEFT EDGE for "
                                  + (EdgeWave.VanillaDuration / EdgeWave.TimePerStep)
                                  + " water steps. The edge is a Dirichlet boundary, so this "
                                  + "MAKES water - it does not borrow it. No impact drive is used.");
            }

            // ── Concentric circles at the epicentre (--ring) ─────────────────────────────
            //
            // ★★ <b>The DLC's waveform is kept as it is</b>; only where it is placed moves to
            //   the epicentre. Phases where the level is below sea level take water in (the
            //   drawback), and phases above it put water out (the crest). See the class doc of
            //   <see cref="SourceDisc"/>.
            EdgeWave ringShape = null;

            if (ringIntensity > 0)
            {
                // The flow rate needed to give a radius of r [m]: rate = ((r - 10) / 0.4)^2
                float wantR = ringRadius * WaterField.CellSizeMetres;
                long rate = (long)Math.Pow((wantR - 10f) / 0.4f, 2.0);

                // ★★ Lay circles along the fault (a line source). With ringLine == 1 it is a
                //    point as before. The spacing is one radius —— overlap them too much and
                //    they fight over the same water, spread them too far and the wave does not
                //    merge into one.
                field.Sources = new SourceDisc[ringLine];
                int lineStep = ringRadius;
                for (int k = 0; k < ringLine; k++)
                {
                    field.Sources[k] = new SourceDisc
                    {
                        CellX = centreX,
                        CellZ = centreZ + (k - (ringLine - 1) / 2) * lineStep,
                    };
                }
                field.Source = field.Sources[0];

                ringShape = new EdgeWave
                {
                    Delta = EdgeWave.DeltaFor(ringIntensity),
                    Duration = ringDurSteps * EdgeWave.TimePerStep,
                };

                drive = 0;
                pinnedDrive = 0;

                Console.WriteLine("  ** EPICENTRE RING mode ** intensity " + ringIntensity
                                  + " -> m_delta " + ringShape.Delta + " units = "
                                  + (ringShape.Delta / 64f).ToString("F1") + " m. A "
                                  + "TYPE_NATURAL WaterSource of radius "
                                  + SourceDisc.RadiusMetres(rate).ToString("F0")
                                  + " m (rate " + rate + ") sits ON the epicentre and is "
                                  + "driven with the DLC's own waveform for "
                                  + (EdgeWave.VanillaDuration / EdgeWave.TimePerStep)
                                  + " water steps. It MAKES water on the crest and TAKES it "
                                  + "on the retreat, so the wave radiates concentrically.");

                // The flow rate is switched by phase on every step. Here we just remember it.
                ringRate = rate;
            }

            long baseVolume = TotalWaterUnits(field);

            Console.WriteLine("== WaterSolverSim : offline reproduction of SimulateWater ==");
            Console.WriteLine("  grid       " + gridSize + " x " + gridSize + " cells ("
                              + (gridSize * WaterField.CellSizeMetres / 1000f).ToString("F1") + " km square), "
                              + WaterField.CellSizeMetres.ToString("F0") + " m / cell");
            Console.WriteLine("  sea level  " + seaLevel.ToString("F0") + " m"
                              + (seaLevel > SeaLevelMetres
                                 ? (" (raised from " + SeaLevelMetres.ToString("F0")
                                    + " m: the seabed cannot go below elevation 0)")
                                 : "")
                              + ", depth " + depth.ToString("F0") + " m  -> water column "
                              + ((int)(depth * WaterField.UnitsPerMetre)) + " units, seabed at "
                              + (seaLevel - depth).ToString("F0") + " m");
            Console.WriteLine("  intensity  " + intensity + "  -> drive "
                              + TsunamiSource.DriveUnitsFor(intensity, depth) + " units");
            Console.WriteLine("  source     R = " + (TsunamiSource.RadiusCells + 1) + " cells ("
                              + ((TsunamiSource.RadiusCells + 1) * WaterField.CellSizeMetres / 1000f).ToString("F2")
                              + " km), " + TsunamiSource.MaxStackedWaves + " stacked waves");
            Console.WriteLine("  frames     " + frames + " ("
                              + (frames / FramesPerRealSecond).ToString("F0") + " real s)");
            Console.WriteLine("  drive      " + (pinnedDrive >= 0
                              ? ("pinned at " + pinnedDrive + " units (open loop)")
                              : ("closed loop, starts at " + drive + ", re-measured every "
                                 + DriveInterval + " frames while stage 1")));
            Console.WriteLine("  out        " + Path.GetFullPath(outDir));
            Console.WriteLine();
            Console.WriteLine("  frame  real s   drive  stage  centre    peak r    peak   0.5km    1km    2km    3km    4km     dV%");
            Console.WriteLine("  -----  ------  ------  -----  ------  --------  ------  ------  -----  -----  -----  -----  ------");

            List<Impulse> impulses = new List<Impulse>();
            HashSet<int> pngFrames = PickPngFrames(frames);

            // ── Arrival time of the wave front (diagnostic; no counterpart in the game) ────────────
            //   Remembers, for each radius, the first frame at which the radial mean surface
            //   exceeds the threshold. "The highest ring" jumps about because of the dip at
            //   the centre and grid noise, so use this instead to measure the wave speed.
            bool trackArrival = !float.IsNaN(arrivalThreshold) && arrivalThreshold > 0f;
            int[] arrival = null;
            if (trackArrival)
            {
                arrival = new int[gridSize / 2];
                for (int r = 0; r < arrival.Length; r++) arrival[r] = -1;
            }

            Stopwatch clock = Stopwatch.StartNew();

            // ★★ **Thinning out the printing is a trap.** (2026-08-30, the adjudicator)
            //    Printing only every 60 frames hides the troughs and peaks in between.
            //    It is the same pitfall as missing "green at step 420, +111 m at step 1080",
            //    so **keep the minimum and maximum scanned on every frame here.**
            float shoreMax = 0f;
            int shoreMaxFrame = -1;
            int floodCells = 0;

            float runningMin = float.MaxValue;
            float runningMax = float.MinValue;
            int runningMinFrame = -1;
            int runningMaxFrame = -1;
            int zeroWaterSteps = 0;

            for (int frame = 0; frame < frames; frame++)
            {
                // ── Rebuild the force (the same procedure as the mod in the game) ──────────
                {
                    if (shelf)
                    {
                        // ★ Track the rise above normal two cells short of the shoreline.
                        //   **This is "how many metres reached the town".**
                        float atShore = field.ColumnRiseMetres(shoreCell - 2, centreZ);
                        if (atShore > shoreMax) { shoreMax = atShore; shoreMaxFrame = frame; }

                        // How many cells it ran up onto the land (columns carrying at least
                        // 0.5 m of water).
                        // ★★ **Judge by the thickness of the water, not the elevation** (see
                        //    the doc of WaterDepthMetres).
                        for (int lx = shoreCell; lx < gridSize; lx++)
                        {
                            if (field.WaterDepthMetres(lx, centreZ) <= 0.5f) break;
                            int inland = lx - shoreCell + 1;
                            if (inland > floodCells) floodCells = inland;
                        }
                    }

                    float watched = field.SurfaceAboveSeaMetres(centreX, centreZ);
                    if (watched < runningMin) { runningMin = watched; runningMinFrame = frame; }
                    if (watched > runningMax) { runningMax = watched; runningMaxFrame = frame; }
                    if (watched <= -depth + 0.005f) zeroWaterSteps++;
                }

                if (frame % DriveInterval == 0)
                {
                    float observed = field.SurfaceAboveSeaMetres(centreX, centreZ);
                    if (float.IsNaN(observed) || float.IsInfinity(observed))
                        throw new InvalidOperationException("the water surface became NaN: frame " + frame);

                    // ★★ The closed loop was abandoned (2026-08-30). The solver's response
                    //    lags by about 100 steps, so measuring and raising every 8 steps always
                    //    winds up (reproduced and confirmed with this tool: 2000 -> 139,516
                    //    units, with the centre at -40 m, i.e. dug down to the sea bed).
                    //    We now use **an open-loop constant measured and fixed with this tool**.
                    if (observed > 1e9f) throw new InvalidOperationException("runaway");

                    int total = DriveTotal(shape, frame, totalSteps, drive);

                    impulses.Clear();
                    for (int w = 0; w < TsunamiSource.MaxStackedWaves; w++)
                    {
                        int d = TsunamiSource.DeltaForWave(w, total);
                        if (d == 0) continue;
                        impulses.Add(Impulse.Round(centreX, centreZ, radiusCells, d));
                    }
                }

                // ★ Drive the epicentre circle with the DLC's waveform. Intake and output are
                //   mutually exclusive.
                if (ringShape != null)
                {
                    int level = ringShape.LevelAt(0, 0, field.SeaLevelUnits);

                    // ★★ Put a lid on the height of the crest. What carries the wave far is
                    //    not the height but the <b>volume</b>, so what the lid takes off is
                    //    made up for in duration.
                    if (ringCap > 0f)
                    {
                        int capUnits = field.SeaLevelUnits + (int)(ringCap * 64f);
                        if (level > capUnits) level = capUnits;

                        float drawCap = ringDraw > 0f ? ringDraw : ringCap;
                        int floorUnits = field.SeaLevelUnits - (int)(drawCap * 64f);
                        if (level < floorUnits) level = floorUnits;
                    }

                    for (int k = 0; k < field.Sources.Length; k++) field.Sources[k].Target = level;

                    // ★★ **Always enable both the push and the pull.** That makes the circle
                    //    "stick to the target level" —— exactly the same behaviour as the
                    //    Dirichlet boundary the DLC applies to the outermost cells.
                    //    With only one at a time there is no force to drain the water that has
                    //    gathered, so the epicentre piles up to more than twice the target
                    //    (measured 2026-08-31: +232 m in reality against a target of +102 m).
                    long outRate = ringHold ? ringRate : (level > field.SeaLevelUnits ? ringRate : 0);
                    for (int k = 0; k < field.Sources.Length; k++) field.Sources[k].OutputRate = outRate;
                    // ★ The intake can use a different radius, so it can be made small enough
                    //   that the game's int32 does not overflow (the reason this file exists).
                    long inRate = ringInR > 0f
                        ? (long)Math.Pow((ringInR - 10f) / 0.4f, 2.0)
                        : ringRate;

                    long inputRate = ringNoIn ? 0
                        : (ringHold ? inRate : (level < field.SeaLevelUnits ? inRate : 0));
                    for (int k = 0; k < field.Sources.Length; k++) field.Sources[k].InputRate = inputRate;

                    if (!ringShape.Active)
                    {
                        field.Source.OutputRate = 0;
                        field.Source.InputRate = 0;
                    }

                    // ★★ Produce the drawback with a mound rather than a water source (see
                    //    the doc at the top of this file).
                    if (ringImpact > 0)
                    {
                        field.Source.InputRate = 0;

                        impulses.Clear();

                        if (level < field.SeaLevelUnits)
                        {
                            // Place a negative mound, i.e. a depression, in proportion to how
                            // far below the target is.
                            float drop = (field.SeaLevelUnits - level)
                                         / (float)Math.Max(1, (int)(ringCap * 64f));
                            int delta = -(int)(ringImpact * Math.Min(1f, drop));
                            if (delta != 0)
                            {
                                impulses.Add(Impulse.Round(centreX, centreZ, ringRadius, delta));
                            }
                        }
                    }

                    ringShape.Step();

                    // ★ Fire the waveform again (the second and third waves). The clock is
                    //   reset to 0, so the amplitude decay term starts over too and a wave of
                    //   the same height arrives once more.
                    if (ringRepeat > 1 && !ringShape.Active)
                    {
                        ringRepeat--;
                        ringShape.CurrentTime = 0;
                    }
                }

                field.Step(impulses);

                // ★ Advance the DLC tsunami's clock. m_currentTime goes up by 64 per water step.
                if (field.Edge != null) field.Edge.Step();

                if (trackArrival)
                {
                    Profile pr = Profile.Build(field, centreX, centreZ);
                    for (int r = 1; r < arrival.Length && r < pr.MeanMetres.Length; r++)
                        if (arrival[r] < 0 && pr.Count[r] != 0 && pr.MeanMetres[r] >= arrivalThreshold)
                            arrival[r] = frame;
                }

                if (frame % PrintInterval == 0 || frame == frames - 1)
                {
                    PrintRow(field, centreX, centreZ, frame, drive, baseVolume);
                    if (audit) PrintAudit(field, baseVolume);
                }

                if (!noPng && pngFrames.Contains(frame))
                    WriteSurfacePng(field, outDir, frame);
            }

            clock.Stop();

            if (trackArrival)
            {
                Console.WriteLine();
                Console.WriteLine("  wave front: first frame the radial mean reaches +"
                                  + arrivalThreshold.ToString("F2") + " m");
                Console.WriteLine("     r(km)  frame   d(frame)  speed(m/frame)  cells/frame");
                int prevR = -1, prevF = -1;
                for (int km = 1; km <= 4; km++)
                {
                    int r = (int)(km * 1000f / WaterField.CellSizeMetres + 0.5f);
                    if (r >= arrival.Length) break;
                    int fr = arrival[r];
                    string sp = "-", cf = "-", df = "-";
                    if (fr >= 0 && prevF >= 0)
                    {
                        int d = fr - prevF;
                        df = d.ToString();
                        if (d > 0)
                        {
                            float v = (r - prevR) * WaterField.CellSizeMetres / d;
                            sp = v.ToString("F2");
                            cf = (v / WaterField.CellSizeMetres).ToString("F3");
                        }
                    }
                    Console.WriteLine("     " + km.ToString().PadLeft(5) + "  "
                                      + (fr < 0 ? "never" : fr.ToString()).PadLeft(5) + "  "
                                      + df.PadLeft(8) + "  " + sp.PadLeft(14) + "  " + cf.PadLeft(11));
                    if (fr >= 0) { prevR = r; prevF = fr; }
                }
            }

            Console.WriteLine();
            Console.WriteLine("  " + frames + " frames in " + clock.Elapsed.TotalSeconds.ToString("F1")
                              + " s (" + (clock.Elapsed.TotalMilliseconds / frames).ToString("F1")
                              + " ms / frame)");
            if (field.Source != null)
            {
                Console.WriteLine();
                Console.WriteLine("  INT32 OVERFLOWS in the game's water-source maths: "
                                  + field.Source.Int32Overflows
                                  + (field.Source.Int32Overflows == 0
                                     ? "  (this configuration is safe on the real game)"
                                     : "  ** THIS CONFIGURATION CORRUPTS CELL HEIGHTS ON THE "
                                       + "REAL GAME ** worst product "
                                       + field.Source.WorstProduct
                                       + " vs int.MaxValue 2147483647"));
            }

            if (shelf)
            {
                Console.WriteLine("  SHORE (" + ((gridSize * 0.85f - centreX)
                                  * WaterField.CellSizeMetres / 1000f).ToString("F1")
                                  + " km from the epicentre): highest water "
                                  + shoreMax.ToString("F2") + " m above sea level @f"
                                  + shoreMaxFrame + ", flooded "
                                  + (floodCells * WaterField.CellSizeMetres).ToString("F0")
                                  + " m inland");
            }

            Console.WriteLine("  centre over EVERY frame: min " + runningMin.ToString("F2")
                              + " m @f" + runningMinFrame + " (" + (100f * -runningMin / depth)
                                .ToString("F0") + "% of the column), max "
                              + runningMax.ToString("F2") + " m @f" + runningMaxFrame
                              + ", steps at bare seabed " + zeroWaterSteps);
            Console.WriteLine("  final drive " + drive + " units = "
                              + (drive / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                              + " m of virtual sea-bed uplift ("
                              + TsunamiSource.WavesNeeded(drive) + " waves)");
            return 0;
        }

        /// <summary>
        /// A <b>mass ledger</b> (a diagnostic with no counterpart in the game).
        /// It breaks the change in total water volume down into the four exits that could
        /// cause it and reconciles them: the outer ring, evaporation, the floor at
        /// Max(...,0) and the ceiling at Min(...,65535).
        /// If <c>resid</c> is not 0, **there is an unknown leak**.
        /// </summary>
        private static void PrintAudit(WaterField f, long baseVolume)
        {
            long total = TotalWaterUnits(f);
            long dv = total - baseVolume;
            long explained = f.RingAdded - f.RingRemoved - f.EvapRemoved - f.SatLost - f.SatGained;

            Cell[] cells = f.Cells;
            int dry = 0, sat = 0, maxH = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                int h = cells[i].Height;
                if (h == 0) dry++;
                if (h == 65535) sat++;
                if (h > maxH) maxH = h;
            }

            // ── Grid-scale oscillation (ringing / checkerboard) ─────────────
            //   Extracts only the two-cell-period mode: h[x] - (h[x-1]+h[x+1])/2.
            //   A smooth wave does not show up here, so if the value rises it is
            //   **numerical oscillation**. A diagnostic with no counterpart in the game.
            int n = f.Size;
            ushort[] terr = f.Terrain;
            double nyq = 0.0; double nyqMax = 0.0; long nyqN = 0;
            for (int z = 1; z < n - 1; z++)
            {
                int row = z * n;
                for (int x = 1; x < n - 1; x++)
                {
                    int i = row + x;
                    double c = terr[i] + cells[i].Height;
                    double l = terr[i - 1] + cells[i - 1].Height;
                    double r = terr[i + 1] + cells[i + 1].Height;
                    double e = (c - 0.5 * (l + r)) / 64.0;
                    nyq += e * e; nyqN++;
                    double ae = e < 0 ? -e : e;
                    if (ae > nyqMax) nyqMax = ae;
                }
            }
            double nyqRms = (nyqN == 0) ? 0.0 : Math.Sqrt(nyq / nyqN);

            Console.WriteLine("         ledger  dV=" + dv
                              + "  ring+=" + f.RingAdded + "  ring-=" + f.RingRemoved
                              + "  evap=" + f.EvapRemoved + "  satLost=" + f.SatLost
                              + "  satGain=" + f.SatGained
                              + "  resid=" + (dv - explained)
                              + " | dry=" + dry + "  at65535=" + sat
                              + "  maxH=" + (maxH / 64.0).ToString("F2") + " m"
                              + " | nyqRms=" + nyqRms.ToString("F4")
                              + " m  nyqMax=" + nyqMax.ToString("F2") + " m");
        }

        /// <summary>Sum of the water columns over the board (cell sum in units of 1/64 m).</summary>
        private static long TotalWaterUnits(WaterField field)
        {
            Cell[] cells = field.Cells;
            long sum = 0;
            for (int i = 0; i < cells.Length; i++) sum += cells[i].Height;
            return sum;
        }

        /// <summary>One row of the table.</summary>
        private static void PrintRow(WaterField field, int cx, int cz, int frame, int drive,
                                     long baseVolume)
        {
            Profile p = Profile.Build(field, cx, cz);
            float centre = field.SurfaceAboveSeaMetres(cx, cz);

            double dv = (baseVolume == 0) ? 0.0
                      : (TotalWaterUnits(field) - baseVolume) * 100.0 / baseVolume;

            string stage = StageTag(frame);
            float peakR = (p.PeakRadiusCells < 0) ? 0f : p.PeakRadiusCells * WaterField.CellSizeMetres;

            Console.WriteLine(
                "  " + frame.ToString().PadLeft(5)
                + "  " + (frame / FramesPerRealSecond).ToString("F1").PadLeft(6)
                + "  " + drive.ToString().PadLeft(6)
                + "  " + stage.PadLeft(5)
                + "  " + centre.ToString("F2").PadLeft(6)
                + "  " + (peakR / 1000f).ToString("F2").PadLeft(6) + " km"
                + "  " + p.PeakMetres.ToString("F2").PadLeft(6)
                + "  " + Fmt(p.AtMetres(500f)).PadLeft(6)
                + "  " + Fmt(p.AtMetres(1000f)).PadLeft(5)
                + "  " + Fmt(p.AtMetres(2000f)).PadLeft(5)
                + "  " + Fmt(p.AtMetres(3000f)).PadLeft(5)
                + "  " + Fmt(p.AtMetres(4000f)).PadLeft(5)
                + "  " + dv.ToString("F2").PadLeft(6));
        }

        private static string Fmt(float v)
        {
            if (float.IsNaN(v)) return "-";
            return v.ToString("F2");
        }

        /// <summary>
        /// Forcing shapes for the sweep. **Only the shape that wins here is taken back into
        /// Core.**
        ///
        /// In the solver <c>dv/dt ∝ drive</c> and <c>dh/dt ∝ -div v</c>, so for the water
        /// level to return to where it started the <b>double integral of the force must be
        /// 0</b>. With only the single integral 0 (i.e. one sine cycle) **a hole is left
        /// behind**. This is used to confirm that.
        /// </summary>
        private static int DriveTotal(int shape, float step, float total, int drive)
        {
            if (step < 0f || step >= total || drive <= 0) return 0;

            double w = step / total;
            double f;

            switch (shape)
            {
                case 0:   // ★ exactly what Core uses today (the shape that won the sweep)
                    return TsunamiSource.DeltaAt(step / total * TsunamiSource.TotalSteps,
                                                 drive);

                case 1:   // 1.5 cycles with a heavier drawback (envelope changed to sin(pi*w))
                    f = -Math.Sin(Math.PI * w) * Math.Sin(2.0 * Math.PI * 1.5 * w);
                    break;

                case 2:   // ★ double integral 0: the second derivative of a bump (Ricker-like)
                    //   For B(w) = (1-cos(2 pi w))/2, B'' ∝ cos(2 pi w).
                    //   This gives ∫drive = 0 and ∫∫drive = 0 (not because B is 0 at both ends
                    //   and ∫B=0, but because the double integral of drive = -B'' comes back
                    //   to -B).
                    f = Math.Cos(2.0 * Math.PI * w);
                    // Remove the steps at both ends (the solver accumulates differences of
                    // slope, so a step acts as an impulse).
                    f *= Taper(w, 0.10);
                    break;

                case 3:   // long drawback, short push (show the uplift, then let it out)
                    if (w < 0.6) f = -Math.Sin(Math.PI * w / 0.6);
                    else f = Math.Sin(Math.PI * (w - 0.6) / 0.4) * 1.5;
                    break;

                case 4:   // push first, drawback after (just the sign flipped)
                    f = ((1.0 - Math.Cos(2.0 * Math.PI * w)) * 0.5)
                        * Math.Sin(2.0 * Math.PI * 1.5 * w);
                    break;

                case 5:   // 2 cycles. A shorter wavelength makes the ring narrower
                    f = -((1.0 - Math.Cos(2.0 * Math.PI * w)) * 0.5)
                        * Math.Sin(2.0 * Math.PI * 2.0 * w);
                    break;

                default:
                    f = 0.0;
                    break;
            }

            int units = (int)(f * drive + (f >= 0 ? 0.5 : -0.5));
            if (units > TsunamiSource.MaxDriveUnits) units = TsunamiSource.MaxDriveUnits;
            if (units < -TsunamiSource.MaxDriveUnits) units = -TsunamiSource.MaxDriveUnits;
            return units;
        }

        /// <summary>A window that rolls both ends smoothly down to 0.</summary>
        private static double Taper(double w, double edge)
        {
            double k = w < edge ? w / edge : (w > 1.0 - edge ? (1.0 - w) / edge : 1.0);
            if (k <= 0.0) return 0.0;
            if (k >= 1.0) return 1.0;
            return k * k * (3.0 - 2.0 * k);
        }

        /// <summary>TsunamiSource's stage in a single character.</summary>
        private static string StageTag(int frame)
        {
            if (frame >= TsunamiSource.TotalSteps) return "4 free";
            if (frame < TsunamiSource.DrawInSteps) return "1 in";
            if (frame < TsunamiSource.PushSteps) return "2 out";
            return "3 back";
        }

        /// <summary>The frames to emit a PNG for: the stage boundaries, and the propagation
        /// afterwards at even intervals.</summary>
        private static HashSet<int> PickPngFrames(int frames)
        {
            HashSet<int> set = new HashSet<int>();
            int[] fixedFrames =
            {
                0,
                (int)TsunamiSource.DrawInSteps - 1,
                (int)((TsunamiSource.DrawInSteps + TsunamiSource.PushSteps) * 0.5f),
                (int)TsunamiSource.PushSteps,
                (int)TsunamiSource.TotalSteps - 1
            };
            foreach (int f in fixedFrames)
                if (f >= 0 && f < frames) set.Add(f);

            for (int f = 0; f < frames; f += 300) set.Add(f);
            set.Add(frames - 1);
            return set;
        }

        /// <summary>
        /// Renders the height of the water surface as a single picture. Blue is a dip, red is
        /// a rise, and white is exactly sea level.
        /// **The colour scale is chosen automatically per picture**, so read absolute values
        /// from the table instead.
        /// </summary>
        private static void WriteSurfacePng(WaterField field, string dir, int frame)
        {
            int n = field.Size;
            Cell[] cells = field.Cells;
            ushort[] terrain = field.Terrain;
            int sea = field.SeaLevelUnits;

            // The scale. A floor is set so that a completely flat field does not divide by 0.
            float scale = 0.25f;
            for (int i = 0; i < cells.Length; i++)
            {
                float d = Math.Abs((terrain[i] + cells[i].Height - sea) / (float)WaterField.UnitsPerMetre);
                if (d > scale) scale = d;
            }

            byte[] rgb = new byte[n * n * 3];
            for (int i = 0; i < cells.Length; i++)
            {
                float d = (terrain[i] + cells[i].Height - sea) / (float)WaterField.UnitsPerMetre;
                float t = d / scale;
                if (t > 1f) t = 1f;
                if (t < -1f) t = -1f;

                byte r, g, b;
                if (t >= 0f)
                {
                    byte fade = (byte)(255f * (1f - t));
                    r = 255; g = fade; b = fade;      // white -> red
                }
                else
                {
                    byte fade = (byte)(255f * (1f + t));
                    r = fade; g = fade; b = 255;      // white -> blue
                }

                int o = i * 3;
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }

            string path = Path.Combine(dir, "water-" + frame.ToString("D5") + ".png");
            Png.Write(path, n, n, rgb);
            Console.WriteLine("    wrote " + Path.GetFileName(path)
                              + "  (colour scale +/-" + scale.ToString("F2") + " m)");
        }

        private static float ParseFloat(string s, float fallback)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }
    }
}
