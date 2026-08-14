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
    ///
    /// ── 唯一の例外: <see cref="IsShowingHazard"/> の診断からの読み取り ─────────
    ///
    /// <c>IDisasterFeature.WriteDiagnostics</c> は sim スレッド専用の契約で
    /// （<c>DiagnosticDump</c> のクラス doc）、そこから <see cref="IsShowingHazard"/> を
    /// 読んでいる箇所が 2 つある（<c>ForecastFeature.WriteUiState</c> /
    /// <c>EarthquakeFeature.WriteUiState</c>）。**これは意図的に許可する。**
    /// 根拠は IL の実測（本レビューで再確認）:
    ///
    /// <code>
    /// InfoManager::get_CurrentMode     IL_0000 ldarg.0 ; ldfld m_actualMode    ; ret
    /// InfoManager::get_CurrentSubMode  IL_0000 ldarg.0 ; ldfld m_actualSubMode ; ret
    /// </code>
    ///
    /// **どちらも単一フィールドの読み出しだけで、配列も遅延初期化も無い。**
    /// この MOD がスレッド境界で恐れているのは
    /// <c>IndexOutOfRangeException</c>（バッファへの添字アクセス）であり、
    /// enum 1 個の非同期読み出しは最悪でも「1 tick 古い値」にしかならない。
    /// 用途も診断ダンプの 1 行（<c>showing hazard view: yes/no</c>）だけで、
    /// 古い値でも意味が壊れない（<c>CameraShakeBooster.LastAdded</c> /
    /// <c>WaveformView.State</c> と同じ扱い）。
    ///
    /// **<see cref="ShowHazard"/> / <see cref="Clear"/> は例外ではない。**
    /// あちらは <c>SetCurrentMode</c> を呼んで UI の状態を書き換えるので、
    /// main スレッド専用のままである。
    /// </summary>
    public static class InfoModeSwitch
    {
        /// <summary>
        /// 災害ハザードビューが表示中か。**診断からは sim スレッドでも読んでよい**
        /// （クラス doc の「唯一の例外」。IL 実測で単一フィールドの読み出しと確定済み）。
        /// </summary>
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
