using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火山パネル。**main スレッド専用。**
    ///
    /// 枠組みは②の <c>EarthquakePanel</c>・④の <see cref="TyphoonPanel"/> をそのまま踏襲する
    /// （<c>UIView.AddUIComponent(Type)</c> の**非総称**オーバーロード、
    /// 構築途中の例外で孤児 GameObject を残さない try/catch、
    /// <c>backgroundSprite = "MenuPanel2"</c>、本体を組まない <see cref="_bodyBuilt"/>、
    /// <c>Tick()</c> の先頭で設定を見るガード）。
    ///
    /// ── ⑤が名乗る 2 つのこと ────────────────────────────────
    ///
    /// 1. **出所の見出し。** ⑤の数値は原則すべて本 MOD のもので、行ごとの印は付けない
    ///    （設計書 §7.4）。代わりに<b>見出しで一度だけ</b>名乗る ——
    ///    <c>Strings.VolcanoModelHeader</c> と <c>Strings.VolcanoModelNote</c> の 2 行。
    /// 2. **地形の変更は取り消せない。** これは<b>火山が無いときも常に出す</b>
    ///    （設計書 §7.1、<see cref="VolcanoStatusRows"/>）。「うるさいから」と
    ///    条件付きにしないこと。
    ///
    /// 担保の実体は <see cref="VolcanoRows"/> に置いてある。
    /// **このファイルには <c>UILabel</c> の生成も <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── 本体を組まない条件は「地形を書けるか」そのものである ──────────────
    ///
    /// ④は DLC の有無で本体を組むかどうかを決めた。⑤は DLC を要らない（設計書 §1.4）。
    /// ⑤が組めない唯一の条件は<b>地形の書き込み経路が解決できないこと</b>で、
    /// その述語は <see cref="VolcanoTerrainFacts.Usable"/> ——
    /// つまり<b>⑤の機能そのものが門にしている式と同じ式</b>である。
    /// 「フィールドが解決した」を述語にすると、値が使えない環境でパネルだけが
    /// 動いて見える（②④のレビューが同じ欠陥を見つけている）。
    ///
    /// ── パネルの位置（①②④と重なる範囲を明示する）─────────────────
    ///
    /// UIView の座標系は高さ 1080 に正規化され、16:9 なら幅はおよそ 1920 になる。
    /// ①は (200,150) 幅 380、②は (600,150) 幅 640、④は (1260,120) 幅 640 なので、
    /// **640 幅のパネルを横に置ける空きはもう無い。** ⑤は (620, 60) に置く:
    ///
    ///   - ①（200〜580）とは重ならない
    ///   - ④（1260〜1900）とも重ならない（右端がちょうど 1260 で接する）
    ///   - ②（600〜1240 / y 150 以降）とは横に重なるが、**上端 90 px は②より上**に
    ///     出るので、②を開いたままでも⑤の見出しと閉じるボタンは必ず押せる
    ///
    /// **「重ならない」と嘘を書かないこと。** 実際に重なるのは②だけで、
    /// その重なり方まで書いてある。<see cref="ClampToView"/> は縦だけ寄せる。
    /// </summary>
    public static class VolcanoPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "VolcanoPanel";

        /// <summary>「火山を設置する」ボタンの大きさ。</summary>
        private const float PlaceButtonWidth = 240f;

        private const float PlaceButtonHeight = 28f;

        /// <summary>パネルの既定の左上。<see cref="ClampToView"/> が縦だけ寄せる。</summary>
        private static readonly Vector3 BasePosition = new Vector3(620f, 60f);

        private static UIPanel _panel;
        private static UILabel _titleLabel;

        /// <summary>最後に <see cref="ApplyHeight"/> が入れた高さ（<c>-1</c> = まだ入れていない）。</summary>
        private static float _appliedHeight = -1f;

        /// <summary>
        /// 火山の行を構築したか。地形の書き込み経路が解決できない環境では
        /// ⑤は 1 メートルも山を上げられないので、行を組まずに理由を 1 行だけ出す
        /// （②の <c>EarthquakePanel._bodyBuilt</c>・④の <c>TyphoonPanel._bodyBuilt</c> と同じ）。
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

        /// <summary>main スレッドから毎フレーム。表示中のときだけ内容を更新する。</summary>
        public static void Tick()
        {
            // 設定で無効化されたときにパネルが開いたままだと、OnSimulationTick が
            // publish を止めた古いスナップショットを永遠に出し続ける「凍りついたのに
            // 生きて見える」パネルになり、閉じる手段のボタンも既に撤去済みで消せない
            // （①のレビュー指摘。②④も同じガードを持っている）。
            if (!ModSettings.VolcanoEnabled.value)
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
            VolcanoStatusRows.Destroy();
            VolcanoConfirmRows.Destroy();
            VolcanoEffectRows.Destroy();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _bodyBuilt = false;
            _appliedHeight = -1f;
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
                Log.Error("volcano panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; volcano panel not built");
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
                Log.Info("volcano panel built");
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
            panel.width = VolcanoRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // クラス doc の「パネルの位置」。重なる相手は②だけで、その上端 90 px は空く。
            panel.relativePosition = BasePosition;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = VolcanoRows.AddTitleRow(panel, "Title", 10f, y,
                VolcanoRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            AddCloseButton(panel, y);
            y += 30f;

            // ★ 述語は⑤の機能そのものの門と同じ式（クラス doc）。
            //   VolcanoReader.ScanTerrainFacts はキャッシュを触らない純粋な走査なので
            //   main スレッドから呼んでよい（あちらのクラス doc）。
            _bodyBuilt = VolcanoReader.ScanTerrainFacts().Usable;
            if (!_bodyBuilt)
            {
                var unavailable = VolcanoRows.AddRow(panel, "TerrainUnavailable", ref y, 56f);
                VolcanoRows.SetPlain(unavailable, Strings.VolcanoTerrainUnavailable);
                ApplyHeight(panel, y + 8f);
                return;
            }

            // ★★ ⑤の表示規約を名乗る 2 行。**行ごとの印を付けない代わりに、
            //     ここで一度だけ全部の出所を言う**（クラス doc / 設計書 §7.4）。
            VolcanoRows.AddSectionHeader(panel, "ModelHeader", ref y, Strings.VolcanoModelHeader);
            var note = VolcanoRows.AddRow(panel, "ModelNote", ref y, 56f);
            // ★ 印（[measured] / [実測]）を挟むのは VolcanoRows の仕事である。
            //   ここで文を組み立てない（あちらの SetModelNote の doc）。
            VolcanoRows.SetModelNote(note);

            AddPlaceButton(panel, ref y);

            VolcanoStatusRows.Build(panel, ref y);

            // ★ 確認の一式は**いちばん下**に置く（あちらの BlockTop の doc）。
            //   出していないときはパネルをその手前まで縮めるので、空白が残らない。
            VolcanoConfirmRows.Build(panel, ref y);

            // ★★ 進行中の各段の行は**確認の一式と同じ y から始める**。
            //    「確認待ち」と「進行中」は同時に成立しない位相なので、同じ場所を
            //    使ってよい（VolcanoEffectRows のクラス doc）。上下に並べると、
            //    出していないほうのぶんだけパネルに空白が残る。
            float effectTop = VolcanoConfirmRows.BlockTop;
            VolcanoEffectRows.Build(panel, ref effectTop);

            ApplyHeight(panel, VolcanoConfirmRows.BlockTop + 8f);
        }

        /// <summary>
        /// 「火山を設置する」。押すと配置ツールが起動し、地面をクリックすると
        /// **調査の依頼だけ**が積まれる（<see cref="VolcanoPlacementTool"/> のクラス doc）。
        /// **このボタンは何も壊さない。**
        /// </summary>
        private static void AddPlaceButton(UIPanel panel, ref float y)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "VolcanoPlaceButton";
            button.text = Strings.VolcanoPlace;
            button.tooltip = Strings.VolcanoPlaceHint;
            button.width = PlaceButtonWidth;
            button.height = PlaceButtonHeight;
            button.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += (c, e) => VolcanoPlacementTool.Arm();
            y += PlaceButtonHeight + 10f;
        }

        /// <summary>
        /// 高さを入れ直し、ビューに収まる位置へ寄せ直す。**必ず既定の左上へ戻してから
        /// 寄せる** —— 前回の寄せの結果から寄せ直すと、開閉のたびに上へずれていく。
        /// </summary>
        private static void ApplyHeight(UIPanel panel, float height)
        {
            if (panel == null) return;

            // ★ 比較には**自分が最後に入れた値**を使う。<c>panel.height</c> を読み返して
            //   比べると、UI 側が丸めた場合に毎フレーム「違う」と判定され、
            //   ClampToView が毎フレーム走る —— あそこには Log.Warn があるので、
            //   内容がビューより高い環境で**毎フレーム 1 行**ログを吐くことになる
            //   （Log.Warn はスロットルされない）。
            if (_appliedHeight == height) return;
            _appliedHeight = height;

            panel.height = height;
            panel.relativePosition = BasePosition;
            ClampToView(panel);
        }

        private static void AddCloseButton(UIPanel panel, float y)
        {
            var closeButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            closeButton.name = FreeSlotFinder.SelfPrefix + "VolcanoCloseButton";
            closeButton.text = "X";
            closeButton.width = 24f;
            closeButton.height = 24f;
            closeButton.relativePosition = new Vector3(VolcanoRows.PanelWidth - 32f, y);
            closeButton.normalBgSprite = "ButtonMenu";
            closeButton.hoveredBgSprite = "ButtonMenuHovered";
            closeButton.pressedBgSprite = "ButtonMenuPressed";
            closeButton.eventClick += (c, e) => Hide();
        }

        /// <summary>
        /// パネルの下端がビューからはみ出さない位置まで上げる。②④の同名メソッドと同じで、
        /// **行を足すたびにパネルは伸びる**。いちばん下の行——警告や確認のボタン——が
        /// 静かに画面外へ出るのを黙って許さない。
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
                    Log.Warn("volcano panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("volcano panel clamp failed: " + e.GetType().Name);
            }
        }

        private static void Refresh()
        {
            VolcanoRows.SetPlain(_titleLabel, Strings.VolcanoTitle);

            // 地形が書けない環境では説明の 1 行しか構築していない（_bodyBuilt の doc）。
            if (!_bodyBuilt) return;

            // ★ スナップショットは 1 フレームに 1 回だけ取る（ロックを 2 回取らない）。
            var snapshot = VolcanoHub.Latest;
            VolcanoStatusRows.Refresh(snapshot);
            VolcanoConfirmRows.Refresh(snapshot);
            VolcanoEffectRows.Refresh(snapshot);

            // 確認も進行中の行も出していないときは、そのぶんだけパネルを縮める。
            // **2 つは同じ y から始まる**ので、下端は出しているほうのものを使う
            // （VolcanoEffectRows のクラス doc）。
            float bottom = VolcanoConfirmRows.BlockTop;
            if (VolcanoConfirmRows.IsShowing) bottom = VolcanoConfirmRows.BlockBottom;
            else if (VolcanoEffectRows.IsShowing) bottom = VolcanoEffectRows.BlockBottom;

            ApplyHeight(_panel, bottom + 8f);
        }
    }
}
