using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ②地震。バニラが既に持っている決定論的な強度モデル（震央距離の線形ランプと、
    /// 建物ごとに固定された乱数しきい値）を**そのまま可視化**する機能。
    ///
    /// このタスク（Task 3）の時点では**パネルの無い機能**である。やることは
    /// sim スレッドで読んで <see cref="EarthquakeHub"/> へ publish することと、
    /// **プレハブ 4 値を診断ダンプに出すこと**だけ。その 4 値
    /// （<c>m_crackLength</c> / <c>m_crackWidth</c> / <c>m_emergingDuration</c> /
    /// <c>m_activeDuration</c>）は **DLL に実数値が無く**（IL 事実文書 §A-0）、
    /// ②の以後の持続時間の設計が全てその上に乗るので、先に実機で 1 回測る。
    ///
    /// <see cref="IPausedTickFeature"/> を実装しているのは①と同じ理由
    /// （ロード直後にポーズしたままパネルを開くと全行が「読み取れません」になる）。
    /// **ただし②はやがてゲームの状態を進める**（Task 9 の津波生成、Task 10 の追加被害）。
    /// その契約を守る仕掛けは <see cref="OnSimulationTick"/> の中にある。
    /// </summary>
    public class EarthquakeFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Earthquake";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            EarthquakeHub.Clear();
            EarthquakeReader.Reset();
        }

        /// <summary>
        /// sim スレッド。<c>DisasterManager</c> / <c>ImmaterialResourceManager</c> /
        /// <c>SimulationManager</c> の読み取りは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.EarthquakeEnabled.value) return;

            // ここまでが「読んで publish するだけ」。ポーズ中もここは通る。
            var snapshot = EarthquakeReader.Read();
            EarthquakeHub.Publish(snapshot);

            // Earthquake チャンネルは既定 OFF。この if が無いと、下の ToString と
            // 文字列連結が毎 sim tick（通常速度でおよそ 50 回/秒）実行されてから
            // Log.Diag に捨てられる——C# は引数を呼び出し前に評価し切るので、
            // Diag の内側のマスク判定では手遅れになる。
            //
            // ①の ForecastFeature と違い、ここは early-return にしてはいけない。
            // 下のポーズガードと第 2 層の処理を丸ごと飛ばすことになる。
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "earthquake",
                    snapshot.Valid
                        ? "quakes=" + snapshot.Quakes.Count
                          + " hour=" + snapshot.HourOfDay.ToString("F1")
                          + " dayNight=" + (snapshot.DayNightEnabled ? "on" : "off")
                        : "snapshot invalid");
            }

            // ★ ここから下は状態を進める。ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    Task 9 / Task 10 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に地震の被害が進む」が起きる。
            if (deltaMinutes <= 0f) return;

            // （Task 9: TsunamiChain.Tick / Task 10: LongPeriodDamage.Apply がここに入る）
        }

        /// <summary>main スレッド。パネル・ボタンの設置と、表示中のみの内容更新はここから。</summary>
        public void OnMainThreadUpdate()
        {
            EarthquakePanelButton.Tick();
            EarthquakePanel.Tick();
        }

        public void OnLevelUnloading()
        {
            EarthquakeHub.Clear();
            EarthquakeReader.Reset();
            // 2 つ目の都市が、ボタン 1 個・パネル 1 枚で始まるようにする。
            EarthquakePanelButton.Remove();
            EarthquakePanel.Destroy();
        }

        /// <summary>
        /// **このタスクの主目的。** プレハブ 4 値と sim スレッドの時計、そして
        /// 進行中の地震の生の値を、そのままダンプに出す。
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.EarthquakeEnabled.value ? "yes" : "no");

            var snapshot = EarthquakeHub.Latest;
            b.Line(1, "snapshot", snapshot == null ? "none yet" : (snapshot.Valid ? "valid" : "INVALID"));

            // UI の状態は snapshot の有無に関わらず出す。「パネルが開かない」
            // 「ボタンが予報ボタンに重なった」の調査に、地震が起きている必要は無い。
            WriteUiState(b, snapshot);

            if (snapshot == null || !snapshot.Valid) return;

            WritePrefabFacts(b, snapshot.Prefab);
            WriteSimClock(b, snapshot);
            WriteQuakes(b, snapshot);
        }

        /// <summary>
        /// ボタンの配置経緯とハザードビューの状態。
        ///
        /// <c>painting quakes</c> の行が**このセクションでいちばん重要**。
        /// 地震のハザードマップも <c>Located &amp;&amp; (Emerging|Active)</c> の 2 段ゲートを持ち
        /// （§A-6）、地震に <c>Located</c> を立てられるのは地震計だけ（§A-2）なので、
        /// ヒートマップが真っ白なとき「地震計が無い（正常）」のか「本当に地震が無い」のか
        /// を切り分ける手段がこれ以外に無い。
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            string placement;
            if (!EarthquakePanelButton.Installed)
            {
                placement = "button not installed yet";
            }
            else if (EarthquakePanelButton.UsedSavedPosition)
            {
                placement = "saved position reused";
            }
            else
            {
                placement = EarthquakePanelButton.FoundFreeSlot
                    ? "fresh free-slot search succeeded"
                    : "fresh free-slot search FAILED (fell back to preferred position)";
            }
            b.Line(1, "button position", ModSettings.EarthquakeButtonX.value + ","
                + ModSettings.EarthquakeButtonY.value + "  (" + placement + ")");

            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");

            // DLC が無い環境ではパネル本体を構築していない（EarthquakePanel._bodyBuilt）。
            b.Line(1, "panel body", ModCompat.NaturalDisastersOwned
                ? "shown"
                : "hidden (Natural Disasters DLC not owned)");

            if (snapshot == null || !snapshot.Valid)
            {
                b.Line(1, "painting quakes", "unknown (no valid snapshot)");
                return;
            }

            int painting = 0;
            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (DisasterPhases.PaintsHazardMap(quakes[i].Located, quakes[i].Phase)) painting++;
            }
            b.Line(1, "painting quakes", painting + " of " + quakes.Count
                + (painting == 0
                    ? "  (the hazard map is legitimately empty; an Earthquake Sensor is what sets Located)"
                    : ""));
        }

        private static void WritePrefabFacts(DiagnosticBuilder b, EarthquakePrefabFacts prefab)
        {
            if (!prefab.Resolved)
            {
                // DLC 非所持環境ではこれが正常。Assumptions 側の impact 文と同じ扱い。
                b.Line(1, "prefab (EarthquakeAI)",
                    "NOT RESOLVED (expected when the Natural Disasters DLC is not owned)");
                return;
            }

            b.Line(1, "prefab (EarthquakeAI)", "resolved");
            b.Line(2, "m_crackLength", prefab.CrackLength.ToString("F2"));
            b.Line(2, "m_crackWidth", prefab.CrackWidth.ToString("F2"));
            b.Line(2, "m_emergingDuration", FramesWithHours(prefab.EmergingDuration));
            b.Line(2, "m_activeDuration", FramesWithHours(prefab.ActiveDuration));
        }

        private static void WriteSimClock(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            // 日夜サイクル OFF は前提の破れではなくプレイヤーの正当な設定なので
            // Assumptions の FAIL にはしない（偽 FAIL を出さない）。代わりに
            // 「12.0 という数字がどこから来ているか」をここで必ず名乗る。
            string clock = "hour=" + snapshot.HourOfDay.ToString("F1")
                + "  dayNight=" + (snapshot.DayNightEnabled ? "on" : "off");
            if (!snapshot.DayNightEnabled)
            {
                clock += " (hour is pinned at 12.0 by the game while day/night is off)";
            }
            b.Line(1, "sim clock", clock);
        }

        private static void WriteQuakes(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            b.Line(1, "active quakes", quakes.Count.ToString());

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];

                b.Line(2, "#" + q.DisasterId,
                    "intensity=" + q.Intensity
                    + " phase=" + q.Phase
                    + " located=" + (q.Located ? "yes" : "no")
                    + " coverage=" + (q.CoverageKnown ? q.CoverageAtEpicentre.ToString() : "unreadable")
                    + " R=" + q.Radius.ToString("F1"));

                // m_activationFrame == 0 は「今」ではなく「未定」。SelfTrigger(64) が
                // 立っていない地震はここが 0 のまま Emerging で永久に固まる（§A-1）。
                string activation = q.ActivationScheduled
                    ? q.ActivationFrame.ToString()
                    : "0 (not scheduled - SelfTrigger was never set)";
                b.Line(3, "frames", "start=" + q.StartFrame + " activation=" + activation);

                b.Line(3, "fault (L/W)", q.CrackLength <= 0f && q.CrackWidth <= 0f
                    ? "unknown (prefab not resolved)"
                    : q.CrackLength.ToString("F1") + " / " + q.CrackWidth.ToString("F1"));
            }
        }

        /// <summary>
        /// フレーム数を「そのままの値 ＋ ゲーム内時間」で出す。
        ///
        /// 換算は必ず <see cref="FeatureHost.FramesPerMinute"/> から出すこと。
        /// 定数を直書きして 4 倍ずれた前科がある（③、DAYTIME_FRAMES の取り違え）。
        /// </summary>
        private static string FramesWithHours(uint frames)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return frames + " frames";

            float hours = frames / framesPerMinute / 60f;
            return frames + " frames (= " + hours.ToString("F2") + " in-game hours)";
        }
    }
}
