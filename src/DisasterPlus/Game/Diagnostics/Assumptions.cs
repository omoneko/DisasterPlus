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
        /// 1 回のレベルロードで最終的に埋まる検証件数。Run() の 16 件 ＋
        /// ReportSliderOutcome() の 1 件。Report() が「何件中の集計か」を
        /// 名乗るために使う。Run() に検証を足したらここも増やすこと。
        ///
        /// 内訳: 火災旋風 4 件（既存）＋ 天気予報 7 件
        /// （SetCurrentMode 解決可否・SubInfoMode 2 種の存在・
        /// ThunderStormAI/TornadoAI.UpdateHazardMap の存在・
        /// WeatherManager の current/target フィールド群・
        /// DisasterManager.m_hazardAmount が private Byte[] のままか・
        /// m_disasters の m_buffer/m_size・ハザードグリッドの形状）＋
        /// 地震 5 件（EarthquakeAI プレハブの 4 調整値・DisasterData の
        /// m_intensity/m_activationFrame/m_startFrame/m_angle・
        /// EarthquakeCoverage と CheckLocalResource・sim スレッドの時計・
        /// SubInfoMode.EarthquakeHazard と EarthquakeAI.UpdateHazardMap）＋
        /// スライダー検証 1 件。
        ///
        /// スライダー検証が「対象外」に確定した場合はこの母数から 1 件引く
        /// （<see cref="_sliderNotApplicable"/>）。
        /// </summary>
        private const int TotalCheckCount = 17;

        private static readonly object _gate = new object();
        private static readonly List<AssumptionResult> _results = new List<AssumptionResult>();
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
        /// ここでは確定的に判定できる 16 件だけを見る。強度スライダーの到達可否は
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

            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); });

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
                      var t = typeof(WeatherManager);
                      return HasField(t, "m_currentRain") && HasField(t, "m_targetRain")
                          && HasField(t, "m_currentCloud") && HasField(t, "m_targetCloud")
                          && HasField(t, "m_currentFog") && HasField(t, "m_targetFog")
                          && HasField(t, "m_currentTemperature") && HasField(t, "m_targetTemperature")
                          && HasField(t, "m_windDirection");
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
                      return HasField(f.FieldType, "m_buffer") && HasField(f.FieldType, "m_size");
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
            Check("EarthquakeAI disaster prefab exposes its four tuning fields",
                  "no earthquake durations or fault geometry can be read; every duration in this "
                  + "feature is designed on top of these four numbers. This also FAILs when the "
                  + "Natural Disasters DLC is not owned, which is expected.",
                  delegate
                  {
                      var t = typeof(EarthquakeAI);
                      if (!HasField(t, "m_crackLength") || !HasField(t, "m_crackWidth")
                          || !HasField(t, "m_emergingDuration") || !HasField(t, "m_activeDuration"))
                      {
                          return false;
                      }
                      // 副作用の無い純粋な走査を使う。EarthquakeReader の内部キャッシュは
                      // sim スレッドが回しており、main スレッドのここから巻き戻してはいけない
                      // （FireWhirlSpawner.HasTornadoPrefab と同じ理由）。
                      return EarthquakeReader.ScanPrefabFacts().Resolved;
                  });

            Check("DisasterData exposes m_intensity / m_activationFrame / m_startFrame / m_angle",
                  "neither the shaking strength nor the per-building margin can be shown",
                  delegate
                  {
                      var t = typeof(DisasterData);
                      return HasField(t, "m_intensity") && HasField(t, "m_activationFrame")
                          && HasField(t, "m_startFrame") && HasField(t, "m_angle");
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
                      return HasField(t, "m_dayTimeFrame") && HasField(t, "m_enableDayNight")
                          && HasStaticField(t, "DAYTIME_FRAME_TO_HOUR");
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

            Report();
        }

        private static bool HasUpdateHazardMap(Type aiType)
        {
            return aiType.GetMethod("UpdateHazardMap",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(ushort), typeof(DisasterData).MakeByRefType(), typeof(byte[]) },
                null) != null;
        }

        private static bool HasField(Type declaringType, string fieldName)
        {
            return declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
        }

        /// <summary>
        /// static フィールド版。<see cref="HasField"/> は Instance しか見ないので、
        /// SimulationManager.DAYTIME_FRAME_TO_HOUR のような static readonly を
        /// そちらに渡すと常に false になる（＝偽 FAIL）。
        /// </summary>
        private static bool HasStaticField(Type declaringType, string fieldName)
        {
            return declaringType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null;
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

        /// <summary>同名の既存結果があれば置き換える。Run() の 16 件と
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
            }
            else
            {
                Log.Warn("  FAIL  " + a.Name);
                Log.Warn("        -> " + a.Impact);
            }
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
