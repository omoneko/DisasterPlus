using ColossalFramework.UI;
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
    /// ── 発生ボタンは sim へ「依頼」を出すだけ ────────────────────────
    ///
    /// <c>eventClick</c> は <see cref="TyphoonHub.Request"/> を呼ぶだけで、
    /// **main スレッドから <c>DisasterManager</c> に触らない**。実際に災害を作るのは
    /// 次の sim tick の <see cref="TyphoonController"/> である。したがって押してから
    /// 見た目が変わるまで **1 tick の遅れがある**。二度押しで 2 個発生したように
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

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

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

        public static void Toggle()
        {
            if (IsVisible) Hide(); else Show();
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
            // ①の予報パネル（x=200、幅 380）とも②の地震パネル（x=600、幅 640）とも
            // 重ならない位置。UIView の座標系は高さ 1080 に正規化され、16:9 なら幅は
            // およそ 1920 になるので、x=1260 + 640 = 1900 は画面内に収まる。
            // 収まらない解像度では ClampToView が縦だけ寄せる（横は動かさない）。
            panel.relativePosition = new Vector3(1260f, 120f);
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = TyphoonRows.AddTitleRow(panel, "Title", 10f, y,
                TyphoonRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            AddCloseButton(panel, y);
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

        private static void AddCloseButton(UIPanel panel, float y)
        {
            var closeButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            closeButton.name = FreeSlotFinder.SelfPrefix + "TyphoonCloseButton";
            closeButton.text = "X";
            closeButton.width = 24f;
            closeButton.height = 24f;
            closeButton.relativePosition = new Vector3(TyphoonRows.PanelWidth - 32f, y);
            closeButton.normalBgSprite = "ButtonMenu";
            closeButton.hoveredBgSprite = "ButtonMenuHovered";
            closeButton.pressedBgSprite = "ButtonMenuPressed";
            closeButton.eventClick += (c, e) => Hide();
        }

        /// <summary>
        /// 「台風を発生させる」「台風を止める」。**どちらも sim へ依頼を積むだけ**
        /// （クラス doc）。ボタンのテキストは <c>UIButton.text</c> であって
        /// <c>UILabel.text</c> ではないので、<see cref="TyphoonRows"/> の担保
        /// （<c>UILabel</c> の生成と <c>.text</c> 代入の一元化）とは無関係である。
        /// </summary>
        private static void AddActionButtons(UIPanel panel, ref float y)
        {
            AddActionButton(panel, "StartButton", Strings.TyphoonStart,
                TyphoonRows.RowLeft, y, TyphoonRequest.Start);
            AddActionButton(panel, "StopButton", Strings.TyphoonStop,
                TyphoonRows.RowLeft + ActionButtonWidth + 12f, y, TyphoonRequest.Stop);
            y += ActionButtonHeight + 10f;
        }

        private static void AddActionButton(UIPanel panel, string suffix, string text,
                                            float x, float y, TyphoonRequest request)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Typhoon" + suffix;
            button.text = text;
            button.width = ActionButtonWidth;
            button.height = ActionButtonHeight;
            button.relativePosition = new Vector3(x, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            // ★ main スレッドから DisasterManager に触らない。依頼を積むだけ。
            button.eventClick += (c, e) => TyphoonHub.Request(request);
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
