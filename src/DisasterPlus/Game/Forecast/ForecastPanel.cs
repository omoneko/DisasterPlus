using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Forecast;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 天気予報パネル。main スレッド専用。
    ///
    /// 設計書 §4 のレイアウトに従うが、**§4.2 の訂正を反映する**: ハザード値は
    /// m_hazardAmount という単一の共有グリッドにしか無く、常に「今表示中のサブモード
    /// 1 種類ぶん」しか保持していない（HazardMapReader のクラス doc 参照）。
    /// したがって「カーソル位置のハザード」は落雷・竜巻を同時には出さない。
    /// 今どちらのサブモードが表示されているかを見て、そのラベルの数値だけを出す。
    /// ハザードビューが出ていなければ、数値の代わりに ForecastSwitchHazardView
    /// （原因を特定したヒント）を出す — カーソル位置が取れないだけの場合は
    /// ForecastUnavailable と分けている（RefreshCursorHazard のコメント参照。
    /// レビュー指摘で ForecastUnavailable の使い回しを修正した）。
    /// 表示していない種別のラベルを付けた数値は絶対に出さない。
    ///
    /// 同じ理由で、落雷・竜巻それぞれの見出し行には「傾向」や「レベル」の数値は
    /// 付けない。DisasterManager.m_randomDisastersProbability /
    /// m_randomDisasterCooldown は災害種別に依らない単一の市全体の値であり、
    /// 落雷・竜巻それぞれの見出しの下に同じ値を出すと「別々に測った値がたまたま
    /// 一致した」ように読めてしまう（本タスクが戒める「確信を持って誤った数値」と
    /// 同種の誤解）。そこで確率は落雷・竜巻いずれの見出しにも属さない位置に、
    /// ForecastProbability ラベル付きで 1 回だけ出す — レビュー指摘: ラベル無しの
    /// 裸の数値は文脈から「降水確率」等と誤読される、これもハザード数値の
    /// 誤ラベルと同種の欠陥だと判定された。DisasterManager が居らず読めなかった
    /// 場合は 0.0% という捏造ゼロを出さず、行自体を空にする
    /// （WeatherSnapshot.DisasterInfoAvailable 参照）。
    ///
    /// 「あと何時間で来る」という断定はしない（設計書 4.1）。出すのは傾向
    /// （Strings.TrendRising/Falling/Steady、矢印記号ではなく単語 —
    /// HazardLevel のバーと同じ理由で、CS の UI フォントに矢印グリフがある保証は無い）
    /// と、相対的な高低（確率のパーセント表示）だけ。クールダウン中かどうかは
    /// WriteDiagnostics（開発者向け、ローカライズ対象外のテキスト）でのみ明示する —
    /// 「クールダウン中」という状態を表すための追加ローカライズキーは今回も
    /// 確保していないため、ユーザー向けパネルでは単語化しない。
    ///
    /// API 実測（Task 5、docs/tools/ilload.ps1 で ColossalManaged.dll を確認）:
    /// UIPanel / UILabel / UIButton は UIComponent の width/height/relativePosition/
    /// isVisible/Show()/Hide() をそのまま継承する。UIView.AddUIComponent(Type) は
    /// 非総称のみ実在するので、ここでもキャストして使う（ForecastPanelButton と同じ）。
    /// backgroundSprite の実際の見え方（"MenuPanel2" が存在するか、意図通りに描画されるか）
    /// はアセットのリフレクションでは確認できない。実機でしか分からない
    /// （docs/playtest-checklist.md に追記した確認項目参照）。
    /// </summary>
    public static class ForecastPanel
    {
        private const string PanelName = FreeSlotFinder.SelfPrefix + "ForecastPanel";

        private const float PanelWidth = 380f;
        private const float MaxRayDistance = 8000f;

        private static UIPanel _panel;
        private static UILabel _titleLabel;
        private static UILabel _temperatureLabel;
        private static UILabel _rainLabel;
        private static UILabel _cloudLabel;
        private static UILabel _windLabel;
        private static UILabel _probabilityLabel;
        private static UILabel _ndrNoteLabel;
        private static UILabel _lightningLabel;
        private static UILabel _tornadoLabel;
        private static UILabel _cursorHeaderLabel;
        private static UILabel _cursorValueLabel;

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
        }

        public static void Toggle()
        {
            if (IsVisible) Hide(); else Show();
        }

        /// <summary>main スレッドから毎フレーム。表示中のときだけ内容を更新する。</summary>
        public static void Tick()
        {
            // レビュー指摘: 設定で無効化されたときにパネルが開いたままだと、
            // OnSimulationTick が publish を止めた古いスナップショットを永遠に
            // 出し続ける「凍りついたのに生きて見える」パネルになり、閉じる手段の
            // ボタンも既に撤去済みで消せない。ボタン側（ForecastPanelButton.Tick）
            // と同じガードをここにも置く。
            if (!ModSettings.ForecastEnabled.value)
            {
                if (IsVisible) Hide();
                return;
            }

            if (_panel == null || !_panel.isVisible) return;
            Refresh();
        }

        /// <summary>レベルアンロード時。</summary>
        public static void Destroy()
        {
            if (_panel != null)
            {
                Object.Destroy(_panel.gameObject);
            }

            _panel = null;
            _titleLabel = null;
            _temperatureLabel = null;
            _rainLabel = null;
            _cloudLabel = null;
            _windLabel = null;
            _probabilityLabel = null;
            _ndrNoteLabel = null;
            _lightningLabel = null;
            _tornadoLabel = null;
            _cursorHeaderLabel = null;
            _cursorValueLabel = null;
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
                Log.Error("forecast panel build failed", e);
                Destroy();
            }
        }

        private static void Build()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                Log.Warn("UIView not available; forecast panel not built");
                return;
            }

            // レビュー指摘: 以前は _panel への代入が構築の最後の一行だったため、
            // 途中で例外が出ると EnsureBuilt() の catch が呼ぶ Destroy() は
            // _panel==null を見て何もせず、UIView に取り付け済みの GameObject が
            // 孤児のまま残った（クリックのたびに 1 枚ずつ積み上がる）。
            // ここではローカル変数に保持し、構築失敗時はこの try/catch で
            // 自分の GameObject を確実に破棄してから外側へ再送出する。
            UIPanel panel = null;
            try
            {
                panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                BuildContents(panel);
                _panel = panel;
                Log.Info("forecast panel built");
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
            // 中央付近に出す。ボタンの位置探索とは独立(パネル自体は同時に 1 枚しか無いので
            // 重なり回避の対象にしない)。
            panel.relativePosition = new Vector3(200f, 150f);
            panel.isVisible = false;

            float y = 8f;

            _titleLabel = AddLabel(panel, "Title", 10f, y, PanelWidth - 44f, 24f);
            _titleLabel.textScale = 1.1f;

            var closeButton = (UIButton)panel.AddUIComponent(typeof(UIButton));
            closeButton.name = FreeSlotFinder.SelfPrefix + "ForecastCloseButton";
            closeButton.text = "X";
            closeButton.width = 24f;
            closeButton.height = 24f;
            closeButton.relativePosition = new Vector3(PanelWidth - 32f, y);
            closeButton.normalBgSprite = "ButtonMenu";
            closeButton.hoveredBgSprite = "ButtonMenuHovered";
            closeButton.pressedBgSprite = "ButtonMenuPressed";
            closeButton.eventClick += (c, e) => Hide();

            y += 30f;

            _temperatureLabel = AddLabel(panel, "Temperature", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _rainLabel = AddLabel(panel, "Rain", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _cloudLabel = AddLabel(panel, "Cloud", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _windLabel = AddLabel(panel, "Wind", 12f, y, PanelWidth - 24f, 20f);
            y += 28f;

            // 確率は災害種別に依らない単一値。落雷・竜巻それぞれの見出しの下ではなく、
            // その 2 つより上に 1 回だけ出す(クラス doc 参照)。
            _probabilityLabel = AddLabel(panel, "Probability", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;

            if (ModCompat.NdrPresent)
            {
                _ndrNoteLabel = AddLabel(panel, "NdrNote", 12f, y, PanelWidth - 24f, 32f);
                _ndrNoteLabel.wordWrap = true;
                _ndrNoteLabel.text = Strings.ForecastNdrNote;
                y += 36f;
            }

            y += 6f;

            _lightningLabel = AddLabel(panel, "Lightning", 12f, y, 170f, 24f);
            var lightningButton = AddShowOnMapButton(panel, "LightningShow", y,
                InfoManager.SubInfoMode.LightningHazard);
            lightningButton.tooltip = Strings.ForecastLightning;
            y += 30f;

            _tornadoLabel = AddLabel(panel, "Tornado", 12f, y, 170f, 24f);
            var tornadoButton = AddShowOnMapButton(panel, "TornadoShow", y,
                InfoManager.SubInfoMode.TornadoHazard);
            tornadoButton.tooltip = Strings.ForecastTornado;
            y += 34f;

            _cursorHeaderLabel = AddLabel(panel, "CursorHeader", 12f, y, PanelWidth - 24f, 20f);
            y += 22f;
            _cursorValueLabel = AddLabel(panel, "CursorValue", 12f, y, PanelWidth - 24f, 20f);
            y += 28f;

            panel.height = y;
        }

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y, float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Forecast" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = new Color32(255, 255, 255, 255);
            label.autoSize = false;
            return label;
        }

        private static UIButton AddShowOnMapButton(UIPanel parent, string suffix, float y,
            InfoManager.SubInfoMode subMode)
        {
            var button = (UIButton)parent.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "Forecast" + suffix;
            button.text = Strings.ForecastShowOnMap;
            button.width = 150f;
            button.height = 24f;
            button.relativePosition = new Vector3(PanelWidth - 12f - 150f, y);
            button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
            button.eventClick += (c, e) => InfoModeSwitch.ShowHazard(subMode);
            return button;
        }

        private static void Refresh()
        {
            var snapshot = ForecastHub.Latest;

            _titleLabel.text = Strings.ForecastTitle;
            _lightningLabel.text = Strings.ForecastLightning;
            _tornadoLabel.text = Strings.ForecastTornado;
            _cursorHeaderLabel.text = Strings.ForecastAtCursor;

            if (snapshot == null || !snapshot.Valid)
            {
                _temperatureLabel.text = Strings.ForecastTemperature + ": " + Strings.ForecastUnavailable;
                _rainLabel.text = Strings.ForecastRain + ": " + Strings.ForecastUnavailable;
                _cloudLabel.text = Strings.ForecastCloud + ": " + Strings.ForecastUnavailable;
                _windLabel.text = Strings.ForecastWind + ": " + Strings.ForecastUnavailable;
                // 完全に読めなかった場合（WeatherManager 自体が居ない等）は素直に
                // 「不明」。DisasterManager だけ居ない場合の抑制（下の分岐）とは別の話。
                _probabilityLabel.text = Strings.ForecastProbability + ": " + Strings.ForecastUnavailable;
                RefreshCursorHazard();
                return;
            }

            // 度記号 (deg, U+00B0) は付けない。HazardLevel のバー文字を ASCII に
            // 揃えたのと同じ理由 (CS の UI フォントに ASCII 範囲外のグリフがある保証は無い)。
            _temperatureLabel.text = Strings.ForecastTemperature + ": "
                + snapshot.Temperature.Current.ToString("F1")
                + "  " + TrendWord(snapshot.Temperature.Trend);
            _rainLabel.text = Strings.ForecastRain + ": "
                + snapshot.Rain.Current.ToString("F2")
                + "  " + TrendWord(snapshot.Rain.Trend);
            _cloudLabel.text = Strings.ForecastCloud + ": "
                + snapshot.Cloud.Current.ToString("F2")
                + "  " + TrendWord(snapshot.Cloud.Trend);
            _windLabel.text = Strings.ForecastWind + ": " + WindDirection.LabelOf(snapshot.WindDegrees);

            // 市全体の値。落雷・竜巻どちらの見出しにも属さない(クラス doc 参照)。
            // クールダウン中かどうかというブール状態はここでは単語化しない
            // (ローカライズキー未確保。診断には出す)。
            //
            // レビュー指摘: ラベル無しの裸の "50.0%" は雨・雲の直後に置かれると
            // 「降水確率」等と誤読される。これはハザード数値を表示中でない種別の
            // ラベルで出すのと同種の誤りを、ラベルを付け忘れることで起こしたもの。
            // ForecastProbability ラベルで明示する。
            //
            // *100 は IL 実測で裏付け済み(Task 5)。DefaultSettings.randomDisastersProbability
            // は 0.5(=50%)で、バニラ自身の PopsTelemetryEventFormatting.DisasterProbability も
            // 同じ値に対して `ldc.r4 100 / mul / Mathf.RoundToInt` と全く同じ変換をテレメトリ用に
            // 行っている。したがって m_randomDisastersProbability は 0.0-1.0 の分数であり、
            // *100 して % 表示するのはこちらの独自解釈ではなくゲーム自身の扱いと一致する。
            // ただし DisasterManager.SimulationStepImpl の IL では、実際の tick 毎の発生判定は
            // この値をそのまま使わず(二乗し、都市面積で補正し、乱数と比較する)複雑な式を通す。
            // ここに出すのは「設定された確率」であって「今この瞬間の発生チャンス」の直接値ではない
            // 、という区別は WriteDiagnostics 側のコメントにも書いておく。
            //
            // レビュー指摘: DisasterManager が居ない場合に以前は 0f のまま "0.0%" と表示していた。
            // これは「本当に 0% だった」のか「読めなかった」のか区別が付かない捏造ゼロだった。
            // DisasterInfoAvailable が false のときは行そのものを出さない(数値を一切出さない)。
            if (snapshot.DisasterInfoAvailable)
            {
                float probabilityPercent = snapshot.DisasterProbability * 100f;
                _probabilityLabel.text = Strings.ForecastProbability + ": "
                    + probabilityPercent.ToString("F1") + "%";
            }
            else
            {
                _probabilityLabel.text = "";
            }

            RefreshCursorHazard();
        }

        /// <summary>
        /// カーソル位置のハザード値。今表示中のサブモードだけを試す。
        /// 表示していない種別のラベルを付けた数値は絶対に出さない
        /// (HazardMapReader.SampleAt が ok=false で保証する)。
        ///
        /// レビュー指摘: 以前はハザードビューが出ていない場合も ForecastUnavailable
        /// （「気象データを読み取れません」）を使い回していたが、これは誤り。
        /// 気象データ自体は生きており(4 行上の温度・雨・雲・風は表示できている)、
        /// 出せないのはハザード数値だけで、原因も「ハザードビューが出ていない」と
        /// 特定できている(§4.2)。しかもパネルを読んでいる間はマウスがほぼ確実に
        /// パネル自身の上にあり(UIView.IsInsideUI()==true)、地形上のカーソル判定は
        /// 常に失敗する——つまりユーザーが最も頻繁に見る行がこれになる。そこで
        /// 「今表示中のハザードビューがあるか」をカーソル位置を問う前に先に見て、
        /// 無ければ原因を特定したヒント(ForecastSwitchHazardView)を即座に出す。
        /// カーソル位置が取れない(UI 上・地形の外)のに何らかのハザードビューは
        /// 出ている、という場合だけ ForecastUnavailable(=カーソル位置が不明)を使う。
        /// </summary>
        private static void RefreshCursorHazard()
        {
            if (!InfoModeSwitch.IsShowingHazard)
            {
                _cursorValueLabel.text = Strings.ForecastSwitchHazardView;
                return;
            }

            Vec3 hit;
            if (!TryPickCursorGround(out hit))
            {
                _cursorValueLabel.text = Strings.ForecastUnavailable;
                return;
            }

            var worldPos = new Vector3(hit.X, hit.Y, hit.Z);

            bool ok;
            byte value = HazardMapReader.SampleAt(worldPos, InfoManager.SubInfoMode.LightningHazard, out ok);
            if (ok)
            {
                _cursorValueLabel.text = Strings.ForecastLightning + ": " + value
                    + "  [" + HazardLevel.BarOf(value) + "]";
                return;
            }

            value = HazardMapReader.SampleAt(worldPos, InfoManager.SubInfoMode.TornadoHazard, out ok);
            if (ok)
            {
                _cursorValueLabel.text = Strings.ForecastTornado + ": " + value
                    + "  [" + HazardLevel.BarOf(value) + "]";
                return;
            }

            // IsShowingHazard は true だが、表示中のサブモードが落雷・竜巻の
            // どちらでもない(洪水・隕石・地盤沈下・地震・森林火災のハザード等)。
            // この機能が扱う 2 種の外なので、原因を「切り替えてください」と
            // 断定するのは不正確。汎用の不明扱いに留める。
            _cursorValueLabel.text = Strings.ForecastUnavailable;
        }

        /// <summary>
        /// Unity 5.6 の Camera.main はタグ検索で、パネル表示中は毎フレーム呼ばれうる
        /// パスなのでキャッシュする(レビュー指摘)。fake-null(破棄済みカメラ)を
        /// 拾えるよう Unity の == null 判定に任せ、素の参照比較はしない。
        /// </summary>
        private static Camera _mainCameraCache;

        private static bool TryPickCursorGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // パネルやその他の UI の上にマウスがあるときは意味のある地点が無い。
            if (UIView.IsInsideUI()) return false;

            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            var cam = _mainCameraCache;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            return RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out hit);
        }

        private static string TrendWord(Trend trend)
        {
            switch (trend)
            {
                case Trend.Rising: return Strings.TrendRising;
                case Trend.Falling: return Strings.TrendFalling;
                default: return Strings.TrendSteady;
            }
        }
    }
}
