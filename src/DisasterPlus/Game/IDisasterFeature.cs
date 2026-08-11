namespace DisasterPlus.Game
{
    /// <summary>
    /// 1 つの災害機能。①天気予報・②地震・④台風・⑤火山も同じ形で足せるようにする。
    /// Mod.cs はこのリストを回すだけで、機能追加で既存コードを変更しなくて済む。
    /// </summary>
    public interface IDisasterFeature
    {
        string Name { get; }

        /// <summary>都市のロード完了時。main スレッド。</summary>
        void OnLevelLoaded();

        /// <summary>シミュレーション tick 後。sim スレッド。バッファの生成・変更はここから。</summary>
        /// <param name="deltaMinutes">前回からのゲーム内経過（分）。ポーズ中は 0。</param>
        void OnSimulationTick(uint frameIndex, float deltaMinutes);

        /// <summary>毎フレーム。main スレッド。Unity オブジェクトはここからのみ触る。</summary>
        void OnMainThreadUpdate();

        /// <summary>都市のアンロード時。全セッション状態と静的キャッシュをここで捨てる。</summary>
        void OnLevelUnloading();
    }
}
