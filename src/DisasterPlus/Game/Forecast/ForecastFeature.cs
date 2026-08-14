namespace DisasterPlus.Game
{
    /// <summary>
    /// ①天気予報タブ。バニラのハザードヒートマップは自前で描かず流用し
    /// （InfoModeSwitch / HazardMapReader）、本機能が足すのは時間軸（傾向）と
    /// ハザード値の数値化だけ（設計書 2）。
    /// </summary>
    public class ForecastFeature : IDisasterFeature
    {
        public const string FeatureName = "Forecast";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            ForecastHub.Clear();
            // ボタンの設置はここでは試みない。UIView がこの時点でまだ準備できていない
            // ことがあるので、③のパネルボタンと同じく OnMainThreadUpdate の間引きに任せる。
        }

        /// <summary>
        /// sim スレッド。WeatherManager / DisasterManager の読み取りは必ずここで行う。
        /// main スレッドから直接触ると、スタックトレースの出ない
        /// IndexOutOfRangeException が後から出る（WeatherReader のクラス doc 参照）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.ForecastEnabled.value) return;

            var snapshot = WeatherReader.Read();
            ForecastHub.Publish(snapshot);

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Forecast, "forecast",
                snapshot.Valid
                    ? "temp=" + snapshot.Temperature.Current.ToString("F1")
                      + " rain=" + snapshot.Rain.Current.ToString("F2")
                      + " cloud=" + snapshot.Cloud.Current.ToString("F2")
                      + " trend=" + snapshot.Temperature.Trend
                    : "snapshot invalid");
        }

        /// <summary>main スレッド。パネル・ボタンの設置と、表示中のみの内容更新はここから。</summary>
        public void OnMainThreadUpdate()
        {
            ForecastPanelButton.Tick();
            ForecastPanel.Tick();
        }

        public void OnLevelUnloading()
        {
            ForecastHub.Clear();
            ForecastPanelButton.Remove();
            ForecastPanel.Destroy();
        }

        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.ForecastEnabled.value ? "yes" : "no");

            var snapshot = ForecastHub.Latest;
            b.Line(1, "snapshot", snapshot == null ? "none yet" : (snapshot.Valid ? "valid" : "INVALID"));

            if (snapshot != null && snapshot.Valid)
            {
                b.Line(2, "temperature", snapshot.Temperature.Current.ToString("F1")
                    + " -> " + snapshot.Temperature.Target.ToString("F1")
                    + "  " + snapshot.Temperature.Trend);
                b.Line(2, "rain", snapshot.Rain.Current.ToString("F2")
                    + " -> " + snapshot.Rain.Target.ToString("F2")
                    + "  " + snapshot.Rain.Trend);
                b.Line(2, "cloud", snapshot.Cloud.Current.ToString("F2")
                    + " -> " + snapshot.Cloud.Target.ToString("F2")
                    + "  " + snapshot.Cloud.Trend);
                // これは「設定された確率」(m_randomDisastersProbability、0.0-1.0 の分数。
                // *100 の妥当性はバニラの PopsTelemetryEventFormatting.DisasterProbability と
                // 同じ変換であることを IL 実測済み、ForecastPanel 側のコメント参照)であって、
                // DisasterManager.SimulationStepImpl が実際に tick 毎の発生判定へ使う値
                // (この値を二乗し面積で補正してから乱数と比較する)そのものではない。
                //
                // DisasterInfoAvailable が false のときは 0f のまま「読めなかった」を
                // 表しており、"0.0%" とだけ出すと本物のゼロ読み取りと見分けが付かない
                // (レビュー指摘)。ここは開発者向けテキストなので明示的に unavailable と書く。
                b.Line(2, "disaster probability (configured)", snapshot.DisasterInfoAvailable
                    ? (snapshot.DisasterProbability * 100f).ToString("F1") + "%"
                    : "unavailable (DisasterManager not present)");
                b.Line(2, "disaster cooldown", !snapshot.DisasterInfoAvailable
                    ? "unavailable"
                    : (snapshot.DisasterCooldown > 0 ? "active (" + snapshot.DisasterCooldown + ")" : "none"));
            }

            string placement;
            if (!ForecastPanelButton.Installed)
            {
                placement = "button not installed yet";
            }
            else if (ForecastPanelButton.UsedSavedPosition)
            {
                placement = "saved position reused";
            }
            else
            {
                placement = ForecastPanelButton.FoundFreeSlot
                    ? "fresh free-slot search succeeded"
                    : "fresh free-slot search FAILED (fell back to preferred position)";
            }
            b.Line(1, "button position", ModSettings.ForecastButtonX.value + ","
                + ModSettings.ForecastButtonY.value + "  (" + placement + ")");

            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");
        }
    }
}
