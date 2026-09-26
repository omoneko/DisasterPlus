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

            // OnLoadData (DisasterPlusSerialization) runs inside LoadSimulationData and has
            // already finished before this OnLevelLoaded. But Clear() has only just run, so
            // the restore must be applied after Clear(), here. Reverse the order and Clear()
            // wipes out the restore.
            var pendingRestore = DisasterPlusSerialization.TakePendingRestore();
            if (pendingRestore != null)
            {
                FireWhirlRegistry.RestoreFromSave(pendingRestore);
                Log.Info("restored " + pendingRestore.Count + " fire whirls");
            }

            if (FeatureHost.Features.Count == 0)
            {
                FeatureHost.Register(new FireWhirlFeature());
                FeatureHost.Register(new ForecastFeature());
                FeatureHost.Register(new EarthquakeFeature());
                FeatureHost.Register(new TyphoonFeature());
                FeatureHost.Register(new VolcanoFeature());
            }

            FeatureHost.LevelLoaded();
            Log.Info("level loaded; features=" + FeatureHost.Features.Count);

            // It looks at Harmony's patches and the resolved prefabs, so run it after the
            // features have finished initialising.
            Assumptions.Run();

            // The overlay is not created here. Pinning it to the setting's value at load time
            // would make it a one-way setting where switching it ON later does nothing.
            // FeatureHost.MainThreadUpdate() keeps it following the current value every frame.
        }

        public override void OnLevelUnloading()
        {
            // The sim thread has already stopped during unloading, so it is fine to clear
            // directly from here (safe in this situation only).
            FeatureHost.LevelUnloading();
            FireWhirlRegistry.Clear();
            Assumptions.Reset();
            DiagnosticOverlay.Destroy();
            // OnLoadData itself always clears at the start of the next load, but drop it here
            // too as a second line of defence. Never carry pending restore data across cities.
            DisasterPlusSerialization.TakePendingRestore();
            base.OnLevelUnloading();
        }
    }
}
