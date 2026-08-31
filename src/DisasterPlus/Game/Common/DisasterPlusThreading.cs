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
            // ★★ **予約も消す。**（2026-08-31、第 2 回検証）これを忘れると、
            //    まだ走っていた sim tick が予約済みの津波を立ててしまい、
            //    そのあと解放する手立てが無くなる。
            try { TsunamiChain.Reset(); }
            catch (System.Exception e) { Log.Error("TsunamiChain.Reset on release", e); }

            // ★ 片道の錠は置かない（TsunamiRing の ★★: OnReleased は
            //   メインメニューへ戻るたびにも来る）。予約を消したうえで解放する。
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
