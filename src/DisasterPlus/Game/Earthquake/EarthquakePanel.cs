using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地震パネル。**main スレッド専用。**
    ///
    /// 枠組みは①の <see cref="ForecastPanel"/> をそのまま踏襲する
    /// （<c>UIView.AddUIComponent(Type)</c> の非総称オーバーロード、構築途中の例外で
    /// 孤児 GameObject を残さない try/catch、<c>backgroundSprite = "MenuPanel2"</c>、
    /// <c>Camera.main</c> のキャッシュ）。②が新しく導入するのは**層の分離機構**だけ。
    ///
    /// ── 層の分離（本機能の中核的な誠実さの担保） ──────────────────
    ///
    /// 第 1 層 = バニラ自身の式と定数だけから導いた量。第 2 層 = 本 MOD が発明した物理。
    /// この 2 つを混ぜたら、この機能は存在価値を失う。担保の実体は
    /// <see cref="EarthquakeRows"/> に移してある（ラベルを作れるのも <c>.text</c> を
    /// 書けるのもあの型の中だけ）。**このファイルには <c>UILabel</c> の生成も
    /// <c>.text</c> への代入も 1 つも無い。**
    ///
    /// ── 縦の予算とタブ（本タスクの構造変更） ────────────────────
    ///
    /// 第 1 層を作り終えた時点でパネルは約 1050 まで積み上がっており、UIView の
    /// 高さ 1080 に対して余地がほぼ無かった。第 2 層は 3 つの節（Task 9〜11）を
    /// 足すので、そのまま行を追記すれば説明文が画面外へ出る。
    /// 第 1 層の内容を 2 枚のタブに分け（<see cref="EarthquakePanelTabs"/>）、
    /// **第 2 層はタブの外・その下**に置くことで縦を作った。
    /// タブを選んだ理由と他案を捨てた理由は <see cref="EarthquakePanelTabs"/> の doc。
    ///
    /// 行の中身は 1 文字も変えていない。文中の「上の…」「下の…」という参照が
    /// 壊れないよう、**互いを参照し合う行は同じタブに置いてある**:
    ///   - <c>EarthquakeShakeNote</c> が「上の破壊半径」と言う → 破壊係数の行と同じタブ
    ///   - <c>EarthquakeCursorModelsNote</c> が「上の破壊係数」「下のオーバーレイ」と
    ///     言う → その 3 つが同じタブ
    ///   - <c>EarthquakeOverlayLegend</c> が「上のバーと同じ 10 段」と言う → 同上
    ///
    /// ── ハザードマップが空なのは「安全」ではない ────────────────────
    ///
    /// 地震のハザードマップは①の嵐と**バイト単位で同じ 2 段ゲート**を持つ
    /// （<c>Located(4096)</c> と <c>Emerging|Active(12)</c>、IL 事実文書 §A-6）。
    /// そして地震に <c>Located</c> を立てられるのは**地震計だけ**（§A-2 / §C-2）。
    /// つまり地震計を建てていない都市では、地震が起きていてもこのビューは
    /// **恒久的に真っ白**で、それが正常な状態である。
    ///
    /// ①はこの前提を取り違えて全ゼロのグリッドを「落雷: 0」と表示しており、
    /// 全体レビューで「誤ったラベルではなく、誤った前提から到達した
    /// 『確信を持って誤った数値』」と判定されて作り直された。②はその**修正後**の
    /// 挙動を写す: 塗られている地震が 1 つも無いときは**数値を一切出さず、
    /// 空である理由と、それを変えるのは地震計であることを書く**
    /// （<c>Strings.EarthquakeNotLocated</c>）。
    ///
    /// ── 全体円盤と断層帯は別のモデル ──────────────────────────
    ///
    /// バニラは 1 ステップに 2 種類の破壊を走らせる（§A-3）。全体円盤は
    /// <c>probability = 0.02</c> の線形ランプ（＝<see cref="SeismicIntensity"/> の s）、
    /// 断層 4 円盤は <c>probability = 1</c> で毎ステップ位置が振り直される。
    /// **s の高い状態として断層帯を出してはいけない。** 別行にし、
    /// 「当たりうる範囲であって当たる場所ではない」注記を必ず添える
    /// （<see cref="EarthquakeDamageRows"/>）。
    /// </summary>
    public static class EarthquakePanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "EarthquakePanel";

        private static UIPanel _panel;
        private static EarthquakePanelTabs _tabs;
        private static UILabel _titleLabel;
        private static UILabel _countLabel;
        private static UILabel _intensityLabel;
        private static UILabel _phaseLabel;
        private static UILabel _timeLabel;
        private static UILabel _cursorLabel;
        private static UILabel _shakeLabel;

        /// <summary>
        /// 地震の行を構築したか。Natural Disasters DLC が無い環境では構築せず、
        /// 代わりに理由を 1 行出す（<see cref="Strings.EarthquakeNeedsDlc"/>）。
        ///
        /// ①の <c>ForecastPanel._hazardRowsBuilt</c> と同じ扱い。DLC が無ければ
        /// <c>EarthquakeAI</c> の DisasterInfo プレハブも地震計も存在しないので、
        /// 地震は原理的に 1 個も起きない。にもかかわらず <c>Assumptions</c> の
        /// 型の存在検査は通ってしまう（AI の**型**は DLC の有無に関わらず
        /// Assembly-CSharp に同梱されている）ので、起動ログにもヒントは出ない。
        /// </summary>
        private static bool _bodyBuilt;

        /// <summary>
        /// 直近に <see cref="EarthquakeHub.PublishCursor"/> へ渡した有効フラグ。
        ///
        /// パネルが閉じている間に「無効」を毎フレーム publish し直す意味は無いので、
        /// 状態が変わるときだけロックを取る。**逆に、無効になったことは必ず 1 回
        /// 伝える** —— 伝えないと sim 側は最後に見た座標を永久に調べ続け、
        /// 閉じたパネルのために毎 tick 建物グリッドを走査することになる。
        /// </summary>
        private static bool _cursorPublishedValid;

        /// <summary>左上。既定値は <see cref="InfoHub"/> が位置を決める前だけ使う。</summary>
        private static Vector3 _origin = new Vector3(600f, 150f);

        public static bool IsVisible { get { return _panel != null && _panel.isVisible; } }

        /// <summary>このパネルの幅。<see cref="InfoHub"/> がタブ帯の幅を合わせるために読む。</summary>
        internal static float Width { get { return EarthquakeRows.PanelWidth; } }

        /// <summary>
        /// 左上を決める。**位置を決める主体は <see cref="InfoHub"/> 1 つだけである**
        /// （<c>DisasterPanelBar</c> のクラス doc「位置を決める主体が複数ある限り、
        /// この事故は形を変えて何度でも起きる」と同じ規律）。
        /// ここで座標を発明しないこと。
        /// </summary>
        internal static void MoveTo(Vector3 origin)
        {
            _origin = origin;
            if (_panel == null) return;
            _panel.relativePosition = _origin;
            ClampToView(_panel);
        }

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
            // 閉じた瞬間に sim 側の建物走査を止める。
            PublishCursor(new Vec3(0f, 0f, 0f), false);
            // ★ 震度分布オーバーレイも一緒に消す。凡例はこのパネルの中にしか
            //    無いので、パネルを閉じたまま絵だけが地図に残ると
            //    「何の量を見ているのか」を名乗るものが画面から消える
            //    （EarthquakeOverlay.Disable の doc）。
            EarthquakeOverlay.Disable();
        }

        /// <summary>main スレッドから毎フレーム。表示中のときだけ内容を更新する。</summary>
        public static void Tick()
        {
            // 設定で無効化されたときにパネルが開いたままだと、OnSimulationTick が
            // publish を止めた古いスナップショットを永遠に出し続ける「凍りついたのに
            // 生きて見える」パネルになり、閉じる手段のボタンも既に撤去済みで消せない
            // （①のレビュー指摘）。ボタン側と同じガードをここにも置く。
            if (!ModSettings.EarthquakeEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible)
            {
                PublishCursor(new Vec3(0f, 0f, 0f), false);
                return;
            }
            Refresh();
        }

        /// <summary>
        /// カーソル座標を sim 側へ渡す。**この 1 箇所からしか publish しない。**
        /// 状態が変わらない「無効」の連投は握り潰す（<see cref="_cursorPublishedValid"/>）。
        /// </summary>
        private static void PublishCursor(Vec3 pos, bool valid)
        {
            if (!valid && !_cursorPublishedValid) return;
            _cursorPublishedValid = valid;
            EarthquakeHub.PublishCursor(pos, valid);
        }

        /// <summary>レベルアンロード時。**セッション状態を 1 つも持ち越さない。**</summary>
        public static void Destroy()
        {
            // ★ パネルの GameObject より先に。Texture2D は Component ではないので
            //    親を Destroy しても道連れにならず、都市を読み込み直すたびに 1 枚ずつ
            //    残る（③で実際に起きた「都市をまたいで静的キャッシュが腐る」形の
            //    リーク版）。WaveformView.Destroy() が自分で Object.Destroy する。
            WaveformView.Destroy();
            // 参照を捨てるだけ。実体はパネルの GameObject と一緒に消える。
            EarthquakeMapRows.Destroy();
            EarthquakeDamageRows.Destroy();
            EarthquakeSensorRows.Destroy();
            EarthquakeLayer2Rows.Destroy();
            // 凡例ごとパネルが消えるので、絵も消す（Hide() と同じ理由）。
            EarthquakeOverlay.Disable();

            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _tabs = null;
            _titleLabel = null;
            _countLabel = null;
            _intensityLabel = null;
            _phaseLabel = null;
            _timeLabel = null;
            _cursorLabel = null;
            _shakeLabel = null;
            _bodyBuilt = false;
            _cursorPublishedValid = false;
            // 次の都市が前の都市のカーソル地点を 1 回でも返さないようにする。
            EarthquakeCursorPicker.Reset();
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
                Log.Error("earthquake panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; earthquake panel not built");
                return;
            }

            // ①のレビュー指摘の再発防止: _panel への代入を構築の最後の 1 行にすると、
            // 途中の例外で EnsureBuilt() の catch が呼ぶ Destroy() は _panel==null を見て
            // 何もせず、UIView に取り付け済みの GameObject が孤児のまま残る
            // （クリックのたびに 1 枚ずつ積み上がる）。
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("earthquake panel built");
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
            panel.width = EarthquakeRows.PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // 位置は InfoHub が決める（MoveTo）。ここには既定値しか無い ——
            // パネルは同時に 1 枚しか出ないので、互いに避ける座標はもう要らない。
            panel.relativePosition = _origin;
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = EarthquakeRows.AddTitleRow(panel, "Title", 10f, y,
                EarthquakeRows.PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            // ★ 閉じるボタンはここには無い。**タブ帯の X が 1 つだけ持つ**（InfoHub）。
            y += 30f;

            // DLC が無い環境では地震そのものが存在しない。行を出さずに理由を書く。
            _bodyBuilt = ModCompat.NaturalDisastersOwned;
            if (!_bodyBuilt)
            {
                EarthquakeRows.AddPlainRow(panel, "NeedsDlc", ref y, Strings.EarthquakeNeedsDlc, 36f);
                panel.height = y;
                return;
            }

            _tabs = new EarthquakePanelTabs(panel, ref y);

            var quakePage = _tabs.AddPage("Quake", Strings.EarthquakeTabQuake);
            float pageY = 0f;
            BuildQuakePage(quakePage, ref pageY);
            _tabs.FinishPage(quakePage, pageY);

            var damagePage = _tabs.AddPage("Damage", Strings.EarthquakeTabDamage);
            pageY = 0f;
            BuildDamagePage(damagePage, ref pageY);
            _tabs.FinishPage(damagePage, pageY);

            _tabs.Finish(ref y);

            // ★ 第 2 層（Task 9〜11）はタブの**外**、ここから下に足す
            //    （計画の共通規則「第 2 層は第 1 層の下に構築し、実行時の
            //    並べ替えはしない」）。どのタブを見ていても同じ場所に見える。
            EarthquakeLayer2Rows.Build(panel, ref y);

            panel.height = y;
            ClampToView(panel);
        }

        /// <summary>
        /// パネルの高さを組み直す。第 2 層の節は設定で丸ごと消えるので
        /// （<see cref="EarthquakeLayer2Rows"/>）、その切り替えの瞬間だけここを通る。
        /// **毎フレーム呼んではいけない** —— <see cref="ClampToView"/> は
        /// <c>relativePosition</c> を書き換えるので、毎フレーム走らせるとパネルが
        /// 微妙に動き続ける。
        /// </summary>
        internal static void Relayout()
        {
            if (_panel == null || !_bodyBuilt) return;
            _panel.height = EarthquakeLayer2Rows.SectionTop + EarthquakeLayer2Rows.VisibleHeight;
            ClampToView(_panel);
        }

        /// <summary>
        /// タブ 1「地震・カーソル・地図」。震源そのものの値と、カーソル 1 点について
        /// 2 つのモデルが出す数字、そして 2 種類の地図。
        ///
        /// この並びは**注記の文面が要求している**。<c>EarthquakeShakeNote</c> は
        /// 「上の破壊半径は別の量」と言い、<c>EarthquakeCursorModelsNote</c> は
        /// 「上の破壊係数」と「下のオーバーレイ」の両方を指し、
        /// <c>EarthquakeOverlayLegend</c> は「上のバーと同じ 10 段」と言う。
        /// これらを別のタブへ割ると、注記が存在しない行を指すことになる。
        /// </summary>
        private static void BuildQuakePage(UIPanel p, ref float y)
        {
            // ★★ **常設の説明は外した**（2026-08-22、所有者の依頼
            //    「ゲーム性にかかわるところ以外は不要です」）。
            //    内容は <c>EarthquakeFeature.WriteDiagnostics</c> の診断ダンプにある。

            EarthquakeRows.AddSectionHeader(p, "Layer1Header", ref y, Strings.EarthquakeLayer1Header);

            _countLabel = EarthquakeRows.AddLayer1Row(p, "Count", ref y);
            _intensityLabel = EarthquakeRows.AddLayer1Row(p, "Intensity", ref y);
            _phaseLabel = EarthquakeRows.AddLayer1Row(p, "Phase", ref y);
            _timeLabel = EarthquakeRows.AddLayer1Row(p, "TimeToShock", ref y);
            _cursorLabel = EarthquakeRows.AddLayer1Row(p, "AtCursor", ref y);

            // ★ 揺れは倒壊ランプとは別の量である（§A-7）。以前は s の行が
            //    「カーソル地点の揺れ」を名乗り、半径 R の外を「揺れていない」と
            //    書いていたが、バニラの揺れの式には半径の打ち切りが無く、同じ
            //    フレームで CameraShakeBooster は揺れを足し、SeismographRecorder は
            //    非ゼロの変位を書き続けている。3 つの部品が同じ物理量について
            //    食い違う主張をしていたので、揺れは揺れとして別行で出す。
            _shakeLabel = EarthquakeRows.AddLayer1Row(p, "ShakeAtCursor", ref y);

            EarthquakeMapRows.Build(p, ref y);
        }

        /// <summary>
        /// タブ 2「建物の被害・地震計」。断層帯と建物ごとの余裕度、地震計の効果、波形。
        ///
        /// **見出しはタブ 1 と同じ「ゲームが実際に計算しているもの」を再掲する。**
        /// 両方のタブが第 1 層であることを、どちらを開いても名乗らせるため。
        /// </summary>
        private static void BuildDamagePage(UIPanel p, ref float y)
        {
            EarthquakeRows.AddSectionHeader(p, "Layer1HeaderDamage", ref y,
                Strings.EarthquakeLayer1Header);

            EarthquakeDamageRows.Build(p, ref y);
            EarthquakeSensorRows.Build(p, ref y);
        }

        /// <summary>
        /// パネルの下端がビューからはみ出さない位置まで上げる。
        ///
        /// **行を足すたびにパネルは伸びる**。位置を決め打ちのままにしておくと、
        /// いちばん下の行——注記や「空である理由」——が静かに画面外へ出る。
        /// **説明を書いたのに読めない**のは、書いていないのと同じかそれより悪い。
        ///
        /// IL 実測: <c>ColossalFramework.UI.UIView.fixedHeight</c> は
        /// <c>Int32</c> の読み書き可能プロパティとして実在する（既定 1080、
        /// <c>relativePosition</c> と同じ正規化座標系）。読めない環境や
        /// 内容がビューより高い場合は上端に寄せる。
        ///
        /// **タブ導入後もこの警告は残す。** タブは縦の余裕を作ったが、行の高さは
        /// 折り返しの実測ができないまま予約しているので、言語やフォント次第では
        /// 依然としてはみ出しうる。黙って切れさせない。
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
                    // ★ ここに来たら**内容がビューより高い** ＝ 上端に寄せても
                    //    いちばん下の行が画面外に出る。
                    //    **黙って切れさせない。** 構築時の 1 回だけなのでスロットル不要。
                    top = Margin;
                    Log.Warn("earthquake panel is taller than the view ("
                             + panel.height.ToString("F0") + " > " + viewHeight.ToString("F0")
                             + "); the bottom rows will be off-screen");
                }
                // ★★ **上へは <c>InfoHub</c> が指定した位置（＝タブ帯の真下）より上に出さない。**
                //    （2026-08-22、実機報告「天気タブ・地震タブの中に X で閉じられない
                //    タブがあり」の正体。）上の 2 つの寄せは下端を画面に収めるためだけに
                //    パネルを上へ上げるので、背の高いパネルは**タブ帯をまるごと覆い隠して
                //    いた** —— 閉じる手段そのものが押せなくなる。収まらないぶんは下へはみ出すが、
                //    帯の左端を掴めば一緒に動かせる（<c>InfoHub</c> のドラッググリップ）。
                if (top < _origin.y) top = _origin.y;
                panel.relativePosition = new Vector3(pos.x, top);
            }
            catch (System.Exception e)
            {
                // 位置の微調整で構築を失敗させない（構築時の 1 回だけなのでスロットル不要）。
                Log.Warn("earthquake panel clamp failed: " + e.GetType().Name);
            }
        }

        // ── 内容の更新 ────────────────────────────────────

        private static void Refresh()
        {
            EarthquakeRows.SetPlain(_titleLabel, Strings.EarthquakeTitle);

            // DLC が無い環境では説明の 1 行しか構築していない（_bodyBuilt の doc）。
            if (!_bodyBuilt) return;

            var snapshot = EarthquakeHub.Latest;
            if (snapshot == null || !snapshot.Valid)
            {
                // 読めていない間は sim 側に建物を探させない。
                PublishCursor(new Vec3(0f, 0f, 0f), false);
                ClearQuakeRows();
                EarthquakeDamageRows.Clear();
                EarthquakeSensorRows.ClearSensor();
                EarthquakeSensorRows.ClearWaveform();
                // 「まだ 1 回も読んでいない」と「読んだが読めなかった」を同じ文言に
                // しないこと（①のレビュー指摘）。ロード直後にポーズしたままだと
                // 前者が普通に起きる（最初の tick の deltaMinutes は必ず 0）。
                EarthquakeRows.SetPlain(_countLabel, snapshot == null
                    ? Strings.ForecastWaiting
                    : Strings.EarthquakeUnavailable);
                // ボタンの見た目だけは実状に合わせる（凡例は消さない）。
                EarthquakeMapRows.ShowUnavailable();
                EarthquakeLayer2Rows.Refresh(snapshot);
                return;
            }

            // カーソル地点は 1 フレームに 1 回だけ求める。地形をかすめて外すレイでは
            // 501 回の高さサンプリングが走るので、同じフレームで 2 回引いてはいけない
            // （強度の行・ハザードの行・地震計の行で共有する）。実際にレイを引くのは
            // 実際にレイを引くのは数フレームに 1 回だけで、残りのフレームは
            // 直前の結果を返す（EarthquakeCursorPicker のクラス doc）。
            //
            // **地震が 1 個も無くても引く。** カーソル地点の地震計カバレッジは
            // 「この場所に地震計が届いているか」であって、地震の有無とは関係が無い
            // （設計書 §3.4）。地震計を建てる場所の下見に使えることがこの行の値打ちで、
            // 地震が起きている間しか読めないなら下見にならない。
            // Task 6 の間引きが入るまでは、この「常に引く」は許容できなかった。
            bool hazardViewOn =
                InfoModeSwitch.IsShowingHazardFor(InfoManager.SubInfoMode.EarthquakeHazard);
            Vec3 cursor;
            bool haveCursor = EarthquakeCursorPicker.TryPick(out cursor);

            // ★ 建物バッファは main スレッドから触らない。座標だけを sim 側へ渡し、
            //    その下に何が建っているかは次の tick のスナップショットで受け取る
            //    （BuildingProbe のクラス doc。1 tick ぶんの遅延はその設計上の代償）。
            PublishCursor(cursor, haveCursor);

            var primary = RefreshQuakeRows(snapshot, haveCursor, cursor);
            EarthquakeSensorRows.RefreshSensor(snapshot, primary, haveCursor);
            EarthquakeSensorRows.RefreshWaveform(snapshot);
            EarthquakeMapRows.Refresh(snapshot, hazardViewOn, haveCursor, cursor);
            // ★ 最後に呼ぶ。第 1 層を全部書き終えたあとに、本 MOD が足したものを書く。
            EarthquakeLayer2Rows.Refresh(snapshot);
        }

        private static void ClearQuakeRows()
        {
            EarthquakeRows.SetPlain(_countLabel, "");
            EarthquakeRows.SetPlain(_intensityLabel, "");
            EarthquakeRows.SetPlain(_phaseLabel, "");
            EarthquakeRows.SetPlain(_timeLabel, "");
            EarthquakeRows.SetPlain(_cursorLabel, "");
            EarthquakeRows.SetPlain(_shakeLabel, "");
            // 注記は行が出ているときだけ（RefreshShakeRow が入れ直す）。
        }

        /// <summary>
        /// 地震の行を書き、以後の行が指すべき地震（<see cref="SelectPrimary"/> の結果）を返す。
        /// 地震が 1 個も無ければ null。
        /// </summary>
        private static EarthquakeReading RefreshQuakeRows(EarthquakeSnapshot snapshot,
                                                          bool haveCursor, Vec3 cursor)
        {
            var quakes = snapshot.Quakes;
            if (quakes.Count == 0)
            {
                ClearQuakeRows();
                EarthquakeDamageRows.Clear();
                // 「0」という裸の数字ではなく、文で言う。走査した結果なので第 1 層。
                EarthquakeRows.SetLayer1(_countLabel, Strings.EarthquakeNoneActive);
                return null;
            }

            var primary = SelectPrimary(quakes, haveCursor, cursor);

            // 複数同時に起きている場合（§E-1 で可能と確定している）、以下の行が
            // どの地震のものかを名乗る。災害バッファ上の添字なのでローカライズしない。
            string count = Strings.EarthquakeCount + ": " + quakes.Count;
            if (quakes.Count > 1) count += "   (#" + primary.DisasterId + ")";
            EarthquakeRows.SetLayer1(_countLabel, count);

            // 強度の表示は災害パネルと揃えて 1/10 にする（§A-2b の
            // m_label.text = (value / 10).ToString("F1")）。生の byte を出すと
            // ゲーム内の他の表示と 10 倍食い違う。
            EarthquakeRows.SetLayer1(_intensityLabel,
                Strings.EarthquakeIntensity + ": " + (primary.Intensity / 10f).ToString("F1")
                + "    " + Strings.EarthquakeRadius + ": " + primary.Radius.ToString("F0") + " m");

            RefreshPhaseRow(primary);
            RefreshTimeRow(snapshot, primary);
            RefreshCursorRow(primary, haveCursor, cursor);
            RefreshShakeRow(snapshot, primary, haveCursor, cursor);
            EarthquakeDamageRows.Refresh(snapshot, primary, haveCursor, cursor);
            return primary;
        }

        private static void RefreshPhaseRow(EarthquakeReading primary)
        {
            string word = null;
            switch (primary.Phase)
            {
                case EarthquakePhase.Emerging: word = Strings.EarthquakePhaseEmerging; break;
                case EarthquakePhase.Active: word = Strings.EarthquakePhaseActive; break;
                case EarthquakePhase.Clearing: word = Strings.EarthquakePhaseClearing; break;
            }

            // Finished / Unknown に当てる語は用意していない。名前を付けると
            // 「進行中の位相のひとつ」に見えるので、行ごと出さない。
            EarthquakeRows.SetPlain(_phaseLabel, "");
            if (word != null)
            {
                EarthquakeRows.SetLayer1(_phaseLabel, Strings.EarthquakePhase + ": " + word);
            }
        }

        /// <summary>
        /// 「本震まで」。**これは予測ではなく予定表の読み上げである。**
        ///
        /// ①は「あと何時間で来る」を禁じている（発生判定が乱数だから）。②のこの行は
        /// 根拠が違う: <c>m_activationFrame</c> は <c>StartDisaster</c> が
        /// <c>m_startFrame + m_emergingDuration</c> として**書き込んだ確定値**であり
        /// （§A-1）、<c>IsStillEmerging</c> はそれと現在フレームを比べているだけである。
        /// 設計書 §7-2 がこの区別を要求している。
        ///
        /// ただし <c>m_activationFrame == 0</c> は「今」ではなく「予定が無い」。
        /// <c>SelfTrigger(64)</c> が立っていない地震はここが 0 のまま Emerging で
        /// 永久に固まるので、そのまま引き算すると「あと 4739 年」のような数字になる。
        /// </summary>
        private static void RefreshTimeRow(EarthquakeSnapshot snapshot, EarthquakeReading primary)
        {
            EarthquakeRows.SetPlain(_timeLabel, "");

            if (primary.Phase != EarthquakePhase.Emerging) return;

            if (!primary.ActivationScheduled)
            {
                EarthquakeRows.SetLayer1(_timeLabel,
                    Strings.EarthquakeTimeToShock + ": " + Strings.EarthquakeTimeUnknown);
                return;
            }

            if (primary.ActivationFrame <= snapshot.CurrentFrame) return;

            // 換算は必ず FeatureHost.FramesPerMinute から出す（定数を直書きして
            // 4 倍ずれた前科がある。③、DAYTIME_FRAMES の取り違え）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            float minutes = (primary.ActivationFrame - snapshot.CurrentFrame) / framesPerMinute;
            EarthquakeRows.SetLayer1(_timeLabel, Strings.EarthquakeTimeToShock + ": "
                + minutes.ToString("F0") + " " + Strings.EarthquakeMinutes);
        }

        /// <summary>
        /// カーソル地点の局所係数 s。**全体円盤（probability = 0.02）の倒壊・出火ランプ**
        /// であって、揺れでも断層帯でもない（どちらも別行で出す）。
        ///
        /// ── この行が名乗ってよいもの・いけないもの（全体レビュー C3）─────────
        ///
        /// 数字（<c>s = 1 - d/R</c>）はバニラの <c>fD</c> そのもので正しい。だが
        /// 以前この行は「カーソル地点の揺れ」を名乗り、R の外を「揺れの範囲外」と
        /// 書いていた。バニラの揺れは <c>amp = 0.3/(1 + dist*0.001)</c> で
        /// **半径の打ち切りが一切無い**（§A-7）ので、それは同じフレームで
        /// <c>CameraShakeBooster</c> が揺れを足し <c>SeismographRecorder</c> が
        /// 非ゼロの変位を書いている地点についての、真っ向から反対の主張だった。
        ///
        /// **区分名（弱い/強い…）も出さない。** あれはこの MOD が付けた名前であって、
        /// バニラは probability の係数を計算しているだけである。<c>[measured]</c> の
        /// 下に置くと、ゲームがそう判断していることになる（<c>Strings</c> の
        /// <c>EarthquakeBandWeak</c> 付近のコメント）。
        ///
        /// 半径 R の外は「係数が 0」ではなく「バニラが判定すらしていない」なので、
        /// <c>0.0</c> と出さずに「圏外」と書く（<see cref="SeismicIntensity.At"/> の doc）。
        /// </summary>
        private static void RefreshCursorRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            // ★ 収束中（Clearing）の地震には破壊判定が走らない。全体円盤の
            //    DestroyBuildings は SimulationStep の Active 分岐にしか無い（§A-3）ので、
            //    ここで係数を出すと「もう起きないこと」の強さを名乗ることになる。
            //    sim 側（QuakeSelection.SelectDamaging）が同じ理由で Clearing を
            //    除いているので、表示側だけ含めていた食い違いを解消する。
            if (!QuakeSelection.RunsDamage(primary.Phase))
            {
                EarthquakeRows.SetPlain(_cursorLabel,
                    Strings.EarthquakeAtCursor + ": " + Strings.EarthquakeNoDamageInPhase);
                return;
            }

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_cursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            float distance = DistanceXZ(cursor, primary.Epicentre);
            if (!SeismicIntensity.IsInside(distance, primary.Intensity))
            {
                EarthquakeRows.SetLayer1(_cursorLabel,
                    Strings.EarthquakeAtCursor + ": " + Strings.EarthquakeOutOfRange);
                return;
            }

            float s = SeismicIntensity.At(distance, primary.Intensity);
            EarthquakeRows.SetLayer1(_cursorLabel, Strings.EarthquakeAtCursor + ": "
                + s.ToString("F2") + "  [" + SeismicScale.BarOf(s) + "]");
        }

        /// <summary>
        /// **カーソル地点で地面が実際にどれだけ揺れているか。** 上の倒壊ランプとは
        /// 別の量で、こちらが依頼文の「揺れ」に当たる。
        ///
        /// 出しているのは <c>EarthquakeAI.RenderInstance</c> の <c>amp</c>
        /// （§A-7 IL_0069、包絡線を掛ける前）そのものである。バニラはこの距離を
        /// **カメラから**測るが、ここは震央からの距離で評価する —— 波形グラフと
        /// まったく同じ置き換えで、同じ式の別評価であって近似ではない（設計書 §3.5）。
        ///
        /// **半径の打ち切りは無い。** 10 km 離れていても震央の 9% で揺れている。
        /// それが常設の注記（<c>EarthquakeShakeNote</c>）の内容である。
        ///
        /// 窓（<c>Emerging|Active</c> かつ <c>0 &lt; e &lt; m_activeDuration</c>）が
        /// 開いていなければ数値を出さない。<c>m_activeDuration</c> はプレハブ値で
        /// まだ誰も実測していないので、読めていないときも数値を出さない
        /// （<c>CameraShakeBooster</c> / <c>SeismographRecorder</c> と同じ判断）。
        /// </summary>
        private static void RefreshShakeRow(EarthquakeSnapshot snapshot, EarthquakeReading primary,
                                            bool haveCursor, Vec3 cursor)
        {
            // 「半径による打ち切りが無い」ことは、数値が出ていない状態でこそ
            // 誤解されうる（上の倒壊ランプが「圏外」と言っている隣なので）。

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_shakeLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                EarthquakeRows.SetPlain(_shakeLabel,
                    Strings.EarthquakeShakeAtCursor + ": " + Strings.EarthquakeUnavailable);
                return;
            }

            long e = (long)snapshot.CurrentFrame - primary.ActivationFrame
                     + ShakeWaveform.FrameOffset;
            if (!primary.ActivationScheduled
                || !QuakeSelection.RunsDamage(primary.Phase)
                || !ShakeWaveform.IsShaking(e, snapshot.Prefab.ActiveDuration))
            {
                EarthquakeRows.SetPlain(_shakeLabel,
                    Strings.EarthquakeShakeAtCursor + ": " + Strings.EarthquakeNotShaking);
                return;
            }

            float amplitude = ShakeWaveform.PeakAmplitudeAt(
                DistanceXZ(cursor, primary.Epicentre));
            EarthquakeRows.SetLayer1(_shakeLabel, Strings.EarthquakeShakeAtCursor + ": "
                + amplitude.ToString("F3") + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                + "  [" + SeismicScale.BarOf(
                    ShakeWaveform.NormalisedDisplacement(amplitude)) + "]");
        }

        /// <summary>
        /// 表示の対象にする地震を 1 つ選ぶ。**全ての行が同じ地震を指すようにするため**で、
        /// 「強度は地震 A、カーソルの揺れは地震 B」という混線を防ぐ（複数同時発生は
        /// §E-1 で可能と確定している）。件数が 1 個の通常の場合は何も起きない。
        ///
        /// 優先順: 進行中 &gt; カーソル地点で強く効いている &gt; 強度が大きい &gt; 添字が小さい。
        /// 「進行中」を先に見るのは、Finished の残骸を主役にしないため。
        ///
        /// ── ここが <c>Clearing</c> を含むのは意図的である（全体レビュー I2）──────
        ///
        /// sim 側の <see cref="QuakeSelection.SelectDamaging"/> は <c>Clearing</c> を
        /// 含めない（破壊判定が <c>Active</c> 分岐にしか無いため、§A-3）。こちらは
        /// **件数・強度・位相**を出すための選定なので、収束中の地震も主役になれる
        /// ——「余震処理中」と表示できないのはむしろ情報の欠落である。
        ///
        /// **代わりに、破壊が走らない位相では破壊由来の行を出さない。**
        /// <see cref="RefreshCursorRow"/> と <see cref="EarthquakeDamageRows"/> が
        /// <see cref="QuakeSelection.RunsDamage"/> で自分から降りる。以前は
        /// この 2 行が収束中の地震について係数と「断層帯: 内側」を出していた。
        /// </summary>
        private static EarthquakeReading SelectPrimary(IList<EarthquakeReading> quakes,
                                                       bool haveCursor, Vec3 cursor)
        {
            EarthquakeReading best = null;
            bool bestInProgress = false;
            float bestFactor = 0f;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                bool inProgress = q.Phase == EarthquakePhase.Emerging
                                  || q.Phase == EarthquakePhase.Active
                                  || q.Phase == EarthquakePhase.Clearing;
                float factor = haveCursor
                    ? SeismicIntensity.At(DistanceXZ(cursor, q.Epicentre), q.Intensity)
                    : 0f;

                bool better;
                if (best == null) better = true;
                else if (inProgress != bestInProgress) better = inProgress;
                else if (factor != bestFactor) better = factor > bestFactor;
                else better = q.Intensity > best.Intensity;

                if (!better) continue;
                best = q;
                bestInProgress = inProgress;
                bestFactor = factor;
            }

            return best;
        }

        private static float DistanceXZ(Vec3 a, Vec3 b)
        {
            return Mathf.Sqrt(a.ToVec2().DistanceSquaredTo(b.ToVec2()));
        }

        // 区分名（弱い/中程度/強い/非常に強い）をここで文字列に落とすヘルパーは
        // 全体レビュー(C3)で撤去した。あれは**この MOD が付けた名前**であって、
        // バニラが計算しているのは probability の係数だけである。[measured] の
        // 接頭辞の下に置くと、ゲームがその判断をしていることになってしまう。
        // SeismicScale.BandOf と Strings.EarthquakeBand* は、第 2 層が
        // 自分の名前として名乗るときのために残してある。
    }
}
