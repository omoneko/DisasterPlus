using ColossalFramework.UI;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 「この場所に火山を作りますか」の行と、2 つのボタン。**main スレッド専用。**
    ///
    /// ★★ <b>設計書 §7.1 / §7.2 / §7.3 の 3 つの断定は全部ここに出る。</b>
    /// 行を削るときは、どの断定を落とすことになるのかを先に読むこと。
    ///
    ///   1. **地形の変更は不可逆である**（§7.1）。バニラにアンドゥは無く、
    ///      セーブに焼き付く（IL 事実文書 §E-13）。利用者は「不可逆でよい」と
    ///      判断したが、**それはプレイヤーに黙っていてよいという意味ではない。**
    ///   2. **壊される道路と建物の概数を先に見せ、概数であることも明示する**（§7.2）。
    ///      丸めるのは <see cref="ClearanceEstimate.RoundedEstimate"/>。
    ///      実数をそのまま出すとプレイヤーは「ぴったりその数だけ壊れる」と読む。
    ///   3. **建設可能判定と水位が数ゲーム内時間遅れることを説明する**（§7.3）。
    ///      <c>m_blockHeights</c> はゲームモードで上へ 2 m / 64 sim フレームしか動かず、
    ///      水シミュはその配列そのものを見ている（§A-2 / §A-4）。**不具合ではない。**
    ///
    /// ── 確認は⑤自身のパネルの中に置く（バニラのモーダルを使わない）──────────
    ///
    /// 本 MOD はモーダルを一度も出したことがなく、バニラの確認ダイアログの可用性も
    /// 引数も IL で確認していない。**モーダルが黙って出なかった場合、プレイヤーは
    /// 何も見ないまま次の操作に進む** —— それは「確認を取る」という要求（設計書 §1.3）の
    /// 失敗として最悪の形である。⑤が既に持っている <c>UIPanel</c> に行を足すのが
    /// 最も確実で、レビューでも実機でも見える。
    ///
    /// ── [measured] が付くのはここの 3 行だけである ─────────────────────
    ///
    /// 設置地点の地形高さ・範囲内の建物数・範囲内の道路数。**⑤全体で
    /// <see cref="VolcanoRows.SetMeasured"/> の呼び出しはこの 3 箇所しか無い**
    /// （<see cref="VolcanoRows"/> のクラス doc の grep 5）。
    /// **読めなかった値には印を付けない** —— 道路の本数が数えられなかったとき
    /// （<c>SegmentCount &lt; 0</c>）は <see cref="VolcanoRows.SetPlain"/> で出す。
    /// 読めなかった値をゲームの実測値として名乗らない。
    ///
    /// ── ボタンは sim へ「依頼」を積むだけ ────────────────────────
    ///
    /// <c>eventClick</c> は <see cref="VolcanoHub.Request"/> を呼ぶだけで、
    /// **main スレッドから建物・道路・地形のバッファに触らない**。
    /// </summary>
    internal static class VolcanoConfirmRows
    {
        /// <summary>ボタン 1 個の大きさ。作る・取りやめるの 2 個を横に並べる。</summary>
        private const float ActionButtonWidth = 240f;

        private const float ActionButtonHeight = 28f;

        /// <summary>説明文の行の高さ（3 行ぶん折り返す想定）。</summary>
        private const float NoteHeight = 52f;

        private static UILabel _headerLabel;
        private static UILabel _groundLabel;
        private static UILabel _shapeLabel;
        private static UILabel _buildingsLabel;
        private static UILabel _segmentsLabel;
        private static UILabel _segmentsUnknownLabel;
        private static UILabel _estimateNoteLabel;
        private static UILabel _cappedLabel;
        private static UILabel _heightLimitedLabel;
        private static UILabel _settingsChangedLabel;
        private static UILabel _irreversibleLabel;
        private static UILabel _clearingLabel;
        private static UILabel _buildabilityLabel;
        private static UIButton _yesButton;
        private static UIButton _noButton;

        private static float _blockTop;
        private static float _blockBottom;
        private static bool _showing;

        /// <summary>
        /// 確認の一式が始まる y。**確認を出していないときのパネルの下端**でもある。
        ///
        /// 行は隠しても場所を空けない（<c>relativePosition</c> は絶対値なので）。
        /// そこで確認の一式を<b>いちばん下</b>に置き、出していないときは
        /// パネルの高さをここまで縮める（<see cref="VolcanoPanel"/> の <c>Refresh</c>）。
        /// こうしないと、火山が無いときのパネルに 400 px の空白が残る。
        /// </summary>
        internal static float BlockTop { get { return _blockTop; } }

        /// <summary>確認の一式の下端（＝出しているときのパネルの下端）。</summary>
        internal static float BlockBottom { get { return _blockBottom; } }

        /// <summary>直近の <see cref="Refresh"/> で確認を出したか。</summary>
        internal static bool IsShowing { get { return _showing; } }

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            _blockTop = y;

            _headerLabel = VolcanoRows.AddSectionHeader(p, "ConfirmHeader", ref y,
                Strings.VolcanoConfirmHeader);

            // ★ ここから 3 行だけがゲームの配列から読んだ値である（クラス doc）。
            _groundLabel = VolcanoRows.AddMeasuredRow(p, "ConfirmGround", ref y);
            _shapeLabel = VolcanoRows.AddRow(p, "ConfirmShape", ref y);
            _buildingsLabel = VolcanoRows.AddMeasuredRow(p, "ConfirmBuildings", ref y);
            _segmentsLabel = VolcanoRows.AddMeasuredRow(p, "ConfirmSegments", ref y);

            _segmentsUnknownLabel = VolcanoRows.AddRow(p, "ConfirmSegmentsUnknown", ref y, NoteHeight);
            _estimateNoteLabel = VolcanoRows.AddRow(p, "ConfirmEstimateNote", ref y, NoteHeight);
            _cappedLabel = VolcanoRows.AddRow(p, "ConfirmCapped", ref y, NoteHeight);
            _heightLimitedLabel = VolcanoRows.AddRow(p, "ConfirmHeightLimited", ref y, NoteHeight);
            _settingsChangedLabel = VolcanoRows.AddRow(p, "ConfirmSettingsChanged", ref y, NoteHeight);

            // ★★ 3 つの警告。**どれも条件付きにしないこと**（クラス doc の 1〜3）。
            _irreversibleLabel = VolcanoRows.AddRow(p, "ConfirmIrreversible", ref y, NoteHeight);
            _clearingLabel = VolcanoRows.AddRow(p, "ConfirmClearing", ref y, 72f);
            _buildabilityLabel = VolcanoRows.AddRow(p, "ConfirmBuildability", ref y, NoteHeight);

            _yesButton = AddActionButton(p, "ConfirmYes", Strings.VolcanoConfirmYes,
                VolcanoRows.RowLeft, y, VolcanoRequest.Start);
            _noButton = AddActionButton(p, "ConfirmNo", Strings.VolcanoConfirmNo,
                VolcanoRows.RowLeft + ActionButtonWidth + 12f, y, VolcanoRequest.Cancel);
            y += ActionButtonHeight + 10f;

            // 構築時の並びは「最大の高さ」を測るためのもので、実際の並びは
            // Refresh が組み直す（下の Reflow）。ここでは下端の初期値だけ入れる。
            _blockBottom = _blockTop;

            // 構築直後は確認待ちではない。**作った瞬間に隠す**（Refresh が来る前の
            // 1 フレームだけ空の確認が見えるのを防ぐ）。
            SetVisible(false);
        }

        /// <summary>
        /// パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。
        ///
        /// **確認待ち以外では、行もボタンも 1 つ残らず消す。** 出したままにすると、
        /// もう存在しない調査結果に対して [作る] が押せることになる。
        ///
        /// ★ 条件つきの注記（打ち切り・天井・設定変更・道路が数えられない）は
        ///   <b>当てはまらないときに場所も取らない</b>。<c>relativePosition</c> は
        ///   絶対値なので、空文字を入れるだけでは 4 つぶんの空白が残り、
        ///   「何か出るはずの場所が空いている」ように見える。だから毎回積み直す。
        ///   <b>ラベルの生成と <c>.text</c> への代入は <see cref="VolcanoRows"/> の
        ///   ままである</b>（担保の grep はその 2 つを数えている）。
        /// </summary>
        internal static void Refresh(VolcanoSnapshot s)
        {
            // ★ 依頼が積まれている間は畳む。位相の反映には設計上 1 tick の遅れがあるので
            //   （<see cref="VolcanoSnapshot.Phase"/> の doc）、押した直後の 1 フレームだけ
            //   [作る] が押せる状態で残る。**押しても何も変わらないボタンは二度押される。**
            bool awaiting = s != null && s.Valid
                            && s.Phase == VolcanoPhase.AwaitingConfirmation
                            && s.Footprint.Valid
                            && VolcanoHub.PendingRequest.Kind == VolcanoRequest.None;

            SetVisible(awaiting);
            if (!awaiting) return;

            VolcanoFootprint f = s.Footprint;
            float y = _blockTop;

            // 見出しの飾りは VolcanoRows が付ける（あちらの SetSectionHeader）。
            VolcanoRows.SetSectionHeader(_headerLabel, Strings.VolcanoConfirmHeader);
            y = Place(y, _headerLabel, VolcanoRows.RowHeight, 26f);

            // ★★ ここから、[measured] を名乗る行が 3 つ。**共通のヘルパーにまとめない。**
            //    まとめると <c>VolcanoRows.SetMeasured</c> の呼び出しが 1 箇所になり、
            //    「印を名乗ってよいのは 3 行だけ」という担保が grep で数えられなくなる
            //    （<see cref="VolcanoRows"/> のクラス doc の grep 5）。2 行の重複は
            //    その担保の代金である。
            VolcanoRows.SetMeasured(_groundLabel,
                Strings.VolcanoGroundHeightRow + ": " + f.GroundHeightMetres.ToString("F0")
                + " " + Strings.VolcanoMetres);
            y = Place(y, _groundLabel, VolcanoRows.RowHeight, VolcanoRows.RowStep);

            y = ReflowRow(y, _shapeLabel,
                Strings.VolcanoFormRow + ": " + FormLabel(f.Form)
                + "    " + Strings.VolcanoRadiusRow + ": " + f.RadiusMetres.ToString("F0")
                + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoHeightRow + ": " + f.HeightMetres.ToString("F0")
                + " " + Strings.VolcanoMetres);

            // ★ 0 のときも行を出す。「0 棟」は「調べていない」とは違う（①②の規律）。
            VolcanoRows.SetMeasured(_buildingsLabel,
                Strings.VolcanoBuildingsRow + ": "
                + ClearanceEstimate.RoundedEstimate(f.BuildingCount));
            y = Place(y, _buildingsLabel, VolcanoRows.RowHeight, VolcanoRows.RowStep);

            // ★ 道路は**「0 本」と「数えられなかった」を混ぜない。**
            //   数えられなかったときは印を付けない（読めなかった値をゲームの実測値として
            //   名乗らない）。そして「それでも壊される」を次の行で言う。
            if (f.SegmentCount < 0)
            {
                y = ReflowRow(y, _segmentsLabel, Strings.VolcanoSegmentsRow + ": ?");
                y = ReflowNote(y, _segmentsUnknownLabel, Strings.VolcanoSegmentsUnknown);
            }
            else
            {
                VolcanoRows.SetMeasured(_segmentsLabel,
                    Strings.VolcanoSegmentsRow + ": "
                    + ClearanceEstimate.RoundedEstimate(f.SegmentCount));
                y = Place(y, _segmentsLabel, VolcanoRows.RowHeight, VolcanoRows.RowStep);
                y = ReflowNote(y, _segmentsUnknownLabel, "");
            }

            y = ReflowNote(y, _estimateNoteLabel, Strings.VolcanoEstimateNote);
            y = ReflowNote(y, _cappedLabel, f.Capped ? Strings.VolcanoSurveyCapped : "");
            y = ReflowNote(y, _heightLimitedLabel,
                f.HeightLimitedByCeiling ? Strings.VolcanoHeightLimited : "");
            y = ReflowNote(y, _settingsChangedLabel,
                s.SettingsChanged ? Strings.VolcanoSettingsChanged : "");

            // ★★ 3 つの警告。**どれも条件付きにしないこと**（クラス doc の 1〜3）。
            y = ReflowNote(y, _irreversibleLabel, Strings.VolcanoIrreversibleWarning);
            y = Reflow(y, _clearingLabel, Strings.VolcanoClearingWarning, 72f, 76f);
            y = ReflowNote(y, _buildabilityLabel, BuildabilityText(f));

            MoveButton(_yesButton, VolcanoRows.RowLeft, y);
            MoveButton(_noButton, VolcanoRows.RowLeft + ActionButtonWidth + 12f, y);
            y += ActionButtonHeight + 10f;

            _blockBottom = y;
        }

        /// <summary>折り返さない 1 行。**空なら隠して場所も取らない**（<see cref="Reflow"/>）。</summary>
        private static float ReflowRow(float y, UILabel label, string text)
        {
            return Reflow(y, label, text, VolcanoRows.RowHeight, VolcanoRows.RowStep);
        }

        /// <summary>
        /// 折り返す注記。**空なら隠して場所も取らない**（<see cref="Refresh"/> の doc）。
        /// </summary>
        private static float ReflowNote(float y, UILabel label, string text)
        {
            return Reflow(y, label, text, NoteHeight, NoteHeight + 4f);
        }

        private static float Reflow(float y, UILabel label, string text,
                                    float height, float step)
        {
            if (label == null) return y;

            VolcanoRows.SetPlain(label, text);

            if (string.IsNullOrEmpty(text))
            {
                label.isVisible = false;
                return y;
            }

            return Place(y, label, height, step);
        }

        /// <summary>
        /// 行を <paramref name="y"/> に置いて、次の行の y を返す。
        /// **高さは構築時に与えたのと同じ値を渡すこと** —— <c>wordWrap</c> は
        /// 構築時の高さで決まっているので（<see cref="VolcanoRows"/> の <c>AddLabel</c>）、
        /// ここで違う高さにすると折り返しの有無と箱の大きさが食い違う。
        /// </summary>
        private static float Place(float y, UILabel label, float height, float step)
        {
            if (label == null) return y;
            label.isVisible = true;
            label.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            label.height = height;
            return y + step;
        }

        private static void MoveButton(UIButton button, float x, float y)
        {
            if (button == null) return;
            button.relativePosition = new Vector3(x, y);
        }

        /// <summary>
        /// 「建てられる地面」と水位の遅れ（設計書 §7.3）。
        ///
        /// ★ 換算は <c>FeatureHost.FramesPerMinute</c> から出す。**定数を直書きしない**
        ///   （③でこれを直書きして 4 倍ずれた前科がある）。読めないときは
        ///   **数字を出さず注記だけにする** —— 出せない値を 0 として出さない。
        /// </summary>
        private static string BuildabilityText(VolcanoFootprint f)
        {
            string note = Strings.VolcanoBuildabilityNote;

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (f.BlockHeightCatchUpFrames <= 0 || !(framesPerMinute > 0f)) return note;

            float minutes = f.BlockHeightCatchUpFrames / framesPerMinute;
            return note + "  (" + minutes.ToString("F0") + " " + Strings.VolcanoMinutes + ")";
        }

        /// <summary>
        /// 形態の表示名。**メソッドであることに意味がある** ——
        /// <c>static readonly string[]</c> にすると起動時の言語で凍る
        /// （<c>Strings</c> のクラス doc）。
        /// </summary>
        private static string FormLabel(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return Strings.VolcanoFormShield;
                case VolcanoForm.Dome: return Strings.VolcanoFormDome;
                default: return Strings.VolcanoFormStrato;
            }
        }

        /// <summary>
        /// 確認の一式を出し入れする。**ボタンは <c>isVisible</c> だけでなく
        /// <c>isEnabled</c> も落とす** —— 見えないボタンがクリックを拾える経路を残さない。
        /// </summary>
        private static void SetVisible(bool visible)
        {
            _showing = visible;
            SetLabelVisible(_headerLabel, visible);
            SetLabelVisible(_groundLabel, visible);
            SetLabelVisible(_shapeLabel, visible);
            SetLabelVisible(_buildingsLabel, visible);
            SetLabelVisible(_segmentsLabel, visible);
            SetLabelVisible(_segmentsUnknownLabel, visible);
            SetLabelVisible(_estimateNoteLabel, visible);
            SetLabelVisible(_cappedLabel, visible);
            SetLabelVisible(_heightLimitedLabel, visible);
            SetLabelVisible(_settingsChangedLabel, visible);
            SetLabelVisible(_irreversibleLabel, visible);
            SetLabelVisible(_clearingLabel, visible);
            SetLabelVisible(_buildabilityLabel, visible);
            SetButtonVisible(_yesButton, visible);
            SetButtonVisible(_noButton, visible);
        }

        private static void SetLabelVisible(UILabel label, bool visible)
        {
            if (label == null) return;
            label.isVisible = visible;
        }

        private static void SetButtonVisible(UIButton button, bool visible)
        {
            if (button == null) return;
            button.isVisible = visible;
            button.isEnabled = visible;
        }

        private static UIButton AddActionButton(UIPanel panel, string suffix, string text,
                                                float x, float y, VolcanoRequest request)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Volcano" + suffix;
            button.text = text;
            button.width = ActionButtonWidth;
            button.height = ActionButtonHeight;
            button.relativePosition = new Vector3(x, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            // ★ main スレッドからゲームのバッファに触らない。依頼を積むだけ。
            //   座標は sim 側が持っている調査結果の中心を使うので、ここでは運ばない
            //   （VolcanoState のクラス doc）。
            button.eventClick += (c, e) =>
                VolcanoHub.Request(new VolcanoRequestData(request,
                    new DisasterPlus.Core.Common.Vec3(0f, 0f, 0f)));
            return button;
        }

        /// <summary>
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。持ち越すと、次の都市で破棄済みのラベルに書き込む。
        /// </summary>
        internal static void Destroy()
        {
            _headerLabel = null;
            _groundLabel = null;
            _shapeLabel = null;
            _buildingsLabel = null;
            _segmentsLabel = null;
            _segmentsUnknownLabel = null;
            _estimateNoteLabel = null;
            _cappedLabel = null;
            _heightLimitedLabel = null;
            _settingsChangedLabel = null;
            _irreversibleLabel = null;
            _clearingLabel = null;
            _buildabilityLabel = null;
            _yesButton = null;
            _noButton = null;
            _blockTop = 0f;
            _blockBottom = 0f;
            _showing = false;
        }
    }
}
