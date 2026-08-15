using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④台風。バニラの雷雨災害スロット 1 個を土台に、④が経路・天候・落雷・風害・
    /// 河川氾濫・巨大な回転雲を毎 tick 駆動する**移動する台風**。
    ///
    /// **①②と違い、④はバニラに原資が無い。** 台風という現象はバニラに存在せず、
    /// 風による破壊機構も、ワールド座標を持つ雲も、洪水災害も存在しない
    /// （IL 事実文書 §B5 / §C7 / §D9）。したがって④が出す数値は**原則すべて本 MOD のもの**で、
    /// パネルは見出しで一度だけそう名乗り、行ごとの印は付けない（設計書 §1.2 / §7）。
    /// 例外は <c>WeatherManager</c> から読んだ雨量・雲量だけである。
    ///
    /// このタスク（Task 2）の時点では**パネルも台風も無い機能**である。やることは
    /// sim スレッドで読んで <see cref="TyphoonHub"/> へ publish することと、
    /// **プレハブ 6 値を診断ダンプに出すこと**だけ。その 6 値
    /// （<c>ThunderStormAI</c> の <c>m_radius</c> / <c>m_emergingDuration</c> /
    /// <c>m_activeDuration</c> と、<c>VortexAI</c> の <c>m_destructionRadiusMin</c> /
    /// <c>m_destructionRadiusMax</c>、<c>VehicleInfo.m_maxSpeed</c>）は
    /// **DLL に実数値が無く**（§A-0 / §B-1、どちらも PARTIAL）、④の以後の
    /// 持続時間・落雷本数・破壊半径・移動速度が全てその上に乗るので、先に実機で 1 回測る。
    ///
    /// <see cref="IPausedTickFeature"/> を実装しているのは①②と同じ理由
    /// （ロード直後にポーズしたままパネルを開くと全行が「読み取れません」になる）。
    /// **ただし④は T3 以降でゲームの状態を進める。** その契約を守る仕掛けは
    /// <see cref="OnSimulationTick"/> の中にある。
    /// </summary>
    public class TyphoonFeature : IDisasterFeature, IPausedTickFeature
    {
        public const string FeatureName = "Typhoon";

        public string Name { get { return FeatureName; } }

        public void OnLevelLoaded()
        {
            TyphoonHub.Clear();
            TyphoonReader.Reset();
            TyphoonController.Reset();
        }

        /// <summary>
        /// sim スレッド。<c>DisasterManager</c> / <c>WeatherManager</c> /
        /// <c>SimulationManager</c> の読み取りは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.TyphoonEnabled.value) return;

            // ここまでが「読んで publish するだけ」。ポーズ中もここは通る。
            var snapshot = TyphoonReader.Read();
            TyphoonHub.Publish(snapshot);

            // Typhoon チャンネルは既定 OFF。この if が無いと、下の ToString と
            // 文字列連結が毎 sim tick（通常速度でおよそ 50 回/秒）実行されてから
            // Log.Diag に捨てられる——C# は引数を呼び出し前に評価し切るので、
            // Diag の内側のマスク判定では手遅れになる。
            //
            // ①の ForecastFeature と違い、ここは early-return にしてはいけない。
            // 下のポーズガードと全要素の処理を丸ごと飛ばすことになる
            // （②の EarthquakeFeature が同じ注記を持っている）。
            if (Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon))
            {
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "typhoon",
                    snapshot.Valid
                        ? "prefab=" + (snapshot.Prefab.Usable ? "usable" : "UNUSABLE")
                          + " rain=" + snapshot.Rain.ToString("F2")
                          + " cloud=" + snapshot.Cloud.ToString("F2")
                        : "snapshot invalid");
            }

            // ★ ここから下は状態を進める。ポーズ中（deltaMinutes == 0）は絶対に通さない。
            //    T3〜T10 が足す処理は必ずこの行より下に置くこと。
            //    このコメントを消すと「ポーズ中に台風が動き、建物が倒れ、川が溢れる」が起きる。
            if (deltaMinutes <= 0f) return;

            TyphoonController.Tick(snapshot, frameIndex, deltaMinutes);

            // （T4: TyphoonWeather.Drive / T6: TyphoonLightning.Tick /
            //   T7: TyphoonWind.Apply / T8: TyphoonFlood.Tick /
            //   T10: TyphoonTornado.Tick がここに入る）
        }

        /// <summary>main スレッド。T5 でパネルとボタンが入る。</summary>
        public void OnMainThreadUpdate()
        {
        }

        public void OnLevelUnloading()
        {
            TyphoonHub.Clear();
            TyphoonReader.Reset();
            // 予約も進行中の台風も都市をまたいで残らない。
            TyphoonController.Reset();
        }

        /// <summary>
        /// **このタスクの主目的。** プレハブ 6 値と、そこから導かれる半径・速度、
        /// そして天候の実測値をそのままダンプに出す。
        /// </summary>
        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.TyphoonEnabled.value ? "yes" : "no");

            var snapshot = TyphoonHub.Latest;
            b.Line(1, "snapshot", snapshot == null
                ? "none yet"
                : (snapshot.Valid ? "valid" : "INVALID"));

            if (snapshot == null || !snapshot.Valid) return;

            WriteStormPrefab(b, snapshot.Prefab);
            WriteVortexPrefab(b, snapshot.Prefab);
            WriteWeather(b, snapshot);
            WriteTyphoon(b, snapshot);
        }

        /// <summary>
        /// 台風そのもの。
        ///
        /// **<c>refusal</c> は必ず出す。** 「起こせなかった」を「何も起きていない」と
        /// 見分ける手段がここにしか無い（計画 §3 Step 5）。
        /// </summary>
        private static void WriteTyphoon(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.Active)
            {
                b.Line(1, "typhoon", "idle");
                if (!string.IsNullOrEmpty(snapshot.Refusal))
                {
                    b.Line(2, "refusal", snapshot.Refusal);
                }
                return;
            }

            b.Line(1, "typhoon", "active  #" + snapshot.TyphoonId
                                 + "  phase=" + snapshot.Phase
                                 + "  intensity=" + snapshot.Intensity);

            b.Line(2, "centre", "(" + snapshot.Centre.X.ToString("F0")
                                + ", " + snapshot.Centre.Y.ToString("F0")
                                + ", " + snapshot.Centre.Z.ToString("F0")
                                + ")  heading=" + DegreesOf(snapshot.HeadingRadians).ToString("F1")
                                + " deg");

            b.Line(2, "elapsed", ElapsedText(snapshot.ElapsedFrames, snapshot.TotalFrames));

            b.Line(2, "radius", "storm " + snapshot.StormRadius.ToString("F0")
                                + " m / gale " + snapshot.GaleRadius.ToString("F0") + " m");

            // 「読めなかった」と「0 分後」を混ぜない。
            b.Line(2, "landfall", snapshot.OverLand
                ? "already over land"
                : (snapshot.LandfallKnown
                    ? "in " + snapshot.MinutesToLandfall.ToString("F1") + " in-game minutes"
                    : "not within the forecast window (it may pass over water only)"));

            b.Line(2, "over land", snapshot.OverLand ? "yes" : "no");

            if (!string.IsNullOrEmpty(snapshot.Refusal))
            {
                b.Line(2, "last refusal", snapshot.Refusal);
            }
        }

        private static float DegreesOf(float radians)
        {
            return radians * 57.29578f;
        }

        private static string ElapsedText(uint elapsed, uint total)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return elapsed + " / " + total + " frames";

            float elapsedHours = elapsed / framesPerMinute / 60f;
            float totalHours = total / framesPerMinute / 60f;
            return elapsed + " / " + total + " frames (= "
                   + elapsedHours.ToString("F2") + " / " + totalHours.ToString("F2")
                   + " in-game hours)";
        }

        /// <summary>
        /// 嵐プレハブの 3 実測値と、そこから導かれる 2 値。
        ///
        /// **読めないときは導出値の 2 行を出さない。** 行の有無そのものが
        /// 「推測した半径や速度を表示していない」ことの証拠になる（設計書 §6）。
        /// </summary>
        private static void WriteStormPrefab(DiagnosticBuilder b, TyphoonPrefabFacts prefab)
        {
            if (!prefab.StormResolved)
            {
                // DLC 非所持環境ではこれが正常。Assumptions 側の impact 文と同じ扱い。
                b.Line(1, "prefab (ThunderStormAI)",
                    "NOT RESOLVED (expected when the Natural Disasters DLC is not owned)");
                return;
            }

            b.Line(1, "prefab (ThunderStormAI)", prefab.Usable
                ? "resolved"
                : "resolved, but UNUSABLE (m_radius or m_activeDuration is 0; no typhoon "
                  + "can be started, and the mod will not guess them)");
            b.Line(2, "m_radius", prefab.StormRadius.ToString("F2"));
            b.Line(2, "m_emergingDuration", FramesWithHours(prefab.EmergingDuration));
            b.Line(2, "m_activeDuration", FramesWithHours(prefab.ActiveDuration));

            if (!prefab.Usable) return;

            // 強度 100 は「バニラの円盤がちょうど m_radius になる」点なので基準に選んだ
            // （R = m_radius * (0.25 + i * 0.0075)、§A-1 / §A-2）。
            b.Line(2, "derived storm radius",
                TyphoonProfile.StormRadiusOf(100, prefab.StormRadius).ToString("F0")
                + " m at intensity 100  [Disaster + model]");

            b.Line(2, "derived travel speed", TravelSpeedText(prefab.ActiveDuration));
        }

        /// <summary>
        /// 竜巻プレハブの 3 実測値。**台風本体はこれが読めなくても動く**ので、
        /// 読めないことを失敗として書かない（随伴竜巻＝ T10 だけが使えなくなる）。
        /// </summary>
        private static void WriteVortexPrefab(DiagnosticBuilder b, TyphoonPrefabFacts prefab)
        {
            if (!prefab.VortexResolved)
            {
                b.Line(1, "prefab (VortexAI)",
                    "NOT RESOLVED (expected when the Natural Disasters DLC is not owned; "
                    + "only the optional accompanying tornadoes need it)");
                return;
            }

            b.Line(1, "prefab (VortexAI)", "resolved");
            b.Line(2, "m_destructionRadiusMin", prefab.DestructionRadiusMin.ToString("F2"));
            b.Line(2, "m_destructionRadiusMax", prefab.DestructionRadiusMax.ToString("F2"));
            // ★ VortexAI ではなく VehicleInfo 側にあるフィールドである
            //    （TyphoonReader のクラス doc の訂正）。ラベルにもそう書く。
            b.Line(2, "m_maxSpeed (VehicleInfo)", prefab.VortexMaxSpeed.ToString("F2"));
        }

        /// <summary>
        /// 天候。**④で <c>(measured)</c> を名乗ってよい唯一の行**（設計書 §7-1）。
        /// 読めなかったときに 0 を並べない。
        /// </summary>
        private static void WriteWeather(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.WeatherReadable)
            {
                b.Line(1, "weather (measured)",
                    "unread (WeatherManager is not available; the values are NOT 0, they are unknown)");
                return;
            }

            b.Line(1, "weather (measured)",
                "rain=" + snapshot.Rain.ToString("F2")
                + " cloud=" + snapshot.Cloud.ToString("F2")
                + " fog=" + snapshot.Fog.ToString("F2")
                + " windDir=" + snapshot.WindDirectionDegrees.ToString("F1")
                + " enableWeather=" + (snapshot.WeatherEnabled ? "on" : "off"));
        }

        /// <summary>
        /// 進行速度。<c>TyphoonTrack.SpeedFor</c> が 0 を返したら**それが答えである** ——
        /// 推測せず、台風を 1 個も起こせないことをそのまま書く（設計書 §6）。
        /// </summary>
        private static string TravelSpeedText(uint activeDuration)
        {
            float speed = TyphoonTrack.SpeedFor(activeDuration);
            if (speed <= 0f)
            {
                return "unknown (m_activeDuration is unreadable; no typhoon can be started)";
            }

            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f) return speed.ToString("F3") + " m/frame";

            return speed.ToString("F3") + " m/frame (= "
                   + (speed * framesPerMinute).ToString("F0") + " m per in-game minute)";
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
