using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④の論理オブジェクト本体。バニラの雷雨災害スロットを 1 つ取り、
    /// その <c>m_targetPosition</c> を**毎 sim tick 書き換えて動かす**（IL 事実文書 §E-1）。
    /// <b>sim スレッド専用。</b>
    ///
    /// ── なぜ④が自分で動かすのか ──────────────────────────────
    ///
    /// <c>DisasterManager.SimulationStepImpl</c> は
    /// <c>idx = m_currentFrameIndex &amp; 255</c> で 1 呼び出しにつき災害 1 個しか進めないので、
    /// **災害 i の <c>SimulationStep</c> は 256 sim フレームに 1 回しか回らない**（§E-2）。
    /// 滑らかに動かすには④が <c>OnAfterSimulationTick</c> から毎 tick 座標を書くしかない。
    ///
    /// ── <c>m_targetPosition</c> を書いてよい根拠（本タスクで再実測した） ──────────
    ///
    /// 全アセンブリで <c>stfld DisasterData::m_targetPosition</c> を走査した結果、
    /// 書き手は 19 メソッドあるが、**既に存在する災害のそれを書くものは 1 つも無い**。
    /// 全て「<c>CreateDisaster</c> の直後に、今作ったスロットへ初期位置を入れる」か、
    /// セーブの <c>Deserialize</c> か、プレイヤーが移動ツールで掴んだとき
    /// （<c>DefaultTool+&lt;EndMoving&gt;</c>）である。<c>ThunderStormAI</c> 側は
    /// <c>SimulationStep</c> でも <c>ActivateDisaster</c> でも一切書かず、
    /// <c>StartDisaster</c> が <c>m_targetPosition.y</c>（Vector3 の y フィールド）に
    /// 地形高を入れるだけである。**つまり Active 中にバニラと殴り合わない。**
    /// 位置に追随するのは落雷の散布中心・ハザード円盤・災害マーカー・
    /// <c>FindDisaster(Vector3)</c>（§E-1）。追随しないのは <c>m_activationFrame</c> と
    /// <c>m_angle</c> である（誰も更新しないので④が書いてよい）。
    ///
    /// ── 罠 1: <c>SelfTrigger</c>（③が実際に出荷した） ───────────────────
    ///
    /// <c>ThunderStormAI.StartDisaster</c> は <c>IL_000E</c> で <c>m_flags &amp; 64</c> を見て、
    /// 立っていなければ**即 return する**（§A-1、本タスクで IL 再確認）。そのとき
    /// <c>m_activationFrame</c> は 0 のまま・<c>Significant(256)</c> も付かないので、
    /// <c>IsStillEmerging</c> が永久 true になり、周囲の建物は <c>DetectDisaster</c> を
    /// 呼ばず、**ハザードマップにも通知にも一切出ない**。例外は 1 つも出ない。
    /// ③がこれを出荷し、②のレビューで初めて見つかった。
    ///
    /// **だからフラグを立てるだけでは足りない。** <see cref="Start"/> は
    /// <c>StartNow</c> の直後に <c>m_activationFrame != 0</c> を観測する。
    /// これが罠 1 の「構造で潰す」部分である —— 将来 <c>m_flags</c> の代入が
    /// リファクタで消えても、実行時に必ず気付く。②が読み手側に
    /// <c>ActivationScheduled</c> を用意したのと同じ規律を、書き手側にも置く。
    ///
    /// ── 罠 2: <c>CreateDisaster</c> の戻り値 ─────────────────────────
    ///
    /// 災害は上限 256。<c>CreateDisaster</c> は失敗時に **false を返し
    /// <c>disasterIndex = 0</c> を出す**（例外は出ない。地震 §E-1）。見ないで書くと
    /// **他人の災害スロットを書き潰す**。<see cref="Start"/> は必ず戻り値を見る。
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
    /// **④は「本当の中心」を自分で持ち、<c>m_targetPosition</c> にはクランプした
    /// 写しを書く。** <c>DisasterAI.ClampDisasterTarget</c> は本タスクで IL を読んだ
    /// ところ**マップ矩形ではなく「解放済みタイル」の内側**へ丸める
    /// （<c>GameAreaManager.IsUnlocked</c> / <c>GetAreaBounds</c> を 8 方向ぶん見る）。
    /// これを④の状態にすると、台風は購入済みエリアの縁に貼り付いたまま
    /// **永久に終わらなくなる**。終了判定は必ずクランプ前の中心で行う。
    ///
    /// ── <c>Singleton&lt;T&gt;.exists</c> を先に見る ─────────────────────
    ///
    /// <c>Singleton&lt;T&gt;.instance</c> は <c>sInstance</c> が null のとき
    /// <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる **main スレッド専用
    /// API** で、sim スレッドから踏むと落ちる（<see cref="TsunamiChain"/> の同じ注記）。
    /// </summary>
    public static class TyphoonController
    {
        /// <summary>強度の下限。<c>.cgs</c> の値は公開契約なので範囲外でも読み捨てず、
        /// 使う側でクランプする（②の <c>EarthquakeLongPeriodStrength</c> と同じ扱い）。</summary>
        private const int MinIntensity = 10;

        private const int MaxIntensity = 255;

        /// <summary>上陸予測の先読みサンプル数の上限（計画 §3.6）。</summary>
        private const int LandfallSamples = 64;

        /// <summary>上陸予測のサンプル間隔（フレーム）。</summary>
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
        private static ushort _id;
        private static uint _activationFrame;

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

        public static ushort DisasterId { get { return _id; } }

        /// <summary>
        /// 真の中心（クランプ前）。Y は地形高のサンプルで、マップ外では地形グリッドが
        /// 端でクランプされるため「いちばん近い縁の高さ」になる。
        /// </summary>
        public static Vec3 Centre { get { return new Vec3(_centre.X, _centreHeight, _centre.Z); } }

        public static float HeadingRadians { get { return _heading; } }

        public static byte Intensity { get { return _intensity; } }

        public static float StormRadius { get { return _stormRadius; } }

        public static float GaleRadius { get { return _galeRadius; } }

        public static TyphoonPhase Phase { get { return _phase; } }

        public static uint ElapsedFrames { get { return _elapsedFrames; } }

        public static uint TotalFrames { get { return _totalFrames; } }

        public static bool OverLand { get { return _overLand; } }

        /// <summary>上陸予測が立っているか。**false は「0 分後」ではなく「このまま
        /// 海上を通過する（か、先読みの範囲に陸が無い）」である。** 0 と混ぜないこと。</summary>
        public static bool LandfallKnown { get { return _landfallKnown; } }

        /// <summary>上陸までのゲーム内分。<see cref="LandfallKnown"/> が true のときだけ意味を持つ。</summary>
        public static float MinutesToLandfall { get; private set; }

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
            if (!TryGetOwnedSlot(out buffer)) return;

            Advance(buffer, frame, deltaMinutes);
        }

        /// <summary>
        /// 台風を起こす。<c>DisasterTool.&lt;CreateDisaster&gt;c__Iterator0.MoveNext</c> の
        /// 手順そのまま（§E-3）で、②の <c>TsunamiChain.Raise</c> と同型である。
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

            var info = FindStormInfo();
            if (info == null)
            {
                _lastRefusal = "no ThunderStormAI prefab (Natural Disasters DLC?)";
                return;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                _lastRefusal = "DisasterManager is not available";
                return;
            }

            var manager = Singleton<DisasterManager>.instance;

            ushort id;
            // ★ 罠 2: 戻り値を必ず見る。false のとき id = 0 になり、そのまま書き込むと
            //    **他人の災害スロットを書き潰す**（上限 256）。
            if (!manager.CreateDisaster(out id, info))
            {
                _lastRefusal = "CreateDisaster returned false (disaster buffer full?)";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFull", _lastRefusal);
                return;
            }

            var buffer = manager.m_disasters.m_buffer;
            var ai = info.m_disasterAI;

            // 種は災害 ID。同じセーブなら同じ経路になる（設計書 §4.1）。
            _seed = id;
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
            ai.ClampDisasterTarget(ref pos);                        // public
            pos.y = SampleHeight(pos);                              // StartDisaster と同じ扱い（§A-1）
            _centreHeight = pos.y;

            buffer[id].m_targetPosition = pos;
            buffer[id].m_angle = _heading;
            buffer[id].m_intensity = _intensity;
            // ★ 罠 1: これが無いと StartDisaster は IL_000E で即 return する（クラス doc）。
            buffer[id].m_flags |= DisasterData.Flags.SelfTrigger;

            // StartDisaster は protected。CreateDisaster 直後の m_flags は Created(1) だけ
            // なので StartNow の「& 60 が 0 なら」の門は必ず通る（§E-3）。
            ai.StartNow(id, ref buffer[id]);

            // ★ SelfTrigger が本当に効いたかを、その場で確かめる。StartDisaster が
            //    通っていれば m_activationFrame = m_startFrame + m_emergingDuration が
            //    入っている（§A-1 IL_003F）。
            if (buffer[id].m_activationFrame == 0u)
            {
                _lastRefusal = "StartDisaster did not schedule an activation frame; "
                             + "the SelfTrigger flag did not take effect";
                Log.Error("typhoon: " + _lastRefusal, null);
                AbandonUnstartableSlot(manager, id);
                Forget();
                return;
            }

            _activationFrame = buffer[id].m_activationFrame;
            _id = id;
            _active = true;
            _lastRefusal = null;

            Log.Info("typhoon started: disaster #" + id
                     + " intensity=" + _intensity
                     + " speed=" + _speed.ToString("F3") + " m/frame"
                     + " duration=" + _totalFrames + " frames");
        }

        /// <summary>
        /// <c>SelfTrigger</c> の見張りが鳴ったときだけ通る後始末。**通常は到達しない。**
        ///
        /// <c>DeactivateNow</c> では畳めない。本タスクで IL を読んだところ
        /// <c>DisasterAI.DeactivateNow</c> は <c>m_flags &amp; Active(8)</c> が無ければ
        /// 何もせず、この時点の旗は <c>Created|Emerging</c> だからである。しかも
        /// <c>ThunderStormAI.IsStillEmerging</c> は <c>m_activationFrame == 0</c> のとき
        /// **恒久的に true を返す**（IL_0015 の <c>brfalse</c>）ので、この災害は
        /// Emerging のまま**永久に Finished にならず、スロットも解放されない**。
        ///
        /// ②の <see cref="TsunamiChain"/> は <c>ReleaseDisaster</c> を「使えるが使わない」と
        /// 判断した。あちらは位相が <c>m_startFrame</c> 基準で自然に進み、最悪でも
        /// 39 ゲーム内時間で自己解放されると IL で確認できていたからである。
        /// **ここはその条件が成り立たない**（進まないことが IL で確定している）ので、
        /// 判断を分ける。放置すると災害スロット 256 を 1 個、都市の寿命ぶん食い潰し、
        /// 災害一覧にも永久に残る。
        /// </summary>
        private static void AbandonUnstartableSlot(DisasterManager manager, ushort id)
        {
            try
            {
                manager.ReleaseDisaster(id);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not release the stuck disaster slot", e);
                }
            }
        }

        /// <summary>
        /// 掴んでいるスロットがまだ④のものか。**災害 ID は解放後に再利用される。**
        /// 別の災害に化けたまま <c>m_targetPosition</c> を書き続けると、
        /// **無関係な災害を④が引きずり回す**（③の <c>FireWhirlPinner</c> が距離で
        /// 偽陽性を弾いているのと同じ事故）。
        ///
        /// 1 つでも外れたら書き込みをやめて <see cref="Forget"/> する ——
        /// <see cref="Stop"/> ではない。**もう④のものではないので触ってはいけない。**
        /// </summary>
        private static bool TryGetOwnedSlot(out DisasterData[] buffer)
        {
            buffer = null;

            if (!Singleton<DisasterManager>.exists)
            {
                LoseSlot("DisasterManager is not available");
                return false;
            }

            var candidate = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
            if (candidate == null || _id == 0 || _id >= candidate.Length)
            {
                LoseSlot("the disaster index is out of range");
                return false;
            }

            if ((candidate[_id].m_flags & DisasterData.Flags.Created) == 0)
            {
                LoseSlot("the disaster slot was released");
                return false;
            }

            // 再利用されたスロットを見分ける最も安いキー。開始時に控えた値と一致するか。
            if (candidate[_id].m_activationFrame != _activationFrame)
            {
                LoseSlot("the disaster slot was reused by something else");
                return false;
            }

            // get_Info は境界検査をしない 4 命令なので、要素ごとに try/catch する。
            try
            {
                var info = candidate[_id].Info;
                if (info == null || !(info.m_disasterAI is ThunderStormAI))
                {
                    LoseSlot("the disaster slot is no longer a thunderstorm");
                    return false;
                }
            }
            catch
            {
                LoseSlot("the disaster slot could not be identified");
                return false;
            }

            buffer = candidate;
            return true;
        }

        /// <summary>**黙って手放さない。** 理由を 1 行出して <see cref="Forget"/> する。</summary>
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
            var ai = buffer[_id].Info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);
            pos.y = SampleHeight(pos);
            _centreHeight = pos.y;

            buffer[_id].m_targetPosition = pos;
            buffer[_id].m_angle = _heading;
            buffer[_id].m_intensity = _intensity;

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
                Log.Info("typhoon #" + _id + " left the map");
                Stop();
                return;
            }

            if (_phase == TyphoonPhase.Gone)
            {
                Log.Info("typhoon #" + _id + " used up its lifetime ("
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

        private static float SampleHeight(Vector3 pos)
        {
            if (!Singleton<TerrainManager>.exists) return 0f;
            return Singleton<TerrainManager>.instance.SampleDetailHeight(pos);
        }

        /// <summary>
        /// 終わり方は 3 つ（マップを抜けた／持続時間を使い切った／プレイヤーが止めた）だが、
        /// **後始末は必ずこの 1 本を通す。**
        ///
        /// **災害スロットは④が解放しない。** <c>DisasterAI.IsStillClearing</c>（base）は
        /// 災害グループの <c>m_refCount &gt; 1</c>、すなわち④の落雷で燃えた建物が残っている
        /// 間 Clearing を続ける（§A-1）。**嵐は火が消えるまで終わらない**のが正しい挙動で、
        /// その後 <c>DisasterManager.SimulationStepImpl</c> が <c>ReleaseDisaster</c> を呼ぶ。
        /// ②の <see cref="TsunamiChain"/> が <c>ReleaseDisaster</c> を「使えるが使わない」と
        /// 判断したのと同じ理由（<c>OnDisasterStarted</c> を受け取った他 MOD から見て、
        /// 終了通知の無い災害を作らない）。
        /// </summary>
        private static void Stop()
        {
            // 1. バニラの終了経路に乗せる。DeactivateNow は public で、本タスクで IL を
            //    読んだところ **m_flags に Active(8) が立っていなければ何もしない**。
            //    立っていれば ThunderStormAI.DeactivateDisaster が走り、SelfTrigger 付き
            //    なので m_targetRain = 0 / m_targetCloud = 0 が書かれる（§A-1）。
            //    ★ Emerging 中に止めた場合はここが空振りする。だから④が触った天候は
            //      ④自身が戻さなければならない（下の 2. と TyphoonWeather.Release）。
            try
            {
                if (_active && _id != 0 && Singleton<DisasterManager>.exists)
                {
                    var buffer = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
                    if (buffer != null && _id < buffer.Length)
                    {
                        var info = buffer[_id].Info;
                        if (info != null && info.m_disasterAI is ThunderStormAI)
                        {
                            info.m_disasterAI.DeactivateNow(_id, ref buffer[_id]);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon deactivation failed", e);
                }
            }

            // 2. ④が触った全部を戻す。T4 以降がこの行の下に自分の復元を足していく。
            //    （T4: TyphoonWeather.Release / T8: TyphoonFlood.RestoreAll /
            //      T9: TyphoonCloud.ReleaseVanillaBoost / T10: TyphoonTornado.StopAll）

            // 3. 自分の状態を捨てる。
            Forget();
        }

        /// <summary>
        /// ④の状態だけを捨てる。**災害スロットには触らない。**
        /// <see cref="LastRefusal"/> は残す（呼び出し側が理由を上書きする）。
        /// </summary>
        private static void Forget()
        {
            _active = false;
            _id = 0;
            _activationFrame = 0u;
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

        /// <summary>
        /// <c>ThunderStormAI</c> を持つ災害プレハブ。**キャッシュしない**
        /// （<c>TsunamiChain.FindTsunamiInfo</c> と同じ判断。走査は
        /// 台風を起こす瞬間にしか走らない）。
        /// </summary>
        private static DisasterInfo FindStormInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<ThunderStormAI>();
            }
            catch
            {
                return null;
            }
        }
    }
}
