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
    /// ── ファイルの構成（全体レビュー I6 で分けた）─────────────────────
    ///
    /// かつては 1 ファイル 1159 行だった。付録A と突き合わせて監査するために
    /// 集約していたのだが、育ちすぎて**内訳を説明するコメントと、その実体が
    /// 900 行離れる**ようになり、片方だけ古くなる形になっていた。
    /// いまは <c>partial</c> で機能ごとに分けてある:
    ///
    /// <code>
    /// Assumptions.cs             土台（この file）— 集計・ログ・Check/SetResult・
    ///                            HasField 系・スライダー検証・機能に属さない前提 1 件
    /// Assumptions.FireWhirl.cs   ③火災旋風  3 件
    /// Assumptions.Forecast.cs    ①天気予報  7 件
    /// Assumptions.Earthquake.cs  ②地震     11 件
    /// Assumptions.Typhoon.cs     ④台風      9 件
    /// Assumptions.Volcano.cs     ⑤火山      3 件
    /// </code>
    ///
    /// **可視性は 1 つも変えていない。** <c>Check</c> も <c>_gate</c> も
    /// <c>_results</c> も private のままで、partial だから届く。
    /// <c>DisasterPlus.csproj</c> は <c>Game\**\*.cs</c> を glob しているので、
    /// 分割にあたって csproj は触っていない。
    ///
    /// **⑤以降の前提はこのファイルに足さない。** 機能ごとの partial を作り、
    /// その中で <c>…CheckCount</c> を宣言して <see cref="TotalCheckCount"/> の
    /// 和に足すこと。
    ///
    /// スレッド安全性: Run() / Reset() / ReportSliderOutcome() は main スレッドからのみ
    /// 呼ばれる想定だが、LastResults は DiagnosticsHub.CollectionEnabled が立つと
    /// FeatureHost.BuildReport() 経由で sim スレッドから毎 tick 読まれる
    /// （Task 5 以降）。書き込み中の List を読む競合を避けるため、_results への
    /// 全アクセスを _gate 1 本で直列化する。FeatureHost._errorGate や
    /// DiagnosticsHub の gate とはロック順序の関係を作らないよう、
    /// このロックを持ったまま他クラスのコードは一切呼ばない。
    /// </summary>
    public static partial class Assumptions
    {
        private const string SliderCheckName = "Disasters panel intensity slider is reachable";
        private const string SliderCheckImpact = "disaster intensity cannot be unlocked to 25.5";

        /// <summary>
        /// 1 回のレベルロードで最終的に埋まる検証件数。Report() が「何件中の集計か」を
        /// 名乗るために使う。
        ///
        /// ★ **内訳はもう here に書かない**（全体レビュー I6）。以前ここには
        ///   34 行の内訳表があったが、それが説明している検証の実体は最大で 900 行
        ///   離れた場所にあり、検証を足したときに片方だけが更新される形だった
        ///   （実際に「31 件」と書いた doc と 32 の定数が同居していた）。
        ///   いまは**機能ごとの partial が自分の件数を宣言し**、ここはその和を取るだけ ——
        ///   検証を足す人は、足したファイルの中の定数だけを見ればよい。
        /// </summary>
        private const int TotalCheckCount = GeneralCheckCount + FireWhirlCheckCount
                                            + ForecastCheckCount + EarthquakeCheckCount
                                            + TyphoonCheckCount + VolcanoCheckCount
                                            + SliderCheckCount;

        /// <summary>このファイルが持つ検証の数（機能に属さない土台の前提）。</summary>
        private const int GeneralCheckCount = 1;

        /// <summary>スライダー到達性の 1 件（<see cref="ReportSliderOutcome"/>）。</summary>
        private const int SliderCheckCount = 1;

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
        /// ここでは確定的に判定できるものだけを見る（<see cref="TotalCheckCount"/> から
        /// <see cref="SliderCheckCount"/> を引いた件数）。**ここに実数を書かないこと** ——
        /// 書くと機能を足すたびに片方だけが古くなる（この doc が 1 度そうなっている）。
        /// 強度スライダーの到達可否は
        /// この時点ではまだ「未構築なだけ」の可能性が拭えない（IntensityUnlock 自身が
        /// 100 回・120 フレーム間隔のリトライを持つほど）ので、ここで即座に判定して
        /// FAIL を出すと、実際には後で正常に到達できるケースまで誤報になる。
        /// その 1 件は ReportSliderOutcome() が IntensityUnlock の確定後に個別に埋める。
        /// </summary>
        public static void Run()
        {
            if (_ran) return;
            _ran = true;

            // ★ 検証の本体は機能ごとの partial にある（Assumptions.<機能>.cs）。
            //   ここは順序と、集計・ログの土台だけを持つ。**新しい機能の検証を
            //   このファイルに足さないこと** —— 1159 行まで育って、内訳の
            //   コメントとその実体が 900 行離れる形になったのが分割の理由である。
            RunGeneral();
            RunFireWhirl();
            RunForecast();
            RunEarthquake();
            RunTyphoon();
            RunVolcano();

            Report();
        }

        /// <summary>機能に属さない土台の前提（<see cref="GeneralCheckCount"/> 件）。</summary>
        private static void RunGeneral()
        {
            Check("SimulationManager.DAYTIME_FRAMES == 65536",
                  "all in-game durations will be wrong",
                  delegate { return SimulationManager.DAYTIME_FRAMES == 65536; });
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

        /// <summary>同名の既存結果があれば置き換える。Run() の各件と
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
