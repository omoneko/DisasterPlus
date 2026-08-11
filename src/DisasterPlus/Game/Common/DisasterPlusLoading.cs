using ICities;

namespace DisasterPlus.Game
{
    public class DisasterPlusLoading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame &&
                mode != LoadMode.NewGameFromScenario) return;

            LocaleLoader.Apply();
            ModSettings.Ensure();
            FireWhirlRegistry.Clear();

            if (FeatureHost.Features.Count == 0)
            {
                FeatureHost.Register(new FireWhirlFeature());
            }

            FeatureHost.LevelLoaded();
            Log.Info("level loaded; features=" + FeatureHost.Features.Count);
        }

        public override void OnLevelUnloading()
        {
            // アンロード中は sim スレッドが既に停止しているので、
            // ここから直接クリアしてよい（この場面に限り安全）。
            FeatureHost.LevelUnloading();
            FireWhirlRegistry.Clear();
            base.OnLevelUnloading();
        }
    }
}
