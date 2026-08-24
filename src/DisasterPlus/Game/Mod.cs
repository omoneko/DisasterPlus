using ICities;
using DisasterPlus.Core.FireWhirl;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description { get { return Strings.ModDescription; } }

        /// <summary>
        /// メインメニューの起動中に 1 回だけ呼ばれる（言語切替でも再実行される）。
        /// レベルロード後にしか分からない情報からオプションを組み立ててはいけない。
        ///
        /// ── ★★ この画面に文章を足す前に読むこと ────────────────────
        ///
        /// 所有者の指示は「Option 画面も説明書きが長すぎます。もっとシンプルに」である。
        /// 線引きはこう決めた:
        ///
        /// | ここに出す | 例 |
        /// |---|---|
        /// | **選ぶために要ること** | つまみのラベル、範囲、既定値の意味 |
        /// | **使えない理由** | 「Natural Disasters DLC が必要です」 |
        /// | **取り返しがつかないこと** | 火山の地形変更は戻せない |
        /// | **設定が消えた告知** | 退役した項目（.cgs は公開契約である） |
        ///
        /// | ここに出さない | 行き先 |
        /// |---|---|
        /// | 「バニラはこうしている」の解説 | 診断ダンプ（<c>WriteNotes</c>） |
        /// | 機能の仕組みの説明 | その機能のパネル（左上のショートカット） |
        /// | テスターが切り分けに使う事実 | 診断ダンプ |
        ///
        /// **落としたのは説明であって、情報ではない。** 説明の 1 行を消すときは、
        /// その内容がダンプかパネルのどちらにあるかを確かめてから消すこと。
        ///
        /// ★ ラベルに畳めるものはラベルに畳む。1 行の注記より、選ぶ対象の名前に
        ///   書いてあるほうが短くて確実である（例: 危険半円がどちら側か）。
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

                // ★★ **帯を広げた（2026-08-22）。** 既定を 450 m / 108 棟へ上げたのに
                //    スライダーが 400 / 40 で止まっていると、**触った瞬間に既定より
                //    小さい値へ落ちる**（しかも下がったことは画面に出ない）。
                //    設定の既定値を変えたら、必ずその値を含む帯にすること。
                fw.AddSlider(Strings.DetectRadius, 50f, 900f, 25f, ModSettings.DetectRadius.value,
                    v => ModSettings.DetectRadius.value = (int)v);

                fw.AddSlider(Strings.DetectCount, 4f, 200f, 4f, ModSettings.DetectCount.value,
                    v => ModSettings.DetectCount.value = (int)v);

                // ★ バニラ（DLC）の竜巻を止める。火災旋風の渦には影響しない
                //   （VanillaTornadoSuppressor のクラス doc）。
                fw.AddCheckbox(Strings.NoVanillaTornado, ModSettings.NoVanillaTornado.value,
                    v => ModSettings.NoVanillaTornado.value = v);

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
            // ★ 「ボタン位置をリセット」は①②④⑤の 4 つとも撤去した。ボタンは
            //    バニラの災害パネルの中に置かれるようになり、位置はパネルの
            //    autolayout が決めるので（DisasterPanelBar）、押しても何も起きない
            //    死んだボタンになる。保存キー（forecastButtonX/Y 等）は .cgs の
            //    公開契約なので ModSettings 側に残してあるが、**別の意味で
            //    再利用してはいけない**（ModSettings の当該コメント参照）。

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

                // ★ 合成記象（P 波・S 波・コーダ）。**既定 OFF。**
                //   こちらは建物を 1 軒も壊さないが、**カメラの揺れの形をバニラから
                //   変える**ので、やはり第 2 層である。上の EarthquakeShakeBoost が
                //   既定 ON にできるのは強度 55 で追加分が厳密に 0 になるからで
                //   （ShakeWaveform.IntensityFactor）、こちらにその逃げ道は無い。
                earthquake.AddCheckbox(Strings.EarthquakeSeismogramEnabled,
                    ModSettings.EarthquakeSeismogram.value,
                    v => ModSettings.EarthquakeSeismogram.value = v);
            }

            // ★ ②の解説 3 本（揺れの補正・長周期・合成記象）はこの画面から降ろした。
            //   - 何をする設定かは**チェックボックスのラベル**が名乗っている
            //     （「バニラには無い被害を足します」等）
            //   - 「バニラはこうしている」の解説は診断ダンプ（EarthquakeFeature.WriteNotes）
            //   - 長周期の注記は②のパネルにも同じ文が出る（EarthquakeLayer2Rows）
            //   消したのは説明であって、情報ではない（このメソッドの doc の表）。

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
            // ★ 風害は既定 ON（②の第 2 層と判断が違う理由は TyphoonWind のクラス doc）。
            //    強さ 0 で完全に無効になる。
            typhoon.AddCheckbox(Strings.TyphoonWindEnabled, ModSettings.TyphoonWindDamage.value,
                v => ModSettings.TyphoonWindDamage.value = v);
            typhoon.AddSlider(Strings.TyphoonWindStrength, 0f, 10f, 1f,
                ModSettings.TyphoonWindStrength.value,
                v => ModSettings.TyphoonWindStrength.value = (int)v);
            // ★ 危険半円の向き。既定は北半球（＝進行方向の右が強い）。
            typhoon.AddCheckbox(Strings.TyphoonSouthernHemisphere,
                ModSettings.TyphoonSouthernHemisphere.value,
                v => ModSettings.TyphoonSouthernHemisphere.value = v);
            // ★ 河川氾濫も既定 ON。**セーブに焼き付く状態を触る唯一の機能**なので、
            //    水位は台風の終了時・都市を出るとき・保存のたびに元へ戻す。
            typhoon.AddCheckbox(Strings.TyphoonFloodEnabled,
                ModSettings.TyphoonFloodEnabled.value,
                v => ModSettings.TyphoonFloodEnabled.value = v);
            typhoon.AddSlider(Strings.TyphoonFloodStrength, 0f, 10f, 1f,
                ModSettings.TyphoonFloodStrength.value,
                v => ModSettings.TyphoonFloodStrength.value = (int)v);
            // ★ 竜巻並みの局所被害。**竜巻の実体は 1 つも作らない**（既定 ON）。
            //    随伴竜巻（バニラの竜巻災害を借りる機能）は撤去された ——
            //    その代わりがこれである。強さ 0 で完全に無効になる。
            typhoon.AddCheckbox(Strings.TyphoonGustEnabled,
                ModSettings.TyphoonGustEnabled.value,
                v => ModSettings.TyphoonGustEnabled.value = v);
            typhoon.AddSlider(Strings.TyphoonGustStrength, 0f, 10f, 1f,
                ModSettings.TyphoonGustStrength.value,
                v => ModSettings.TyphoonGustStrength.value = (int)v);
            // ★ 雲は既定 ON。**見た目だけの機能**で、切っても他の 5 要素はそのまま動く
            //    （TyphoonCloud のクラス doc の独立性）。
            typhoon.AddCheckbox(Strings.TyphoonCloudEnabled,
                ModSettings.TyphoonCloudEnabled.value,
                v => ModSettings.TyphoonCloudEnabled.value = v);
            typhoon.AddCheckbox(Strings.TyphoonVanillaCloudBoost,
                ModSettings.TyphoonVanillaCloudBoost.value,
                v => ModSettings.TyphoonVanillaCloudBoost.value = v);
            // ★ 暴風雨の演出。風害とは別のつまみである（ModSettings.TyphoonStormFx の doc）。
            typhoon.AddCheckbox(Strings.TyphoonStormFx,
                ModSettings.TyphoonStormFx.value,
                v => ModSettings.TyphoonStormFx.value = v);
            // ★ 風の音。EffectGroup へ流すので効果音スライダーとミュートは効く。
            typhoon.AddCheckbox(Strings.TyphoonStormSound,
                ModSettings.TyphoonStormSound.value,
                v => ModSettings.TyphoonStormSound.value = v);
            // ★ ④の解説 5 本もこの画面から降ろした。
            //   - 強度の目安（バニラの嵐は 55）は**スライダーのラベル**に畳んだ
            //   - 危険半円がどちら側かは**チェックボックスのラベル**が名乗っている
            //   - 風害・局所被害・氾濫の説明は④のパネルに同じ文が出る
            //     （TyphoonEffectRows。左上のショートカットから 1 クリック）
            //   - 危険半円の理屈は診断ダンプ（TyphoonFeatureDiagnostics.WriteNotes）
            //
            // ★★ **退役した設定の告知だけは残す。** 随伴竜巻を ON にしていた
            //    プレイヤーには、その項目が消えたことと .cgs の値がもう読まれない
            //    ことを 1 度は見せる（設定は公開契約である）。
            helper.AddGroup(Strings.TyphoonTornadoRetiredNote);

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

            // ★★ **半径と最終高のスライダーは撤去した**（2026-08-22）。
            //    大きさを決めるつまみは**災害パネルの強度スライダー 1 本だけ**である。
            //    基準は形態ごとの推奨値（<c>VolcanoShape.DefaultRadiusOf</c> /
            //    <c>DefaultHeightOf</c>）。経緯は <c>VolcanoSizeScale</c> のクラス doc。
            //    .cgs の volcanoRadius / volcanoHeight は**退役**であり、
            //    別の意味で使い回さないこと（<c>ModSettings</c> の退役の覚書）。

            // ★ 準備が隆起より先行する距離（T5）。**下限は 0 ではなく 16 m** ——
            //    0 だと「何も壊さない → 何も上がらない → 進捗が動かない」の輪から
            //    出られなくなる（VolcanoClearing.LeadMetres）。使う側でも
            //    同じ下限へクランプするので、.cgs を手で書き換えても止まらない。
            volcano.AddSlider(Strings.VolcanoClearingLead, 16f, 400f, 16f,
                ModSettings.VolcanoClearingLeadMetres.value,
                v => ModSettings.VolcanoClearingLeadMetres.value = (int)v);

            // ★ 隆起にかけるゲーム内分（T6）。長すぎる値を入れても
            //    UpliftSchedule.TotalTicksFor が「山頂が毎 tick 1/64 m 以上動く」上限で
            //    切り詰めるので、**無言で隆起が止まることは無い**（罠 2）。
            volcano.AddSlider(Strings.VolcanoUpliftMinutes, 5f, 240f, 5f,
                ModSettings.VolcanoUpliftMinutes.value,
                v => ModSettings.VolcanoUpliftMinutes.value = (int)v);

            // ★ 山肌の凹凸の強さ（%）。**0 で今日どおりの滑らかな円錐**に戻る。
            //   形態ごとの性格（谷の本数・波長・粗さ）は Core/Volcano/VolcanoRelief が
            //   持っていて、ここはその全体倍率 1 本だけである（つまみを増やさない）。
            //   起伏は半径 R と最終高 H を決して超えない —— 掛け算だけで作ってあり、
            //   実効半径は縮む向きにしか動かない（VolcanoRelief のクラス doc）。
            volcano.AddSlider(Strings.VolcanoReliefStrength,
                0f, VolcanoRelief.MaxStrengthUnit * 100f, 10f,
                ModSettings.VolcanoReliefStrength.value,
                v => ModSettings.VolcanoReliefStrength.value = (int)v);

            // ★ 噴煙を描くか（T7）。**描画は main スレッドだけの機能**なので、
            //    切っても隆起は同じように進む（実機チェックリストの項目でもある）。
            volcano.AddCheckbox(Strings.VolcanoEruptionFx, ModSettings.VolcanoEruptionFx.value,
                v => ModSettings.VolcanoEruptionFx.value = v);
            // ★ 火山雷。噴煙の中でしか光らないので、噴煙を切ればこれも出ない。
            //   別の設定にしてあるのは、閃光が苦手な人に噴煙ごと切らせないためである。
            volcano.AddCheckbox(Strings.VolcanoLightningSetting,
                ModSettings.VolcanoLightningFx.value,
                v => ModSettings.VolcanoLightningFx.value = v);

            // ★ 斜面を下る土煙の帯（「火砕流」の代用）。**切っても噴火も溶岩も変わらない**
            //   —— この帯は何も壊さないし、ゲームに火砕流のエフェクトは 1 つも無い
            //   （Strings.VolcanoPyroclasticNote がそう名乗る）。
            //   噴火の描画とは別のつまみにしてあるのは、帯 1 本の粒子数が大きく、
            //   これだけ切りたい人が居る見た目だからである。
            volcano.AddCheckbox(Strings.VolcanoPyroclasticSetting,
                ModSettings.VolcanoPyroclasticFx.value,
                v => ModSettings.VolcanoPyroclasticFx.value = v);

            // ★ 噴火の音。**切っても隆起も溶岩も噴煙も変わらない**（音も main スレッド
            //   だけの機能である）。音量つまみはここに置かない ——
            //   ゲーム本体の効果音スライダーとミュートがそのまま効くので、
            //   2 本目を作ると「どちらが効いているのか」が分からなくなる。
            volcano.AddCheckbox(Strings.VolcanoEruptionSound,
                ModSettings.VolcanoEruptionSound.value,
                v => ModSettings.VolcanoEruptionSound.value = v);

            // ★ 溶岩の本数（T8）。**0 で完全に無効**（溶岩も着火も出ない）。
            //    上限は VolcanoLava.MaxFlows と同じ 8 —— 1 tick あたりの仕事量が
            //    「本数 × 2 歩」で決まるので、ここが費用の上限そのものである。
            volcano.AddSlider(Strings.VolcanoLavaFlowsSetting, 0f, VolcanoLava.MaxFlows, 1f,
                ModSettings.VolcanoLavaFlows.value,
                v => ModSettings.VolcanoLavaFlows.value = (int)v);

            // ★ 着火を切っても溶岩は流れる（見た目だけになる）。
            volcano.AddCheckbox(Strings.VolcanoLavaFireSetting, ModSettings.VolcanoLavaFire.value,
                v => ModSettings.VolcanoLavaFire.value = v);

            // ★ 溶岩の面を描くか（T9）。**切っても溶岩は流れ、地面を焦がし、
            //    建物に火を付ける** —— T9 は他のどのタスクからも依存されていない。
            volcano.AddCheckbox(Strings.VolcanoLavaRenderSetting,
                ModSettings.VolcanoLavaRender.value,
                v => ModSettings.VolcanoLavaRender.value = v);

            // ★ 火山性地震。②の設定とは無関係に効く（VolcanoTremorShake のクラス doc）。
            //   ラベルに「揺れるだけ」と畳んである —— 説明の 1 行より、選ぶ対象の
            //   名前に書いてあるほうが短くて確実である（このメソッドの doc の規律）。
            volcano.AddCheckbox(Strings.VolcanoQuakeSetting,
                ModSettings.VolcanoQuake.value,
                v => ModSettings.VolcanoQuake.value = v);

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
            // ★ 診断ダンプの出し方はもうここに書かない。左上のショートカットの
            //   「診断」タブに**押せるボタン**がある（DiagnosticsPanel）——
            //   説明を減らすというのは、出し方ごと隠すことではない。

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
