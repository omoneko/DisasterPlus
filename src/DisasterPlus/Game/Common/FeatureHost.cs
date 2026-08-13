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

            if (deltaMinutes <= 0f) return;   // ポーズ中は何もしない。負にも絶対にしない。

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnSimulationTick(frame, deltaMinutes); }
                catch (System.Exception e)
                {
                    Log.Error(_features[i].Name + ".OnSimulationTick", e);
                    NoteFailure(_features[i].Name, e);
                }
            }

            // オーバーレイが閉じていてダンプ要求も無ければ何もしない。
            if (DiagnosticsHub.CollectionEnabled)
            {
                try { DiagnosticsHub.Publish(BuildReport()); }
                catch (System.Exception e) { Log.Error("diagnostics collection failed", e); }
            }
        }

        public static void MainThreadUpdate()
        {
            if (!_levelReady) return;

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
            }
            DiagnosticsHub.Clear();
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
        /// 全機能の診断を集めて 1 つの不変レポートにする。sim スレッドから呼ぶこと。
        /// </summary>
        public static DiagnosticReport BuildReport()
        {
            var header = new List<DiagnosticLine>();
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
            var healths = new FeatureHealth[_features.Count];
            var notes = new string[_features.Count];
            lock (_errorGate)
            {
                for (int i = 0; i < _features.Count; i++)
                {
                    var f = _features[i];
                    healths[i] = FeatureHealth.Healthy;
                    notes[i] = "";

                    int errors;
                    if (_errorCounts.TryGetValue(f.Name, out errors) && errors > 0)
                    {
                        healths[i] = FeatureHealth.Degraded;
                        string last;
                        _lastErrors.TryGetValue(f.Name, out last);
                        notes[i] = errors + " errors, last: " + last;
                    }
                }
            }

            for (int i = 0; i < _features.Count; i++)
            {
                var f = _features[i];
                var health = healths[i];
                var note = notes[i];

                try
                {
                    f.WriteDiagnostics(builder);
                }
                catch (System.Exception e)
                {
                    // 診断の失敗で他機能の診断まで巻き込まない。
                    // バッジが本文と矛盾しないよう、この機能の health も Degraded にする。
                    builder.Line(1, "diagnostics failed", e.GetType().Name);
                    health = FeatureHealth.Degraded;
                }

                sections.Add(new DiagnosticSection(f.Name, health, note, builder.Take()));
            }

            // Task 4 で Assumptions.LastResults に差し替える
            return new DiagnosticReport(header, null, sections);
        }
    }
}
