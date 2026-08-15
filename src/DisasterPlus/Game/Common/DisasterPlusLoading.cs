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

            // OnLoadData（DisasterPlusSerialization）は LoadSimulationData の中で走り、
            // この OnLevelLoaded より前に完了している。だが Clear() は今しがた実行したばかりなので、
            // 復元の適用は必ず Clear() の後、ここで行う。順序を逆にすると Clear() が復元を消す。
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
            }

            FeatureHost.LevelLoaded();
            Log.Info("level loaded; features=" + FeatureHost.Features.Count);

            // Harmony の適用と prefab の解決を見るので、機能の初期化が終わってから走らせる。
            Assumptions.Run();

            // オーバーレイはここでは作らない。ロード時の設定値で固定すると、
            // 途中で ON にしても何も起きない片道の設定になる。
            // FeatureHost.MainThreadUpdate() が毎フレーム現在値に追従させる。
        }

        public override void OnLevelUnloading()
        {
            // アンロード中は sim スレッドが既に停止しているので、
            // ここから直接クリアしてよい（この場面に限り安全）。
            FeatureHost.LevelUnloading();
            FireWhirlRegistry.Clear();
            Assumptions.Reset();
            DiagnosticOverlay.Destroy();
            // OnLoadData 自身も次回ロードの先頭で必ずクリアするが、二重の安全策として
            // ここでも捨てる。都市をまたいで保留中の復元データを持ち越さない。
            DisasterPlusSerialization.TakePendingRestore();
            base.OnLevelUnloading();
        }
    }
}
