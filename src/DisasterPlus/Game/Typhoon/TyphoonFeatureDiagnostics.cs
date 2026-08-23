using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④台風の**診断ダンプの行**（<c>Ctrl+F11</c> に出るもの）。
    ///
    /// <see cref="TyphoonFeature"/> の partial である。分けたのは 800 行の上限に
    /// 掛かったからで、境目は「ゲームの状態を進めるもの」と「読んで並べるだけの
    /// もの」の間に引いてある —— このファイルには<b>状態を変える行が 1 つも無い</b>。
    ///
    /// **main スレッド専用。** <c>DiagnosticsHub</c> が main から呼ぶ。
    /// sim 側の static（<c>TyphoonGust</c> / <c>TyphoonCloud</c> …）から
    /// int と bool を読む箇所があるが、これは④が前から取っている形である
    /// （整列した 32bit の読みは裂けない。表示が 1 tick 古くても誰も困らない）。
    /// **ここから sim 側の状態を書き換えないこと。**
    /// </summary>
    public partial class TyphoonFeature
    {
        /// <summary>
        /// ④が今どうなっているかを全部並べる。**倒壊 0 のときも、台風が居ないときも
        /// 全部出す** —— 画面上は「設定で切っている」「近くに建物が無い」
        /// 「上限で外縁まで届いていない」「全部ゲームに断られた」がどれも同じ顔
        /// （何も起きない）になるので、切り分けはここでしかできない。
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
            WriteWeather(b, snapshot);
            WriteTyphoon(b, snapshot);
            WriteNotes(b);
        }

        /// <summary>
        /// **設定画面から降ろした解説の行き場**（<c>Mod.OnSettingsUI</c> の doc の表）。
        ///
        /// 風害・局所被害・氾濫の説明は④のパネルに同じ文が出る（<c>TyphoonEffectRows</c>）。
        /// ここに置くのは、パネルにも設定画面にも無い**危険半円の理屈**だけである。
        ///
        /// ★ ここは sim スレッドである（<c>DiagnosticDump</c> のクラス doc）。
        ///   ゲームのバッファにも UI にも触らない、定数の行だけにすること。
        /// </summary>
        private static void WriteNotes(DiagnosticBuilder b)
        {
            b.Line(1, "note: dangerous side",
                "a real typhoon is not symmetric: on one side the spin and the storm's own "
                + "travel add up. That side gets a slightly wider and slightly more likely "
                + "damage footprint - the right of the track in the northern hemisphere, the "
                + "left in the southern one. Which side is used is a setting");
        }

        /// <summary>
        /// UI の状態。①②③⑤と同じ形（<see cref="DisasterPanelBar"/> に問い合わせるだけ）。
        ///
        /// **「①②のボタンと重なっていないか」はもう診断項目ではない。** 4 個の位置は
        /// 1 本の並びに対する 1 回のループが決めるので、重なる経路が存在しない
        /// （DisasterPanelBar のクラス doc）。ここで見るのは
        /// 「④のボタンが実際に居るか」と「どこに居るか（バニラのパネルの中か、
        /// 退避先の浮遊バーか）」だけである。
        /// </summary>
        private static void WriteUiState(DiagnosticBuilder b)
        {
            // ボタンは④専用ではなく DisasterPanelBar が 4 個まとめて置く。座標は
            // もうこの MOD が決めていないので、出すのは「居るか」と「どこに居るか」だけ。
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdTyphoon)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");
            b.Line(1, "panel body", TyphoonPanel.IsVisible ? "shown" : "hidden");
            // ★ タイルは配置カーソルを構える（バニラの災害ボタンと同じ約束）。
            //   構えたまま指していないのか、指したのに何も起きないのかを見分ける。
            b.Line(1, "placement tool", TyphoonPlacementTool.IsActive ? "active" : "idle");
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
                WriteShutdown(b, snapshot);
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
            // ★ 桁を主張しすぎない（全体レビュー）。上陸フレームの推定は
            //   256 フレーム刻み（LandfallStepFrames ≒ ゲーム内 5.6 分）でしか
            //   打っていないので、F1（0.1 分）は持っていない精度である。
            b.Line(2, "landfall", snapshot.OverLand
                ? "already over land"
                : (snapshot.LandfallKnown
                    ? "in about " + snapshot.MinutesToLandfall.ToString("F0")
                      + " in-game minutes (sampled every "
                      + TyphoonController.LandfallStepMinutesText() + ")"
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
            WriteGusts(b, snapshot);
            WriteCloud(b);
            WriteStormFx(b);
            WriteStormSound(b);
        }

        /// <summary>
        /// **台風が去ったあとに④が何も握っていないことの証明。**
        ///
        /// 持ち主の指摘「台風が去ったら暴風雨や竜巻被害がなくなるように」に対して、
        /// 「本当に止まったか」を画面から確かめる手段がここである。
        /// **正常なら 5 行とも <c>released</c> / <c>0</c> になる。**
        /// 1 つでもそうでなければ、その行が名指しで不具合を指している。
        ///
        /// ★ <c>host thunderstorm</c> の行は**不具合ではない。** 宿主の
        /// <c>ThunderStormAI</c> 災害はセーブから外さないと決めてある（設計書 §4.2）——
        /// 外す手は保存の前に <c>DeactivateNow</c> する以外に無く、それは
        /// 「セーブしただけで台風が消える」ことを意味するからである。
        /// **台風の途中で保存して開き直すと、動かない雷雨がその場に残る。**
        /// それは雨も風も被害も駆動していない抜け殻で、
        /// <c>m_activeDuration</c> が尽きればバニラが自分で畳む。
        /// この行が無いと、次にそれを見た人は④が後始末を忘れたと読む。
        /// </summary>
        private static void WriteShutdown(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            b.Line(2, "weather override", snapshot.WeatherDriving
                ? "STILL APPLIED WITH NO TYPHOON (m_targetRain / m_targetCloud were not "
                  + "released; this is a bug)"
                : "released");

            b.Line(2, "wind damage", snapshot.WindLastCollapsed == 0
                ? "stopped (0 collapsed in the last sweep)"
                : "STILL COLLAPSING BUILDINGS WITH NO TYPHOON ("
                  + snapshot.WindLastCollapsed + " in the last sweep; this is a bug)");

            b.Line(2, "tornado-strength damage", snapshot.GustActive == 0
                    && snapshot.GustLastCollapsed == 0
                ? "stopped (0 patches, 0 collapsed)"
                : "STILL RUNNING WITH NO TYPHOON (" + snapshot.GustActive + " patches, "
                  + snapshot.GustLastCollapsed + " collapsed; this is a bug)");

            b.Line(2, "river flooding", snapshot.FloodTouched == 0
                ? "restored (0 water sources held)"
                : "STILL HOLDING " + snapshot.FloodTouched + " WATER SOURCE(S) WITH NO "
                  + "TYPHOON (this is a bug; the map would stay flooded)");

            b.Line(2, "host thunderstorm",
                "left in the city on purpose. A typhoon rides on a vanilla ThunderStormAI "
                + "disaster, and Disaster + deliberately does not remove it from saves - "
                + "the only way to do that would be to deactivate it before writing, which "
                + "would mean saving the game destroyed your typhoon. After a mid-storm "
                + "reload you may see a stationary thunderstorm: it drives no rain, no wind "
                + "and no damage, and vanilla ends it when m_activeDuration runs out");
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

            // ★★ **どちらの経路で出しているかを必ず名乗る。** 本経路はバニラの
            //    粒子エフェクトを借りた雲の粒で、メッシュはそれが取れなかった
            //    ときの退避である。ここが唯一の見分け方になる。
            b.Line(3, "cloud puffs", TyphoonCloudFx.EffectDetail);

            // ★ 退避経路のシェーダ。Standard へ落ちた／借りてきたことは
            //   ここでしか分からない（③⑤と同じ扱い）。
            b.Line(3, "fallback mesh material", TyphoonCloud.ShaderDetail);

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

        /// <summary>
        /// 暴風雨の演出（横殴りの飛沫と吹き飛ばし）。
        /// **どれも建物・道路・樹木には触れない**ので、被害の行とは分けて出す。
        /// </summary>
        private static void WriteStormFx(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonStormFx.value)
            {
                b.Line(2, "storm effects", "off (setting)");
                return;
            }

            b.Line(2, "storm effects", SquallStateText());
            b.Line(3, "driving rain", TyphoonSquallFx.Detail);
            b.Line(3, "gusts pushing citizens", TyphoonWind.GalePushes
                   + " push(es) so far (every 64 frames; citizens and vehicles only)");
        }

        /// <summary>
        /// 風の音。**何を借りたのか**を必ず名乗る —— 将来ゲームが更新されて
        /// 音が消えたときの唯一の手がかりである。
        /// </summary>
        private static void WriteStormSound(DiagnosticBuilder b)
        {
            if (!ModSettings.TyphoonStormSound.value)
            {
                b.Line(2, "storm sound", "off (setting)");
                return;
            }

            b.Line(2, "storm sound", TyphoonStormAudio.Detail);
            b.Line(3, "volume this frame", TyphoonStormAudio.LastVolume > 0f
                   ? TyphoonStormAudio.LastVolume.ToString("F2")
                     + " (the player's effect volume slider and mute are applied by the game "
                     + "on top of this)"
                   : "silent (no typhoon, or the camera is outside the storm)");
        }

        private static string SquallStateText()
        {
            switch (TyphoonSquallFx.State)
            {
                case TyphoonSquallState.Emitting:
                    return "driving rain (" + TyphoonSquallFx.LastRenderCalls
                           + " RenderEffect/frame, strength "
                           + TyphoonSquallFx.LastStrength.ToString("F2") + ")";

                case TyphoonSquallState.OutsideStorm:
                    return "the camera is outside the storm, so no spray is drawn "
                           + "(this is normal)";

                case TyphoonSquallState.Idle:
                    return "idle (no typhoon)";

                case TyphoonSquallState.NoEffect:
                    return "NOT DRAWN: no vanilla water particle effect could be borrowed. "
                           + "The rain, the wind damage and the vortex are unaffected";

                case TyphoonSquallState.Failed:
                    return "NOT DRAWN: the spray path threw (see output_log.txt)";

                default:
                    return "off";
            }
        }

        private static string CloudStateText()
        {
            switch (TyphoonCloud.State)
            {
                case TyphoonCloudState.Puffs:
                    // ★ 自前の白い雲（既定）と、借り物の粒子（退避）を
                    //   **名前で区別する**。どちらで描いているのかが
                    //   分からないと、「まだ煙に見える」の切り分けができない。
                    if (TyphoonVortexPuffFx.Drawing)
                    {
                        return "own white cloud (" + TyphoonVortexPuffFx.PuffsPlaced
                               + " puffs placed/frame, radius "
                               + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m; "
                               + (CloudParticleAssets.Detail ?? "material not described") + ")";
                    }

                    return "BORROWED vanilla particles (" + TyphoonCloudFx.LastRenderCalls
                           + " RenderEffect/frame, radius "
                           + TyphoonCloud.LastRadiusMetres.ToString("F0") + " m) - the own "
                           + "white cloud could not be built, so this falls back to steam";

                case TyphoonCloudState.Drawing:
                    return "fallback spiral mesh (" + TyphoonCloud.LastDrawCalls
                           + " draw call/frame, radius "
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
        /// 竜巻並みの局所被害（パッチ）。**竜巻の実体は 1 つも作っていない。**
        ///
        /// <c>active 0</c> は不具合ではない —— パッチは
        /// <c>GustPatchPlan.SpawnIntervalFrames</c> ごとに 1 個生まれて
        /// <c>LifetimeFrames</c> で消えるので、居ない瞬間がある。
        /// **「今は無い」と「機能が死んでいる」を見分けられるのがここだけ**なので、
        /// 倒壊 0 のときも走査回数と生存数を必ず出す。
        /// </summary>
        private static void WriteGusts(DiagnosticBuilder b, TyphoonSnapshot snapshot)
        {
            if (!ModSettings.TyphoonGustEnabled.value)
            {
                b.Line(2, "tornado-strength damage", "off (setting)");
                return;
            }

            int gustStrength = ModSettings.TyphoonGustStrength.value;
            if (gustStrength <= 0)
            {
                b.Line(2, "tornado-strength damage", "off (strength slider is 0)");
                return;
            }

            b.Line(2, "tornado-strength damage",
                "pass " + TyphoonGust.Passes
                + " / " + snapshot.GustActive + " patch(es) alive"
                + " / collapsed " + snapshot.GustLastCollapsed
                + " (total " + snapshot.GustTotalCollapsed + ")"
                + " / refused " + snapshot.GustLastRefused
                + " / strength " + gustStrength);

            // ★ 「今は無い」と「機能が死んでいる」を見分けられるのはここだけである。
            b.Line(3, "patches",
                "no tornado disaster and no funnel is created; up to "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.MaxActivePatches
                + " patches of "
                + (int)DisasterPlus.Core.Typhoon.GustPatchPlan.MinRadiusMetres + "-"
                + (int)DisasterPlus.Core.Typhoon.GustPatchPlan.MaxRadiusMetres
                + " m live at once, one born every "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.SpawnIntervalFrames
                + " storm frames and gone after "
                + DisasterPlus.Core.Typhoon.GustPatchPlan.LifetimeFrames
                + ". 0 alive is normal between spawns");

            // 「壊れていない」と「壊せない」を取り違えさせない（風害と同じ）。
            if (snapshot.GustLastRefused > 0)
            {
                b.Line(3, "refused",
                    snapshot.GustLastRefused
                    + " (shelters / vaults / dams / decoration / tsunami buoys refuse "
                    + "demolish:false; that is the game answering correctly, not a failure)");
            }

            if (TyphoonGust.LastCapped)
            {
                b.Line(3, "capped",
                    "the per-pass building budget ran out; some patches were not rolled "
                    + "this pass");
            }

            // ★★ NDR がいても**このパッチは影響を受けない**。かつての随伴竜巻は
            //    DisasterHelpers.DestroyStuff を通っていたので NDR に丸ごと
            //    置き換えられていたが、パッチは BuildingAI.CollapseBuilding を
            //    直接呼ぶ（＝④の風害と同じ経路）。**その事実を名乗る** ——
            //    退役した機能の注意書きが残っていると、次の担当者が
            //    「まだ NDR に食われている」と読む。
            if (ModCompat.NdrPresent)
            {
                b.Line(3, "Natural Disasters Renewal",
                    "present, but it does not affect these patches: they call "
                    + "BuildingAI.CollapseBuilding directly and never go through "
                    + "DisasterHelpers, which is the surface NDR replaces. The accompanying "
                    + "vanilla tornadoes that did go through it have been retired");
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

            // ★★ 危険半円がどちら側か。**左右が逆でもプレイヤーには気付けない**ので、
            //    向きと上乗せの大きさをここで名乗る。数字は TrackBias が持っている
            //    定数そのもので、ここに写した別の値ではない。
            b.Line(3, "dangerous side",
                (ModSettings.TyphoonSouthernHemisphere.value
                    ? "left of the track (southern hemisphere)"
                    : "right of the track (northern hemisphere)")
                + " - radius x" + (1f + TrackBias.MaxRadiusBoost).ToString("F2")
                + ", collapse chance x" + (1f + TrackBias.MaxChanceBoost).ToString("F2")
                + " at its strongest; the other side is unchanged");

            // 「壊れていない」と「壊せない」を取り違えさせない（§F-2）。
            // ★ 送電柱・索道の支柱はここに入らない（全体レビュー）。あれらは
            //   dry-run で false を返した直後に本物の倒壊を行うので、collapsed に
            //   だけ積まれる。以前はこの行が refused を丸ごと防災施設に帰していた。
            b.Line(3, "refused", snapshot.WindLastRefused == 0
                ? "0"
                : snapshot.WindLastRefused
                  + " (shelters / vaults / dams / decoration / tsunami buoys refuse "
                  + "demolish:false; that is the game answering correctly, not a failure. "
                  + "Power poles and cable-car pylons are NOT counted here - they refuse the "
                  + "dry run and then collapse anyway, so they land in 'collapsed')");

            // 高さは係数であって足切りではない（②の長周期と判断が違う）。
            b.Line(3, "unknown height", snapshot.WindLastUnknownHeight == 0
                ? "0"
                : snapshot.WindLastUnknownHeight
                  + " (these buildings stayed eligible at the base chance; the height bonus "
                  + "was declined, not guessed)");

            if (snapshot.WindLastCapped)
            {
                // ★ 「次の走査で続きから」とは書かない（全体レビュー I1）。
                //   眼は 1 走査（256 フレーム）のあいだに 64〜1536 m 動き、
                //   グリッドのセルは 64 m なので、中心のセルはほぼ毎回変わって
                //   走査位置は 0 に戻る。**次の走査もまた眼から始まる。**
                //   打ち切りは安全側（判定しない ＝ 倒さない）に外れる。
                b.Line(3, "capped",
                    "the sweep was truncated this pass, so the outer edge was not rolled. It "
                    + "does NOT resume where it stopped: the eye moves 64-1536 m per pass "
                    + "against a 64 m grid, so the next pass restarts at the eye");
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

            // ★ 「宿主に全部譲っていて④は 1 発も撃っていない」を名指しする
            //   （全体レビュー I4）。強度 170 以上ではこれが恒常状態になり、
            //   T6 の壁雲への偏りが消えて宿主の一様な円盤だけになる。
            //   在庫（一時的に 0）ではなく**宿主の取り分だけ**を見る。
            if (DisasterPlus.Core.Typhoon.LightningBudget.YieldsCompletely(
                    snapshot.LightningVanillaReserve))
            {
                b.Line(3, "share",
                    "0 — the host storm's reserve alone uses the whole queue at this intensity "
                    + "(>= " + DisasterPlus.Core.Typhoon.LightningBudget.IntensityWithNoShareAtPeak
                    + " at the ramp peak). Disaster + queues nothing, so the eye-wall placement "
                    + "is gone and only the host storm's uniform disc remains. This is the "
                    + "designed yield, not a failure");
            }

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
