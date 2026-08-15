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
    ///
    /// ── ★★ [止める] ボタン（全体レビュー I5）─────────────────────
    ///
    /// <c>VolcanoRequest.Stop</c> には完全な受け口（<c>VolcanoState.HandleStop</c>）が
    /// 最初から在ったのに、**それを積む場所が 1 つも無かった。** つまり確認を押した
    /// あとの唯一の抜け道は設定の「火山を有効にする」を切ることで、あれは
    /// <c>VolcanoFeature</c> の入口で位相ごと捨てたうえに**ボタンとパネルも同時に
    /// 消す**ので、半分削れて半分盛り上がった山だけが残り、画面には何の説明も出ない。
    /// **自分の都市が壊されているのを見ている人には、止める手段が要る。**
    ///
    /// 止まるのは「これからの破壊と隆起」だけである（<c>Strings.VolcanoStopNote</c>）。
    /// </summary>
    internal static class VolcanoEffectRows
    {
        /// <summary>説明文の行の高さ（3 行ぶん折り返す想定）。</summary>
        private const float NoteHeight = 52f;

        /// <summary>追随の遅れの行の高さ（数字 ＋ 説明文で 4 行ぶん）。</summary>
        private const float CatchUpHeight = 72f;

        /// <summary>長い注記の行の高さ（道路の断りと噴火の注記。4〜5 行ぶん）。</summary>
        private const float LongNoteHeight = 72f;

        /// <summary>[止める] ボタンの大きさ（確認の一式のボタンと揃える）。</summary>
        private const float StopButtonWidth = 240f;

        private const float StopButtonHeight = 28f;

        private static UILabel _clearingLabel;
        private static UILabel _clearingCountsLabel;
        private static UILabel _clearingRefusedNoteLabel;
        private static UILabel _clearingCappedLabel;
        private static UILabel _upliftLabel;
        private static UILabel _upliftRadiusLabel;
        private static UILabel _catchUpLabel;
        private static UILabel _craterLabel;
        private static UILabel _eruptionLabel;
        private static UILabel _eruptionNoteLabel;
        private static UILabel _lavaLabel;
        private static UILabel _lavaIgnitedLabel;
        private static UILabel _lavaTreesNoteLabel;
        private static UILabel _lavaRoadsNoteLabel;
        private static UILabel _lavaSurfaceLabel;
        private static UILabel _lavaNoMaterialLabel;
        private static UILabel _clearingPathLabel;

        // ── 進行中の火山を止める一式（全体レビュー I5）────────────────────
        private static UILabel _saveWarningLabel;
        private static UILabel _stopNoteLabel;
        private static UIButton _stopButton;

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

            // ── 噴火（T7）────────────────────────────────────
            _eruptionLabel = VolcanoRows.AddRow(p, "EffectEruption", ref y);
            // ★ 「ゲームに溶岩も噴火も無い」を名乗る注記。4 行ぶん折り返す。
            _eruptionNoteLabel =
                VolcanoRows.AddRow(p, "EffectEruptionNote", ref y, LongNoteHeight);

            // ── 溶岩（T8）────────────────────────────────────
            _lavaLabel = VolcanoRows.AddRow(p, "EffectLava", ref y);
            _lavaIgnitedLabel = VolcanoRows.AddRow(p, "EffectLavaIgnited", ref y);
            // ★ ND 非所持で木が燃えないことの説明。**不具合ではない**（§B-7c）。
            _lavaTreesNoteLabel = VolcanoRows.AddRow(p, "EffectLavaTrees", ref y, NoteHeight);
            // ★ 道路が燃えないことの説明。ゲームに API が無い（§B-7d）。
            _lavaRoadsNoteLabel = VolcanoRows.AddRow(p, "EffectLavaRoads", ref y, NoteHeight);

            // ── 溶岩の描画（T9）──────────────────────────────
            // ★ このファイルで溶岩の描画側を参照するのはこの 2 行だけである
            //   （T9 の独立性。あちらのクラス doc の grep）。
            _lavaSurfaceLabel = VolcanoRows.AddRow(p, "EffectLavaSurface", ref y);
            _lavaNoMaterialLabel = VolcanoRows.AddRow(p, "EffectLavaNoMaterial", ref y, NoteHeight);

            // ★ **道路と建物を取り除けない環境の断り**。これだけは位相に関係なく出す
            //    （設計書 §1.2）。黙って火山を作らないのがいちばん悪い。
            _clearingPathLabel = VolcanoRows.AddRow(p, "EffectClearingPath", ref y,
                                                    LongNoteHeight);

            // ★★ 進行中だけ出す一式（全体レビュー I5 / I6）。
            //    セーブの警告を**進行中にも**出すのは、そこが実際に押される瞬間だからである。
            _saveWarningLabel = VolcanoRows.AddRow(p, "EffectSaveWarning", ref y, LongNoteHeight);
            _stopNoteLabel = VolcanoRows.AddRow(p, "EffectStopNote", ref y, NoteHeight);
            _stopButton = AddStopButton(p, y);
            y += StopButtonHeight + 10f;

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
            bool clearingPathBroken = !s.ClearingPathAvailable;

            // ★ 隆起が終わった火山の実績は**終わってからも出したままにする**。
            //   位相が Done に落ちた瞬間に全部消えると、「山ができた」の結果
            //   （山頂の高さ・火口・壊した数）を読む機会が 1 度も無い。
            //   新しい火山を始めるか都市を出ると Reset で 0 に戻る。
            bool clearing = InProgress(s.Phase) || s.UpliftComplete;

            _showing = clearingPathBroken || clearing;
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
                y = RefreshEruption(y, s);
                y = RefreshLava(y, s);
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
                y = ReflowRow(y, _eruptionLabel, "");
                y = Reflow(y, _eruptionNoteLabel, "", LongNoteHeight, LongNoteHeight + 4f);
                y = ReflowRow(y, _lavaLabel, "");
                y = ReflowRow(y, _lavaIgnitedLabel, "");
                y = ReflowNote(y, _lavaTreesNoteLabel, "");
                y = ReflowNote(y, _lavaRoadsNoteLabel, "");
                y = ReflowRow(y, _lavaSurfaceLabel, "");
                y = ReflowNote(y, _lavaNoMaterialLabel, "");
            }

            y = Reflow(y, _clearingPathLabel,
                clearingPathBroken ? Strings.VolcanoClearingPathUnavailable : "",
                LongNoteHeight, LongNoteHeight + 4f);

            y = RefreshStop(y, s);

            _blockBottom = y;
        }

        /// <summary>
        /// 進行中のあいだだけ出す [止める] の一式（全体レビュー I5 / I6）。
        ///
        /// ★ **終わった火山には出さない。** <see cref="Refresh"/> は隆起が終わった
        ///   火山の実績を出したままにするので（あちらの doc）、位相そのものを見る。
        /// ★ 押した直後の 1 フレームはまだ位相が変わらない（設計上 1 tick の遅れ）。
        ///   依頼が積まれている間はボタンを畳む —— **押しても何も変わらない
        ///   ボタンは二度押される**（<see cref="VolcanoConfirmRows"/> と同じ判断）。
        /// </summary>
        private static float RefreshStop(float y, VolcanoSnapshot s)
        {
            bool stoppable = InProgress(s.Phase)
                             && VolcanoHub.PendingRequest.Kind == VolcanoRequest.None;

            // ★ セーブの警告は**山がまだ出来上がっていない間だけ**出す（全体レビュー I6）。
            //   噴火と溶岩まで来ていれば地形はもう最終形なので、そこで保存しても
            //   残るのは「噴火と溶岩を見損ねた完成した山」であって、
            //   火口の無い切り株ではない。噴火・溶岩の段はここに長い注記が 2 つ出るので、
            //   条件を絞ることでパネルが画面より高くなるのも避けている。
            bool halfBuilt = s.Phase == VolcanoPhase.Clearing
                             || s.Phase == VolcanoPhase.Uplifting;

            y = Reflow(y, _saveWarningLabel,
                halfBuilt ? Strings.VolcanoSaveWarning : "",
                LongNoteHeight, LongNoteHeight + 4f);

            y = ReflowNote(y, _stopNoteLabel, stoppable ? Strings.VolcanoStopNote : "");

            if (_stopButton == null) return y;

            _stopButton.isVisible = stoppable;
            _stopButton.isEnabled = stoppable;
            if (!stoppable) return y;

            _stopButton.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            return y + StopButtonHeight + 10f;
        }

        /// <summary>
        /// [止める]。**main スレッドからゲームのバッファに触らない** —— 依頼を積むだけで、
        /// 実際に畳むのは sim スレッドの <c>VolcanoState.HandleStop</c> である。
        /// 座標は運ばない（sim 側が持っている調査結果を使う）。
        /// </summary>
        private static UIButton AddStopButton(UIPanel panel, float y)
        {
            var button = (UIButton)panel.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "VolcanoStopButton";
            button.text = Strings.VolcanoStopButton;
            button.width = StopButtonWidth;
            button.height = StopButtonHeight;
            button.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.isVisible = false;
            button.isEnabled = false;
            button.eventClick += (c, e) =>
                VolcanoHub.Request(new VolcanoRequestData(VolcanoRequest.Stop,
                    new DisasterPlus.Core.Common.Vec3(0f, 0f, 0f)));
            return button;
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
        /// 噴火の 2 行（T7）。**噴火の段に入るまでは出さない**（隆起の 4 行と同じ扱い）。
        ///
        /// ★ 強さは <b>0〜10 の段階</b>で出す。⑤は温度も噴出量も持っておらず、
        ///   実在の物理単位を名乗ってはいけない（設計書 §7.4 /
        ///   計画「出してよい断定の範囲」の 5）。印（<c>[measured]</c>）も付かない ——
        ///   これは⑤が決めた量であって、ゲームが計算した値ではない。
        ///
        /// ★ 注記は**噴火の段のあいだだけ**出す。常に出すと、山を作っている間ずっと
        ///   「ゲームに溶岩は無い」と言い続けることになる。
        /// </summary>
        private static float RefreshEruption(float y, VolcanoSnapshot s)
        {
            // ★ T8 で位相を絞った。噴火の段のあいだだけ出す —— 溶岩が流れている間は
            //   その下の 4 行（<see cref="RefreshLava"/>）がその場所を使う。
            bool erupting = s.Phase == VolcanoPhase.Erupting;

            if (!erupting)
            {
                y = ReflowRow(y, _eruptionLabel, "");
                y = Reflow(y, _eruptionNoteLabel, "", LongNoteHeight, LongNoteHeight + 4f);
                return y;
            }

            // 0〜10 の段階。0.0 でも「1 段」と言わないよう、素直に四捨五入する。
            float stage = s.EruptionIntensityUnit * 10f;

            y = ReflowRow(y, _eruptionLabel,
                Strings.VolcanoEruptionRow + ": " + stage.ToString("F1") + " / 10");

            y = Reflow(y, _eruptionNoteLabel, Strings.VolcanoEruptionBorrowedNote,
                       LongNoteHeight, LongNoteHeight + 4f);
            return y;
        }

        /// <summary>
        /// 溶岩の 4 行（T8）。**溶岩の段に入るまでは出さない。**
        ///
        /// ★ 木の説明は <b>ND DLC を持っていないときだけ</b>出す（§B-7c）。
        ///   持っている環境で常に出すと、起きてもいない制約を毎回読ませることになる。
        /// ★ 道路の説明は溶岩の段のあいだ常に出す —— 「溶岩の下の道路が燃えない」は
        ///   必ず目に入る事実で、ゲームに API が無いことを説明できるのはここだけである。
        /// </summary>
        private static float RefreshLava(float y, VolcanoSnapshot s)
        {
            bool flowing = s.Phase == VolcanoPhase.Flowing || s.Phase == VolcanoPhase.Cooling;

            if (!flowing || s.LavaFlowCount <= 0)
            {
                y = ReflowRow(y, _lavaLabel, "");
                y = ReflowRow(y, _lavaIgnitedLabel, "");
                y = ReflowNote(y, _lavaTreesNoteLabel, "");
                y = ReflowNote(y, _lavaRoadsNoteLabel, "");
                y = ReflowRow(y, _lavaSurfaceLabel, "");
                y = ReflowNote(y, _lavaNoMaterialLabel, "");
                return y;
            }

            y = ReflowRow(y, _lavaLabel,
                Strings.VolcanoLavaRow + ": " + s.LavaAliveCount + " / " + s.LavaFlowCount
                + "    " + Strings.VolcanoLavaLongest + ": "
                + s.LavaLongestMetres.ToString("F0") + " " + Strings.VolcanoMetres);

            y = ReflowRow(y, _lavaIgnitedLabel,
                Strings.VolcanoLavaIgnited + ": " + s.LavaBuildingsIgnited + " / "
                + s.LavaTreesIgnited);

            y = ReflowNote(y, _lavaTreesNoteLabel,
                s.LavaTreesAvailable ? "" : Strings.VolcanoTreesNeedDlc);

            y = ReflowNote(y, _lavaRoadsNoteLabel, Strings.VolcanoLavaRoadsNote);

            // ★ 溶岩の描画（T9）。**設定で切っているときは行ごと出さない** ——
            //   「描いていない」と「切ってある」を混ぜない。
            bool renderOn = ModSettings.VolcanoLavaRender.value;
            y = ReflowRow(y, _lavaSurfaceLabel, renderOn
                ? Strings.VolcanoLavaRenderRow + ": " + VolcanoLavaFx.PointsDrawn
                : "");

            // マテリアルを作れなかったときだけ説明する。**流れも焦げも着火も
            //   変わらない**ことを同時に言う（Strings.VolcanoLavaNoMaterial）。
            y = ReflowNote(y, _lavaNoMaterialLabel,
                renderOn && !VolcanoLavaFx.MaterialResolved
                    ? Strings.VolcanoLavaNoMaterial : "");
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

        /// <summary>
        /// 一式まとめて出し入れする。
        ///
        /// ★ <b>この一式の行を 1 つも取りこぼさないこと。</b> T6 まで隆起の 4 行が
        ///   この列に入っておらず、位相が畳まれたフレームで**古い隆起の行が
        ///   そのまま残る**形になっていた（<see cref="Refresh"/> は <c>false</c> の枝で
        ///   すぐ return するので、空文字を入れる経路を通らない）。T7 で
        ///   噴火の 2 行を足すにあたって、隆起の 4 行もここへ入れてある。
        /// </summary>
        private static void SetVisible(bool visible)
        {
            _showing = visible;
            SetLabelVisible(_clearingLabel, visible);
            SetLabelVisible(_clearingCountsLabel, visible);
            SetLabelVisible(_clearingRefusedNoteLabel, visible);
            SetLabelVisible(_clearingCappedLabel, visible);
            SetLabelVisible(_upliftLabel, visible);
            SetLabelVisible(_upliftRadiusLabel, visible);
            SetLabelVisible(_catchUpLabel, visible);
            SetLabelVisible(_craterLabel, visible);
            SetLabelVisible(_eruptionLabel, visible);
            SetLabelVisible(_eruptionNoteLabel, visible);
            SetLabelVisible(_lavaLabel, visible);
            SetLabelVisible(_lavaIgnitedLabel, visible);
            SetLabelVisible(_lavaTreesNoteLabel, visible);
            SetLabelVisible(_lavaRoadsNoteLabel, visible);
            SetLabelVisible(_lavaSurfaceLabel, visible);
            SetLabelVisible(_lavaNoMaterialLabel, visible);
            SetLabelVisible(_clearingPathLabel, visible);
            SetLabelVisible(_saveWarningLabel, visible);
            SetLabelVisible(_stopNoteLabel, visible);

            // ★ 見えないボタンがクリックを拾える経路を残さない
            //   （<see cref="VolcanoConfirmRows"/> と同じ扱い）。
            if (_stopButton != null)
            {
                _stopButton.isVisible = visible;
                _stopButton.isEnabled = visible;
            }
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
            _upliftLabel = null;
            _upliftRadiusLabel = null;
            _catchUpLabel = null;
            _craterLabel = null;
            _eruptionLabel = null;
            _eruptionNoteLabel = null;
            _lavaLabel = null;
            _lavaIgnitedLabel = null;
            _lavaTreesNoteLabel = null;
            _lavaRoadsNoteLabel = null;
            _lavaSurfaceLabel = null;
            _lavaNoMaterialLabel = null;
            _clearingPathLabel = null;
            _saveWarningLabel = null;
            _stopNoteLabel = null;
            _stopButton = null;
            _blockTop = 0f;
            _blockBottom = 0f;
            _showing = false;
        }
    }
}
