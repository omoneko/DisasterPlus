using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 震度分布オーバーレイの操作と凡例。<see cref="EarthquakePanel"/> の一部だが、
    /// **既に 1592 行あるあのファイルをこれ以上伸ばさない**ために別ファイルにしてある
    /// （プロジェクト規約は 800 行）。
    ///
    /// ── 層の分離の担保はそのまま ─────────────────────────────
    ///
    /// このファイルは <c>AddUIComponent(typeof(UILabel))</c> も
    /// <c>UILabel.text</c> への代入も**書かない**。行は
    /// <see cref="EarthquakePanel.AddPlainRow"/> /
    /// <see cref="EarthquakePanel.AddLayer1Row"/> を通してしか作れず、
    /// 中身は <see cref="EarthquakePanel.SetPlain"/> /
    /// <see cref="EarthquakePanel.SetLayer1"/> を通してしか書けない
    /// （<c>Strings.SourceVanilla</c> はあちらのセッターの中にしか現れない）。
    /// つまり「grep 1 回で確認できる」という担保は壊れていない。
    ///
    /// ── 既定 OFF、ただし隠さない ─────────────────────────────
    ///
    /// オーバーレイは常時表示にしない（地震のフレームはゲーム中で最も重く、
    /// 常に地図を塗り潰していると都市そのものが見えない）。かといって
    /// 設定画面の奥のチェックボックスにもしない ——「分布が見たい」という
    /// 依頼そのものに対する答えが、見つけられない場所にあっては意味が無い。
    /// **パネルの中に、バニラのハザードビューのボタンと並べて置く。**
    ///
    /// ── 隣のボタンと混同させない ─────────────────────────────
    ///
    /// すぐ上の「マップに表示」はバニラの情報ビュー
    /// （<c>SubInfoMode.EarthquakeHazard</c>）で、塗る形が**違う**:
    /// 亀裂**線分**までの距離・2 次減衰・<c>Rmax = R + 400</c>、しかも
    /// <c>Located</c>（＝地震計）が無いと 1 セルも塗られない（§A-6）。
    /// 一方このオーバーレイは震央からの線形ランプで、地震計が無くても出る。
    /// 誤解を防ぐ手当てを 3 段重ねてある:
    ///   1. 凡例（常設）が両者の違いを名指しする
    ///   2. 2 つが同時に出ているときは、状態行がそれを名指しする
    ///   3. 色相を分ける（バニラのハザードは黄〜赤系、こちらは青緑）
    /// </summary>
    internal static class EarthquakeOverlayRows
    {
        private static UIButton _button;
        private static UILabel _statusLabel;

        /// <summary>パネル構築時に 1 回。<paramref name="y"/> を進める。</summary>
        internal static void Build(UIPanel panel, ref float y)
        {
            _button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            _button.name = FreeSlotFinder.SelfPrefix + "EarthquakeOverlayToggle";
            _button.text = Strings.EarthquakeOverlayShow;
            _button.tooltip = Strings.EarthquakeOverlayRow;
            _button.width = EarthquakePanel.PanelWidth - 24f;
            _button.height = 24f;
            _button.relativePosition = new Vector3(12f, y);
            _button.normalBgSprite = "ButtonMenu";
            _button.hoveredBgSprite = "ButtonMenuHovered";
            _button.pressedBgSprite = "ButtonMenuPressed";
            _button.eventClick += (c, e) => EarthquakeOverlay.Toggle();
            y += 30f;

            // 状態は第 1 層で名乗る。出している量がバニラの実測値そのものだからである。
            _statusLabel = EarthquakePanel.AddLayer1Row(panel, "OverlayStatus", ref y, 42f);

            // 凡例は**常設**。地震が無くても、読み取りに失敗していても出す
            // （これは「今の観測値」ではなく「この絵の読み方」である。
            //  Strings.EarthquakeSensorEffect と同じ扱いで、一度書いたら
            //  以後どこからも書き換えない＝参照を保持する必要が無い）。
            EarthquakePanel.AddPlainRow(panel, "OverlayLegend", ref y,
                Strings.EarthquakeOverlayLegend, 72f);
        }

        /// <summary>
        /// 毎フレーム。<paramref name="hazardViewOn"/> はバニラのハザード情報ビューが
        /// 表示中かどうかで、呼び出し側が既に 1 回だけ求めている値を受け取る
        /// （同じフレームで <c>InfoManager</c> を 2 回引かない）。
        /// </summary>
        internal static void Refresh(bool hazardViewOn)
        {
            if (_button == null || _statusLabel == null) return;

            _button.text = EarthquakeOverlay.Enabled
                ? Strings.EarthquakeOverlayHide
                : Strings.EarthquakeOverlayShow;

            if (!EarthquakeOverlay.Registered)
            {
                // 描画経路そのものが取れていない。黙って何も出ないのを避ける。
                EarthquakePanel.SetLayer1(_statusLabel,
                    Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayUnavailable);
                return;
            }

            if (!EarthquakeOverlay.Enabled)
            {
                EarthquakePanel.SetLayer1(_statusLabel,
                    Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayOff);
                return;
            }

            string text;
            if (EarthquakeOverlay.DrawnQuakes == 0)
            {
                // ★ 0 を「安全」と読ませない。描くものが無い理由を書く
                //    （①の ForecastNoStormDetected と同じ扱い）。
                text = Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayNothingToDraw;
            }
            else
            {
                text = Strings.EarthquakeOverlayRow + ": " + Strings.EarthquakeOverlayOn
                       + ", " + EarthquakeOverlay.DrawnQuakes + " "
                       + Strings.EarthquakeOverlayQuakes;
            }

            if (EarthquakeOverlay.FaultGeometryMissing)
            {
                text += "  " + Strings.EarthquakeOverlayFaultUnknown;
            }
            if (EarthquakeOverlay.BudgetExhausted)
            {
                text += "  " + Strings.EarthquakeOverlayCapped;
            }
            if (hazardViewOn)
            {
                // いちばん誤解が起きる状態。2 枚の絵が同時に出ている。
                text += "  " + Strings.EarthquakeOverlayBothOn;
            }

            EarthquakePanel.SetLayer1(_statusLabel, text);
        }

        /// <summary>
        /// スナップショットが読めていないとき。**凡例は消さない**
        /// （絵の読み方は観測値ではない）。ボタンの表示だけは実状に合わせる。
        /// </summary>
        internal static void Clear()
        {
            if (_button != null)
            {
                _button.text = EarthquakeOverlay.Enabled
                    ? Strings.EarthquakeOverlayHide
                    : Strings.EarthquakeOverlayShow;
            }
            EarthquakePanel.SetPlain(_statusLabel, "");
        }

        /// <summary>
        /// レベルアンロード時。パネルの <c>GameObject</c> ごと破棄されるので
        /// 参照を捨てるだけでよい（<see cref="EarthquakePanel.Destroy"/> から）。
        /// </summary>
        internal static void Destroy()
        {
            _button = null;
            _statusLabel = null;
        }
    }
}
