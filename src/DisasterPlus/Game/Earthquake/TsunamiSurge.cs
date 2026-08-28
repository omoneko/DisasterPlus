using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>海溝型地震の津波。</b>震源を中心に 2〜3 本の波が続けて外へ広がる。
    /// **sim スレッド専用。**
    ///
    /// ── 所有者の指示（2026-08-25）─────────────────────────────────
    ///
    /// &gt; DLC の津波を使うのをやめましょう。代わりに海溝型地震の震源地付近を
    /// &gt; 中心とした領域で一定時間持続的な海面上昇（震源地を中心に２-3 個の
    /// &gt; 連続する山状：実際の津波メカニズムで）を発生させてください。
    ///
    /// 波の形は <see cref="TsunamiWaveTrain"/>（Core・テスト付き）が持つ。
    /// ここは<b>それをゲームの水面に写すだけ</b>である。
    ///
    /// ── どうやって海面を上げるのか ───────────────────────────────
    ///
    /// <c>m_currentSeaLevel</c> は<b>全マップ一律</b>なので局所的な波にならない
    /// （④の <c>TyphoonFlood</c> のクラス doc に同じ調査がある）。
    /// 使えるのは <c>TYPE_NATURAL</c> の水源で、あれは
    /// <b>目標水位 <c>m_target</c> まで注ぎ、超えたら吸い戻す自己調整の泉</b>である。
    ///
    /// 震源のまわりの海に格子状に水源を置き、**1 つ 1 つの目標水位を
    /// 波の式で毎 tick 書き換える**。波が来れば目標が上がって水が乗り、
    /// 波が過ぎれば目標が下がって<b>同じ泉が水を吸い戻す</b>。
    ///
    /// ★★ <b>これが <c>CreateWaterSource</c> を使ってよい理由である。</b>
    ///   ④の <c>TyphoonFlood</c> のクラス doc は「新しい泉を置くな」と書いている ——
    ///   あれは<b>注ぐだけの泉</b>を想定した警告で、止めたあとに残った水を
    ///   引かせる手段が無くなることを心配していた。<c>TYPE_NATURAL</c> は
    ///   <b>吸い戻す側も同じ泉が持っている</b>ので、目標を海面へ戻してから
    ///   解放すれば水は残らない。
    ///
    /// ── ★★ 絶対に守ること: 水源はセーブに焼き付く ──────────────────────
    ///
    /// <c>WaterSimulation.Data.Serialize</c> は水源をセーブに書く（§D-4）。
    /// **解放し忘れた泉は、MOD を外しても都市に残り続ける。**
    /// だから
    ///
    /// <list type="bullet">
    /// <item><see cref="Reset"/> は<b>問答無用で全部解放する</b>（レベルアンロード）</item>
    /// <item>波列が終わったら<b>目標を海面へ戻し</b>、<see cref="DrainFrames"/> だけ
    ///   待ってから解放する（吸い戻す時間を与える）</item>
    /// <item>台帳（<see cref="_sources"/>）以外の場所に泉の番号を持たない</item>
    /// </list>
    /// </summary>
    public static class TsunamiSurge
    {
        /// <summary><c>WaterSource.m_type</c> の <c>TYPE_NATURAL</c>（④の調査と同じ）。</summary>
        private const ushort TypeNatural = 1;

        /// <summary><c>m_target</c> の 1 m ぶん（④の <c>FloodTarget.UnitsPerMetre</c> と同じ）。</summary>
        private const int UnitsPerMetre = 64;

        /// <summary>泉を置く格子の間隔（m）。**波の幅より細かくする**（跨がれない）。</summary>
        private const float SpacingMetres = 480f;

        /// <summary>
        /// 震源からこの距離までに泉を置く（m）。
        ///
        /// ★★ 4200 -> 8200（2026-08-29、実機報告「海面全体が持ち上がるレベル
        ///   じゃないと津波っぽさは出ない」）。<c>TsunamiWaveTrain</c> の台地は
        ///   <c>PlateauEdgeMetres</c> まで海を持ち上げるので、**泉もそこまで
        ///   置かないと、台地の外側だけが上がらない**。
        /// </summary>
        private const float ReachMetres = TsunamiWaveTrain.ReachEdgeMetres;

        /// <summary>
        /// 置く泉の数の上限。**水シミュの負荷はここで決まる。**
        /// 増やす前に実機で測ること。
        /// </summary>
        /// <remarks>
        /// ★★ 160 -> 300（2026-08-29）。半径 8.2 km を間隔 480 m で覆うには
        ///   最大 900 個ほど要るが、水シミュの負荷を見て 300 で切る。
        ///   **近い順に採る**ので、切られるのはいちばん外側である。
        /// </remarks>
        private const int MaxSources = 300;

        /// <summary>
        /// 注ぐ／吸う速さ。**この MOD が決めた値**（プレハブ由来ではない）。
        ///
        /// ★★ 60000 -> 250000（2026-08-29）。津波は<b>半径 3 km の海を 20 m 上げる</b>
        ///   —— 川の氾濫とは桁が違う体積である。遅いと目標水位に届く前に
        ///   波が通り過ぎてしまい、**何も起きていないように見える。**
        ///
        /// ★ この単位の意味はゲーム側の水シミュにしか無く、**IL からは読めなかった。**
        ///   ここは実機で見ながら決める数字であって、物理量ではない。
        /// </summary>
        private const uint Rate = 250000u;

        /// <summary>
        /// 目標を海面へ戻してから解放するまで待つフレーム数。
        ///
        /// ★★ 900 -> 3600（2026-08-29、実機報告「水源をすぐに除去してしまうと
        ///   ただの高潮になってしまっています」）。**吸い戻すには注ぐより時間がかかる**
        ///   —— 引くのを待たずに泉を消すと、水が引く相手を失って残る。
        /// </summary>
        private const int DrainFrames = 3600;

        /// <summary>海面が読めないときの既定（m）。<c>WaterSimulation.DEFAULT_SEA_LEVEL</c>。</summary>
        private const float DefaultSeaLevelMetres = 40f;

        /// <summary>更新の間隔（フレーム）。毎フレームは要らない。</summary>
        private const int IntervalFrames = 16;

        // ── ★★ 目に見える「波の壁」（2026-08-29）──────────────────────────
        //
        // 泉の目標水位を上げるのは<b>じわじわ効く</b>ので、「海面が上がった」は
        // 作れても「波が来た」は作れない。そこへ
        // <c>DisasterHelpers.SplashWater(position, radius, depth)</c> を重ねる。
        //
        // IL 実測（SplashWater、IL_0000-0135）:
        //
        //     cells   = CeilToInt(radius / 16)
        //     delta   = Clamp(CeilToInt(depth * 64), -32767, 32767)   // 65536/1024 = 64
        //     origX   = Clamp((x + 8640)/16 + 0.5, 0, 1080)           // 16 m セル
        //     m_type  = 2 (IMPACT) / m_duration = 256 / m_dirX = m_dirZ = 0
        //
        // ★ IMPACT の波は <c>SimulateWater</c> が<b>セルの水量に delta を足す</b>
        //   （IL_099A-09C3）。**外周リング専用の TYPE_TSUNAMI と違い、どこにでも置ける。**
        //   これが「震源を中心に」を満たす唯一のバニラ経路である。

        /// <summary>
        /// 波の壁を撃つ間隔（フレーム）。
        ///
        /// ★★ 64 -> 20（2026-08-29、実機報告「求めているのは水の壁が同心円状に
        ///   生成される挙動」）。64 フレームおきだと、45 m/s の前線は
        ///   <b>1 発ごとに 48 m しか進まない</b>——のではなく、実際には
        ///   飛び飛びに現れて<b>輪が途切れる</b>。20 フレームなら前線の動きに追随する。
        /// </summary>
        private const int SplashIntervalFrames = 20;

        /// <summary>1 発の波の壁の半径（m）。**壁の厚み**である。</summary>
        private const float SplashRadiusMetres = 700f;

        /// <summary>波の壁の高さ（そのときの持ち上がりに対する比）。</summary>
        private const float SplashDepthFraction = 0.85f;

        /// <summary>
        /// 隣り合う発の重なり。1 未満で**必ず重ねる** ——
        /// 1 以上にすると隣との間に切れ目ができ、<b>輪ではなく点線</b>になる。
        /// </summary>
        private const float SplashOverlap = 0.72f;

        /// <summary>1 回に撃つ数の上限。**円周が伸びても際限なく増やさない。**</summary>
        private const int MaxSplashesPerPulse = 40;

        /// <summary>同じく下限（前線が小さいうちでも輪に見えるように）。</summary>
        private const int MinSplashesPerPulse = 8;

        private static uint _lastSplashFrame;
        private static int _splashPulses;

        /// <summary>撃った波の壁の回数（診断用）。</summary>
        public static int SplashPulses { get { return _splashPulses; } }

        private static int _splashesLastPulse;

        /// <summary>直近の 1 回で輪に並べた発の数（診断用）。**8 のままなら輪が細い。**</summary>
        public static int SplashesLastPulse { get { return _splashesLastPulse; } }

        /// <summary>置いた泉 1 つぶん。</summary>
        private struct Spring
        {
            public readonly ushort Handle;

            /// <summary>震源からの距離（m）。波の式に渡す。</summary>
            public readonly float DistanceMetres;

            public Spring(ushort handle, float distanceMetres)
            {
                Handle = handle;
                DistanceMetres = distanceMetres;
            }
        }

        private static readonly List<Spring> _sources = new List<Spring>();

        private static bool _running;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static uint _drainFromFrame;
        private static float _amplitude;
        private static float _seaLevel;
        private static float _peakRise;
        private static Vec3 _centre;
        private static bool _errorLogged;

        /// <summary>今 津波が動いているか（診断・表示用）。</summary>
        public static bool Running { get { return _running; } }

        /// <summary>置いている泉の数（診断用）。</summary>
        public static int SourceCount { get { return _sources.Count; } }

        /// <summary>これまでに観測したいちばん高い持ち上がり（m、診断用）。</summary>
        public static float PeakRiseMetres { get { return _peakRise; } }

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
            _sources.Clear();
            _peakRise = 0f;

            // ── 震源のまわりの海に格子状に置く ────────────────────────
            //
            // ★★ **近い順に選ぶ。**（2026-08-29、ログを読んで気づいた）
            //    以前は格子を <c>gz</c> の小さいほうから走査し、
            //    <see cref="MaxSources"/> に達したところで打ち切っていた。
            //    候補は 300 個以上あるので、**採れるのは南側の数列だけ**になり、
            //    <b>震源の片側にしか波が立たなかった</b>。
            //    いったん候補を集めて距離で並べ替え、近いものから採る。
            int steps = (int)(ReachMetres / SpacingMetres);
            var candidates = new List<Vector3>();   // x, z, distance

            for (int gz = -steps; gz <= steps; gz++)
            {
                for (int gx = -steps; gx <= steps; gx++)
                {
                    float ox = gx * SpacingMetres;
                    float oz = gz * SpacingMetres;
                    float d = Mathf.Sqrt(ox * ox + oz * oz);
                    if (d > ReachMetres) continue;

                    float x = epicentre.X + ox;
                    float z = epicentre.Z + oz;

                    // ★ 海の上にだけ置く。陸に置くと、そこから水が湧いて
                    //   「津波」ではなく「泉」になる。
                    if (!terrain.HasWater(new Vector2(x, z))) continue;

                    candidates.Add(new Vector3(x, d, z));
                }
            }

            candidates.Sort(delegate(Vector3 a, Vector3 b)
            {
                return a.y.CompareTo(b.y);
            });

            for (int i = 0; i < candidates.Count && _sources.Count < MaxSources; i++)
            {
                ushort handle;
                if (!TryCreate(terrain, candidates[i].x, candidates[i].z, out handle)) continue;

                _sources.Add(new Spring(handle, candidates[i].y));
            }

            if (_sources.Count == 0)
            {
                Detail = "no open water around the epicentre; no tsunami was raised";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _running = true;
            _centre = epicentre;
            _lastSplashFrame = frame;
            _splashPulses = 0;
            _splashesLastPulse = 0;
            _startFrame = frame;
            _lastFrame = frame;
            _drainFromFrame = 0u;
            Detail = null;

            Log.Info("tsunami raised at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): " + _sources.Count
                     + " water sources over the sea, peak wave "
                     + _amplitude.ToString("F1") + " m, "
                     + TsunamiWaveTrain.CrestCount + " crests "
                     + TsunamiWaveTrain.CrestGapSeconds.ToString("F0")
                     + " s apart travelling outward at "
                     + TsunamiWaveTrain.SpeedMetresPerSecond.ToString("F0")
                     + " m/s over " + TsunamiWaveTrain.TotalSeconds.ToString("F0")
                     + " s. Sea level here is " + _seaLevel.ToString("F0") + " m. "
                     + "The DLC TsunamiAI is NOT used (it can only start from the map edge)");
            return true;
        }

        /// <summary>
        /// **sim スレッド、ポーズガードより下。** 波を進める。
        /// </summary>
        public static void Tick(uint frame, float framesPerMinute)
        {
            if (!_running) return;

            try
            {
                Step(frame, framesPerMinute);
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

        private static void Step(uint frame, float framesPerMinute)
        {
            if (frame - _lastFrame < IntervalFrames) return;
            _lastFrame = frame;

            // ── 排水待ち。目標はもう海面に戻してある ──────────────────
            if (_drainFromFrame != 0u)
            {
                if (frame - _drainFromFrame < DrainFrames) return;

                Log.Info("tsunami finished; " + _sources.Count
                         + " water sources released (the sea is back to "
                         + _seaLevel.ToString("F0") + " m)");
                ReleaseAll();
                _running = false;
                return;
            }

            // ★ ゲーム内の秒。フレームから出す（**実時間ではない** ——
            //   ゲーム速度を変えたら波もそれに追随するのが正しい）。
            float minutes = framesPerMinute > 0f
                ? (frame - _startFrame) / framesPerMinute
                : 0f;
            float seconds = minutes * 60f;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                ReleaseAll();
                _running = false;
                return;
            }

            if (seconds >= TsunamiWaveTrain.TotalSeconds)
            {
                // ★★ **目標を海面へ戻す。** 戻さずに解放すると、持ち上がった水が
                //    引く相手を失って残る（クラス doc）。
                LowerAllToSeaLevel(terrain);
                _drainFromFrame = frame;
                return;
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                Spring spring = _sources[i];

                float rise = TsunamiWaveTrain.RiseAt(spring.DistanceMetres, seconds, _amplitude);
                if (rise > _peakRise) _peakRise = rise;

                SetTarget(terrain, spring.Handle, _seaLevel + rise);
            }

            // ★★ **目に見える波の壁**（上の doc）。前線に沿って円周上へ撃つ。
            if (frame - _lastSplashFrame >= SplashIntervalFrames)
            {
                _lastSplashFrame = frame;
                Splash(seconds);
            }
        }

        /// <summary>
        /// いちばん外の波の前線に沿って、バニラの <c>SplashWater</c> を円周上に撃つ。
        /// **これが「波が来た」を作る**（泉の目標水位はじわじわしか効かない）。
        /// </summary>
        private static void Splash(float seconds)
        {
            // ★★ **壁は「波列のいちばん外の山」の位置にある。**
            //    ここを LeadingFront にしておかないと、内側の山にも壁が立って
            //    <b>輪が何重にも重なって見える</b>。
            float front = TsunamiWaveTrain.LeadingFrontAt(seconds);
            if (front <= SplashRadiusMetres) front = SplashRadiusMetres;
            if (front > TsunamiWaveTrain.ReachEdgeMetres) return;   // もう外へ出た

            float rise = TsunamiWaveTrain.RiseAt(front, seconds, _amplitude);
            if (!(rise > 0.5f)) return;

            float depth = rise * SplashDepthFraction;

            // ★★ **数は円周で決める。**（2026-08-29）
            //    固定 8 発だと、半径 4 km（円周 25 km）では 3 km ごとに
            //    1 発しか置けず、**輪ではなく点が 8 個**にしかならなかった。
            //    隣どうしが重なる数を計算する。
            float step = SplashRadiusMetres * 2f * SplashOverlap;
            int count = Mathf.CeilToInt(6.2831853f * front / step);
            if (count < MinSplashesPerPulse) count = MinSplashesPerPulse;
            if (count > MaxSplashesPerPulse) count = MaxSplashesPerPulse;

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

                DisasterHelpers.SplashWater(at, SplashRadiusMetres, depth);
                placed++;
            }

            _splashesLastPulse = placed;
            _splashPulses++;
        }

        /// <summary>マップ半辺（m）。<c>SplashWater</c> の IL 実測にある 8640 と同じ。</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// 泉を 1 つ置く。**ハンドルは 1 基点**（<c>LockWaterSource</c> の IL 実測）。
        /// </summary>
        private static bool TryCreate(TerrainManager terrain, float x, float z,
                                      out ushort handle)
        {
            handle = 0;

            var data = new WaterSource();
            data.m_type = TypeNatural;
            data.m_inputPosition = new Vector3(x, _seaLevel, z);
            data.m_outputPosition = new Vector3(x, _seaLevel, z);
            data.m_inputRate = Rate;
            data.m_outputRate = Rate;
            data.m_target = (ushort)Mathf.Clamp(
                Mathf.RoundToInt(_seaLevel * UnitsPerMetre), 0, 65535);

            // ★★ **戻り値を必ず見る。** false は「上限に達した」で、
            //    無視して 0 を台帳へ入れると、他人の泉を解放しに行くことになる。
            return terrain.WaterSimulation.CreateWaterSource(out handle, data) && handle != 0;
        }

        /// <summary>
        /// 1 つの泉の目標水位を書く。
        ///
        /// ★★ <c>UnlockWaterSource</c> は**必ず <c>finally</c> で呼ぶ。**
        ///   <c>LockWaterSource</c> は <c>Monitor.TryEnter</c> のスピンロックで、
        ///   <b>解放経路は <c>UnlockWaterSource</c> ただ 1 つ</b>である（IL 実測）。
        ///   落とすと<b>ゲームが無反応になる</b>（④の <c>TyphoonFlood</c> の罠 3）。
        /// </summary>
        private static void SetTarget(TerrainManager terrain, ushort handle, float metres)
        {
            if (handle == 0) return;

            WaterSource data = terrain.WaterSimulation.LockWaterSource(handle);
            try
            {
                int units = Mathf.RoundToInt(metres * UnitsPerMetre);
                if (units < 0) units = 0;
                if (units > 65535) units = 65535;
                data.m_target = (ushort)units;
            }
            finally
            {
                terrain.WaterSimulation.UnlockWaterSource(handle, data);
            }
        }

        private static void LowerAllToSeaLevel(TerrainManager terrain)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                SetTarget(terrain, _sources[i].Handle, _seaLevel);
            }
        }

        /// <summary>
        /// 置いた泉を全部解放する。**冪等。例外を投げない。**
        /// ここが最後の砦である —— 通らないとセーブに泉が残る。
        /// </summary>
        private static void ReleaseAll()
        {
            if (_sources.Count == 0) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain != null && terrain.WaterSimulation != null)
                {
                    for (int i = 0; i < _sources.Count; i++)
                    {
                        if (_sources[i].Handle == 0) continue;
                        terrain.WaterSimulation.ReleaseWaterSource(_sources[i].Handle);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water sources failed ("
                         + e.GetType().Name + "); they may persist in this save");
            }

            _sources.Clear();
            _drainFromFrame = 0u;
        }
    }
}
