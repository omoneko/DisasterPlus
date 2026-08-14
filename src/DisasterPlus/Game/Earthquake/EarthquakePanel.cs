using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Forecast;
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
    /// この 2 つを混ぜたら、この機能は存在価値を失う。**Task 4 の行は全て第 1 層である。**
    /// それでも機構を先に入れておくのは、Task 9〜11 が同じパネルへ第 2 層の行を足すため。
    ///
    /// 機械的に確認できる形にしてある:
    ///   - <c>AddUIComponent(typeof(UILabel))</c> が現れるのは <see cref="AddLabel"/> の 1 箇所だけ。
    ///     ラベルを作れるのは <see cref="AddSectionHeader"/> / <see cref="AddPlainRow"/> /
    ///     <see cref="AddLayer1Row"/> / <see cref="AddLayer2Row"/> の 4 ヘルパー経由のみ。
    ///   - <c>UILabel.text</c> への代入が現れるのは <see cref="SetPlain"/> の 1 箇所だけ。
    ///     <see cref="SetLayer1"/> / <see cref="SetLayer2"/> はそこへ委譲しつつ
    ///     <c>Strings.SourceVanilla</c> / <c>Strings.SourceModel</c> を**必ず**行頭に付ける
    ///     （呼び出し側がどちらを名乗るか選べる余地を作らない）。
    ///   - <c>Strings.SourceVanilla</c> / <c>Strings.SourceModel</c> が現れるのも
    ///     その 2 つのセッターの中だけ。
    ///
    /// **色だけに頼らない。** 接頭辞・セクション見出し・色の 3 つを同時に使う。
    /// 色覚や UI テーマの違いで区別が消える可能性に、この担保を賭けない。
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
    /// 「当たりうる範囲であって当たる場所ではない」注記を必ず添える。
    /// </summary>
    public static class EarthquakePanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "EarthquakePanel";

        private const float PanelWidth = 420f;
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// カーソル地点のレイを実際に引き直す間隔（描画フレーム数）。
        ///
        /// ── なぜ間引くのか（①からの持ち越しの是正）─────────────────────
        ///
        /// <see cref="TryPickCursorGround"/> は地形と交差するまで
        /// <c>MaxRayDistance / 16m</c> ＝ **最大 500 回**の高さサンプリングを行い、
        /// 当たれば二分法が 20 回追加される。**いちばん高くつくのは「外す」場合**
        /// （地平線をかすめるレイ）で、これは視点を動かしている間に普通に起きる。
        ///
        /// ①はこの費用を「ハザード情報ビューを開いている間しか走らないので許容」と
        /// して意図的に未最適化のまま残した。**Task 4 でその前提が変わった** ——
        /// 地震が進行中ならパネルは毎フレームこのレイを引くので、カメラが揺れ、
        /// 建物が倒れ、パーティクルが出ている**いちばん重いフレーム**に重なる。
        ///
        /// 直し方は「引く回数を上限で縛る」。4 フレームに 1 回だけ引き、それ以外の
        /// フレームは直前の結果を返す。**表示する値そのものは変えない**（同じ計算の
        /// 結果を、最大 3 フレーム（60fps で 50ms 未満）遅れて出すだけ）。
        /// 建物の余裕度が既に 1 sim tick 遅れて届く設計（<see cref="BuildingProbe"/>）と
        /// 同じ性質の、目に見えない遅延である。
        ///
        /// 1 にすると毎フレーム引く（＝この是正が無効になる）。大きくすると
        /// カーソル追従が目に見えて遅れる。
        /// </summary>
        private const int RepickIntervalFrames = 4;

        private const float RowStep = 22f;
        private const float RowHeight = 20f;

        /// <summary>第 1 層 ＝ バニラが実際に計算している量。</summary>
        private static readonly Color32 Layer1Color = new Color32(255, 255, 255, 255);

        /// <summary>
        /// 第 2 層 ＝ この MOD が発明した数字。白と明確に違う色にするが、
        /// **色だけには頼らない**（接頭辞とセクション見出しが本体）。
        /// Task 4 の時点で読むのは <see cref="AddLayer2Row"/> だけで、
        /// その呼び出し側は Task 9〜11 が足す。
        /// </summary>
        private static readonly Color32 Layer2Color = new Color32(150, 190, 255, 255);

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _countLabel;
        private static UILabel _intensityLabel;
        private static UILabel _phaseLabel;
        private static UILabel _timeLabel;
        private static UILabel _cursorLabel;
        private static UILabel _faultLabel;
        private static UILabel _faultNoteLabel;
        private static UILabel _marginBuildingLabel;
        private static UILabel _marginVerdictLabel;
        private static UILabel _marginNoteLabel;
        private static UILabel _sensorEpicentreLabel;
        private static UILabel _sensorLeadLabel;
        private static UILabel _sensorCursorLabel;
        private static UILabel _hazardLabel;

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
            // 閉じた瞬間に sim 側の建物走査を止める。
            PublishCursor(new Vec3(0f, 0f, 0f), false);
        }

        public static void Toggle()
        {
            if (IsVisible) Hide(); else Show();
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
            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _countLabel = null;
            _intensityLabel = null;
            _phaseLabel = null;
            _timeLabel = null;
            _cursorLabel = null;
            _faultLabel = null;
            _faultNoteLabel = null;
            _marginBuildingLabel = null;
            _marginVerdictLabel = null;
            _marginNoteLabel = null;
            _sensorEpicentreLabel = null;
            _sensorLeadLabel = null;
            _sensorCursorLabel = null;
            _hazardLabel = null;
            _bodyBuilt = false;
            _cursorPublishedValid = false;
            // 次の都市が前の都市のカーソル地点を 1 回でも返さないようにする。
            _pickCached = false;
            _pickFrame = 0;
            _pickOk = false;
            _pickHit = new Vec3(0f, 0f, 0f);
            // fake-null 経由でも次回 Camera.main を引き直せるが、都市をまたいで
            // 古い参照を抱え続けない、という本プロジェクトの原則を明示的に守る。
            _mainCameraCache = null;
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
            panel.width = PanelWidth;
            panel.backgroundSprite = "MenuPanel2";
            panel.color = new Color32(255, 255, 255, 240);
            // ①の予報パネル（x=200、幅 380）と重ならない位置。UIView の座標系は
            // 高さ 1080 に正規化され、16:9 なら幅はおよそ 1920 になるので、
            // x=600 は現実的な解像度で画面外に出ない。
            panel.relativePosition = new Vector3(600f, 150f);
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = AddLabel(panel, "Title", 10f, y, PanelWidth - 44f, 24f, Layer1Color);
            _titleLabel.textScale = 1.1f;

            var closeButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            closeButton.name = FreeSlotFinder.SelfPrefix + "EarthquakeCloseButton";
            closeButton.text = "X";
            closeButton.width = 24f;
            closeButton.height = 24f;
            closeButton.relativePosition = new Vector3(PanelWidth - 32f, y);
            closeButton.normalBgSprite = "ButtonMenu";
            closeButton.hoveredBgSprite = "ButtonMenuHovered";
            closeButton.pressedBgSprite = "ButtonMenuPressed";
            closeButton.eventClick += (c, e) => Hide();

            y += 30f;

            // DLC が無い環境では地震そのものが存在しない。行を出さずに理由を書く。
            _bodyBuilt = ModCompat.NaturalDisastersOwned;
            if (!_bodyBuilt)
            {
                AddPlainRow(panel, "NeedsDlc", ref y, Strings.EarthquakeNeedsDlc, 36f);
                panel.height = y;
                return;
            }

            // ★ 構築順で層の上下を保証する。第 2 層のセクション（Task 9〜11）は
            //    必ずこの第 1 層の**下**に足すこと。実行時の並べ替えはしない。
            AddSectionHeader(panel, "Layer1Header", ref y, Strings.EarthquakeLayer1Header);

            _countLabel = AddLayer1Row(panel, "Count", ref y);
            _intensityLabel = AddLayer1Row(panel, "Intensity", ref y);
            _phaseLabel = AddLayer1Row(panel, "Phase", ref y);
            _timeLabel = AddLayer1Row(panel, "TimeToShock", ref y);
            _cursorLabel = AddLayer1Row(panel, "AtCursor", ref y);
            _faultLabel = AddLayer1Row(panel, "FaultBand", ref y);

            // 断層帯の行には**常に**この注記が付く（計画の共通規則）。
            // 4 円盤の位置は毎ステップ振り直されるので、帯は「当たりうる範囲」であって
            // 「当たる場所」ではない。テキストは固定なのでここで一度だけ入れる。
            _faultNoteLabel = AddPlainRow(panel, "FaultBandNote", ref y,
                Strings.EarthquakeFaultBandNote, 36f);

            // ── 建物ごとの余裕度（Task 5、本機能の目玉）──────────────────
            // ここも第 1 層である。バニラが (建物, 災害) の組ごとに引く固定のしきい値を
            // 同じ種から再構成しているだけで、新しい物理は 1 つも足していない（§A-3）。
            _marginBuildingLabel = AddLayer1Row(panel, "MarginBuilding", ref y);
            _marginVerdictLabel = AddLayer1Row(panel, "MarginVerdict", ref y);

            // この注記は**常に**併記する。全体円盤についての判定でしかないことと、
            // それが地震開始の瞬間に既に決まっていることの両方を、行の隣で名乗る。
            _marginNoteLabel = AddPlainRow(panel, "MarginNote", ref y,
                Strings.EarthquakeGlobalDiscOnly, 54f);

            // ── 地震計（Task 7）──────────────────────────────────
            // ここも第 1 層である。リードタイムの式も上限 100 もバニラのリテラルで
            // （§A-2）、この MOD は 1 つも係数を足していない。
            //
            // 位置は意図的にここ ——「マップに表示」ボタンとハザードの行の**すぐ上**。
            // 地震計はハザードマップが空である理由そのものなので、説明を読んだ直後に
            // ボタンとその結果が目に入る。第 2 層のセクション（Task 9〜11）は
            // これより下に足すこと。
            AddSectionHeader(panel, "SensorSection", ref y, Strings.EarthquakeSensorSection);
            _sensorEpicentreLabel = AddLayer1Row(panel, "SensorEpicentre", ref y);
            _sensorLeadLabel = AddLayer1Row(panel, "SensorLead", ref y);
            _sensorCursorLabel = AddLayer1Row(panel, "SensorCursor", ref y);

            // ★ この 1 行は**建てたら何が変わるか**の説明であって観測値ではないので、
            //    地震が起きていなくても、スナップショットが読めていなくても出す。
            //    したがってここで一度書いたら以後どこからも書き換えない
            //    （Refresh 側に「消す」経路を作らないことがその保証になっている）。
            AddPlainRow(panel, "SensorEffect", ref y, Strings.EarthquakeSensorEffect, 54f);

            y += 6f;

            var showButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            showButton.name = FreeSlotFinder.SelfPrefix + "EarthquakeShowOnMap";
            showButton.text = Strings.EarthquakeShowOnMap;
            showButton.tooltip = Strings.EarthquakeTitle;
            showButton.width = 150f;
            showButton.height = 24f;
            showButton.relativePosition = new Vector3(12f, y);
            showButton.normalBgSprite = "ButtonMenu";
            showButton.hoveredBgSprite = "ButtonMenuHovered";
            showButton.pressedBgSprite = "ButtonMenuPressed";
            // ①のラッパーは一切変更しない。②はそのまま使うだけ（計画 4.1）。
            showButton.eventClick += (c, e) =>
                InfoModeSwitch.ShowHazard(InfoManager.SubInfoMode.EarthquakeHazard);
            y += 30f;

            // この 1 行が、数値か「空である理由」かのどちらか一方だけを出す。
            // 2 つのラベルに分けないのは意図的で、「理由を書いたのに隣に数値も残っている」
            // という状態を構造的に作れなくするため。3 行ぶんの高さを取る。
            _hazardLabel = AddPlainRow(panel, "Hazard", ref y, Strings.EarthquakeSwitchHazardView, 54f);

            panel.height = y;
        }

        // ── ラベル生成（UILabel を作ってよいのはこの 1 箇所だけ） ──────────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height, Color32 color)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Earthquake" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = color;
            label.autoSize = false;
            // 1 行に収まらない説明文が途中で切れないようにする。
            label.wordWrap = height > RowHeight;
            return label;
        }

        private static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y, string text)
        {
            var label = AddLabel(p, suffix, 12f, y, PanelWidth - 24f, RowHeight, Layer1Color);
            SetPlain(label, "-- " + text + " --");
            y += 26f;
            return label;
        }

        /// <summary>出典の接頭辞を持たない行（見出しの注記・状態の説明）。</summary>
        private static UILabel AddPlainRow(UIPanel p, string suffix, ref float y,
                                           string text, float height)
        {
            var label = AddLabel(p, suffix, 12f, y, PanelWidth - 24f, height, Layer1Color);
            SetPlain(label, text);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **第 1 層の行。** バニラ自身の式と定数だけから導いた量にのみ使う。
        /// 中身は <see cref="SetLayer1"/> でしか書けない。
        /// </summary>
        private static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, 12f, y, PanelWidth - 24f, RowHeight, Layer1Color);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// **第 2 層の行。** この MOD が発明した物理にのみ使う。
        /// Task 4 の時点で呼び出し側は無い（第 1 層しか出さないため）。
        /// Task 9〜11 が行を足すときは、必ず第 1 層のセクションより**下**に構築すること。
        /// </summary>
        private static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, 12f, y, PanelWidth - 24f, RowHeight, Layer2Color);
            y += RowStep;
            return label;
        }

        // ── テキスト設定（UILabel.text への代入はこの 1 箇所だけ） ──────────

        private static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>第 1 層の行に書く。接頭辞は呼び出し側に選ばせない。</summary>
        private static void SetLayer1(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }

        /// <summary>第 2 層の行に書く。接頭辞は呼び出し側に選ばせない。</summary>
        private static void SetLayer2(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceModel + " " + body);
        }

        // ── 内容の更新 ────────────────────────────────────

        private static void Refresh()
        {
            SetPlain(_titleLabel, Strings.EarthquakeTitle);

            // DLC が無い環境では説明の 1 行しか構築していない（_bodyBuilt の doc）。
            if (!_bodyBuilt) return;

            var snapshot = EarthquakeHub.Latest;
            if (snapshot == null || !snapshot.Valid)
            {
                // 読めていない間は sim 側に建物を探させない。
                PublishCursor(new Vec3(0f, 0f, 0f), false);
                ClearQuakeRows();
                ClearSensorRows();
                // 「まだ 1 回も読んでいない」と「読んだが読めなかった」を同じ文言に
                // しないこと（①のレビュー指摘）。ロード直後にポーズしたままだと
                // 前者が普通に起きる（最初の tick の deltaMinutes は必ず 0）。
                SetPlain(_countLabel, snapshot == null
                    ? Strings.ForecastWaiting
                    : Strings.EarthquakeUnavailable);
                SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // カーソル地点は 1 フレームに 1 回だけ求める。地形をかすめて外すレイでは
            // 501 回の高さサンプリングが走るので、同じフレームで 2 回引いてはいけない
            // （強度の行・ハザードの行・地震計の行で共有する）。実際にレイを引くのは
            // さらに RepickIntervalFrames フレームに 1 回だけで、残りのフレームは
            // 直前の結果を返す（TryPickCursorGround の doc）。
            //
            // **地震が 1 個も無くても引く。** カーソル地点の地震計カバレッジは
            // 「この場所に地震計が届いているか」であって、地震の有無とは関係が無い
            // （設計書 §3.4）。地震計を建てる場所の下見に使えることがこの行の値打ちで、
            // 地震が起きている間しか読めないなら下見にならない。
            // Task 6 の間引きが入るまでは、この「常に引く」は許容できなかった。
            bool hazardViewOn =
                InfoModeSwitch.IsShowingHazardFor(InfoManager.SubInfoMode.EarthquakeHazard);
            Vec3 cursor;
            bool haveCursor = TryPickCursorGround(out cursor);

            // ★ 建物バッファは main スレッドから触らない。座標だけを sim 側へ渡し、
            //    その下に何が建っているかは次の tick のスナップショットで受け取る
            //    （BuildingProbe のクラス doc。1 tick ぶんの遅延はその設計上の代償）。
            PublishCursor(cursor, haveCursor);

            var primary = RefreshQuakeRows(snapshot, haveCursor, cursor);
            RefreshSensorRows(snapshot, primary, haveCursor);
            RefreshHazardRow(snapshot, hazardViewOn, haveCursor, cursor);
        }

        private static void ClearQuakeRows()
        {
            SetPlain(_countLabel, "");
            SetPlain(_intensityLabel, "");
            SetPlain(_phaseLabel, "");
            SetPlain(_timeLabel, "");
            SetPlain(_cursorLabel, "");
            SetPlain(_faultLabel, "");
            SetPlain(_faultNoteLabel, "");
            SetPlain(_marginBuildingLabel, "");
            SetPlain(_marginVerdictLabel, "");
            SetPlain(_marginNoteLabel, "");
        }

        /// <summary>
        /// 地震計の行の値だけを消す。**説明文（<c>SensorEffect</c>）は消さない** ——
        /// あれは「建てたら何が変わるか」であって観測値ではないので、
        /// 何も読めていないときこそ読む価値がある。
        /// </summary>
        private static void ClearSensorRows()
        {
            SetPlain(_sensorEpicentreLabel, "");
            SetPlain(_sensorLeadLabel, "");
            SetPlain(_sensorCursorLabel, "");
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
                // 「0」という裸の数字ではなく、文で言う。走査した結果なので第 1 層。
                SetLayer1(_countLabel, Strings.EarthquakeNoneActive);
                return null;
            }

            var primary = SelectPrimary(quakes, haveCursor, cursor);

            // 複数同時に起きている場合（§E-1 で可能と確定している）、以下の行が
            // どの地震のものかを名乗る。災害バッファ上の添字なのでローカライズしない。
            string count = Strings.EarthquakeCount + ": " + quakes.Count;
            if (quakes.Count > 1) count += "   (#" + primary.DisasterId + ")";
            SetLayer1(_countLabel, count);

            // 強度の表示は災害パネルと揃えて 1/10 にする（§A-2b の
            // m_label.text = (value / 10).ToString("F1")）。生の byte を出すと
            // ゲーム内の他の表示と 10 倍食い違う。
            SetLayer1(_intensityLabel,
                Strings.EarthquakeIntensity + ": " + (primary.Intensity / 10f).ToString("F1")
                + "    " + Strings.EarthquakeRadius + ": " + primary.Radius.ToString("F0") + " m");

            RefreshPhaseRow(primary);
            RefreshTimeRow(snapshot, primary);
            RefreshCursorRow(primary, haveCursor, cursor);
            RefreshFaultRow(primary, haveCursor, cursor);
            RefreshMarginRows(snapshot);
            return primary;
        }

        /// <summary>
        /// **地震計を建てると何が変わるか。** ゲーム内のどこにも書かれていない 2 つの効果を
        /// 名指しする（§A-2 / §C-2）:
        ///
        ///   1. 警報リードタイムが 1755 → 最大 8192 フレーム（38.6 分 → ちょうど 3.0 時間）
        ///   2. <c>located</c> が立ち、**そもそも地震がハザードマップに描かれるようになる**
        ///
        /// **因果の向きを間違えないこと。** 地震計は <c>DetectDisaster</c> を呼ばない。
        /// 呼ぶのは <c>EarthquakeAI.SimulationStep</c> で、判断材料は
        /// **震央 1 点のカバレッジ**である。したがってここでリードタイムを出してよいのは
        /// <see cref="EarthquakeReading.CoverageAtEpicentre"/> からだけで、
        /// カーソル地点の値から出してはいけない（効果範囲が震央に届いていない地震計は、
        /// その地震について何も寄与しない）。
        ///
        /// **カバレッジ 0 と「読めなかった」を同じ顔にしない。** 0 は
        /// 「震央に届いている地震計が 1 つも無い」という本機能の看板の実測値であり、
        /// そのときは数値も出す。読めなかったときだけ数値を伏せる
        /// （<see cref="EarthquakeReading.CoverageKnown"/> /
        /// <see cref="EarthquakeSnapshot.CursorCoverageValid"/>）。
        /// </summary>
        private static void RefreshSensorRows(EarthquakeSnapshot snapshot,
                                              EarthquakeReading primary, bool haveCursor)
        {
            RefreshEpicentreCoverageRows(primary);
            RefreshCursorCoverageRow(snapshot, haveCursor);
        }

        private static void RefreshEpicentreCoverageRows(EarthquakeReading primary)
        {
            // 地震が無ければ震央も無い。ここで 0 を出すと「地震計が無い」に見える。
            if (primary == null)
            {
                SetPlain(_sensorEpicentreLabel, "");
                SetPlain(_sensorLeadLabel, "");
                return;
            }

            if (!primary.CoverageKnown)
            {
                // ★ 捏造ゼロを作らない。読めなかったことを言い、リードタイムは伏せる
                //    （カバレッジ不明のまま 38.6 分と出すと、それは 0 の断定になる）。
                SetPlain(_sensorEpicentreLabel,
                    Strings.EarthquakeCoverageAtEpicentre + ": " + Strings.EarthquakeUnavailable);
                SetPlain(_sensorLeadLabel, "");
                return;
            }

            int raw = primary.CoverageAtEpicentre;
            int used = WarningLeadTime.ClampCoverage(raw);

            string text = Strings.EarthquakeCoverageAtEpicentre + ": " + raw;
            // バニラが Min(cov, 100) で頭打ちにしている事実を、実際に頭打ちに
            // なっているときだけ見せる（§A-2）。
            if (raw != used) text += " -> " + used;
            if (used == 0) text += "   (" + Strings.EarthquakeNoSensor + ")";
            SetLayer1(_sensorEpicentreLabel, text);

            // 換算は必ず FeatureHost.FramesPerMinute から出す（定数を直書きして
            // 4 倍ずれた前科がある）。換算できないときは 0 が返るので行ごと伏せる。
            float minutes = WarningLeadTime.MinutesFor(raw, FeatureHost.FramesPerMinute);
            if (minutes <= 0f)
            {
                SetPlain(_sensorLeadLabel, "");
                return;
            }

            // これは「あと何分で警報が出る」ではなく、**本震の何分前に警報が出るか**
            // という長さである。ポーズしていても縮まない（ゲーム内分の尺度）。
            SetLayer1(_sensorLeadLabel, Strings.EarthquakeWarningLead + ": "
                + minutes.ToString("F1") + " " + Strings.EarthquakeMinutes);
        }

        private static void RefreshCursorCoverageRow(EarthquakeSnapshot snapshot, bool haveCursor)
        {
            // 「カーソルが地形の上に無い」と「読めなかった」を言い分ける。
            // 前者はパネルを読んでいる間ほぼ常に起きる（マウスがパネルの上にある）。
            if (!haveCursor)
            {
                SetPlain(_sensorCursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            if (!snapshot.CursorCoverageValid)
            {
                SetPlain(_sensorCursorLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // 上限 100 の注記は付けない。ここは「この場所に地震計が届いているか」を
            // 見る行であって、リードタイムの式に入る値ではない。
            SetLayer1(_sensorCursorLabel,
                Strings.EarthquakeCoverageAtCursor + ": " + snapshot.CursorCoverage);
        }

        /// <summary>
        /// **本機能の目玉。** カーソル下の建物が、その地震で倒れるかどうか。
        ///
        /// バニラは建物ごとに <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> から
        /// 固定のしきい値を引く（§A-3）。この種はフレームにもステップにも依存せず、
        /// 全体円盤の震央も動かないので、**結論は地震が始まった瞬間に既に確定している**。
        /// 依頼文の「揺れによる火災や倒壊はおそらくランダム」への回答がこれで、
        /// だからこの行だけは「予測」ではなく事実として書ける。
        ///
        /// ただし断定してよい範囲は狭い。ここで扱っているのは全体円盤
        /// （probability = 0.02、震央中心）だけで、断層 4 円盤（probability = 1、
        /// 毎ステップ位置が振り直される）については何も言えない。**帯の内側と、
        /// 帯の幾何が読めていないときは、「倒壊しません」と言わない**
        /// —— それを保証しているのは <see cref="BuildingMargin.Evaluate"/> 側の分岐順で、
        /// ここはその結論を書き出すだけである。
        ///
        /// 値は全て 1 tick 前の sim スレッドの読み取りで、**このメソッドは建物バッファに
        /// 一切触らない**（<see cref="BuildingProbe"/> のクラス doc）。
        /// </summary>
        private static void RefreshMarginRows(EarthquakeSnapshot snapshot)
        {
            var margin = snapshot.CursorBuilding;

            // 注記は行が出ているときだけ添える（空行の下に注記だけ残さない）。
            SetPlain(_marginNoteLabel, "");

            // CursorQuakeId == 0 は「まだ調べていない」——カーソルが地形の上に無い、
            // あるいは破壊判定が走る地震（Active / Emerging）が 1 つも無い。
            // **この状態で「カーソルの下に建物がありません」と書いてはいけない。**
            // 建物の上にカーソルがあっても同じ 0 になるので、それは嘘になる。
            // 言えることが無いときは、何も言わない。
            if (snapshot.CursorQuakeId == 0)
            {
                SetPlain(_marginBuildingLabel, "");
                SetPlain(_marginVerdictLabel, "");
                return;
            }

            if (!margin.HasBuilding)
            {
                SetLayer1(_marginBuildingLabel,
                    Strings.EarthquakeBuildingUnderCursor + ": " + Strings.EarthquakeNoBuilding);
                SetPlain(_marginVerdictLabel, "");
                return;
            }

            // どの地震についての判定かを必ず名乗る。複数同時進行のとき、上の行が
            // 選んでいる地震（SelectPrimary）とここで判定した地震（sim 側の
            // SelectDamagingQuake）は一致しないことがある。
            SetLayer1(_marginBuildingLabel,
                Strings.EarthquakeBuildingUnderCursor + ": #" + margin.BuildingId
                + "   (#" + snapshot.CursorQuakeId + ")");

            SetLayer1(_marginVerdictLabel, VerdictText(margin));
            SetPlain(_marginNoteLabel, Strings.EarthquakeGlobalDiscOnly);
        }

        /// <summary>
        /// 結論の 1 行。**設計書 §3.2 と計画 5.2 の表がそのままこの switch である。**
        /// 断定してよい状態としてはいけない状態を、ここで取り違えないこと。
        /// </summary>
        private static string VerdictText(BuildingMargin margin)
        {
            string current = Strings.EarthquakeCurrentDistance + " "
                             + margin.Distance.ToString("F0") + " m";
            // 「倒壊するのは震央から X m 以内」。X ≦ 0 の建物は、震央に居ても
            // 全体円盤では倒れない（しきい値が 200 以上）。
            string within = Strings.EarthquakeCollapseWithin + " "
                            + margin.CollapseWithin.ToString("F0") + " m";

            switch (margin.Verdict)
            {
                case CollapseVerdict.AlreadyDown:
                    return Strings.EarthquakeAlreadyDown;

                case CollapseVerdict.OutOfRange:
                    // バニラが preRadius で判定自体を打ち切っている領域。
                    return Strings.EarthquakeOutOfRange + "   (" + current + ")";

                case CollapseVerdict.Unknown:
                    // 断層の幾何が読めていない。数値は出すが、判定は出さない。
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictUnknown;

                case CollapseVerdict.InsideFaultZone:
                    // 倒壊距離は出す。しかし「倒れません」とは言わない
                    // （帯の内側は probability = 1 の破壊円盤が別に判定する）。
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeFaultBand + ": " + Strings.EarthquakeFaultInside;

                case CollapseVerdict.WillCollapse:
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictCollapse;

                case CollapseVerdict.Survives:
                    // 全体円盤についてのみの「倒壊しません」。
                    // ここへ来られるのは断層帯の**外側**の建物だけである
                    // （BuildingMargin.Evaluate の分岐順がそれを保証している）。
                    return margin.CollapseWithin > 0f
                        ? within + " / " + current + "   -> " + Strings.EarthquakeVerdictSurvive
                        : current + "   -> " + Strings.EarthquakeVerdictSurviveAnyDistance;

                default:
                    // ★ 既定を「倒壊しません」にしない。将来 CollapseVerdict に
                    //    値が増えてここを直し忘れたとき、黙って生存を断定することに
                    //    なる——この機能がいちばん避けたい壊れ方そのもの。
                    //    知らない結論は「判定できません」に倒す。
                    return Strings.EarthquakeVerdictUnknown;
            }
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
            SetPlain(_phaseLabel, "");
            if (word != null) SetLayer1(_phaseLabel, Strings.EarthquakePhase + ": " + word);
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
            SetPlain(_timeLabel, "");

            if (primary.Phase != EarthquakePhase.Emerging) return;

            if (!primary.ActivationScheduled)
            {
                SetLayer1(_timeLabel,
                    Strings.EarthquakeTimeToShock + ": " + Strings.EarthquakeTimeUnknown);
                return;
            }

            if (primary.ActivationFrame <= snapshot.CurrentFrame) return;

            // 換算は必ず FeatureHost.FramesPerMinute から出す（定数を直書きして
            // 4 倍ずれた前科がある。③、DAYTIME_FRAMES の取り違え）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return;

            float minutes = (primary.ActivationFrame - snapshot.CurrentFrame) / framesPerMinute;
            SetLayer1(_timeLabel, Strings.EarthquakeTimeToShock + ": "
                + minutes.ToString("F0") + " " + Strings.EarthquakeMinutes);
        }

        /// <summary>
        /// カーソル地点の局所係数 s。**全体円盤（probability = 0.02）の話であって、
        /// 断層帯の話ではない**（別行で出す）。
        ///
        /// 半径 R の外は「揺れが 0」ではなく「バニラが判定すらしていない」なので、
        /// <c>0.0</c> と出さずに「圏外」と書く（<see cref="SeismicIntensity.At"/> の doc）。
        /// </summary>
        private static void RefreshCursorRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            if (!haveCursor)
            {
                SetPlain(_cursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            float distance = DistanceXZ(cursor, primary.Epicentre);
            if (!SeismicIntensity.IsInside(distance, primary.Intensity))
            {
                SetLayer1(_cursorLabel,
                    Strings.EarthquakeAtCursor + ": " + Strings.EarthquakeOutOfRange);
                return;
            }

            float s = SeismicIntensity.At(distance, primary.Intensity);
            string band = BandWord(SeismicScale.BandOf(s));
            SetLayer1(_cursorLabel, Strings.EarthquakeAtCursor + ": " + s.ToString("F2")
                + "  [" + SeismicScale.BarOf(s) + "]  " + band);
        }

        /// <summary>
        /// 断層帯。プレハブ 4 値（<c>m_crackLength</c> / <c>m_crackWidth</c>）が読めて
        /// いなければ幾何が確定しないので、**行ごと出さない**。
        /// 「分からない」を「外側」と言い換えない（<see cref="FaultBand.Known"/> の doc）。
        /// </summary>
        private static void RefreshFaultRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            SetPlain(_faultLabel, "");
            SetPlain(_faultNoteLabel, "");

            if (!haveCursor) return;

            var band = new FaultBand(primary.Epicentre.ToVec2(), primary.AngleRadians,
                                     primary.CrackLength, primary.CrackWidth);
            if (!band.Known) return;

            SetLayer1(_faultLabel, Strings.EarthquakeFaultBand + ": "
                + (band.Contains(cursor.ToVec2())
                    ? Strings.EarthquakeFaultInside
                    : Strings.EarthquakeFaultOutside));
            // この注記は必ず併記する（計画の共通規則）。帯は「当たりうる範囲」であって
            // 「当たる場所」ではない。
            SetPlain(_faultNoteLabel, Strings.EarthquakeFaultBandNote);
        }

        /// <summary>
        /// ハザードマップの読み取り。**このメソッドが数値を出せる経路は 1 本だけで、
        /// そこへ至るには 4 つの門を全て通る必要がある。**
        ///
        ///   1. 地震のハザードビューが表示中であること
        ///      （<c>m_hazardAmount</c> は単一グリッドで、表示中のサブモード 1 種類ぶんの
        ///        値しか持たない。<see cref="HazardMapReader"/> のクラス doc）
        ///   2. スナップショットが有効であること（件数が読めていなければ何も断定しない）
        ///   3. <c>Located &amp;&amp; (Emerging|Active)</c> を満たす地震が 1 つ以上あること
        ///      （§A-6。**ここが 0 なら、グリッドは正常に全ゼロである**）
        ///   4. カーソルが地形の上にあり、グリッドの内側であること
        ///
        /// どこで落ちても**数値の代わりに落ちた理由**を出す。これが①の全体レビューが
        /// 突き止めた欠陥（全ゼロのグリッドを「落雷: 0」と表示していた）の直接の修正で、
        /// 地震は同じゲートを持つので同じ扱いが要る。
        /// </summary>
        private static void RefreshHazardRow(EarthquakeSnapshot snapshot, bool hazardViewOn,
                                             bool haveCursor, Vec3 cursor)
        {
            if (!hazardViewOn)
            {
                // 「読み取れません」を使い回さない。原因は特定できていて、
                // しかもワンクリックで直せる（①の ForecastSwitchHazardView と同じ扱い）。
                SetPlain(_hazardLabel, Strings.EarthquakeSwitchHazardView);
                return;
            }

            if (CountPaintingQuakes(snapshot.Quakes) <= 0)
            {
                // ★ ここが本タスクでいちばん重要な分岐。数値は一切出さない。
                SetPlain(_hazardLabel, Strings.EarthquakeNotLocated);
                return;
            }

            if (!haveCursor)
            {
                SetPlain(_hazardLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            bool ok;
            byte value = HazardMapReader.SampleAt(
                new Vector3(cursor.X, cursor.Y, cursor.Z),
                InfoManager.SubInfoMode.EarthquakeHazard, out ok);
            if (!ok)
            {
                SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // ラベルは①と共用する（"Hazard at cursor" は災害種別に依らない文言で、
            // ここでは地震のハザードビューが表示中であることを 1 で確認済み）。
            SetLayer1(_hazardLabel, Strings.ForecastAtCursor + ": " + value
                + "  [" + HazardLevel.BarOf(value) + "]");
        }

        /// <summary>
        /// 今ハザードマップに何かを塗っている地震の数（§A-6 の 2 段ゲート）。
        /// 判定は <see cref="DisasterPhases.PaintsHazardMap(bool, EarthquakePhase)"/> に
        /// 任せる —— このゲートをここで書き直すと、ゲートの定義が 2 箇所に分かれる。
        /// </summary>
        private static int CountPaintingQuakes(IList<EarthquakeReading> quakes)
        {
            int painting = 0;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (DisasterPhases.PaintsHazardMap(quakes[i].Located, quakes[i].Phase)) painting++;
            }
            return painting;
        }

        /// <summary>
        /// 表示の対象にする地震を 1 つ選ぶ。**全ての行が同じ地震を指すようにするため**で、
        /// 「強度は地震 A、カーソルの揺れは地震 B」という混線を防ぐ（複数同時発生は
        /// §E-1 で可能と確定している）。件数が 1 個の通常の場合は何も起きない。
        ///
        /// 優先順: 進行中 &gt; カーソル地点で強く効いている &gt; 強度が大きい &gt; 添字が小さい。
        /// 「進行中」を先に見るのは、Finished の残骸を主役にしないため。
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

        /// <summary>
        /// 区分名。**実在の震度階級の名前は使わない**（設計書 §3.1 / §7-4）。
        /// s は加速度でも計測震度でもなく、ゲームの倒壊係数である。
        /// </summary>
        private static string BandWord(SeismicBand band)
        {
            switch (band)
            {
                case SeismicBand.Weak: return Strings.EarthquakeBandWeak;
                case SeismicBand.Moderate: return Strings.EarthquakeBandModerate;
                case SeismicBand.Strong: return Strings.EarthquakeBandStrong;
                case SeismicBand.Severe: return Strings.EarthquakeBandSevere;
                default: return "";
            }
        }

        /// <summary>
        /// Unity 5.6 の <c>Camera.main</c> はタグ検索で、パネル表示中は毎フレーム
        /// 呼ばれうるパスなのでキャッシュする（①のレビュー指摘）。fake-null
        /// （破棄済みカメラ）を拾えるよう Unity の <c>== null</c> 判定に任せ、
        /// 素の参照比較はしない。
        /// </summary>
        private static Camera _mainCameraCache;

        /// <summary>直近に実際にレイを引いたフレーム（<c>Time.frameCount</c>）と、その結果。</summary>
        private static int _pickFrame;
        private static bool _pickCached;
        private static Vec3 _pickHit;
        private static bool _pickOk;

        /// <summary>
        /// カーソル直下の地面。計算そのものは①の <c>ForecastPanel.TryPickCursorGround</c> と
        /// 同じで、**引く頻度だけ**を <see cref="RepickIntervalFrames"/> で縛ってある。
        ///
        /// **共通化はしていないが、①側も同じ形に揃えてある**（Task 7 で
        /// <c>ForecastPanel.TryPickCursorGround</c> にも同じ間引きを入れた）。
        /// 片方だけ直すと、この MOD に無制限なサンプリング経路が 1 本残る。
        /// 一方を変えるときはもう一方も見ること。
        ///
        /// <c>UIView.IsInsideUI()</c> の 1 行は間引きの**外**に置く。パネルを読んでいる間
        /// ——マウスがパネルの上にある間——はサンプリング自体が起きないので、
        /// これがいちばん効く早期打ち切りであり、キャッシュより先に判定したい。
        /// またこの経路では「カーソルが無効になったこと」を遅らせずに伝えられる
        /// （遅らせると、UI の上にマウスを載せた後も数フレーム古い地点を指し続ける）。
        /// </summary>
        private static bool TryPickCursorGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // パネルやその他の UI の上にマウスがあるときは意味のある地点が無い。
            if (UIView.IsInsideUI())
            {
                // 次にカーソルが地形へ戻ったとき、UI の上に載る前の古い地点を
                // そのまま返さないよう、キャッシュを捨てる。
                _pickCached = false;
                return false;
            }

            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            var cam = _mainCameraCache;
            if (cam == null)
            {
                _pickCached = false;
                return false;
            }

            // ★ ここが持ち越しの是正。最大 501 回の高さサンプリングは
            //    RepickIntervalFrames フレームに 1 回しか走らない。
            int frame = Time.frameCount;
            if (_pickCached && frame - _pickFrame < RepickIntervalFrames)
            {
                hit = _pickHit;
                return _pickOk;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            _pickOk = RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out _pickHit);
            _pickFrame = frame;
            _pickCached = true;

            hit = _pickHit;
            return _pickOk;
        }
    }
}
