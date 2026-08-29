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
    /// ── ★★ 外力から水位への増幅率は、読んでも決まらなかった ──────────────
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
    /// ソルバの応答は<b>水深に頭打ちされる</b>（<c>v = min(v, m_height)</c>）し、
    /// 周りの海の広さでも変わる。**IL からは決まらない。**
    ///
    /// ★★ だから<b>決め打ちをやめた</b>。①の引き込みのあいだ、
    ///   <b>震源の海面を毎 tick 実際に測って、目標の隆起に届くまで外力を上げる</b>
    ///   （<see cref="TsunamiSource.NextDrive"/>）。増幅率を知らなくても届く。
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
        /// <summary><c>WaterWave.m_type</c> の <c>TYPE_IMPACT</c>（IL 実測）。</summary>
        private const ushort TypeImpact = 2;

        /// <summary>16 m セルの数。<c>(world + 8640) / 16</c> でセルになる（IL 実測）。</summary>
        private const int GridCells = 1080;

        /// <summary>マップ半辺（m）。<c>SplashWater</c> の IL 実測にある 8640 と同じ。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 外力を書き換える間隔（フレーム）。
        /// ★ ソルバは毎フレーム 1 水ステップ進むので、8 フレームなら十分細かい。
        /// </summary>
        private const int IntervalFrames = 8;

        /// <summary>
        /// <c>m_duration</c>（<c>m_currentTime</c> は毎水ステップ +64）。
        /// 4096 ＝ **64 水ステップ ≒ 1 実秒**。
        ///
        /// ★★ **必ず有限にし、しかも短くする。**（Codex レビュー P1）
        ///   <c>WaterWave</c> はセーブに焼き付くのに <see cref="_waves"/> は静的変数なので
        ///   <b>ロードでは戻らない</b>。65535 にすると <c>m_currentTime</c> は
        ///   <c>Min(currentTime + 64, 65535)</c> で<b>張り付き</b>、
        ///   <c>currentTime &gt; duration</c> が永久に成立しない ——
        ///   **津波の最中にセーブした都市は外力を永久に抱える。**
        /// </summary>
        private const ushort WaveDurationTicks = 4096;

        // ── 状態 ──────────────────────────────────────────────

        /// <summary>重ねた波のハンドル。**0 は「その枠は使っていない」。**</summary>
        private static readonly ushort[] _waves =
            new ushort[TsunamiSource.MaxStackedWaves];

        private static bool _running;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static int _drive;
        private static int _delta;
        private static float _targetMetres;
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
                    Singleton<SimulationManager>.instance.m_currentFrameIndex - _startFrame);
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
            _startFrame = 0u;
            _lastFrame = 0u;
            _drive = 0;
            _delta = 0;
            _targetMetres = 0f;
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
            _drive = TsunamiSource.FirstDriveUnits;
            _delta = 0;
            _targetMetres = TsunamiSource.TargetBulgeMetres(intensity);
            _peakRiseMetres = 0f;
            _peakRingMetres = 0f;
            _lastCentreMetres = 0f;

            // ★★ **水深を必ず名乗る。** ソルバの流量は <c>v = min(v, m_height)</c> で
            //    <b>水深に頭打ちされる</b>。「押しても動かない」の第一容疑者はここなので、
            //    ログに出しておかないと次も同じところで詰まる。
            _depthMetres = DepthAt(terrain, epicentre.X, epicentre.Z);

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

            Log.Info("tsunami started at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): " + CountWaves()
                     + " stacked TYPE_IMPACT water waves, radius "
                     + TsunamiSource.RadiusMetres.ToString("F0")
                     + " m. Sea level " + _seaLevel.ToString("F0")
                     + " m, water is " + _depthMetres.ToString("F1")
                     + " m deep here (the solver caps flow at the depth, so a shallow sea "
                     + "cannot carry a big wave). Target bulge "
                     + _targetMetres.ToString("F1") + " m; the drive starts at " + _drive
                     + " units and is RAISED every " + IntervalFrames
                     + " frames until the measured bulge reaches that target (up to "
                     + TsunamiSource.MaxDriveUnits + "). For scale, the DLC tsunami drives "
                     + "the map border with " + TsunamiSource.VanillaDeltaUnits(intensity)
                     + " units");
            return true;
        }

        /// <summary>**sim スレッド、ポーズガードより下。** 外力を進める。</summary>
        public static void Tick(uint frame)
        {
            if (!_running) return;

            try
            {
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
            if (frame - _lastFrame < IntervalFrames) return;
            _lastFrame = frame;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                ReleaseAll();
                _running = false;
                return;
            }

            float elapsed = frame - _startFrame;

            Observe(terrain);

            if (TsunamiSource.IsFinished(elapsed))
            {
                Log.Info("tsunami drive finished after " + elapsed.ToString("F0")
                         + " frames. Drive settled at " + _drive + " units ("
                         + TsunamiSource.WavesNeeded(_drive) + " stacked waves). "
                         + "Highest sea over the epicentre " + _peakRiseMetres.ToString("F1")
                         + " m, over the source rim (" + TsunamiSource.RadiusMetres.ToString("F0")
                         + " m out) " + _peakRingMetres.ToString("F1")
                         + " m. Water depth here was " + _depthMetres.ToString("F1")
                         + " m. The waves are released; the solver carries the ring on its own");
                ReleaseAll();
                _running = false;
                return;
            }

            // ── ★★ ① のあいだだけ、実測を見て外力を決める ─────────────────
            //    ② 以降は「① で見つかった大きさ」をそのまま押し出しに使う。
            //    ② でも上げ続けると外向きの押しが際限なく強くなり、海が壊れる。
            if (elapsed < TsunamiSource.DrawInFrames)
            {
                _drive = TsunamiSource.NextDrive(_drive, _lastCentreMetres, _targetMetres);
            }

            _delta = TsunamiSource.DeltaAt(elapsed, _drive);
            Write(terrain, _delta);
        }

        /// <summary>
        /// 震源とその縁の海面をのぞく。
        /// **これが実機で「効いたのか」を答える唯一の数字**なので必ず出す。
        /// </summary>
        private static void Observe(TerrainManager terrain)
        {
            _lastCentreMetres = RiseAt(terrain, _centre.X, _centre.Z);
            if (_lastCentreMetres > _peakRiseMetres) _peakRiseMetres = _lastCentreMetres;

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

        /// <summary>海面が平常からどれだけ上がっているか（m）。マップの外では 0。</summary>
        private static float RiseAt(TerrainManager terrain, float x, float z)
        {
            if (x < -MapHalfExtent || x > MapHalfExtent) return 0f;
            if (z < -MapHalfExtent || z > MapHalfExtent) return 0f;

            float rise = terrain.WaterLevel(new Vector2(x, z)) - _seaLevel;
            return float.IsNaN(rise) || rise < 0f ? 0f : rise;
        }

        /// <summary>その地点の水深（m）。**ソルバの流量の上限そのもの。**</summary>
        private static float DepthAt(TerrainManager terrain, float x, float z)
        {
            float surface = terrain.WaterLevel(new Vector2(x, z));
            float ground = terrain.SampleRawHeightSmooth(new Vector3(x, 0f, z));

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

            for (int i = 0; i < _waves.Length; i++)
            {
                ushort handle;

                // ★★ **戻り値を必ず見る。** false は「上限に達した」で、
                //    無視して 0 を持つと、他人の波を解放しに行くことになる。
                if (!terrain.WaterSimulation.CreateWaterWave(out handle, data) || handle == 0)
                {
                    break;
                }

                _waves[i] = handle;
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
                    if (at < 0 || at >= list.m_size) continue;

                    // ★ 枠が生きているか確かめる。他人の枠を踏まない保証である。
                    if (list.m_buffer[at].m_type != TypeImpact) continue;

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
                    for (int i = 0; i < _waves.Length; i++)
                    {
                        if (_waves[i] == 0) continue;
                        terrain.WaterSimulation.ReleaseWaterWave(_waves[i]);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water waves failed ("
                         + e.GetType().Name + "); they may persist in this save");
            }

            for (int i = 0; i < _waves.Length; i++) _waves[i] = 0;
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
