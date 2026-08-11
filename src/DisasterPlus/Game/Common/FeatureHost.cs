using System.Collections.Generic;
using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>機能の登録と、例外を 1 機能に閉じ込めたディスパッチ。</summary>
    public static class FeatureHost
    {
        private static readonly List<IDisasterFeature> _features = new List<IDisasterFeature>();
        private static uint _lastFrame;
        private static bool _hasLastFrame;

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
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnLevelLoaded", e); }
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
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnSimulationTick", e); }
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
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnMainThreadUpdate", e); }
            }
        }

        public static void LevelUnloading()
        {
            // 解体を始める前に tick を止める。
            _levelReady = false;

            for (int i = 0; i < _features.Count; i++)
            {
                try { _features[i].OnLevelUnloading(); }
                catch (System.Exception e) { Log.Error(_features[i].Name + ".OnLevelUnloading", e); }
            }

            _hasLastFrame = false;
            IntensityUnlock.Reset();
            Log.Reset();
        }
    }
}
