using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 河川氾濫の状態。パネルは <see cref="Idle"/> のとき行を出さず、
    /// <see cref="NoSources"/> のとき**「不具合ではありません」と理由を出す**
    /// （設計書 §7.4。①の「なぜハザードマップが空か」と同じ扱い）。
    /// </summary>
    public enum TyphoonFloodState
    {
        /// <summary>まだ何も起きていない（台風が強風域に入っていない）。</summary>
        Idle,

        /// <summary>
        /// 台風の近くに <c>TYPE_NATURAL</c> の水源が 1 個も無い。
        /// **これは正常な結果である。** 内陸の池だけのマップ、水源をエディタで
        /// 置いていないマップではこれが正しい。
        /// </summary>
        NoSources,

        /// <summary>水位を持ち上げている。</summary>
        Raised,

        /// <summary>持ち上げたぶんを元に戻した。水は吸い込み側が自然に引かせる。</summary>
        Restored,

        /// <summary>
        /// 到達経路が使えなかった。**パネルには行を出さず**、理由を診断に出す
        /// （<see cref="TyphoonFlood.LastFailure"/>）。
        /// </summary>
        Failed
    }

    /// <summary>
    /// ④が持ち上げている水源 1 個ぶんの台帳。
    ///
    /// <see cref="Original"/> は**必ず④が触る前の値**である。走査のたびに
    /// <see cref="Raised"/> は変わるが（台風が近づけば上げ幅が増える）、
    /// <see cref="Original"/> は最初に掴んだ値のまま持ち続ける ——
    /// 持ち上げた値を「元の値」として上書きしたら、復元しても水位が戻らない。
    ///
    /// <see cref="Handle"/> は <c>WaterSimulation</c> のハンドルで、**1 基点**である
    /// （<see cref="TyphoonFlood"/> のクラス doc の IL 実測）。
    /// </summary>
    public struct TyphoonFloodedSource
    {
        public readonly ushort Handle;
        public readonly ushort Original;
        public readonly ushort Raised;

        public TyphoonFloodedSource(ushort handle, ushort original, ushort raised)
        {
            Handle = handle;
            Original = original;
            Raised = raised;
        }
    }

    /// <summary>
    /// 台風による河川氾濫。<b>sim スレッド専用。</b>既定 ON。
    ///
    /// **この機能だけが「ゲームを固める」と「セーブを壊す」を同時に持つ。**
    ///
    /// ── 先に潰しておく 3 つの不成立ルート（設計書 §1.3）───────────────
    ///
    /// 1. **洪水災害はバニラに無い。** <c>GenericFloodAI</c> はフィールド 0・
    ///    メソッド 0 の空クラス（§D-1）
    /// 2. **海面上昇は使えない。** ゲームプレイ中に <c>m_nextSeaLevel</c> を動かす
    ///    バニラのコードは無く、全マップ一律なので河川の局所氾濫にならない（§D-1）
    /// 3. **<c>TYPE_TSUNAMI</c> の波を川に置いても何も起きない。**
    ///    <c>GetSeaLevel</c> は <c>SimulateWater</c> の**マップ外周リングでしか
    ///    評価されない**（§D-3(a)）。地震ファクト §B-3 が「逃げ道」として明記していた
    ///    経路は成立しない
    ///
    /// ── 成立する唯一のルート（§D-4）──────────────────────────
    ///
    /// <c>m_type == TYPE_NATURAL(1)</c> の水源は**目標水位 <c>m_target</c> まで注ぎ、
    /// 超えたら吸い戻す自己調整の泉**である。しかも吐き出し側のループは
    /// <c>natural &amp;&amp; terrain[i] &gt;= m_target</c> のセルを**スキップする**
    /// （IL_1E5E / IL_1EBF）ので、**丘の上には水を載せず、谷筋だけが濡れる**。
    /// これは河川氾濫そのものである。
    ///
    /// **新しい水源は作らない**（設計書 §2 が (i) を選び (ii) を代案としている）。
    /// <c>CreateWaterSource</c> を呼ばなければ、「上限 65535 で false」も
    /// 「注いだ水が引かない」も最初から発生しない。
    /// **次の担当者へ: 「もっと強い氾濫を」と <c>CreateWaterSource</c> に手を伸ばさないこと。**
    /// 新しい泉を置くと、止めたあとに残った水を引かせる手段が
    /// 「吸い込み側の水源を残す」しか無くなり、復元経路が 1 本増える。
    ///
    /// <c>DisasterHelpers.SplashWater</c>（<c>TYPE_IMPACT</c> の波）を演出として
    /// 重ねてもよいが、**体積は増えないのでこれ単独では氾濫にならない**（§D-3(b)）。
    /// **本タスクでは足さない** —— 水面の見た目より <c>m_target</c> の復元を優先する。
    ///
    /// ── ロックの取り方（罠 3。落とすとゲームが無反応になる）─────────────
    ///
    /// **本タスクで IL を直接読み直して確定させた**（§D-4 の主張の再確認）:
    ///
    /// ```
    /// WaterSimulation.LockWaterSource(ushort source)     // public, instance
    ///   IL_0000  br IL_0005
    ///   IL_0005  Monitor.TryEnter(m_waterSources, 0) ; brfalse IL_0005   // ★ スピンロック
    ///   IL_0016  return m_waterSources.m_buffer[source - 1]              // ★ 1 基点
    ///   -- Monitor.Exit も try/finally 領域も無い（leave / endfinally が 1 つも無い）--
    ///
    /// WaterSimulation.UnlockWaterSource(ushort source, WaterSource data) // public, instance
    ///   IL_0000  m_waterSources.m_buffer[source - 1] = data              // ★ 1 基点
    ///   IL_0019  Monitor.Exit(m_waterSources)                            // ★ 唯一の解放経路
    /// ```
    ///
    /// したがって <c>UnlockWaterSource</c> は**必ず <c>finally</c> に置く**。落とすと
    /// 水シミュ専用スレッドが <c>TryEnter(_, 0)</c> のループで永久に回り、
    /// **ゲームが無反応になる。**
    ///
    /// **<c>try</c> の中で例外を投げうる処理を増やさない。** ログも診断カウンタの更新も
    /// <c>finally</c> の**外**でやる。<c>Log.Diag</c> は内部で <c>lock</c> を取るので、
    /// 水源のモニタを握ったまま呼ぶと**2 本のロックの取得順**という、この MOD が
    /// まだ一度も抱えていない種類の問題を作ることになる。
    ///
    /// ── ★ ハンドルは呼ぶ前に検証する（IL から新たに分かった危険）──────────
    ///
    /// <c>LockWaterSource</c> は <c>source</c> の範囲を**検査しない**。
    /// <c>Monitor.TryEnter</c> は IL_000C、配列アクセスは IL_0024 なので、
    /// **範囲外のハンドルを渡すとモニタを取った直後に <c>IndexOutOfRangeException</c> が
    /// 出て、`Monitor.Exit` に到達しないまま抜ける** ＝ 恒久デッドロックである。
    /// ハンドル 0 は <c>m_buffer[-1]</c> になるので特に危ない。
    /// <see cref="IsValidHandle"/> を通ってからでなければ <c>LockWaterSource</c> を呼ばない。
    ///
    /// ── 走査はロックの外（§8.3）──────────────────────────────
    ///
    /// <c>m_waterSources</c> は public な <c>FastList</c> なので直接読む。
    /// ロックを取るのは**書き換える 1 個ずつ**にする。全件をロックの中で回すと、
    /// 水スレッドが 1 ステップぶん止まる。
    ///
    /// <c>m_buffer</c> を先に、<c>m_size</c> を後に読み、**短いほうで打ち切る** ——
    /// 逆順だと <c>FastList.Add</c> の再確保に挟まれて「新しい長さ ＋ 古い配列」を
    /// 掴み、範囲外になる。
    ///
    /// ── 復元は 3 箇所から呼ぶ（罠 4）────────────────────────────
    ///
    /// 水源は <c>WaterSimulation.Data.Serialize</c> で**セーブに焼き付く**（§D-4）。
    /// <see cref="RestoreAll"/> は**冪等**で、次の 3 箇所から呼ばれる:
    ///
    /// | 呼び元 | なぜ要るか |
    /// |---|---|
    /// | <c>TyphoonController.Forget</c> | 台風が終わった／スロットを失った（通常の経路） |
    /// | <c>TyphoonFeature.OnLevelUnloading</c> | 都市を出るとき。忘れると次の都市で前の都市のハンドルを復元しようとする |
    /// | <c>DisasterPlusSerialization.OnSaveData</c> | 下記。無いと川が溢れたままセーブに焼き付く |
    ///
    /// ── 保存時は「戻して、遅らせて、戻し直す」（§8.5）───────────────────
    ///
    /// 台風の最中にプレイヤーがセーブすると、そのセーブには持ち上げた
    /// <c>m_target</c> が入る。MOD を外してそのセーブを開けば**川は永久に溢れたまま**になる。
    ///
    /// 本プロジェクトは**これと同じ形の失敗を一度出荷している**（一時フラグがセーブに
    /// 漏れた件）。そのとき確定した事実は:
    ///
    /// > **MOD の <c>OnSaveData</c> はバニラの配列書き込みより先に走る。** したがって
    /// > 「<c>OnSaveData</c> の中で clear → <c>finally</c> で再適用」では**漏れる**。
    /// > 再適用は <c>SimulationManager.AddAction</c> で**遅延させる**必要がある。
    ///
    /// <see cref="SnapshotAndRestoreForSave"/> が「今持ち上げているぶんを元に戻し、
    /// その内容を返す」、<see cref="ReapplyAfterSave"/> が「台風がまだ Active なら
    /// 持ち上げ直す」。後者は <c>AddAction</c> の契約により **sim スレッドで走る**。
    /// <c>SimulationManager.AddAction(System.Action)</c> は **public instance、
    /// 戻り値 <c>AsyncAction</c>**（本タスクで IL 実測）。
    ///
    /// ── 起きなかったときに理由を出す（設計書 §7.4）──────────────────
    ///
    /// **対象マップに <c>TYPE_NATURAL</c> の水源が 1 個も無ければ、正常に何も起きない。**
    /// <see cref="TyphoonFloodState.NoSources"/> を立てて理由を表示する。
    /// **警告としてログに出さない** —— 不具合ではない。
    /// <see cref="NaturalSourceCount"/> はマップ依存で未知なので診断に必ず出す。
    /// </summary>
    public static class TyphoonFlood
    {
        /// <summary>走査の間隔（フレーム相当のゲーム内時間）。風害と同じ 256。</summary>
        private const int IntervalFrames = 256;

        /// <summary>
        /// 1 回の走査で**新たに掴む**水源の上限。
        ///
        /// 1 個ごとに水シミュのモニタを取るので、無制限にすると 1 tick のあいだ
        /// 水スレッドを断続的に止め続けることになる。上限に達したぶんは次の走査で拾う
        /// （台帳に載っているぶんの更新はこの上限に数えない —— 数えると、
        /// 台帳が上限より大きくなった瞬間に更新が回らなくなる）。
        /// </summary>
        private const int MaxNewSourcesPerPass = 64;

        /// <summary><c>WaterSource.TYPE_NATURAL</c>（§D-4）。</summary>
        private const ushort TypeNatural = 1;

        /// <summary>
        /// ④が持ち上げている水源の台帳（ハンドル → 元の値と今の値）。
        /// **sim スレッドからのみ触る。**
        /// </summary>
        private static readonly Dictionary<ushort, TyphoonFloodedSource> _raised =
            new Dictionary<ushort, TyphoonFloodedSource>();

        /// <summary>
        /// この走査で範囲内だったハンドル（範囲外に出たものを外すのに使う）。
        /// **毎 tick 作らない**ので使い回す。
        ///
        /// ★ <c>List</c> ではなく <c>HashSet</c>（全体レビュー）。
        ///   <see cref="DropOutOfRange"/> は台帳の全要素について「今回の範囲内か」を
        ///   引くので、List だと <c>Contains</c> が線形走査になり
        ///   **台帳 × 範囲内の掛け算**になる。台帳は大きな川のあるマップでは
        ///   水源の数ぶんまで育ちうるので、256 フレームに 1 回とはいえ
        ///   ここを O(n^2) のまま置かない。.NET 3.5 に HashSet&lt;T&gt; はある
        ///   （System.Core）。
        /// </summary>
        private static readonly HashSet<ushort> _inRange = new HashSet<ushort>();

        /// <summary>台帳から外すハンドルの作業用。毎回 <c>Clear()</c> して使い回す。</summary>
        private static readonly List<ushort> _toDrop = new List<ushort>();

        private static float _minutesSincePass;
        private static TyphoonFloodState _state = TyphoonFloodState.Idle;
        private static int _naturalSourceCount;
        private static float _lastPeakRiseMetres;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>今の状態（設計書 §7.4 の 5 状態）。</summary>
        public static TyphoonFloodState State { get { return _state; } }

        /// <summary>
        /// マップ全体の <c>TYPE_NATURAL</c> 水源の数。
        /// **マップ依存で未知**（§D-4 / 設計書 §6）なので診断に必ず出す。
        /// 0 は不具合ではない。
        /// </summary>
        public static int NaturalSourceCount { get { return _naturalSourceCount; } }

        /// <summary>今④が持ち上げている水源の数。</summary>
        public static int TouchedCount { get { return _raised.Count; } }

        /// <summary>直近の走査で中心に適用した上げ幅（m）。</summary>
        public static float LastPeakRiseMetres { get { return _lastPeakRiseMetres; } }

        /// <summary>
        /// 到達経路が使えなかった理由（英語、診断用。無ければ null）。
        /// **「使えなかった」を「何も起きていない」と見分ける手段がここにしか無い。**
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// **レベルアンロードで必ず呼ぶ。** <see cref="RestoreAll"/> を済ませてから
        /// セッション状態を捨てる。冪等である。
        /// </summary>
        public static void Reset()
        {
            RestoreAll();

            // RestoreAll が（水シミュに到達できず）台帳を空にできなかった場合でも、
            // 次の都市へハンドルを持ち越さない。前の都市のハンドルで復元しにいくと、
            // **無関係な川の水位を書き換える。**
            _raised.Clear();

            _minutesSincePass = 0f;
            _state = TyphoonFloodState.Idle;
            _naturalSourceCount = 0;
            _lastPeakRiseMetres = 0f;
            _lastFailure = null;
            // ★ _errorLogged は戻さない（ゲームのビルドに対する事実であって
            //    都市ごとの状態ではない。TyphoonLightning / TyphoonWind と同じ判断）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>TyphoonFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に川が溢れる）。
        /// 設定が OFF のときは呼び出し側が呼ばない。
        ///
        /// <paramref name="snapshot"/> からは**降雨量だけ**を読む
        /// （<c>WeatherManager.m_currentRain</c>。1 tick 前の値だが、雨量は
        /// 0.0002/step でしか動かないので差は無い）。位置と強度は
        /// <c>TyphoonController</c> の static から同じスレッドで直接読む。
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step(snapshot, deltaMinutes);
            }
            catch (System.Exception e)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = e.GetType().Name + ": " + e.Message;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon river flooding failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFlood",
                             "typhoon river flooding failed: " + e.GetType().Name);
                }

                // ★ 失敗しても持ち上げたままにしない。ここを飛ばすと、
                //    例外の出た走査で掴んだ水源が誰にも戻されずセーブに焼き付く。
                RestoreAll();
            }
        }

        private static void Step(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            // ★ 間隔の累積は**対象の台風より先に**進める（風害と同じ。②の I3）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (!TyphoonController.Active)
            {
                // 台風が終わっていれば必ず戻す。**ここが通常の復元経路ではない**
                // （通常は TyphoonController.Forget → RestoreAll）が、
                // 取りこぼしがあってもここで拾う。
                if (_raised.Count > 0) RestoreAll();
                return;
            }

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;
            _minutesSincePass = 0f;

            int strength = ModSettings.TyphoonFloodStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;

            if (strength == 0)
            {
                // スライダーを 0 にした瞬間に川が引くこと。持ち上げたままにしない。
                if (_raised.Count > 0) RestoreAll();
                return;
            }

            Sweep(snapshot, strength);
        }

        /// <summary>1 回ぶんの走査。</summary>
        private static void Sweep(TyphoonSnapshot snapshot, int strength)
        {
            var sim = WaterSim();
            if (sim == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "TerrainManager.WaterSimulation is not reachable";
                return;
            }

            var sources = sim.m_waterSources;
            if (sources == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "WaterSimulation.m_waterSources is null";
                return;
            }

            // ★ m_buffer を先に、m_size を後に読み、短いほうで打ち切る（クラス doc）。
            var buffer = sources.m_buffer;
            if (buffer == null)
            {
                _state = TyphoonFloodState.Failed;
                _lastFailure = "WaterSimulation.m_waterSources.m_buffer is null";
                return;
            }
            int size = sources.m_size;
            if (size > buffer.Length) size = buffer.Length;

            float gale = TyphoonController.GaleRadius;
            var centre = TyphoonController.Centre;

            float rain = snapshot != null && snapshot.WeatherReadable ? snapshot.Rain : 0f;
            float peak = FloodTarget.RiseMetresOf(TyphoonController.Intensity, rain, strength);
            _lastPeakRiseMetres = peak;

            _inRange.Clear();
            int natural = 0;
            int newlyTaken = 0;
            int applied = 0;

            for (int i = 0; i < size; i++)
            {
                if (buffer[i].m_type != TypeNatural) continue;
                natural++;

                // ★ ハンドルは 1 基点（クラス doc の IL 実測）。
                ushort handle = (ushort)(i + 1);

                float dx = buffer[i].m_outputPosition.x - centre.X;
                float dz = buffer[i].m_outputPosition.z - centre.Z;
                float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

                float rise = FloodTarget.RiseAt(distance, gale, peak);
                if (!(rise > 0f)) continue;

                TyphoonFloodedSource entry;
                bool known = _raised.TryGetValue(handle, out entry);
                if (!known && newlyTaken >= MaxNewSourcesPerPass) continue;

                ushort original, actual;
                if (!WriteTarget(sim, handle, size, known, entry.Original, rise,
                                 out original, out actual))
                {
                    // 型が変わっていた（解放・再利用された）。台帳から外す。
                    if (known) _raised.Remove(handle);
                    continue;
                }

                if (!known) newlyTaken++;

                // ★ Original は**最初に掴んだ値のまま**持ち続ける
                //   （WriteTarget が known のときは引数をそのまま返す）。
                //   持ち上げた値を「元の値」にすると、復元しても水位が戻らない。
                _raised[handle] = new TyphoonFloodedSource(handle, original, actual);
                _inRange.Add(handle);
                applied++;
            }

            _naturalSourceCount = natural;

            DropOutOfRange(sim, size);

            if (applied > 0) _state = TyphoonFloodState.Raised;
            else if (_raised.Count > 0) _state = TyphoonFloodState.Raised;
            else _state = TyphoonFloodState.NoSources;

            _lastFailure = null;

            WriteDiag(natural, applied, peak, gale);
        }

        /// <summary>
        /// 台帳にあるが今回の範囲に入らなかった水源を元に戻して台帳から外す。
        /// 台風が遠ざかれば川が引く、という当たり前の挙動がここにある。
        /// </summary>
        private static void DropOutOfRange(WaterSimulation sim, int size)
        {
            if (_raised.Count == 0) return;

            _toDrop.Clear();
            foreach (var entry in _raised)
            {
                // HashSet なので 1 件あたり定数時間（_inRange の doc）。
                if (!_inRange.Contains(entry.Key)) _toDrop.Add(entry.Key);
            }

            for (int i = 0; i < _toDrop.Count; i++)
            {
                ushort handle = _toDrop[i];
                TyphoonFloodedSource s;
                if (_raised.TryGetValue(handle, out s)) RestoreOne(sim, handle, size, s.Original);
                _raised.Remove(handle);
            }
        }

        /// <summary>
        /// 水源 1 個の <c>m_target</c> を書く。**ロックはここでしか取らない。**
        ///
        /// <c>try</c> の中には**例外を投げうる処理を置かない**（ログも診断も外）。
        /// <c>finally</c> の <c>UnlockWaterSource</c> が唯一の解放経路である（クラス doc）。
        /// </summary>
        /// <param name="known">台帳に載っているか。false なら元の値をここで読む。</param>
        /// <param name="knownOriginal">台帳に載っている「④が触る前の値」。</param>
        /// <param name="original">
        /// 「④が触る前の値」。<paramref name="known"/> が true なら
        /// <paramref name="knownOriginal"/> がそのまま返る。**持ち上げた値は返らない。**
        /// </param>
        /// <param name="actual">実際に書いた値。</param>
        /// <returns>書けたか（<c>TYPE_NATURAL</c> でなければ false）。</returns>
        private static bool WriteTarget(WaterSimulation sim, ushort handle, int size,
                                        bool known, ushort knownOriginal, float rise,
                                        out ushort original, out ushort actual)
        {
            original = knownOriginal;
            actual = 0;

            // ★ ハンドルを先に検証する。LockWaterSource は範囲を検査せず、
            //   モニタを取った**後**に m_buffer[handle - 1] を読むので、
            //   範囲外だと Monitor.Exit に到達しないまま抜ける＝恒久デッドロック。
            if (!IsValidHandle(handle, size)) return false;

            ushort seen = knownOriginal;
            bool ok = false;

            // ★ LockWaterSource は Monitor を取ったまま返る（§D-4、IL_0005-002E に
            //   Monitor.Exit が無い）。UnlockWaterSource が唯一の解放経路。
            //   **必ず try/finally で対にする。** 落とすと水シミュ専用スレッドが
            //   スピンロックで固まり、ゲームが無反応になる。
            WaterSource src = sim.LockWaterSource(handle);
            try
            {
                if (src.m_type == TypeNatural)
                {
                    if (!known) seen = src.m_target;
                    src.m_target = FloodTarget.RaisedTarget(seen, rise);
                    ok = true;
                }
            }
            finally
            {
                // ★ return しても例外が出ても、必ずここを通る。
                sim.UnlockWaterSource(handle, src);
            }

            if (!ok) return false;

            original = seen;
            actual = src.m_target;
            return true;
        }

        /// <summary>
        /// 水源 1 個を元の値へ戻す。<see cref="WriteTarget"/> と同じロックの形。
        /// **戻せなくても例外を外へ出さない** —— 復元は 3 箇所から呼ばれるので、
        /// 1 個の失敗で残りの復元を止めてはいけない。
        /// </summary>
        private static void RestoreOne(WaterSimulation sim, ushort handle, int size,
                                       ushort original)
        {
            if (!IsValidHandle(handle, size)) return;

            try
            {
                WaterSource src = sim.LockWaterSource(handle);
                try
                {
                    // 型が変わっていたら（解放・再利用）触らない。
                    // **他人のものになった水源に④の値を書かない。**
                    if (src.m_type == TypeNatural) src.m_target = original;
                }
                finally
                {
                    sim.UnlockWaterSource(handle, src);
                }
            }
            catch (System.Exception e)
            {
                // ログはロックの外（ここは finally の外側）。
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                         "could not restore water source #" + handle + ": " + e.GetType().Name);
            }
        }

        /// <summary>
        /// ハンドルが <c>m_buffer[handle - 1]</c> として有効か。**1 基点**なので 0 は無効。
        /// <c>LockWaterSource</c> を呼ぶ前に必ず通す（クラス doc の危険）。
        /// </summary>
        private static bool IsValidHandle(ushort handle, int size)
        {
            return handle >= 1 && handle <= size;
        }

        /// <summary>
        /// **持ち上げたぶんを全部元に戻す。冪等**（2 回呼ばれても、既に空なら何もしない）。
        ///
        /// 3 箇所から呼ばれる（クラス doc の表）ので、重なるのが普通である。
        /// </summary>
        public static void RestoreAll()
        {
            if (_raised.Count == 0)
            {
                if (_state == TyphoonFloodState.Raised) _state = TyphoonFloodState.Restored;
                return;
            }

            var sim = WaterSim();
            if (sim == null)
            {
                // 水シミュに到達できない（都市が既に落ちている等）。
                // **台帳は捨てる** —— 残しても次の都市で無関係な川を書き換えるだけ。
                //
                // ★ 状態を先に、ログを後に。逆にすると、ログ側が投げたときに
                //   台帳が残る ——「前の都市のハンドルを次の都市で復元しに行く」という、
                //   この機能でいちばん避けたい状態そのものになる。
                int lost = _raised.Count;
                _raised.Clear();
                _state = TyphoonFloodState.Restored;

                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                         "the water simulation is gone; " + lost
                         + " raised water source(s) could not be restored");
                return;
            }

            int size = SizeOf(sim);
            int restored = 0;

            foreach (var entry in _raised)
            {
                RestoreOne(sim, entry.Key, size, entry.Value.Original);
                restored++;
            }

            _raised.Clear();
            _state = TyphoonFloodState.Restored;

            // ログはロックを 1 本も握っていない場所で。
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodRestore",
                     "restored " + restored + " water source target(s) to their original level");
        }

        /// <summary>
        /// **保存の直前に呼ぶ**（<c>DisasterPlusSerialization.OnSaveData</c> の先頭）。
        /// 今持ち上げているぶんを元に戻し、その内容を返す。
        /// 持ち上げていなければ <c>null</c>。
        ///
        /// 呼び出し側は返り値を <c>SimulationManager.AddAction</c> 越しに
        /// <see cref="ReapplyAfterSave"/> へ渡すこと。**ここで（あるいは
        /// <c>finally</c> で）すぐ戻し直すと、バニラが配列を書く前に持ち上げ直す
        /// ことになり漏れが再発する**（クラス doc §8.5）。
        /// </summary>
        public static List<TyphoonFloodedSource> SnapshotAndRestoreForSave()
        {
            if (_raised.Count == 0) return null;

            var copy = new List<TyphoonFloodedSource>(_raised.Count);
            foreach (var entry in _raised) copy.Add(entry.Value);

            RestoreAll();

            Log.Info("typhoon flooding: lowered " + copy.Count
                     + " water source(s) to their original level before saving");
            return copy;
        }

        /// <summary>
        /// **保存が終わったあとに sim スレッドで呼ぶ**
        /// （<c>SimulationManager.AddAction</c> の契約）。台風がまだ動いていれば
        /// 持ち上げ直す。終わっていれば何もしない。
        ///
        /// 呼ばれるまでの間に走査が同じ水源を掴み直していることがある。その場合は
        /// **台帳側を正とする** —— あちらは今の台風の位置で計算した値で、
        /// こちらは保存時点の古い値だからである。
        /// </summary>
        public static void ReapplyAfterSave(List<TyphoonFloodedSource> raised)
        {
            if (raised == null || raised.Count == 0) return;

            try
            {
                if (!TyphoonController.Active) return;

                var sim = WaterSim();
                if (sim == null) return;

                int size = SizeOf(sim);
                int reapplied = 0;

                for (int i = 0; i < raised.Count; i++)
                {
                    var s = raised[i];
                    if (_raised.ContainsKey(s.Handle)) continue;   // 台帳が既に掴み直した

                    if (WriteRaw(sim, s.Handle, s.Raised, size))
                    {
                        _raised[s.Handle] = s;
                        reapplied++;
                    }
                }

                if (reapplied > 0)
                {
                    _state = TyphoonFloodState.Raised;
                    Log.Info("typhoon flooding: re-raised " + reapplied
                             + " water source(s) after the save");
                }
            }
            catch (System.Exception e)
            {
                // 戻し直せなくても**川は元の高さのまま**なので害は無い。
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFloodReapply",
                         "could not re-raise water sources after the save: " + e.GetType().Name);
            }
        }

        /// <summary>
        /// <c>m_target</c> に値をそのまま書く（再適用専用）。
        /// <see cref="WriteTarget"/> と同じロックの形。
        ///
        /// ハンドルの検証は呼び出し側でも済ませているが、**ここでも通す** ——
        /// 範囲外のハンドルで <c>LockWaterSource</c> を呼ぶと Monitor を取った直後に
        /// 例外が出て <c>Monitor.Exit</c> に到達せず、恒久デッドロックになる
        /// （クラス doc）。この 1 行を二重に置く価値がある種類の失敗である。
        /// </summary>
        private static bool WriteRaw(WaterSimulation sim, ushort handle, ushort target,
                                     int size)
        {
            if (!IsValidHandle(handle, size)) return false;

            bool ok = false;

            WaterSource src = sim.LockWaterSource(handle);
            try
            {
                if (src.m_type == TypeNatural)
                {
                    src.m_target = target;
                    ok = true;
                }
            }
            finally
            {
                sim.UnlockWaterSource(handle, src);
            }

            return ok;
        }

        /// <summary>
        /// <c>WaterSimulation</c> への到達経路。**本タスクで IL 実測した**:
        /// <c>TerrainManager.WaterSimulation</c> は **public なインスタンスプロパティ**
        /// （裏は private フィールド <c>m_waterSimulation</c>）。
        ///
        /// <c>Singleton&lt;T&gt;.instance</c> は <c>sInstance</c> が null のとき
        /// <c>FindObjectOfType</c> と <c>new GameObject</c> を走らせる main スレッド専用
        /// API なので、<c>exists</c> で先に確認する。
        /// </summary>
        private static WaterSimulation WaterSim()
        {
            if (!Singleton<TerrainManager>.exists) return null;

            var tm = Singleton<TerrainManager>.instance;
            return tm == null ? null : tm.WaterSimulation;
        }

        /// <summary>
        /// 有効なハンドルの上限。<see cref="Sweep"/> と同じ順序で読む
        /// （<c>m_buffer</c> が先、<c>m_size</c> が後、短いほうを採る）。
        /// </summary>
        private static int SizeOf(WaterSimulation sim)
        {
            var sources = sim.m_waterSources;
            if (sources == null) return 0;

            var buffer = sources.m_buffer;
            if (buffer == null) return 0;

            int size = sources.m_size;
            return size > buffer.Length ? buffer.Length : size;
        }

        /// <summary>
        /// **持ち上げた数が 0 のときも毎回出す。** 「水源が無い」「範囲に入っていない」
        /// 「設定で切っている」「到達経路が壊れている」が画面上どれも同じ顔（川が
        /// 増水しない）になるので、切り分けはここでしかできない。
        ///
        /// <c>Log.Diag</c> は同一キーで間引かれるが**引数の文字列連結は毎回走る**ので
        /// <c>DiagEnabled</c> で先に落とす。
        /// </summary>
        private static void WriteDiag(int natural, int applied, float peak, float gale)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFlood",
                "flood: naturalSources=" + natural
                + " raised=" + _raised.Count
                + " appliedThisPass=" + applied
                + " peakRise=" + peak.ToString("F2") + " m"
                + " galeRadius=" + gale.ToString("F0")
                + " state=" + _state
                + (natural == 0
                    ? "  (this map has no natural water sources; nothing is wrong)"
                    : ""));
        }
    }
}
