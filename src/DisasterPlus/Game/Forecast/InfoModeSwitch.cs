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

        /// <summary>
        /// 今まさに DisasterHazard ビューが表示されていて、かつ表示中のサブモードが
        /// <paramref name="subMode"/> と一致しているか。
        ///
        /// なぜ要るか（IL 実測で確定、Task 4 フォローアップ）: DisasterManager.UpdateTexture
        /// は各災害 AI の GetHazardSubMode を見て、単一の m_hazardAmount 配列へ
        /// 「今表示中のサブモードのハザード」だけを書き込む。つまりグリッドは常に
        /// 1 種類のサブモード分の値しか保持していない。表示中のサブモードと一致しない
        /// 状態でグリッドを読むと、無関係な災害種別の値を要求したサブモードの値として
        /// 返してしまう——「確信を持って誤った数値」になる。HazardMapReader.SampleAt は
        /// この判定を必ず経由するので、呼び出し元がチェックを忘れる余地は無い。
        ///
        /// IL 実測: InfoManager.CurrentSubMode は public な読み取り専用プロパティ
        /// （型 InfoManager.SubInfoMode）として実在する。
        /// </summary>
        public static bool IsShowingHazardFor(InfoManager.SubInfoMode subMode)
        {
            try
            {
                if (!Singleton<InfoManager>.exists) return false;
                var im = Singleton<InfoManager>.instance;
                return im.CurrentMode == InfoManager.InfoMode.DisasterHazard
                       && im.CurrentSubMode == subMode;
            }
            catch { return false; }
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
