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
            CameraShakeBooster.Reset();
            SeismographRecorder.Reset();
            // ★ 予約は都市をまたいで残らない（第 2 層はセッション状態で、セーブにも入れない）。
            TsunamiChain.Reset();
            LongPeriodDamage.Reset();

            // 震度分布オーバーレイ。**main スレッド。** 登録は
            // RenderManager の静的リストへの追加で、外す API が存在しない
            // （OverlayRenderable のクラス doc）ので、この呼び出しは
            // プロセスにつき 1 回しか効かない。以後の都市では
            // 「このセッションでは描いてよい」を立て直すだけになる。
            EarthquakeOverlay.EnsureRegistered();
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

            // 地震計の位置で 1 サンプル取る。**必ずポーズガードより下**。
            // ポーズ中はゲーム内時間が進んでいないので地動も進んでおらず、
            // ここで貯め続けると波形だけが伸びる嘘になる。
            SeismographRecorder.Sample(snapshot, frameIndex);

            // ★ 第 2 層。**既定 OFF**（ModSettings.EarthquakeTsunamiChain の doc）。
            //    設定を見てから呼ぶことで、OFF のときは TsunamiChain の状態が
            //    Idle のまま一切進まない＝パネルにも節が出ない。
            if (ModSettings.EarthquakeTsunamiChain.value)
            {
                TsunamiChain.Tick(snapshot, frameIndex);
            }

            // ★ 第 2 層その 2。**既定 OFF**（ModSettings.EarthquakeLongPeriod の doc）。
            //    これは津波と違い、**バニラなら倒れなかった建物を実際に倒す**。
            //    設定を見てから呼ぶので、OFF のときは走査そのものが 1 回も走らない。
            if (ModSettings.EarthquakeLongPeriod.value)
            {
                LongPeriodDamage.Apply(snapshot, deltaMinutes);
            }
        }

        /// <summary>main スレッド。パネル・ボタンの設置と、表示中のみの内容更新はここから。</summary>
        public void OnMainThreadUpdate()
        {
            // ボタンは DisasterPanelBar が 4 個まとめて持つ（FeatureHost が呼ぶ）。
            EarthquakePanel.Tick();

            // ★ パネルが閉じていても必ず呼ぶ。カメラの揺れはパネルの表示物ではなく、
            //    ゲーム側が毎フレーム消費してリセットする値なので（§A-7、
            //    CameraController.LateUpdate の最後の 1 行が Vector3.zero を書く）、
            //    毎フレーム足し続けない限り効かない。
            CameraShakeBooster.Update();
        }

        public void OnLevelUnloading()
        {
            EarthquakeHub.Clear();
            EarthquakeReader.Reset();
            // 揺れの加算を止める。バニラが毎フレーム m_cameraShake をゼロに戻すので、
            // ここで止めれば残留オフセットは残らない（§A-7）。
            CameraShakeBooster.Reset();
            // 波形は**セーブにも次の都市にも持ち越さない**（設計書 §3.5）。
            SeismographRecorder.Reset();
            // ★ 予約したまま撃っていない津波を、都市をまたいで持ち越さない。
            //    ここを忘れると 2 つ目の都市で、起きてもいない地震の津波が来る。
            TsunamiChain.Reset();
            // ★ 走査の途中状態と診断カウンタ、そして Degraded の自己申告を下ろす。
            LongPeriodDamage.Reset();
            // ★ オーバーレイを止める。登録は外せないので、描かないことを
            //    こちらの状態で保証する（EarthquakeOverlay.Reset の doc）。
            //    これを忘れると、都市を出た直後の数フレームに前の都市の
            //    震央が地図に描かれる。
            EarthquakeOverlay.Reset();
            // 2 つ目の都市が、ボタン 1 個・パネル 1 枚で始まるようにする。
            // EarthquakePanel.Destroy() が波形テクスチャ（Texture2D）も破棄する
            // —— GameObject と違って Unity は勝手に回収しないので、これを
            // 忘れると都市をまたぐたびに 320x80 のテクスチャが 1 枚ずつ残る。
            // ボタンの撤去は FeatureHost.LevelUnloading が DisasterPanelBar.Remove で行う。
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
            WriteShakeBoost(b, snapshot);
            WriteSimClock(b, snapshot);
            WriteSensorCoverage(b, snapshot);
            WriteWaveform(b, snapshot);
            WriteTsunamiChain(b, snapshot);
            WriteLongPeriod(b, snapshot);
            WriteQuakes(b, snapshot);
        }

        /// <summary>
        /// 第 2 層（長周期地震動）の状態。**「建物が余分に倒れたか」の切り分けは
        /// ここでしかできない。** 倒れない理由は 6 通りあり（設定が OFF ／強さ 0 ／
        /// 進行中の地震が Active でない ／範囲内に高層が無い ／高さが読めない ／
        /// バニラが倒壊を断った）、画面上はどれも「何も起きない」で同じ顔になる。
        ///
        /// **倒壊 0 のときも必ず全数字を出す**（③で「延焼が動いているか診断から
        /// 一切見えなかった」失敗を繰り返さない）。
        /// </summary>
        private static void WriteLongPeriod(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            if (!ModSettings.EarthquakeLongPeriod.value)
            {
                b.Line(1, "long period", "off (setting; this is the default)");
                return;
            }

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            b.Line(1, "long period", strength <= 0
                ? "on, but the strength slider is 0 (nothing is added; this is a valid way to "
                  + "disable it without losing the setting)"
                : "on, strength " + strength + " of 10");

            b.Line(2, "model", "wave period "
                + LongPeriodResponse.WavePeriodFrames.ToString("F0")
                + " frames, resonance peak at height "
                + (LongPeriodResponse.WavePeriodFrames
                   / LongPeriodResponse.PeriodFramesPerMetre).ToString("F0")
                + " m, range = " + LongPeriodResponse.RangeFactor.ToString("F0")
                + "x the vanilla disc, ceiling "
                // ★ 0.25 とだけ書かない。時間帯係数はモデルのクランプの**後**に
                //    掛かるので、実際に使われる上限は 0.25 x 1.15 である
                //    （LongPeriodResponse.MaxExtraChance の doc / 第 2 層レビュー M8）。
                + (LongPeriodResponse.MaxExtraChance * 100f).ToString("F1") + "% x up to "
                + TimeOfDayFactor.NightFactor.ToString("F2") + " time-of-day = "
                + (LongPeriodResponse.MaxExtraChance * TimeOfDayFactor.NightFactor * 100f)
                    .ToString("F2") + "% per pass"
                + "  [Disaster + model, not measured]");

            b.Line(2, "passes", LongPeriodDamage.Passes.ToString());
            b.Line(2, "last pass",
                "scanned=" + LongPeriodDamage.LastScanned
                + " selected=" + LongPeriodDamage.LastSelected
                + " attempted=" + LongPeriodDamage.LastAttempted
                + " refused=" + LongPeriodDamage.LastRefused
                + " collapsed=" + LongPeriodDamage.LastCollapsed
                + (LongPeriodDamage.LastCapped ? "  (capped; resumes next pass)" : ""));
            b.Line(2, "total collapsed", LongPeriodDamage.TotalCollapsed.ToString());

            // 高さが読めない建物には何もしていない。0 でないこと自体が合図。
            b.Line(2, "unreadable height", LongPeriodDamage.LastUnknownHeight
                + (LongPeriodDamage.LastUnknownHeight > 0
                    ? "  (these buildings were skipped entirely; the mod never guesses a height)"
                    : ""));

            b.Line(2, "cursor building height", snapshot.CursorBuildingHeight > 0f
                ? snapshot.CursorBuildingHeight.ToString("F1") + " m"
                : "unread (no building under the cursor, or its prefab height is unusable)");

            WriteTimeOfDay(b, snapshot);
        }

        /// <summary>
        /// 時間帯係数（第 2 層その 3）。長周期の追加被害にだけ掛かるので、
        /// <see cref="WriteLongPeriod"/> の中から呼ぶ（独立した設定は無い）。
        ///
        /// **日夜サイクル OFF を隠さない。** その設定では sim スレッドの時刻が
        /// 永久に 12.0 に固定され（§F-1）、係数は黙って 1.00 の定数になる。
        /// これは前提の破れではなくプレイヤーの正当な設定なので <c>Assumptions</c> の
        /// FAIL にはしないが、**黙って無効になったことは必ず名乗る**。
        /// 「係数 1.00」だけを出すと、それは読めた値に見える。
        /// </summary>
        private static void WriteTimeOfDay(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            float hour = snapshot.HourOfDay;
            b.Line(2, "time of day factor",
                TimeOfDayFactor.Of(hour).ToString("F2")
                + "  (hour=" + hour.ToString("F1")
                + " night=" + (TimeOfDayFactor.IsNight(hour) ? "yes" : "no")
                + ", day " + TimeOfDayFactor.DayFactor.ToString("F2")
                + " -> night " + TimeOfDayFactor.NightFactor.ToString("F2")
                + "  [Disaster + model, vanilla has no basis for this])");

            if (!snapshot.DayNightEnabled)
            {
                b.Line(3, "day/night",
                    "OFF: the game pins the hour at 12.0 every sim frame, so this factor is "
                    + "permanently 1.00 and the time of day changes nothing. This is a valid "
                    + "player setting, not a broken assumption");
            }
        }

        /// <summary>
        /// 第 2 層（津波連鎖）の状態。**「津波が来ない」の切り分けはここでしかできない。**
        /// 来ない理由は 5 通りあり（設定が OFF ／震源が陸 ／DLC 無し ／海側外周が足りない ／
        /// 災害スロット満杯）、画面上はどれも「何も起きない」で同じ顔になる。
        ///
        /// **内陸マップの <c>NoSea</c> は失敗ではない**ことを、ここでも文で名乗る（§B-3）。
        /// </summary>
        private static void WriteTsunamiChain(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            if (!ModSettings.EarthquakeTsunamiChain.value)
            {
                b.Line(1, "tsunami chain", "off (setting; this is the default)");
                return;
            }

            string state;
            switch (snapshot.TsunamiState)
            {
                case TsunamiChainState.Scheduled:
                    state = "scheduled for frame " + snapshot.TsunamiDueFrame;
                    break;
                case TsunamiChainState.Raised:
                    state = "raised (a wave was actually created)";
                    break;
                case TsunamiChainState.NoSea:
                    state = "no wave: TsunamiAI.FindSea found no run of 10+ sea cells on the map "
                            + "border. This is normal on an inland map and is NOT a failure";
                    break;
                case TsunamiChainState.NoDlc:
                    state = "no TsunamiAI prefab (the Natural Disasters DLC is not owned)";
                    break;
                case TsunamiChainState.Failed:
                    state = "FAILED (see the EqTsunami* diagnostic lines; the disaster buffer "
                            + "may be full)";
                    break;
                default:
                    state = "idle (no undersea main shock has been observed)";
                    break;
            }

            b.Line(1, "tsunami chain", state);
            b.Line(2, "watching quake", snapshot.TsunamiQuakeId == 0
                ? "none"
                : "#" + snapshot.TsunamiQuakeId);
            b.Line(2, "delay setting",
                ModSettings.EarthquakeTsunamiDelayMinutes.value + " in-game minutes");
        }

        /// <summary>
        /// 波形の観測状態。**「グラフが出ない」の切り分けはここでしかできない。**
        /// 出ない理由は 4 通りあり（記録対象の地震が無い／地震計が 0 個／
        /// サンプルがまだ 0 件／描画経路が使えない）、画面上はどれも「絵が無い」で
        /// 同じ顔になる。
        ///
        /// <c>rendering</c> の行は main スレッドが書いた値をここ（sim スレッド）で
        /// 読んでいる。構築時に 1 回決まったきり変わらない bool なので、
        /// スナップショット経路には載せていない（<c>CameraShakeBooster.LastAdded</c>
        /// と同じ判断）。
        /// </summary>
        private static void WriteWaveform(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            var traces = snapshot.Traces;

            b.Line(1, "waveform", snapshot.WaveformQuakeId == 0
                ? "not recording (no Emerging/Active quake)"
                : "recording quake #" + snapshot.WaveformQuakeId
                  + ", " + traces.Count + " observation point(s)"
                  + (traces.Count == 0
                      ? "  (no Earthquake Sensor exists; the game keeps no ground-motion history "
                        + "of its own, so there is nothing else to plot)"
                      : ""));

            // ★ 「まだ作っていない」を「使えない」と書かない（全体レビュー I6）。
            //    以前は bool 1 個だったので、パネルを一度も開いていない起動直後の
            //    ダンプが「描画不可（最大振幅の行で代替）」と主張していた。
            //    切り分けの手掛かりはこの 1 行しかないので、4 状態のまま出す。
            string rendering;
            switch (WaveformView.State)
            {
                case WaveformViewState.Ready:
                    rendering = "UITextureSprite + Texture2D";
                    break;
                case WaveformViewState.BuildFailed:
                    rendering = "build failed (falls back to the peak amplitude row)";
                    break;
                case WaveformViewState.RenderFailed:
                    rendering = "drawing stopped after a runtime error "
                                + "(falls back to the peak amplitude row)";
                    break;
                default:
                    rendering = "not built yet (the earthquake panel has never been opened)";
                    break;
            }
            b.Line(2, "rendering", rendering);

            for (int i = 0; i < traces.Count; i++)
            {
                var t = traces[i];
                b.Line(2, "#" + t.BuildingId,
                    "distance=" + t.DistanceToEpicentre.ToString("F0") + "m"
                    + " samples=" + t.Count
                    + " newestFrame=" + t.NewestFrame
                    + " peak=" + t.PeakAbsolute.ToString("F3"));
            }
        }

        /// <summary>
        /// カーソル地点の地震計カバレッジ。**震央のカバレッジとは別物**で、
        /// 警報リードタイムを決めるのは震央のほうである（§A-2）。震央の値と
        /// そこから決まるリードタイムは <see cref="WriteQuakes"/> が地震ごとに出す。
        ///
        /// 「読めなかった」を 0 と混ぜない。カバレッジ 0 は「ここに地震計が届いて
        /// いない」という意味のある実測値で、これがハザードマップが空である理由を
        /// 説明する唯一の根拠になる。
        /// </summary>
        private static void WriteSensorCoverage(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            b.Line(1, "sensor coverage at cursor", snapshot.CursorCoverageValid
                ? snapshot.CursorCoverage.ToString()
                : "unread (no valid cursor point, or the resource could not be read)");
        }

        /// <summary>
        /// カメラシェイク補正の状態。**実機で効いているかを確かめる唯一の手段。**
        /// 画面の揺れは目で見ても「強度が入った揺れ」と「バニラの揺れ」を区別できず、
        /// しかも強度 55 では追加分が厳密に 0 になるのが**正しい**——つまり
        /// 「何も起きない」が仕様である状態と、機能が黙って死んでいる状態が、
        /// 見た目では完全に同じになる。だから数値で名乗る。
        ///
        /// <c>added</c> は main スレッドが書いた値をここ（sim スレッド）で読んでいる。
        /// 表示専用の float 1 個で、遅れて読めても意味が壊れないため、
        /// スナップショット経路には載せていない。
        /// </summary>
        private static void WriteShakeBoost(DiagnosticBuilder b, EarthquakeSnapshot snapshot)
        {
            if (!ModSettings.EarthquakeShakeBoost.value)
            {
                b.Line(1, "camera shake boost", "off (setting)");
                return;
            }

            // DisasterManager は sim スレッドの持ち物なので、ここで読むのが正しい。
            string state = "on";
            if (ColossalFramework.Singleton<DisasterManager>.exists
                && ColossalFramework.Singleton<DisasterManager>.instance.m_disableCameraShake)
            {
                state = "on, but the game's 'disable camera shake' option wins (nothing is added)";
            }
            else if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                state = "on, but m_activeDuration is unreadable (nothing is added: the shaking "
                        + "window is unknown, and guessing it would keep shaking after the quake ends)";
            }

            b.Line(1, "camera shake boost", state);
            b.Line(2, "added last frame", CameraShakeBooster.LastAdded.ToString("F3"));
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
            // ボタンは②専用ではなく DisasterPanelBar が 4 個まとめて置く。座標は
            // もうこの MOD が決めていないので、出すのは「居るか」と「どこに居るか」だけ。
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdEarthquake)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");

            // sim スレッドから main の持ち物を読んでいるが、これは InfoModeSwitch の
            // クラス doc が IL 実測つきで明示的に許可している唯一の例外である
            // （get_CurrentMode は単一フィールドの読み出しで、最悪でも 1 tick 古い値）。
            b.Line(1, "showing hazard view", InfoModeSwitch.IsShowingHazard ? "yes" : "no");

            // 震度分布オーバーレイ。**「絵が出ない」の切り分けはここでしかできない。**
            // 出ない理由は 4 通りあり（登録に失敗／トグルが OFF／描くべき地震が無い／
            // 予算切れ）、画面上はどれも「何も出ない」で同じ顔になる。
            //
            // 数値は main スレッド（描画）が書いたものをここ（sim スレッド）で
            // 読んでいる。表示専用の int / bool で、遅れて読めても意味が壊れない
            // （CameraShakeBooster.LastAdded / WaveformView.State と同じ扱い）。
            string overlay;
            if (!EarthquakeOverlay.Registered)
            {
                overlay = "NOT REGISTERED with RenderManager (nothing will ever be drawn)";
            }
            else if (!EarthquakeOverlay.Enabled)
            {
                overlay = "registered, toggled off";
            }
            else
            {
                overlay = "on, " + EarthquakeOverlay.DrawnQuakes + " quake(s), "
                          + EarthquakeOverlay.LastDrawCalls + " of "
                          + EarthquakeOverlay.MaxDrawCallsPerFrame + " draw calls last frame"
                          + (EarthquakeOverlay.FaultGeometryMissing
                              ? "  (fault zone omitted: prefab geometry unreadable)" : "")
                          + (EarthquakeOverlay.BudgetExhausted
                              ? "  (draw budget exhausted; some quakes omitted)" : "");
            }
            b.Line(1, "intensity overlay", overlay);

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

                // 警報リードタイム。カバレッジが読めていないときに 1755（＝カバレッジ 0）を
                // 出すと、それは「地震計が無い」という断定になる。読めていなければ出さない。
                // 換算の guard も含めて FramesWithHours に任せる（カバレッジ 100 なら
                // ちょうど 3.00 in-game hours になるはずで、そこが合っているかを見る行）。
                b.Line(3, "warning lead", q.CoverageKnown
                    ? FramesWithHours((uint)WarningLeadTime.FramesFor(q.CoverageAtEpicentre))
                    : "unknown (the coverage at the epicentre could not be read)");

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
