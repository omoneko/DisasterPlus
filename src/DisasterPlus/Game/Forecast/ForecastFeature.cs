namespace DisasterPlus.Game
{
    /// <summary>
    /// ①天気予報タブ。バニラのハザードヒートマップは自前で描かず流用し
    /// （InfoModeSwitch / HazardMapReader）、本機能が足すのは時間軸（傾向）と
    /// ハザード値の数値化、そして**ハザードマップが何を意味しているかの説明**
    /// （設計書 2、および全体レビューでの前提の訂正 — ForecastPanel のクラス doc 参照）。
    ///
    /// IPausedTickFeature を実装しているのは、ロード直後にポーズしたままでも
    /// パネルが「読み取れません」で埋まらないようにするため。本機能は
    /// WeatherReader で読んで ForecastHub へ publish するだけでゲームの状態を
    /// 一切進めないので、この印を名乗る条件を満たす（そちらの doc 参照）。
    /// </summary>
    public class ForecastFeature : IDisasterFeature, IPausedTickFeature
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
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（IPausedTickFeature）。
        /// deltaMinutes は使っていないので、それで挙動は変わらない。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.ForecastEnabled.value) return;

            var snapshot = WeatherReader.Read();
            ForecastHub.Publish(snapshot);

            // Forecast チャンネルは既定 OFF。この if が無いと、下の 5 回の ToString と
            // 文字列連結が毎 sim tick（通常速度でおよそ 50 回/秒）実行されてから
            // Log.Diag に捨てられる——C# は引数を呼び出し前に評価し切るので、
            // Diag の内側のマスク判定では手遅れになる（全体レビュー指摘）。
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Forecast)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Forecast, "forecast",
                snapshot.Valid
                    ? "temp=" + snapshot.Temperature.Current.ToString("F1")
                      + " rain=" + snapshot.Rain.Current.ToString("F2")
                      + " cloud=" + snapshot.Cloud.Current.ToString("F2")
                      + " fog=" + snapshot.Fog.Current.ToString("F2")
                      + " trend=" + snapshot.Temperature.Trend
                      + " locatedStorms=" + snapshot.LocatedLightningStorms
                      + "/" + snapshot.LocatedTornadoes
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
                b.Line(2, "fog", snapshot.Fog.Current.ToString("F2")
                    + " -> " + snapshot.Fog.Target.ToString("F2")
                    + "  " + snapshot.Fog.Trend);

                // ハザードマップが「空」なのか「本当にリスクが低い」のかを
                // テスターが切り分けられるようにする。0/0 なら、どこにカーソルを
                // 置いてもグリッドは 0 で、それが正常な状態
                // （WeatherSnapshot.LocatedLightningStorms の doc 参照）。
                b.Line(2, "located storms (lightning/tornado)", snapshot.DisasterInfoAvailable
                    ? snapshot.LocatedLightningStorms + " / " + snapshot.LocatedTornadoes
                    : "unavailable (DisasterManager not present)");
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

            // sim スレッドから main の持ち物を読んでいるが、これは InfoModeSwitch の
            // クラス doc が IL 実測つきで明示的に許可している唯一の例外である
            // （get_CurrentMode は単一フィールドの読み出しで、最悪でも 1 tick 古い値）。
            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");

            // ハザードの半分は DLC 依存（I2）。無い環境では「マップに表示」も
            // カーソル位置の数値もパネルに出していないので、それが意図どおりか
            // ダンプから分かるようにする。
            b.Line(1, "hazard rows", ModCompat.NaturalDisastersOwned
                ? "shown"
                : "hidden (Natural Disasters DLC not owned)");
        }
    }
}
