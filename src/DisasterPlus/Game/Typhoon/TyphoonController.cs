using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④の論理オブジェクト本体 —— **台風のモデル**（経路・強度・位相・上陸予測）と、
    /// その寿命の管理。<b>sim スレッド専用。</b>
    ///
    /// バニラの災害バッファに触るのは <see cref="TyphoonSlot"/> の仕事で、
    /// **このファイルには <c>DisasterManager</c> / <c>DisasterAI</c> への呼び出しが
    /// 1 つも無い**（<see cref="DisasterData"/> の配列を <see cref="TyphoonSlot"/> から
    /// 受け取って渡し直すだけ）。分けた理由はあちらのクラス doc。
    ///
    /// ── なぜ④が自分で動かすのか ──────────────────────────────
    ///
    /// <c>DisasterManager.SimulationStepImpl</c> は
    /// <c>idx = m_currentFrameIndex &amp; 255</c> で 1 呼び出しにつき災害 1 個しか進めないので、
    /// **災害 i の <c>SimulationStep</c> は 256 sim フレームに 1 回しか回らない**
    /// （IL 事実文書 §E-2）。滑らかに動かすには④が <c>OnAfterSimulationTick</c> から
    /// 毎 tick 座標を書くしかない。
    ///
    /// ── <c>m_targetPosition</c> を書いてよい根拠（T3 で再実測した） ──────────
    ///
    /// 全アセンブリで <c>stfld DisasterData::m_targetPosition</c> を走査した結果、
    /// 書き手は 19 メソッドあるが、**既に存在する災害のそれを書くものは 1 つも無い**。
    /// 全て「<c>CreateDisaster</c> の直後に、今作ったスロットへ初期位置を入れる」か、
    /// セーブの <c>Deserialize</c> か、プレイヤーが移動ツールで掴んだとき
    /// （<c>DefaultTool+&lt;EndMoving&gt;</c>）である。<c>ThunderStormAI</c> 側は
    /// <c>SimulationStep</c> でも <c>ActivateDisaster</c> でも一切書かない。
    /// **つまり Active 中にバニラと殴り合わない。** 位置に追随するのは落雷の散布中心・
    /// ハザード円盤・災害マーカー・<c>FindDisaster(Vector3)</c>（§E-1）。
    ///
    /// ── 同時に 1 個だけ ────────────────────────────────────
    ///
    /// 既に動いていれば <see cref="TyphoonRequest.Start"/> は無視して理由を
    /// <see cref="LastRefusal"/> に残す。②の <see cref="TsunamiChain"/> /
    /// <c>LongPeriodDamage</c> と同じ制限で、④が上限 256 の災害スロットを
    /// 食い潰す形を作らないためである。
    ///
    /// ── 真の中心とクランプした写し ──────────────────────────────
    ///
    /// **④は「本当の中心」を自分で持ち、災害には <see cref="TyphoonSlot.WriteTarget"/> が
    /// クランプした写しを書く。** 終了判定は必ずクランプ前の中心で行う（理由は
    /// あちらの doc）。
    /// </summary>
    public static class TyphoonController
    {
        /// <summary>強度の下限。<c>.cgs</c> の値は公開契約なので範囲外でも読み捨てず、
        /// 使う側でクランプする（②の <c>EarthquakeLongPeriodStrength</c> と同じ扱い）。</summary>
        private const int MinIntensity = 10;

        private const int MaxIntensity = 255;

        /// <summary>上陸予測の先読みサンプル数の上限（計画 §3.6）。</summary>
        private const int LandfallSamples = 64;

        /// <summary>
        /// 上陸予測のサンプル間隔（フレーム）。
        ///
        /// ★ **これが予測の粒度そのものである。** 256 フレーム ＝ ゲーム内でおよそ
        ///   5.6 分なので、<see cref="MinutesToLandfall"/> の実際の分解能は
        ///   「約 5.6 分」であって「0.1 分」ではない。値そのものは
        ///   （経路が閉じた式なので）安定して単調に減るが、**持っていない精度を
        ///   表示で主張しない** —— 表示は F0 に丸め、刻みを
        ///   <see cref="LandfallStepMinutesText"/> で名乗る（全体レビュー）。
        /// </summary>
        private const uint LandfallStepFrames = 256u;

        /// <summary>
        /// 上陸予測を打ち直す間隔（フレーム）。
        ///
        /// **経路は elapsedFrames の閉じた関数**（<see cref="TyphoonTrack.CentreAt"/>）
        /// なので、上陸「フレーム」の予測値は tick が進んでも変わらない。毎 tick 打ち直すと
        /// 同じ答えを出すために <c>HasWater</c> を 64 回（＝水シミュのロックを 64 回）
        /// 取り直すだけになる。表示する「あと何分」は、キャッシュした上陸フレームと
        /// 現在フレームの差から毎 tick 計算するので、値は 1 フレーム刻みで滑らかに減る。
        /// </summary>
        private const uint LandfallRescanFrames = 256u;

        private static bool _active;

        private static uint _seed;
        private static float _speed;
        private static byte _peakIntensity;
        private static uint _totalFrames;
        private static uint _elapsedFrames;
        private static uint _lastFrame;
        private static float _decay;
        private static float _prefabRadius;

        /// <summary>クランプ前の真の中心。マップ外にもなる。</summary>
        private static Vec2 _centre;
        private static float _centreHeight;
        private static float _heading;
        private static byte _intensity;
        private static float _stormRadius;
        private static float _galeRadius;
        private static TyphoonPhase _phase;
        private static bool _overLand;

        /// <summary>1 度でもマップの中に入ったか。終了判定に使う（<see cref="Advance"/>）。</summary>
        private static bool _wasInsideMap;

        private static bool _landfallKnown;
        private static uint _landfallFrame;
        private static bool _landfallScanned;
        private static uint _landfallScanFrame;

        private static string _lastRefusal;

        /// <summary>例外を 1 回だけ <c>Log.Error</c> で出したか。<see cref="Reset"/> で戻さない
        /// （ゲームのビルドに対する事実であって都市ごとの状態ではない）。</summary>
        private static bool _errorLogged;

        public static bool Active { get { return _active; } }

        public static ushort DisasterId { get { return TyphoonSlot.Id; } }

        /// <summary>
        /// <c>StartDisaster</c> が予定した活性化フレーム。
        /// **落雷の予算がバニラの取り分を見積もる起点**でもある（T6）。
        /// </summary>
        public static uint ActivationFrame { get { return TyphoonSlot.ActivationFrame; } }

        /// <summary>
        /// 真の中心（クランプ前）。Y は地形高のサンプルで、マップ外では地形グリッドが
        /// 端でクランプされるため「いちばん近い縁の高さ」になる。
        /// </summary>
        public static Vec3 Centre { get { return new Vec3(_centre.X, _centreHeight, _centre.Z); } }

        public static float HeadingRadians { get { return _heading; } }

        public static byte Intensity { get { return _intensity; } }

        public static float StormRadius { get { return _stormRadius; } }

        public static float GaleRadius { get { return _galeRadius; } }

        /// <summary>
        /// この台風が使っている <c>ThunderStormAI.m_radius</c>（プレハブ実測値）。
        /// **0 は「読めていない」である**（設計書 §6。<c>TyphoonPrefabFacts.Usable</c> が
        /// false のとき台風はそもそも起きないので、Active 中は必ず正）。
        ///
        /// <see cref="StormRadius"/> / <see cref="GaleRadius"/> は今の強度での**結果**で、
        /// こちらは <c>TyphoonProfile.WindAt</c> / <c>StormRadiusOf</c> に渡す**入力**である。
        /// T7 の風害が距離ごとの風速相当を求めるのに要る ——
        /// 結果から割り戻すと強度 0 のとき 0 除算になる。
        /// </summary>
        public static float PrefabRadius { get { return _prefabRadius; } }

        public static TyphoonPhase Phase { get { return _phase; } }

        public static uint ElapsedFrames { get { return _elapsedFrames; } }

        public static uint TotalFrames { get { return _totalFrames; } }

        public static bool OverLand { get { return _overLand; } }

        /// <summary>上陸予測が立っているか。**false は「0 分後」ではなく「このまま
        /// 海上を通過する（か、先読みの範囲に陸が無い）」である。** 0 と混ぜないこと。</summary>
        public static bool LandfallKnown { get { return _landfallKnown; } }

        /// <summary>上陸までのゲーム内分。<see cref="LandfallKnown"/> が true のときだけ意味を持つ。
        /// **分解能は <see cref="LandfallStepFrames"/> 刻み**（その doc）。</summary>
        public static float MinutesToLandfall { get; private set; }

        /// <summary>
        /// 上陸予測の刻み（ゲーム内分）を人が読める形で。表示側が
        /// 「この数字はこれくらいの粒度でしか打っていない」と名乗るために使う。
        /// <c>FramesPerMinute</c> が読めなければフレーム数のまま名乗る
        /// （推測した分に換算しない）。
        /// </summary>
        public static string LandfallStepMinutesText()
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return LandfallStepFrames + " frames";
            return (LandfallStepFrames / framesPerMinute).ToString("F1") + " in-game minutes";
        }

        /// <summary>
        /// 直近で台風を起こせなかった／手放した理由（英語、診断用）。
        /// **「起こせなかった」を「何も起きていない」と見分ける手段がここにしか無い。**
        /// </summary>
        public static string LastRefusal { get { return _lastRefusal; } }

        /// <summary>
        /// **レベルアンロードで必ず呼ぶ。** 進行中の台風も予約も都市をまたいで残らない。
        /// スロットには触らない —— 前の都市の災害バッファはもう存在しない。
        /// </summary>
        public static void Reset()
        {
            Forget();
            _lastRefusal = null;
            // ★ _errorLogged は戻さない（クラス doc）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>TyphoonFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に台風が動く）。
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step(snapshot, frame, deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon controller failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyCtl",
                             "typhoon controller failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            // ★ 1 tick に 1 回だけ（TyphoonHub.TakeRequest の doc）。
            var request = TyphoonHub.TakeRequest();

            if (request == TyphoonRequest.Stop && _active) Stop();
            else if (request == TyphoonRequest.Start) Start(snapshot, frame);

            if (!_active) return;

            DisasterData[] buffer;
            string lostReason;
            if (!TyphoonSlot.TryGetBuffer(out buffer, out lostReason))
            {
                LoseSlot(lostReason);
                return;
            }

            Advance(buffer, frame, deltaMinutes);
        }

        /// <summary>
        /// 台風を起こす。<c>DisasterTool.&lt;CreateDisaster&gt;c__Iterator0.MoveNext</c> の
        /// 手順そのまま（§E-3）で、②の <c>TsunamiChain.Raise</c> と同型である。
        /// スロットの取得と開始は <see cref="TyphoonSlot"/> が持ち、ここは
        /// **起こしてよいかの判断と、④のモデルの初期化**だけを行う。
        /// </summary>
        private static void Start(TyphoonSnapshot snapshot, uint frame)
        {
            if (_active)
            {
                // 同時に 1 個だけ（クラス doc）。黙って無視しない。
                _lastRefusal = "a typhoon is already running; only one at a time";
                return;
            }

            if (snapshot == null || !snapshot.Valid)
            {
                _lastRefusal = "the simulation could not be read this tick";
                return;
            }

            var prefab = snapshot.Prefab;
            if (!prefab.Usable)
            {
                // 設計書 §6: 読めなければ推測せず何もしない。
                _lastRefusal = "ThunderStormAI prefab values are unreadable "
                             + "(m_radius / m_activeDuration); the mod will not guess them";
                return;
            }

            float speed = TyphoonTrack.SpeedFor(prefab.ActiveDuration);
            if (speed <= 0f)
            {
                _lastRefusal = "travel speed is unknown (m_activeDuration is 0)";
                return;
            }

            string refusal;
            if (!TyphoonSlot.Create(out refusal))
            {
                _lastRefusal = refusal;
                return;
            }

            // 種は災害 ID。同じセーブなら同じ経路になる（設計書 §4.1）。
            _seed = TyphoonSlot.Id;
            _speed = speed;
            _peakIntensity = ClampIntensity(ModSettings.TyphoonIntensity.value);
            _totalFrames = prefab.ActiveDuration;
            _prefabRadius = prefab.StormRadius;
            _elapsedFrames = 0u;
            _lastFrame = frame;
            _decay = 0f;
            _wasInsideMap = false;
            _landfallKnown = false;
            _landfallScanned = false;
            _landfallScanFrame = 0u;
            MinutesToLandfall = 0f;

            _centre = TyphoonTrack.CentreAt(_seed, 0u, _speed);
            _heading = TyphoonTrack.HeadingAt(_seed, 0u, _speed);
            _phase = TyphoonTrack.PhaseAt(0u, _totalFrames);
            _intensity = TyphoonTrack.IntensityAt(_peakIntensity, 0u, _totalFrames, 0f);
            _stormRadius = TyphoonProfile.StormRadiusOf(_intensity, _prefabRadius);
            _galeRadius = TyphoonProfile.GaleRadiusOf(_intensity, _prefabRadius);

            var pos = new Vector3(_centre.X, 0f, _centre.Z);
            if (!TyphoonSlot.Begin(ref pos, _heading, _intensity, out refusal))
            {
                _lastRefusal = refusal;
                Forget();
                return;
            }

            _centreHeight = pos.y;
            _active = true;
            _lastRefusal = null;

            Log.Info("typhoon started: disaster #" + TyphoonSlot.Id
                     + " intensity=" + _intensity
                     + " speed=" + _speed.ToString("F3") + " m/frame"
                     + " duration=" + _totalFrames + " frames");
        }

        /// <summary>
        /// **黙って手放さない。** 理由を 1 行出して <see cref="Forget"/> する。
        /// <see cref="Stop"/> ではない —— **もう④のものではないので触ってはいけない。**
        /// </summary>
        private static void LoseSlot(string reason)
        {
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyLost",
                     "typhoon released its disaster slot: " + reason);
            Forget();
            _lastRefusal = reason;
        }

        /// <summary>毎 tick の本体（計画 §3.3）。</summary>
        private static void Advance(DisasterData[] buffer, uint frame, float deltaMinutes)
        {
            if (frame > _lastFrame) _elapsedFrames += frame - _lastFrame;
            _lastFrame = frame;

            // 経路は elapsedFrames の閉じた関数。積算しない（Core の doc）。
            _centre = TyphoonTrack.CentreAt(_seed, _elapsedFrames, _speed);
            _heading = TyphoonTrack.HeadingAt(_seed, _elapsedFrames, _speed);

            bool inside = TyphoonTrack.IsInsideMap(_centre);
            if (inside) _wasInsideMap = true;

            _overLand = inside && !HasWaterAt(_centre);
            _decay = TyphoonTrack.DecayAfter(_decay, _overLand, deltaMinutes);
            _intensity = TyphoonTrack.IntensityAt(_peakIntensity, _elapsedFrames,
                                                  _totalFrames, _decay);
            _stormRadius = TyphoonProfile.StormRadiusOf(_intensity, _prefabRadius);
            _galeRadius = TyphoonProfile.GaleRadiusOf(_intensity, _prefabRadius);
            _phase = TyphoonTrack.PhaseAt(_elapsedFrames, _totalFrames);

            var pos = new Vector3(_centre.X, 0f, _centre.Z);
            TyphoonSlot.WriteTarget(buffer, ref pos, _heading, _intensity);
            _centreHeight = pos.y;

            UpdateLandfall(frame);

            // ── 終了判定。**必ずクランプ前の中心で行う**（クラス doc）。 ──────────
            //
            // 計画 §3.5 は 1 つ目の条件を「マップの外、かつ elapsed が進入に要する
            // フレーム数を超えた」と書いている。ここでは「1 度でも中に入ったことが
            // あるか」（_wasInsideMap）で同じことを**推定ではなく観測で**判定する。
            // 進入に要するフレーム数を別途見積もると、その見積りが外れたときに
            // 「到着する前に台風が終わる」という、例外の出ない壊れ方をする。
            if (_wasInsideMap && !inside)
            {
                Log.Info("typhoon #" + TyphoonSlot.Id + " left the map");
                Stop();
                return;
            }

            if (_phase == TyphoonPhase.Gone)
            {
                Log.Info("typhoon #" + TyphoonSlot.Id + " used up its lifetime ("
                         + _totalFrames + " frames)");
                Stop();
            }
        }

        /// <summary>
        /// 上陸予測（設計書 §7.2）。経路が閉じた式なので先読みは単なるサンプリングである。
        ///
        /// <c>HasWater</c> は水シミュのロックを取るので**打ち直しは
        /// <see cref="LandfallRescanFrames"/> フレームに 1 回・上限
        /// <see cref="LandfallSamples"/> サンプル**に抑える（その定数の doc）。
        /// </summary>
        private static void UpdateLandfall(uint frame)
        {
            if (_overLand)
            {
                _landfallKnown = true;
                MinutesToLandfall = 0f;
                _landfallFrame = frame;
                _landfallScanned = false;
                return;
            }

            if (!_landfallScanned || frame - _landfallScanFrame >= LandfallRescanFrames)
            {
                _landfallScanFrame = frame;
                _landfallScanned = true;
                ScanLandfall(frame);
            }

            if (!_landfallKnown) return;

            if (_landfallFrame <= frame)
            {
                // 予測した時刻を過ぎたのにまだ海の上。次の走査で取り直す。
                _landfallKnown = false;
                MinutesToLandfall = 0f;
                return;
            }

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f)
            {
                _landfallKnown = false;
                MinutesToLandfall = 0f;
                return;
            }

            MinutesToLandfall = (_landfallFrame - frame) / framesPerMinute;
        }

        private static void ScanLandfall(uint frame)
        {
            _landfallKnown = false;

            for (int k = 1; k <= LandfallSamples; k++)
            {
                uint ahead = (uint)k * LandfallStepFrames;
                uint elapsed = _elapsedFrames + ahead;
                if (elapsed > _totalFrames) return;

                var c = TyphoonTrack.CentreAt(_seed, elapsed, _speed);
                if (!TyphoonTrack.IsInsideMap(c)) continue;
                if (HasWaterAt(c)) continue;

                _landfallKnown = true;
                _landfallFrame = frame + ahead;
                return;
            }
        }

        /// <summary>
        /// 中心が水の上か。<c>TerrainManager.HasWater(Vector2)</c> は
        /// <c>WaterSimulation.BeginRead()/EndRead()</c> を取るので **sim スレッド専用**。
        /// 引数は**ワールド XZ**（②が IL 実測済み。<see cref="TsunamiChain"/> の doc）。
        /// </summary>
        private static bool HasWaterAt(Vec2 centre)
        {
            if (!Singleton<TerrainManager>.exists) return false;
            return Singleton<TerrainManager>.instance.HasWater(new Vector2(centre.X, centre.Z));
        }

        /// <summary>
        /// 終わり方は 3 つ（マップを抜けた／持続時間を使い切った／プレイヤーが止めた）だが、
        /// **後始末は必ずこの 1 本を通す。**
        /// </summary>
        private static void Stop()
        {
            // 1. バニラの終了経路に乗せる（スロットは解放しない。あちらの doc）。
            TyphoonSlot.Deactivate();
            // ★ ここで return しない。バニラの終了経路が投げても、
            //    ④が握っている天候は下の Forget() が必ず戻す。

            // 2. 握っていたものを全部手放す（<see cref="Forget"/> が復元も持つ）。
            Forget();
        }

        /// <summary>
        /// ④が握っていたものを全部手放す。**災害スロットには触らない**
        /// （<see cref="Stop"/> だけがバニラの終了経路を呼ぶ）。
        /// <see cref="LastRefusal"/> は残す（呼び出し側が理由を上書きする）。
        ///
        /// ★ **④が触った他の系の復元はここに書くこと。<see cref="Stop"/> ではない。**
        /// 台風を手放す経路は <see cref="Stop"/> だけではない ——
        /// <see cref="LoseSlot"/>（災害スロットが再利用された）と
        /// <see cref="Reset"/>（レベルアンロード）も通る。復元を <see cref="Stop"/> 側に
        /// 置くと、スロットを奪われた瞬間に**天候を握ったまま台風だけが消える**。
        ///
        /// ★ **ここに <c>TyphoonCloud</c> を足さないこと。** 雲は main スレッドだけの
        /// 機能で、sim 側からは 1 度も呼ばれない。それが T9 を他から独立させている
        /// 実体である（<c>TyphoonCloud</c> のクラス doc）。雲の後始末は
        /// <c>TyphoonFeature.OnMainThreadUpdate</c> が「Active でなくなったフレーム」に
        /// 自分で行う。
        ///
        /// 復元はどれも冪等でなければならない（<see cref="Stop"/> → <see cref="Forget"/> と
        /// <c>TyphoonFeature.OnLevelUnloading</c> の両方から重ねて呼ばれる）。
        /// </summary>
        private static void Forget()
        {
            // ★ バニラの DeactivateDisaster に任せない。DisasterAI.DeactivateNow は
            //    m_flags & Active(8) が無ければ何もしないので（T3 で IL 実測）、
            //    Emerging 中に止めた台風では m_targetRain = 0 が走らない。
            TyphoonWeather.Release();

            // ★ 落雷の在庫を次の台風へ持ち越さない。持ち越すと、次の台風は実際には
            //    空いているキューを「埋まっている」と見て 1 発も撃たなくなる ——
            //    そしてキューが空のままになるので、抑え込んでいたはずの環境落雷が戻る。
            TyphoonLightning.Reset();

            // ★ 風害の走査位置も次の台風へ持ち越さない。持ち越すと、次の台風は
            //    前の台風の中心を基準にした序数から走り出す。
            TyphoonWind.Reset();

            // ★★ 河川の水位を必ず戻す（罠 4 の復元経路 1 本目）。**Stop ではなく
            //    ここに置く** —— 台風を手放す経路は Stop だけではなく、LoseSlot
            //    （災害スロットを奪われた）と Reset（アンロード）も通る。Stop 側に
            //    置くと、スロットを奪われた瞬間に**川を溢れさせたまま台風だけが消える**。
            //    RestoreAll は冪等なので重ねて呼んでよい。
            TyphoonFlood.RestoreAll();

            // ★ 随伴竜巻もここで手放す。**Stop ではなくここ**（上の 3 つと同じ理由）。
            //    StopAll は掴んでいる竜巻をバニラの終了経路へ乗せてから台帳を捨てる
            //    冪等な操作なので、重ねて呼んでよい。ここを飛ばすと、台風が終わった
            //    あとに竜巻だけが単独で都市に残る。
            TyphoonTornado.StopAll();

            TyphoonSlot.Forget();

            _active = false;
            _seed = 0u;
            _speed = 0f;
            _peakIntensity = 0;
            _totalFrames = 0u;
            _elapsedFrames = 0u;
            _lastFrame = 0u;
            _decay = 0f;
            _prefabRadius = 0f;
            _centre = new Vec2(0f, 0f);
            _centreHeight = 0f;
            _heading = 0f;
            _intensity = 0;
            _stormRadius = 0f;
            _galeRadius = 0f;
            _phase = TyphoonPhase.Idle;
            _overLand = false;
            _wasInsideMap = false;
            _landfallKnown = false;
            _landfallFrame = 0u;
            _landfallScanned = false;
            _landfallScanFrame = 0u;
            MinutesToLandfall = 0f;
        }

        /// <summary>
        /// <c>.cgs</c> の値は公開契約なので範囲外でも読み捨てず、ここでクランプする。
        /// **<c>(byte)</c> へのキャストの前にクランプすること**（範囲外を先にキャストすると
        /// いちばん弱い設定がいちばん強い台風になる）。
        /// </summary>
        private static byte ClampIntensity(int value)
        {
            if (value < MinIntensity) value = MinIntensity;
            if (value > MaxIntensity) value = MaxIntensity;
            return (byte)value;
        }
    }
}
