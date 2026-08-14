using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>機能の登録と、例外を 1 機能に閉じ込めたディスパッチ。</summary>
    public static class FeatureHost
    {
        private static readonly List<IDisasterFeature> _features = new List<IDisasterFeature>();
        private static uint _lastFrame;
        private static bool _hasLastFrame;

        // 機能ごとの例外記録。レベルアンロードでリセットする。
        // NoteFailure は main スレッド（OnMainThreadUpdate 等）からも sim スレッド
        // （OnSimulationTick）からも呼ばれ、BuildReport は sim スレッドで読む。
        // DiagnosticsHub の lock とは別に、この 2 つの Dictionary 専用の gate で守る
        // （FeatureHost はここで DiagnosticsHub にも触るため、lock の順序問題を避けて gate を分ける）。
        private static readonly object _errorGate = new object();
        private static readonly Dictionary<string, int> _errorCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _lastErrors = new Dictionary<string, string>();

        // 例外を伴わない「振る舞いの異常」の記録。例外が出ないまま静かに壊れるのが
        // このプロジェクトで実際に 3 回起きた失敗の形なので、機能側から明示的に
        // Degraded を申告できる口を用意する。_errorCounts と同じ gate で守り、
        // レベルアンロードで一緒に消す。
        private static readonly Dictionary<string, string> _degradeNotes = new Dictionary<string, string>();

        /// <summary>
        /// LevelLoaded() を通過済みか。
        ///
        /// _features は都市をまたいで生き残る static なのに、sim tick の入口
        /// （SimulationManager.SimulationStep）は LoadingManager.m_simulationDataLoaded を
        /// 見ているだけで、これは OnLevelLoaded を起こすコルーチンより先に立つ。
        /// このフラグが無いと、2 つ目の都市をロードした直後の数秒間、前の都市の
        /// スキャナカーソル・prefab キャッシュを抱えたまま機能が回り続ける。
        /// アセット／マップエディタ（DisasterPlusLoading.OnLevelLoaded が早期 return する）
        /// でも同じ経路で回ってしまうので、そこも同時に塞ぐ。
        /// </summary>
        private static bool _levelReady;

        public static IList<IDisasterFeature> Features { get { return _features; } }

        /// <summary>
        /// 1 ゲーム内分あたりの sim フレーム数。
        ///
        /// IL 実測: SimulationManager.DAYTIME_FRAMES は 65536（public static UInt32）。
        /// 1 日 = 1440 分なので 1 分 = 65536 / 1440 ≒ 45.51 フレーム。
        /// 定数を直書きせず毎回この値から割ることで、ゲーム更新で変わっても黙ってずれない。
        /// </summary>
        public static float FramesPerMinute
        {
            get { return SimulationManager.DAYTIME_FRAMES / 1440f; }
        }

        public static void Register(IDisasterFeature feature)
        {
            if (feature != null && !_features.Contains(feature)) _features.Add(feature);
        }

        public static void LevelLoaded()
        {
            _hasLastFrame = false;
            IntensityUnlock.Apply();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelLoaded(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnLevelLoaded", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            // 全機能のリセットが済んでから初めて tick を許可する。
            _levelReady = true;
        }

        public static void SimulationTick()
        {
            if (!_levelReady) return;

            uint frame = SimulationManager.instance.m_currentFrameIndex;

            // ゲーム内時間の経過（分）を出す。ポーズ中はフレームが進まないので 0 になる。
            float framesPerMinute = FramesPerMinute;

            float deltaMinutes = 0f;
            if (_hasLastFrame && frame > _lastFrame)
            {
                deltaMinutes = (frame - _lastFrame) / framesPerMinute;
            }
            _lastFrame = frame;
            _hasLastFrame = true;

            // main スレッド（Ctrl+ホットキー）からの依頼を、この pause guard より前で拾う。
            // OnAfterSimulationTick はポーズ中も呼ばれ続ける（止まるのはゲーム内時間の
            // 進みだけで、sim スレッド自体は止まらない）。この guard が守っているのは
            // 「経過 0 分で機能の状態を進めない」ことであって、スレッドの所有権とは
            // 無関係。ダンプはどの機能の状態も変更しないので、guard より前で
            // BuildReport() してもここが守ろうとしているものを壊さない。
            //
            // ここで ConsumeRequest() を呼ぶのは 1 回だけ（このメソッド内で 2 度呼ぶと
            // 依頼を取りこぼす側が out-of-sync になる）。ポーズ中／非ポーズのどちらの
            // 経路でも、この 1 個の dumpRequested を CollectAndPublish に渡す。
            bool dumpRequested = DiagnosticDump.ConsumeRequest();

            if (deltaMinutes <= 0f)
            {
                // ポーズ中でも収集はする。OnSimulationTick は呼ばない
                // （＝機能の状態は進めない）ので、pause guard 本来の目的は保たれる。
                //
                // 収集をダンプ依頼のときだけにしてはいけない。プレイヤーが
                // 手を止めて中を見るためにポーズしてオーバーレイを開く、というのが
                // 最も自然な使い方で、そこで箱が空のまま更新されないのでは
                // オーバーレイの意味が無い。
                CollectAndPublish(dumpRequested);
                return;   // ポーズ中は機能の状態を進めない。負にも絶対にしない。
            }

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnSimulationTick(frame, deltaMinutes); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnSimulationTick", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            CollectAndPublish(dumpRequested);
        }

        /// <summary>
        /// 診断を 1 回集めて公開する。sim スレッド専用。
        /// BuildReport() は WriteDiagnostics 経由で機能の内部状態を読むだけで
        /// 書き換えないので、OnSimulationTick を呼ばないポーズ経路から呼んでも安全。
        ///
        /// オーバーレイが閉じていてダンプ依頼も無ければ何もしない（収集コストはゼロ）。
        /// </summary>
        private static void CollectAndPublish(bool dumpRequested)
        {
            if (!DiagnosticsHub.CollectionEnabled && !dumpRequested) return;

            try
            {
                var report = BuildReport();
                DiagnosticsHub.Publish(report);
                // ファイル I/O はここでは行わない。main スレッドの
                // DiagnosticDump.FlushPendingWrite() に不変レポートを渡すだけ。
                if (dumpRequested) DiagnosticDump.SubmitReport(report);
            }
            catch (System.Exception e) { Log.Error("diagnostics collection failed", e); }
        }

        public static void MainThreadUpdate()
        {
            if (!_levelReady) return;

            // オーバーレイの生成・破棄は「ロード時の設定値」ではなく「現在の設定値」に従う。
            // ロード時に一度だけ見ていた頃は、途中で ON にしても何も起きず、しかも
            // ダンプの書き出しがオーバーレイの Update に相乗りしていたため
            // Ctrl+ホットキーまで無反応になっていた。
            SyncOverlay();

            // sim スレッドが組み立て終えたダンプをここ（main スレッド）で書き出す。
            // オーバーレイの有無に依存させない（依存させると上記の事故が戻る）。
            DiagnosticDump.FlushPendingWrite();

            // 災害パネルはロード直後にはまだ無いことがある。見つかるまで間隔をあけて再試行する。
            IntensityUnlock.Tick();

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnMainThreadUpdate(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnMainThreadUpdate", e);
                    NoteFailure(_features[i].Name, e);
                }
            }
        }

        public static void LevelUnloading()
        {
            // 解体を始める前に tick を止める。
            _levelReady = false;

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelUnloading(); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnLevelUnloading", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            _hasLastFrame = false;
            IntensityUnlock.Reset();
            Log.Reset();

            lock (_errorGate)
            {
                _errorCounts.Clear();
                _lastErrors.Clear();
                _degradeNotes.Clear();
            }
            DiagnosticsHub.Clear();
            // テアダウン中に立ったダンプ依頼・組み立て済みレポートを次の都市へ持ち越さない。
            DiagnosticDump.Reset();
        }

        /// <summary>既存の catch 節から呼ぶ。呼び出しは止めない。main / sim どちらのスレッドからも呼ばれる。</summary>
        private static void NoteFailure(string featureName, System.Exception e)
        {
            lock (_errorGate)
            {
                int n;
                _errorCounts.TryGetValue(featureName, out n);
                _errorCounts[featureName] = n + 1;
                _lastErrors[featureName] = e == null ? "unknown" : e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>
        /// 例外は出ていないが振る舞いがおかしいことを機能が自己申告する口。
        /// 同じ機能から複数回呼ぶと直近の理由で上書きされる。
        /// レベルアンロードまで残る（＝一度立ったバッジは下がらない）。
        /// main / sim どちらのスレッドから呼んでもよい。
        /// </summary>
        public static void NoteDegraded(string featureName, string reason)
        {
            if (string.IsNullOrEmpty(featureName)) return;
            lock (_errorGate)
            {
                _degradeNotes[featureName] = string.IsNullOrEmpty(reason) ? "degraded" : reason;
            }
        }

        /// <summary>
        /// 全機能の診断を集めて 1 つの不変レポートにする。sim スレッドから呼ぶこと。
        /// </summary>
        public static DiagnosticReport BuildReport()
        {
            var header = new List<DiagnosticLine>();
            // バージョンは先頭に置く。「ゲームが下で変わった」を前提にした基盤なのに
            // どのゲームビルドの記録なのか分からないダンプは、いちばん重要な欄が
            // 抜けている（設計書 8）。オーバーレイも同じ header を描くので両方に出る。
            header.Add(new DiagnosticLine(0, "Mod", ModVersion()));
            header.Add(new DiagnosticLine(0, "Game", GameVersion()));
            header.Add(new DiagnosticLine(0, "DLC:ND",
                ModCompat.NaturalDisastersOwned ? "owned" : "MISSING"));
            header.Add(new DiagnosticLine(0, "NDR",
                ModCompat.NdrPresent ? "detected" : "absent"));
            header.Add(new DiagnosticLine(0, "Harmony",
                HarmonyBootstrap.Installed ? "patched" : "NOT PATCHED"));
            header.Add(new DiagnosticLine(0, "Level", _levelReady ? "ready" : "not ready"));

            var sections = new List<DiagnosticSection>();
            var builder = new DiagnosticBuilder();

            // _errorCounts / _lastErrors は先に丸ごとスナップショットしてから lock を離れる。
            // f.WriteDiagnostics は任意の機能コードで、FeatureHost へコールバックする可能性が
            // ゼロではない（例えば Log.Diag 経由の何か）。lock を持ったまま呼ぶと、そのコード経路が
            // 同じ _errorGate を取ろうとした瞬間にデッドロックしうるので、ここでは絶対に避ける。
            // 名前も lock の外で先に確保する。IDisasterFeature.Name は契約上「任意の
            // 機能コード」であり（②〜⑤はこのファイルを手本に書かれる）、_errorGate を
            // 持ったまま呼べば同じ規律違反になる。プロパティが投げても診断全体を
            // 落とさないよう、ここで個別に受け止めておく。
            var names = new string[_features.Count];
            for (int i = 0; i < _features.Count; i++)
            {
                try { names[i] = _features[i].Name; }
                catch { names[i] = "feature#" + i; }
                if (string.IsNullOrEmpty(names[i])) names[i] = "feature#" + i;
            }

            var healths = new FeatureHealth[_features.Count];
            var notes = new string[_features.Count];
            lock (_errorGate)
            {
                for (int i = 0; i < _features.Count; i++)
                {
                    healths[i] = FeatureHealth.Healthy;
                    notes[i] = "";

                    int errors;
                    if (_errorCounts.TryGetValue(names[i], out errors) && errors > 0)
                    {
                        healths[i] = FeatureHealth.Degraded;
                        string last;
                        _lastErrors.TryGetValue(names[i], out last);
                        notes[i] = errors + " errors, last: " + last;
                    }

                    // 例外を伴わない自己申告（NoteDegraded）。例外記録と両方あれば併記する。
                    string degraded;
                    if (_degradeNotes.TryGetValue(names[i], out degraded))
                    {
                        healths[i] = FeatureHealth.Degraded;
                        notes[i] = notes[i].Length == 0 ? degraded : notes[i] + "; " + degraded;
                    }
                }
            }

            for (int i = 0; i < _features.Count; i++)
            {
                var health = healths[i];
                var note = notes[i];

                try
                {
                    _features[i].WriteDiagnostics(builder);
                }
                catch (System.Exception e)
                {
                    // 診断の失敗で他機能の診断まで巻き込まない。
                    // バッジが本文と矛盾しないよう、この機能の health も Degraded にする。
                    builder.Line(1, "diagnostics failed", e.GetType().Name);
                    health = FeatureHealth.Degraded;
                }

                sections.Add(new DiagnosticSection(names[i], health, note, builder.Take()));
            }

            return new DiagnosticReport(header, Assumptions.LastResults, sections);
        }

        /// <summary>MOD 版。アセンブリのバージョンをそのまま出す（AssemblyInfo.cs が唯一の情報源）。</summary>
        private static string ModVersion()
        {
            try
            {
                var v = typeof(FeatureHost).Assembly.GetName().Version;
                return v == null ? "unknown" : v.ToString();
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// ゲーム版。
        ///
        /// IL 実測: BuildConfig.applicationVersion は public static な String プロパティで、
        /// 中身は BuildConfig.VersionToString(APPLICATION_VERSION, false) を返すだけ
        /// （フル版が要るときは applicationVersionFull）。static なのでインスタンスも
        /// シングルトンも要らず、どのタイミングでも読める。
        /// </summary>
        private static string GameVersion()
        {
            try { return BuildConfig.applicationVersion; }
            catch { return "unknown"; }
        }

        /// <summary>
        /// 設定に合わせてオーバーレイを生成・破棄する。main スレッド専用。
        /// Create / Destroy はどちらも「既にその状態なら即 return」なので毎フレーム呼んでよい。
        /// </summary>
        private static void SyncOverlay()
        {
            try
            {
                ModSettings.Ensure();
                if (ModSettings.OverlayEnabled.value) DiagnosticOverlay.Create();
                else DiagnosticOverlay.Destroy();
            }
            catch (System.Exception e) { Log.Error("overlay sync failed", e); }
        }
    }
}
