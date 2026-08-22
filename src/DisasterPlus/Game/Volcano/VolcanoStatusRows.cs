using ColossalFramework.UI;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火山そのものの状態を出す行（状態・形態・半径・最終高と、**常設の不可逆警告**）。
    /// **main スレッド専用。**
    ///
    /// 行を作るのも文字を入れるのも <see cref="VolcanoRows"/> を通す。
    /// **このファイルに <c>UILabel</c> の生成も <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── ここが守っている 4 つの約束（設計書 §7）───────────────────────
    ///
    /// 1. **行ごとの出所の印は付けない。** ⑤の数値は原則すべて本 MOD のものである
    ///    （見出しの 2 行は 2026-08-22 に外した。<see cref="VolcanoPanel"/> の doc）。
    ///    <b>このファイルに <c>SetMeasured</c> の呼び出しは 1 つも無い</b> ——
    ///    形態も半径も最終高も、⑤が設定から決めた数字である。
    /// 2. **不可逆の警告はこのタブから外した**（2026-08-22、所有者の依頼
    ///    「細かい説明やデバッグはゲーム内では表示不要」）。
    ///    同じ文は**オプション画面の見出し**に残してあり
    ///    （<c>Mod.cs</c> の <c>Strings.VolcanoIrreversibleWarning</c>）、診断ダンプにもある。
    ///    **黙ってはいない** —— 読む場所を遊んでいる画面から外しただけである。
    /// 3. **読めない値は数字にしない。** スナップショットがまだ無いときは
    ///    <c>VolcanoWaiting</c>、読めなかったときは <c>VolcanoUnavailable</c>。
    ///    「まだ読んでいない」と「読めない」を同じ文言にしない（①②が確立した規律）。
    /// 4. **実在の物理単位を名乗らない。** ⑤が出すのは距離 (m)・高さ (m)・
    ///    ゲーム内時間・0〜10 の段階だけである（設計書 §7.5）。
    ///
    /// ── 形態のラベルは配列に入れない ─────────────────────────
    ///
    /// <see cref="FormLabel"/> は毎回 <c>switch</c> で <c>Strings</c> を読み直す。
    /// <c>static readonly string[]</c> に入れると**型初期化時の言語で凍結**し、
    /// ゲーム中に言語を切り替えても英語のまま残る（<c>Strings</c> のクラス doc）。
    /// </summary>
    internal static class VolcanoStatusRows
    {
        private static UILabel _stateLabel;
        private static UILabel _shapeLabel;

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // 「火山が居ない」「まだ読んでいない」「読めない」「調べています」
            // 「断られた理由」を全部ここに出す。**折り返す高さを取る** ——
            // refusal は英語の 1 文なので、折り返さない行に入れると理由が途中で切れる。
            _stateLabel = VolcanoRows.AddRow(p, "State", ref y, 40f);

            _shapeLabel = VolcanoRows.AddRow(p, "Shape", ref y);

            // ★★ **常設の不可逆警告は外した**（2026-08-22、所有者の依頼
            //    「細かい説明やデバッグはゲーム内では表示不要」）。
            //    内容は診断ダンプ（note: unfinished volcano ほか）と
            //    オプション画面に残っている。
        }

        /// <summary>パネル表示中に毎フレーム。<paramref name="s"/> は null でありうる。</summary>
        internal static void Refresh(VolcanoSnapshot s)
        {
            // 形態・半径・最終高はスナップショットに依らない（設定と Core の
            // クランプだけで決まる）ので、読めていないときも出せる。
            RefreshShapeRow();

            if (s == null)
            {
                // 「まだ 1 回も読んでいない」と「読んだが読めなかった」を同じ文言に
                // しない（①②④が確立した規律）。ロード直後にポーズしたままだと
                // 前者が普通に起きる。
                VolcanoRows.SetPlain(_stateLabel, Strings.VolcanoWaiting);
                return;
            }

            if (!s.Valid || !s.Terrain.Usable)
            {
                VolcanoRows.SetPlain(_stateLabel, Strings.VolcanoUnavailable);
                return;
            }

            VolcanoRows.SetPlain(_stateLabel, StateText(s));
        }

        /// <summary>
        /// 状態の 1 行。**「何も起きていない」と「起こせなかった」を見分けられるようにする。**
        ///
        /// 優先順は「依頼を出した直後（次の sim tick を待っている）」＞「進行中」＞
        /// 「理由つきで断られた」＞「ただ起きていない」。
        /// <see cref="VolcanoHub.PendingRequest"/> を見るのは
        /// **押しても何も変わらないように見えて二度押しするのを防ぐため**で、
        /// 依頼から反映までには設計上 1 tick の遅れがある（<see cref="VolcanoHub"/> の doc）。
        /// </summary>
        private static string StateText(VolcanoSnapshot s)
        {
            // 配置ツールが出ている間は、何をすればよいかを出す。**押した直後に
            // 画面が何も変わらないと、プレイヤーはボタンが壊れていると読む。**
            if (VolcanoPlacementTool.IsActive) return Strings.VolcanoPlaceHint;

            VolcanoRequest pending = VolcanoHub.PendingRequest.Kind;
            // ★ 依頼を積んでから sim が拾うまでの 1 tick。⑤はその 1 tick の中で
            //   影響範囲を数えてから壊し始めるので、ここに出るのは「調べています」である。
            if (pending == VolcanoRequest.Place) return Strings.VolcanoSurveying;
            if (pending != VolcanoRequest.None) return Strings.VolcanoWaiting;

            switch (s.Phase)
            {
                case VolcanoPhase.Idle:
                case VolcanoPhase.Done:
                case VolcanoPhase.Refused:
                    return string.IsNullOrEmpty(s.Refusal)
                        ? Strings.VolcanoInactive
                        : Strings.VolcanoInactive + "  (" + s.Refusal + ")";

                default:
                    // ★ 進行中の位相。**T5〜T8 が各段を実装するまで翻訳キーを持たない**
                    //   ので、列挙の名前（英語）をそのまま出す。何も出さないより、
                    //   どこで止まっているかが分かるほうが良い
                    //   （④の refusal を英語のまま出しているのと同じ判断）。
                    return Strings.VolcanoPhaseRow + ": " + s.Phase;
            }
        }

        /// <summary>
        /// 形態・半径・最終高の 1 行。**クランプ後の値を出す**（<c>.cgs</c> は
        /// 公開契約で手で編集されうるので、生の設定値をそのまま画面に出さない）。
        ///
        /// **天井（§C-10）による切り下げはここでは掛けない** —— それは
        /// 設置地点の地形高さが決まって初めて分かる量で、実際に置いたあとに
        /// <see cref="VolcanoEffectRows"/> の調査の行が名乗る。
        ///
        /// ★ **スライダーの倍率もここでは掛けない。** 倍率が決まるのは地図を
        ///   クリックした瞬間で（<c>Core.Volcano.VolcanoSizeScale</c>）、この行は
        ///   「設定でいま選ばれている基準の大きさ」である。倍率を掛けた実寸は
        ///   置いたあとに <see cref="VolcanoEffectRows"/> が出す。
        /// </summary>
        private static void RefreshShapeRow()
        {
            VolcanoForm form = CurrentForm();
            float radius = VolcanoShape.RadiusFor(form, ModSettings.VolcanoRadius.value);
            // 高さは形態の帯だけでクランプする（天井は地点が決まってから）。
            float height = ClampHeightToForm(form, ModSettings.VolcanoHeight.value);

            VolcanoRows.SetPlain(_shapeLabel,
                Strings.VolcanoFormRow + ": " + FormLabel(form)
                + "    " + Strings.VolcanoRadiusRow + ": " + radius.ToString("F0")
                + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoHeightRow + ": " + height.ToString("F0")
                + " " + Strings.VolcanoMetres);
        }

        /// <summary>
        /// いま選ばれている形態。範囲外の値は <c>VolcanoShape.FormOf</c> が既定へ落とす
        /// （<c>.cgs</c> は公開契約で、手で編集されうる）。
        /// </summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);
        }

        /// <summary>
        /// 形態の帯だけでクランプした高さ。**<c>VolcanoShape.HeightFor</c> を使わない** ——
        /// あちらは天井（§C-10）まで見るので、設置地点が決まっていないここで通すと
        /// 「起点 0 m」を仮定した値になる。
        /// </summary>
        private static float ClampHeightToForm(VolcanoForm form, float requested)
        {
            float min = VolcanoShape.MinHeightOf(form);
            float max = VolcanoShape.MaxHeightOf(form);
            if (float.IsNaN(requested)) return VolcanoShape.DefaultHeightOf(form);
            if (requested < min) return min;
            if (requested > max) return max;
            return requested;
        }

        /// <summary>
        /// 形態の表示名。**メソッドであることに意味がある**（クラス doc）。
        /// <c>static readonly string[]</c> にすると起動時の言語で凍る。
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
        /// レベルアンロード時。**参照を捨てるだけ**（実体はパネルの GameObject と
        /// 一緒に消える）。持ち越すと、次の都市で破棄済みのラベルに書き込む。
        /// </summary>
        internal static void Destroy()
        {
            _stateLabel = null;
            _shapeLabel = null;
        }
    }
}
