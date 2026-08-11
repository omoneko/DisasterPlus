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

        public static IList<IDisasterFeature> Features { get { return _features; } }

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
        }

        public static void SimulationTick()
        {
            uint frame = SimulationManager.instance.m_currentFrameIndex;

            // ゲーム内時間の経過（分）を出す。ポーズ中はフレームが進まないので 0 になる。
            // CS のシミュレーションは 1 日 = 262144 フレーム。1 分 = 262144 / 1440 フレーム。
            const float framesPerMinute = 262144f / 1440f;

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
