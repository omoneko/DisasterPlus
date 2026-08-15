using System;
using System.Collections.Generic;
using System.Reflection;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 設計書 付録A の前提を、実行時に実際のゲームへ問い合わせて照合する。
    ///
    /// なぜ要るか: ③の実装中、IL から読んだ前提が 3 件も誤っていた
    /// (DAYTIME_FRAMES の 4 倍ズレ、m_targetPos0 の意味、m_fireIntensity 直接書込)。
    /// 共通点は「前提が破れても何も起きない」こと。ゲームは平然と動き、
    /// 挙動だけが静かに違う。ここで名指しでログに出すのが唯一の防波堤になる。
    ///
    /// 前提を 1 ファイルに集約しているのは、付録A と突き合わせて監査するため。
    /// ②〜⑤で前提が増えたらここに足すこと。
    ///
    /// スレッド安全性: Run() / Reset() / ReportSliderOutcome() は main スレッドからのみ
    /// 呼ばれる想定だが、LastResults は DiagnosticsHub.CollectionEnabled が立つと
    /// FeatureHost.BuildReport() 経由で sim スレッドから毎 tick 読まれる
    /// （Task 5 以降）。書き込み中の List を読む競合を避けるため、_results への
    /// 全アクセスを _gate 1 本で直列化する。FeatureHost._errorGate や
    /// DiagnosticsHub の gate とはロック順序の関係を作らないよう、
    /// このロックを持ったまま他クラスのコードは一切呼ばない。
    /// </summary>
    public static class Assumptions
    {
        private const string SliderCheckName = "Disasters panel intensity slider is reachable";
        private const string SliderCheckImpact = "disaster intensity cannot be unlocked to 25.5";

        /// <summary>
        /// 1 回のレベルロードで最終的に埋まる検証件数。Run() の 31 件 ＋
        /// ReportSliderOutcome() の 1 件。Report() が「何件中の集計か」を
        /// 名乗るために使う。Run() に検証を足したらここも増やすこと。
        ///
        /// 内訳: 火災旋風 4 件（既存）＋ 天気予報 7 件
        /// （SetCurrentMode 解決可否・SubInfoMode 2 種の存在・
        /// ThunderStormAI/TornadoAI.UpdateHazardMap の存在・
        /// WeatherManager の current/target フィールド群・
        /// DisasterManager.m_hazardAmount が private Byte[] のままか・
        /// m_disasters の m_buffer/m_size・ハザードグリッドの形状）＋
        /// 地震 8 件（EarthquakeAI プレハブの 4 調整値・DisasterData の
        /// m_intensity/m_activationFrame/m_startFrame/m_angle・
        /// EarthquakeCoverage と CheckLocalResource・sim スレッドの時計・
        /// SubInfoMode.EarthquakeHazard と EarthquakeAI.UpdateHazardMap・
        /// VanillaRandomizer と本物の Randomizer のビット一致・
        /// m_cameraShake と m_disableCameraShake が public のままか・
        /// RenderManager のオーバーレイ描画 API が届くか）＋
        /// 地震 第 2 層 3 件（TsunamiAI プレハブの実在・
        /// TerrainManager.HasWater と DisasterData.m_waveIndex・
        /// BuildingAI.CollapseBuilding と BuildingInfo.m_size / m_generatedInfo）＋
        /// 台風 9 件（嵐プレハブの 3 調整値・竜巻プレハブの 3 調整値・
        /// DisasterData の移動 4 フィールドと DisasterAI の公開ラッパー 3 メソッド・
        /// WeatherManager の target 系 6 フィールド・
        /// QueueLightningStrike の 4 引数版・
        /// BuildingAI.CollapseBuilding と DisasterHelpers.AddWind / DestroyTrees・
        /// WaterSimulation の到達経路と WaterSource の m_type / m_target・
        /// Vehicle.SetTargetPos と InstanceManager.GetAllGroupInstances・
        /// 雲の透過シェーダと Graphics.DrawMesh）＋
        /// スライダー検証 1 件。
        ///
        /// スライダー検証が「対象外」に確定した場合はこの母数から 1 件引く
        /// （<see cref="_sliderNotApplicable"/>）。
        /// </summary>
        private const int TotalCheckCount = 32;

        private static readonly object _gate = new object();
        private static readonly List<AssumptionResult> _results = new List<AssumptionResult>();

        /// <summary>
        /// Natural Disasters DLC を持たない環境では FAIL するのが正常な検証の名前。
        /// <see cref="Check(string,string,Func{bool},bool)"/> が登録する。
        ///
        /// **名前を別表として二重に書かない。** 書くと検証名を直したときに黙って
        /// 対応が切れ、正常な FAIL がまた警告として出るようになる。
        /// <see cref="Reset"/> では消さない（ゲームのビルドに対する事実であって
        /// 都市ごとの状態ではない）。
        /// </summary>
        private static readonly List<string> _expectedWithoutDlc = new List<string>();

        private static bool _ran;

        /// <summary>
        /// スライダー検証が「この環境では対象外」に確定したか。
        ///
        /// 設定で強度解放を切っている環境では、そもそも検証すべき前提が無い。
        /// これは NDR を検出した環境の既定値なので、「保留中」のまま放置すると
        /// 該当ユーザーには永久に未確定の集計が出続ける。母数から外して
        /// 「4 件中 4 件」と正直に名乗るのがこちらの選択。
        /// Run() / Reset() / ReportSlider* と同じく main スレッド専用（_ran と同じ扱い）。
        /// </summary>
        private static bool _sliderNotApplicable;

        /// <summary>
        /// _results が前の都市のものか。Reset() で立て、この都市で最初の結果を
        /// 書き込むときに SetResult が捨てる（＝遅延クリア）。
        ///
        /// 「Run() の先頭でクリア」にはできない。ReportSliderOutcome() は
        /// FeatureHost.LevelLoaded() → IntensityUnlock.Apply() の経路で
        /// Assumptions.Run() より先に走ることがあり（DisasterPlusLoading の呼び出し順）、
        /// Run() の先頭で消すとその 1 件だけ黙って失われる。
        /// 「次の書き込みで消す」なら、どちらが先でも積み増しにならず、
        /// かつメインメニューでは前の都市の結果が残る。
        /// _gate の内側でだけ触ること。
        /// </summary>
        private static bool _stale;

        /// <summary>呼び出し元がリストを保持し続けても _results の以後の変更から保護されるよう、
        /// 常に防御的コピーを返す。</summary>
        public static IList<AssumptionResult> LastResults
        {
            get
            {
                lock (_gate) { return new List<AssumptionResult>(_results); }
            }
        }

        /// <summary>
        /// レベルアンロード時に呼ぶ。次のロードで Run() を再実行できるようにするだけで、
        /// 結果は消さない。
        ///
        /// ここで _results を消してはいけない。OnSettingsUI（設計書 4.4 が要求する
        /// 3 つの出力先のひとつ）はメインメニューで走る＝必ずこの Reset() より後になるため、
        /// ここで消すと LastResults は常に空になり、設定画面の前提警告が原理的に
        /// 出せなくなる（Strings.AssumptionsFailedHint が案内している手順そのものが
        /// 何も表示しない手順になる）。
        /// 前の都市の結果はメインメニューまで持ち越す。積み増しにならないよう、
        /// 次の都市で最初に結果が書かれた時点で SetResult がまとめて捨てる（_stale 参照）。
        /// </summary>
        public static void Reset()
        {
            _ran = false;
            _sliderNotApplicable = false;
            lock (_gate) { _stale = true; }
        }

        /// <summary>
        /// レベルロード完了後に 1 回だけ呼ぶ。起動時ではないのは、
        /// Harmony の適用状況と prefab の解決を見る必要があるため。
        ///
        /// ここでは確定的に判定できる 31 件だけを見る。強度スライダーの到達可否は
        /// この時点ではまだ「未構築なだけ」の可能性が拭えない（IntensityUnlock 自身が
        /// 100 回・120 フレーム間隔のリトライを持つほど）ので、ここで即座に判定して
        /// FAIL を出すと、実際には後で正常に到達できるケースまで誤報になる。
        /// その 1 件は ReportSliderOutcome() が IntensityUnlock の確定後に個別に埋める。
        /// </summary>
        public static void Run()
        {
            if (_ran) return;
            _ran = true;

            Check("SimulationManager.DAYTIME_FRAMES == 65536",
                  "all in-game durations will be wrong",
                  delegate { return SimulationManager.DAYTIME_FRAMES == 65536; });

            Check("VortexAI.SimulationStep(6) is patched",
                  "fire whirls will drift instead of staying put",
                  delegate { return HarmonyBootstrap.Installed && VortexStepIsPatched(); });

            Check("BuildingAI.BurnBuilding is resolvable",
                  "fire spread will not work",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("BurnBuilding",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ushort), typeof(Building).MakeByRefType(),
                              typeof(InstanceManager.Group), typeof(bool)
                          },
                          null) != null;
                  });

            // DLC 非所持環境では FAIL するのが正常（プレハブごと存在しない）。
            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created. This also FAILs when the Natural Disasters "
                  + "DLC is not owned, which is expected.",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); },
                  true);

            // --- ①天気予報タブ（Task 5）ここから ---

            Check("InfoManager.SetCurrentMode is resolvable",
                  "cannot switch to the vanilla disaster hazard heatmap view",
                  delegate
                  {
                      return typeof(InfoManager).GetMethod("SetCurrentMode",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[] { typeof(InfoManager.InfoMode), typeof(InfoManager.SubInfoMode) },
                          null) != null;
                  });

            Check("SubInfoMode.LightningHazard / TornadoHazard exist",
                  "lightning/tornado hazard display cannot be shown",
                  delegate
                  {
                      var t = typeof(InfoManager.SubInfoMode);
                      // 文字列ベースで見る。列挙メンバへのコード内の直接参照はコンパイル時に
                      // 整数値へ畳み込まれるため、将来ゲーム側で名前が変わったり削除されたり
                      // しても再ビルドしない限り検出できない。ここは実行時に「今のゲームの
                      // アセンブリにその名前のメンバが実在するか」を毎回問い直す。
                      return Enum.IsDefined(t, "LightningHazard") && Enum.IsDefined(t, "TornadoHazard");
                  });

            // impact 文を全体レビューで訂正した。以前は「ハザードマップが埋まらない＝
            // 空か古いデータが出る」と書いていたが、これは「本来は埋まっているはずの
            // 静的なリスク面」を前提にした誤った説明だった。IL 実測（本 MOD 自身で
            // 再確認）では、この 2 つの UpdateHazardMap は
            //   (m_flags & 4096) == 0 -> return   … 4096 = DisasterData.Flags.Located
            //   (m_flags & 12)   == 0 -> return   … 12   = Emerging|Active
            // という 2 段ゲートで始まり、地形も建物も一切参照せず、通過した場合だけ
            // m_targetPosition の周りに半径と強度で決まる円盤を塗る。つまりこのマップは
            // 静的なリスク面ではなく「測位済みで進行中の嵐の予測被害範囲」であり、
            // 空であること自体は正常な状態（＝今どの嵐も検知されていない）でもある。
            // impact はメソッドが消えた場合に何が失われるかだけを述べる。
            Check("ThunderStormAI/TornadoAI.UpdateHazardMap exist",
                  "a located, in-progress storm's predicted impact area cannot be painted, "
                  + "so the hazard map would stay empty even while a storm is detected",
                  delegate
                  {
                      return HasUpdateHazardMap(typeof(ThunderStormAI))
                          && HasUpdateHazardMap(typeof(TornadoAI));
                  });

            // 全体レビュー指摘(I5): ここは以前 m_groundWetness / m_lastLightningIntensity /
            // m_targetDirection も要求していたが、この 3 つは MOD のどこからも読んでいない。
            // 「予報機能の中核が壊れる」という重い impact の項目が、機能が使っていない
            // フィールドの改名だけで FAIL しうる状態だった——誤検知を消すための層が
            // 誤検知を出すのでは本末転倒なので、実際に WeatherReader が読むものだけに絞る。
            // （m_groundWetness / m_lastLightningIntensity は読み取り自体も撤去した。
            //  m_currentFog / m_targetFog は Fog 行を新設して表示側へ回したので残す。）
            Check("WeatherManager current/target fields are resolvable",
                  "trend cannot be computed (the core of the forecast feature)",
                  delegate
                  {
                      // 全て Single であることを実際のゲームアセンブリで確認済み。
                      var t = typeof(WeatherManager);
                      return HasField(t, "m_currentRain", typeof(float))
                          && HasField(t, "m_targetRain", typeof(float))
                          && HasField(t, "m_currentCloud", typeof(float))
                          && HasField(t, "m_targetCloud", typeof(float))
                          && HasField(t, "m_currentFog", typeof(float))
                          && HasField(t, "m_targetFog", typeof(float))
                          && HasField(t, "m_currentTemperature", typeof(float))
                          && HasField(t, "m_targetTemperature", typeof(float))
                          && HasField(t, "m_windDirection", typeof(float));
                  });

            // 測位済み災害の走査に必要なもの（WeatherReader.CountLocatedStorms）。
            // ここが解決できないと「嵐が検知されていない」という説明を出す根拠が消え、
            // パネルは再び全ゼロのグリッドを「落雷: 0」と表示する側へ戻ってしまう。
            Check("DisasterManager.m_disasters exposes m_buffer / m_size",
                  "located storms cannot be counted, so an all-zero hazard grid would again be "
                  + "shown as a real '0' instead of 'no storm detected'",
                  delegate
                  {
                      var f = typeof(DisasterManager).GetField("m_disasters",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (f == null) return false;
                      // FastList<DisasterData> であることまで見る。名前だけでは、
                      // 中身が別の型の FastList に差し替わっても通ってしまう。
                      return HasField(f.FieldType, "m_buffer", typeof(DisasterData[]))
                          && HasField(f.FieldType, "m_size", typeof(int));
                  });

            // グリッド形状。HazardMapReader の GridSize / WorldUnitsPerCell は
            // DisasterManager の const を参照して書いているが、C# の const は
            // コンパイル時に呼び出し側へ焼き込まれるため、出荷済み DLL の中では
            // 単なる即値 256 / 38.4 である（HazardMapReader のコメント参照）。
            // したがって「参照しているから自動追従する」は成り立たない。
            // 再ビルドを挟まずに食い違いを検知するには、実行時に**ロード中のゲームの
            // メタデータ**を読むしかない。GetRawConstantValue() は const の宣言値を
            // メタデータから直接取るので、焼き込み済みの即値とは別経路になる。
            Check("DisasterManager hazard grid geometry is 256 cells x 38.4 m",
                  "hazard values would be sampled from the wrong cell (the label would be right "
                  + "but the number would belong to somewhere else)",
                  delegate
                  {
                      var res = typeof(DisasterManager).GetField("HAZARDMAP_RESOLUTION",
                          BindingFlags.Public | BindingFlags.Static);
                      var cell = typeof(DisasterManager).GetField("HAZARDMAP_CELL_SIZE",
                          BindingFlags.Public | BindingFlags.Static);
                      if (res == null || cell == null) return false;
                      if (!res.IsLiteral || !cell.IsLiteral) return false;
                      return (int)res.GetRawConstantValue() == 256
                          && (float)cell.GetRawConstantValue() == 38.4f;
                  });

            // Task 4 レビュー指摘の持ち越し分。HazardMapReader は m_hazardAmount を
            // リフレクションで直接読む（公開 API は Color しか返さないため）。
            // このフィールドがゲーム更新で改名・型変更されても HazardMapReader 自身は
            // 例外にせず黙って 0/ok=false へ倒れるので、ここで名指ししないと
            // 「もっともらしいがずっと 0 のハザード数値」が起動時の ASSUMPTIONS 要約に
            // 一切現れないまま静かに壊れる。
            Check("DisasterManager.m_hazardAmount is a private Byte[] field",
                  "hazard numbers may silently read wrong data if the game renames or retypes this field",
                  delegate
                  {
                      var f = typeof(DisasterManager).GetField("m_hazardAmount",
                          BindingFlags.NonPublic | BindingFlags.Instance);
                      return f != null && f.FieldType == typeof(byte[]);
                  });

            // --- ①天気予報タブ（Task 5）ここまで ---

            // --- ②地震（Task 3）ここから ---

            // このプロジェクトで唯一「実行時にしか値が取れない」前提。
            // m_crackLength / m_crackWidth / m_emergingDuration / m_activeDuration の
            // 実数値は DLL に無く（プレハブのシリアライズ値、IL 事実文書 §A-0）、
            // UnityPy による sharedassets の読み出しも失敗している。②の以後の
            // 持続時間の設計は全てこの 4 値の上に乗るので、読めないなら読めないと
            // 名指しする以外に防波堤が無い。
            //
            // DLC 非所持環境ではこれが FAIL するのが正常。TornadoAI の項目が既に
            // 同じ性質を持っており、それが確立した扱い。母数からは外さない
            // （ReportSliderNotApplicable 方式にしない）——地震機能そのものが
            // DLC 依存なので、「使えない」と名指しするのが正しい。
            Check("EarthquakeAI disaster prefab exposes its four tuning fields and "
                  + "m_activeDuration is non-zero",
                  "no earthquake durations or fault geometry can be read; every duration in this "
                  + "feature is designed on top of these four numbers. This also FAILs when the "
                  + "Natural Disasters DLC is not owned, which is expected.",
                  delegate
                  {
                      var t = typeof(EarthquakeAI);
                      if (!HasField(t, "m_crackLength", typeof(float))
                          || !HasField(t, "m_crackWidth", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // 副作用の無い純粋な走査を使う。EarthquakeReader の内部キャッシュは
                      // sim スレッドが回しており、main スレッドのここから巻き戻してはいけない
                      // （FireWhirlSpawner.HasTornadoPrefab と同じ理由）。
                      //
                      // ★★ **Resolved だけを見ない**（全体レビュー C1 の監査で見つかった
                      //    3 件目）。Resolved は「オブジェクトが見つかり 4 値を読み終えた」
                      //    だけで立ち、**中身を 1 バイトも見ていない**。②で実際に振る舞いを
                      //    決めているのは 4 箇所の `Resolved || ActiveDuration == 0u` という
                      //    ゲート（CameraShakeBooster / EarthquakeFeature /
                      //    EarthquakePanel / EarthquakeSensorRows）である。
                      //    m_activeDuration が 0 だと波形もカメラ補正も地震計も黙って
                      //    止まるのに、この検証だけ PASS を出していた。
                      //
                      //    m_crackLength / m_crackWidth は**ゲートに含めない**。
                      //    こちらは 0 のとき表示側が「unknown」と名乗る経路を既に持って
                      //    いる（EarthquakeFeature の "fault (L/W)" 行）ので、
                      //    黙って壊れる値ではない。
                      var quake = EarthquakeReader.ScanPrefabFacts();
                      return quake.Resolved && quake.ActiveDuration > 0u;
                  },
                  true);

            Check("DisasterData exposes m_intensity / m_activationFrame / m_startFrame / m_angle",
                  "neither the shaking strength nor the per-building margin can be shown",
                  delegate
                  {
                      var t = typeof(DisasterData);
                      return HasField(t, "m_intensity", typeof(byte))
                          && HasField(t, "m_activationFrame", typeof(uint))
                          && HasField(t, "m_startFrame", typeof(uint))
                          && HasField(t, "m_angle", typeof(float));
                  });

            // 列挙メンバは文字列で見る。コード内の直接参照はコンパイル時に整数へ
            // 畳み込まれるので、名前の変更を検出できない（SubInfoMode の検証と同じ理由）。
            Check("ImmaterialResourceManager.Resource.EarthquakeCoverage exists and "
                  + "CheckLocalResource is resolvable",
                  "seismograph coverage cannot be read, so the mod cannot explain why the hazard map is empty",
                  delegate
                  {
                      if (!Enum.IsDefined(typeof(ImmaterialResourceManager.Resource), "EarthquakeCoverage"))
                      {
                          return false;
                      }
                      return typeof(ImmaterialResourceManager).GetMethod("CheckLocalResource",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ImmaterialResourceManager.Resource),
                              typeof(UnityEngine.Vector3),
                              typeof(int).MakeByRefType()
                          },
                          null) != null;
                  });

            // sim スレッドの時計。ここが解決できないと、残るのは main スレッドが書く
            // m_currentDayTimeHour だけになる——それはスレッド境界を跨ぐ上に、
            // m_referenceFrameIndex（描画補間側）由来の別の量である（§F-1）。
            //
            // なお m_enableDayNight が false であること自体は前提の破れではない
            // （プレイヤーが選べる正当な設定で、hour が 12.0 に固定されるだけ）。
            // ここで FAIL にすると偽 FAIL になるので、その事実は診断ダンプの
            // "sim clock" 行とパネルで名乗る。
            Check("SimulationManager exposes m_dayTimeFrame / DAYTIME_FRAME_TO_HOUR / m_enableDayNight",
                  "the sim-thread clock cannot be read; the mod would have to fall back to "
                  + "m_currentDayTimeHour, which is written by the main thread",
                  delegate
                  {
                      var t = typeof(SimulationManager);
                      return HasField(t, "m_dayTimeFrame", typeof(uint))
                          && HasField(t, "m_enableDayNight", typeof(bool))
                          && HasStaticField(t, "DAYTIME_FRAME_TO_HOUR", typeof(float));
                  });

            // --- ②地震（Task 3）ここまで ---

            // --- ②地震（Task 4）ここから ---

            // 地震のハザードビューへの切替と、そこに何かを塗る側の両方。
            // ここが FAIL すると「ハザードマップが空である理由」——本機能が出す
            // いちばん重要な説明——を、そもそも見せる場所が無くなる。
            // 列挙メンバは文字列で見る（コード内の直接参照はコンパイル時に整数へ
            // 畳み込まれるので、名前の変更を検出できない）。
            Check("SubInfoMode.EarthquakeHazard exists and EarthquakeAI.UpdateHazardMap exists",
                  "the earthquake hazard heatmap cannot be shown, so the mod cannot explain "
                  + "the Located gate",
                  delegate
                  {
                      return Enum.IsDefined(typeof(InfoManager.SubInfoMode), "EarthquakeHazard")
                          && HasUpdateHazardMap(typeof(EarthquakeAI));
                  });

            // --- ②地震（Task 4）ここまで ---

            // --- ②地震（Task 5）ここから ---

            // **これが Task 1 のビット一致を実際に保証する唯一の検査である。**
            // 建物ごとの倒壊判定は全て VanillaRandomizer の再現の上に乗っており、
            // 1 ビットずれても何も壊れない——もっともらしい数字が出続けたまま、
            // パネルの断定だけが全部嘘になる。ユニットテストは LCG の定義からの
            // 逸脱しか捕まえられない（本物の DLL を参照できない）ので、
            // ゲーム本体との一致はここでしか見られない。
            Check("VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit",
                  "every per-building collapse verdict is wrong; the panel would keep showing "
                  + "plausible numbers that do not match what the game draws",
                  delegate
                  {
                      // 本物と自前の実装を並べて回す。ビット列だけでなく「引く順序」も見る
                      // （1 個ずれる壊れ方をこの検査で捕まえるため、必ず 2 回以上引く）。
                      // Randomizer は struct なので、必ずローカル変数に置いて使うこと
                      // （プロパティやフィールド経由で呼ぶとコピーが進んで列が分岐する）。
                      int[] seeds = { 0, 1, -1, 12345, 0x00070000 | 1234, int.MinValue, int.MaxValue };
                      for (int i = 0; i < seeds.Length; i++)
                      {
                          var real = new ColossalFramework.Math.Randomizer(seeds[i]);
                          var ours = new DisasterPlus.Core.Earthquake.VanillaRandomizer(seeds[i]);
                          for (int k = 0; k < 4; k++)
                          {
                              if (real.Int32(10000u) != ours.Int32(10000u)) return false;
                          }
                      }
                      return true;
                  });

            // --- ②地震（Task 5）ここまで ---

            // --- ②地震（Task 6）ここから ---

            // カメラの揺れの補正は、この 2 つの public フィールドの上にしか成り立たない。
            // どちらも Harmony を使わずに触れることが前提で（§A-7）、片方でも
            // 非公開化・改名・型変更されると CameraShakeBooster は例外を 1 回吐いた後
            // 黙って何も足さなくなる——そして**追加分 0 は強度 55 では正常な状態**
            // なので、画面を見ても機能が死んでいることに気付けない。
            //
            // m_disableCameraShake の方が重い。読めなければ「揺らすな」という
            // プレイヤーの明示的な選択を無視して足すことになるので、
            // CameraShakeBooster は読めない場合に**何も足さない**側へ倒している。
            Check("CameraController.m_cameraShake and DisasterManager.m_disableCameraShake "
                  + "are public fields",
                  "camera shake cannot be scaled with intensity and distance, and the mod cannot "
                  + "honour the player's \"disable camera shake\" choice",
                  delegate
                  {
                      var shake = typeof(CameraController).GetField("m_cameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (shake == null || shake.FieldType != typeof(UnityEngine.Vector3)) return false;

                      var disable = typeof(DisasterManager).GetField("m_disableCameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      return disable != null && disable.FieldType == typeof(bool);
                  });

            // --- ②地震（Task 6）ここまで ---

            // --- ②地震（震度分布の地図オーバーレイ）ここから ---

            // **この機能はまるごとこの 4 つの API の上に乗っている。**
            // どれか 1 つでも消えると、オーバーレイは例外を 1 回吐いた後
            // 黙って何も描かなくなる —— そして「何も描かない」は
            // 「地震が無い」「トグルが OFF」とも見分けが付かない。
            //
            // IL 実測（この機能の着手時に自分で逆アセンブルして確認した）:
            //   RenderManager::RegisterRenderableManager  public static、m_renderables へ Add するだけ
            //   OverlayEffect::OnPostRender  IL_00A3  → RenderManager::Managers_RenderOverlay
            //   Managers_RenderOverlay       IL_0050  → 各 IRenderableManager::EndOverlay
            //   OverlayEffect::DrawCircle / DrawQuad → DrawEffect → Graphics::DrawMeshNow（即時描画）
            //
            // 列挙メンバではなくメソッドなので、型引数まで込みで照合する
            // （オーバーロードが増えたときに GetMethod(name) が
            //  AmbiguousMatchException を投げて偽 FAIL になるのを避ける）。
            Check("RenderManager overlay drawing API is reachable "
                  + "(RegisterRenderableManager / OverlayEffect.DrawCircle / DrawQuad)",
                  "the earthquake intensity distribution cannot be drawn on the map at all; "
                  + "the panel would keep offering a toggle that does nothing",
                  delegate
                  {
                      if (typeof(RenderManager).GetMethod("RegisterRenderableManager",
                              BindingFlags.Public | BindingFlags.Static,
                              null, new Type[] { typeof(IRenderableManager) }, null) == null)
                      {
                          return false;
                      }

                      var effect = typeof(RenderManager).GetProperty("OverlayEffect",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (effect == null || effect.PropertyType != typeof(OverlayEffect)) return false;

                      if (typeof(OverlayEffect).GetMethod("DrawCircle",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                                  typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                                  typeof(float), typeof(bool), typeof(bool)
                              }, null) == null)
                      {
                          return false;
                      }

                      return typeof(OverlayEffect).GetMethod("DrawQuad",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                              typeof(ColossalFramework.Math.Quad3), typeof(float), typeof(float),
                              typeof(bool), typeof(bool)
                          }, null) != null;
                  });

            // --- ②地震（震度分布の地図オーバーレイ）ここまで ---

            // --- ②地震（Task 9: 第 2 層 — 海中震源からの津波連鎖）ここから ---

            // **DLC が無い環境ではここが FAIL するのが正常である。** TsunamiAI の
            // *型* は DLC の有無に関わらず Assembly-CSharp に同梱されているので、
            // 型の存在検査は通ってしまう。実在を決めるのは PrefabCollection に
            // TsunamiAI を持つ DisasterInfo が居るかどうかで（§B-5）、
            // ModCompat.NaturalDisastersOwned は UI を出すかどうかの事前判定にすぎない。
            // 影響の文にその期待を書いておかないと、正常な環境の FAIL が不具合に見える。
            //
            // 走査は副作用の無い純粋な問い合わせを使う（FireWhirlSpawner.HasTornadoPrefab
            // と同じ理由。ここは main スレッドで、sim スレッドのキャッシュを
            // 巻き戻してはいけない）。
            Check("TsunamiAI disaster prefab is available",
                  "the tsunami chain cannot run (this also FAILs when the Natural Disasters DLC "
                  + "is not owned, which is expected)",
                  delegate { return TsunamiChain.HasTsunamiPrefab(); },
                  true);

            // 津波連鎖の入口と出口。HasWater が解決できなければ「震源が水中か」を
            // 判断できず、m_waveIndex が読めなければ「波が実際に立ったか」を判断できない
            // ——後者が読めないと、内陸マップの正常な「何も起きない」を
            // 「起こしたつもり」と取り違える。
            //
            // 引数の型まで込みで照合する（オーバーロードが 2 つあり、名前だけで
            // GetMethod を引くと AmbiguousMatchException で偽 FAIL になる。
            // 実測: HasWater(Vector2) と HasWater(Segment2, float, bool)）。
            Check("TerrainManager.HasWater is resolvable and DisasterData exposes m_waveIndex",
                  "the mod cannot tell whether the epicentre is under water, nor whether a wave "
                  + "was actually raised",
                  delegate
                  {
                      var hasWater = typeof(TerrainManager).GetMethod("HasWater",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector2) }, null);
                      if (hasWater == null || hasWater.ReturnType != typeof(bool)) return false;

                      var wave = typeof(DisasterData).GetField("m_waveIndex",
                          BindingFlags.Public | BindingFlags.Instance);
                      return wave != null && wave.FieldType == typeof(ushort);
                  });

            // --- ②地震（Task 9）ここまで ---

            // --- ②地震（Task 10: 第 2 層 — 長周期地震動）ここから ---

            // **この機能は建物を実際に壊す。** だから前提が破れたときに
            // 「静かに違う挙動」になることを許さない。見るのは 2 つ:
            //
            //   1. BuildingAI.CollapseBuilding が引数まで込みで解決できるか。
            //      DisasterHelpers を経由しないのが NDR 回避の要点（§E-2）なので、
            //      迂回先そのものが消えていないかを名指しする。
            //   2. 建物の高さが読めるか。**Building 構造体に高さのフィールドは無い**
            //      （IL 実測。あるのは m_baseHeight / m_width / m_length だけ）。
            //      高さはプレハブ側の BuildingInfo.m_size（Vector3、m）の y で、
            //      InitializePrefab が m_generatedInfo.m_size から入れる（IL_09BE）。
            //      単位がメートルであることは CommonBuildingAI.CollapseIfFlooded の
            //      `waterLevel > m_position.y + Max(4f, m_collisionHeight)` で確定
            //      （m_collisionHeight の出発点が m_size.y。BuildingHeight の
            //      クラス doc に IL 全文がある）。
            //
            //      ★ **m_collisionHeight は見ない**（第 2 層レビュー I2）。あちらは
            //      CheckReferences が敷地のプロップと樹木の上端まで Mathf.Max で
            //      取り込むので、平屋が 20 m 以上を名乗る。読めるかを確かめる相手は、
            //      実際に使うフィールドでなければ意味が無い。
            //
            // ここが FAIL したとき LongPeriodDamage は**何もしない**（推測した高さで
            // 建物を壊さない）ので、影響の文にもそう書く。
            Check("BuildingAI.CollapseBuilding is resolvable and building height can be read",
                  "long-period damage cannot be applied; the feature disables itself rather than "
                  + "guessing a height",
                  delegate
                  {
                      if (typeof(BuildingAI).GetMethod("CollapseBuilding",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(ushort), typeof(Building).MakeByRefType(),
                                  typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                                  typeof(int)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      var size = typeof(BuildingInfo).GetField("m_size",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (size == null || size.FieldType != typeof(UnityEngine.Vector3))
                      {
                          return false;
                      }

                      // 予備経路（BuildingHeight.MetresOf）が使う出所そのもの。
                      var generated = typeof(BuildingInfo).GetField("m_generatedInfo",
                          BindingFlags.Public | BindingFlags.Instance);
                      return generated != null
                             && typeof(BuildingInfoGen).IsAssignableFrom(generated.FieldType);
                  });

            // --- ②地震（Task 10）ここまで ---

            // --- ④台風（Task 2: 骨格・プレハブ実測）ここから ---

            // ②の EarthquakeAI の項目と同じ性質の検証で、**実行時にしか値が取れない**。
            // m_radius / m_emergingDuration / m_activeDuration の実数値は DLL に無く
            // （プレハブのシリアライズ値、IL 事実文書 §A-0、PARTIAL）、④の
            // 暴風域半径も持続時間も**進行速度**も全部この 3 値の上に乗る
            // （TyphoonTrack.SpeedFor は m_activeDuration が 0 なら 0 を返し、
            //  呼び出し側は台風を 1 個も起こさない）。読めないなら読めないと
            // 名指しする以外に防波堤が無い。
            //
            // DLC 非所持環境ではこれが FAIL するのが正常。TornadoAI / TsunamiAI の
            // 項目が既に同じ性質を持っており、それが確立した扱いである。
            // 母数からは外さない——台風機能そのものが DLC 依存なので、
            // 「使えない」と名指しするのが正しい。
            Check("ThunderStormAI disaster prefab exposes m_radius / m_emergingDuration / "
                  + "m_activeDuration, and m_radius / m_activeDuration are non-zero",
                  "no typhoon can be started at all: its radius, its lifetime and its travel "
                  + "speed are all derived from these three numbers, and the mod refuses to "
                  + "guess them. This also FAILs when the Natural Disasters DLC is not owned, "
                  + "which is expected.",
                  delegate
                  {
                      var t = typeof(ThunderStormAI);
                      if (!HasField(t, "m_radius", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // 副作用の無い純粋な走査を使う。TyphoonReader の内部キャッシュは
                      // sim スレッドが回しており、main スレッドのここから巻き戻しては
                      // いけない（EarthquakeAI の項目と同じ理由）。
                      //
                      // ★★ **StormResolved ではなく Usable を見る**（全体レビュー C1）。
                      //    StormResolved は「プレハブという*オブジェクト*が見つかり、
                      //    3 つのフィールドを読み終えた」だけで立つ ——
                      //    **中身は 1 バイトも見ていない。** 台風を起こしてよいかを
                      //    実際に決めているのは TyphoonPrefabFacts.Usable
                      //    （＝ StormResolved && StormRadius > 0 && ActiveDuration > 0）で、
                      //    TyphoonController.Start はそちらで断っている。
                      //    ここが Resolved のままだと、m_radius か m_activeDuration が
                      //    0 でデシリアライズされた環境で**ボタンを押しても何も起きないのに、
                      //    ダンプも設定画面もこの検証だけ PASS と名乗る**。
                      //    「読めた」を「使える」の代わりに使わない。
                      return TyphoonReader.ScanPrefabFacts().Usable;
                  },
                  true);

            // ★ 設計書の記述をここでも訂正して固定する。**VortexAI に m_maxSpeed は
            //    存在しない。** §B-1 の IL_00AF が読んでいるのは VehicleAI.m_info、
            //    すなわち VehicleInfo.m_maxSpeed である。到達経路は
            //    TornadoAI.m_vortexInfo（VehicleInfo）.m_maxSpeed なので、
            //    m_vortexInfo の**型まで**照合する——ここが VehicleInfo でなくなったら、
            //    次の担当者は VortexAI 側に無いフィールドを探して推測で別のものを掴む。
            //
            //    影響は台風本体には及ばない（随伴竜巻＝ T10 だけが使えない）。
            //    そのことを impact に書いておかないと、正常に台風が動く環境の
            //    この FAIL が「台風が壊れている」と読まれる。
            Check("TornadoAI.m_vortexInfo resolves to a VortexAI with m_destructionRadiusMin / "
                  + "m_destructionRadiusMax and a VehicleInfo with m_maxSpeed, and "
                  + "m_maxSpeed / m_destructionRadiusMax are non-zero",
                  "the optional accompanying tornadoes cannot be sized or steered; the typhoon "
                  + "itself is unaffected. This also FAILs when the Natural Disasters DLC is "
                  + "not owned, which is expected.",
                  delegate
                  {
                      var vortexInfoField = typeof(TornadoAI).GetField("m_vortexInfo",
                          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                      if (vortexInfoField == null
                          || vortexInfoField.FieldType != typeof(VehicleInfo))
                      {
                          return false;
                      }

                      if (!HasField(typeof(VortexAI), "m_destructionRadiusMin", typeof(float))
                          || !HasField(typeof(VortexAI), "m_destructionRadiusMax", typeof(float))
                          || !HasField(typeof(VehicleInfo), "m_maxSpeed", typeof(float)))
                      {
                          return false;
                      }

                      // ★★ **VortexResolved だけでは足りない**（全体レビュー C1、嵐と同じ形）。
                      //    随伴竜巻を実際に止めているのは TyphoonTornado.Step の
                      //    `!prefab.VortexResolved || !(prefab.VortexMaxSpeed > 0f)` で、
                      //    軌道半径の見積りは m_destructionRadiusMax の上に乗る。
                      //    値が 0 でも VortexResolved は立つので、ここを Resolved の
                      //    ままにすると「竜巻が 1 個も出ないのに検証は PASS」になる。
                      var vortex = TyphoonReader.ScanPrefabFacts();
                      return vortex.VortexResolved
                             && vortex.VortexMaxSpeed > 0f
                             && vortex.DestructionRadiusMax > 0f;
                  },
                  true);

            // --- ④台風（Task 2）ここまで ---

            // --- ④台風（Task 3: 論理オブジェクトと経路追従）ここから ---

            // ④の移動機構そのもの。DisasterData.m_targetPosition を毎 sim tick 書き換えて
            // 災害を動かす（IL 事実文書 §E-1。本タスクで全アセンブリの
            // stfld DisasterData::m_targetPosition を走査し直し、既存の災害のそれを
            // 書くバニラのコードが 1 つも無いことを再確認した）。
            //
            // m_activationFrame は罠 1 の見張りに使う——SelfTrigger が効いていなければ
            // StartDisaster が即 return し、この値が 0 のままになる（§A-1 IL_003F）。
            // ここが読めなければ見張りごと成立しないので、同じ項目で照合する。
            //
            // メソッドは引数の型まで指定して見る（②の BuildingAI.CollapseBuilding の
            // 検査と同じ形）。名前だけの一致では、シグネチャが変わったときに
            // 偽 PASS を出す。
            Check("DisasterData exposes m_targetPosition / m_angle / m_intensity / "
                  + "m_activationFrame, and DisasterAI.StartNow / DeactivateNow / "
                  + "ClampDisasterTarget are resolvable",
                  "the typhoon cannot be created, moved or stopped; the feature does nothing "
                  + "at all",
                  delegate
                  {
                      var d = typeof(DisasterData);
                      if (!HasField(d, "m_targetPosition", typeof(UnityEngine.Vector3))
                          || !HasField(d, "m_angle", typeof(float))
                          || !HasField(d, "m_intensity", typeof(byte))
                          || !HasField(d, "m_activationFrame", typeof(uint)))
                      {
                          return false;
                      }

                      var byRef = new Type[]
                      {
                          typeof(ushort), typeof(DisasterData).MakeByRefType()
                      };
                      if (typeof(DisasterAI).GetMethod("StartNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }
                      if (typeof(DisasterAI).GetMethod("DeactivateNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }

                      return typeof(DisasterAI).GetMethod("ClampDisasterTarget",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector3).MakeByRefType() },
                          null) != null;
                  });

            // --- ④台風（Task 3）ここまで ---

            // --- ④台風（Task 4: 天候の駆動）ここから ---

            // ④が天候を握る 6 フィールド（§A-4）。どれが欠けても**例外は出ず**、
            // 台風が晴天の下を進むだけになる。
            //
            // m_forceWeatherOn だけは性質が違う。これが無いと、天候を切っている
            // プレイヤーの環境で m_targetRain / Cloud / Fog が毎ステップ 0 へ潰される
            // （IL_053D の枝）。「一部の環境でだけ静かに何も起きない」という、
            // いちばん報告されにくい壊れ方をするので、名指しで検証する。
            Check("WeatherManager exposes m_targetRain / m_targetCloud / m_targetFog / "
                  + "m_targetDirection / m_forceWeatherOn / m_enableWeather",
                  "the typhoon cannot drive the weather; it would move across the map under "
                  + "a clear sky",
                  delegate
                  {
                      var w = typeof(WeatherManager);
                      return HasField(w, "m_targetRain", typeof(float))
                             && HasField(w, "m_targetCloud", typeof(float))
                             && HasField(w, "m_targetFog", typeof(float))
                             && HasField(w, "m_targetDirection", typeof(float))
                             && HasField(w, "m_forceWeatherOn", typeof(float))
                             && HasField(w, "m_enableWeather", typeof(bool));
                  });

            // --- ④台風（Task 4）ここまで ---

            // --- ④台風（Task 6: 落雷）ここから ---

            // 落雷は**実体**で、BurnBuilding / BurnTree / CollapseSegment を起こす
            // （IL 事実文書 §A-3）。このメソッドが解決できなければ台風は雷を 1 発も
            // 運ばないが、**例外は出ず、嵐は動き天候も駆動され続ける** ——
            // 「雷の少ない台風」に見えるだけで、原因を指すものが他に無い。
            //
            // 引数の型まで指定して見る（1 引数版 QueueLightningStrike(uint) が別に
            // 存在するので、名前だけの一致では偽 PASS になる）。
            Check("WeatherManager.QueueLightningStrike(uint, Vector3, Quaternion, "
                  + "InstanceManager.Group) is resolvable",
                  "the typhoon carries no lightning; the storm still moves and drives the "
                  + "weather",
                  delegate
                  {
                      return typeof(WeatherManager).GetMethod("QueueLightningStrike",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(uint), typeof(UnityEngine.Vector3),
                              typeof(UnityEngine.Quaternion), typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④台風（Task 6）ここまで ---

            // --- ④台風（Task 7: 風害）ここから ---

            // 風害の 3 経路。**どれが欠けても例外は出ない** ——
            // 台風が通っても建物が 1 棟も倒れないだけになる。
            //
            // CollapseBuilding は②が既に検証している 6 引数版と同じ形で見る。
            // AddWind / DestroyTrees の並びは Task 7 Step 1 で IL 実測し、
            // §B-1 が DestroyStuff の転送から導いていた並びと一致することを確認した:
            //
            //   public static void AddWind(Vector3, float, Vector3, float, float,
            //                              InstanceManager.Group)
            //   public static void DestroyTrees(int, InstanceManager.Group, Vector3,
            //                                   float, float, float, float, float, float)
            //
            // ★ DestroyTrees だけが解決できない場合はこの検査を FAIL にしない。
            //   風害本体（倒壊と AddWind）は動くので、FAIL にすると狼少年になる。
            //   倒木を諦めた事実は TyphoonWind が FeatureHost.NoteDegraded で名乗る。
            //
            // ★★ **ただし「見ていないものを名前で名乗らない」**（全体レビュー）。
            //    この検査は以前 "AddWind / DestroyTrees are reachable" と名乗りながら
            //    DestroyTrees を 1 度も引いていなかった —— PASS が、一度も調べて
            //    いない相手について断定していたことになる。合否には入れないが
            //    **実際に引き、結果を名前に書く**。名前は Check に渡す前に組み立てる
            //    （AssumptionResult は Name しか表示しないので、ここが唯一の出口）。
            Check("BuildingAI.CollapseBuilding is resolvable and DisasterHelpers.AddWind is "
                  + "reachable (DisasterHelpers.DestroyTrees: "
                  + (DestroyTreesIsReachable()
                        ? "reachable"
                        : "MISSING - the typhoon fells no trees; the rest of the wind sweep runs")
                  + ")",
                  "wind damage cannot be applied. The typhoon still moves, drives the weather "
                  + "and drops lightning; the mod disables the wind sweep rather than reaching "
                  + "for DisasterHelpers.DestroyBuildings, which Natural Disasters Renewal "
                  + "replaces wholesale",
                  delegate
                  {
                      if (typeof(BuildingAI).GetMethod("CollapseBuilding",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(ushort), typeof(Building).MakeByRefType(),
                                  typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                                  typeof(int)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      // AddWind が無ければ演出だけでなく「風害の経路が丸ごと違う」
                      // 合図なので、こちらは FAIL に含める。
                      return typeof(DisasterHelpers).GetMethod("AddWind",
                          BindingFlags.Public | BindingFlags.Static, null,
                          new Type[]
                          {
                              typeof(UnityEngine.Vector3), typeof(float),
                              typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                              typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④台風（Task 7）ここまで ---

            // --- ④台風（Task 8: 河川氾濫）ここから ---

            // 氾濫の到達経路。**解決できなければ TyphoonFlood は 1 バイトも書かない。**
            //
            // Task 8 Step 1 で IL 実測して確定させたこと:
            //   TerrainManager.WaterSimulation  … public インスタンスプロパティ
            //                                     （裏は private m_waterSimulation）
            //   WaterSimulation.m_waterSources  … public FastList<WaterSource>
            //   LockWaterSource(ushort)         … public、m_buffer[source - 1] を返す
            //                                     （**1 基点**）。Monitor を取ったまま返る
            //   UnlockWaterSource(ushort, WaterSource) … public、書き戻して Monitor.Exit
            //   WaterSource                     … public struct、m_type / m_target は
            //                                     public UInt16、TYPE_NATURAL = 1
            //
            // ★ 代替経路へ逃げないことを impact に書く。CreateWaterWave は内陸で
            //   **何も起こさない**（津波の波はマップ外周リングでしか評価されない。§D-3(a)）。
            Check("WaterSimulation is reachable and exposes m_waterSources / LockWaterSource / "
                  + "UnlockWaterSource, and WaterSource exposes m_type / m_target",
                  "river flooding cannot run. The mod does nothing rather than reaching for "
                  + "CreateWaterWave, which does nothing at all inland (the tsunami wave is "
                  + "only evaluated on the map border ring)",
                  delegate
                  {
                      var wsProperty = typeof(TerrainManager).GetProperty("WaterSimulation",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (wsProperty == null
                          || wsProperty.PropertyType != typeof(WaterSimulation))
                      {
                          return false;
                      }

                      // FastList<WaterSource> であることまで見る（実測で確認済み）。
                      if (!HasField(typeof(WaterSimulation), "m_waterSources",
                                    typeof(FastList<WaterSource>)))
                      {
                          return false;
                      }

                      if (typeof(WaterSimulation).GetMethod("LockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort) }, null) == null)
                      {
                          return false;
                      }
                      if (typeof(WaterSimulation).GetMethod("UnlockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort), typeof(WaterSource) },
                              null) == null)
                      {
                          return false;
                      }

                      // ★ 型まで見る（全体レビュー）。ここのコメントは以前から
                      //    「public UInt16」と断定していたのに、検査は名前しか見て
                      //    いなかった。TyphoonFlood は ushort として読み書きする。
                      return HasField(typeof(WaterSource), "m_type", typeof(ushort))
                             && HasField(typeof(WaterSource), "m_target", typeof(ushort));
                  });

            // --- ④台風（Task 8）ここまで ---

            // --- ④台風（Task 10: 随伴竜巻）ここから ---

            // 随伴竜巻の**操舵**の経路。竜巻は「災害」ではなく VortexAI の車両が動くので
            // （§E-1）、m_targetPosition を書いても車両は追随しない。車両を見つけて
            // Vehicle.SetTargetPos の**両スロット**に書くのが唯一の手段である
            // （スロット 0 だけでは次のステップでスロット 1 に上書きされる。
            //  ③が IL 実測で確定させた事実）。
            //
            // ★ 解決できないときに「竜巻だけ出して操舵を諦める」ことはしない。
            //   操舵できない竜巻は台風と無関係に都市を横断するので、
            //   プレイヤーから見ると④が野良の竜巻を落としたのと区別が付かない。
            //   TyphoonTornado はそのとき 1 個も作らない。
            //
            // 引数の型まで指定して見る（②の CollapseBuilding の検査と同じ形）。
            // 名前だけの一致では、シグネチャが変わったときに偽 PASS を出す。
            Check("Vehicle.SetTargetPos(int, Vector4) and "
                  + "InstanceManager.GetAllGroupInstances(InstanceID, FastList<InstanceID>) "
                  + "are resolvable",
                  "the accompanying tornadoes cannot be steered around the typhoon. They "
                  + "would still spawn and drift on vanilla's own path, so the feature "
                  + "refuses to create them at all rather than dropping loose tornadoes on "
                  + "the city. Everything else about the typhoon is unaffected",
                  delegate
                  {
                      if (typeof(Vehicle).GetMethod("SetTargetPos",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(int), typeof(UnityEngine.Vector4) },
                              null) == null)
                      {
                          return false;
                      }

                      return typeof(InstanceManager).GetMethod("GetAllGroupInstances",
                          BindingFlags.Public | BindingFlags.Static, null,
                          new Type[] { typeof(InstanceID), typeof(FastList<InstanceID>) },
                          null) != null;
                  });

            // --- ④台風（Task 10）ここまで ---

            // --- ④台風（Task 9: 巨大な回転雲）ここから ---

            // ④の雲は**全部自前**である。バニラに流用できる雲は 1 つも無く
            // （DisasterInfo.m_effect はフィールドごと存在しない。§C-1）、バニラの雲は
            // ワールド座標を持たないスカイドームなので合成もできない（§C-2）。
            // したがって雲の生死は「自前のマテリアルが作れるか」だけに掛かっている。
            //
            // ★ ここが FAIL のとき、④は**何も描かない**。CS のマテリアルを借りる
            //   逃げ道は取らない —— CS のシェーダはエンジンが供給する per-instance
            //   データを要求するので、自前の DrawMesh に載せると不可視か真っ黒になる
            //   （VortexAI.RenderExtraStuff がその実例。§C-1 / 火災旋風 §4.9）。
            //   将来のゲーム更新でシェーダ名が変わったとき、黙って雲が消えるのではなく
            //   ここが名指しする。
            //
            // Shader.Find は main スレッド専用だが、Run() 自体が main スレッド専用なので
            // 問題ない。**DayNightDynamicCloudsProperties の実在はここに入れない** ——
            // 無いのは正当な環境（DLC・グラフィック設定）で、④の自前の雲には
            // 影響しないため（FAIL にすると狼少年になる）。
            // ★★ **どのシェーダで通ったかを名前に書く**（全体レビュー）。
            //    以前はこの検査が `|| Shader.Find("Standard") != null` で終わっていた。
            //    Standard は Unity 組み込みなので実質いつでも解決し、**この検査は
            //    原理的に FAIL しない**——「粒子系のアルファブレンドが 1 つも無い」
            //    という、この検査が名指しするはずだった事態が起きても PASS が出る。
            //    合否の式は TyphoonCloud.FindShader と同じままにし（そちらが実際に
            //    使う順序であり、Standard は MakeStandardTransparent で正しく透過に
            //    してから使う正当な最終手段である）、**勝ったシェーダ名を出す**ことで
            //    「Standard まで落ちている」を読めるようにする。
            string cloudShader = FirstResolvableCloudShader();
            Check("Graphics.DrawMesh(Mesh, Matrix4x4, Material, int, Camera, int, "
                  + "MaterialPropertyBlock, bool, bool) is reachable and a transparent shader "
                  + "resolves via Shader.Find (winner: "
                  + (cloudShader ?? "NONE - the cloud is not drawn") + ")",
                  "the typhoon's own cloud cannot be drawn. Every other part of the typhoon is "
                  + "unaffected; the mod draws nothing rather than borrowing a Cities material, "
                  + "which renders invisible or black in a hand-rolled DrawMesh",
                  delegate
                  {
                      // ★ 4 引数版ではなく**実際に呼んでいる 9 引数版**を見る。
                      //   4 引数版は castShadows: true / receiveShadows: true を転送するので、
                      //   ④は影を落とさない 9 引数版へ移した（TyphoonCloud の doc）。
                      //   検査する相手は、実際に呼ぶオーバーロードでなければ意味が無い。
                      if (typeof(UnityEngine.Graphics).GetMethod("DrawMesh",
                              BindingFlags.Public | BindingFlags.Static, null,
                              new Type[]
                              {
                                  typeof(UnityEngine.Mesh), typeof(UnityEngine.Matrix4x4),
                                  typeof(UnityEngine.Material), typeof(int),
                                  typeof(UnityEngine.Camera), typeof(int),
                                  typeof(UnityEngine.MaterialPropertyBlock),
                                  typeof(bool), typeof(bool)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      return cloudShader != null;
                  });

            // --- ④台風（Task 9）ここまで ---

            Report();
        }

        /// <summary>
        /// ④の雲が使えるシェーダの名前。1 つも無ければ null。
        /// **順序は <c>TyphoonCloud.FindShader</c> と同じでなければならない**
        /// （違う順序で調べると、検査が報告する名前と実際に使うシェーダがずれる）。
        /// <c>Shader.Find</c> は main スレッド専用だが、<see cref="Run"/> 自体が
        /// main スレッド専用なので問題ない。
        /// </summary>
        private static string FirstResolvableCloudShader()
        {
            string[] names =
            {
                "Particles/Alpha Blended",
                "Legacy Shaders/Particles/Alpha Blended",
                "Particles/Additive",
                "Standard",
            };

            for (int i = 0; i < names.Length; i++)
            {
                // UnityEngine.Object の == 多重定義で fake-null も弾く（?? は素通しする）。
                if (UnityEngine.Shader.Find(names[i]) != null) return names[i];
            }
            return null;
        }

        /// <summary>
        /// <c>DisasterHelpers.DestroyTrees</c> が引数の型まで込みで解決できるか。
        /// **合否には使わない**（倒木だけが使えない環境で風害まで FAIL にすると
        /// 狼少年になる）。検証の名前に事実を書くためだけに引く。
        /// 並びは Task 7 Step 1 で IL 実測したものと同じ。
        /// </summary>
        private static bool DestroyTreesIsReachable()
        {
            try
            {
                return typeof(DisasterHelpers).GetMethod("DestroyTrees",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new Type[]
                    {
                        typeof(int), typeof(InstanceManager.Group), typeof(UnityEngine.Vector3),
                        typeof(float), typeof(float), typeof(float), typeof(float),
                        typeof(float), typeof(float)
                    },
                    null) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasUpdateHazardMap(Type aiType)
        {
            return aiType.GetMethod("UpdateHazardMap",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(ushort), typeof(DisasterData).MakeByRefType(), typeof(byte[]) },
                null) != null;
        }

        /// <summary>
        /// 名前だけで見る版。**新しい検証では使わないこと**（全体レビュー）。
        ///
        /// 名前の一致は「同じ意味のフィールドがまだそこに在る」ことを保証しない。
        /// 型が <c>UInt16</c> から <c>UInt32</c> へ変わっても、<c>float</c> が
        /// <c>double</c> になっても、この関数は true を返し続ける ——
        /// そして本 MOD の読み書きは**コンパイル済みの型で**行われるので、
        /// 実際には型ロード時例外か、黙って別の値を読む結果になる。
        /// 型まで分かっているものは必ず <see cref="HasField(Type,string,Type)"/> を使う。
        ///
        /// 残してあるのは、型を名指しできない相手（<c>FastList&lt;T&gt;</c> の内部
        /// フィールドなど、ジェネリック実引数を跨いで照合したい場合）のためだけである。
        /// </summary>
        private static bool HasField(Type declaringType, string fieldName)
        {
            return declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
        }

        /// <summary>
        /// 型まで込みで見る版。**こちらが既定。**
        ///
        /// 期待する型は全て本タスクで実際のゲームアセンブリへリフレクションして確定させた
        /// （<c>WeatherManager</c> の天候値は全て <c>Single</c>、
        /// <c>DisasterData.m_intensity</c> は <c>Byte</c>、
        /// <c>m_activationFrame</c> / <c>m_startFrame</c> は <c>UInt32</c>、
        /// <c>WaterSource.m_type</c> / <c>m_target</c> は <c>UInt16</c>、
        /// <c>ThunderStormAI</c> / <c>EarthquakeAI</c> の duration は <c>UInt32</c>）。
        /// </summary>
        private static bool HasField(Type declaringType, string fieldName, Type fieldType)
        {
            var f = declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null && f.FieldType == fieldType;
        }

        /// <summary>
        /// static フィールド版。<see cref="HasField"/> は Instance しか見ないので、
        /// SimulationManager.DAYTIME_FRAME_TO_HOUR のような static readonly を
        /// そちらに渡すと常に false になる（＝偽 FAIL）。
        /// </summary>
        private static bool HasStaticField(Type declaringType, string fieldName, Type fieldType)
        {
            var f = declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f != null && f.FieldType == fieldType;
        }

        /// <summary>
        /// IntensityUnlock がスライダー到達の可否を確定させた時点（成功で _applied、
        /// あるいは MaxAttempts 尽きての _gaveUp）で呼ぶ。ロード直後の 1 回勝負にせず、
        /// 「わかった時点で」名指しの結果を残す・ログに出すのが、この検証項目の
        /// 誤報（false FAIL）を避ける唯一の方法。設定でこの機能自体を無効にした
        /// 場合（_gaveUp だが ModSettings.IntensityUnlock.value == false）は
        /// 前提が破れたわけではないので、ここではなく
        /// <see cref="ReportSliderNotApplicable"/> を呼ぶこと。
        /// </summary>
        public static void ReportSliderOutcome(bool reachable)
        {
            var result = new AssumptionResult(
                SliderCheckName, reachable, reachable ? "" : SliderCheckImpact);
            SetResult(result);
            LogResult(result);
        }

        /// <summary>
        /// 強度解放を設定で切っているため、スライダー検証がこの環境では対象外だと確定させる。
        ///
        /// PASS を publish してはいけない（通っていない前提を通ったと名乗ることになる）。
        /// 代わりに母数から外し、集計行にその旨を書く。
        /// </summary>
        public static void ReportSliderNotApplicable()
        {
            _sliderNotApplicable = true;
            Log.Info("  n/a   " + SliderCheckName + " (intensity unlock is off in settings)");
        }

        private static bool VortexStepIsPatched()
        {
            // Harmony が実際にこのメソッドを持っているかを見る。
            // [HarmonyPatch] の引数が実メソッドと 1 つでも食い違うと
            // パッチは無言で当たらず、MOD は正常に見えたまま竜巻だけが流れる。
            var target = typeof(VortexAI).GetMethod("SimulationStep",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(ushort), typeof(Vehicle).MakeByRefType(),
                    typeof(Vehicle.Frame).MakeByRefType(), typeof(ushort),
                    typeof(Vehicle).MakeByRefType(), typeof(int)
                },
                null);
            if (target == null) return false;

            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info == null || info.Postfixes == null) return false;

            // 「誰かの postfix が載っている」ではなく「自分の postfix が載っている」を見る。
            // 同じ private overload に別 MOD が偶然 postfix を当てていた場合、前者だと
            // 自分のパッチが無言で失敗していてもマスクされて PASS になってしまう。
            for (int i = 0; i < info.Postfixes.Count; i++)
            {
                if (info.Postfixes[i].owner == HarmonyBootstrap.HarmonyId) return true;
            }
            return false;
        }

        private static void Check(string name, string impact, Func<bool> predicate)
        {
            Check(name, impact, predicate, false);
        }

        /// <param name="expectedWithoutDlc">
        /// Natural Disasters DLC を持っていない環境では**この検証が FAIL するのが正常**か。
        ///
        /// true を渡した検証は、DLC 非所持の環境で <see cref="LogResult"/> が
        /// <c>Log.Warn</c> ではなく <c>Log.Info</c> で出し、設定画面の警告群からも
        /// 外れる（<see cref="UnexpectedFailures"/>）。**結果自体は FAIL のまま**で、
        /// 診断ダンプには従来どおり FAIL として出る —— 「DLC が無いから使えない」を
        /// PASS と言い換えることはしない。
        ///
        /// これが要るのは、DLC を持たない環境では 5 件が**毎回のレベルロードで**
        /// FAIL するからである。1 件につき Log.Warn が 2 行出るので、正常な
        /// バニラ環境のログが毎回 10 行の警告で埋まり、設定画面には
        /// 「一部の機能が使えません」の群が固定表示されて消えなくなる。
        /// 狼少年にしないための扱いで、他の FAIL は今までどおり Warn で目立たせる。
        /// </param>
        private static void Check(string name, string impact, Func<bool> predicate,
                                  bool expectedWithoutDlc)
        {
            if (expectedWithoutDlc)
            {
                lock (_gate)
                {
                    if (!_expectedWithoutDlc.Contains(name)) _expectedWithoutDlc.Add(name);
                }
            }

            bool passed;
            string detail = impact;
            try
            {
                passed = predicate();
            }
            catch (Exception e)
            {
                // 検証そのものが落ちても起動を壊さない。FAIL として扱う。
                passed = false;
                detail = impact + " (check threw " + e.GetType().Name + ")";
            }
            SetResult(new AssumptionResult(name, passed, passed ? "" : detail));
        }

        /// <summary>同名の既存結果があれば置き換える。Run() の 31 件と
        /// ReportSliderOutcome() の 1 件が非同期に混ざっても、Name をキーに
        /// 常に最新・単一の結果だけが残るようにする。
        ///
        /// 前の都市の結果はここで（この都市の最初の書き込み時に）まとめて捨てる。
        /// _stale の説明を参照。</summary>
        private static void SetResult(AssumptionResult result)
        {
            lock (_gate)
            {
                if (_stale)
                {
                    _results.Clear();
                    _stale = false;
                }

                for (int i = 0; i < _results.Count; i++)
                {
                    if (_results[i].Name == result.Name) { _results.RemoveAt(i); break; }
                }
                _results.Add(result);
            }
        }

        private static void LogResult(AssumptionResult a)
        {
            if (a.Passed)
            {
                Log.Info("  PASS  " + a.Name);
                return;
            }

            // ★ DLC 非所持環境で「正常な FAIL」が毎回 2 行の警告になるのを止める
            //   （全体レビュー）。行は必ず出す —— 黙らせるのではなく、
            //   重み付けだけを変える。
            if (IsExpectedFailure(a.Name))
            {
                Log.Info("  FAIL  " + a.Name
                         + "  (expected: the Natural Disasters DLC is not owned)");
                return;
            }

            Log.Warn("  FAIL  " + a.Name);
            Log.Warn("        -> " + a.Impact);
        }

        /// <summary>
        /// この FAIL が「この環境では正常」か。DLC 依存の検証で、かつ DLC を
        /// 持っていないときだけ true。
        /// </summary>
        private static bool IsExpectedFailure(string name)
        {
            if (ModCompat.NaturalDisastersOwned) return false;
            lock (_gate) { return _expectedWithoutDlc.Contains(name); }
        }

        /// <summary>
        /// 設定画面に「一部の機能が使えません」として出すべき FAIL だけを返す。
        /// **main スレッド専用**（<c>ModCompat.NaturalDisastersOwned</c> を読む）。
        ///
        /// DLC 非所持環境で正常に FAIL する 5 件を外すためだけに在る。外さないと、
        /// バニラのままの環境では警告の群が**永久に出続ける** —— そして本当の
        /// 前提破れが起きたとき、その 1 件は既に見慣れた群に紛れて読まれない。
        /// </summary>
        public static IList<AssumptionResult> UnexpectedFailures()
        {
            var all = LastResults;
            var failures = new List<AssumptionResult>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Passed) continue;
                if (IsExpectedFailure(all[i].Name)) continue;
                failures.Add(all[i]);
            }
            return failures;
        }

        private static void Report()
        {
            // ReportSliderOutcome が Run() より前（同じ OnLevelLoaded 内、
            // IntensityUnlock.Apply() の初回呼び出しが即座に確定した場合）に
            // 既に 1 件足していることがあるので、その時点のスナップショットをそのまま数える。
            var snapshot = LastResults;

            int passed = 0, failed = 0;
            bool sliderSettled = false;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Passed) passed++; else failed++;
                if (snapshot[i].Name == SliderCheckName) sliderSettled = true;
            }

            // 未確定の検証があるまま「4 passed, 0 FAILED」とだけ出すと、
            // 「全部通った」と読める。本基盤が消したいのは、まさにその
            // 「信じたが実は違う出力」なので、母数を必ず名乗る。
            //
            // 「対象外」は未確定ではない。母数から外して確定扱いにする。
            // 外さないと、NDR を検出した環境（強度解放が既定で OFF）では
            // 永久に「slider check pending」が出続けることになる。
            int total = _sliderNotApplicable ? TotalCheckCount - 1 : TotalCheckCount;
            string summary;
            if (sliderSettled || _sliderNotApplicable)
            {
                summary = "ASSUMPTIONS  " + passed + " passed, " + failed + " FAILED";
                if (_sliderNotApplicable)
                {
                    summary += "  (" + total + " checks; slider check n/a: intensity unlock is off)";
                }
            }
            else
            {
                summary = "ASSUMPTIONS  " + snapshot.Count + " of " + total
                          + " checks: " + passed + " passed, " + failed
                          + " FAILED  (slider check pending)";
            }
            Log.Info(summary);
            for (int i = 0; i < snapshot.Count; i++)
            {
                LogResult(snapshot[i]);
            }
        }
    }
}
