using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風パネル。**main スレッド専用。**
    ///
    /// 枠組みは②の <see cref="EarthquakePanel"/> をそのまま踏襲する
    /// （<c>UIView.AddUIComponent(Type)</c> の**非総称**オーバーロード、
    /// 構築途中の例外で孤児 GameObject を残さない try/catch、
    /// <c>backgroundSprite = "MenuPanel2"</c>、DLC 非所持で本体を組まない
    /// <see cref="_bodyBuilt"/>、<c>Tick()</c> の先頭で設定を見るガード）。
    ///
    /// ── ④が新しく持ち込むもの: 出所の見出し ────────────────────────
    ///
    /// ②は行ごとに <c>[measured]</c> / <c>[Disaster + model]</c> を付けた。
    /// **④はそれをしない。** バニラに原資が無いので④の数値はほぼ全部が本 MOD のもので、
    /// 全部が同じ出所なら行ごとの印は情報を持たない（設計書 §1.2 / §7-1）。
    /// 代わりに<b>見出しで一度だけ</b>名乗る —— それが
    /// <c>Strings.TyphoonModelHeader</c> と <c>Strings.TyphoonModelNote</c> の 2 行で、
    /// **このパネルにおいて最も重要な 2 行である**。行を足す担当者はここを消さないこと。
    ///
    /// 担保の実体は <see cref="TyphoonRows"/> に置いてある。
    /// **このファイルには <c>UILabel</c> の生成も <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── 発生は「地点を指す」から始まる ───────────────────────────
    ///
    /// ★★ 災害パネルの④のタイルも、このパネルの「発生」ボタンも、押すと
    ///    <b>配置カーソルを構える</b>（<see cref="TyphoonPlacementTool"/>）。
    ///    バニラの災害ボタンと同じ約束で、地図をクリックした地点から台風が始まる。
    ///    <see cref="ArmPlacement"/> がその入口で、**タイルはこれを呼ぶ**。
    ///
    /// 「止める」ボタンだけは地点を要らないので、従来どおり
    /// <see cref="TyphoonHub.Request"/> に依頼を積むだけである。
    /// どちらも **main スレッドから <c>DisasterManager</c> に触らない**。実際に災害を
    /// 作るのは次の sim tick の <see cref="TyphoonController"/> である。したがって
    /// 指してから見た目が変わるまで **1 tick の遅れがある**。二度指して 2 個発生したように
    /// 見えないよう、その間は「最初のシミュレーション更新を待っています」を出す
    /// （<see cref="TyphoonStatusRows"/> が <see cref="TyphoonHub.PendingRequest"/> を見る）。
    /// なお sim 側は同時に 1 個しか作らない（<c>TyphoonController.Start</c>）。
    ///
    /// ── 縦の予算 ──────────────────────────────────────
    ///
    /// T6〜T10 が <c>TyphoonEffectRows</c> に行を足す。パネルの高さは**中身に合わせて
    /// 決め、固定値を焼き込まない**（<see cref="ClampToView"/> がビューからはみ出す
    /// ときに警告する）。
    /// </summary>
    public static class TyphoonPanel
    {
        /// <summary>ボタン 1 個の動作。net35 なので <c>Action</c> ではなく自前の delegate。</summary>
        private delegate void ClickHandler();

        private const string PanelName = FreeSlotFinder.SelfPrefix + "TyphoonPanel";

        /// <summary>ボタン 1 個の大きさ。発生・停止の 2 個を横に並べる。</summary>
        private const float ActionButtonWidth = 200f;

        private const float ActionButtonHeight = 28f;

        private static UIPanel _panel;
        private static UILabel _titleLabel;

        /// <summary>
        /// 台風の行を構築したか。Natural Disasters DLC が無い環境では
        /// <c>ThunderStormAI</c> のプレハブが存在せず台風は原理的に 1 個も起きないので、
        /// 行を組まずに理由を 1 行だけ出す（②の <c>EarthquakePanel._bodyBuilt</c> と同じ）。
        /// </summary>
        private static bool _bodyBuilt;

        /// <summary>左上。既定値は <see cref="InfoHub"/> が位置を決める前だけ使う。</summary>
        private static Vector3 _origin = new Vector3(1260f, 120f);

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>このパネルの幅。<see cref="InfoHub"/> がタブ帯の幅を合わせるために読む。</summary>
        internal static float Width { get { return TyphoonRows.PanelWidth; } }

        /// <summary>
        /// 左上を決める。**位置を決める主体は <see cref="InfoHub"/> 1 つだけである**
        /// （<c>DisasterPanelBar</c> のクラス doc「位置を決める主体が複数ある限り、
        /// この事故は形を変えて何度でも起きる」と同じ規律）。
        /// ここで座標を発明しないこと。
        /// </summary>
        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
            _panel.relativePosition = _origin;
            ClampToView(_panel);
        }

        public static void Show()
        {
            EnsureBuilt();
            if (_panel == null) return;
            _panel.Show();
            Refresh();
        }

        public static void Hide()
        {
            if (_panel != null) _panel.Hide();
        }

        /// <summary>main スレッドから毎フレーム。表示中のときだけ内容を更新する。</summary>
        public static void Tick()
        {
            // 設定で無効化されたときにパネルが開いたままだと、OnSimulationTick が
            // publish を止めた古いスナップショットを永遠に出し続ける「凍りついたのに
            // 生きて見える」パネルになり、閉じる手段のボタンも既に撤去済みで消せない
            // （①のレビュー指摘。②も同じガードを持っている）。
            if (!ModSettings.TyphoonEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>レベルアンロード時。**セッション状態を 1 つも持ち越さない。**</summary>
        public static void Destroy()
        {
            // 参照を捨てるだけ。実体はパネルの GameObject と一緒に消える。
            TyphoonStatusRows.Destroy();
            TyphoonEffectRows.Destroy();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _bodyBuilt = false;
        }

        private static void EnsureBuilt()
        {
            if (_panel != null) return;
            try
            {
                Build();
            }
            catch (System.Exception e)
            {
                Log.Error("typhoon panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; typhoon panel not built");
                return;
            }

            // ★ _panel への代入は構築の最後の 1 行にしない。途中の例外で
            //    EnsureBuilt() の catch が呼ぶ Destroy() は _panel==null を見て何もせず、
            //    UIView に取り付け済みの GameObject が孤児のまま残る
            //    （クリックのたびに 1 枚ずつ積み上がる。①のレビュー指摘）。
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("typhoon panel built");
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
            panel.width = TyphoonRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // 位置は InfoHub が決める（MoveTo）。ここには既定値しか無い ——
            // パネルは同時に 1 枚しか出ないので、互いに避ける座標はもう要らない。
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = TyphoonRows.AddTitleRow(panel, "Title", 10f, y,
                TyphoonRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ 閉じるボタンはここには無い。**タブ帯の X が 1 つだけ持つ**（InfoHub）。
            y += 30f;

            // DLC が無い環境では台風そのものが存在しない。行を出さずに理由を書く。
            _bodyBuilt = ModCompat.NaturalDisastersOwned;
            if (!_bodyBuilt)
            {
                var needsDlc = TyphoonRows.AddRow(panel, "NeedsDlc", ref y, 36f);
                TyphoonRows.SetPlain(needsDlc, Strings.TyphoonNeedsDlc);
                panel.height = y + 8f;
                return;
            }

            // ★★ ④の表示規約を名乗る 2 行。**行ごとの印を付けない代わりに、
            //     ここで一度だけ全部の出所を言う**（クラス doc / 設計書 §7-1）。
            TyphoonRows.AddSectionHeader(panel, "ModelHeader", ref y, Strings.TyphoonModelHeader);
            var note = TyphoonRows.AddRow(panel, "ModelNote", ref y, 56f);
            // ★ 印（[measured] / [実測]）を挟むのは TyphoonRows の仕事である。
            //   ここで文を組み立てない（全体レビュー I5。あちらの SetModelNote の doc）。
            TyphoonRows.SetModelNote(note);

            AddActionButtons(panel, ref y);

            TyphoonStatusRows.Build(panel, ref y);
            // ★ 各要素（落雷・風害・氾濫・雲・竜巻）の行は台風そのものの状態より下。
            //   T7〜T10 は TyphoonEffectRows の中に足すこと（ここの並びは変えない）。
            TyphoonEffectRows.Build(panel, ref y);

            panel.height = y + 8f;
            ClampToView(panel);
        }

        /// <summary>
        /// 「台風を止める」の 1 個だけ。
        ///
        /// ★★ **発生させるボタンはここには無い。** 台風を起こすのは災害パネルの
        ///    ④タイル（バニラの災害ボタンと同じ 3 手）だけである。このパネルは
        ///    <b>読むための場所</b>で、起こす場所ではない —— 2 か所から起こせると、
        ///    「押してから地図をクリック」という約束の入口が 2 つになる。
        ///
        /// 止めるのは地点が要らないので依頼を積むだけ。ボタンのテキストは
        /// <c>UIButton.text</c> であって <c>UILabel.text</c> ではないので、
        /// <see cref="TyphoonRows"/> の担保（<c>UILabel</c> の生成と <c>.text</c> 代入の
        /// 一元化）とは無関係である。
        /// </summary>
        private static void AddActionButtons(UIPanel panel, ref float y)
        {
            AddButton(panel, "StopButton", Strings.TyphoonStop, null,
                TyphoonRows.RowLeft, y,
                delegate { TyphoonHub.Request(TyphoonRequestData.Of(TyphoonRequest.Stop)); });
            y += ActionButtonHeight + 10f;
        }

        private static void AddButton(UIPanel panel, string suffix, string text, string tooltip,
                                      float x, float y, ClickHandler onClick)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Typhoon" + suffix;
            button.text = text;
            if (!string.IsNullOrEmpty(tooltip)) button.tooltip = tooltip;
            button.width = ActionButtonWidth;
            button.height = ActionButtonHeight;
            button.relativePosition = new Vector3(x, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            // ★ main スレッドから DisasterManager に触らない。構えるか、依頼を積むだけ。
            button.eventClick += (c, e) => onClick();
        }

        /// <summary>
        /// パネルの下端がビューからはみ出さない位置まで上げる。②の
        /// <c>EarthquakePanel.ClampToView</c> と同じで、**行を足すたびにパネルは伸びる**。
        /// いちばん下の行——注記や「なぜ何も起きないか」——が静かに画面外へ出るのを
        /// 黙って許さない。
        /// </summary>
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
                if (top + panel.height > viewHeight - Margin)
                {
                    top = viewHeight - Margin - panel.height;
                }
                if (top < Margin)
                {
                    // ★ ここに来たら**内容がビューより高い**。上端に寄せても
                    //    いちばん下の行が画面外に出る。**黙って切れさせない。**
                    //    構築時の 1 回だけなのでスロットル不要。
                    top = Margin;
                    Log.Warn("typhoon panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
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
                Log.Warn("typhoon panel clamp failed: " + e.GetType().Name);
            }
        }

        private static void Refresh()
        {
            TyphoonRows.SetPlain(_titleLabel, Strings.TyphoonTitle);

            // DLC が無い環境では説明の 1 行しか構築していない（_bodyBuilt の doc）。
            if (!_bodyBuilt) return;

            // ★ スナップショットは 1 フレームに 1 回だけ取る（ロックを 2 回取らない）。
            var snapshot = TyphoonHub.Latest;
            TyphoonStatusRows.Refresh(snapshot);
            TyphoonEffectRows.Refresh(snapshot);
        }
    }
}
