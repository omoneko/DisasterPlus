using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 進行中の火山の各段の状態行。**main スレッド専用。**
    /// T5 が準備の段を入れ、**T6〜T9 がこの型に段を足していく。**
    ///
    /// 行を作るのも文字を入れるのも <see cref="VolcanoRows"/> を通す。
    /// **このファイルに <c>UILabel</c> の生成も <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── 出所の印は 1 つも付かない ────────────────────────────────
    ///
    /// 走査した半径も、壊した建物と道路の数も、**⑤が自分で数えた実績**である。
    /// ゲームが計算した値ではないので <c>VolcanoRows.SetMeasured</c> は呼ばない
    /// （⑤で <c>SetMeasured</c> を呼んでよいのは <see cref="VolcanoConfirmRows"/> の
    /// 3 行だけ。<see cref="VolcanoRows"/> のクラス doc の grep 5）。
    ///
    /// ── 確認の一式と同じ場所に置く ──────────────────────────────
    ///
    /// 「確認待ち」と「進行中」は**同時に成立しない位相**なので、この一式は
    /// <c>VolcanoConfirmRows.BlockTop</c> と同じ y から始める。<c>relativePosition</c> は
    /// 絶対値なので、上下に並べると出していないほうのぶんだけ空白が残る。
    /// パネルの高さは <see cref="VolcanoPanel"/> がどちらが出ているかで選ぶ。
    ///
    /// ── 条件つきの注記は、当てはまらないときに場所も取らない ───────────────
    ///
    /// <see cref="VolcanoConfirmRows"/> と同じ <c>Reflow</c> の形。空文字を入れるだけでは
    /// 「何か出るはずの場所が空いている」ように見えるので、毎回積み直す。
    /// </summary>
    internal static class VolcanoEffectRows
    {
        /// <summary>説明文の行の高さ（3 行ぶん折り返す想定）。</summary>
        private const float NoteHeight = 52f;

        /// <summary>追随の遅れの行の高さ（数字 ＋ 説明文で 4 行ぶん）。</summary>
        private const float CatchUpHeight = 72f;

        private static UILabel _clearingLabel;
        private static UILabel _clearingCountsLabel;
        private static UILabel _clearingRefusedNoteLabel;
        private static UILabel _clearingCappedLabel;
        private static UILabel _upliftLabel;
        private static UILabel _upliftRadiusLabel;
        private static UILabel _catchUpLabel;
        private static UILabel _craterLabel;
        private static UILabel _roadPathLabel;

        private static float _blockTop;
        private static float _blockBottom;
        private static bool _showing;

        /// <summary>この一式が始まる y（＝<c>VolcanoConfirmRows.BlockTop</c> と同じ）。</summary>
        internal static float BlockTop { get { return _blockTop; } }

        /// <summary>この一式の下端（＝出しているときのパネルの下端）。</summary>
        internal static float BlockBottom { get { return _blockBottom; } }

        /// <summary>直近の <see cref="Refresh"/> で 1 行でも出したか。</summary>
        internal static bool IsShowing { get { return _showing; } }

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            _blockTop = y;

            _clearingLabel = VolcanoRows.AddRow(p, "EffectClearing", ref y);
            _clearingCountsLabel = VolcanoRows.AddRow(p, "EffectClearingCounts", ref y);
            _clearingRefusedNoteLabel =
                VolcanoRows.AddRow(p, "EffectClearingRefused", ref y, NoteHeight);
            _clearingCappedLabel = VolcanoRows.AddRow(p, "EffectClearingCapped", ref y, NoteHeight);

            // ── 隆起（T6）────────────────────────────────────
            _upliftLabel = VolcanoRows.AddRow(p, "EffectUplift", ref y);
            _upliftRadiusLabel = VolcanoRows.AddRow(p, "EffectUpliftRadius", ref y);
            // 追随の遅れの行は数字と説明を両方持つので、注記より 1 行ぶん高くする。
            _catchUpLabel = VolcanoRows.AddRow(p, "EffectCatchUp", ref y, CatchUpHeight);
            _craterLabel = VolcanoRows.AddRow(p, "EffectCrater", ref y);

            // ★ **道路を取り除けない環境の断り**。これだけは位相に関係なく出す
            //    （設計書 §1.2）。黙って火山を作らないのがいちばん悪い。
            _roadPathLabel = VolcanoRows.AddRow(p, "EffectRoadPath", ref y, 72f);

            _blockBottom = _blockTop;

            // 構築直後は何も進んでいない。**作った瞬間に隠す。**
            SetVisible(false);
        }

        /// <summary>
        /// パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。
        /// </summary>
        internal static void Refresh(VolcanoSnapshot s)
        {
            if (s == null || !s.Valid)
            {
                SetVisible(false);
                _blockBottom = _blockTop;
                return;
            }

            // 道路の経路が無い環境の断りは、位相に関係なく出す。
            bool roadPathBroken = !s.RoadPathAvailable;

            // ★ 隆起が終わった火山の実績は**終わってからも出したままにする**。
            //   位相が Done に落ちた瞬間に全部消えると、「山ができた」の結果
            //   （山頂の高さ・火口・壊した数）を読む機会が 1 度も無い。
            //   新しい火山を始めるか都市を出ると Reset で 0 に戻る。
            bool clearing = InProgress(s.Phase) || s.UpliftComplete;

            _showing = roadPathBroken || clearing;
            if (!_showing)
            {
                SetVisible(false);
                _blockBottom = _blockTop;
                return;
            }

            SetVisible(true);
            float y = _blockTop;

            if (clearing)
            {
                y = ReflowRow(y, _clearingLabel,
                    Strings.VolcanoClearingRow + ": " + Strings.VolcanoClearedRadius + " "
                    + s.ClearedRadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                    + " / " + s.Footprint.RadiusMetres.ToString("F0") + " "
                    + Strings.VolcanoMetres);

                y = ReflowRow(y, _clearingCountsLabel,
                    Strings.VolcanoBuildingsDestroyed + ": " + s.BuildingsDestroyed
                    + "    " + Strings.VolcanoSegmentsDestroyed + ": " + s.SegmentsDestroyed
                    + "    " + Strings.VolcanoClearingRefusedRow + ": " + s.BuildingsRefused);

                // ★ 0 のときは注記を出さない。**断られたものが在るときだけ**説明する
                //   （常に出すと「必ず何か残る」と読める）。
                y = ReflowNote(y, _clearingRefusedNoteLabel,
                    s.BuildingsRefused > 0 ? Strings.VolcanoClearingRefusedNote : "");

                y = ReflowNote(y, _clearingCappedLabel,
                    s.ClearingCapped ? Strings.VolcanoSurveyCapped : "");

                y = RefreshUplift(y, s);
            }
            else
            {
                y = ReflowRow(y, _clearingLabel, "");
                y = ReflowRow(y, _clearingCountsLabel, "");
                y = ReflowNote(y, _clearingRefusedNoteLabel, "");
                y = ReflowNote(y, _clearingCappedLabel, "");
                y = ReflowRow(y, _upliftLabel, "");
                y = ReflowRow(y, _upliftRadiusLabel, "");
                y = Reflow(y, _catchUpLabel, "", CatchUpHeight, CatchUpHeight + 4f);
                y = ReflowRow(y, _craterLabel, "");
            }

            y = Reflow(y, _roadPathLabel,
                roadPathBroken ? Strings.VolcanoRoadPathUnavailable : "", 72f, 76f);

            _blockBottom = y;
        }

        /// <summary>
        /// 隆起の 4 行（T6）。**準備の段のうちは出さない** —— 進捗 0 % の行は
        /// 「進んでいない」ではなく「まだその段に入っていない」だからである。
        ///
        /// ★★ <b>有効半径には必ず「準備が届いた範囲」と添える</b>
        /// （<c>Strings.VolcanoActiveRadiusRow</c> がその文言を持っている）。
        /// これが罠 1 の可視化であり、実機で「準備が止まると隆起も止まる」ことを
        /// 目で確かめられる唯一の行である。
        /// </summary>
        private static float RefreshUplift(float y, VolcanoSnapshot s)
        {
            bool uplifting = s.Phase != VolcanoPhase.Clearing;
            if (!uplifting)
            {
                y = ReflowRow(y, _upliftLabel, "");
                y = ReflowRow(y, _upliftRadiusLabel, "");
                y = Reflow(y, _catchUpLabel, "", CatchUpHeight, CatchUpHeight + 4f);
                y = ReflowRow(y, _craterLabel, "");
                return y;
            }

            y = ReflowRow(y, _upliftLabel,
                Strings.VolcanoUpliftRow + ": " + Strings.VolcanoUpliftProgress + " "
                + (s.ProgressUnit * 100f).ToString("F0") + "%"
                + "    " + Strings.VolcanoSummitRow + ": +"
                + s.SummitMetres.ToString("F0") + " " + Strings.VolcanoMetres
                + " / " + s.Footprint.HeightMetres.ToString("F0") + " "
                + Strings.VolcanoMetres);

            y = ReflowRow(y, _upliftRadiusLabel,
                Strings.VolcanoActiveRadiusRow + ": "
                + s.ActiveRadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoTilesRow + ": "
                + s.UpliftTileCursor + " / " + s.UpliftTileCount);

            y = Reflow(y, _catchUpLabel, CatchUpText(s), CatchUpHeight, CatchUpHeight + 4f);
            y = ReflowRow(y, _craterLabel, s.CraterCarved ? Strings.VolcanoCraterCarved : "");
            return y;
        }

        /// <summary>
        /// 「建てられる地面」と水位の遅れ（設計書 §7.3、§A-2 / §A-4）。**不具合ではない。**
        ///
        /// ★ 換算は <c>FeatureHost.FramesPerMinute</c> から出す。**定数を直書きしない**
        ///   （③でこれを直書きして 4 倍ずれた前科がある）。読めないときは
        ///   **フレーム数だけを出す** —— 出せない値を 0 として出さない。
        /// </summary>
        private static string CatchUpText(VolcanoSnapshot s)
        {
            int frames = s.Footprint.BlockHeightCatchUpFrames;
            if (frames <= 0) return "";

            string text = Strings.VolcanoCatchUpRow + ": " + frames + " " + Strings.VolcanoFrames;

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute > 0f)
            {
                text += " (" + (frames / framesPerMinute).ToString("F0") + " "
                        + Strings.VolcanoMinutes + ")";
            }

            return text + "  " + Strings.VolcanoBuildabilityNote;
        }

        /// <summary>
        /// 「進行中」の位相か。<see cref="VolcanoPhase.AwaitingConfirmation"/> は**含めない**
        /// —— そちらは <see cref="VolcanoConfirmRows"/> が同じ場所に出す。
        /// </summary>
        private static bool InProgress(VolcanoPhase phase)
        {
            return phase == VolcanoPhase.Clearing
                   || phase == VolcanoPhase.Uplifting
                   || phase == VolcanoPhase.Erupting
                   || phase == VolcanoPhase.Flowing
                   || phase == VolcanoPhase.Cooling;
        }

        private static float ReflowRow(float y, UILabel label, string text)
        {
            return Reflow(y, label, text, VolcanoRows.RowHeight, VolcanoRows.RowStep);
        }

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

            label.isVisible = true;
            label.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            // ★ 高さは構築時に与えたのと同じ値を渡すこと —— wordWrap は構築時の高さで
            //   決まっている（VolcanoRows.AddLabel）。
            label.height = height;
            return y + step;
        }

        private static void SetVisible(bool visible)
        {
            _showing = visible;
            SetLabelVisible(_clearingLabel, visible);
            SetLabelVisible(_clearingCountsLabel, visible);
            SetLabelVisible(_clearingRefusedNoteLabel, visible);
            SetLabelVisible(_clearingCappedLabel, visible);
            SetLabelVisible(_roadPathLabel, visible);
        }

        private static void SetLabelVisible(UILabel label, bool visible)
        {
            if (label == null) return;
            label.isVisible = visible;
        }

        /// <summary>
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。持ち越すと、次の都市で破棄済みのラベルに書き込む。
        /// </summary>
        internal static void Destroy()
        {
            _clearingLabel = null;
            _clearingCountsLabel = null;
            _clearingRefusedNoteLabel = null;
            _clearingCappedLabel = null;
            _roadPathLabel = null;
            _blockTop = 0f;
            _blockBottom = 0f;
            _showing = false;
        }
    }
}
