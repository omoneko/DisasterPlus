using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>海溝型地震の津波。</b>**sim スレッド専用。**
    ///
    /// 形と時計は <see cref="TsunamiSource"/>（Core・テスト付き）が持つ。
    /// ここは<b>それをゲームの水シミュへ渡し、効き具合を測って返す</b>だけである。
    ///
    /// ── 何を作り直したか（2026-08-29）───────────────────────────
    ///
    /// 研究は <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>。要点:
    ///
    /// &gt; バニラの津波は<b>外周の一区画で海面を 1.5 周期上下させる境界条件</b>で、
    /// &gt; 水の壁はすべてゲームの浅水ソルバの伝播である。発生源は 256 フレームしかない。
    ///
    /// ソルバには<b>マップのどこにでも置ける外力</b>があり（<c>TYPE_IMPACT</c>）、
    /// それは「そこに水の山があるかのように水面の傾きを足す」——
    /// <b>海底の隆起と同じ</b>、津波の教科書どおりの発生源である。
    /// だから震源にそれを置けば、同心円状の水の壁は<b>ソルバが作ってくれる</b>。
    ///
    /// ── ★★ 「発生しない」の真因（2026-08-30）──────────────────────────
    ///
    /// 所有者:「海溝型地震による津波は実装されていますか？発生しないんですが。」
    ///
    /// 実機ログでは<b>動いていた</b>。動いたうえで 0.7 m しか上がらなかった:
    ///
    /// <code>
    ///   tsunami started at (-7204,-44) ... peak drive 7447 units
    ///   tsunami drive finished after 1080 frames ...
    ///       Highest sea seen over the epicentre was 0.7 m above sea level
    /// </code>
    ///
    /// 原因は 2 つで、**どちらも読みだけでは分からず、ゲームの水ソルバを
    /// オフラインで再現して（<c>tools/WaterSolverSim</c>）はじめて分かった**:
    ///
    /// <list type="number">
    /// <item><b>時計が 64 倍速かった。</b><c>SimulateWater</c> は 64 sim フレームに
    ///   1 回しか走らない（<see cref="FramesPerWaterStep"/>）。「1080 フレーム押す」は
    ///   水ステップにして 17 回でしかなかった</item>
    /// <item><b>押しっぱなしは波にならない。</b>定常的な外力は定常的な流出を作る ——
    ///   それは波ではなく穴である。外力は「引き→押し→引き」でなければならない
    ///   （<see cref="TsunamiSource"/> のクラス doc）</item>
    /// </list>
    ///
    /// ★ Int16 の上限（32767）を超える外力が要りうるので、
    ///   <b>同じ原点・同じ半径の波を重ねる</b>（外力は波ごとに加算される）。
    ///
    /// ── ★★ 守ること ────────────────────────────────────────
    ///
    /// <list type="bullet">
    /// <item><c>WaterWave</c> は<b>セーブに焼き付く</b>。<see cref="Reset"/> で問答無用に解放する</item>
    /// <item><c>m_duration</c> は<b>短く有限に</b>し、書き換えで延命する ——
    ///   ロードでハンドルを失っても、ソルバが 1 秒で片付けてくれる</item>
    /// <item><c>m_delta</c> の書き換えは <c>m_waterWaves</c> の
    ///   <c>Monitor.TryEnter</c> の中で行う（水スレッドが同じ配列を読む）</item>
    /// </list>
    /// </summary>
    public static class TsunamiWave
    {
        // ★★ **2026-08-31 以降、この経路で津波は立たない。**
        //    <c>TYPE_IMPACT</c> は水を押しのけるだけで作らないので、汀線まで持たない
        //    （オフライン実測: 汀線 19.3 m 対 DLC 84.8 m）。いまの津波は
        //    <see cref="TsunamiRing"/>（震源に置く WaterSource）である。
        //    ここに残しているのは
        //      1. <see cref="DepthAt"/> —— 水深はソルバと同じ量で測る必要があり、
        //         その式がここにある（TrenchQuakeSlot も使う）
        //      2. <see cref="Reset"/> —— 旧版で置いた水波が残っている都市の後始末
        //    の 2 つだけである。**Begin は誰も呼ばない。**

        /// <summary><c>WaterWave.m_type</c> の <c>TYPE_IMPACT</c>（IL 実測）。</summary>
        private const ushort TypeImpact = 2;

        /// <summary>16 m セルの数。<c>(world + 8640) / 16</c> でセルになる（IL 実測）。</summary>
        private const int GridCells = 1080;

        /// <summary>マップ半辺（m）。<c>SplashWater</c> の IL 実測にある 8640 と同じ。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 1 水ステップぶんの sim フレーム数。
        ///
        /// ★★ **64 である。1 ではない。**（2026-08-30）
        ///   <c>SimulateWater</c> は終わりに <c>m_waterFrameIndex</c> を
        ///   <c>start + 64</c> にし、水スレッドはそれが
        ///   <c>m_simulationFrameIndex</c> に追い越されるまで回らない。
        ///   ＝ **ソルバは 64 sim フレームに 1 回しか進まない。**
        ///
        ///   前の版はここを 8 にしていたので、
        ///   <b>水が 1 度も動かないうちに外力を 8 回書き換えていた</b>。
        ///   （<see cref="TsunamiSource"/> のクラス doc に裏取り 2 件）
        /// </summary>
        private const int FramesPerWaterStep = 64;

        /// <summary>
        /// <c>m_duration</c>（<c>m_currentTime</c> は<b>毎水ステップ</b> +64）。
        /// 128 ＝ **3 水ステップ ≒ 3.2 実秒**（m_currentTime は 0 から 64, 128, 192 と
        /// 進み、192 > 128 で初めて解放される。2 ではなく 3 —— 安全側に 1 歩多い）。
        ///
        /// ★★ 4096 -> 128（2026-08-30、最終検証）。4096 を「1 実秒」と書いていたが
        ///   <b>64 倍まちがっていた</b>: 4096/64 ＝ 64 水ステップ ＝ 4096 sim フレーム
        ///   ＝ **68 実秒**。ロードでハンドルを失った波が、最後に書かれた外力
        ///   （最大で drive の 1.5 倍）で<b>1 分以上も海を押し続ける</b>ことになる。
        ///   <see cref="Write"/> が毎水ステップ <c>m_currentTime</c> を 0 に戻すので、
        ///   短くしても走行中は何も困らない。
        ///
        /// ★★ **必ず有限にし、しかも短くする。**（Codex レビュー P1）
        ///   <c>WaterWave</c> はセーブに焼き付くのに <see cref="_waves"/> は静的変数なので
        ///   <b>ロードでは戻らない</b>。65535 にすると <c>m_currentTime</c> は
        ///   <c>Min(currentTime + 64, 65535)</c> で<b>張り付き</b>、
        ///   <c>currentTime &gt; duration</c> が永久に成立しない ——
        ///   **津波の最中にセーブした都市は外力を永久に抱える。**
        /// </summary>
        private const ushort WaveDurationTicks = 128;

        // ── 状態 ──────────────────────────────────────────────

        /// <summary>重ねた波のハンドル。**0 は「その枠は使っていない」。**</summary>
        private static readonly ushort[] _waves =
            new ushort[TsunamiSource.MaxStackedWaves];

        /// <summary>
        /// その枠が<b>まだ自分の波であること</b>を確かめるための指紋。
        ///
        /// ★★ **<c>m_type == TYPE_IMPACT</c> だけでは足りない。**（2026-08-30、最終検証）
        ///   IL: <c>CreateWaterWave</c> は <c>m_type == 0</c> の枠を<b>使い回す</b>し、
        ///   <c>ReleaseWaterWave</c> は所有者を確かめずに <c>m_type</c> を 0 にして
        ///   末尾の空き枠を詰める。さらにソルバは
        ///   <c>m_currentTime &gt; m_duration</c> で<b>勝手に解放する</b>。
        ///   一方 <c>DisasterHelpers.SplashWater</c> は<b>まさに TYPE_IMPACT</b> の波を
        ///   作る（隕石・地震の水柱）ので、生きた都市では同じ型の枠が絶えず
        ///   入れ替わる。指紋が合わない枠は<b>他人のもの</b>である ——
        ///   書いても解放してもいけない。
        /// </summary>
        private static readonly int[] _fingerprints =
            new int[TsunamiSource.MaxStackedWaves];

        /// <summary>
        /// 外力を切ったあとも<b>波を追いかけて記録する</b>フレーム数（水ステップ）。
        ///
        /// ★★ **これが 2026-08-30 の「震源では 82 m なのに海岸では高潮」を
        ///   詰めるための唯一の道具である。**（オフライン再現は海岸で 20〜28 m を
        ///   出すのに、実機ではそう見えない。**再現とゲームが食い違っている**ので、
        ///   ゲームの中で測るしかない。）
        ///   波は 8.2 m/水ステップで進むので、900 歩 ＝ 7.4 km ぶん。
        /// </summary>
        private const int WatchSteps = 900;

        /// <summary>
        /// 途中経過を出す間隔（水ステップ）。
        ///
        /// ★★ **最後に 1 行だけ出す作りは失敗だった。**（2026-08-31）
        ///   900 歩 ＝ 16 実分。所有者はその前にゲームを閉じ、
        ///   <b>1 行も残らなかった</b>。測る道具が「最後まで座っていること」を
        ///   要求してはいけない。120 歩（≒2 実分）ごとに出す ——
        ///   2 km は 2 分、4 km は 6 分で読めるようになる。
        /// </summary>
        private const int WatchReportEvery = 120;

        /// <summary>測る半径（m）。**街がありそうな距離**を並べる。</summary>
        private static readonly float[] WatchRadii = { 2000f, 4000f, 6000f, 8000f };

        /// <summary>その半径で見た最高の海面（m）。</summary>
        private static readonly float[] _watchPeak = new float[4];

        /// <summary>それを見た水ステップ。</summary>
        private static readonly int[] _watchPeakStep = new int[4];

        /// <summary>その半径のいちばん浅い水深（m）。**棚で波が絞られるかを見る。**</summary>
        private static readonly float[] _watchMinDepth = new float[4];

        /// <summary>
        /// その半径で<b>海だった方位の数</b>（<see cref="WatchAzimuths"/> のうち）。
        /// **0 ならそのリングは全部陸で、そこには波の届きようがない。**
        /// </summary>
        private static readonly int[] _watchSeaAzimuths = new int[4];

        private static bool _watching;
        private static uint _watchStartFrame;
        private static bool _running;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static int _drive;
        private static int _delta;
        private static float _peakRiseMetres;
        private static float _peakRingMetres;
        private static float _lastCentreMetres;
        private static float _seaLevel;
        private static float _depthMetres;
        private static Vec3 _centre;
        private static bool _errorLogged;

        /// <summary>今 津波が動いているか（診断・表示用）。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>今の外力（<c>m_delta</c> の単位。負＝中心へ、正＝外へ）。</summary>
        public static int DeltaUnits { get { return _delta; } }

        /// <summary>実測から決めた外力の大きさ（同上、符号なし）。</summary>
        public static int DriveUnits { get { return _drive; } }

        /// <summary>震源で観測したいちばん高い海面（m、診断用）。</summary>
        public static float PeakRiseMetres { get { return _peakRiseMetres; } }

        /// <summary>発生源の縁（＝環ができるところ）で観測した最高（m、診断用）。</summary>
        public static float PeakRingMetres { get { return _peakRingMetres; } }

        /// <summary>震源の水深（m、診断用）。**ソルバの流量の上限そのもの。**</summary>
        public static float DepthMetres { get { return _depthMetres; } }

        /// <summary>今どの段か（**英語・診断用**）。</summary>
        public static string Stage
        {
            get
            {
                if (!_running) return "not running";
                return TsunamiSource.StageAt(
                    (Singleton<SimulationManager>.instance.m_currentFrameIndex - _startFrame)
                    / (float)FramesPerWaterStep);
            }
        }

        /// <summary>直近の顛末（**英語・診断用**）。断ったときは必ず入る。</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// **レベルのロード／アンロードで必ず呼ぶ。** 置いた波を<b>問答無用で解放する</b>。
        /// 呼び忘れるとセーブに残る（クラス doc）。
        /// </summary>
        public static void Reset()
        {
            ReleaseAll();

            _running = false;
            _watching = false;
            _watchStartFrame = 0u;
            _startFrame = 0u;
            _lastFrame = 0u;
            _drive = 0;
            _delta = 0;
            _peakRiseMetres = 0f;
            _peakRingMetres = 0f;
            _lastCentreMetres = 0f;
            _depthMetres = 0f;
            Detail = null;
        }

        /// <summary>
        /// **sim スレッド。** 震源 <paramref name="epicentre"/> で津波を起こす。
        /// 既に動いていれば何もしない（同時に 1 本だけ）。
        /// </summary>
        public static bool Begin(Vec3 epicentre, byte intensity, uint frame)
        {
            if (_running) return false;

            try
            {
                return BeginCore(epicentre, intensity, frame);
            }
            catch (System.Exception e)
            {
                Detail = "starting the tsunami threw " + e.GetType().Name;
                Log.Error("tsunami failed to start", e);
                ReleaseAll();
                return false;
            }
        }

        private static bool BeginCore(Vec3 epicentre, byte intensity, uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                Detail = "the water simulation is not reachable; no tsunami";
                return false;
            }

            // ★ 陸の上に置いても水は動かない（ソルバは水深で流量を頭打ちにする）。
            if (!terrain.HasWater(new Vector2(epicentre.X, epicentre.Z)))
            {
                Detail = "the epicentre is not on open water; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            _centre = epicentre;
            _delta = 0;
            _peakRiseMetres = 0f;
            _peakRingMetres = 0f;
            _lastCentreMetres = 0f;

            // ★★ **水深を先に測る。外力はそれで割る。**
            //    ソルバの流量は v = min(v, m_height) で水深に頭打ちされるので、
            //    浅い海に深い海用の外力を出すと<b>震源が海底むき出しになる</b>
            //    （オフライン再現: 水深 10 m で 136 水ステップ ≒ 145 実秒）。
            //    ログにも必ず出す —— 「押しても動かない」の第一容疑者だからである。
            _depthMetres = DepthAt(terrain, epicentre.X, epicentre.Z);
            _drive = TsunamiSource.DriveUnitsFor(intensity, _depthMetres);

            if (!CreateAll(terrain))
            {
                Detail = "the water simulation refused the wave; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                ReleaseAll();
                return false;
            }

            _running = true;
            _startFrame = frame;
            _lastFrame = frame;
            Detail = null;

            // ★ バニラと同じ物差しで自分の波も測る（SeaWatch のクラス doc）。
            SeaWatch.Arm("Disaster+ trench tsunami, drive " + _drive + " units", frame);

            Log.Info("tsunami started at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): " + CountWaves()
                     + " stacked TYPE_IMPACT water waves, radius "
                     + TsunamiSource.RadiusMetres.ToString("F0")
                     + " m. Sea level " + _seaLevel.ToString("F0")
                     + " m, water is " + _depthMetres.ToString("F1")
                     + " m deep here (the solver caps flow at the depth, so a shallow sea "
                     + "cannot carry a big wave). Drive " + _drive + " units over "
                     + TsunamiSource.WavesNeeded(_drive) + " stacked waves, written once "
                     + "per water step (" + FramesPerWaterStep
                     + " sim frames). For scale, the DLC tsunami drives the map border "
                     + "with " + TsunamiSource.VanillaDeltaUnits(intensity)
                     + " units, though that is a boundary level, not a hill. "
                     + "The drive lasts " + TsunamiSource.TotalSteps.ToString("F0")
                     + " water steps = " + (TsunamiSource.TotalSteps * FramesPerWaterStep)
                     .ToString("F0") + " sim frames");
            return true;
        }

        /// <summary>**sim スレッド、ポーズガードより下。** 外力を進める。</summary>
        public static void Tick(uint frame)
        {
            if (!_running && !_watching) return;

            try
            {
                if (_watching && !_running) { Watch(frame); return; }
                Step(frame);
            }
            catch (System.Exception e)
            {
                Detail = "the tsunami tick threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("tsunami failed", e);
                }

                // ★★ **落ちたら畳む。** 外力を書き換える経路が止まったまま波が残ると、
                //    海がずっと押され続ける。
                ReleaseAll();
                _running = false;
            }
        }

        private static void Step(uint frame)
        {
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                ReleaseAll();
                _running = false;
                return;
            }

            // ★★ **水ステップに直す。** ソルバはこの単位でしか進まない。
            float elapsed = (frame - _startFrame) / (float)FramesPerWaterStep;

            Observe(terrain);

            if (TsunamiSource.IsFinished(elapsed))
            {
                Log.Info("tsunami drive finished after " + elapsed.ToString("F0")
                         + " water steps (" + (frame - _startFrame)
                         + " sim frames). Drive settled at " + _drive + " units ("
                         + TsunamiSource.WavesNeeded(_drive) + " stacked waves). "
                         + "Highest sea over the epicentre " + _peakRiseMetres.ToString("F1")
                         + " m, over the source rim (" + TsunamiSource.RadiusMetres.ToString("F0")
                         + " m out) " + _peakRingMetres.ToString("F1")
                         + " m. Water depth here was " + _depthMetres.ToString("F1")
                         + " m. The waves are released; the solver carries the ring on its own");
                ReleaseAll();
                _running = false;

                // ★★ **ここで終わりにしない。** 波がどこまで届くかを測る。
                BeginWatch(frame);
                return;
            }

            // ★★ 閉ループはやめた（2026-08-30）。ソルバの応答は 100 歩ほど遅れるので、
            //    測って上げる制御は必ず巻き上がる（オフライン再現で確認:
            //    2000 -> 139,516 units、中心が -40 m ＝ 海底まで掘れた）。
            //    いまの外力は <c>tools/WaterSolverSim</c> で測って決めた開ループの定数。
            _delta = TsunamiSource.DeltaAt(elapsed, _drive);
            Write(terrain, _delta);
        }

        /// <summary>監視を始める。水深はここで 1 度だけ測る（地形は動かない）。</summary>
        private static void BeginWatch(uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) return;

            _watching = true;
            _watchStartFrame = frame;

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                _watchPeak[r] = 0f;
                _watchPeakStep[r] = -1;
                _watchMinDepth[r] = float.MaxValue;

                for (int a = 0; a < WatchAzimuths; a++)
                {
                    float ang = 6.2831853f * a / WatchAzimuths;
                    float x = _centre.X + Mathf.Cos(ang) * WatchRadii[r];
                    float z = _centre.Z + Mathf.Sin(ang) * WatchRadii[r];

                    if (x < -MapHalfExtent || x > MapHalfExtent) continue;
                    if (z < -MapHalfExtent || z > MapHalfExtent) continue;
                    if (!terrain.HasWater(new Vector2(x, z))) continue;

                    // ★ 陸を水深 0 として混ぜない（「棚だ」と誤読する）。
                    if (IsLand(terrain, x, z)) continue;

                    float d = DepthAt(terrain, x, z);
                    if (d < _watchMinDepth[r]) _watchMinDepth[r] = d;
                }

                if (_watchMinDepth[r] == float.MaxValue) _watchMinDepth[r] = 0f;
            }

            Log.Info("tsunami watch started: sampling the sea every water step at 2/4/6/8 km "
                     + "from the epicentre for " + WatchSteps + " water steps ("
                     + (WatchSteps * FramesPerWaterStep / 3600f).ToString("F0")
                     + " real minutes). Shallowest water on each ring: "
                     + _watchMinDepth[0].ToString("F0") + " / "
                     + _watchMinDepth[1].ToString("F0") + " / "
                     + _watchMinDepth[2].ToString("F0") + " / "
                     + _watchMinDepth[3].ToString("F0") + " m. The solver caps flow at the "
                     + "depth, so a shallow ring throttles the wave rather than raising it. "
                     + "NOTE: only cells whose seabed is BELOW sea level are sampled - "
                     + "hitting a mountain would otherwise read as a huge false wave");
        }

        /// <summary>方位の数。**円周のどこかで高ければ、そこへ届いている。**</summary>
        private const int WatchAzimuths = 12;

        /// <summary>**sim スレッド。** 波を追いかけて、半径ごとの最高を覚える。</summary>
        private static void Watch(uint frame)
        {
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) { _watching = false; return; }

            int step = (int)((frame - _watchStartFrame) / FramesPerWaterStep);

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                float best = 0f;
                int seaCount = 0;

                for (int a = 0; a < WatchAzimuths; a++)
                {
                    float ang = 6.2831853f * a / WatchAzimuths;
                    float rise = RiseAt(terrain,
                                        _centre.X + Mathf.Cos(ang) * WatchRadii[r],
                                        _centre.Z + Mathf.Sin(ang) * WatchRadii[r]);

                    if (rise < 0f) continue;   // 陸。混ぜない
                    seaCount++;
                    if (rise > best) best = rise;
                }

                _watchSeaAzimuths[r] = seaCount;

                if (seaCount > 0 && best > _watchPeak[r])
                {
                    _watchPeak[r] = best;
                    _watchPeakStep[r] = step;
                }
            }

            // ★★ 途中経過。閉じられても、そこまでは分かる。
            if (step > 0 && step % WatchReportEvery == 0)
            {
                Log.Info("tsunami watch @step " + step + " ("
                         + (step * FramesPerWaterStep / 3600f).ToString("F1")
                         + " real min since the drive ended): " + Rings());
            }

            if (step < WatchSteps) return;

            _watching = false;

            Log.Info("tsunami watch finished. " + Rings() + " Reading: a ring whose sea "
                     + "azimuth count is 0 is all land, so nothing can arrive there; heights "
                     + "that collapse between rings mean the sea in between is too shallow "
                     + "to carry the wave.");
        }

        /// <summary>リングごとの一行（途中経過と結びで同じ形にする）。</summary>
        private static string Rings()
        {
            string t = "";

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                if (r > 0) t += " | ";
                t += (WatchRadii[r] / 1000f).ToString("F0") + " km: "
                     + _watchPeak[r].ToString("F1") + " m @step " + _watchPeakStep[r]
                     + " (sea " + _watchSeaAzimuths[r] + "/" + WatchAzimuths
                     + ", shallowest " + _watchMinDepth[r].ToString("F0") + " m)";
            }

            return t;
        }


        /// <summary>
        /// 震源とその縁の海面をのぞく。
        /// **これが実機で「効いたのか」を答える唯一の数字**なので必ず出す。
        /// </summary>
        private static void Observe(TerrainManager terrain)
        {
            // ★ NotSea(-1) は混ぜない（上の RiseAt の ★★）。
            float centre = RiseAt(terrain, _centre.X, _centre.Z);
            _lastCentreMetres = centre > 0f ? centre : 0f;
            if (centre > _peakRiseMetres) _peakRiseMetres = centre;

            // ★ 環ができるのは<b>発生源の縁</b>である。中心だけ見ていると
            //   ②③ に入った瞬間「効いていない」と読み違える（中心は下がるので）。
            float ring = 0f;
            for (int i = 0; i < 4; i++)
            {
                float dx = i == 0 ? TsunamiSource.RadiusMetres
                         : i == 1 ? -TsunamiSource.RadiusMetres : 0f;
                float dz = i == 2 ? TsunamiSource.RadiusMetres
                         : i == 3 ? -TsunamiSource.RadiusMetres : 0f;

                float r = RiseAt(terrain, _centre.X + dx, _centre.Z + dz);
                if (r > ring) ring = r;
            }

            if (ring > _peakRingMetres) _peakRingMetres = ring;
        }

        /// <summary>海の上でない地点。**平均や最大に混ぜてはいけない。**</summary>
        private const float NotSea = -1f;

        /// <summary>
        /// 海面が平常からどれだけ上がっているか（m）。
        /// **海の上でないところは <see cref="NotSea"/> を返す**（呼び出し側は捨てる）。
        ///
        /// ★★ **陸の標高を「波」と読んではいけない。**（2026-08-31、実機の計測で発覚）
        ///   <c>TerrainManager.WaterLevel</c> は<b>地形の高さ + 水柱</b>を返す。
        ///   海面 207 m のマップで標高 277 m の山を叩けば、水が 1 滴も無くても
        ///   <c>WaterLevel - seaLevel = +70 m</c> になる。実際そうなった ——
        ///   「4 km 地点に 70.3 m の波、ただし水深 2 m」という<b>ありえない組</b>で
        ///   気づいた。あれは波ではなく山だった。
        ///
        /// ★ 海底が海面より高いセルは陸である。そこは測らない。
        ///   （岸に乗り上げた水を測るには別の物差しが要る。ここで見たいのは
        ///    <b>沖で波が生きているか</b>である。）
        /// </summary>
        private static float RiseAt(TerrainManager terrain, float x, float z)
        {
            if (x < -MapHalfExtent || x > MapHalfExtent) return NotSea;
            if (z < -MapHalfExtent || z > MapHalfExtent) return NotSea;

            if (IsLand(terrain, x, z)) return NotSea;

            var xz = new Vector2(x, z);
            if (!terrain.HasWater(xz)) return NotSea;

            float rise = terrain.WaterLevel(xz) - _seaLevel;
            if (float.IsNaN(rise)) return NotSea;
            return rise < 0f ? 0f : rise;
        }

        /// <summary>海底が海面より高いか（＝陸か）。**ソルバと同じ配列で見る。**</summary>
        private static bool IsLand(TerrainManager terrain, float x, float z)
        {
            ushort[] block = terrain.BlockHeights;
            if (block == null) return true;

            int at = CellOf(z) * (GridCells + 1) + CellOf(x);
            if (at < 0 || at >= block.Length) return true;

            return block[at] / 64f >= _seaLevel;
        }

        /// <summary>
        /// その地点の水深（m）。**ソルバの流量の上限そのもの**なので、
        /// <b>ソルバが使っているのと同じ量で測らなければならない。</b>
        ///
        /// ★★ <c>SampleRawHeightSmooth</c> で引いてはいけない（2026-08-30、最終検証）。
        ///   IL: <c>WaterSimulation.Initialize</c> が受け取る地形配列は
        ///   <c>TerrainManager.m_blockHeights</c> であり、
        ///   <c>WaterLevel</c> も <c>blockHeights + Cell.m_height</c> を返す。
        ///   ところが <c>SampleRawHeightSmooth</c> は <c>m_rawHeights2</c> を読む。
        ///   両者は<b>岸壁・ダム・護岸・道路の基礎</b>で食い違うので、
        ///   引き算すると水柱ではなく <c>m_height + (block - raw)</c> になる。
        ///   **過大に出た水深はそのまま外力の過大につながり、掘り抜きを招く。**
        /// </summary>
        internal static float DepthAt(TerrainManager terrain, float x, float z)
        {
            float surface = terrain.WaterLevel(new Vector2(x, z));

            ushort[] block = terrain.BlockHeights;
            if (block == null) return 0f;

            // ★ セルの index は TsunamiAI.FindSea の IL 実測と同じ:
            //   (world + 8640) / 16 を 0..1080 に丸め、z * 1081 + x で引く。
            int cx = CellOf(x);
            int cz = CellOf(z);
            int at = cz * (GridCells + 1) + cx;
            if (at < 0 || at >= block.Length) return 0f;

            float ground = block[at] / 64f;

            float depth = surface - ground;
            return float.IsNaN(depth) || depth < 0f ? 0f : depth;
        }

        /// <summary>重ねる波をまとめて作る。**1 個でも作れれば成功。**</summary>
        private static bool CreateAll(TerrainManager terrain)
        {
            int cx = CellOf(_centre.X);
            int cz = CellOf(_centre.Z);

            var data = new WaterWave();
            data.m_type = TypeImpact;
            data.m_origX = (ushort)cx;
            data.m_origZ = (ushort)cz;

            // ★ IL: R = 1 + max(maxX - origX, origX - minX) —— **X しか見ない。**
            data.m_minX = (ushort)Clamp(cx - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxX = (ushort)Clamp(cx + TsunamiSource.RadiusCells, 0, GridCells);
            data.m_minZ = (ushort)Clamp(cz - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxZ = (ushort)Clamp(cz + TsunamiSource.RadiusCells, 0, GridCells);

            data.m_dirX = 0;      // IMPACT では読まれない（IL 実測）
            data.m_dirZ = 0;
            data.m_delta = 0;
            data.m_duration = WaveDurationTicks;
            data.m_currentTime = 0;

            // ★★ **要る本数だけ作る。**（2026-08-30、最終検証）
            //    外力の最大は drive × PushOvershoot なので、いまの帯（≦900）では
            //    Int16 の上限（32767）に遠く届かず、**8 本中 7 本は常に 0** だった。
            //    それでもソルバは<b>全セルで全波の bbox を見に行く</b>ので、
            //    ただの無駄である。使わない波を作らなければ、
            //    <b>ハンドルが迷子になる面積も 8 分の 1</b>になる。
            int needed = TsunamiSource.WavesNeeded(
                (int)(_drive * TsunamiSource.PushOvershoot) + 1);
            if (needed < 1) needed = 1;
            if (needed > _waves.Length) needed = _waves.Length;

            for (int i = 0; i < needed; i++)
            {
                ushort handle;

                // ★★ **戻り値を必ず見る。** false は「上限に達した」で、
                //    無視して 0 を持つと、他人の波を解放しに行くことになる。
                if (!terrain.WaterSimulation.CreateWaterWave(out handle, data) || handle == 0)
                {
                    break;
                }

                _waves[i] = handle;
                _fingerprints[i] = Fingerprint(data);
            }

            return CountWaves() > 0;
        }

        private static int CountWaves()
        {
            int n = 0;
            for (int i = 0; i < _waves.Length; i++) if (_waves[i] != 0) n++;
            return n;
        }

        /// <summary>
        /// 外力を配る。<paramref name="total"/> を <c>MaxDeltaUnits</c> ずつ分けて
        /// 重ねた波に入れる（余った波は 0）。
        ///
        /// ★★ <c>m_waterWaves</c> は public な <c>FastList</c> で、水スレッドは
        ///   <c>Monitor.TryEnter(m_waterWaves, 0)</c> のスピンロックで守って読む
        ///   （IL_0333–033F）。**同じ錠を取ってから書く。**
        /// </summary>
        private static void Write(TerrainManager terrain, int total)
        {
            FastList<WaterWave> list = terrain.WaterSimulation.m_waterWaves;
            if (list == null) return;

            while (!System.Threading.Monitor.TryEnter(list, 0)) { }
            try
            {
                for (int i = 0; i < _waves.Length; i++)
                {
                    if (_waves[i] == 0) continue;

                    int at = _waves[i] - 1;
                    if (at < 0 || at >= list.m_size)
                    {
                        // ★ 台帳から外れた ＝ もう自分の枠ではない。忘れる。
                        _waves[i] = 0;
                        continue;
                    }

                    // ★★ **指紋で本人確認する**（<see cref="_fingerprints"/>）。
                    if (Fingerprint(list.m_buffer[at]) != _fingerprints[i])
                    {
                        _waves[i] = 0;
                        continue;
                    }

                    list.m_buffer[at].m_delta =
                        (short)TsunamiSource.DeltaForWave(i, total);

                    // ★ 寿命を巻き戻して、ソルバの自動解放に先を越されないようにする。
                    list.m_buffer[at].m_currentTime = 0;
                }
            }
            finally
            {
                System.Threading.Monitor.Exit(list);
            }
        }

        /// <summary>
        /// その波が「自分のもの」だと言えるだけの特徴を 1 つの int に畳む。
        /// <c>m_delta</c> と <c>m_currentTime</c> は毎歩書き換えるので<b>入れない</b>。
        /// </summary>
        private static int Fingerprint(WaterWave w)
        {
            int h = w.m_type;
            h = h * 397 ^ w.m_origX;
            h = h * 397 ^ w.m_origZ;
            h = h * 397 ^ w.m_minX;
            h = h * 397 ^ w.m_maxX;
            h = h * 397 ^ w.m_minZ;
            h = h * 397 ^ w.m_maxZ;
            h = h * 397 ^ w.m_duration;
            return h;
        }

        /// <summary>
        /// 置いた波を全部解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに波が残る。
        /// </summary>
        private static void ReleaseAll()
        {
            bool any = false;
            for (int i = 0; i < _waves.Length; i++) if (_waves[i] != 0) any = true;
            if (!any) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain != null && terrain.WaterSimulation != null)
                {
                    FastList<WaterWave> list = terrain.WaterSimulation.m_waterWaves;

                    for (int i = 0; i < _waves.Length; i++)
                    {
                        if (_waves[i] == 0) continue;

                        // ★★ **指紋が合う枠だけ解放する。**（2026-08-30、最終検証）
                        //    合わない枠を解放すると<b>他人の波を消す</b> ——
                        //    ReleaseWaterWave は所有者を確かめないからである。
                        int at = _waves[i] - 1;
                        bool mine = list != null && at >= 0 && at < list.m_size
                                    && Fingerprint(list.m_buffer[at]) == _fingerprints[i];

                        if (mine) terrain.WaterSimulation.ReleaseWaterWave(_waves[i]);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water waves failed ("
                         + e.GetType().Name + "); they may persist in this save");
            }

            for (int i = 0; i < _waves.Length; i++) { _waves[i] = 0; _fingerprints[i] = 0; }
        }

        /// <summary>ワールド座標を 16 m セルへ（<c>SplashWater</c> の IL 実測と同じ式）。</summary>
        private static int CellOf(float world)
        {
            return Clamp((int)((world + MapHalfExtent) / 16f + 0.5f), 0, GridCells);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
