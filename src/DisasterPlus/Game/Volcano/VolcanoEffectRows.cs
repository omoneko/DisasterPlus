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
    /// ── 出所の印が付く行は 1 つだけ ─────────────────────────────
    ///
    /// 走査した半径も、壊した建物と道路の数も、**⑤が自分で数えた実績**である。
    /// ゲームが計算した値ではないので <c>VolcanoRows.SetMeasured</c> は呼ばない。
    ///
    /// ★★ <b>唯一の例外が「影響範囲」の行</b>（<see cref="RefreshFootprint"/>）である。
    /// あれは調査がゲームの配列（<c>m_buildingGrid</c> / <c>m_segmentGrid</c>）から
    /// 数えただけの値で、⑤は 1 つも計算していない。**2026-08-21 に撤去した
    /// 確認の窓が持っていた行がここへ来た** —— 窓は消えたが、
    /// 「何が巻き込まれたか」を読む場所は残す（<see cref="VolcanoRows"/> の grep 5）。
    ///
    /// ── パネルのいちばん下に置く ──────────────────────────────
    ///
    /// この一式がパネルのいちばん下であり、出していないときはパネルの高さを
    /// <see cref="BlockTop"/> まで縮める —— <c>relativePosition</c> は絶対値なので、
    /// 隠すだけでは空白が残る。
    ///
    /// ── 条件つきの注記は、当てはまらないときに場所も取らない ───────────────
    ///
    /// 空文字を入れるだけでは「何か出るはずの場所が空いている」ように見えるので、
    /// <c>Reflow</c> で毎回積み直す。
    ///
    /// ── ★★ [止める] ボタン（全体レビュー I5）─────────────────────
    ///
    /// <c>VolcanoRequest.Stop</c> には完全な受け口（<c>VolcanoState.HandleStop</c>）が
    /// 最初から在ったのに、**それを積む場所が 1 つも無かった。** つまり着手した
    /// あとの唯一の抜け道は設定の「火山を有効にする」を切ることで、あれは
    /// <c>VolcanoFeature</c> の入口で位相ごと捨てたうえに**ボタンとパネルも同時に
    /// 消す**ので、半分削れて半分盛り上がった山だけが残り、画面には何の説明も出ない。
    /// **自分の都市が壊されているのを見ている人には、止める手段が要る。**
    ///
    /// 止まるのは「これからの破壊と隆起」だけである（<c>Strings.VolcanoStopNote</c>）。
    ///
    /// ★ 確認の窓が無くなった今、**[止める] が唯一の「やめる」経路である。**
    ///
    /// ★★ **ここに置くのは「今何が起きているか」だけである**（2026-08-22）。
    ///
    /// 所有者の依頼: 「D＋タブ内の火山の細かい説明やデバッグは
    /// ゲーム内では表示不要かと思われます」。
    ///
    /// 以前は 24 行あった。その大半は**普通に動いているときの振る舞いの説明**
    /// （木に火が付かない理由、道路が燃えない理由、建てられる地面の遅れ、
    /// 火砕流の代用の断り……）と**進行中のカウンタ**（壊した数、有効半径、
    /// タイル数、描いた点の数……）で、どちらも遊んでいる最中に読むものではない。
    ///
    /// ★ <b>捨ててはいない。</b> 全部 <c>VolcanoFeature.WriteDiagnostics</c> の
    ///   診断ダンプに入っており、診断タブのボタン 1 つで書き出せる。
    ///   この MOD が禁じているのは「黙って何もしない」であって、
    ///   「遊んでいる画面に全部出す」ではない。
    ///
    /// ★ <b>残したのは 2 種類だけ</b>: 今の段の状態と、**失敗を名乗る行**である。
    ///   後者は実際に壊れているときしか出ないので、普段は 1 行も場所を取らない。
    /// </summary>
    internal static class VolcanoEffectRows
    {
        /// <summary>説明文の行の高さ（3 行ぶん折り返す想定）。</summary>
        private const float NoteHeight = 52f;

        /// <summary>長い注記の行の高さ（道路の断りと噴火の注記。4〜5 行ぶん）。</summary>
        private const float LongNoteHeight = 72f;

        /// <summary>[止める] ボタンの大きさ。</summary>
        private const float StopButtonWidth = 240f;

        private const float StopButtonHeight = 28f;

        private static UILabel _footprintLabel;
        private static UILabel _clearingLabel;
        private static UILabel _upliftLabel;
        private static UILabel _quakeLabel;
        private static UILabel _eruptionLabel;
        private static UILabel _lavaLabel;

        // 失敗を名乗る 3 行。**実際に壊れているときしか出ない。**
        private static UILabel _eruptionMissingLabel;
        private static UILabel _lavaNoMaterialLabel;
        private static UILabel _clearingPathLabel;

        private static UIButton _stopButton;

        private static float _blockTop;
        private static float _blockBottom;
        private static bool _showing;

        /// <summary>この一式が始まる y（＝出していないときのパネルの下端）。</summary>
        internal static float BlockTop { get { return _blockTop; } }

        /// <summary>この一式の下端（＝出しているときのパネルの下端）。</summary>
        internal static float BlockBottom { get { return _blockBottom; } }

        /// <summary>直近の <see cref="Refresh"/> で 1 行でも出したか。</summary>
        internal static bool IsShowing { get { return _showing; } }

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            _blockTop = y;

            // ★★ **今何が起きているかだけ**（クラス doc）。
            //    説明とカウンタは診断ダンプにある。

            // 影響範囲。調査がゲームの配列から数えた実数なので、
            // ⑤で唯一 [実測] が付く行である（<c>VolcanoRows</c> の grep 5）。
            _footprintLabel = VolcanoRows.AddMeasuredRow(p, "EffectFootprint", ref y);

            _clearingLabel = VolcanoRows.AddRow(p, "EffectClearing", ref y);
            _upliftLabel = VolcanoRows.AddRow(p, "EffectUplift", ref y);

            // ★ 火山性地震は**噴火の前（隆起）から後（冷却）まで**続くので、
            //   噴火の行より上に置く（時間の順に並べる）。
            _quakeLabel = VolcanoRows.AddRow(p, "EffectQuake", ref y);
            _eruptionLabel = VolcanoRows.AddRow(p, "EffectEruption", ref y);
            _lavaLabel = VolcanoRows.AddRow(p, "EffectLava", ref y);

            // ── 失敗を名乗る 3 行。**壊れているときしか場所を取らない** ─────
            //    この 3 つを消すと、この MOD がいちばん避けている
            //    「黙って何もしない」になる。説明を減らすのとは別の話である。
            _eruptionMissingLabel =
                VolcanoRows.AddRow(p, "EffectEruptionMissing", ref y, NoteHeight);
            _lavaNoMaterialLabel = VolcanoRows.AddRow(p, "EffectLavaNoMaterial", ref y,
                                                      NoteHeight);
            _clearingPathLabel = VolcanoRows.AddRow(p, "EffectClearingPath", ref y,
                                                    LongNoteHeight);

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
                y = RefreshFootprint(y, s);

                y = ReflowRow(y, _clearingLabel,
                    Strings.VolcanoClearingRow + ": " + Strings.VolcanoClearedRadius + " "
                    + s.ClearedRadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                    + " / " + s.Footprint.RadiusMetres.ToString("F0") + " "
                    + Strings.VolcanoMetres);

                y = RefreshUplift(y, s);
                y = RefreshQuake(y, s);
                y = RefreshEruption(y, s);
                y = RefreshLava(y, s);
            }
            else
            {
                y = ReflowMeasured(y, _footprintLabel, "");
                y = ReflowRow(y, _clearingLabel, "");
                y = ReflowRow(y, _upliftLabel, "");
                y = ReflowRow(y, _quakeLabel, "");
                y = ReflowRow(y, _eruptionLabel, "");
                y = ReflowNote(y, _eruptionMissingLabel, "");
                y = ReflowRow(y, _lavaLabel, "");
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
        ///   ボタンは二度押される。**
        /// </summary>
        private static float RefreshStop(float y, VolcanoSnapshot s)
        {
            bool stoppable = InProgress(s.Phase)
                             && VolcanoHub.PendingRequest.Kind == VolcanoRequest.None;

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
                VolcanoHub.Request(VolcanoRequestData.Of(VolcanoRequest.Stop));
            return button;
        }

        /// <summary>
        /// **影響範囲の 2 行。撤去した確認の窓が持っていた中身そのものである。**
        ///
        /// ★ 1 行目は調査がゲームの配列から数えた建物数と道路セグメント数で、
        ///   ⑤で唯一 <c>SetMeasured</c> を呼んでよい行である
        ///   （<see cref="VolcanoRows"/> のクラス doc の grep 5）。
        ///   数は**調べた瞬間のもの**なので、地面をならしている間に街が動けば
        ///   実際に消えた数とは前後する（診断ダンプの <c>note: counts</c>）。
        ///
        /// ★ 道路は**「0 本」と「数えられなかった」を混ぜない。** 数えられなかった
        ///   ときは印を付けない（読めなかった値をゲームの実測値として名乗らない）。
        ///
        /// ★ 2 行目は「結果が変わる」条件つきの注記だけ —— 走査の打ち切りと、
        ///   1024 m の天井による切り下げである。当てはまらなければ場所も取らない。
        /// </summary>
        private static float RefreshFootprint(float y, VolcanoSnapshot s)
        {
            VolcanoFootprint f = s.Footprint;
            if (!f.Valid) return ReflowMeasured(y, _footprintLabel, "");

            string body = Strings.VolcanoFootprintRow + ": "
                          + FormLabel(f.Form)
                          + "    " + Strings.VolcanoRadiusRow + " "
                          + f.RadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                          + "    " + Strings.VolcanoHeightRow + " "
                          + f.HeightMetres.ToString("F0") + " " + Strings.VolcanoMetres
                          + "    " + Strings.VolcanoBuildingsRow + " " + f.BuildingCount
                          + " / " + Strings.VolcanoSegmentsRow + " "
                          + (f.SegmentCount < 0 ? "?" : f.SegmentCount.ToString());

            // ★★ **ゲームの高さの天井（1024 m）で山頂が削られたときだけ、行に足す。**
            //    これは「普通の振る舞いの説明」ではなく、**設定した高さが
            //    そのままは届かない**という結果の違いである（クラス doc の「失敗を名乗る行」側）。
            //    天井を MOD から上げられない理由は <c>UpliftSchedule.CeilingClipped</c> の doc。
            //    行を増やさず末尾に付ける。
            if (f.HeightLimitedByCeiling) body += "   " + Strings.VolcanoHeightLimited;

            if (f.SegmentCount < 0)
            {
                // 数えられなかったので印を付けない。**読めなかった値をゲームの
                // 実測値として名乗らない。**
                y = ReflowRow(y, _footprintLabel, body + "   " + Strings.VolcanoSegmentsUnknown);
            }
            else
            {
                y = ReflowMeasured(y, _footprintLabel, body);
            }

            return y;
        }

        /// <summary>
        /// 形態の表示名。**メソッドであることに意味がある** ——
        /// <c>static readonly string[]</c> にすると起動時の言語で凍る
        /// （<c>Strings</c> のクラス doc）。
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
            if (!uplifting) return ReflowRow(y, _upliftLabel, "");

            y = ReflowRow(y, _upliftLabel,
                Strings.VolcanoUpliftRow + ": " + Strings.VolcanoUpliftProgress + " "
                + (s.ProgressUnit * 100f).ToString("F0") + "%"
                + "    " + Strings.VolcanoSummitRow + ": +"
                + s.SummitMetres.ToString("F0") + " " + Strings.VolcanoMetres
                + " / " + s.Footprint.HeightMetres.ToString("F0") + " "
                + Strings.VolcanoMetres);

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
        /// <summary>
        /// 火山性地震の 1 行。**揺れの強さ 0〜10** で、実在の震度でもマグニチュードでもない
        /// （⑤が決めた量である。設計書 §7.4 の規律）。
        ///
        /// ★ 読むのは <c>VolcanoTremorShake</c> が main スレッドで書いた値で、
        ///   このパネルも main スレッドである（<c>VolcanoPanel.Tick</c>）。
        ///
        /// ★ 設定で切っているあいだは**行ごと出さない** ——
        ///   「0 / 10」は「揺れていない」であって「切ってある」ではない。
        /// </summary>
        private static float RefreshQuake(float y, VolcanoSnapshot s)
        {
            if (!ModSettings.VolcanoQuake.value)
            {
                return ReflowRow(y, _quakeLabel, "");
            }

            float activity = VolcanoTremorShake.ActivityUnit;
            if (!(activity > 0f))
            {
                return ReflowRow(y, _quakeLabel, "");
            }

            return ReflowRow(y, _quakeLabel,
                Strings.VolcanoQuakeRow + ": " + (activity * 10f).ToString("F1") + " / 10");
        }

        private static float RefreshEruption(float y, VolcanoSnapshot s)
        {
            // ★ T8 で位相を絞った。噴火の段のあいだだけ出す —— 溶岩が流れている間は
            //   その下の 4 行（<see cref="RefreshLava"/>）がその場所を使う。
            bool erupting = s.Phase == VolcanoPhase.Erupting;

            if (!erupting)
            {
                y = ReflowRow(y, _eruptionLabel, "");
                return ReflowNote(y, _eruptionMissingLabel, "");
            }

            // 0〜10 の段階。0.0 でも「1 段」と言わないよう、素直に四捨五入する。
            float stage = s.EruptionIntensityUnit * 10f;

            y = ReflowRow(y, _eruptionLabel,
                Strings.VolcanoEruptionRow + ": " + stage.ToString("F1") + " / 10");

            // ★ 引けなかったときだけ断る。**引けている環境で毎回読ませない。**
            //   設定で切っているだけのときも出さない（「切ってある」と
            //   「この環境では出せない」を混ぜない）。
            bool fxOn = ModSettings.VolcanoEruptionFx.value;
            y = ReflowNote(y, _eruptionMissingLabel,
                fxOn && !VolcanoEruptionFx.Facts.EruptionUsable
                    ? Strings.VolcanoEffectsMissing : "");

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
                return ReflowNote(y, _lavaNoMaterialLabel, "");
            }

            y = ReflowRow(y, _lavaLabel,
                Strings.VolcanoLavaRow + ": " + s.LavaAliveCount + " / " + s.LavaFlowCount
                + "    " + Strings.VolcanoLavaLongest + ": "
                + s.LavaLongestMetres.ToString("F0") + " " + Strings.VolcanoMetres);

            // マテリアルを作れなかったときだけ説明する。**流れも焦げも着火も
            //   変わらない**ことを同時に言う（Strings.VolcanoLavaNoMaterial）。
            //   設定で描画を切っているときは出さない ——
            //   「切ってある」と「この環境では出せない」を混ぜない。
            y = ReflowNote(y, _lavaNoMaterialLabel,
                ModSettings.VolcanoLavaRender.value && !VolcanoLavaFx.MaterialResolved
                    ? Strings.VolcanoLavaNoMaterial : "");
            return y;
        }


        /// <summary>「進行中」の位相か。**壊し始めてからの 5 つ**である。</summary>
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

        /// <summary>
        /// [実測] の印が付く行。**接頭辞は <c>VolcanoRows.SetMeasured</c> が付ける**ので、
        /// ここでは空文字のときだけ素通しする（空の行に印だけが残らないこと）。
        /// </summary>
        private static float ReflowMeasured(float y, UILabel label, string body)
        {
            if (label == null) return y;
            if (string.IsNullOrEmpty(body)) return ReflowRow(y, label, "");

            VolcanoRows.SetMeasured(label, body);
            label.isVisible = true;
            label.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            label.height = VolcanoRows.RowHeight;
            return y + VolcanoRows.RowStep;
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
            SetLabelVisible(_footprintLabel, visible);
            SetLabelVisible(_clearingLabel, visible);
            SetLabelVisible(_upliftLabel, visible);
            SetLabelVisible(_quakeLabel, visible);
            SetLabelVisible(_eruptionLabel, visible);
            SetLabelVisible(_eruptionMissingLabel, visible);
            SetLabelVisible(_lavaLabel, visible);
            SetLabelVisible(_lavaNoMaterialLabel, visible);
            SetLabelVisible(_clearingPathLabel, visible);

            // ★ 見えないボタンがクリックを拾える経路を残さない
            //   （見えないボタンがクリックを拾える経路を残さない）。
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
            _footprintLabel = null;
            _clearingLabel = null;
            _upliftLabel = null;
            _quakeLabel = null;
            _eruptionLabel = null;
            _eruptionMissingLabel = null;
            _lavaLabel = null;
            _lavaNoMaterialLabel = null;
            _clearingPathLabel = null;
            _stopButton = null;
            _blockTop = 0f;
            _blockBottom = 0f;
            _showing = false;
        }
    }
}
