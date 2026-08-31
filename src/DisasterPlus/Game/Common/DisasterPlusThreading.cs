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

        /// <summary>
        /// **MOD が外されたときに呼ばれる。**
        ///
        /// ★★ ここを実装していないと、<b>津波の最中に MOD を無効化された都市に
        ///   水源が残る</b>（2026-08-31、相互検証）。拡張が外されると
        ///   <c>OnAfterSimulationTick</c> も <c>OnSaveData</c> も
        ///   <c>OnLevelUnloading</c> も二度と来ないので、
        ///   自力で終わる道が無くなる —— 次のオートセーブで焼き付き、
        ///   MOD を消しても消えない湧き水になる。
        ///
        /// ★ 無関係な MOD の切り替えでもここは呼ばれる。そのときの損は
        ///   「走っていた波が早く終わる」だけなので、迷わず解放してよい。
        /// </summary>
        public override void OnReleased()
        {
            try { TsunamiRing.Reset(); }
            catch (System.Exception e) { Log.Error("TsunamiRing.Reset on release", e); }

            base.OnReleased();
        }

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
