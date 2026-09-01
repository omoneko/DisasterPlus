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
    /// <b>津波の発生源を、ゲームを起動せずにソルバへ通す。</b>
    ///
    /// ── なぜ要るのか ─────────────────────────────────────────────
    ///
    /// <c>tools/TsunamiPreview</c> で描けるのは<b>ソルバへの入力</b>だけだった。
    /// 「その外力で海面が何 m 上がるか」は<b>IL を読んでも決まらない</b> ——
    /// 応答は水深と海の広さで変わるからで、TsunamiSource のコメントにも
    /// そう書いてある（だから実機で測って外力を上げる作りになっている）。
    ///
    /// ★★ このツールは<b>その「測って上げる」ループをオフラインで回す</b>。
    ///   実機を起動して 18 秒待たなくても、外力と水深を変えて即座に結果が出る。
    ///
    /// ── 使い方 ───────────────────────────────────────────────────
    ///
    /// <code>
    ///   dotnet run --project tools/WaterSolverSim -- docs/images/water
    ///   dotnet run --project tools/WaterSolverSim -- out --depth 60 --intensity 200 --frames 1800
    /// </code>
    ///
    /// ── 読み方 ───────────────────────────────────────────────────
    ///
    /// <list type="bullet">
    /// <item><c>drive</c> ── いま出している外力（<c>m_delta</c> の単位、符号なし）。
    ///       <c>NextDrive</c> が実測から自分で決める。</item>
    /// <item><c>centre</c> ── 震源の海面の持ち上がり（m）。①の目標はこれ。</item>
    /// <item><c>peak</c> ── いちばん高い<b>輪</b>の半径と高さ。**これが走っている波**。
    ///       半径が増えていけば伝播しており、増えなければ立ち上がっていない。</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        /// <summary>格子の一辺（セル）の既定。512 x 16 m = 8.2 km 四方。実機は 1081。</summary>
        private const int DefaultGridSize = 512;

        /// <summary>海面（m）。実機の既定と同じ。</summary>
        private const float SeaLevelMetres = 40f;

        /// <summary>外力を組み直す間隔（フレーム）。実機の MOD も毎フレームは触らない。</summary>
        private const int DriveInterval = 1;   // 1 Step() = 1 water step = MOD の書き換え間隔

        /// <summary>表を出す間隔（フレーム）。</summary>
        private const int PrintInterval = 60;

        /// <summary>ゲーム速度 1 の目安。1 水ステップ ≒ 1 sim フレーム（IL 実測）。</summary>
        private const float FramesPerRealSecond = 60f / 64f;   // 1 water step = 64 sim frames

        private static int Main(string[] args)
        {
            string outDir = ".";
            float depth = 40f;
            byte intensity = 100;
            int frames = 1500;
            int pinnedDrive = -1;
            int shape = 0;             // 0 = TsunamiSource.DriveAt をそのまま使う
            bool shelf = false;   // 沖 -> 大陸棚 -> 汀線 -> 陸 の断面で回す
            float srcFrac = 0.5f;   // 源の X 位置（格子に対する比）。0.1 ならマップ端すれすれ
            int radiusCells = TsunamiSource.RadiusCells;
            float totalSteps = TsunamiSource.TotalSteps;
            // ★ 外周の輪は矩形の Dirichlet 境界なので、盤面が狭いと角の反射が
            //   早く戻ってくる。実機は 1081 —— 「見えている構造が境界のせいか」を
            //   分けるには、格子を変えて同じ結果になるかを見るしかない。
            int gridSize = DefaultGridSize;
            bool noPng = false;
            bool audit = false;
            int edgeIntensity = 0;   // >0 なら DLC の津波を左端に立てる（外力は使わない）
            int ringIntensity = 0;   // >0 なら震源に WaterSource を置いて同心円で立てる
            int ringRadius = 80;     // 震源の円の半径（セル）。80 -> 1280 m
            long ringRate = 0;       // その半径を出すのに要る流量
            bool ringHold = false;   // 円をつねに目標水位へ張り付かせる
            bool ringNoIn = false;   // 取り込みを一切使わない（int32 の溢れを避ける）
            int ringImpact = 0;      // >0 なら引き波を TYPE_IMPACT で出す（その最大 delta）
            float ringInR = 0f;      // >0 なら取り込み円の半径（m）を別に決める
            int ringDurSteps = 256;  // 波形の長さ（水ステップ）。バニラは 256
            float ringCap = 0f;      // >0 なら押し波の高さの頭打ち（m）
            float landRise = 0.5f;   // 陸の勾配（m/セル）。飽和すると浸水距離が測れない
            float ringDraw = 0f;     // >0 なら引き波の深さの頭打ち（m）。既定は ringCap と同じ
            float arrivalThreshold = float.NaN;   // ★ 診断。>0 なら波頭の到達時刻を測る。         // ★ 診断用。ゲームには対応物が無い。

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
                else if (a == "--ringdraw" && i + 1 < args.Length) { ringDraw = ParseFloat(args[++i], ringDraw); }
                else if (a == "--arrival" && i + 1 < args.Length) { arrivalThreshold = ParseFloat(args[++i], arrivalThreshold); }
                else if (a == "--nopng") { noPng = true; }
                else if (!a.StartsWith("--")) { outDir = a; }
            }

            if (frames < 1) frames = 1;
            if (gridSize < 16) gridSize = 16;
            Directory.CreateDirectory(outDir);

            // ── 平らな海 ──────────────────────────────────────────
            //
            // ★★ **海面より深い海は作れない。** 地形は ushort（1/64 m）なので
            //   海底の下限は標高 0 であり、海面 40 m のマップで作れる海は
            //   最大 40 m である —— これは実機の制約そのもの（m_heightBuffer は ushort）。
            //   要求された水深がそれを超えるときは<b>海面ごと持ち上げる</b>。
            //   黙って浅い海で回すと「深くしたのに波が伸びない」を誤診する。
            float seaLevel = Math.Max(SeaLevelMetres, depth);

            WaterField field = new WaterField(gridSize, seaLevel);

            // ★★ 海岸を置くと「震源では大きいのに海岸では高潮」が測れる。
            //    棚は格子の 60% 地点から始まり、85% 地点が汀線。
            int shelfStart = (int)(gridSize * 0.60f);
            int shoreCell = (int)(gridSize * 0.85f);

            if (shelf) field.FillShelf(depth, shelfStart, shoreCell, landRise);
            else field.FillFlatSea(depth);

            // ★★ 源を端に寄せられるようにする（2026-08-30）。外周セルはソルバが
            //    海面に固定する境界なので、**近いとエネルギーを吸われる**。
            int centreX = (int)(gridSize * srcFrac);
            int centreZ = gridSize / 2;

            float target = 0f;
            int drive = (pinnedDrive >= 0) ? pinnedDrive : TsunamiSource.DriveUnitsFor(intensity, depth);

            // 初期の総水量。以降の行で「ソルバが水を作った／消した」を見るための基準。
            // ★ 外周の輪は設計どおり水を捨てる／湧かせるので、0% にはならない。
            //   見たいのは**跳ねていないか**である。
            //
            // ★★ <c>--audit</c> で分解した結果（2026-08-30、drive 0-12800 /
            //   水深 5・40・150 m / 格子 512・1081 / 1500 フレームまで）:
            //   <b>dV は「外周の輪 - 蒸発」で完全に説明でき、resid は常に 0</b>。
            //   <c>Math.Max(...,0)</c> / <c>Math.Min(...,65535)</c> の飽和は
            //   <b>一度も発火しなかった</b>（satLost = satGain = 0、65535 のセルも 0 個）。
            //   「飽和が水を失わせている」という当初の見立ては**誤り**である。
            // ── DLC の津波（--edge）──────────────────────────────
            //
            // ★★ **こちらの外力とは仕掛けがまるで違う**（EdgeWave のクラス doc）。
            //   左端まるごとを海の区画とみなし、内向き +X で立てる。
            //   実機の GetSeaSideLocation は「いちばん長く続く海の区画」を採るので、
            //   左が全部海のこの断面ではまさにこうなる。
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

                drive = 0;   // 外力は出さない。比べたいのは仕掛けの違いである。
                pinnedDrive = 0;

                Console.WriteLine("  ** DLC tsunami mode ** intensity " + edgeIntensity
                                  + " -> m_delta " + field.Edge.Delta + " units = "
                                  + (field.Edge.Delta / 64f).ToString("F1")
                                  + " m, driven along the WHOLE LEFT EDGE for "
                                  + (EdgeWave.VanillaDuration / EdgeWave.TimePerStep)
                                  + " water steps. The edge is a Dirichlet boundary, so this "
                                  + "MAKES water - it does not borrow it. No impact drive is used.");
            }

            // ── 震源の同心円（--ring）─────────────────────────────
            //
            // ★★ DLC の<b>波形はそのまま</b>、置き場所だけ震源へ移す。
            //   水位が海面より下の位相は取り込み（引き波）、上の位相は
            //   吐き出し（押し波）。<see cref="SourceDisc"/> のクラス doc。
            EdgeWave ringShape = null;

            if (ringIntensity > 0)
            {
                // 半径 r [m] を出すのに要る流量: rate = ((r - 10) / 0.4)^2
                float wantR = ringRadius * WaterField.CellSizeMetres;
                long rate = (long)Math.Pow((wantR - 10f) / 0.4f, 2.0);

                field.Source = new SourceDisc
                {
                    CellX = centreX,
                    CellZ = centreZ,
                };

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

                // 流量は毎ステップ位相で切り替える。ここでは覚えておくだけ。
                ringRate = rate;
            }

            long baseVolume = TotalWaterUnits(field);

            Console.WriteLine("== WaterSolverSim : SimulateWater のオフライン再現 ==");
            Console.WriteLine("  grid       " + gridSize + " x " + gridSize + " cells ("
                              + (gridSize * WaterField.CellSizeMetres / 1000f).ToString("F1") + " km 四方), "
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

            // ── 波頭の到達時刻（診断。ゲームには対応物が無い）────────────
            //   半径ごとの平均水面が初めて閾値を超えたフレームを覚える。
            //   「いちばん高い輪」は中心の窪みや格子ノイズで跳ねるので、
            //   波の速さを測るにはこちらを使う。
            bool trackArrival = !float.IsNaN(arrivalThreshold) && arrivalThreshold > 0f;
            int[] arrival = null;
            if (trackArrival)
            {
                arrival = new int[gridSize / 2];
                for (int r = 0; r < arrival.Length; r++) arrival[r] = -1;
            }

            Stopwatch clock = Stopwatch.StartNew();

            // ★★ **印字の間引きは罠である。**（2026-08-30、判定役）
            //    60 フレームおきに出すだけだと、そのあいだの谷と山が見えない。
            //    「420 歩で緑・1080 歩で +111 m」を見落としたのと同じ穴なので、
            //    **毎フレーム走査した最小・最大をここで持つ。**
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
                // ── 外力を組み直す（実機の MOD と同じ手順）──────────
                {
                    if (shelf)
                    {
                        // ★ 汀線の手前 2 セルで、平常からの上がりを追う。
                        //   **ここが「街に何 m 来たか」である。**
                        float atShore = field.ColumnRiseMetres(shoreCell - 2, centreZ);
                        if (atShore > shoreMax) { shoreMax = atShore; shoreMaxFrame = frame; }

                        // 陸へ何セル乗り上げたか（水が 0.5 m 以上乗っている列）。
                        // ★★ **標高ではなく水の厚みで見る**（WaterDepthMetres の doc）。
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
                        throw new InvalidOperationException("水面が NaN になった: frame " + frame);

                    // ★★ 閉ループはやめた（2026-08-30）。ソルバの応答は 100 歩ほど
                    //    遅れるので、8 歩ごとに測って上げると必ず巻き上がる
                    //    （このツールで再現して確かめた: 2000 -> 139,516 units、
                    //     中心が -40 m ＝ 海底まで掘れる）。
                    //    いまは **このツールで測って決めた開ループの定数**を使う。
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

                // ★ 震源の円を DLC の波形で動かす。取り込みと吐き出しは排他。
                if (ringShape != null)
                {
                    int level = ringShape.LevelAt(0, 0, field.SeaLevelUnits);

                    // ★★ 押し波の高さに蓋をする。遠くへ届かせるのは
                    //    高さではなく<b>体積</b>なので、蓋のぶんは長さで補う。
                    if (ringCap > 0f)
                    {
                        int capUnits = field.SeaLevelUnits + (int)(ringCap * 64f);
                        if (level > capUnits) level = capUnits;

                        float drawCap = ringDraw > 0f ? ringDraw : ringCap;
                        int floorUnits = field.SeaLevelUnits - (int)(drawCap * 64f);
                        if (level < floorUnits) level = floorUnits;
                    }

                    field.Source.Target = level;

                    // ★★ **押しと引きを両方いつも入れる。** これで円は
                    //    「目標水位へ張り付く」——DLC が外周セルにやっている
                    //    Dirichlet 境界とまったく同じ振る舞いになる。
                    //    片方ずつにすると、寄せ集まった水を抜く力が無いので
                    //    震源が目標の 2 倍以上に盛り上がる（2026-08-31 実測:
                    //    目標 +102 m に対して実際 +232 m）。
                    field.Source.OutputRate = ringHold ? ringRate : (level > field.SeaLevelUnits ? ringRate : 0);
                    // ★ 取り込みは別半径にできる。ゲームの int32 が溢れないところまで
                    //   小さくするため（このファイルを書いた理由）。
                    long inRate = ringInR > 0f
                        ? (long)Math.Pow((ringInR - 10f) / 0.4f, 2.0)
                        : ringRate;

                    field.Source.InputRate = ringNoIn ? 0
                        : (ringHold ? inRate : (level < field.SeaLevelUnits ? inRate : 0));

                    if (!ringShape.Active)
                    {
                        field.Source.OutputRate = 0;
                        field.Source.InputRate = 0;
                    }

                    // ★★ 引き波は水源ではなく丘で出す（このファイルの冒頭 doc）。
                    if (ringImpact > 0)
                    {
                        field.Source.InputRate = 0;

                        impulses.Clear();

                        if (level < field.SeaLevelUnits)
                        {
                            // 目標がどれだけ下かに比例して、負の丘＝窪みを置く。
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
                }

                field.Step(impulses);

                // ★ DLC の津波の時計を進める。m_currentTime は 1 水ステップで +64。
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
                              + " m の仮想的な海底隆起 ("
                              + TsunamiSource.WavesNeeded(drive) + " waves)");
            return 0;
        }

        /// <summary>
        /// <b>質量の帳簿</b>（ゲームには存在しない診断）。
        /// 総水量の増減を、それを起こしうる 4 つの出口に分解して突き合わせる:
        /// 外周の輪、蒸発、Max(...,0) の底打ち、Min(...,65535) の頭打ち。
        /// <c>resid</c> が 0 でなければ**未知の漏れがある**。
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

            // ── 格子スケールの振動（ringing / checkerboard）─────────────
            //   2 セル周期のモードだけを取り出す: h[x] - (h[x-1]+h[x+1])/2。
            //   なめらかな波はここに乗らないので、値が立てば**数値的な振動**である。
            //   ゲームには存在しない診断。
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

        /// <summary>盤面の水柱の総和（1/64 m 単位のセル和）。</summary>
        private static long TotalWaterUnits(WaterField field)
        {
            Cell[] cells = field.Cells;
            long sum = 0;
            for (int i = 0; i < cells.Length; i++) sum += cells[i].Height;
            return sum;
        }

        /// <summary>表の 1 行。</summary>
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
        /// 掃引用の外力の形。**ここで勝った形だけを Core へ持ち帰る。**
        ///
        /// ソルバは <c>dv/dt ∝ drive</c>、<c>dh/dt ∝ -div v</c> なので、
        /// 水位が元へ戻るには<b>外力の 2 重積分が 0</b>でなければならない。
        /// 1 重積分だけ 0（＝正弦 1 周期）では**穴が残る**。ここはその確認に使う。
        /// </summary>
        private static int DriveTotal(int shape, float step, float total, int drive)
        {
            if (step < 0f || step >= total || drive <= 0) return 0;

            double w = step / total;
            double f;

            switch (shape)
            {
                case 0:   // ★ Core の現行そのもの（掃引で勝った形を取り込んだ）
                    return TsunamiSource.DeltaAt(step / total * TsunamiSource.TotalSteps,
                                                 drive);

                case 1:   // 引きを厚くした 1.5 周期（包絡を sin(pi*w) に）
                    f = -Math.Sin(Math.PI * w) * Math.Sin(2.0 * Math.PI * 1.5 * w);
                    break;

                case 2:   // ★ 2 重積分 0: bump の 2 階微分（Ricker 風）
                    //   B(w) = (1-cos(2 pi w))/2 のとき B'' ∝ cos(2 pi w)。
                    //   これなら ∫drive = 0 かつ ∫∫drive = 0（B が両端 0 で ∫B=0 だから
                    //   ではなく、drive = -B'' の 2 重積分が -B に戻るため）。
                    f = Math.Cos(2.0 * Math.PI * w);
                    // 両端の段差を消す（ソルバは傾きの差を積むので段差は衝撃になる）。
                    f *= Taper(w, 0.10);
                    break;

                case 3:   // 引きを長く、押しを短く（隆起を見せてから吐き出す）
                    if (w < 0.6) f = -Math.Sin(Math.PI * w / 0.6);
                    else f = Math.Sin(Math.PI * (w - 0.6) / 0.4) * 1.5;
                    break;

                case 4:   // 押しを先に、引きを後に（符号を反転しただけ）
                    f = ((1.0 - Math.Cos(2.0 * Math.PI * w)) * 0.5)
                        * Math.Sin(2.0 * Math.PI * 1.5 * w);
                    break;

                case 5:   // 2 周期。短い波長で環を細くする
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

        /// <summary>両端を滑らかに 0 へ落とす窓。</summary>
        private static double Taper(double w, double edge)
        {
            double k = w < edge ? w / edge : (w > 1.0 - edge ? (1.0 - w) / edge : 1.0);
            if (k <= 0.0) return 0.0;
            if (k >= 1.0) return 1.0;
            return k * k * (3.0 - 2.0 * k);
        }

        /// <summary>TsunamiSource の段を 1 文字で。</summary>
        private static string StageTag(int frame)
        {
            if (frame >= TsunamiSource.TotalSteps) return "4 free";
            if (frame < TsunamiSource.DrawInSteps) return "1 in";
            if (frame < TsunamiSource.PushSteps) return "2 out";
            return "3 back";
        }

        /// <summary>PNG を出すフレーム。段の切れ目と、そのあとの伝播を等間隔で。</summary>
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
        /// 水面の高さを 1 枚の絵にする。青が凹み、赤が盛り上がり、白が海面ちょうど。
        /// **色の目盛りは絵ごとに自動**なので、絶対値は表のほうで読むこと。
        /// </summary>
        private static void WriteSurfacePng(WaterField field, string dir, int frame)
        {
            int n = field.Size;
            Cell[] cells = field.Cells;
            ushort[] terrain = field.Terrain;
            int sea = field.SeaLevelUnits;

            // 目盛り。全部平らなときに 0 除算しないよう下限を置く。
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
                    r = 255; g = fade; b = fade;      // 白 -> 赤
                }
                else
                {
                    byte fade = (byte)(255f * (1f + t));
                    r = fade; g = fade; b = 255;      // 白 -> 青
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
