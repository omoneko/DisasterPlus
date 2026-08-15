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
            TyphoonWeather.Reset();
        }

        /// <summary>
        /// sim スレッド。<c>DisasterManager</c> / <c>WeatherManager</c> /
        /// <c>SimulationManager</c> の読み取りは必ずここで行う。
        ///
        /// ポーズ中（deltaMinutes == 0）にも呼ばれる（<see cref="IPausedTickFeature"/>）。
        /// </summary>
        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            if (!ModSettings.TyphoonEnabled.value)
            {
                // ★★ 機能を切っても、**触ったものは全部返す。** プレイヤーが台風の
                //    最中にこの設定を切ると、以降この tick は 1 行も走らなくなるので、
                //    ここで返さなかったものは（都市を出るか保存するまで）返す機会が
                //    無くなる。3 つとも台帳が空なら 1 命令で返るので、毎 tick 通っても
                //    構わない（ログも確保も走らない）。
                //
                //    ここはポーズガードより上だが、**復元はゲームの状態を進めない**
                //    ので IPausedTickFeature の契約は破らない（むしろポーズ中に
                //    切られたときに戻せるほうが正しい）。
                TyphoonFlood.RestoreAll();

                // ★★ 竜巻と天候も返す（全体レビュー C2）。以前ここは水位しか
                //    戻しておらず、掴んだままの竜巻の台帳が**1 tick も検証されない
                //    まま**残った。その間に竜巻は自然終了してスロットが配り直され、
                //    設定を戻した瞬間か都市を出た瞬間に、④の後始末が
                //    **他人の生きている災害**を止めるか解放しに行った。
                //    天候も同じで、切った瞬間に m_targetRain を握ったまま
                //    台風だけが止まる（雨がやまなくなる）。
                //    StopAll / Release はどちらも冪等である。
                TyphoonTornado.StopAll();
                TyphoonWeather.Release();
                return;
            }

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
            if (TyphoonController.Active)
            {
                TyphoonWeather.Drive(snapshot, deltaMinutes);
                TyphoonLightning.Tick(snapshot, frameIndex);

                // ★ 風害は設定で切れる（既定 ON。強さ 0 でも完全に無効）。
                //   切ったときに Apply を呼ばないのは②の第 2 層と同じ形で、
                //   走査そのものを起こさないためである。
                if (ModSettings.TyphoonWindDamage.value)
                {
                    TyphoonWind.Apply(snapshot, deltaMinutes);
                }
            }

            // ★ 河川氾濫は台風が居なくても呼ぶ。**持ち上げた水位を戻すのが
            //    この経路の仕事でもある**（TyphoonController.Forget が既に
            //    RestoreAll を呼んでいるが、取りこぼしをここで拾う）。
            //    設定を OFF にした瞬間に呼ばれなくなると川が溢れたままになるので、
            //    OFF のときも「台帳が空でなければ戻す」ところまでは通す。
            if (ModSettings.TyphoonFloodEnabled.value)
            {
                TyphoonFlood.Tick(snapshot, frameIndex, deltaMinutes);
            }
            else
            {
                TyphoonFlood.RestoreAll();
            }

            // ★ 随伴竜巻は**既定 OFF**（設計書 §2）。切っている間・台風が居ない間は
            //    StopAll を通す —— 台風の最中にこの設定を切ったプレイヤーが、
            //    掴まれたままの竜巻を止める手段を失わないようにする
            //    （TyphoonFlood.RestoreAll と同じ形。StopAll は台帳が空なら
            //    1 命令で返るので毎 tick 通ってよい）。
            if (TyphoonController.Active && ModSettings.TyphoonTornadoes.value)
            {
                TyphoonTornado.Tick(snapshot, frameIndex, deltaMinutes);
            }
            else
            {
                TyphoonTornado.StopAll();
            }
        }

        /// <summary>
        /// main スレッド。**ここから sim 側の型（<see cref="TyphoonController"/> /
        /// <see cref="TyphoonWeather"/>）を呼ばないこと。** 読むのは
        /// <see cref="TyphoonHub.Latest"/> のスナップショットだけである。
        /// </summary>
        public void OnMainThreadUpdate()
        {
            TyphoonPanelButton.Tick();
            TyphoonPanel.Tick();

            // ★ 雲は main スレッドだけの機能で、**sim 側からは 1 度も呼ばれない。**
            //   それが T9 を④の他の要素から独立させている実体である
            //   （TyphoonCloud のクラス doc）。台風が終わったときの後始末も
            //   TyphoonCloud.Update が自分で行う——TyphoonController.Forget の
            //   後始末列にこの型を足さないこと。
            //   ★ TyphoonEnabled も見ること。機能そのものを切ると OnSimulationTick が
            //     早期 return して TyphoonHub.Latest が更新されなくなるので、最後に
            //     publish された「Active な」スナップショットが残り続ける ——
            //     見ないと**止まった雲が画面に貼り付いたまま**になる
            //     （TyphoonPanelButton / TyphoonPanel が同じガードを持っている）。
            if (ModSettings.TyphoonEnabled.value && ModSettings.TyphoonCloudEnabled.value)
            {
                TyphoonCloud.Update(TyphoonHub.Latest);
            }
            else
            {
                TyphoonCloud.Destroy();
            }
        }

        public void OnLevelUnloading()
        {
            // ★ UI から先に畳む。2 つ目の都市が**ボタン 1 個・パネル 1 枚**で
            //    始まること（残すと都市を読み込むたびに 1 枚ずつ積み上がる）。
            TyphoonPanelButton.Remove();
            TyphoonPanel.Destroy();
            // ★ Mesh も Material も Component ではないので、GameObject を消しても
            //    道連れにならない。**自分で Object.Destroy する**（TyphoonCloud の
            //    クラス doc）。バニラ空の雲の設定もここで元へ戻る。
            TyphoonCloud.Destroy();

            TyphoonHub.Clear();
            TyphoonReader.Reset();
            // 予約も進行中の台風も都市をまたいで残らない。
            TyphoonController.Reset();
            // ★ 天候の上書きは必ずここでも戻す。都市を出た瞬間に台風が消えても、
            //    m_targetRain を握ったままにしない。
            TyphoonWeather.Reset();
            // ★ 落雷の在庫も持ち越さない。持ち越すと次の都市の台風が、実際には
            //    空いているキューを「埋まっている」と見て撃たなくなる。
            TyphoonLightning.Reset();
            // ★ 風害の走査位置とカウンタも都市をまたがない。持ち越すと次の都市で
            //    前の都市の序数から走り出す（＝中心の周りが 1 度も判定されない）。
            TyphoonWind.Reset();
            // ★★ 河川の水位を必ず戻す（罠 4 の復元経路 2 本目）。
            //    ここを忘れると、次に開いた都市で**前の都市のハンドル**を復元しに行き、
            //    無関係な川の水位を書き換える。TyphoonFlood.Reset は内部で
            //    RestoreAll を呼んでから台帳を捨てる。
            TyphoonFlood.Reset();
            // ★ 随伴竜巻も都市をまたがない。持ち越すと、次の都市で**前の都市の
            //    災害 ID** を操舵しに行き、無関係な災害を引きずり回す。
            //    TyphoonTornado.Reset は内部で StopAll を呼んでから台帳を捨てる。
            TyphoonTornado.Reset();
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

            WriteUiState(b);

            if (snapshot == null || !snapshot.Valid) return;

            WriteStormPrefab(b, snapshot.Prefab);
            WriteVortexPrefab(b, snapshot.Prefab);
            WriteWeather(b, snapshot);
            WriteTyphoon(b, snapshot);
        }

        /// <summary>
        /// UI の状態。②の <c>EarthquakeFeature.WriteUiState</c> と同じ 3 状態
        /// （未設置／保存位置の再利用／新規探索の成否）。
        ///
        /// **ボタンが①②のボタンと重なっているかどうかは、ここでしか分からない。**
        /// 重なったボタンは画面上で「1 個しか無い」ように見えるので、
        /// <c>fresh free-slot search FAILED</c> が出ているかを診断で確かめる。
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            string placement;
            if (!TyphoonPanelButton.Installed)
            {
                placement = "button not installed yet";
            }
            else if (TyphoonPanelButton.UsedSavedPosition)
            {
                placement = "saved position reused";
            }
            else
            {
                placement = TyphoonPanelButton.FoundFreeSlot
                    ? "fresh free-slot search succeeded"
                    : "fresh free-slot search FAILED (fell back to preferred position)";
            }

            b.Line(1, "button position", ModSettings.TyphoonButtonX.value + ","
                                         + ModSettings.TyphoonButtonY.value
                                         + "  (" + placement + ")");
            b.Line(1, "panel body", TyphoonPanel.IsVisible ? "shown" : "hidden");
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
                // 台風が居ないのに天候を握っていたら、それは④が戻し損ねている。
                // 正常時は 1 行も出ない（＝この行が出たら不具合）。
                if (snapshot.WeatherDriving)
                {
                    b.Line(2, "weather driving",
                        "ON WITH NO TYPHOON (the weather override was not released)");
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

            WriteWeatherDriving(b, snapshot);
            WriteLightning(b, snapshot);
            WriteWind(b, snapshot);
            WriteFlood(b, snapshot);
            WriteTornadoes(b, snapshot);
            WriteCloud(b);
        }

        /// <summary>
        /// 巨大な回転雲（T9）。**バニラに流用できる雲は 1 つも無い**ので、
        /// ここに出るのは全部④が自分で組んだものである（§C-1 / §C-2）。
        ///
        /// <c>vanilla sky boost</c> が <c>not available</c> なのは**不具合ではない** ——
        /// <c>DayNightDynamicCloudsProperties</c> は DLC・グラフィック設定によっては
        /// 存在しない（§C-2、PARTIAL）。④の自前の雲はそれに依存しない。
        /// </summary>
        private static void WriteCloud(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonCloudEnabled.value)
            {
                b.Line(2, "cloud", "off (setting)");
                return;
            }

            b.Line(2, "cloud", CloudStateText());

            if (!ModSettings.TyphoonVanillaCloudBoost.value)
            {
                b.Line(3, "vanilla sky boost", "off (setting)");
                return;
            }

            b.Line(3, "vanilla sky boost", TyphoonCloud.VanillaBoostApplied
                ? "applied"
                : "not available in this environment (this is normal on some DLC/graphics "
                  + "settings; Disaster + draws its own cloud regardless)");
        }

        private static string CloudStateText()
        {
            switch (TyphoonCloud.State)
            {
                case TyphoonCloudState.Drawing:
                    return "drawing (" + TyphoonCloud.LastDrawCalls + " draw call/frame, radius "
                           + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m)";

                case TyphoonCloudState.ShaderMissing:
                    return "NOT DRAWN: no usable shader resolved. Disaster + refuses to borrow "
                           + "a Cities material - that renders invisible or black in a "
                           + "hand-rolled DrawMesh";

                case TyphoonCloudState.BuildFailed:
                    return "NOT DRAWN: the mesh or material could not be built";

                default:
                    return "idle (no typhoon to draw)";
            }
        }

        /// <summary>
        /// 随伴竜巻（T10）。**既定 OFF なので「出ていない」が正常である。**
        ///
        /// <c>attached</c> が <c>count</c> より小さい状態を隠さない ——
        /// 渦車両が付かなかった竜巻は④の軌道に乗らず、バニラの竜巻として自由に流れる。
        /// 画面上は「台風の周りを回っていない竜巻」に見えるだけで、原因を指すものが
        /// 他に無い。
        /// </summary>
        private static void WriteTornadoes(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!ModSettings.TyphoonTornadoes.value)
            {
                b.Line(2, "accompanying tornadoes", "off (setting; this is the default)");
                return;
            }

            b.Line(2, "accompanying tornadoes",
                snapshot.TornadoCount + " running / " + snapshot.TornadoAttached
                + " steered / " + ModSettings.TyphoonTornadoCount.value + " requested");

            if (snapshot.TornadoAttached < snapshot.TornadoCount)
            {
                b.Line(3, "not steered",
                    (snapshot.TornadoCount - snapshot.TornadoAttached)
                    + " tornado(es) have no vortex vehicle yet. Until one attaches they "
                    + "drift on vanilla's own path instead of orbiting the typhoon");
            }

            if (!string.IsNullOrEmpty(TyphoonTornado.LastFailure))
            {
                b.Line(3, "last failure", TyphoonTornado.LastFailure);
            }

            // ★ NDR がいる環境で「風害と竜巻で壊れ方が違う」理由は、ここと設定画面と
            //    パネルにしか出ない（IL 事実文書 §F-1）。
            if (ModCompat.NdrPresent)
            {
                b.Line(3, "Natural Disasters Renewal",
                    "present: vanilla tornado destruction is replaced wholesale, so these "
                    + "tornadoes follow NDR's tornado settings. The typhoon's own wind damage "
                    + "does not - it never goes through DisasterHelpers");
            }
        }

        /// <summary>
        /// 河川氾濫（T8）。
        ///
        /// **<c>natural sources</c> の個数は必ず出す。** マップ依存で未知（§D-4 /
        /// 設計書 §6）なので、実機で初めて分かる数である。**0 は不具合ではない。**
        /// </summary>
        private static void WriteFlood(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!ModSettings.TyphoonFloodEnabled.value)
            {
                b.Line(2, "river flooding", "off (setting)");
                return;
            }

            int strength = ModSettings.TyphoonFloodStrength.value;
            if (strength <= 0)
            {
                b.Line(2, "river flooding", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "river flooding",
                snapshot.FloodState
                + "  raised " + snapshot.FloodTouched
                + " / peak +" + snapshot.FloodPeakRiseMetres.ToString("F2") + " m"
                + " / strength " + strength);

            // ★ マップ依存で未知の数。実機の報告に必ず要る。
            b.Line(3, "natural sources", snapshot.FloodNaturalSources
                + (snapshot.FloodNaturalSources == 0
                    ? " (this map has none; no river can rise and NOTHING IS WRONG - the game "
                      + "has no flood disaster of its own and Disaster + only raises water "
                      + "sources the map already has)"
                    : " (TYPE_NATURAL water sources on the whole map)"));

            if (snapshot.FloodState == TyphoonFloodState.Failed)
            {
                b.Line(3, "failure", TyphoonFlood.LastFailure ?? "unknown");
            }
        }

        /// <summary>
        /// 風害（T7）。**倒壊 0 のときも全部出す。** 画面上は「設定で切っている」
        /// 「近くに建物が無い」「上限で外縁まで届いていない」「全部ゲームに断られた」が
        /// どれも同じ顔（何も倒れない）になるので、切り分けはここでしかできない。
        /// </summary>
        private static void WriteWind(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!ModSettings.TyphoonWindDamage.value)
            {
                b.Line(2, "wind damage", "off (setting)");
                return;
            }

            int strength = ModSettings.TyphoonWindStrength.value;
            if (strength <= 0)
            {
                b.Line(2, "wind damage", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "wind damage",
                "pass " + snapshot.WindPasses
                + " / collapsed " + snapshot.WindLastCollapsed
                + " (total " + snapshot.WindTotalCollapsed + ")"
                + " / examined " + snapshot.WindLastScanned
                + " / refused " + snapshot.WindLastRefused
                + " / strength " + strength);

            // 「壊れていない」と「壊せない」を取り違えさせない（§F-2）。
            b.Line(3, "refused", snapshot.WindLastRefused == 0
                ? "0"
                : snapshot.WindLastRefused
                  + " (shelters / vaults / dams / decoration / tsunami buoys refuse "
                  + "demolish:false; that is the game answering correctly, not a failure)");

            // 高さは係数であって足切りではない（②の長周期と判断が違う）。
            b.Line(3, "unknown height", snapshot.WindLastUnknownHeight == 0
                ? "0"
                : snapshot.WindLastUnknownHeight
                  + " (these buildings stayed eligible at the base chance; the height bonus "
                  + "was declined, not guessed)");

            if (snapshot.WindLastCapped)
            {
                b.Line(3, "capped",
                    "the sweep was truncated this pass; the outer edge has not been rolled yet "
                    + "and resumes next pass");
            }
        }

        /// <summary>
        /// ④が書いている天候の**目標値**。上の <c>weather (measured)</c> は
        /// <c>m_current*</c>（バニラの実測値）で、こちらは <c>m_target*</c>（本 MOD の量）。
        /// **2 つを取り違えないこと。**
        ///
        /// 天候を切っている環境では note を出す。**黙って動かない状態を作らない。**
        /// </summary>
        private static void WriteWeatherDriving(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!snapshot.WeatherDriving)
            {
                b.Line(2, "weather driving", "off");
                return;
            }

            b.Line(2, "weather driving",
                "on   target rain=" + snapshot.DrivenRain.ToString("F2")
                + " cloud=" + snapshot.DrivenCloud.ToString("F2")
                + " fog=0.00"
                + " dir=" + snapshot.DrivenDirectionDegrees.ToString("F1") + " deg");

            if (!snapshot.WeatherEnabled)
            {
                b.Line(3, "note",
                    "the player has weather disabled; m_forceWeatherOn=2 is written every "
                    + "tick to keep the storm visible");
            }

            // 雨量 0.8 超はゲーム自身の環境落雷を呼ぶ。**意図した代償**なので隠さない
            // （TyphoonWeather のクラス doc 6.）。
            if (snapshot.DrivenRain > 0.8f)
            {
                b.Line(3, "note",
                    "target rain is above 0.8: once m_currentRain passes it the game queues "
                    + "its own lightning. While this typhoon is Active the game reuses this "
                    + "very disaster instead of creating another one (measured); a separate "
                    + "vanilla thunderstorm can only appear before it activates or after it ends");
            }
        }

        /// <summary>
        /// 落雷（T6）。**撃った数が 0 のときも必ず全部出す**（③の「延焼が動いているか
        /// 診断から一切見えなかった」失敗を繰り返さない）。画面上は
        /// 「上限に当たって捨てられている」「宿主に全部譲っている」「そもそも撒いていない」が
        /// どれも同じ顔（雷が少ない）になるので、切り分けはここでしかできない。
        /// </summary>
        private static void WriteLightning(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            b.Line(2, "lightning",
                "in flight " + snapshot.LightningInFlight
                + " / total " + snapshot.LightningTotal
                + " / vanilla reserve " + snapshot.LightningVanillaReserve
                + " / cap " + DisasterPlus.Core.Typhoon.LightningBudget.QueueCapacity);

            // ★ 0 以外は不具合の合図。上限に当たると、宿主の嵐や他 MOD の落雷まで
            //   同じように捨てられる（IL 事実文書 §A-3）。
            b.Line(3, "dropped by the game", snapshot.LightningRejected == 0
                ? "0 (the queue cap was never hit)"
                : snapshot.LightningRejected
                  + " — THE 20-STRIKE CAP WAS HIT; the host storm's own strikes are being "
                  + "thrown away too");

            // 環境落雷（雨 > 0.8 かつキューが空）を抑えているかどうか。
            b.Line(3, "environmental lightning", snapshot.LightningInFlight > 0
                ? "suppressed (the queue is not empty)"
                : "possible (the queue may be empty this tick; the game reuses this very "
                  + "disaster rather than creating another one)");
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
