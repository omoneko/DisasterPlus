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
    public static class TyphoonTornado
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

        /// <returns><c>CreateDisaster</c> を 1 回でも呼んだなら true
        /// （呼び出し側は災害バッファの参照を取り直すこと）。</returns>
        private static bool TopUp(int desired, uint frame, float radius)
        {
            bool created = false;
            for (int i = 0; i < _slots.Length && _count < desired; i++)
            {
                if (_slots[i].DisasterId != 0) continue;
                created |= TryCreate(i, frame, radius);
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
        /// <returns><c>CreateDisaster</c> を呼んだなら true（成否によらず）。</returns>
        private static bool TryCreate(int index, uint frame, float radius)
        {
            var info = FindTornadoInfo();
            if (info == null || info.m_disasterAI == null)
            {
                _lastFailure = "no TornadoAI prefab (Natural Disasters DLC?)";
                return false;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                _lastFailure = "DisasterManager is not available";
                return false;
            }

            ushort id;
            // ★ 罠 2: 戻り値を必ず見る。false のとき id = 0 になり、そのまま書き込むと
            //    **他人の災害スロットを書き潰す**（上限 256。④は台風本体で既に 1 個
            //    握っているので、この経路が最も上限に近い）。
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                _lastFailure = "CreateDisaster returned false (disaster buffer full?)";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorFull", _lastFailure);
                return true;
            }

            // ★ ここで初めて取る（メソッド doc）。CreateDisaster より前に取った参照は
            //    FastList.Add で作り直されている可能性がある。
            var buffer = DisasterBuffer();
            if (buffer == null || id == 0 || id >= buffer.Length)
            {
                _lastFailure = "CreateDisaster returned an out-of-range index";
                return true;
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
                Log.Error("typhoon tornado: " + _lastFailure, null);
                Abandon(id);
                return true;
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
            return true;
        }

        // ── 紐づけ ─────────────────────────────────────────────

        /// <summary>
        /// 渦車両を探す。**災害が Active になるまでは探さない**（<see cref="MaxAttachTicks"/>）。
        /// </summary>
        private static void Attach(DisasterData[] buffer)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0 || id >= buffer.Length || _slots[i].VehicleId != 0) continue;
                if ((buffer[id].m_flags & DisasterData.Flags.Active) == 0) continue;
                if (_slots[i].AttachTicks >= MaxAttachTicks) continue;

                _slots[i].AttachTicks++;

                ushort found = FindVortexVehicle(id, buffer[id].m_targetPosition,
                                                 buffer[id].m_intensity);
                if (found != 0)
                {
                    _slots[i].VehicleId = found;
                    Log.Info("typhoon tornado " + id + " attached to vortex vehicle " + found);
                    continue;
                }

                // **隠さない。** 諦めた竜巻は④の軌道に乗らず、バニラの竜巻として
                // 自由に流れる（見た目も破壊も出る）。診断の attached < count が示す。
                if (_slots[i].AttachTicks == MaxAttachTicks)
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorAttach",
                             "typhoon tornado " + id + " never got a vortex vehicle; "
                             + "it drifts on vanilla's own path");
                }
            }
        }

        /// <summary>
        /// 災害グループに属する車両のうち、渦の <c>VehicleAI</c> を持つものを探す。
        /// <c>TornadoAI.GetPosition</c> と同じ経路（<c>InstanceID.Disaster</c> →
        /// <c>InstanceManager.GetAllGroupInstances</c>）。③の
        /// <c>FireWhirlPinner.FindVortexVehicle</c> を写した形で、
        /// **発生地点から離れすぎている候補は棄却する**（災害 ID 使い回し対策）。
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId, Vector3 expectedCentre,
                                                byte intensity)
        {
            var id = InstanceID.Empty;
            id.Disaster = disasterId;

            _tempInstances.Clear();
            InstanceManager.GetAllGroupInstances(id, _tempInstances);
            if (!Singleton<VehicleManager>.exists) return 0;

            var buffer = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            if (buffer == null) return 0;

            // TornadoAI.ActivateDisaster の生成距離（本タスクで IL 実測）。
            float expected = intensity * 10f + 400f;
            float limit = expected * AttachDistanceFactor;
            float limitSq = limit * limit;

            for (int i = 0; i < _tempInstances.m_size; i++)
            {
                ushort v = _tempInstances.m_buffer[i].Vehicle;
                if (v == 0 || v >= buffer.Length) continue;

                var info = buffer[v].Info;
                if (info == null || !(info.m_vehicleAI is VortexAI)) continue;

                Vector3 pos = buffer[v].GetLastFrameData().m_position;
                float dx = pos.x - expectedCentre.x;
                float dz = pos.z - expectedCentre.z;
                if (dx * dx + dz * dz > limitSq)
                {
                    // 遠すぎる = 災害 ID 使い回しで無関係な渦を拾った可能性。
                    // ここで掴むとバニラの竜巻を④が引きずり回すことになる。
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorReject",
                             "vortex vehicle " + v + " for tornado " + disasterId
                             + " is too far from the expected centre; rejecting");
                    continue;
                }

                return v;
            }
            return 0;
        }

        // ── 操舵 ───────────────────────────────────────────────

        /// <summary>
        /// 台風の中心の周りを回らせる。位相の進みには**2 段の上限**がある。
        /// まず 1 tick の角度の増分を「車両が追える距離 ÷ 軌道半径」で抑える
        /// （抑えないと位相だけが先へ進み、竜巻は円ではなく弦を横切る）。次に、実際に
        /// 書く目標点の移動量も <paramref name="maxStep"/> でクランプする ——
        /// 台風の中心自体が動くので、位相を抑えるだけでは足りない。
        /// </summary>
        private static void Steer(DisasterData[] buffer, float deltaMinutes,
                                  float radius, float maxStep)
        {
            float desiredStep = OrbitDegreesPerMinute * deltaMinutes * 0.017453292f;
            float reachableStep = maxStep / radius;
            float phaseStep = desiredStep < reachableStep ? desiredStep : reachableStep;

            byte intensity = TornadoIntensity();
            float angle = TyphoonController.HeadingRadians;

            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0 || id >= buffer.Length) continue;

                // Verify がこの tick で確かめているが、get_Info は境界検査をしない
                // 4 命令なので、参照は毎回自分で確かめてから使う。
                var info = buffer[id].Info;
                if (info == null || info.m_disasterAI == null) continue;

                _slots[i].OrbitPhase += phaseStep;

                var wanted = OrbitPoint(_slots[i].OrbitPhase, radius);
                var target = ClampStep(_slots[i].Target, wanted, maxStep);

                var ai = info.m_disasterAI;
                ai.ClampDisasterTarget(ref target);
                target.y = SampleHeight(target);

                // Emerging 中も書く。ActivateDisaster は m_targetPosition を読んで
                // 渦車両の生成位置を決めるので（§B-2）、書いておくと軌道上に生まれる。
                buffer[id].m_targetPosition = target;
                buffer[id].m_angle = angle;
                buffer[id].m_intensity = intensity;
                _slots[i].Target = target;

                ushort v = _slots[i].VehicleId;
                if (v == 0) continue;

                // ★ **車両 ID も再利用される。** 渦が Unspawn されたあと同じ添字が
                //   普通のバスに配られる。掴んだままだと④がその車両の目標地点を毎 tick
                //   書き潰し、市内の車が 1 台だけ台風の周りを回り出す（例外は出ない）。
                //   探し直しはさせない —— 渦が消えた＝竜巻自体が終わりかけなので
                //   （TornadoAI.IsStillActive は渦が生きている間だけ true。§B-2）、
                //   Verify がまもなくスロットを外す。
                if (!StillOurVortex(v))
                {
                    _slots[i].VehicleId = 0;
                    _slots[i].AttachTicks = MaxAttachTicks;
                    continue;
                }

                // ★ 罠 3: **両スロットに書く**（クラス doc）。
                WriteVehicleTarget(v, target);
            }
        }

        /// <summary>
        /// <paramref name="from"/> から <paramref name="to"/> へ、水平距離
        /// <paramref name="maxStep"/> までしか動かさない。
        /// </summary>
        private static Vector3 ClampStep(Vector3 from, Vector3 to, float maxStep)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float d2 = dx * dx + dz * dz;
            if (d2 <= maxStep * maxStep || d2 <= 0f) return to;

            float scale = maxStep / Mathf.Sqrt(d2);
            return new Vector3(from.x + dx * scale, to.y, from.z + dz * scale);
        }

        /// <summary>位相と半径から台風の中心まわりの点を出す。</summary>
        private static Vector3 OrbitPoint(float phase, float radius)
        {
            Vec3 centre = TyphoonController.Centre;
            return new Vector3(centre.X + Mathf.Cos(phase) * radius,
                               0f,
                               centre.Z + Mathf.Sin(phase) * radius);
        }

        /// <summary>
        /// 台風の今の強度から竜巻の強度を出す。**<c>(byte)</c> へのキャストの前に
        /// クランプすること**（範囲外を先にキャストすると最弱の台風が最強の竜巻を産む）。
        /// </summary>
        private static byte TornadoIntensity()
        {
            int value = (int)(TyphoonController.Intensity * IntensityFraction);
            if (value < MinTornadoIntensity) value = MinTornadoIntensity;
            if (value > MaxTornadoIntensity) value = MaxTornadoIntensity;
            return (byte)value;
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

        private static void StopOne(DisasterData[] buffer, TyphoonTornadoSlot slot)
        {
            // ★ 車両 ID の再利用を先に弾く（<see cref="StillOurVortex"/> の注記）。
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

            if (buffer == null || slot.DisasterId >= buffer.Length) return;
            if ((buffer[slot.DisasterId].m_flags & DisasterData.Flags.Created) == 0) return;

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
