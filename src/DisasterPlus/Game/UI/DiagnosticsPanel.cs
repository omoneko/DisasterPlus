using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断のタブ。**main スレッド専用。**
    ///
    /// ── なぜ画面に置くのか ────────────────────────────────
    ///
    /// 診断ダンプはこれまで <c>Ctrl + ホットキー</c> でしか出せなかった。
    /// 押し方はオプション画面の説明文にしか書いておらず、**説明を消すと
    /// 出し方ごと消える**。そこで押せるボタンにしてタブの 1 枚に置く ——
    /// 説明を減らすというのは、機能を隠すことではない。
    ///
    /// ── 出すのは 4 行だけ ────────────────────────────────
    ///
    /// 状態そのものはダンプファイルの中にある。ここに要約を積み上げると、
    /// **同じ内容が 2 か所にあって片方だけ古くなる**（この MOD が
    /// <c>DiagnosticFormatter</c> をオーバーレイとダンプで共有している理由と同じ）。
    /// ここに置くのは「どうやって出すか」と「どこに出るか」だけである。
    /// </summary>
    internal static class DiagnosticsPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "DiagnosticsPanel";

        /// <summary>①②④⑤と同じ 640。<see cref="InfoHub"/> がタブ帯の幅に使う。</summary>
        internal const float PanelWidth = 640f;

        private const float RowLeft = 12f;
        private const float RowWidth = PanelWidth - 2f * RowLeft;

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _overlayLabel;
        private static UILabel _hintLabel;
        private static UIButton _dumpButton;

        /// <summary>左上。既定値は <see cref="InfoHub"/> が位置を決める前だけ使う。</summary>
        private static Vector3 _origin = new Vector3(8f, 120f);

        internal static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        internal static float Width { get { return PanelWidth; } }

        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
            _panel.relativePosition = _origin;
            ClampToView(_panel);
        }

        internal static void Show()
        {
            EnsureBuilt();
            if (_panel == null) return;
            _panel.Show();
            Refresh();
        }

        internal static void Hide()
        {
            if (_panel != null) _panel.Hide();
        }

        /// <summary>main スレッドから毎フレーム。表示中のときだけ内容を更新する。</summary>
        internal static void Tick()
        {
            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>レベルアンロード時。**セッション状態を 1 つも持ち越さない。**</summary>
        internal static void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);

            _panel = null;
            _titleLabel = null;
            _overlayLabel = null;
            _hintLabel = null;
            _dumpButton = null;
        }

        private static void EnsureBuilt()
        {
            if (_panel != null) return;
            try { Build(); }
            catch (System.Exception e)
            {
                Log.Error("diagnostics panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; diagnostics panel not built");
                return;
            }

            // ★ _panel への代入を構築の最後の 1 行にしない（他のパネルと同じ理由 ——
            //    途中の例外で孤児 GameObject が残る）。
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
            }
            catch
            {
                if (panel != null) Object.Destroy(panel.gameObject);
                throw;
            }
        }

        private static void BuildContents(UIPanel panel)
        {
            panel.name = PanelName;
            panel.width = PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = AddLabel(panel, "Title", y, 24f);
            _titleLabel.textScale = 1.1f;
            y += 30f;

            _overlayLabel = AddLabel(panel, "Overlay", y, 20f);
            y += 26f;

            _dumpButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            _dumpButton.name = FreeSlotFinder.SelfPrefix + "DiagnosticsDumpButton";
            _dumpButton.text = Strings.DiagnosticsDumpButton;
            _dumpButton.width = 260f;
            _dumpButton.height = 28f;
            _dumpButton.relativePosition = new Vector3(RowLeft, y);
            _dumpButton.normalBgSprite = "ButtonMenu";
            _dumpButton.hoveredBgSprite = "ButtonMenuHovered";
            _dumpButton.pressedBgSprite = "ButtonMenuPressed";
            // ★ ここではファイルを書かない。main が依頼し、sim が組み立て、
            //   main が書き出す 3 段（DiagnosticDump のクラス doc）。
            _dumpButton.eventClick += (c, e) => DiagnosticDump.RequestDump();
            y += 34f;

            _hintLabel = AddLabel(panel, "Hint", y, 36f);
            _hintLabel.wordWrap = true;
            y += 42f;

            panel.height = y;
            ClampToView(panel);
        }

        private static void Refresh()
        {
            SetText(_titleLabel, Strings.DiagnosticsTitle);
            SetText(_overlayLabel, Strings.DiagnosticsOverlayRow + ": "
                + (ModSettings.OverlayEnabled.value ? Strings.DiagnosticsOn : Strings.DiagnosticsOff)
                + "    " + Strings.DiagnosticsHotkeyRow + ": "
                + ((KeyCode)ModSettings.OverlayHotkey.value));
            SetText(_hintLabel, Strings.DiagnosticsDumpHint + "  (" + DiagnosticDump.FileName + ")");

            if (_dumpButton != null && _dumpButton.text != Strings.DiagnosticsDumpButton)
            {
                _dumpButton.text = Strings.DiagnosticsDumpButton;
            }
        }

        private static UILabel AddLabel(UIPanel parent, string suffix, float y, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Diagnostics" + suffix;
            label.relativePosition = new Vector3(RowLeft, y);
            label.width = RowWidth;
            label.height = height;
            label.autoSize = false;
            return label;
        }

        private static void SetText(UILabel label, string text)
        {
            if (label == null) return;
            string value = text == null ? "" : text;
            if (label.text != value) label.text = value;
        }

        private static void ClampToView(UIPanel panel)
        {
            try
            {
                var view = panel.GetUIView();
                float viewHeight = view != null ? view.fixedHeight : 0f;
                if (viewHeight <= 0f) return;

                const float Margin = 8f;
                var pos = panel.relativePosition;
                float top = pos.y;
                if (top + panel.height > viewHeight - Margin) top = viewHeight - Margin - panel.height;
                if (top < Margin) top = Margin;
                // ★★ **上へは <c>InfoHub</c> が指定した位置（＝タブ帯の真下）より上に出さない。**
                //    （2026-08-22、実機報告「天気タブ・地震タブの中に X で閉じられない
                //    タブがあり」の正体。）上の 2 つの寄せは下端を画面に収めるためだけに
                //    パネルを上へ上げるので、背の高いパネルは**タブ帯をまるごと覆い隠して
                //    いた** —— 閉じる手段そのものが押せなくなる。収まらないぶんは下へはみ出すが、
                //    帯の左端を掴めば一緒に動かせる（<c>InfoHub</c> のドラッググリップ）。
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("diagnostics panel clamp failed: " + e.GetType().Name);
            }
        }
    }
}
