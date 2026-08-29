using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>海溝型地震の津波。</b>震源から水の壁が同心円状に広がる。**sim スレッド専用。**
    ///
    /// ── 所有者の指示（2026-08-29）─────────────────────────────────
    ///
    /// &gt; 地震→すぐに震源地に海面の巨大な隆起が生成→隆起が台地状に拡大→
    /// &gt; ある程度大きくなったら中央は緩やかに沈下して元の海面に（ドーナツ状）→
    /// &gt; 同心円状の水の壁が、減衰することなく拡散する
    ///
    /// 波の形と時計は <see cref="TsunamiWaveTrain"/>（Core・テスト付き）が持つ。
    /// **ここはそれをゲームの水面に写すだけ**である。
    ///
    /// ── どうやって海面を上げるのか ───────────────────────────────
    ///
    /// <c>m_currentSeaLevel</c> は<b>全マップ一律</b>なので局所的な波にならない。
    /// 使えるのは <c>TYPE_NATURAL</c> の水源で、あれは
    /// <b>目標水位 <c>m_target</c> まで注ぎ、超えたら吸い戻す自己調整の泉</b>である。
    ///
    /// ★★ <b>これが <c>CreateWaterSource</c> を使ってよい理由である。</b>
    ///   ④の <c>TyphoonFlood</c> のクラス doc は「新しい泉を置くな」と書いている ——
    ///   あれは<b>注ぐだけの泉</b>を想定した警告だった。<c>TYPE_NATURAL</c> は
    ///   <b>吸い戻す側も同じ泉が持っている</b>ので、目標を海面へ戻してから
    ///   解放すれば水は残らない。
    ///
    /// ── ★★ 2026-08-29 の作り直し（実機報告「高さと波の継続力がとても弱い」）───
    ///
    /// 数えたら 3 つとも算数で説明がついた。**3 つとも直してある。**
    ///
    /// <code>
    ///   ① 時計がゲーム内秒だった
    ///        1 sim フレーム = 1.32 ゲーム内秒（DAYTIME_FRAMES 65536/日）。
    ///        「900 ゲーム内秒」は 683 フレーム ≒ 11 実秒でしかなく、
    ///        環が広がる区間に至っては 94 フレーム ≒ 1.6 実秒だった。
    ///        → 時計を sim フレームに変えた（TsunamiWaveTrain のクラス doc）。
    ///
    ///   ② 水源どうしが届いていなかった
    ///        作用半径 = Sqrt(rate)*0.4+10 [m]（IL 実測）。
    ///        rate 250,000 → 210 m、直径 420 m。間隔は 480 m。
    ///        ＝ 隣と 60 m 離れていて一度も繋がらない。
    ///        持ち上がるのは点々とした円盤で、水はすぐ隙間へ落ちる。
    ///        → 作用半径 390 m（<see cref="Rate"/>）、間隔はそこから導く
    ///          （<see cref="TsunamiSourceLayout.StepFor"/>）。
    ///
    ///   ③ 水源が前線についていけなかった
    ///        300 個 × 480 m が覆えるのは半径 4,691 m まで。
    ///        その先（〜8,200 m）には水源が 1 つも無かった。
    ///        → **撒くのをやめた。** 毎 tick、環の帯に並べ直す。
    /// </code>
    ///
    /// ── ★★ 絶対に守ること: 水源はセーブに焼き付く ──────────────────────
    ///
    /// <c>WaterSimulation.Data.Serialize</c> は水源をセーブに書く（§D-4）。
    /// **解放し忘れた泉は、MOD を外しても都市に残り続ける。**
    /// だから
    ///
    /// <list type="bullet">
    /// <item><see cref="Reset"/> は<b>問答無用で全部解放する</b>（レベルアンロード）</item>
    /// <item>波が終わったら<b>目標を海面へ戻し</b>、<see cref="DrainFrames"/> だけ
    ///   待ってから解放する（吸い戻す時間を与える）</item>
    /// <item>台帳（<see cref="_handles"/>）以外の場所に泉の番号を持たない</item>
    /// </list>
    /// </summary>
    public static class TsunamiSurge
    {
        /// <summary><c>WaterSource.m_type</c> の <c>TYPE_NATURAL</c>（④の調査と同じ）。</summary>
        private const ushort TypeNatural = 1;

        /// <summary><c>m_target</c> の 1 m ぶん（④の <c>FloodTarget.UnitsPerMetre</c> と同じ）。</summary>
        private const int UnitsPerMetre = 64;

        /// <summary>
        /// 注ぐ／吸う速さ。**作用半径はここで決まる**（<see cref="TsunamiSourceLayout"/>）。
        ///
        /// <code>  Sqrt(900000) * 0.4 + 10 = 390 m  </code>
        ///
        /// ★★ 250,000（＝210 m）から上げた。間隔 480 m の格子に対して直径 420 m では
        ///   <b>隣と繋がらず、壁にならなかった</b>（クラス doc の原因 ②）。
        ///
        /// ★ 大きくすると 1 個あたりの走査セル数が半径の 2 乗で増える
        ///   （390 m なら約 1,866 セル ×2 周）。**上げる前に実機で測ること。**
        /// </summary>
        private const uint Rate = 900000u;

        /// <summary>
        /// 同時に置く水源の数。**水シミュの負荷はここ × 作用面積で決まる。**
        ///
        /// ★ 環がいちばん外（8,200 m）に居るとき、帯 900 m を 3 本の同心円で覆うと
        ///   1 本あたり約 78 個 ＝ 234 個。ここを下回ると<b>輪に切れ目ができる</b>。
        ///
        /// ★★ 前の版と違い、これは「撒く数」ではなく「**並べ直す数**」である。
        ///   余った個体は流量 0 にして黙らせる —— IL に <c>inRate &gt; 0</c> /
        ///   <c>outRate &gt; 0</c> の門があるので、流量 0 の水源は 1 セルも走査されない。
        /// </summary>
        private const int MaxSources = 240;

        /// <summary>
        /// 目標を海面へ戻してから解放するまで待つフレーム数（≒ 60 実秒）。
        /// **吸い戻すには注ぐより時間がかかる** —— 引くのを待たずに泉を消すと、
        /// 水が引く相手を失って残る。
        /// </summary>
        private const int DrainFrames = 3600;

        /// <summary>海面が読めないときの既定（m）。<c>WaterSimulation.DEFAULT_SEA_LEVEL</c>。</summary>
        private const float DefaultSeaLevelMetres = 40f;

        /// <summary>
        /// 並べ直す間隔（フレーム）。
        ///
        /// ★ 環は 2.4 m/フレームで進むので、8 フレームで 19 m。
        ///   帯の幅 900 m に対して十分細かい。**16 に戻さないこと** ——
        ///   時計がフレームになる前は 16 フレームで 949 m 進んでいて、
        ///   <b>帯の幅より大きく飛んでいた</b>。
        /// </summary>
        private const int IntervalFrames = 8;

        /// <summary>衝撃波を撃つ間隔（フレーム）。深さは <see cref="TsunamiSplash"/> が割る。</summary>
        private const int SplashIntervalFrames = 30;

        /// <summary>マップ半辺（m）。<c>SplashWater</c> の IL 実測にある 8640 と同じ。</summary>
        private const float MapHalfExtent = 8640f;

        // ── 状態 ──────────────────────────────────────────────

        /// <summary>
        /// 置いた泉。**枠は <see cref="MaxSources"/> ぶん確保し、使うのは
        /// <see cref="_count"/> 個まで。**
        ///
        /// ★★ 配列は<b>泉を 1 つも作る前に</b>据える。ローカルの一覧に溜めてから
        ///   代入すると、作成の途中で例外が飛んだときに
        ///   <b>作った泉の番号がどこにも残らず、解放できない</b>
        ///   —— そのままセーブに焼き付く（クラス doc）。
        /// </summary>
        private static ushort[] _handles = new ushort[0];

        /// <summary><see cref="_handles"/> のうち実際に作れた数。</summary>
        private static int _count;

        /// <summary>
        /// その泉が今「黙っている」（流量 0）か。<see cref="_handles"/> と同じ長さ。
        /// **同じ状態に書き直しに行かないためだけの覚え書き**である。
        /// </summary>
        private static bool[] _silent = new bool[0];

        /// <summary>並べ直す先を受ける作業用。**毎 tick 確保しない**（GC を出さない）。</summary>
        private static readonly float[] _layout = new float[MaxSources * 2];

        private static bool _running;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static uint _drainFromFrame;
        private static uint _lastSplashFrame;
        private static float _lastSplashFront;
        private static int _splashPulses;
        private static int _splashesLastPulse;
        private static int _activeSources;
        private static float _amplitude;
        private static float _seaLevel;
        private static float _peakRise;
        private static Vec3 _centre;
        private static bool _errorLogged;

        /// <summary>今 津波が動いているか（診断・表示用）。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>置いている泉の数（診断用）。</summary>
        public static int SourceCount { get { return _count; } }

        /// <summary>そのうち今まさに水を動かしている数（診断用）。</summary>
        public static int ActiveSourceCount { get { return _activeSources; } }

        /// <summary>これまでに観測したいちばん高い持ち上がり（m、診断用）。</summary>
        public static float PeakRiseMetres { get { return _peakRise; } }

        /// <summary>撃った衝撃波の回数（診断用）。</summary>
        public static int SplashPulses { get { return _splashPulses; } }

        /// <summary>直近の 1 回で輪に並べた発の数（診断用）。</summary>
        public static int SplashesLastPulse { get { return _splashesLastPulse; } }

        /// <summary>直近の顛末（**英語・診断用**）。断ったときは必ず入る。</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// **レベルのロード／アンロードで必ず呼ぶ。** 置いた泉を<b>問答無用で解放する</b>。
        /// 呼び忘れるとセーブに残る（クラス doc）。
        /// </summary>
        public static void Reset()
        {
            ReleaseAll();

            _running = false;
            _startFrame = 0u;
            _lastFrame = 0u;
            _drainFromFrame = 0u;
            _lastSplashFrame = 0u;
            _lastSplashFront = 0f;
            _splashPulses = 0;
            _splashesLastPulse = 0;
            _activeSources = 0;
            _amplitude = 0f;
            _peakRise = 0f;
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
                Log.Error("tsunami surge failed to start", e);
                // ★ 途中まで置いた泉を残さない。
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

            _seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            if (float.IsNaN(_seaLevel) || _seaLevel <= 0f) _seaLevel = DefaultSeaLevelMetres;

            _amplitude = TsunamiWaveTrain.AmplitudeOf(intensity);
            _peakRise = 0f;

            // ── 泉をまとめて作る。位置と流量は毎 tick 書き換えるので、
            //    ここでは全部を震源に置いて<b>黙らせておく</b>（流量 0）。
            //
            // ★★ 台帳を<b>先に</b>据えてから作る。作った番号がその場で台帳に載るので、
            //    途中で落ちても Begin の catch が ReleaseAll で回収できる。
            _handles = new ushort[MaxSources];
            _silent = new bool[MaxSources];
            _count = 0;

            for (int i = 0; i < MaxSources; i++) _silent[i] = true;   // 流量 0 で作る

            for (int i = 0; i < MaxSources; i++)
            {
                ushort handle;
                if (!TryCreate(terrain, epicentre.X, epicentre.Z, out handle)) break;

                _handles[_count] = handle;
                _count++;
            }

            if (_count == 0)
            {
                Detail = "the water simulation refused every water source; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _running = true;
            _centre = epicentre;
            _startFrame = frame;
            _lastFrame = frame;
            _drainFromFrame = 0u;
            _lastSplashFrame = frame;
            _lastSplashFront = 0f;
            _splashPulses = 0;
            _splashesLastPulse = 0;
            _activeSources = 0;
            Detail = null;

            float radius = TsunamiSourceLayout.RadiusForRate(Rate);

            Log.Info("tsunami raised at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): " + _count
                     + " water sources (reach " + radius.ToString("F0")
                     + " m each) that are RE-LAID along the wave front every "
                     + IntervalFrames + " frames, peak wave "
                     + _amplitude.ToString("F1") + " m. The ring spreads at "
                     + TsunamiWaveTrain.SpeedMetresPerFrame.ToString("F1")
                     + " m/frame and leaves the reach at frame "
                     + TsunamiWaveTrain.RingLeavesAtFrame.ToString("F0") + " of "
                     + TsunamiWaveTrain.TotalFrames.ToString("F0")
                     + ". Sea level here is " + _seaLevel.ToString("F0") + " m. "
                     + "The DLC TsunamiAI is NOT used (it can only start from the map edge)");
            return true;
        }

        /// <summary>
        /// **sim スレッド、ポーズガードより下。** 波を進める。
        ///
        /// ★ <paramref name="framesPerMinute"/> はもう<b>要らない</b>（時計がフレームに
        ///   なったので）。呼び出し側の形を変えないために受け取るだけ。
        /// </summary>
        public static void Tick(uint frame, float framesPerMinute)
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
                    Log.Error("tsunami surge failed", e);
                }

                // ★★ **落ちたら畳む。** 持ち上げた水位を書き換える経路が
                //    止まったまま泉が残ると、海がずっと高いままになる。
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

            // ── 排水待ち。目標はもう海面に戻してある ──────────────────
            if (_drainFromFrame != 0u)
            {
                if (frame - _drainFromFrame < DrainFrames) return;

                Log.Info("tsunami finished; " + _count
                         + " water sources released (the sea is back to "
                         + _seaLevel.ToString("F0") + " m). Peak rise seen was "
                         + _peakRise.ToString("F1") + " m over " + _splashPulses
                         + " impact-wave pulses");
                ReleaseAll();
                _running = false;
                return;
            }

            float elapsed = frame - _startFrame;

            if (elapsed >= TsunamiWaveTrain.TotalFrames)
            {
                // ★★ **目標を海面へ戻す。** 戻さずに解放すると、持ち上がった水が
                //    引く相手を失って残る（クラス doc）。衝撃波が足した水も
                //    ここで吸わせるので、**円盤いっぱいに広げてから**下げる。
                LayOutForDrain(terrain);
                _drainFromFrame = frame;
                return;
            }

            LayOutOnTheWave(terrain, elapsed);

            if (frame - _lastSplashFrame >= SplashIntervalFrames)
            {
                Splash(elapsed, frame);
            }
        }

        /// <summary>
        /// 今の帯（<c>[環の内側, 環]</c>）に泉を並べ直し、それぞれの目標水位を書く。
        /// 余った泉は<b>流量 0</b> にして黙らせる。
        /// </summary>
        private static void LayOutOnTheWave(TerrainManager terrain, float elapsed)
        {
            float ring = TsunamiWaveTrain.RingRadiusAt(elapsed);
            if (ring > TsunamiWaveTrain.ReachEdgeMetres) ring = TsunamiWaveTrain.ReachEdgeMetres;

            float inner = TsunamiWaveTrain.RingInnerRadiusAt(elapsed);
            if (inner > ring) inner = ring;

            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            float step = TsunamiSourceLayout.StepFor(radius);

            // ★ 継ぎ目を毎回わずかに回す。固定すると同じ方位に筋が残る。
            float spin = _splashPulses * 0.37f + elapsed * 0.001f;

            int used = TsunamiSourceLayout.Fill(inner, ring, step, spin,
                                                _layout, _count);

            int live = 0;

            for (int i = 0; i < _count; i++)
            {
                if (i >= used)
                {
                    Silence(terrain, i);
                    continue;
                }

                float dx = _layout[i * 2];
                float dz = _layout[i * 2 + 1];
                float x = _centre.X + dx;
                float z = _centre.Z + dz;

                // ★ マップの外には置かない。外周のセルは水シミュが折り返して扱うので、
                //   はみ出した泉は<b>反対側の海を持ち上げる</b>おそれがある。
                if (x < -MapHalfExtent || x > MapHalfExtent
                    || z < -MapHalfExtent || z > MapHalfExtent)
                {
                    Silence(terrain, i);
                    continue;
                }

                float distance = Mathf.Sqrt(dx * dx + dz * dz);

                float rise = TsunamiWaveTrain.RiseAt(distance, elapsed, _amplitude);
                if (rise > _peakRise) _peakRise = rise;

                Place(terrain, i, x, z, _seaLevel + rise, Rate);
                live++;
            }

            _activeSources = live;
        }

        /// <summary>
        /// 後始末の配置。**円盤いっぱいに広げて、目標を海面に落とす。**
        /// 衝撃波が足した水は環の通り道すべてに残っているので、
        /// 帯だけに置いたままだと吸う相手がいない。
        /// </summary>
        private static void LayOutForDrain(TerrainManager terrain)
        {
            float step = TsunamiSourceLayout.DrainStepFor(
                TsunamiWaveTrain.ReachEdgeMetres, _count);

            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            if (step < TsunamiSourceLayout.StepFor(radius))
            {
                step = TsunamiSourceLayout.StepFor(radius);
            }

            int used = TsunamiSourceLayout.Fill(0f, TsunamiWaveTrain.ReachEdgeMetres,
                                                step, 0f, _layout, _count);

            int live = 0;

            for (int i = 0; i < _count; i++)
            {
                if (i >= used)
                {
                    Silence(terrain, i);
                    continue;
                }

                float x = _centre.X + _layout[i * 2];
                float z = _centre.Z + _layout[i * 2 + 1];

                // ★★ **並べるときと同じマップ外ガードを掛ける。**
                //    震源がマップ中心から 440 m も離れれば、届く限界（8,200 m）の
                //    円盤は半辺（8,640 m）をはみ出す。外へ置いた泉は
                //    <b>関係のない外周を触る</b>ので、後始末でこそ避ける。
                if (x < -MapHalfExtent || x > MapHalfExtent
                    || z < -MapHalfExtent || z > MapHalfExtent)
                {
                    Silence(terrain, i);
                    continue;
                }

                Place(terrain, i, x, z, _seaLevel, Rate);
                live++;
            }

            _activeSources = live;

            Log.Info("tsunami is draining: " + live
                     + " water sources spread over the whole reach with their target back "
                     + "at sea level (" + _seaLevel.ToString("F0") + " m) for "
                     + DrainFrames + " frames");
        }

        /// <summary>
        /// いちばん外の波の前線に沿って、バニラの <c>SplashWater</c> を円周上に撃つ。
        /// **これが「波が来た」を作る**（泉の目標水位はじわじわしか効かない）。
        /// </summary>
        private static void Splash(float elapsed, uint frame)
        {
            // ★★ **壁が立つ前は撃たない。** ①②（隆起と台地）のあいだは
            //    まだ「壁」ではないので、衝撃波を重ねると輪が 2 本に見える。
            if (elapsed < TsunamiWaveTrain.SpreadFrames) return;

            float front = TsunamiWaveTrain.RingRadiusAt(elapsed);
            if (front > TsunamiWaveTrain.ReachEdgeMetres) return;   // もう外へ出た
            if (front <= 0f) return;

            float rise = TsunamiWaveTrain.RiseAt(front, elapsed, _amplitude);
            if (!(rise > 0.5f)) return;

            // ★★ **前線が前回からどれだけ進んだかで深さを割る。**
            //    SplashWater は足し算なので、割らないと同じセルに何十発も積み上がる
            //    （TsunamiSplash のクラス doc）。
            float advance = _lastSplashFront > 0f ? front - _lastSplashFront : front;
            float depth = TsunamiSplash.DepthFor(rise, advance);

            _lastSplashFrame = frame;
            _lastSplashFront = front;

            if (!(depth > 0f)) return;

            int count = TsunamiSplash.CountFor(front);

            // ★ 発の並びを毎回わずかに回す。回さないと、同じ方位に穴が残り続けて
            //   <b>輪に切れ目の筋</b>が見える。
            float spin = _splashPulses * 0.37f;

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                float a = 6.2831853f * i / count + spin;
                var at = new Vector2(_centre.X + Mathf.Cos(a) * front,
                                     _centre.Z + Mathf.Sin(a) * front);

                // ★ マップの外へ撃たない（SplashWater は自分でクランプするが、
                //   外周へ寄せた波が 1 か所に固まるのを避ける）。
                if (at.x < -MapHalfExtent || at.x > MapHalfExtent) continue;
                if (at.y < -MapHalfExtent || at.y > MapHalfExtent) continue;

                DisasterHelpers.SplashWater(at, TsunamiSplash.RadiusMetres, depth);
                placed++;
            }

            _splashesLastPulse = placed;
            _splashPulses++;
        }

        /// <summary>
        /// 泉を 1 つ作る。**ハンドルは 1 基点**（<c>LockWaterSource</c> の IL 実測）。
        /// 流量 0 で作る —— 位置も目標も、最初の tick が書く。
        /// </summary>
        private static bool TryCreate(TerrainManager terrain, float x, float z,
                                      out ushort handle)
        {
            handle = 0;

            var data = new WaterSource();
            data.m_type = TypeNatural;
            data.m_inputPosition = new Vector3(x, _seaLevel, z);
            data.m_outputPosition = new Vector3(x, _seaLevel, z);
            data.m_inputRate = 0u;
            data.m_outputRate = 0u;
            data.m_target = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(_seaLevel * UnitsPerMetre), 0, 65535);

            // ★★ **戻り値を必ず見る。** false は「上限に達した」で、
            //    無視して 0 を台帳へ入れると、他人の泉を解放しに行くことになる。
            return terrain.WaterSimulation.CreateWaterSource(out handle, data) && handle != 0;
        }

        /// <summary>
        /// 泉を 1 つ、指定の場所・目標水位・流量に置き直す。
        ///
        /// ★★ <c>UnlockWaterSource</c> は**必ず <c>finally</c> で呼ぶ。**
        ///   <c>LockWaterSource</c> は <c>Monitor.TryEnter</c> のスピンロックで、
        ///   <b>解放経路は <c>UnlockWaterSource</c> ただ 1 つ</b>である（IL 実測）。
        ///   落とすと<b>ゲームが無反応になる</b>（④の <c>TyphoonFlood</c> の罠 3）。
        /// </summary>
        private static void Place(TerrainManager terrain, int index,
                                  float x, float z, float metres, uint rate)
        {
            if (index < 0 || index >= _count) return;

            ushort handle = _handles[index];
            if (handle == 0) return;

            _silent[index] = rate == 0u;

            WaterSource data = terrain.WaterSimulation.LockWaterSource(handle);
            try
            {
                int units = Mathf.RoundToInt(metres * UnitsPerMetre);
                if (units < 0) units = 0;
                if (units > 65535) units = 65535;

                data.m_type = TypeNatural;
                data.m_target = (ushort)units;
                data.m_inputPosition = new Vector3(x, metres, z);
                data.m_outputPosition = new Vector3(x, metres, z);
                data.m_inputRate = rate;
                data.m_outputRate = rate;
            }
            finally
            {
                terrain.WaterSimulation.UnlockWaterSource(handle, data);
            }
        }

        /// <summary>
        /// 使わない泉を黙らせる。**流量 0 の水源は 1 セルも走査されない** ——
        /// IL の吸い込み側／吐き出し側の両方に <c>rate &gt; 0</c> の門がある
        /// （IL_1A2E / IL_1DC4）。だから余りを抱えていても負荷にならない。
        /// </summary>
        private static void Silence(TerrainManager terrain, int index)
        {
            // ★ もう黙っているものにロックを取りに行かない。並べ直す帯が小さい
            //   あいだは余りが 200 個を超えるので、これが無いと毎 tick
            //   200 回の <c>Monitor.TryEnter</c> を無駄に回すことになる。
            if (index >= 0 && index < _silent.Length && _silent[index]) return;

            Place(terrain, index, _centre.X, _centre.Z, _seaLevel, 0u);
        }

        /// <summary>
        /// 置いた泉を全部解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに泉が残る。
        /// </summary>
        private static void ReleaseAll()
        {
            if (_handles.Length == 0) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain != null && terrain.WaterSimulation != null)
                {
                    // ★ 枠ぜんぶを見る（_count ではなく）。作成の途中で落ちたときに
                    //   _count がまだ進んでいない可能性があるため、**取りこぼさない**。
                    for (int i = 0; i < _handles.Length; i++)
                    {
                        if (_handles[i] == 0) continue;
                        terrain.WaterSimulation.ReleaseWaterSource(_handles[i]);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water sources failed ("
                         + e.GetType().Name + "); they may persist in this save");
            }

            _handles = new ushort[0];
            _silent = new bool[0];
            _count = 0;
            _activeSources = 0;
            _drainFromFrame = 0u;
        }
    }
}
