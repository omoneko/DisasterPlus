using ColossalFramework;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 津波連鎖が今どうなっているか。**表示側はこれを必ず名乗る。**
    /// </summary>
    public enum TsunamiChainState
    {
        /// <summary>予約も発生もしていない（陸の震源を含む）。行を出さない。</summary>
        Idle,

        /// <summary>海中の震源を見つけ、遅延の満了を待っている。</summary>
        Scheduled,

        /// <summary>津波を起こし、波が実際に立った（<c>m_waveIndex != 0</c>）。</summary>
        Raised,

        /// <summary>
        /// 起こしたが <c>FindSea</c> が海側区間を見つけられず、波が立たなかった。
        /// **これは失敗ではない。** 内陸マップでは正常な結果である（§B-3）。
        /// </summary>
        NoSea,

        /// <summary>Natural Disasters DLC が無く、<c>TsunamiAI</c> のプレハブが存在しない（§B-5）。</summary>
        NoDlc,

        /// <summary>災害スロットが満杯など。理由は診断へ出し、パネルには行を出さない。</summary>
        Failed
    }

    /// <summary>
    /// **第 2 層の 1 つ目。海中で地震が起きたら、遅れて津波を起こす。**
    /// <b>sim スレッド専用。</b>既定 OFF。
    ///
    /// ── できることと、できないこと（UI の文言はこれで決まる） ──────────────
    ///
    /// 依頼は「海中で地震を起こしてもプレート境界型の津波が来ない」だった。
    /// **「震源から波が広がる」は <c>TsunamiAI</c> では literally 不可能である**（§B-3）:
    ///
    ///   - <c>FindSea</c> は**マップ外周セルしか候補にしない**（1080 &lt;&lt; 3 = 4320 個）
    ///   - <c>m_targetPosition</c> は「どの外周区間を選ぶか」のヒントにしかならず、
    ///     開始時に原点セルの座標で**上書きされる**（IL_03D3–0426）
    ///   - <c>m_angle</c> も区間の内向き法線から導出されて**上書きされる**（IL_042B–0438）
    ///
    /// したがって実現するのは「**震源に最も近い海側の外周から津波が来る**」であり、
    /// パネルはそう書く（<c>Strings.EarthquakeTsunamiFromShore</c>）。
    ///
    /// ★★ **2026-08-29、そこはもう当てはまらない。** <c>TsunamiAI</c> は使わず、
    ///    <c>TsunamiWave</c> が <c>TYPE_IMPACT</c> の水波を震源に置く。
    ///    波は震源から同心円状に広がる。
    /// **「震源から波が広がります」と書いてはいけない。**
    ///
    /// ── 4 つの罠（全部 IL で確定済み） ────────────────────────────
    ///
    /// 1. <c>m_flags |= SelfTrigger (64)</c> は**必須**。<c>TsunamiAI.StartDisaster</c> は
    ///    IL_000E でこのビットを見て、立っていなければ即 return する（§B-2）。
    ///    そのとき波は 1 つも作られない。
    ///    <b>ただし地震と違い、津波は「Emerging で固まる」わけではない</b> ——
    ///    本タスクで再実測したところ、<c>TsunamiAI.IsStillEmerging</c> /
    ///    <c>IsStillActive</c> / <c>IsStillClearing</c> はどれも
    ///    <c>m_activationFrame</c> を**一度も読まない**（使うのは <c>m_startFrame</c> と
    ///    <c>m_angle</c> と <c>m_targetPosition</c> だけ）。設計書 §4.1 は
    ///    「立てないと災害が Emerging のまま永久に固まる（地震・津波の両方）」と
    ///    書いているが、**後半は津波には当てはまらない**。結論（必ず立てる）は同じ。
    /// 2. <c>CreateDisaster</c> の**戻り値を必ず見る**。false のとき出力は 0 になり、
    ///    そのまま書き込むと**他人の災害スロットを書き潰す**（§E-1、上限 256）。
    /// 3. <c>FindDisasterInfo&lt;TsunamiAI&gt;()</c> が null なら DLC が無い（§B-5）。
    ///    <c>ModCompat.NaturalDisastersOwned</c> は事前判定にすぎず、
    ///    **実行直前の権威はこの走査**である。
    /// 4. <c>FindSea</c> は海側区間が 10 セル未満だと false を返し、津波は起きない（§B-3）。
    ///    **内陸マップでは正常に何も起きない。これを失敗として扱わない。**
    ///
    /// ── <c>FindSea</c> が失敗したときの後始末（計画 Step 1 の結論） ─────────────
    ///
    /// 計画は「後始末の方法を IL で確定させてから決める」としていた。実測した結果:
    ///
    /// ```
    /// TsunamiAI.StartDisaster   IL_0003 base.StartDisaster（★ SelfTrigger の判定より前）
    ///                           IL_000E if ((m_flags & 64) == 0) return
    ///                           IL_003D if (!FindSea(...)) return      ← ここで抜ける
    /// DisasterAI.StartDisaster  m_flags = (m_flags & 0xFFFC88C7) | Emerging(4)
    ///                           m_startFrame = m_currentFrameIndex     ★ 必ず入る
    ///                           （m_activationFrame には一切書かない）
    /// TsunamiAI.IsStillEmerging elapsed = currentFrame - m_startFrame
    ///                           travel  = elapsed * 0.125
    ///                           dir     = (-sin(m_angle), 0, cos(m_angle))
    ///                           corner  = 進行方向と逆側の ±4800
    ///                           return dot(corner - m_targetPosition, dir) > travel
    /// IsStillActive / IsStillClearing も同じ形（Active は travel から 3000 を引き、
    /// Clearing は corner を進行方向側に取る）。**3 つとも m_activationFrame を読まない。**
    /// ```
    ///
    /// つまり波が立たなくても位相は <c>m_startFrame</c> を基準に自然に進み、
    /// <c>Finished</c> になった時点で <c>DisasterManager.SimulationStepImpl</c> が
    /// <c>ReleaseDisaster</c> を呼んでスロットを解放する（§E-1）。
    /// 最悪でも 107520 フレーム（≒39 ゲーム内時間）で消える。
    /// → 計画の対応表の 1 行目「**何もしない（ログのみ）**」を採る。
    ///
    /// <c>DisasterManager.ReleaseDisaster(ushort)</c> は public であることも確認したが、
    /// **使わない**。<c>base.StartDisaster</c> は既に
    /// <c>DisasterWrapper.OnDisasterStarted(id)</c> を呼んでおり、その直後に
    /// スロットを消すのは <c>IDisastersExtension</c> を実装した MOD から見て
    /// 「開始したのに何の終了通知も無い災害」になる。固まらないと分かっている以上、
    /// 検証していない副作用を足す理由が無い。
    ///
    /// **いずれにせよ <c>m_waveIndex == 0</c> の判定は必ず行う**（<c>DisasterData.m_waveIndex</c>
    /// は public UInt16、本タスクで実測）。波が立たなかったことを
    /// <see cref="TsunamiChainState.NoSea"/> として UI に理由付きで出す。
    ///
    /// ── 監視するのは 1 個だけ ────────────────────────────────
    ///
    /// 同時に複数の地震から津波を出さない。②が上限 256 の災害スロットを
    /// 食い潰す形を作らないための制限である。
    /// </summary>
    public static class TsunamiChain
    {
        /// <summary>
        /// 海溝型地震のあと津波が来るまで（ゲーム内分）。
        ///
        /// ★ 設定の <c>eqTsunamiDelay</c> がこれより長ければ、海溝型に限って
        ///   こちらまで縮める。**短く設定している人の値は尊重する**（縮めるだけ）。
        /// </summary>
        internal const int TrenchDelayMinutes = 3;

        /// <summary>
        /// 強度の下限。強度 0 の津波は波高が 0 になり（<c>m_delta = m_height * 1024 * i / 55</c>、
        /// §B-3）、「起こしたのに何も起きない」という原因の分からない状態になる。
        /// </summary>
        private const byte MinIntensity = 10;

        private static ushort _quakeId;
        private static bool _havePhase;
        private static EarthquakePhase _lastPhase;
        private static uint _dueFrame;
        private static TsunamiChainState _state;

        /// <summary>例外を 1 回だけ <c>Log.Error</c> で出したか（以後は Diag へ落とす）。</summary>
        private static bool _errorLogged;

        /// <summary>
        /// 今の状態。**sim スレッドが書き、<see cref="EarthquakeReader"/> が同じ
        /// スレッドで読んでスナップショットへ載せる。** main スレッドはここを直接読まない。
        /// </summary>
        public static TsunamiChainState State { get { return _state; } }

        /// <summary>予約の満了フレーム。<see cref="State"/> が Scheduled のときだけ意味を持つ。</summary>
        public static uint DueFrame { get { return _dueFrame; } }

        /// <summary>
        /// この地震に<b>まだ津波を負っているか</b>。
        ///
        /// ★★ **2 つ目の海溝型を断る判定はこれで行う。**
        ///   理由は「追える津波が 1 本だけ」であって、地震のスロットが
        ///   17〜35 実分も生きることではない（<c>TrenchQuakeSlot.RaiseCore</c> の ★★）。
        ///
        /// ★★ <b>「予約済みか波が出ている最中か」だけでは足りない。</b>
        ///   （2026-08-30、Codex P1）地震が Emerging のあいだ、こちらはまだ
        ///   その地震を拾っていないので状態は Idle である。そこで 2 発目を通すと
        ///   <b>1 発目の印が奪われ、しかも 2 発目は拾われない</b>。
        ///   だから<b>決着（Raised / NoSea / NoDlc / Failed）が付くまで</b>
        ///   負っていることにする。
        ///
        /// ★ 災害スロットが空けば <c>IsTrenchQuake</c> が忘れるので、
        ///   この錠が地震より長生きすることはない。
        /// </summary>
        public static bool StillOwes(ushort quakeId)
        {
            if (quakeId == 0) return false;
            if (TsunamiRing.Running) return true;

            // まだ拾っていない（Emerging の最中など）。負っている。
            if (_quakeId != quakeId) return true;

            return _state == TsunamiChainState.Idle
                   || _state == TsunamiChainState.Scheduled;
        }

        /// <summary>
        /// **新しい海溝型地震が起きたときに呼ぶ（sim スレッド）。**
        /// 追いかける相手を捨てて、次の tick から拾い直せるようにする。
        ///
        /// ★★ これが無いと、前の地震が Clearing で生きているあいだ
        ///   <c>PickCandidate</c> は<b>古い相手を追い続け</b>、新しい海溝型は
        ///   Emerging→Active の瞬間を見逃されて<b>津波を取りこぼす</b>
        ///   （2026-08-30、Codex P1）。前の津波は既に出し終えている
        ///   （<see cref="StillOwes"/> がそれを保証する）ので、捨ててよい。
        /// </summary>
        public static void Retarget()
        {
            Forget();
        }

        /// <summary>監視している地震（災害バッファ上の添字）。0 なら監視していない。</summary>
        public static ushort QuakeId { get { return _quakeId; } }

        /// <summary>
        /// **レベルアンロードで必ず呼ぶ。** 予約は都市をまたいで残らない
        /// （セッション状態であって、セーブにも入れない）。
        /// </summary>
        public static void Reset()
        {
            Forget();
            // ★ _errorLogged は戻さない（第 2 層レビュー M4）。
            //    「この経路は投げる」は、この DLL が参照しているゲームのビルドに対する
            //    事実であって都市ごとの状態ではないので、都市を替えても変わらない。
            //    前例は EarthquakeReader._readErrorLogged / LongPeriodDamage._errorLogged
            //    で、どちらもレベルアンロードで戻していない。ここだけ戻していたのは
            //    取りこぼしで、同じ質問に 2 つの逆の答えが doc として書かれていた。
        }

        /// <summary>
        /// 監視をやめて表示も畳む。<see cref="_errorLogged"/> は
        /// 触らない —— あれは「同じ例外で output_log を埋めない」ための
        /// **ゲームのビルドに対する事実**で、地震 1 個が終わるたびに巻き戻すと
        /// <c>Log.Error</c> の連投を許してしまう。
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _havePhase = false;
            _lastPhase = EarthquakePhase.Unknown;
            _dueFrame = 0u;
            _state = TsunamiChainState.Idle;
        }

        /// <summary>
        /// 前提検証用。**副作用なしに** <c>TsunamiAI</c> のプレハブが在るかだけを返す。
        /// キャッシュを書き換える関数へ委譲しない（<c>FireWhirlSpawner.HasTornadoPrefab</c>
        /// と同じ理由。あちらは sim スレッドのキャッシュを main から巻き戻していた）。
        /// </summary>
        public static bool HasTsunamiPrefab()
        {
            return FindTsunamiInfo() != null;
        }

        /// <summary>
        /// sim スレッド。**必ず <c>EarthquakeFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に予約が進むと、止めているはずのゲーム内時間で
        /// 津波が来る）。設定が OFF のときは呼び出し側が呼ばない。
        /// </summary>
        public static void Tick(EarthquakeSnapshot snapshot, uint frame)
        {
            if (snapshot == null || !snapshot.Valid) return;

            try
            {
                Step(snapshot, frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("tsunami chain failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqTsunami", "tsunami chain failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, uint frame)
        {
            // ★★ 追っている地震が<b>本当に同じ地震か</b>を種でも確かめる
            //    （2026-08-30、第 5 回検証）。災害の番号は使い回されるので、
            //    番号だけで追うと<b>別の地震に津波を付けかねない</b>。
            //    海溝型を追っているときだけ効く（バニラの地震は元々採らない）。
            if (_quakeId != 0 && _quakeId == TrenchQuakeSlot.LastId
                && !TrenchQuakeSlot.IsTrenchQuake(_quakeId))
            {
                Forget();
            }

            var quake = FindTracked(snapshot);
            if (quake == null)
            {
                // 監視対象が消えた（Finished またはバッファから消えた）。
                // 表示していた結果もここで畳む —— 終わった地震について
                // 「30 分後に津波」と出し続ける方が悪い。
                if (_quakeId != 0) Forget();
                quake = PickCandidate(snapshot);
                if (quake == null) return;

                _quakeId = quake.DisasterId;
                _lastPhase = quake.Phase;
                _havePhase = true;
                _state = TsunamiChainState.Idle;
                return;
            }

            if (_state == TsunamiChainState.Scheduled)
            {
                // uint の巻き戻り（約 4739 年）はゲーム内で起きないが、
                // 引き算ではなく比較で書いておく。
                if (frame >= _dueFrame) Raise(quake);
                _lastPhase = quake.Phase;
                return;
            }

            if (_state == TsunamiChainState.Idle
                && _havePhase
                && _lastPhase == EarthquakePhase.Emerging
                && quake.Phase == EarthquakePhase.Active)
            {
                Schedule(quake, frame);
            }

            _lastPhase = quake.Phase;
            _havePhase = true;
        }

        /// <summary>監視中の地震をスナップショットから引く。位相が終わっていれば null。</summary>
        private static EarthquakeReading FindTracked(EarthquakeSnapshot snapshot)
        {
            if (_quakeId == 0) return null;

            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].DisasterId != _quakeId) continue;
                var q = quakes[i];
                bool alive = q.Phase == EarthquakePhase.Emerging
                             || q.Phase == EarthquakePhase.Active
                             || q.Phase == EarthquakePhase.Clearing;
                return alive ? q : null;
            }
            return null;
        }

        /// <summary>
        /// 新しく監視する地震を 1 個選ぶ。**Emerging のものだけ**を採る ——
        /// 本震（Emerging → Active）の瞬間を観測できないと、連鎖の起点が決まらない。
        /// 途中から見た地震について「今が本震だ」と決めつけない。
        /// </summary>
        private static EarthquakeReading PickCandidate(EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].Phase != EarthquakePhase.Emerging) continue;

                // ★★ **津波が付くのは海溝型地震だけである**（2026-08-22、所有者の指示）。
                //
                //    > バニラの地震では津波は発生させず、新たに新設する海溝型地震
                //    > （アイコンも新規で）でのみ発生するようにしてください。
                //
                //    見分けは<b>災害 ID</b>で行う（<c>TrenchQuakeSlot.IsTrenchQuake</c>）。
                //    ★ **震源が海の上かどうかで判定しない。** プレイヤーがバニラの
                //      災害パネルから海に地震を置くこともでき、それは断層型のつもりで
                //      置いたものである。位置で見るとそこにも津波が付いてしまう。
                if (!TrenchQuakeSlot.IsTrenchQuake(quakes[i].DisasterId)) continue;

                return quakes[i];
            }
            return null;
        }

        /// <summary>
        /// 本震の瞬間。震源が水中なら遅延を予約する。
        ///
        /// 陸の震源では <see cref="TsunamiChainState.Idle"/> のまま何も出さない ——
        /// 「陸だったので津波はありません」は当たり前のことであり、
        /// 毎回の地震でそれを名乗ると、本当に言うべきこと（海だったのに海が無い）が埋もれる。
        /// </summary>
        private static void Schedule(EarthquakeReading quake, uint frame)
        {
            // DLC の権威はプレハブの実在（§B-5）。ModCompat は UI を出すかどうかの事前判定。
            if (FindTsunamiInfo() == null)
            {
                _state = TsunamiChainState.NoDlc;
                Log.Info("tsunami NOT raised: the Natural Disasters DLC is not owned, "
                         + "so there is no TsunamiAI prefab to name the disaster after");
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiNoDlc",
                    "no TsunamiAI DisasterInfo found; the Natural Disasters DLC is required "
                    + "for the tsunami chain");
                return;
            }

            if (!IsUnderWater(quake.Epicentre)) return;

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            // .cgs の値は公開契約なので読み捨てないが、負の値を uint へ落とすと
            // 巨大なフレーム数になり、予約が事実上永久に満了しなくなる。
            // 範囲はスライダーが 5〜120 に縛っているものの、手で編集された
            // 設定ファイルに対してもここが破綻しないようにする。
            // ★★ **海溝型地震の津波は「すぐ」である。**（2026-08-25、所有者の指示）
            //
            //    <c>eqTsunamiDelay</c> の既定は 30 ゲーム内分だった。あれは
            //    「遠地津波が届くまで」の感覚で置いた値だが、海溝型は<b>沖合すぐ</b>
            //    で起きるので、実際にも数分で第一波が来る。
            //    設定を触っていない人には <see cref="TrenchDelayMinutes"/> を使う。
            int minutes = ModSettings.EarthquakeTsunamiDelayMinutes.value;
            if (TrenchQuakeSlot.IsTrenchQuake(quake.DisasterId)
                && minutes > TrenchDelayMinutes)
            {
                minutes = TrenchDelayMinutes;
            }
            if (minutes < 0) minutes = 0;

            uint delay = (uint)(minutes * framesPerMinute);
            uint baseFrame = quake.ActivationScheduled ? quake.ActivationFrame : frame;
            _dueFrame = baseFrame + delay;
            _state = TsunamiChainState.Scheduled;

            // ★★ **海溝型なら必ず出す。**（第 3 回検証）Diag だけだと
            //    LogChannel.DefaultMask は General だけなので、既定では
            //    「いつ波が来るのか」がどこにも残らない。
            Log.Info("tsunami scheduled for quake #" + quake.DisasterId + " at frame "
                     + _dueFrame + " (" + minutes
                     + " in-game minutes from now). After that the drive runs for "
                     + DisasterPlus.Core.Earthquake.TsunamiSource.TotalSteps.ToString("F0")
                     + " water steps and the sea over the epicentre needs about 100 real "
                     + "seconds to lift a metre, so give it time before calling it broken");
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiSchedule",
                "undersea quake #" + quake.DisasterId + "; tsunami scheduled for frame "
                + _dueFrame);
        }

        /// <summary>
        /// 震源が水中か。<c>TerrainManager.HasWater(Vector2)</c> は public インスタンス
        /// メソッドで、引数は**ワールド XZ 座標**（本タスクで IL 実測）:
        ///
        /// ```
        /// x = FloorToInt((position.x + 8640) * 16) >> 8   // = 16 m セル、[0,1080] にクランプ
        /// z = FloorToInt((position.y + 8640) * 16) >> 8
        /// 4 隅のセルを WaterSimulation.BeginRead() の配列から読み、
        /// m_height が全て 0 なら false（水がまったく無い）。
        /// そうでなければ水面高と地形高を双一次補間し、差が 8（＝ 1/64 m 単位で 0.125 m）
        /// 以上なら true。
        /// ```
        ///
        /// <c>BeginRead()</c> / <c>EndRead()</c> を取るので**sim スレッドから呼ぶこと**。
        /// </summary>
        private static bool IsUnderWater(DisasterPlus.Core.Common.Vec3 epicentre)
        {
            if (!Singleton<TerrainManager>.exists) return false;
            // VectorUtils.XZ(Vector3) と同じ変換（x, z）。型を 1 つ減らすために直接組む。
            return Singleton<TerrainManager>.instance.HasWater(
                new Vector2(epicentre.X, epicentre.Z));
        }

        /// <summary>
        /// 津波を起こす。<c>DisasterTool.&lt;CreateDisaster&gt;c__Iterator0.MoveNext</c> の
        /// IL_0071–01E8 をそのまま写した手順である（§A-1）。
        /// </summary>
        private static void Raise(EarthquakeReading quake)
        {
            // ★★ **DLC の津波（TsunamiAI）はもう使わない。**（2026-08-25、所有者の指示）
            //
            //    > DLC の津波を使うのをやめましょう。代わりに海溝型地震の震源地付近を
            //    > 中心とした領域で一定時間持続的な海面上昇（震源地を中心に
            //    > ２-3 個の連続する山状：実際の津波メカニズムで）を発生させてください。
            //
            //    <c>TsunamiAI</c> は<b>震源から波を出せない</b> —— <c>FindSea</c> が
            //    候補にするのはマップ外周のセルだけで、<c>m_targetPosition</c> は
            //    開始時に原点セルの座標で上書きされる（§B-3、IL_03D3-0426）。
            //    つまり「沖合の震源から同心円状に広がる波」は原理的に作れなかった。
            //
            //    いまは <c>TsunamiWave</c> が震源に <c>TYPE_IMPACT</c> の水波を
            //    1 個置き、<c>Core.Earthquake.TsunamiSource</c> の式でその外力を
            //    毎 tick 書き換える。IMPACT はマップのどこにでも置けて、
            //    <b>そこに水の山があるかのように水面の傾きを足す</b>
            //    ＝ 海底の隆起と同じ外力である（IL 実測、
            //    docs/superpowers/specs/2026-08-29-tsunami-il-facts.md）。
            //    **同心円状の水の壁は、そのあとゲーム自身の浅水ソルバが作る**
            //    —— バニラの津波の水の壁とまったく同じ経路である。
            uint frame = 0u;
            if (Singleton<SimulationManager>.exists)
            {
                frame = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            }

            if (!TsunamiRing.Begin(quake.Epicentre, quake.Intensity, frame))
            {
                // ★ 海が無い／水シミュが読めない。**失敗ではない場合がある**ので、
                //   理由をそのまま持ち帰る（TsunamiRing.Detail）。
                _state = TsunamiChainState.NoSea;
                Log.Info("tsunami NOT raised: "
                         + (TsunamiRing.Detail ?? "the wave could not be raised"));
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "EqTsunamiNoSea",
                    TsunamiRing.Detail ?? "the tsunami could not be raised");
                return;
            }

            _state = TsunamiChainState.Raised;
        }

        /// <summary>
        /// <c>TsunamiAI</c> を持つ災害プレハブ。**キャッシュしない。**
        /// <c>FindDisasterInfo&lt;T&gt;</c> は <c>PrefabCollection</c> を舐めるだけの
        /// public static な走査（§B-5）で、呼ぶのは前提検証と津波を起こす瞬間だけである。
        /// キャッシュを持つと、それを main スレッドの前提検証と共有することになる。
        /// </summary>
        private static DisasterInfo FindTsunamiInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<TsunamiAI>();
            }
            catch
            {
                // プレハブ走査で落ちても機能を巻き込まない（壊れた MOD の DisasterInfo 等）。
                return null;
            }
        }
    }
}
