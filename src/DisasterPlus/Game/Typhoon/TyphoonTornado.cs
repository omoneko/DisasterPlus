using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④が掴んでいる随伴竜巻 1 個ぶんの台帳。**sim スレッドからのみ触る。**
    /// 可変 struct なのは、毎 tick 3 個ぶんを書き換えるのに割り当てを 1 バイトも
    /// 出さないため（配列の要素として持ち <c>_slots[i].X = ...</c> で直接書く）。
    /// </summary>
    internal struct TyphoonTornadoSlot
    {
        /// <summary>掴んでいる災害スロットの添字。0 は「空き」。</summary>
        public ushort DisasterId;

        /// <summary><c>StartDisaster</c> が書いた <c>m_activationFrame</c>。
        /// **スロットが再利用されたことを見分ける最も安いキー**（<see cref="TyphoonSlot"/> と同じ）。</summary>
        public uint ActivationFrame;

        /// <summary>紐づいた渦車両。0 は「まだ／もう付いていない」。</summary>
        public ushort VehicleId;

        /// <summary>災害が Active になってから車両を探した回数。</summary>
        public int AttachTicks;

        /// <summary>台風の中心まわりの位相（rad）。</summary>
        public float OrbitPhase;

        /// <summary>前 tick に実際に書いた目標点。移動量をここから測ってクランプする。</summary>
        public Vector3 Target;
    }

    /// <summary>
    /// 台風に随伴する**バニラの竜巻**。<b>sim スレッド専用。</b>
    ///
    /// ── なぜバニラの竜巻を借りるのか、そして**既定 OFF** の理由 ────────────
    ///
    /// 見た目（高さ 2000 m の漏斗メッシュ・粉塵・音）と破壊がまるごと無料で、
    /// しかもバニラ品質になる。ただし代償がある: <c>VortexAI.SimulationStep</c> は
    /// 破壊を <c>DisasterHelpers.DestroyStuff(... burnRadiusMin: 0f, burnRadiusMax: 0f)</c>
    /// で行い（§B-1）、**Natural Disasters Renewal はこの実引数を「竜巻だ」と嗅ぎ分けて
    /// 破壊を完全に置き換える**（§F-1）。つまり NDR の入った環境では、
    /// **この竜巻の破壊だけは NDR の竜巻設定に従う**。③火災旋風が既に同じ代償を
    /// 受け入れているので④も一貫させるが、**プレイヤーに黙って渡さない**:
    /// 設計書 §2 の指定どおり既定 OFF にし、設定画面（<c>ModCompat.NdrPresent</c> の
    /// とき）とパネルと診断の 3 箇所で <c>Strings.TyphoonTornadoNdrNote</c> が名乗る。
    ///
    /// **④自身の風害（<see cref="TyphoonWind"/>）はこの影響を受けない。**
    /// あちらは <c>BuildingAI.CollapseBuilding</c> を直接呼び、<c>DisasterHelpers</c> を
    /// 1 度も通らないので NDR と完全に無衝突である（§F-1）。同じ都市で両方が
    /// 動くとき、片方だけが NDR の設定に従う——その区別もパネルに出す。
    ///
    /// ── 罠 1: <c>SelfTrigger</c>（③が実際に出荷した） ───────────────────
    ///
    /// <c>TornadoAI.StartDisaster</c> は本タスクで IL を読み直したところ、嵐と同じく
    /// <c>IL_000E</c> で <c>m_flags &amp; 64</c> を見て、立っていなければ**即 return する**。
    /// そのとき <c>m_activationFrame</c> は 0 のまま・<c>Significant(256)</c> も付かず、
    /// <c>IsStillEmerging</c> が永久 true になって**ハザードマップにも通知にも出ず、
    /// 渦車両も永久に生まれない**。例外は 1 つも出ない。だからフラグを立てるだけでは
    /// 足りず、<see cref="TryCreate"/> は <c>StartNow</c> の直後に
    /// <c>m_activationFrame != 0</c> を観測する（<see cref="TyphoonSlot.Begin"/> と同じ形）。
    ///
    /// ── 罠 2: <c>CreateDisaster</c> の戻り値 ─────────────────────────
    ///
    /// 災害は上限 256。<c>CreateDisaster</c> は失敗時に **false を返し
    /// <c>disasterIndex = 0</c> を出す**（例外は出ない）。見ないで書くと**他人の災害
    /// スロットを書き潰す**。④は台風本体で既に 1 個握っているので、この経路が
    /// 上限にいちばん近い。
    ///
    /// ── 罠 3: 操舵はスロット 0 だけでは足りない ────────────────────────
    ///
    /// 竜巻は「災害」ではなく <c>VortexAI</c> の**車両**が動く（§E-1）。
    /// <c>m_targetPosition</c> を書いても車両は追随しない。しかも
    /// <c>VortexAI.SimulationStep</c> は冒頭で
    /// <c>if (LengthXZ(m_targetPos0 - position) &lt; m_info.m_maxSpeed) m_targetPos0 = m_targetPos1;</c>
    /// をやるので、スロット 0 だけ書いても次のステップでスロット 1 に上書きされる
    /// （③が IL 実測で確定させた事実）。**必ず両スロットに書く。**
    ///
    /// ── <c>m_maxSpeed</c> が読めなければ 1 個も出さない ──────────────────
    ///
    /// 追いつけない目標を置くと、竜巻は台風の周りを回らず**ずっと後ろを一直線に
    /// 追いかける**。<c>m_maxSpeed</c> は <c>VortexAI</c> ではなく
    /// <c>TornadoAI.m_vortexInfo</c>（<c>VehicleInfo</c>）側にあるプレハブ値で、
    /// DLL に実数値が無い（§B-1、PARTIAL）。**読めなければ竜巻を 1 個も作らない** ——
    /// 推測した速度で操舵するより出さないほうが正しい（T1 §1.2 と同じ規律）。
    /// なお車両は 16 sim フレームに 1 回しか進まない
    /// （<c>VehicleManager.SimulationStepImpl</c> の <c>m_currentFrameIndex &amp; 15</c>。
    /// 本タスクで IL 実測）ので、1 tick に目標を動かしてよい量は <c>m_maxSpeed / 16</c>。
    /// </summary>
    public static partial class TyphoonTornado
    {
        /// <summary>同時に持てる竜巻の数の上限。④が災害スロット 256 を食い潰す形を作らない。</summary>
        public const int MaxTornadoes = 3;

        /// <summary>台風の中心まわりを回る角速度（度／ゲーム内分）。**④が決めた演出値。**</summary>
        private const float OrbitDegreesPerMinute = 90f;

        /// <summary>軌道半径 ÷ 暴風域半径。</summary>
        private const float OrbitFraction = 0.6f;

        /// <summary>竜巻の強度 ÷ 台風の強度。</summary>
        private const float IntensityFraction = 0.5f;

        /// <summary>竜巻の強度の下限（<c>DisasterData.m_intensity</c> は Byte）。</summary>
        private const int MinTornadoIntensity = 10;

        private const int MaxTornadoIntensity = 255;

        /// <summary>1 sim tick が車両の 1 ステップに占める割合（クラス doc の 16 フレーム周期）。
        /// <c>m_maxSpeed</c> は**その 1 ステップぶん**の距離である。</summary>
        private const float VehicleStepShare = 1f / 16f;

        /// <summary>目標を車両の全力より少し遅く動かす余裕。
        /// <c>VortexAI</c> の速度は一次遅れ（<c>vel = vel*0.9 + dir*0.1</c>、§B-1）なので、
        /// 上限ぴったりに置くと立ち上がりで必ず置いていかれる。</summary>
        private const float TrackingMargin = 0.8f;

        /// <summary>災害が Active になってから車両を探す回数の上限。**Emerging 中は数えない**
        /// —— 渦車両を作るのは <c>TornadoAI.ActivateDisaster</c> なので（§B-2）、Active に
        /// なるまで居ないのが正常であり、<c>m_emergingDuration</c> は長さが分からない。</summary>
        private const int MaxAttachTicks = 256;

        /// <summary>紐づけを受理する最大距離 ÷ 期待生成距離。<c>TornadoAI.ActivateDisaster</c>
        /// は渦車両を <c>m_targetPosition</c> から <c>m_intensity * 10 + 400</c> だけ離した点に
        /// 作る（本タスクで IL 実測）。無関係な渦を弾きつつ正規の紐づけは絶対に誤って
        /// 弾かないよう 3 倍の余裕を持たせる（③と同じ判断）。</summary>
        private const float AttachDistanceFactor = 3f;

        /// <summary>sim スレッド専用の使い回しバッファ。**毎 tick 確保しない。**
        /// 型は <c>DisasterAI.m_tempList</c> と同じ（③の <c>FireWhirlPinner</c> と同じ）。</summary>
        private static readonly FastList<InstanceID> _tempInstances = new FastList<InstanceID>();

        private static readonly TyphoonTornadoSlot[] _slots = new TyphoonTornadoSlot[MaxTornadoes];

        private static int _count;
        private static string _lastFailure;

        /// <summary>例外を 1 回だけ <c>Log.Error</c> で出したか。<see cref="Reset"/> で戻さない
        /// （ゲームのビルドに対する事実であって都市ごとの状態ではない）。</summary>
        private static bool _errorLogged;

        /// <summary>
        /// <c>SelfTrigger</c> の見張りが鳴ったことを 1 回だけ <c>Log.Error</c> で出したか。
        /// <see cref="_errorLogged"/> と分けてあるのは、片方が立つともう片方の 1 回目が
        /// 黙って消えるからである（<c>TyphoonLightning._rejectionLogged</c> と同じ判断）。
        /// <see cref="Reset"/> で戻さない。
        /// </summary>
        private static bool _startFailureLogged;

        /// <summary>今④が掴んでいる竜巻の数。</summary>
        public static int Count { get { return _count; } }

        /// <summary>そのうち渦車両まで紐づいた数。**<see cref="Count"/> より小さい状態を
        /// 隠さない** —— 付いていない竜巻は④の軌道に乗らず自由に流れる（診断に出す）。</summary>
        public static int Attached
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].DisasterId != 0 && _slots[i].VehicleId != 0) n++;
                }
                return n;
            }
        }

        /// <summary>竜巻を作れなかった／手放した理由（英語、診断用。無ければ null）。
        /// **「作れなかった」を「何も起きていない」と見分ける手段がここにしか無い。**</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>**レベルアンロードで必ず呼ぶ。** 掴んでいる竜巻をバニラの終了経路へ
        /// 乗せてから台帳を捨てる。冪等である。</summary>
        public static void Reset()
        {
            StopAll();

            // StopAll が（災害バッファに到達できず）台帳を空にできなかった場合でも、
            // 次の都市へ災害 ID を持ち越さない。前の都市の ID で操舵しにいくと、
            // **無関係な災害を④が引きずり回す。**
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new TyphoonTornadoSlot();
            _count = 0;
            _lastFailure = null;
            // ★ _errorLogged は戻さない（クラス doc）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>TyphoonFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に竜巻が動く）。
        ///
        /// <paramref name="snapshot"/> からは**プレハブ実測値だけ**を読む。位置・強度・
        /// 半径は <c>TyphoonController</c> の static から同じスレッドで直接読む
        /// （<see cref="TyphoonSnapshot"/> は 1 tick 前の状態である）。
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step(snapshot, frame, deltaMinutes);
            }
            catch (System.Exception e)
            {
                _lastFailure = e.GetType().Name + ": " + e.Message;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon tornadoes failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTor",
                             "typhoon tornadoes failed: " + e.GetType().Name);
                }

                // ★ 失敗しても掴んだままにしない。ここを飛ばすと、例外の出た tick で
                //    作った竜巻が誰にも止められず都市に残る。
                StopAll();
            }
        }

        private static void Step(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            if (!TyphoonController.Active)
            {
                // 通常は TyphoonController.Forget が先に StopAll を呼んでいる。
                // 取りこぼしをここで拾う（StopAll は台帳が空なら 1 命令で返る）。
                StopAll();
                return;
            }

            int desired = ModSettings.TyphoonTornadoCount.value;
            if (desired < 0) desired = 0;
            if (desired > MaxTornadoes) desired = MaxTornadoes;

            if (desired == 0)
            {
                // スライダーを 0 にした瞬間に竜巻が引き上げること。
                StopAll();
                return;
            }

            var prefab = snapshot != null && snapshot.Valid
                ? snapshot.Prefab
                : new TyphoonPrefabFacts();

            if (!prefab.VortexResolved || !(prefab.VortexMaxSpeed > 0f))
            {
                // ★ 読めなければ推測せず 1 個も出さない（クラス doc）。
                _lastFailure = "the vortex prefab speed (VehicleInfo.m_maxSpeed) is unreadable; "
                             + "no tornado is created rather than dropping one that cannot be steered";
                StopAll();
                return;
            }

            float radius = TyphoonController.StormRadius * OrbitFraction;
            if (!(radius > 0f)) return;

            // 1 sim tick あたりに目標を動かしてよい量（クラス doc の 16 フレーム周期）。
            float maxStep = prefab.VortexMaxSpeed * VehicleStepShare * TrackingMargin;

            DisasterData[] buffer = DisasterBuffer();
            if (buffer == null)
            {
                _lastFailure = "DisasterManager is not available";
                return;
            }

            Verify(buffer);

            if (TopUp(desired, frame, radius))
            {
                // ★★ **配列参照を取り直す。** DisasterManager.CreateDisaster は
                //    空きスロットが 1 つも無いとき FastList&lt;DisasterData&gt;.Add に落ちる
                //    （本タスクで IL 実測。IL_00DB）。FastList.Add は容量が足りなければ
                //    **配列を作り直す**ので、その前に掴んでいた m_buffer への
                //    書き込みは**捨てられた配列**に入る —— 例外は出ず、竜巻の位置だけが
                //    永久に更新されない。TyphoonSlot.Begin が Create の後で
                //    取り直しているのと同じ理由。
                buffer = DisasterBuffer();
                if (buffer == null) return;
            }

            Attach(buffer);
            Steer(buffer, deltaMinutes, radius, maxStep);
        }

        // ── 所有権の確認 ─────────────────────────────────────────

        /// <summary>
        /// 掴んでいるスロットがまだ④のものかを毎 tick 確かめる。
        /// **災害 ID は解放後に再利用される** —— 別の災害に化けたまま操舵し続けると
        /// ④が無関係な災害を引きずり回す（<see cref="TyphoonSlot.TryGetBuffer"/> と同じ事故）。
        /// 1 つでも外れたら**書き込みをやめて手放す**（バニラの終了経路は呼ばない ——
        /// もう④のものではない）。
        /// </summary>
        private static void Verify(DisasterData[] buffer)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0) continue;

                string lost = LostReason(buffer, id, _slots[i].ActivationFrame);
                if (lost == null) continue;

                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorLost",
                         "typhoon released tornado #" + id + ": " + lost);
                _slots[i] = new TyphoonTornadoSlot();
                _count--;
            }
        }

        /// <summary>まだ④のものなら null。そうでなければ理由を返す。</summary>
        private static string LostReason(DisasterData[] buffer, ushort id, uint activationFrame)
        {
            if (id >= buffer.Length) return "the disaster index is out of range";
            if ((buffer[id].m_flags & DisasterData.Flags.Created) == 0) return "the slot was released";
            if (buffer[id].m_activationFrame != activationFrame) return "the slot was reused";

            // get_Info は境界検査をしない 4 命令なので、要素ごとに try/catch する。
            try
            {
                var info = buffer[id].Info;
                if (info == null || !(info.m_disasterAI is TornadoAI))
                {
                    return "the disaster slot is no longer a tornado";
                }
            }
            catch
            {
                return "the disaster slot could not be identified";
            }

            return null;
        }

        // ── 生成 ───────────────────────────────────────────────

        /// <summary>1 個ぶんの生成がどう終わったか。<see cref="TopUp"/> の打ち切り判断に使う。</summary>
        private enum CreateOutcome
        {
            /// <summary>竜巻が 1 個立ち上がった。</summary>
            Started,

            /// <summary><c>CreateDisaster</c> まで届かずに諦めた（プレハブ無しなど）。</summary>
            RefusedBeforeCreate,

            /// <summary><c>CreateDisaster</c> を**呼んだ**が竜巻にならなかった（成否によらず）。
            /// **呼び出し側は災害バッファの参照を取り直すこと**（<see cref="Step"/> の注記）。</summary>
            FailedAfterCreate,
        }

        /// <summary>
        /// 足りないぶんを補充する。
        ///
        /// ★★ **1 tick のうち最初の失敗で打ち切る**（全体レビュー I3）。以前は
        /// 失敗しても <c>_count</c> が増えないため、3 スロットぶんの生成が
        /// **毎 sim tick・永久に**再試行されていた。実害は 3 つあった:
        ///
        ///   1. 災害バッファが満杯のとき、<c>CreateDisaster</c> の 256 スロット全走査が
        ///      毎 tick 3 回走る（満杯は「そのうち直る」状態ではない）
        ///   2. <c>SelfTrigger</c> の見張りが鳴る環境では、ラッチの無い
        ///      <c>Log.Error</c> が毎 tick 3 行 —— 通常速度で毎秒 150 行の
        ///      スタックトレースになり、ログが読めなくなる
        ///   3. どちらも「失敗の理由は次の tick でも同じ」であり、連打しても直らない
        ///
        /// 失敗した tick は 1 回で降りる。次の tick で改めて 1 回試すので、
        /// 一時的な失敗（バッファが一瞬満杯だった等）からは自然に回復する。
        /// </summary>
        /// <returns><c>CreateDisaster</c> を 1 回でも呼んだなら true
        /// （呼び出し側は災害バッファの参照を取り直すこと）。</returns>
        private static bool TopUp(int desired, uint frame, float radius)
        {
            bool created = false;
            for (int i = 0; i < _slots.Length && _count < desired; i++)
            {
                if (_slots[i].DisasterId != 0) continue;

                CreateOutcome outcome = TryCreate(i, frame, radius);
                if (outcome == CreateOutcome.FailedAfterCreate) created = true;
                if (outcome != CreateOutcome.Started) break;   // ★ この tick はここまで
                created = true;
            }
            return created;
        }

        /// <summary>
        /// 竜巻を 1 個起こす。手順は <see cref="TyphoonSlot.Begin"/> と**同じ**（§E-3）。
        /// 罠 1（<c>SelfTrigger</c>）と罠 2（戻り値）をここで潰す。
        ///
        /// **災害バッファは <c>CreateDisaster</c> の後で自分で取り直す**（あちらが
        /// <c>FastList.Add</c> で配列を作り直すため。<see cref="Step"/> の注記）。
        /// </summary>
        /// <returns>この 1 個がどう終わったか（<see cref="TopUp"/> が打ち切りに使う）。</returns>
        private static CreateOutcome TryCreate(int index, uint frame, float radius)
        {
            var info = FindTornadoInfo();
            if (info == null || info.m_disasterAI == null)
            {
                _lastFailure = "no TornadoAI prefab (Natural Disasters DLC?)";
                return CreateOutcome.RefusedBeforeCreate;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                _lastFailure = "DisasterManager is not available";
                return CreateOutcome.RefusedBeforeCreate;
            }

            ushort id;
            // ★ 罠 2: 戻り値を必ず見る。false のとき id = 0 になり、そのまま書き込むと
            //    **他人の災害スロットを書き潰す**（上限 256。④は台風本体で既に 1 個
            //    握っているので、この経路が最も上限に近い）。
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                _lastFailure = "CreateDisaster returned false (disaster buffer full?)";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorFull", _lastFailure);
                // ★ 呼んだ側は 256 スロットの全走査を 1 回させている。**この tick は
                //   もう試さない**（TopUp の doc）。
                // ★ 失敗でも FailedAfterCreate を返す —— CreateDisaster を**呼んだ**
                //   以上、災害バッファの参照は取り直させる（あちらは空きが無いとき
                //   FastList.Add へ落ち、配列を作り直しうる。Step の注記）。
                //   「呼んだかどうか」で分けるのがこの列挙の意味である。
                return CreateOutcome.FailedAfterCreate;
            }

            // ★ ここで初めて取る（メソッド doc）。CreateDisaster より前に取った参照は
            //    FastList.Add で作り直されている可能性がある。
            var buffer = DisasterBuffer();
            if (buffer == null || id == 0 || id >= buffer.Length)
            {
                _lastFailure = "CreateDisaster returned an out-of-range index";
                // ★★ **取ったスロットを必ず返す**（全体レビュー I3）。以前はここで
                //    そのまま return しており、CreateDisaster が成功して確保した
                //    スロットが誰にも解放されないまま残った ——
                //    毎 tick 再試行していたので、256 の予算がそのぶんだけ削られ続けた。
                Abandon(id);
                return CreateOutcome.FailedAfterCreate;
            }

            // 位相は災害 ID から決定論的に引く（VanillaRandomizer は使わない ——
            // これはバニラが引く値ではなく④が発明した判断である）。
            float phase = DeterministicRandom.Unit(id, 0x544F524Eu) * 6.2831855f;   // "TORN"
            byte intensity = TornadoIntensity();

            var pos = OrbitPoint(phase, radius);
            var ai = info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);
            pos.y = SampleHeight(pos);

            buffer[id].m_targetPosition = pos;
            buffer[id].m_angle = TyphoonController.HeadingRadians;
            buffer[id].m_intensity = intensity;
            // ★ 罠 1: これが無いと TornadoAI.StartDisaster は IL_000E で即 return する
            //    （本タスクで IL 実測。クラス doc）。
            buffer[id].m_flags |= DisasterData.Flags.SelfTrigger;

            ai.StartNow(id, ref buffer[id]);

            // ★ SelfTrigger が本当に効いたかを、その場で確かめる。StartDisaster が
            //    通っていれば m_activationFrame = m_startFrame + m_emergingDuration が
            //    入っている（本タスクの IL 実測 IL_003F）。
            if (buffer[id].m_activationFrame == 0u)
            {
                _lastFailure = "StartDisaster did not schedule an activation frame; "
                             + "the SelfTrigger flag did not take effect";

                // ★★ **ラッチする**（全体レビュー I3）。ここは毎 sim tick 通る経路で、
                //    Log.Error はスロットルされない。TyphoonSlot に同じ 1 行が
                //    あるが、あちらはプレイヤーが「台風を発生させる」を押した
                //    ときの一発勝負なので費用が違う —— 形だけ写して、
                //    ループが費用の前提を変えたことを見落としていた。
                if (!_startFailureLogged)
                {
                    _startFailureLogged = true;
                    Log.Error("typhoon tornado: " + _lastFailure, null);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorSelfTrigger",
                             "typhoon tornado: " + _lastFailure);
                }

                Abandon(id);
                return CreateOutcome.FailedAfterCreate;
            }

            _slots[index] = new TyphoonTornadoSlot
            {
                DisasterId = id,
                ActivationFrame = buffer[id].m_activationFrame,
                VehicleId = 0,
                AttachTicks = 0,
                OrbitPhase = phase,
                Target = pos,
            };
            _count++;
            _lastFailure = null;

            Log.Info("typhoon tornado started: disaster #" + id
                     + " intensity=" + intensity
                     + " (frame " + frame + ")");
            return CreateOutcome.Started;
        }


        // ── 後始末 ─────────────────────────────────────────────

        /// <summary>
        /// 掴んでいる竜巻を全部バニラの終了経路へ乗せる。**冪等。**
        /// <c>TyphoonController.Forget</c>・<see cref="Reset"/>・設定を切ったときの
        /// 3 経路から重ねて呼ばれる。<see cref="StopOne"/> が 3 段に分けて畳む:
        /// (1) 車両が付いている → **両スロットに現在位置**。やがて
        /// <c>VortexAI.ArriveAtDestination</c> が true になり、バニラが
        /// <c>DeactivateNow</c> と <c>Vehicle.Unspawn</c> を正しく実行する
        /// （**④が車両を自分で解放しない**。③の <c>FireWhirlPinner.BeginEnding</c>）。
        /// (2) 車両が無く Active → <c>DeactivateNow</c>。
        /// (3) 車両が無く、まだ Emerging → <c>DeactivateNow</c> は
        /// <c>m_flags &amp; Active(8)</c> が無いと何もしないので効かない。Active でない＝
        /// <c>ActivateDisaster</c> 未実行＝**渦車両はまだ 1 台も存在しない**ので、
        /// <c>ReleaseDisaster</c> でスロットごと畳む（<see cref="TyphoonSlot"/> の
        /// <c>Abandon</c> と同じ判断）。放置すると台風が終わったあとに竜巻だけが残る。
        /// </summary>
        public static void StopAll()
        {
            if (_count == 0) return;

            DisasterData[] buffer = DisasterBuffer();

            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0) continue;

                try
                {
                    StopOne(buffer, _slots[i]);
                }
                catch (System.Exception e)
                {
                    if (!_errorLogged)
                    {
                        _errorLogged = true;
                        Log.Error("typhoon tornado teardown failed", e);
                    }
                }

                _slots[i] = new TyphoonTornadoSlot();
            }

            _count = 0;
        }

        /// <summary>
        /// 竜巻 1 個を畳む。
        ///
        /// ★★ **災害 ID の再利用を、車両 ID と同じ厳しさで弾く**（全体レビュー C2）。
        /// 以前ここは <c>Created</c> と <c>Info != null</c> しか見ておらず、
        /// <c>LostReason</c> が毎 tick 見ている 2 条件——<c>m_activationFrame</c> の一致と
        /// <c>m_disasterAI is TornadoAI</c>——を飛ばしていた。通常は <see cref="Verify"/> が
        /// 同じ tick で走るので窓は 1 tick だが、**設定の「台風」を切ると窓が無限になる**:
        /// <c>TyphoonFeature.OnSimulationTick</c> は無効時に早期 return するので、
        /// 台帳は <c>(DisasterId, ActivationFrame)</c> を抱えたまま 1 tick も検証されず、
        /// その間に竜巻は自然終了してスロットがバニラや他 MOD に配り直される。
        /// あとで都市を出るか設定を戻すと、ここが**他人の生きている災害**を
        /// <c>DeactivateNow</c> するか <c>ReleaseDisaster</c> する。例外もログも出ない。
        /// （その早期 return 自体も同じレビューで塞いだが、<b>この関数は自分で確かめる</b>
        ///  ——呼び出し側の順序に安全性を預けない。）
        ///
        /// **確かめる前は車両にも触らない。** 掴んでいる車両はこの災害グループから
        /// 探した渦なので、災害スロットが再利用されていればその車両ももう④のもの
        /// ではない（<see cref="StillOurVortex"/> は「渦であること」しか見ないので、
        /// 他人の竜巻を現在位置に釘付けにできてしまう）。
        /// </summary>
        private static void StopOne(DisasterData[] buffer, TyphoonTornadoSlot slot)
        {
            // ★★ **所有権が最初。** 確かめる前は車両にも触らない。
            //    掴んでいる VehicleId は「この災害グループから探した渦」なので、
            //    災害スロットが再利用されていれば、その車両ももう④のものではない
            //    （StillOurVortex は「渦であること」しか見ない ——
            //     他人の竜巻を現在位置に釘付けにできてしまう）。
            if (buffer == null)
            {
                // 災害バッファに届かない ＝ 確かめる手段が無い。**何も触らない。**
                // 台帳は呼び出し側（StopAll / Reset）が捨てるので、次の都市へは残らない。
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorNoBuffer",
                         "typhoon dropped tornado #" + slot.DisasterId
                         + " without touching it: the disaster buffer is unavailable");
                return;
            }

            string lost = LostReason(buffer, slot.DisasterId, slot.ActivationFrame);
            if (lost != null)
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorStale",
                         "typhoon dropped tornado #" + slot.DisasterId
                         + " without touching it: " + lost);
                return;
            }

            // ★ 車両 ID の再利用も弾く（<see cref="StillOurVortex"/> の注記）。
            //   ここを飛ばすと、終了処理が無関係な車両を現在位置に釘付けにする。
            if (slot.VehicleId != 0 && StillOurVortex(slot.VehicleId))
            {
                var vehicles = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
                if (vehicles != null && slot.VehicleId < vehicles.Length)
                {
                    Vector3 here = vehicles[slot.VehicleId].GetLastFrameData().m_position;
                    // ★ 両スロットに現在位置（③の BeginEnding）。片方だけでは、次の
                    //    ステップでスロット 1 に上書きされて永久に到着しない。
                    WriteVehicleTarget(slot.VehicleId, here);
                    Log.Info("typhoon tornado " + slot.DisasterId
                             + " ending; both target slots moved to current position");
                    return;
                }
            }

            var info = buffer[slot.DisasterId].Info;
            if (info == null || info.m_disasterAI == null) return;

            if ((buffer[slot.DisasterId].m_flags & DisasterData.Flags.Active) != 0)
            {
                info.m_disasterAI.DeactivateNow(slot.DisasterId, ref buffer[slot.DisasterId]);
                Log.Info("typhoon tornado " + slot.DisasterId
                         + " had no vortex vehicle; deactivated directly");
                return;
            }

            // まだ Emerging。渦車両はまだ存在しないので、スロットごと畳んでよい（doc）。
            Abandon(slot.DisasterId);
            Log.Info("typhoon tornado " + slot.DisasterId
                     + " was still emerging; released before it could spawn");
        }

        /// <summary>
        /// その車両添字がまだ渦か。**車両 ID は Unspawn 後に再利用される**ので、
        /// 書き込む前に毎回確かめる（<see cref="Steer"/> の注記）。
        /// </summary>
        private static bool StillOurVortex(ushort vehicleId)
        {
            try
            {
                if (!Singleton<VehicleManager>.exists) return false;
                var vehicles = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
                if (vehicles == null || vehicleId >= vehicles.Length) return false;
                if ((vehicles[vehicleId].m_flags & Vehicle.Flags.Created) == 0) return false;

                var info = vehicles[vehicleId].Info;
                return info != null && info.m_vehicleAI is VortexAI;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteVehicleTarget(ushort vehicleId, Vector3 target)
        {
            if (!Singleton<VehicleManager>.exists) return;
            var vehicles = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            if (vehicles == null || vehicleId >= vehicles.Length) return;

            // w は 0。バニラも Vector3 → Vector4 の暗黙変換で入れているだけで
            // 意味を持たない（③の FireWhirlPinner.BeginEnding の doc）。
            var v4 = new Vector4(target.x, target.y, target.z, 0f);
            vehicles[vehicleId].SetTargetPos(0, v4);
            vehicles[vehicleId].SetTargetPos(1, v4);
        }

        /// <summary>災害スロットを畳む。<see cref="TyphoonSlot"/> の <c>Abandon</c> と同型。</summary>
        private static void Abandon(ushort id)
        {
            try
            {
                if (Singleton<DisasterManager>.exists)
                {
                    Singleton<DisasterManager>.instance.ReleaseDisaster(id);
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not release tornado slot " + id, e);
                }
            }
        }

        // ── 小物 ───────────────────────────────────────────────

        private static DisasterData[] DisasterBuffer()
        {
            // ★ Singleton<T>.exists を先に見る。instance は main スレッド専用 API を
            //    走らせることがある（TyphoonSlot のクラス doc）。
            if (!Singleton<DisasterManager>.exists) return null;
            return Singleton<DisasterManager>.instance.m_disasters.m_buffer;
        }

        private static float SampleHeight(Vector3 pos)
        {
            if (!Singleton<TerrainManager>.exists) return 0f;
            return Singleton<TerrainManager>.instance.SampleDetailHeight(pos);
        }

        /// <summary><c>TornadoAI</c> を持つ災害プレハブ。**キャッシュしない**
        /// （<see cref="TyphoonSlot"/> の <c>FindStormInfo</c> と同じ判断）。</summary>
        private static DisasterInfo FindTornadoInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<TornadoAI>();
            }
            catch
            {
                return null;
            }
        }
    }
}
