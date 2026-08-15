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
    /// 1. **行ごとの出所の印は付けない。** ⑤の数値は原則すべて本 MOD のもので、
    ///    それはパネルの見出し（<c>VolcanoModelNote</c>）が一度だけ名乗る。
    ///    <b>このファイルに <c>SetMeasured</c> の呼び出しは 1 つも無い</b> ——
    ///    形態も半径も最終高も、⑤が設定から決めた数字である。
    /// 2. **不可逆の警告は常に出す。** 火山が無いときも出す（設計書 §7.1）。
    ///    利用者は「地形は不可逆でよい」と判断したが、**それはプレイヤーに黙って
    ///    いてよいという意味ではない。** この行を条件付きにしないこと。
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
        private static UILabel _irreversibleLabel;

        /// <summary>パネル構築時に 1 回。行は常に作り、中身の有無で出し分ける。</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // 「火山が居ない」「まだ読んでいない」「読めない」を全部ここに出す。
            // **折り返す高さを取る** —— 折り返さない行に入れると理由が途中で切れる。
            _stateLabel = VolcanoRows.AddRow(p, "State", ref y, 40f);

            _shapeLabel = VolcanoRows.AddRow(p, "Shape", ref y);

            // ★★ 常設の不可逆警告（クラス doc の約束 2 / 設計書 §7.1）。
            //    火山が無いときも出るよう、構築時に一度入れて以後触らない。
            _irreversibleLabel = VolcanoRows.AddRow(p, "Irreversible", ref y, 40f);
            VolcanoRows.SetPlain(_irreversibleLabel, Strings.VolcanoIrreversibleWarning);
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

            // T4 が位相機械を入れるまでは、火山は構造上 1 つも存在しない。
            VolcanoRows.SetPlain(_stateLabel, Strings.VolcanoInactive);
        }

        /// <summary>
        /// 形態・半径・最終高の 1 行。**クランプ後の値を出す**（<c>.cgs</c> は
        /// 公開契約で手で編集されうるので、生の設定値をそのまま画面に出さない）。
        ///
        /// このタスクの時点では形態・半径・最終高の設定がまだ無いので、
        /// <see cref="VolcanoShape"/> の既定値を通す（T4 が設定を足して差し替える）。
        /// **天井（§C-10）による切り下げはここでは掛けない** —— それは
        /// 設置地点の地形高さが決まって初めて分かる量で、T4 の確認の行が名乗る。
        /// </summary>
        private static void RefreshShapeRow()
        {
            VolcanoForm form = CurrentForm();
            float radius = VolcanoShape.RadiusFor(form, VolcanoShape.DefaultRadiusOf(form));
            float height = VolcanoShape.DefaultHeightOf(form);

            VolcanoRows.SetPlain(_shapeLabel,
                Strings.VolcanoFormRow + ": " + FormLabel(form)
                + "    " + Strings.VolcanoRadiusRow + ": " + radius.ToString("F0")
                + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoHeightRow + ": " + height.ToString("F0")
                + " " + Strings.VolcanoMetres);
        }

        /// <summary>
        /// いま選ばれている形態。設定はまだ無いので既定（成層）。T4 が
        /// <c>ModSettings.VolcanoShapeSetting</c> から引くように差し替える。
        /// </summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoForm.Strato;
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
            _irreversibleLabel = null;
        }
    }
}
