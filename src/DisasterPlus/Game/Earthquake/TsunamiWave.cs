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
    /// ここは<b>それをゲームの水シミュへ 1 個の波として渡すだけ</b>である。
    ///
    /// ── 何を作り直したか（2026-08-29）───────────────────────────
    ///
    /// 所有者:「natural DisasterDLC の津波のメカニズムを研究して、
    ///        私が求めているものを一から作り直してください。」
    ///
    /// 研究は <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>。要点:
    ///
    /// > バニラの津波は<b>外周の一区画で海面を 1.5 周期上下させる境界条件</b>で、
    /// > 水の壁はすべてゲームの浅水ソルバの伝播である。発生源は 256 フレームしかない。
    ///
    /// ソルバには<b>マップのどこにでも置ける外力</b>があり（<c>TYPE_IMPACT</c>）、
    /// それは「そこに水の山があるかのように水面の傾きを足す」——
    /// <b>海底の隆起と同じ</b>、津波の教科書どおりの発生源である。
    /// だから震源にそれを置けば、同心円状の水の壁は<b>ソルバが作ってくれる</b>。
    ///
    /// ★★ **旧 <c>TsunamiSurge</c> を消した理由。** あれは水源 240 個で海面を塗り、
    ///   <c>SplashWater</c> を連射して壁を描いていた。**ソルバの中を通っていない。**
    ///   だから壁にならず、減衰し、水源の届く範囲で止まった。原理から違っていた。
    ///
    /// ── ★★ 守ること ────────────────────────────────────────
    ///
    /// <list type="bullet">
    /// <item><c>WaterWave</c> は<b>セーブに焼き付く</b>（<c>WaterWave.Serialize</c> がある）。
    ///   <see cref="Reset"/> で問答無用に解放する</item>
    /// <item><c>m_delta</c> の書き換えは <c>m_waterWaves</c> の
    ///   <c>Monitor.TryEnter</c> の中で行う（水スレッドが同じ配列を読む）</item>
    /// <item><c>m_duration</c> は寿命より十分長くする ——
    ///   <b>ソルバが勝手に解放した枠を、こちらが二度解放しないため</b></item>
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
        /// ★★ **必ず有限にし、しかも短くする。**（2026-08-29、Codex レビュー P1）
        ///   <c>WaterWave</c> は<b>セーブに焼き付く</b>のに、こちらの
        ///   <c>_waveIndex</c> は静的変数なので<b>ロードでは戻らない</b>。
        ///   65535 にしていたら、<c>m_currentTime</c> は
        ///   <c>Min(currentTime + 64, 65535)</c> で<b>65535 に張り付き</b>、
        ///   <c>currentTime &gt; duration</c> が永久に成立しない ——
        ///   **津波の最中にセーブした都市は、外へ押し続ける外力を永久に抱える。**
        ///
        /// ★ 有限にしておけば、こちらが書き換えを止めた時点（＝ロードで
        ///   ハンドルを失った時点）から 1 秒でソルバが自分で解放する。
        ///   生かし続けるのは <see cref="Write"/> が
        ///   <see cref="IntervalFrames"/>（8 フレーム）ごとに
        ///   <c>m_currentTime</c> を 0 へ戻すからで、**8 &lt;&lt; 64 の余裕がある。**
        /// </summary>
        private const ushort WaveDurationTicks = 4096;

        // ── 状態 ──────────────────────────────────────────────

        private static bool _running;
        private static ushort _waveIndex;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static int _peakUnits;
        private static int _delta;
        private static float _peakRiseMetres;
        private static float _seaLevel;
        private static Vec3 _centre;
        private static bool _errorLogged;

        /// <summary>今 津波が動いているか（診断・表示用）。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>今の外力（<c>m_delta</c> の単位。負＝中心へ、正＝外へ）。</summary>
        public static int DeltaUnits { get { return _delta; } }

        /// <summary>この地震が出せる外力の頂点（同上、診断用）。</summary>
        public static int PeakUnits { get { return _peakUnits; } }

        /// <summary>震源のまわりで観測したいちばん高い海面（m、診断用）。</summary>
        public static float PeakRiseMetres { get { return _peakRiseMetres; } }

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
            Release();

            _running = false;
            _startFrame = 0u;
            _lastFrame = 0u;
            _peakUnits = 0;
            _delta = 0;
            _peakRiseMetres = 0f;
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
                Release();
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
            //   海溝型地震はもともと沖に置いているが、断ったことは名乗る。
            if (!terrain.HasWater(new Vector2(epicentre.X, epicentre.Z)))
            {
                Detail = "the epicentre is not on open water; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            _centre = epicentre;
            _peakUnits = TsunamiSource.PeakDeltaUnits(intensity);
            _delta = 0;
            _peakRiseMetres = 0f;

            if (_peakUnits <= 0)
            {
                Detail = "the quake is too weak to move the sea; no tsunami";
                return false;
            }

            if (!Create(terrain, 0))
            {
                Detail = "the water simulation refused the wave; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _running = true;
            _startFrame = frame;
            _lastFrame = frame;
            Detail = null;

            Log.Info("tsunami started at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): one TYPE_IMPACT water wave, "
                     + "radius " + TsunamiSource.RadiusMetres.ToString("F0")
                     + " m, peak drive " + _peakUnits + " units ("
                     + (_peakUnits / (float)TsunamiSource.UnitsPerMetre).ToString("F0")
                     + " m of virtual sea-floor uplift; the DLC tsunami uses "
                     + TsunamiSource.VanillaDeltaUnits(intensity)
                     + " on the map border). The drive runs for "
                     + TsunamiSource.TotalFrames.ToString("F0")
                     + " frames and then stops - the concentric wall of water after "
                     + "that is the game's own water solver, exactly as it is for the "
                     + "DLC tsunami. Sea level here is " + _seaLevel.ToString("F0") + " m");
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
                Release();
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
                Release();
                _running = false;
                return;
            }

            float elapsed = frame - _startFrame;

            Observe(terrain);

            if (TsunamiSource.IsFinished(elapsed))
            {
                Log.Info("tsunami drive finished after " + elapsed.ToString("F0")
                         + " frames; the wave is released and the water solver carries "
                         + "the ring on its own. Highest sea seen over the epicentre was "
                         + _peakRiseMetres.ToString("F1") + " m above sea level");
                Release();
                _running = false;
                return;
            }

            _delta = TsunamiSource.DeltaAt(elapsed, _peakUnits);
            Write(terrain, _delta);
        }

        /// <summary>
        /// 震源の海面をのぞいて、いちばん高かった値を覚える（診断用）。
        /// **これが実機で「効いたのか」を答える唯一の数字**なので必ず出す。
        /// </summary>
        private static void Observe(TerrainManager terrain)
        {
            float best = 0f;

            for (int i = 0; i < 5; i++)
            {
                // 中心と、東西南北に半径のおよそ半分。
                float dx = i == 1 ? TsunamiSource.RadiusMetres * 0.5f
                         : i == 2 ? -TsunamiSource.RadiusMetres * 0.5f : 0f;
                float dz = i == 3 ? TsunamiSource.RadiusMetres * 0.5f
                         : i == 4 ? -TsunamiSource.RadiusMetres * 0.5f : 0f;

                float x = _centre.X + dx;
                float z = _centre.Z + dz;
                if (x < -MapHalfExtent || x > MapHalfExtent) continue;
                if (z < -MapHalfExtent || z > MapHalfExtent) continue;

                float rise = terrain.WaterLevel(new Vector2(x, z)) - _seaLevel;
                if (rise > best) best = rise;
            }

            if (best > _peakRiseMetres) _peakRiseMetres = best;
        }

        /// <summary>
        /// 波を 1 個作る。**ハンドルは 1 基点**（<c>CreateWaterWave</c> の IL 実測）。
        /// </summary>
        private static bool Create(TerrainManager terrain, int delta)
        {
            var data = new WaterWave();
            data.m_type = TypeImpact;

            int cx = CellOf(_centre.X);
            int cz = CellOf(_centre.Z);

            data.m_origX = (ushort)cx;
            data.m_origZ = (ushort)cz;

            // ★ IL: R = 1 + max(maxX - origX, origX - minX) —— **X しか見ない。**
            //   端でクランプされて半径が縮まないよう、X は必ず両側を確保できる
            //   ぶんだけ内側に寄せて考える（Z は bbox が切れても R は変わらない）。
            data.m_minX = (ushort)Clamp(cx - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxX = (ushort)Clamp(cx + TsunamiSource.RadiusCells, 0, GridCells);
            data.m_minZ = (ushort)Clamp(cz - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxZ = (ushort)Clamp(cz + TsunamiSource.RadiusCells, 0, GridCells);

            data.m_dirX = 0;      // IMPACT では読まれない（IL 実測）
            data.m_dirZ = 0;
            data.m_delta = (short)Clamp(delta, -TsunamiSource.MaxDeltaUnits,
                                        TsunamiSource.MaxDeltaUnits);
            data.m_duration = WaveDurationTicks;
            data.m_currentTime = 0;

            // ★★ **戻り値を必ず見る。** false は「上限に達した」で、
            //    無視して 0 を持つと、他人の波を解放しに行くことになる。
            ushort handle;
            if (!terrain.WaterSimulation.CreateWaterWave(out handle, data) || handle == 0)
            {
                return false;
            }

            _waveIndex = handle;
            return true;
        }

        /// <summary>
        /// 外力を書き換える。
        ///
        /// ★★ <c>m_waterWaves</c> は public な <c>FastList</c> で、水スレッドは
        ///   <c>Monitor.TryEnter(m_waterWaves, 0)</c> のスピンロックで守って読む
        ///   （IL_0333–033F）。**同じ錠を取ってから書く。**
        ///
        /// ★ 枠が生きているか（<c>m_type == TYPE_IMPACT</c>）を必ず確かめる ——
        ///   <c>ReleaseWaterWave</c> は末尾の空き枠だけ詰めるので添字は動かないが、
        ///   それでも他人の枠を踏まない保証を書いておく。
        /// </summary>
        private static void Write(TerrainManager terrain, int delta)
        {
            if (_waveIndex == 0) return;

            FastList<WaterWave> list = terrain.WaterSimulation.m_waterWaves;
            if (list == null) return;

            while (!System.Threading.Monitor.TryEnter(list, 0)) { }
            try
            {
                int at = _waveIndex - 1;
                if (at < 0 || at >= list.m_size) return;
                if (list.m_buffer[at].m_type != TypeImpact) return;

                list.m_buffer[at].m_delta =
                    (short)Clamp(delta, -TsunamiSource.MaxDeltaUnits,
                                 TsunamiSource.MaxDeltaUnits);

                // ★ 寿命を巻き戻して、ソルバの自動解放に先を越されないようにする。
                list.m_buffer[at].m_currentTime = 0;
            }
            finally
            {
                System.Threading.Monitor.Exit(list);
            }
        }

        /// <summary>
        /// 置いた波を解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに波が残る。
        /// </summary>
        private static void Release()
        {
            if (_waveIndex == 0) return;

            ushort handle = _waveIndex;
            _waveIndex = 0;
            _delta = 0;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain != null && terrain.WaterSimulation != null)
                {
                    terrain.WaterSimulation.ReleaseWaterWave(handle);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water wave failed ("
                         + e.GetType().Name + "); it may persist in this save");
            }
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
