using ICities;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description { get { return Strings.ModDescription; } }

        /// <summary>
        /// メインメニューの起動中に 1 回だけ呼ばれる（言語切替でも再実行される）。
        /// レベルロード後にしか分からない情報からオプションを組み立ててはいけない。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            // 言語切替でもこのメソッドは再実行される。ここで読み直せばオプション画面が追従する。
            // ただしゲーム内ボタンのツールチップはレベルロード時に一度設定されるだけなので、
            // 次のロードまで前の言語のままになる。
            LocaleLoader.Apply();
            ModSettings.Ensure();

            // Assumptions はレベルロード後に走るので、初回起動時はまだ空。
            // つまり警告は「一度都市を読み込んだ後、次にオプションを開いたとき」に出る。
            // OnSettingsUI はメインメニュー起動時に 1 回しか走らないため、これは避けられない。
            //
            // これが成立するのは Assumptions.Reset()（レベルアンロード時）が結果を
            // 消さないから。ここは必ずアンロードより後に走るので、Reset() でクリアすると
            // LastResults は常に空になり、この警告は原理的に出せなくなる。
            // ★ **DLC 非所持環境で正常に FAIL する 5 件は出さない**（全体レビュー）。
            //   出すと、バニラのままの環境ではこの群が永久に表示され続け、
            //   本当の前提破れが起きたときにその 1 件が見慣れた群に紛れて読まれない。
            //   何が外れているかは Assumptions.UnexpectedFailures の doc にある。
            var failures = Assumptions.UnexpectedFailures();

            if (failures.Count > 0)
            {
                var warn = helper.AddGroup(Strings.AssumptionsFailedTitle);
                for (int i = 0; i < failures.Count; i++)
                {
                    warn.AddGroup("- " + failures[i].Impact);
                }
                warn.AddGroup(Strings.AssumptionsFailedHint);
            }

            if (ModCompat.NaturalDisastersOwned)
            {
                var fw = helper.AddGroup(Strings.GroupFireWhirl);
                fw.AddCheckbox(Strings.FireWhirlEnabled, ModSettings.FireWhirlEnabled.value,
                    v => ModSettings.FireWhirlEnabled.value = v);

                fw.AddSlider(Strings.DetectRadius, 50f, 400f, 10f, ModSettings.DetectRadius.value,
                    v => ModSettings.DetectRadius.value = (int)v);

                fw.AddSlider(Strings.DetectCount, 4f, 40f, 1f, ModSettings.DetectCount.value,
                    v => ModSettings.DetectCount.value = (int)v);

                fw.AddSlider(Strings.MaxLifetime, 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
                    v => ModSettings.MaxLifetimeMinutes.value = (int)v);

                fw.AddSlider(Strings.SpreadStrength, 0f, IgnitionSpread.MaxStrength, 1f,
                    ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);

                fw.AddSlider(Strings.MinSeparation, 100f, 800f, 25f, ModSettings.MinSeparation.value,
                    v => ModSettings.MinSeparation.value = (int)v);
            }
            else
            {
                helper.AddGroup(Strings.GroupFireWhirl).AddSpace(4);
                // グループ名の下に理由を出す。設定が「消えた」ように見えないようにする。
                helper.AddGroup(Strings.FireWhirlNeedsDlc);
            }

            var forecast = helper.AddGroup(Strings.GroupForecast);
            forecast.AddCheckbox(Strings.ForecastEnabled, ModSettings.ForecastEnabled.value,
                v => ModSettings.ForecastEnabled.value = v);
            // 探索のやり直しは次回のレベルロードで自然に起きる（ForecastPanelButton.Install
            // は保存済み座標が -1 のときだけ FreeSlotFinder を再度呼ぶ）。ここでは保存値を
            // 戻すだけで十分（設計書 5.1 の「ボタン位置をリセット」）。
            forecast.AddButton(Strings.ForecastResetButton, delegate
            {
                ModSettings.ForecastButtonX.value = -1;
                ModSettings.ForecastButtonY.value = -1;
            });

            // 予報パネルの気象・傾向の行は DLC 無しでも正しく動くので、機能そのものは
            // 隠さない。ただしハザードの 2 行（落雷・竜巻の「マップに表示」とカーソル
            // 位置の数値）は DLC が無いと prefab も気象レーダーも存在せず、永久に
            // 空のビューと 0 になる。パネル側では行ごと出さないようにしてあるが
            // （ForecastPanel._hazardRowsBuilt）、設定画面にも理由を書いておかないと
            // 「機能の一部が黙って無い」ように見える。FireWhirlNeedsDlc と同じ扱い。
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.ForecastHazardNeedsDlc);
            }

            var earthquake = helper.AddGroup(Strings.GroupEarthquake);
            earthquake.AddCheckbox(Strings.EarthquakeEnabled, ModSettings.EarthquakeEnabled.value,
                v => ModSettings.EarthquakeEnabled.value = v);
            earthquake.AddCheckbox(Strings.EarthquakeShakeBoost, ModSettings.EarthquakeShakeBoost.value,
                v => ModSettings.EarthquakeShakeBoost.value = v);

            // ── 第 2 層（Disaster + が足した挙動。バニラにはありません）──────────
            //
            // **既定 OFF。** 海中の地震から津波を起こすのはバニラの挙動ではないので、
            // 既定で入れるとプレイヤーは「地震のあと勝手に津波が来る」原因が MOD だと
            // 気付く手段を持たない（ModSettings.EarthquakeTsunamiChain の doc）。
            //
            // DLC が無い環境では出さない。TsunamiAI のプレハブが存在しないので
            // （§B-5）、この設定は何も制御しない死んだチェックボックスになる。
            if (ModCompat.NaturalDisastersOwned)
            {
                earthquake.AddCheckbox(Strings.EarthquakeTsunamiChain,
                    ModSettings.EarthquakeTsunamiChain.value,
                    v => ModSettings.EarthquakeTsunamiChain.value = v);
                earthquake.AddSlider(Strings.EarthquakeTsunamiDelay, 5f, 120f, 5f,
                    ModSettings.EarthquakeTsunamiDelayMinutes.value,
                    v => ModSettings.EarthquakeTsunamiDelayMinutes.value = (int)v);

                // ★ 長周期地震動。**既定 OFF。** 津波と違い、これは
                //    「バニラなら倒れなかった建物を倒す」ので、チェックボックスの
                //    ラベル自体にその事実を書く（Strings.EarthquakeLongPeriodEnabled）。
                earthquake.AddCheckbox(Strings.EarthquakeLongPeriodEnabled,
                    ModSettings.EarthquakeLongPeriod.value,
                    v => ModSettings.EarthquakeLongPeriod.value = v);
                earthquake.AddSlider(Strings.EarthquakeLongPeriodStrength, 0f, 10f, 1f,
                    ModSettings.EarthquakeLongPeriodStrength.value,
                    v => ModSettings.EarthquakeLongPeriodStrength.value = (int)v);
            }

            earthquake.AddButton(Strings.EarthquakeResetButton, delegate
            {
                ModSettings.EarthquakeButtonX.value = -1;
                ModSettings.EarthquakeButtonY.value = -1;
            });

            // 揺れの補正が「既定の強度では何も変えない」ことを名乗る。バニラを抑制せず
            // 足すだけで、強度 55（バニラ既定）では追加分が厳密に 0 になる（§A-7）。
            // 注記の出し方は IntensityUnlockHandledByOther と同じ（root への AddGroup）。
            helper.AddGroup(Strings.EarthquakeShakeBoostNote);

            // 長周期地震動が「バニラのどこにも無い量」であることを、設定画面でも名乗る。
            // パネルの第 2 層の注記と同じ文（EarthquakeLongPeriodNote）。
            if (ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.EarthquakeLongPeriodNote);
            }

            // ②は機能そのものが DLC 依存（EarthquakeAI のプレハブが存在しない）。
            // ForecastHazardNeedsDlc / FireWhirlNeedsDlc と同じ形で理由を書く。
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.EarthquakeNeedsDlc);
            }

            var typhoon = helper.AddGroup(Strings.GroupTyphoon);
            typhoon.AddCheckbox(Strings.TyphoonEnabled, ModSettings.TyphoonEnabled.value,
                v => ModSettings.TyphoonEnabled.value = v);
            typhoon.AddSlider(Strings.TyphoonIntensity, 10f, 255f, 5f,
                ModSettings.TyphoonIntensity.value,
                v => ModSettings.TyphoonIntensity.value = (int)v);
            // 探索のやり直しは次回のレベルロードで自然に起きる（TyphoonPanelButton.Install
            // は保存済み座標が -1 のときだけ FreeSlotFinder を再度呼ぶ）。ここでは保存値を
            // 戻すだけで十分（①②のボタン位置リセットと同じ形）。
            // ★ 風害は既定 ON（②の第 2 層と判断が違う理由は TyphoonWind のクラス doc）。
            //    強さ 0 で完全に無効になる。
            typhoon.AddCheckbox(Strings.TyphoonWindEnabled, ModSettings.TyphoonWindDamage.value,
                v => ModSettings.TyphoonWindDamage.value = v);
            typhoon.AddSlider(Strings.TyphoonWindStrength, 0f, 10f, 1f,
                ModSettings.TyphoonWindStrength.value,
                v => ModSettings.TyphoonWindStrength.value = (int)v);
            // ★ 河川氾濫も既定 ON。**セーブに焼き付く状態を触る唯一の機能**なので、
            //    復元が 3 箇所から掛かることを TyphoonFloodNote が名乗る。
            typhoon.AddCheckbox(Strings.TyphoonFloodEnabled,
                ModSettings.TyphoonFloodEnabled.value,
                v => ModSettings.TyphoonFloodEnabled.value = v);
            typhoon.AddSlider(Strings.TyphoonFloodStrength, 0f, 10f, 1f,
                ModSettings.TyphoonFloodStrength.value,
                v => ModSettings.TyphoonFloodStrength.value = (int)v);
            // ★ 随伴竜巻は**既定 OFF**（設計書 §2）。バニラの竜巻をそのまま借りるので
            //    見た目も破壊も無料でバニラ品質だが、その破壊は DisasterHelpers を
            //    通るため NDR がいる環境ではあちらの設定に従う（下の注記）。
            typhoon.AddCheckbox(Strings.TyphoonTornadoEnabled,
                ModSettings.TyphoonTornadoes.value,
                v => ModSettings.TyphoonTornadoes.value = v);
            typhoon.AddSlider(Strings.TyphoonTornadoCount, 0f,
                DisasterPlus.Game.TyphoonTornado.MaxTornadoes, 1f,
                ModSettings.TyphoonTornadoCount.value,
                v => ModSettings.TyphoonTornadoCount.value = (int)v);
            // ★ 雲は既定 ON。**見た目だけの機能**で、切っても他の 5 要素はそのまま動く
            //    （TyphoonCloud のクラス doc の独立性）。
            typhoon.AddCheckbox(Strings.TyphoonCloudEnabled,
                ModSettings.TyphoonCloudEnabled.value,
                v => ModSettings.TyphoonCloudEnabled.value = v);
            typhoon.AddCheckbox(Strings.TyphoonVanillaCloudBoost,
                ModSettings.TyphoonVanillaCloudBoost.value,
                v => ModSettings.TyphoonVanillaCloudBoost.value = v);
            typhoon.AddButton(Strings.TyphoonResetButton, delegate
            {
                ModSettings.TyphoonButtonX.value = -1;
                ModSettings.TyphoonButtonY.value = -1;
            });

            // 強度がバニラの領域を超えることを名乗る（EarthquakeShakeBoostNote と同じ形）。
            helper.AddGroup(Strings.TyphoonIntensityNote);
            // ★ 「バニラに風害は存在しない」「数値は風速ではない」を設定画面でも名乗る。
            helper.AddGroup(Strings.TyphoonWindNote);
            // ★ 「水位は必ず戻す」を設定画面でも名乗る。氾濫の唯一の怖さは
            //    「MOD を外したら川が溢れたままだった」である。
            helper.AddGroup(Strings.TyphoonFloodNote);

            // ★★ NDR がいる環境でだけ出す。**この 1 行が、随伴竜巻の代償を
            //    プレイヤーに見せる主経路である**（設計書 §2 / IL 事実文書 §F-1）。
            //    バニラ竜巻の破壊は DisasterHelpers.DestroyStuff を通るので NDR に
            //    置き換えられるが、④自身の風害は通していないので影響を受けない。
            //    その区別まで書く（IntensityUnlockHandledByOther と同じ形）。
            if (ModCompat.NdrPresent)
            {
                helper.AddGroup(Strings.TyphoonTornadoNdrNote);
            }

            // ④は機能そのものが DLC 依存（ThunderStormAI のプレハブが存在しない）。
            // FireWhirlNeedsDlc / EarthquakeNeedsDlc と同じ形で理由を書く。
            if (!ModCompat.NaturalDisastersOwned)
            {
                helper.AddGroup(Strings.TyphoonNeedsDlc);
            }

            // ★★ ⑤火山には「Natural Disasters が必要です」の群を**置かない**。
            //    ⑤は DLC を要らない（設計書 §1.4）—— 災害スロットに載らず、
            //    RawHeights を自分で書き、MakeCrater / BurnGround にも DLC ゲートは
            //    無い（IL 事実文書 §C-8 / §B-7b）。分岐するのは樹木の着火だけ
            //    （TreeManager.BurnTree、§B-7c）で、それは T8 が
            //    FeatureHost.NoteDegraded で名乗る。ここに DLC の注記を置くと嘘になる。
            var volcano = helper.AddGroup(Strings.GroupVolcano);
            volcano.AddCheckbox(Strings.VolcanoEnabled, ModSettings.VolcanoEnabled.value,
                v => ModSettings.VolcanoEnabled.value = v);

            // ラベル配列は static readonly にしてはいけない。型初期化時の言語で凍結する。
            // 毎回組み直すことで言語切替に追従する（このファイルの他の 2 箇所と同じ）。
            string[] volcanoShapes =
            {
                Strings.VolcanoFormShield, Strings.VolcanoFormStrato, Strings.VolcanoFormDome
            };
            int currentShape = ModSettings.VolcanoShapeSetting.value;
            if (currentShape < 0 || currentShape >= volcanoShapes.Length)
            {
                currentShape = ModSettings.VolcanoShapeStrato;
            }
            volcano.AddDropdown(Strings.VolcanoShapeSetting, volcanoShapes, currentShape,
                v => ModSettings.VolcanoShapeSetting.value = v);

            // ★ スライダーの範囲は 3 形態を合わせた外枠にしてある。**形態ごとの帯へ
            //    絞るのは使う側（VolcanoShape.RadiusFor / HeightFor）の仕事**で、
            //    .cgs は公開契約なので範囲外の値が入っていても読み捨てない。
            volcano.AddSlider(Strings.VolcanoRadiusSetting, 250f, 3000f, 50f,
                ModSettings.VolcanoRadius.value,
                v => ModSettings.VolcanoRadius.value = (int)v);
            volcano.AddSlider(Strings.VolcanoHeightSetting, 50f, 700f, 10f,
                ModSettings.VolcanoHeight.value,
                v => ModSettings.VolcanoHeight.value = (int)v);

            // T3 でボタンが入ったので、位置リセットもここで生きた設定になる（④と同じ形）。
            volcano.AddButton(Strings.VolcanoResetButton, delegate
            {
                ModSettings.VolcanoButtonX.value = -1;
                ModSettings.VolcanoButtonY.value = -1;
            });

            // ★★ 設定画面でも不可逆であることを名乗る（設計書 §7.1）。
            //    パネルの警告はパネルを開いた人しか読まない。
            helper.AddGroup(Strings.VolcanoIrreversibleWarning);

            var general = helper.AddGroup(Strings.GroupGeneral);
            general.AddCheckbox(Strings.IntensityUnlock, ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);

            // 競合MOD が居るときだけ出す。居ないときはこの設定に意味が無く、
            // 値は保存したまま既定動作（Disaster + が担当）に戻る。
            if (ModCompat.NdrPresent)
            {
                // 強度解放が既定 OFF になっている理由を明示する（仕様 3.3(a)）。
                // 黙って OFF だと「設定が効いていない」と見える。
                helper.AddGroup(Strings.IntensityUnlockHandledByOther);

                var compat = helper.AddGroup(Strings.NdrDetected);

                // ラベル配列は static readonly にしてはいけない。型初期化時の言語で凍結する。
                // 毎回組み直すことで言語切替に追従する。
                string[] owners = { Strings.EarthquakeOwnerOther, Strings.EarthquakeOwnerSelf };

                int current = ModSettings.EarthquakeDamageOwner.value;
                if (current < 0 || current >= owners.Length) current = ModSettings.EarthquakeOwnerOther;

                compat.AddDropdown(Strings.EarthquakeDamageOwner, owners, current,
                    v => ModSettings.EarthquakeDamageOwner.value = v);
            }

            var dbg = helper.AddGroup(Strings.GroupDebug);
            dbg.AddCheckbox(Strings.OverlayEnabled, ModSettings.OverlayEnabled.value,
                v => ModSettings.OverlayEnabled.value = v);

            // ラベル配列は static にしないこと。型初期化時の言語で凍結する。
            string[] keys = { "F9", "F10", "F11", "F12" };
            int[] codes = { (int)UnityEngine.KeyCode.F9, (int)UnityEngine.KeyCode.F10,
                            (int)UnityEngine.KeyCode.F11, (int)UnityEngine.KeyCode.F12 };
            int current2 = 2;
            for (int i = 0; i < codes.Length; i++)
                if (codes[i] == ModSettings.OverlayHotkey.value) current2 = i;

            dbg.AddDropdown(Strings.OverlayHotkey, keys, current2,
                v => ModSettings.OverlayHotkey.value = codes[v]);

            // Assembly-CSharp にも同名の LogChannel (ゲーム側の別物) があるため、
            // using を足すと解決が衝突する。常に完全修飾で参照する。
            //
            // ここに出すのは「実際にそのチャンネルのログを出している機能」だけにする。
            // Log.Diag(key, msg) は General へ委譲されるので General は本物のスイッチだが、
            // FireWhirl チャンネルを付けた呼び出しは 1 件も無い（設計書 5.2 が本フェーズでの
            // 移行を禁じている: 移行すると既定 OFF になり docs/playtest-checklist.md の
            // 手順が壊れる）。チェックボックスだけ置くと「切っても何も変わらない」
            // 死んだ設定になるので、②〜⑤がチャンネル付きログを出すまで UI から外す。
            // ビットと保存キーは公開契約なので消さない（LogChannel.FireWhirl は据え置き）。
            var channels = dbg.AddGroup(Strings.LogChannels);
            channels.AddCheckbox(Strings.LogChannelGeneral,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.General, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.General)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.General));

            // FireWhirl と違い、Forecast チャンネル付きの Log.Diag 呼び出しが実在する
            // （ForecastFeature.OnSimulationTick）。このチェックボックスは死んだ設定ではない。
            channels.AddCheckbox(Strings.LogChannelForecast,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Forecast, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Forecast)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Forecast));

            // Forecast と同じく、Earthquake チャンネル付きの Log.Diag 呼び出しが実在する
            // （EarthquakeFeature.OnSimulationTick）。死んだ設定ではない。
            channels.AddCheckbox(Strings.LogChannelEarthquake,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Earthquake)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Earthquake));

            // Forecast / Earthquake と同じく、Typhoon チャンネル付きの Log.Diag 呼び出しが
            // 実在する（TyphoonFeature.OnSimulationTick）。死んだ設定ではない。
            channels.AddCheckbox(Strings.LogChannelTyphoon,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Typhoon));

            // Volcano チャンネル（= 32）は⑤の Task 2 まで**定義済み・未使用**だった。
            // VolcanoFeature.OnSimulationTick がこのチャンネル付きの Log.Diag を出すので、
            // ここで初めて死んだ設定ではなくなる。
            channels.AddCheckbox(Strings.LogChannelVolcano,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.Volcano, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.Volcano)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.Volcano));
        }
    }
}
