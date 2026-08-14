using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラの災害ハザード情報ビューへの切替。
    ///
    /// ヒートマップは自前で描かない。IL 実測でバニラに完全な実装があることが分かっている
    /// （ThunderStormAI.UpdateHazardMap 237命令 / TornadoAI 335命令、
    /// InfoMode.DisasterHazard、SubInfoMode.LightningHazard / TornadoHazard 等）。
    /// SetCurrentMode は public なので、ここは薄いラッパーで足りる。
    ///
    /// IL 実測（Task 4 Step 1）:
    ///   Void InfoManager.SetCurrentMode(InfoMode mode, SubInfoMode subMode)  -- public, non-static。
    ///   InfoManager.InfoMode.None は実在する（enum の先頭要素、値 0）。
    ///   InfoManager.SubInfoMode.Default も実在する。
    /// どちらもブリーフのスケッチどおりで、シグネチャの相違は無かった。
    ///
    /// **main スレッドから呼ぶこと**（InfoManager は UI/描画向けの状態で、CS1 の他の
    /// InfoMode 切替コードと同じく main スレッド専用として扱う）。
    /// </summary>
    public static class InfoModeSwitch
    {
        public static bool IsShowingHazard
        {
            get
            {
                try
                {
                    if (!Singleton<InfoManager>.exists) return false;
                    return Singleton<InfoManager>.instance.CurrentMode
                           == InfoManager.InfoMode.DisasterHazard;
                }
                catch { return false; }
            }
        }

        public static void ShowHazard(InfoManager.SubInfoMode subMode)
        {
            try
            {
                if (!Singleton<InfoManager>.exists) { Log.Warn("InfoManager not ready"); return; }
                Singleton<InfoManager>.instance.SetCurrentMode(
                    InfoManager.InfoMode.DisasterHazard, subMode);
            }
            catch (System.Exception e)
            {
                Log.Error("failed to switch to the disaster hazard info view", e);
            }
        }

        /// <summary>通常表示に戻す。</summary>
        public static void Clear()
        {
            try
            {
                if (!Singleton<InfoManager>.exists) return;
                Singleton<InfoManager>.instance.SetCurrentMode(
                    InfoManager.InfoMode.None, InfoManager.SubInfoMode.Default);
            }
            catch (System.Exception e)
            {
                Log.Error("failed to clear the info view", e);
            }
        }
    }
}
