using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 災害パネルに「火災旋風」ボタンを足す。
    ///
    /// パネルの見つけ方: 名前文字列 "DisastersPanel" を推測するのではなく、実際に存在する
    /// バニラの型 DisastersPanel（Assembly-CSharp、名前空間なし、public sealed、
    /// GeneratedScrollPanel 継承、IL 実測済み）を型で直接探す。文字列名の当て外れが起きない。
    /// 探索は SceneObjects.FindInScene 経由で行う（Object.FindObjectOfType は Unity 5.6 では
    /// 非アクティブな GameObject を返さないため）。
    ///
    /// DisastersPanel は UIPanel 本体ではなく、その GameObject に同居する
    /// UICustomControl 派生（ColossalFramework.UI.UICustomControl）。IL 実測で
    /// UICustomControl.component は GetComponent&lt;UIComponent&gt;() を遅延キャッシュして
    /// 返すだけと確認済みなので、同じ GameObject 上の実 UI コンポーネント
    /// （バニラの災害選択パネル本体）を安全に取得できる。
    ///
    /// ツールチップの文言はここで一度設定されるだけなので、ゲーム内で言語を切り替えても
    /// 次のレベルロードまで変わらない。設定画面と違って OnLocaleChanged では再構築されない。
    /// </summary>
    public static class FireWhirlPanelButton
    {
        private const string ButtonName = "DisasterPlusFireWhirlButton";

        /// <summary>再試行の間隔（main スレッド更新の回数）。シーン全体の走査は毎フレーム回すには重い。</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>再試行の上限。パネルが現れない環境で永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private static UIButton _button;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の試行は RetryIntervalFrames ごと。
        /// IntensityUnlock.Tick と同じ間引き・上限にそろえてある。
        /// </summary>
        public static void Tick()
        {
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
                Log.Warn("gave up looking for the disasters panel after "
                         + MaxAttempts + " attempts; fire whirl button not added");
                return;
            }

            try
            {
                var panel = SceneObjects.FindInScene<DisastersPanel>();
                if (panel == null)
                {
                    Log.Diag("panelButton", "DisastersPanel not found yet");
                    return;
                }

                var container = panel.component;
                if (container == null)
                {
                    Log.Diag("panelButton", "DisastersPanel.component not available yet");
                    return;
                }

                var button = container.AddUIComponent<UIButton>();
                button.name = ButtonName;
                button.text = Strings.FireWhirlName;
                button.tooltip = Strings.FireWhirlTooltip;
                button.width = 140f;
                button.height = 28f;
                button.relativePosition = new Vector3(8f, 8f);
                button.normalBgSprite = "ButtonMenu";
                button.hoveredBgSprite = "ButtonMenuHovered";
                button.pressedBgSprite = "ButtonMenuPressed";

                button.eventClick += (c, e) => FireWhirlPlacementTool.Activate();

                _button = button;
                Log.Info("fire whirl panel button installed");
            }
            catch (System.Exception e)
            {
                // 例外で毎フレーム再突入しないよう、ここで諦める。
                _gaveUp = true;
                Log.Error("fire whirl panel button install failed", e);
            }
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
        }
    }
}
