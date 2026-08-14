using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 天気予報パネルを開閉するトグルボタン。
    ///
    /// ③のボタンと違い、既存パネル（DisastersPanel）の中には置かない。予報は独立した
    /// パネルなので、ボタンも UIView 直下に浮かせ、FreeSlotFinder で他 MOD やバニラの
    /// ボタンと重ならない位置を探す（設計書 5、ユーザーからの明示要件）。
    ///
    /// API 実測（docs/tools/ilload.ps1 で ColossalManaged.dll を直接確認、Task 5）:
    ///   - ColossalFramework.UI.UIView は UnityEngine.MonoBehaviour 直継承で
    ///     UIComponent ではない。したがって UIComponent.AddUIComponent&lt;T&gt;() の
    ///     総称オーバーロードは使えず、UIView 自身が持つ
    ///     `UIComponent AddUIComponent(Type type)`（非総称）だけを使ってキャストする。
    ///   - UIComponent.width / .height / .relativePosition / .absolutePosition /
    ///     .isVisible / .Show() / .Hide() / .tooltip はいずれも public な get/set として
    ///     実在（FireWhirlPanelButton が既に使っている width/height/relativePosition/
    ///     normalBgSprite 系と合わせて確認済み）。
    /// </summary>
    public static class ForecastPanelButton
    {
        private const string ButtonName = FreeSlotFinder.SelfPrefix + "ForecastButton";

        /// <summary>再試行の間隔（main スレッド更新の回数）。③のボタンと同じ間引きに揃える。</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>再試行の上限。UIView が現れない環境で永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private static readonly Vector2 ButtonSize = new Vector2(150f, 28f);
        private static readonly Vector2 PreferredPosition = new Vector2(8f, 50f);
        private const float StepY = 34f;
        private const int MaxSlotTries = 30;

        private static UIButton _button;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        /// <summary>診断向け。ボタンが設置済みか。</summary>
        public static bool Installed { get { return _button != null; } }

        /// <summary>診断向け。今回の設置で保存済み座標を再利用したか（false なら新規探索）。</summary>
        public static bool UsedSavedPosition { get; private set; }

        /// <summary>診断向け。新規探索だった場合、空きが見つかったか。</summary>
        public static bool FoundFreeSlot { get; private set; }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の試行は間引き、かつ設定で無効なら
        /// 何もしない（既に設置済みなら撤去する）。
        /// </summary>
        public static void Tick()
        {
            if (!ModSettings.ForecastEnabled.value)
            {
                if (_button != null) Remove();
                return;
            }

            if (_button != null || _gaveUp) return;
            if (_framesSinceTry++ < RetryIntervalFrames) return;
            _framesSinceTry = 0;
            Install();
        }

        /// <summary>main スレッドから呼ぶ。</summary>
        public static void Install()
        {
            if (_button != null || _gaveUp) return;

            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                Log.Warn("gave up looking for a UIView after "
                         + MaxAttempts + " attempts; forecast button not added");
                return;
            }

            try
            {
                var view = UIView.GetAView();
                if (view == null)
                {
                    Log.Diag("forecastButton", "UIView not ready yet");
                    return;
                }

                Vector2 pos = ResolvePosition();

                var component = view.AddUIComponent(typeof(UIButton));
                var button = (UIButton)component;
                button.name = ButtonName;
                button.text = Strings.ForecastTitle;
                button.tooltip = Strings.ForecastTitle;
                button.width = ButtonSize.x;
                button.height = ButtonSize.y;
                button.relativePosition = new Vector3(pos.x, pos.y);
                button.normalBgSprite = "ButtonMenu";
                button.hoveredBgSprite = "ButtonMenuHovered";
                button.pressedBgSprite = "ButtonMenuPressed";

                button.eventClick += (c, e) => ForecastPanel.Toggle();

                _button = button;
                Log.Info("forecast panel button installed at (" + pos.x + "," + pos.y + ")");
            }
            catch (System.Exception e)
            {
                // 例外で毎フレーム再突入しないよう、ここで諦める。
                _gaveUp = true;
                Log.Error("forecast panel button install failed", e);
            }
        }

        /// <summary>
        /// 保存済み座標があればそれを使い、無ければ FreeSlotFinder で探して保存する。
        /// 設定画面の「ボタン位置をリセット」は両方を -1 に戻すだけ（ModSettings 側）なので、
        /// ここでの再探索は次回のインストール（次のレベルロード）で自然に起きる。
        /// </summary>
        private static Vector2 ResolvePosition()
        {
            if (ModSettings.ForecastButtonX.value >= 0 && ModSettings.ForecastButtonY.value >= 0)
            {
                UsedSavedPosition = true;
                FoundFreeSlot = false;
                return new Vector2(ModSettings.ForecastButtonX.value, ModSettings.ForecastButtonY.value);
            }

            UsedSavedPosition = false;
            bool foundFree;
            // owner は null。ここは初回配置で、ボタンはこの呼び出しの後に
            // 生成される（＝除外すべき「自分自身」がまだ画面に存在しない）。
            // 逆に言えば、いま画面にある DisasterPlus 製のコンポーネントは全て
            // 本物の占有物なので、除外せずに避ける対象として数えるのが正しい
            // （FreeSlotFinder.SelfPrefix の doc、全体レビュー指摘 I4）。
            Vector2 pos = FreeSlotFinder.Find(
                PreferredPosition, ButtonSize, StepY, MaxSlotTries, null, out foundFree);
            FoundFreeSlot = foundFree;

            if (!foundFree)
            {
                Log.Diag("forecastButton", "no free slot found; using preferred position (may overlap)");
            }

            ModSettings.ForecastButtonX.value = Mathf.RoundToInt(pos.x);
            ModSettings.ForecastButtonY.value = Mathf.RoundToInt(pos.y);
            return pos;
        }

        /// <summary>レベルアンロード時。次の都市で必ず探し直せるよう再試行状態も戻す。</summary>
        public static void Remove()
        {
            if (_button != null)
            {
                Object.Destroy(_button.gameObject);
                _button = null;
            }

            _gaveUp = false;
            _framesSinceTry = 0;
            _attempts = 0;
            UsedSavedPosition = false;
            FoundFreeSlot = false;
        }
    }
}
