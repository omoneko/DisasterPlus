using ColossalFramework.UI;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **第 2 層の節 —— この MOD が発明した挙動だけを置く場所。** main スレッド専用。
    ///
    /// ── ここが第 1 層と混ざったら、この機能は存在価値を失う ────────────────
    ///
    /// 第 1 層（タブの中身）は全て「バニラ自身の式と定数から導いた量」で、
    /// <c>[measured]</c> の接頭辞が付く。この節に出るものは 1 つも実測ではない ——
    /// バニラは海中の地震から津波を起こさない。したがって:
    ///
    ///   - 行は必ず <see cref="EarthquakeRows.AddLayer2Row(UIPanel,string,ref float)"/> で作る
    ///     （中身は <see cref="EarthquakeRows.SetLayer2"/> でしか書けず、
    ///     <c>Strings.SourceModel</c> が必ず頭に付く）
    ///   - 節の見出し（<c>EarthquakeLayer2Header</c>）が「Disaster + が足した挙動
    ///     （バニラにはありません）」と名乗る
    ///   - 色も第 1 層と変える（ただし**色だけには頼らない**）
    ///
    /// **この節はタブの外にある。** 計画の共通規則「第 2 層は第 1 層の下に構築し、
    /// 実行時の並べ替えはしない」を、タブ化したあとも守るため
    /// （<see cref="EarthquakePanelTabs"/> の doc）。どのタブを見ていても、
    /// 本 MOD が足した挙動は常に同じ場所に、第 1 層の全内容より下に見えている。
    ///
    /// ── 設定が OFF なら節ごと消える ───────────────────────────
    ///
    /// 第 2 層は全て既定 OFF である。OFF のときに見出しだけ残すと
    /// 「何かを足しているが今は何も出ていない」に見えるので、**節ごと畳んで
    /// パネルの高さもその分縮める**（<see cref="EarthquakePanel.Relayout"/>）。
    /// 高さの組み直しは状態が変わったフレームだけで、毎フレームは走らない。
    ///
    /// ── 文言（できていないことをできているように書かない） ──────────────
    ///
    /// <c>TsunamiAI</c> は**マップ外周からしか波を出せない**（IL 事実文書 §B-3。
    /// <c>FindSea</c> は外周セルしか候補にせず、<c>m_targetPosition</c> も
    /// <c>m_angle</c> も開始時に上書きされる）。したがって
    /// <c>EarthquakeTsunamiFromShore</c> は「波は震源そのものからではなく、
    /// 震源に最も近い海から到達します」と書く。**「震源から波が広がります」ではない。**
    /// </summary>
    internal static class EarthquakeLayer2Rows
    {
        private static UILabel _headerLabel;
        private static UILabel _tsunamiLabel;
        private static UILabel _tsunamiNoteLabel;

        private static bool _built;
        private static bool _visible;
        private static float _sectionTop;
        private static float _sectionHeight;

        /// <summary>節が始まる y（＝第 1 層の下端）。パネルの高さの計算に使う。</summary>
        internal static float SectionTop { get { return _sectionTop; } }

        /// <summary>今この節が占めている高さ。畳んでいるときは 0。</summary>
        internal static float VisibleHeight { get { return _visible ? _sectionHeight : 0f; } }

        /// <summary>
        /// パネル構築時に 1 回。**行は常に作る**（設定は実行中に変わるので、
        /// あとから作れる仕組みを持つより、作って隠す方が単純で壊れにくい）。
        /// <paramref name="y"/> は、節が畳まれているなら進めない。
        /// </summary>
        internal static void Build(UIPanel root, ref float y)
        {
            _sectionTop = y;

            _headerLabel = EarthquakeRows.AddSectionHeader(root, "Layer2Header", ref y,
                Strings.EarthquakeLayer2Header);

            // ★ **2 行ぶんの高さを取る（＝折り返させる）。** 予定時刻の 1 行だけなら
            //    1 行で足りるが、同じラベルには「十分な広さの海がマップ外周に無いため…」
            //    （英語で約 110 文字）も入る。折り返さない行に入れると**途中で切れて
            //    消える** —— しかもいちばん切れてほしくない、「これは失敗ではない」を
            //    説明している文である。
            _tsunamiLabel = EarthquakeRows.AddLayer2Row(root, "Tsunami", ref y, 42f);

            // 「波がどこから来るか」の常設の説明。**観測値ではない**ので接頭辞を付けない
            // （Strings.EarthquakeSensorEffect / EarthquakeOverlayLegend と同じ扱い）。
            // 予定・発生のときだけ出す —— 波が来ないと分かっているとき（NoSea / DLC 無し）に
            // 「波は…から到達します」を残すと、来ない波の到達方向を説明することになる。
            _tsunamiNoteLabel = EarthquakeRows.AddPlainRow(root, "TsunamiNote", ref y, "", 56f);

            _sectionHeight = y - _sectionTop;
            _built = true;

            _visible = ShouldShow();
            ApplyVisibility();
            if (!_visible) y = _sectionTop;
        }

        /// <summary>レベルアンロード時。参照を捨てるだけ（実体はパネルごと消える）。</summary>
        internal static void Destroy()
        {
            _headerLabel = null;
            _tsunamiLabel = null;
            _tsunamiNoteLabel = null;
            _built = false;
            _visible = false;
            _sectionTop = 0f;
            _sectionHeight = 0f;
        }

        /// <summary>
        /// main スレッドから毎フレーム。<paramref name="snapshot"/> が null または
        /// 無効なら値の行だけを空にする（節そのものは設定で決まる）。
        /// </summary>
        internal static void Refresh(EarthquakeSnapshot snapshot)
        {
            if (!_built) return;

            bool show = ShouldShow();
            if (show != _visible)
            {
                _visible = show;
                ApplyVisibility();
                // 高さが変わったフレームだけ。毎フレーム位置を書き換えない。
                EarthquakePanel.Relayout();
            }
            if (!show) return;

            if (snapshot == null || !snapshot.Valid)
            {
                EarthquakeRows.SetPlain(_tsunamiLabel, "");
                EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");
                return;
            }

            RefreshTsunamiRow(snapshot);
        }

        /// <summary>
        /// 第 2 層の節を出すか。**今のところ津波連鎖の設定だけ**で、
        /// Task 10（長周期地震動）と Task 11（時間帯係数）が足す設定を
        /// ここに OR していくこと。1 つでも ON なら節ごと出す。
        /// </summary>
        private static bool ShouldShow()
        {
            return ModSettings.EarthquakeTsunamiChain.value;
        }

        private static void ApplyVisibility()
        {
            SetVisible(_headerLabel);
            SetVisible(_tsunamiLabel);
            SetVisible(_tsunamiNoteLabel);
        }

        private static void SetVisible(UILabel label)
        {
            if (label != null) label.isVisible = _visible;
        }

        /// <summary>
        /// 状態ごとの 1 行。**「震源から波が広がる」とは絶対に書かない**（クラス doc）。
        ///
        /// <c>NoSea</c> は**失敗ではない**。内陸マップでは海側外周区間が 10 セルに満たず、
        /// 何も起きないのが正常な結果である（§B-3）。だから理由を書いて終わりにする。
        /// <c>Failed</c>（災害スロット満杯など）だけは行を出さない —— あれは
        /// プレイヤーが何かできる状態ではなく、原因は診断ダンプにしか意味がない。
        /// </summary>
        private static void RefreshTsunamiRow(EarthquakeSnapshot snapshot)
        {
            // 注記は「これから波が来る」ときだけ添える（既定は消す）。
            EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");

            switch (snapshot.TsunamiState)
            {
                case TsunamiChainState.Scheduled:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, PendingText(snapshot));
                    return;

                case TsunamiChainState.Raised:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeTsunamiRaised
                        + "   (#" + snapshot.TsunamiQuakeId + ")");
                    return;

                case TsunamiChainState.NoSea:
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeTsunamiNoSea);
                    return;

                case TsunamiChainState.NoDlc:
                    // 新しいキーは増やさない（計画 Step 5）。
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeNeedsDlc);
                    return;

                default:
                    // Idle（陸の震源を含む）と Failed。行を出さない。
                    EarthquakeRows.SetPlain(_tsunamiLabel, "");
                    return;
            }
        }

        /// <summary>
        /// 残り時間。**換算は必ず <see cref="FeatureHost.FramesPerMinute"/> から出す**
        /// （定数を直書きして 4 倍ずれた前科がある）。換算できないときや予定が過ぎている
        /// ときは、数字の代わりに「予定」とだけ言う —— 負の残り時間を出さない。
        /// </summary>
        private static string PendingText(EarthquakeSnapshot snapshot)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f || snapshot.TsunamiDueFrame <= snapshot.CurrentFrame)
            {
                return Strings.EarthquakeTsunamiPending + "   (#" + snapshot.TsunamiQuakeId + ")";
            }

            float minutes = (snapshot.TsunamiDueFrame - snapshot.CurrentFrame) / framesPerMinute;
            return Strings.EarthquakeTsunamiPending + ": " + minutes.ToString("F0") + " "
                   + Strings.EarthquakeMinutes + "   (#" + snapshot.TsunamiQuakeId + ")";
        }
    }
}
