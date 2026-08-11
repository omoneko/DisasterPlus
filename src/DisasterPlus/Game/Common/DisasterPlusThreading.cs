using ICities;

namespace DisasterPlus.Game
{
    public class DisasterPlusThreading : ThreadingExtensionBase
    {
        /// <summary>
        /// sim スレッド。建物・車両・災害バッファの生成と変更はここからのみ行う。
        /// main スレッド（OnUpdate）からこれらを触ると、スタックトレースの無い
        /// IndexOutOfRangeException が後から出て、自分の try/catch にも掛からない。
        /// </summary>
        public override void OnAfterSimulationTick()
        {
            FeatureHost.SimulationTick();
        }

        /// <summary>main スレッド。Unity オブジェクト・描画・UI 専用。</summary>
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            FeatureHost.MainThreadUpdate();
        }
    }
}
