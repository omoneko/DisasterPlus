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
    /// ハザードビューが出ていなければ、数値の代わりに ForecastUnavailable を出す。
    /// 表示していない種別のラベルを付けた数値は絶対に出さない。
    ///
    /// 同じ理由で、落雷・竜巻それぞれの見出し行には「傾向」や「レベル」の数値は
    /// 付けない。DisasterManager.m_randomDisastersProbability /
    /// m_randomDisasterCooldown は災害種別に依らない単一の市全体の値であり、
    /// 落雷・竜巻それぞれの見出しの下に同じ値を出すと「別々に測った値がたまたま
    /// 一致した」ように読めてしまう（本タスクが戒める「確信を持って誤った数値」と
    /// 同種の誤解）。そこで確率は落雷・竜巻いずれの見出しにも属さない位置に 1 回だけ出す。
    ///
    /// 「あと何時間で来る」という断定はしない（設計書 4.1）。出すのは傾向
    /// （Strings.TrendRising/Falling/Steady、矢印記号ではなく単語 —
    /// HazardLevel のバーと同じ理由で、CS の UI フォントに矢印グリフがある保証は無い）
    /// と、相対的な高低（確率のパーセント表示）だけ。クールダウン中かどうかは
    /// WriteDiagnostics（開発者向け、ローカライズ対象外のテキスト）でのみ明示する —
    /// 「クールダウン中」という状態を表すための追加ローカライズキーがブリーフの
    /// 18 件に含まれていないため、ユーザー向けパネルでは単語化しない
    /// （無いキーを勝手に増やすと 26→44 の契約が崩れる）。
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

            var panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
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

            _panel = panel;
            Log.Info("forecast panel built");
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
                _probabilityLabel.text = Strings.ForecastUnavailable;
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
            // *100 は IL 実測で裏付け済み(Task 5)。DefaultSettings.randomDisastersProbability
            // は 0.5(=50%)で、バニラ自身の PopsTelemetryEventFormatting.DisasterProbability も
            // 同じ値に対して `ldc.r4 100 / mul / Mathf.RoundToInt` と全く同じ変換をテレメトリ用に
            // 行っている。したがって m_randomDisastersProbability は 0.0-1.0 の分数であり、
            // *100 して % 表示するのはこちらの独自解釈ではなくゲーム自身の扱いと一致する。
            // ただし DisasterManager.SimulationStepImpl の IL では、実際の tick 毎の発生判定は
            // この値をそのまま使わず(二乗し、都市面積で補正し、乱数と比較する)複雑な式を通す。
            // ここに出すのは「設定された確率」であって「今この瞬間の発生チャンス」の直接値ではない
            // 、という区別は WriteDiagnostics 側のコメントにも書いておく。
            float probabilityPercent = snapshot.DisasterProbability * 100f;
            _probabilityLabel.text = probabilityPercent.ToString("F1") + "%";

            RefreshCursorHazard();
        }

        /// <summary>
        /// カーソル位置のハザード値。今表示中のサブモードだけを試す。
        /// 表示していない種別のラベルを付けた数値は絶対に出さない
        /// (HazardMapReader.SampleAt が ok=false で保証する)。
        /// </summary>
        private static void RefreshCursorHazard()
        {
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

            _cursorValueLabel.text = Strings.ForecastUnavailable;
        }

        private static bool TryPickCursorGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // パネルやその他の UI の上にマウスがあるときは意味のある地点が無い。
            if (UIView.IsInsideUI()) return false;

            var cam = Camera.main;
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
