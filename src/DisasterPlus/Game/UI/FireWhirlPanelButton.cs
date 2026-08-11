using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 災害パネルに「火災旋風」ボタンを足す。
    ///
    /// パネルの見つけ方: 名前文字列 "DisastersPanel" を推測するのではなく、実際に存在する
    /// バニラの型 DisastersPanel（Assembly-CSharp、名前空間なし、public sealed、
    /// GeneratedScrollPanel 継承、IL 実測済み）を Object.FindObjectOfType で直接探す。
    /// Task 8 の IntensityUnlock が Object.FindObjectOfType&lt;DisastersOptionPanel&gt;() で
    /// 実証済みの手法と同じで、文字列名の当て外れが起きない。
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

        private static UIButton _button;

        public static void Install()
        {
            if (_button != null) return;

            var panel = Object.FindObjectOfType<DisastersPanel>();
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

            _button = container.AddUIComponent<UIButton>();
            _button.name = ButtonName;
            _button.text = Strings.FireWhirlName;
            _button.tooltip = Strings.FireWhirlTooltip;
            _button.width = 140f;
            _button.height = 28f;
            _button.relativePosition = new Vector3(8f, 8f);
            _button.normalBgSprite = "ButtonMenu";
            _button.hoveredBgSprite = "ButtonMenuHovered";
            _button.pressedBgSprite = "ButtonMenuPressed";

            _button.eventClick += (c, e) => FireWhirlPlacementTool.Activate();

            Log.Info("fire whirl panel button installed");
        }

        public static void Remove()
        {
            if (_button == null) return;
            Object.Destroy(_button.gameObject);
            _button = null;
        }
    }
}
