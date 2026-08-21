using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 「ここに火山を作りますか」だけの小さな窓。**main スレッド専用。**
    ///
    /// ── なぜ独立した窓なのか ─────────────────────────────
    ///
    /// 以前この確認は⑤の説明パネルの中にあった。所有者の依頼で⑤のタイルは
    /// **説明のパネルを開かなくなった**（バニラの災害ボタンと同じ 3 手にした）ので、
    /// 確認だけを別の窓に出す。⑤の説明と状態は左上のショートカットの側にある。
    ///
    /// ── 何を出し、何を出さないか（★ 行を足す前に読むこと） ──────────
    ///
    /// 所有者の指示は「あれこれ説明は出さなくていい」である。同時に、
    /// **⑤の確認だけは残す**（地形の変更は取り消せず、道路と建物が消える）。
    /// その 2 つを両立させる線引きがこれである:
    ///
    /// | 出す | 理由 |
    /// |---|---|
    /// | 作る山の形・半径・最終高 | スライダーで選んだ大きさの**唯一の確認手段** |
    /// | 壊される建物と道路の**数** | 依頼そのもの（「何が壊れるか＝数」） |
    /// | 取り消せないこと・保存の注意 | 取り返しがつかない唯一の事実 |
    /// | 当てはまるときだけの 1 行 × 4 | 打ち切り・天井・設定変更・ポーズ。**結果が変わる**もの |
    ///
    /// | 出さない（診断ダンプへ移した） | 理由 |
    /// |---|---|
    /// | 地点の地形高さ | 作る人の判断を変えない（テスト用の数） |
    /// | 概数の言い訳の長文 | 見出しに「（概算）」と 3 文字で書けば足りる |
    /// | なぜ壊す必要があるのか | 設計の説明であって、この場の判断ではない |
    /// | 建設可能判定と水位の遅れ | 不具合ではないことの説明。起きてから読めばよい |
    ///
    /// **落としたのは説明であって、断りではない。** どれも
    /// <c>VolcanoFeature.WriteDiagnostics</c> に同じ内容の行がある。
    ///
    /// ── ボタンは sim へ「依頼」を積むだけ ────────────────────
    ///
    /// <c>eventClick</c> は <see cref="VolcanoHub.Request"/> を呼ぶだけで、
    /// **main スレッドから建物・道路・地形のバッファに触らない**。
    /// 座標は sim 側が持っている調査結果の中心を使うので運ばない。
    /// </summary>
    internal static class VolcanoConfirmPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "VolcanoConfirmPanel";

        /// <summary>ボタン 1 個の大きさ。作る・やめるの 2 個を横に並べる。</summary>
        private const float ButtonWidth = 240f;

        private const float ButtonHeight = 28f;

        /// <summary>折り返す注記 1 行ぶんの高さ（2 行ぶん折り返す想定）。</summary>
        private const float NoteHeight = 36f;

        /// <summary>既定の左上。<see cref="ClampToView"/> が縦だけ寄せる。</summary>
        private static readonly Vector3 BasePosition = new Vector3(620f, 320f);

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _shapeLabel;
        private static UILabel _destroyLabel;
        private static UILabel _irreversibleLabel;
        private static UILabel _noteLabel;
        private static UIButton _yesButton;
        private static UIButton _noButton;

        /// <summary>最後に入れた高さ（<c>-1</c> = まだ入れていない）。</summary>
        private static float _appliedHeight = -1f;

        internal static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>
        /// main スレッドから毎フレーム。**確認待ちのときだけ窓を出す。**
        ///
        /// ★ 依頼が積まれている間は畳む。位相の反映には設計上 1 tick の遅れがあるので、
        ///   押した直後の 1 フレームだけ [作る] が押せる状態で残る。
        ///   **押しても何も変わらないボタンは二度押される。**
        /// </summary>
        internal static void Tick()
        {
            if (!ModSettings.VolcanoEnabled.value)
            {
                if (IsVisible) _panel.Hide();
                return;
            }

            VolcanoSnapshot s = VolcanoHub.Latest;
            bool awaiting = s != null && s.Valid
                            && s.Phase == VolcanoPhase.AwaitingConfirmation
                            && s.Footprint.Valid
                            && VolcanoHub.PendingRequest.Kind == VolcanoRequest.None;

            if (!awaiting)
            {
                if (IsVisible) _panel.Hide();
                return;
            }

            EnsureBuilt();
            if (_panel == null) return;

            Refresh(s);
            if (!_panel.isVisible) _panel.Show();
        }

        /// <summary>レベルアンロード時。**セッション状態を 1 つも持ち越さない。**</summary>
        internal static void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);

            _panel = null;
            _titleLabel = null;
            _shapeLabel = null;
            _destroyLabel = null;
            _irreversibleLabel = null;
            _noteLabel = null;
            _yesButton = null;
            _noButton = null;
            _appliedHeight = -1f;
        }

        private static void EnsureBuilt()
        {
            if (_panel != null) return;
            try { Build(); }
            catch (System.Exception e)
            {
                Log.Error("volcano confirm panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; volcano confirm panel not built");
                return;
            }

            // ★ _panel への代入は構築の最後の 1 行にしない。途中の例外で
            //    EnsureBuilt() の catch が呼ぶ Destroy() は _panel==null を見て何もせず、
            //    UIView に取り付け済みの GameObject が孤児のまま残る。
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("volcano confirm panel built");
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
            panel.color = new Color32(255, 255, 255, 250);
            panel.relativePosition = BasePosition;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = VolcanoRows.AddTitleRow(panel, "ConfirmTitle", VolcanoRows.RowLeft, y,
                VolcanoRows.RowWidth, 24f);
            _titleLabel.textScale = 1.1f;
            y += 30f;

            _shapeLabel = VolcanoRows.AddRow(panel, "ConfirmShape", ref y);

            // ★ ゲームの配列から数えただけの行。**⑤で印を名乗ってよいのはここだけ**
            //   （VolcanoRows のクラス doc の grep 5）。
            _destroyLabel = VolcanoRows.AddMeasuredRow(panel, "ConfirmDestroyed", ref y);

            _irreversibleLabel = VolcanoRows.AddRow(panel, "ConfirmIrreversible", ref y, NoteHeight);
            // 当てはまるときだけ出る 1 行（打ち切り・天井・設定変更・ポーズ）。
            _noteLabel = VolcanoRows.AddRow(panel, "ConfirmNote", ref y, NoteHeight);

            _yesButton = AddButton(panel, "ConfirmYes", Strings.VolcanoConfirmYes,
                VolcanoRows.RowLeft, y, VolcanoRequest.Start);
            _noButton = AddButton(panel, "ConfirmNo", Strings.VolcanoConfirmNo,
                VolcanoRows.RowLeft + ButtonWidth + 12f, y, VolcanoRequest.Cancel);
            y += ButtonHeight + 10f;

            ApplyHeight(panel, y);
        }

        private static void Refresh(VolcanoSnapshot s)
        {
            VolcanoFootprint f = s.Footprint;

            VolcanoRows.SetPlain(_titleLabel, Strings.VolcanoConfirmHeader);

            // スライダーで選んだ大きさが実際に何メートルになったか。**確認の要点である。**
            VolcanoRows.SetPlain(_shapeLabel,
                FormLabel(f.Form)
                + "    " + Strings.VolcanoRadiusRow + " " + f.RadiusMetres.ToString("F0")
                + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoHeightRow + " " + f.HeightMetres.ToString("F0")
                + " " + Strings.VolcanoMetres);

            // ★ 道路は**「0 本」と「数えられなかった」を混ぜない。** 数えられなかった
            //   ときは印を付けない（読めなかった値をゲームの実測値として名乗らない）。
            if (f.SegmentCount < 0)
            {
                VolcanoRows.SetPlain(_destroyLabel,
                    Strings.VolcanoConfirmDestroyed + ": "
                    + Strings.VolcanoBuildingsRow + " " + f.BuildingCount
                    + " / " + Strings.VolcanoSegmentsRow + " ?   "
                    + Strings.VolcanoSegmentsUnknown);
            }
            else
            {
                VolcanoRows.SetMeasured(_destroyLabel,
                    Strings.VolcanoConfirmDestroyed + ": "
                    + Strings.VolcanoBuildingsRow + " " + f.BuildingCount
                    + " / " + Strings.VolcanoSegmentsRow + " " + f.SegmentCount);
            }

            // ★★ **条件付きにしないこと。** 取り消せないことと保存の注意は常に出す。
            VolcanoRows.SetPlain(_irreversibleLabel,
                Strings.VolcanoIrreversibleWarning + " " + Strings.VolcanoSaveWarning);

            bool paused = SimulationIsPaused();
            VolcanoRows.SetPlain(_noteLabel, ConditionalNote(f, s, paused));

            // ★ ポーズ中は着手できない。**押しても何も起きないボタンは二度押される。**
            if (_yesButton != null) _yesButton.isEnabled = !paused;
        }

        /// <summary>
        /// 当てはまるときだけ出る注記。**4 つとも「結果が変わる」ものだけ**である
        /// （クラス doc の表）。当てはまらなければ空文字＝行は空になる。
        /// </summary>
        private static string ConditionalNote(VolcanoFootprint f, VolcanoSnapshot s, bool paused)
        {
            string note = "";
            if (f.Capped) note = Join(note, Strings.VolcanoSurveyCapped);
            if (f.HeightLimitedByCeiling) note = Join(note, Strings.VolcanoHeightLimited);
            if (s.SettingsChanged) note = Join(note, Strings.VolcanoSettingsChanged);
            if (paused) note = Join(note, Strings.VolcanoPausedNote);
            return note;
        }

        private static string Join(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? b : a + " " + b;
        }

        /// <summary>
        /// 形態の表示名。**メソッドであることに意味がある** ——
        /// <c>static readonly string[]</c> にすると起動時の言語で凍る。
        /// </summary>
        private static string FormLabel(DisasterPlus.Core.Volcano.VolcanoForm form)
        {
            switch (form)
            {
                case DisasterPlus.Core.Volcano.VolcanoForm.Shield: return Strings.VolcanoFormShield;
                case DisasterPlus.Core.Volcano.VolcanoForm.Dome: return Strings.VolcanoFormDome;
                default: return Strings.VolcanoFormStrato;
            }
        }

        /// <summary>
        /// シミュレーションが止まっているか。**main スレッドから読んでよい**
        /// （<c>SimulationPaused</c> は bool の読み取りで、バッファに触らない）。
        /// 読めなければ「止まっていない」に倒す —— 読めないことを理由に
        /// [作る] を押せなくすると、確認そのものが行き止まりになる。
        /// </summary>
        private static bool SimulationIsPaused()
        {
            try
            {
                if (!ColossalFramework.Singleton<SimulationManager>.exists) return false;
                return ColossalFramework.Singleton<SimulationManager>.instance.SimulationPaused;
            }
            catch
            {
                return false;
            }
        }

        private static UIButton AddButton(UIPanel panel, string suffix, string text,
                                          float x, float y, VolcanoRequest request)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Volcano" + suffix;
            button.text = text;
            button.width = ButtonWidth;
            button.height = ButtonHeight;
            button.relativePosition = new Vector3(x, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += (c, e) => VolcanoHub.Request(VolcanoRequestData.Of(request));
            return button;
        }

        /// <summary>
        /// 高さを入れ直し、ビューに収まる位置へ寄せ直す。**必ず既定の左上へ戻してから
        /// 寄せる** —— 前回の寄せの結果から寄せ直すと、開閉のたびに上へずれていく。
        /// </summary>
        private static void ApplyHeight(UIPanel panel, float height)
        {
            if (panel == null) return;

            // ★ 比較には**自分が最後に入れた値**を使う。panel.height を読み返して
            //   比べると、UI 側が丸めた場合に毎フレーム「違う」と判定される。
            if (_appliedHeight == height) return;
            _appliedHeight = height;

            panel.height = height;
            panel.relativePosition = BasePosition;
            ClampToView(panel);
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
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                Log.Warn("volcano confirm panel clamp failed: " + e.GetType().Name);
            }
        }
    }
}
