using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地震計の節と波形グラフの節。**main スレッド専用。**
    ///
    /// <see cref="EarthquakePanel"/> から切り出したのは、あのファイルがプロジェクト規約の
    /// 800 行を大きく超えていたためで、**内容は 1 文字も変えていない**。
    /// 行の生成も <c>.text</c> の代入もこのファイルには無く、
    /// <see cref="EarthquakeRows"/> を通してしか行えない（あちらのクラス doc の担保）。
    ///
    /// 2 つの節を 1 つの型にまとめてあるのは、片方がもう片方の説明になっているため。
    /// 「地震計を建てると何が変わるか」の直後に、建てた地震計が実際に何を見ているかが来る。
    /// </summary>
    internal static class EarthquakeSensorRows
    {
        private static UILabel _sensorEpicentreLabel;
        private static UILabel _sensorLeadLabel;
        private static UILabel _sensorCursorLabel;
        private static UILabel _waveformLabel;
        private static UILabel _waveformModelLabel;
        private static UILabel _waveformTremorLabel;
        private static UILabel _waveformUnavailableLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            // ── 地震計（Task 7）──────────────────────────────────
            // ここも第 1 層である。リードタイムの式も上限 100 もバニラのリテラルで
            // （§A-2）、この MOD は 1 つも係数を足していない。
            EarthquakeRows.AddSectionHeader(p, "SensorSection", ref y,
                Strings.EarthquakeSensorSection);
            _sensorEpicentreLabel = EarthquakeRows.AddLayer1Row(p, "SensorEpicentre", ref y);
            _sensorLeadLabel = EarthquakeRows.AddLayer1Row(p, "SensorLead", ref y);
            _sensorCursorLabel = EarthquakeRows.AddLayer1Row(p, "SensorCursor", ref y);

            // ★ この 1 行は**建てたら何が変わるか**の説明であって観測値ではないので、
            //    地震が起きていなくても、スナップショットが読めていなくても出す。
            //    したがってここで一度書いたら以後どこからも書き換えない
            //    （Refresh 側に「消す」経路を作らないことがその保証になっている）。
            EarthquakeRows.AddPlainRow(p, "SensorEffect", ref y,
                Strings.EarthquakeSensorEffect, 42f);

            // ── 波形（Task 8）───────────────────────────────────
            // ここも第 1 層である。プロットしているのは**バニラ自身の揺れの式**
            // （§A-7、カメラを動かしているのと同じ式・同じ定数・同じ窓）を、
            // カメラの代わりに地震計の位置で評価した値で、この MOD は物理を
            // 1 つも足していない。**ただしゲームの地震計が測った値ではない** ——
            // EarthquakeSensorAI は時系列データを一切持たない（§C-1）。
            // その区別は EarthquakeWaveformNote が毎回グラフの真下で名乗る。
            //
            // 地震計の節の直下に置いている。「地震計を建てると何が変わるか」の
            // 説明のすぐ次に、建てた地震計が実際に何を見ているかが来る。
            // 3 行ぶんの高さを取る。この行は最も長いとき
            // Strings.EarthquakeWaveformNeedsSensor（約 150 文字）を丸ごと入れる。
            _waveformLabel = EarthquakeRows.AddLayer1Row(p, "Waveform", ref y, 42f);

            WaveformView.Build(p, "WaveformPlot", 12f, y);
            if (WaveformView.Available) y += WaveformView.PlotHeight + 6f;

            // ★★ **第 2 層の行がこの節に 1 本だけ入る**（合成記象、既定 OFF）。
            //    グラフから離れた第 2 層の節へ追い出すと、目の前の橙の線が
            //    何なのか、その場では分からなくなる。離さない代わりに
            //    <c>AddLayer2Row</c> を使い、接頭辞（[Disaster + model]）と色の
            //    両方でこの 1 行だけが層の違う行であることを名乗らせる。
            //    **接頭辞は EarthquakeRows しか付けられない**ので、この行を
            //    うっかり実測として出すことはできない（あちらのクラス doc の担保）。
            _waveformModelLabel = EarthquakeRows.AddLayer2Row(p, "WaveformModel", ref y, 40f);

            // ★★ **第 3 層＝火山性微動の行**（2026-08-22、所有者の依頼
            //    「火山性地震は震度計に記録されていないのも修正して」）。
            //    第 2 層とまったく同じ扱いにする —— これも<b>この MOD のモデル</b>で
            //    あって、ゲームが計算している値ではない
            //    （<c>Game/Volcano/VolcanoTremorTrace</c> のクラス doc）。
            //    <c>AddLayer2Row</c> を使うのはそのためで、接頭辞と色が
            //    「実測ではない」を毎回名乗る。
            _waveformTremorLabel = EarthquakeRows.AddLayer2Row(p, "WaveformTremor", ref y, 40f);

            // ★ 黙って空欄にしない。最大振幅の行（_waveformLabel）は出したうえで、
            //    グラフが出ない理由を名乗る。劣化であって嘘ではない。
            //
            //    **この行は構築の成否に関わらず作る**（全体レビュー I6）。以前は
            //    構築時に失敗したときしか作っておらず、**実行時**に描画が落ちて
            //    WaveformView.Destroy() がスプライトを隠したときには、
            //    理由を書く場所が存在しなかった —— プレイヤーには何の説明も無い
            //    空白だけが残り、それは WaveformView のクラス doc が
            //    「黙って空欄にならず…劣化であって嘘ではない」と約束している
            //    ことの正反対である。中身は Refresh 側が状態を見て入れる。
            _waveformUnavailableLabel = EarthquakeRows.AddPlainRow(p, "WaveformUnavailable",
                ref y, "", 28f);

            // グラフが何の絵なのかを、グラフのすぐ下で毎回言う。
            // 内容はグラフを出しているときだけ入れる（Refresh 側で設定する）。
        }

        /// <summary>レベルアンロード時。参照を捨てるだけ（実体はパネルごと消える）。</summary>
        internal static void Destroy()
        {
            _sensorEpicentreLabel = null;
            _sensorLeadLabel = null;
            _sensorCursorLabel = null;
            _waveformLabel = null;
            _waveformModelLabel = null;
            _waveformTremorLabel = null;
            _waveformUnavailableLabel = null;
        }

        /// <summary>
        /// **地震計を建てると何が変わるか。** ゲーム内のどこにも書かれていない 2 つの効果を
        /// 名指しする（§A-2 / §C-2）:
        ///
        ///   1. 警報リードタイムが 1755 → 最大 8192 フレーム（38.6 分 → ちょうど 3.0 時間）
        ///   2. <c>located</c> が立ち、**そもそも地震がハザードマップに描かれるようになる**
        ///
        /// **因果の向きを間違えないこと。** 地震計は <c>DetectDisaster</c> を呼ばない。
        /// 呼ぶのは <c>EarthquakeAI.SimulationStep</c> で、判断材料は
        /// **震央 1 点のカバレッジ**である。したがってここでリードタイムを出してよいのは
        /// <see cref="EarthquakeReading.CoverageAtEpicentre"/> からだけで、
        /// カーソル地点の値から出してはいけない（効果範囲が震央に届いていない地震計は、
        /// その地震について何も寄与しない）。
        ///
        /// **カバレッジ 0 と「読めなかった」を同じ顔にしない。** 0 は
        /// 「震央に届いている地震計が 1 つも無い」という本機能の看板の実測値であり、
        /// そのときは数値も出す。読めなかったときだけ数値を伏せる
        /// （<see cref="EarthquakeReading.CoverageKnown"/> /
        /// <see cref="EarthquakeSnapshot.CursorCoverageValid"/>）。
        /// </summary>
        internal static void RefreshSensor(EarthquakeSnapshot snapshot,
                                           EarthquakeReading primary, bool haveCursor)
        {
            RefreshEpicentreCoverageRows(primary);
            RefreshCursorCoverageRow(snapshot, haveCursor);
        }

        /// <summary>
        /// 地震計の行の値だけを消す。**説明文（<c>SensorEffect</c>）は消さない** ——
        /// あれは「建てたら何が変わるか」であって観測値ではないので、
        /// 何も読めていないときこそ読む価値がある。
        /// </summary>
        internal static void ClearSensor()
        {
            EarthquakeRows.SetPlain(_sensorEpicentreLabel, "");
            EarthquakeRows.SetPlain(_sensorLeadLabel, "");
            EarthquakeRows.SetPlain(_sensorCursorLabel, "");
        }

        private static void RefreshEpicentreCoverageRows(EarthquakeReading primary)
        {
            // 地震が無ければ震央も無い。ここで 0 を出すと「地震計が無い」に見える。
            if (primary == null)
            {
                EarthquakeRows.SetPlain(_sensorEpicentreLabel, "");
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            if (!primary.CoverageKnown)
            {
                // ★ 捏造ゼロを作らない。読めなかったことを言い、リードタイムは伏せる
                //    （カバレッジ不明のまま 38.6 分と出すと、それは 0 の断定になる）。
                EarthquakeRows.SetPlain(_sensorEpicentreLabel,
                    Strings.EarthquakeCoverageAtEpicentre + ": " + Strings.EarthquakeUnavailable);
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            int raw = primary.CoverageAtEpicentre;
            int used = WarningLeadTime.ClampCoverage(raw);

            string text = Strings.EarthquakeCoverageAtEpicentre + ": " + raw;
            // バニラが Min(cov, 100) で頭打ちにしている事実を、実際に頭打ちに
            // なっているときだけ見せる（§A-2）。
            if (raw != used) text += " -> " + used;
            if (used == 0) text += "   (" + Strings.EarthquakeNoSensor + ")";
            EarthquakeRows.SetLayer1(_sensorEpicentreLabel, text);

            // 換算は必ず FeatureHost.FramesPerMinute から出す（定数を直書きして
            // 4 倍ずれた前科がある）。換算できないときは 0 が返るので行ごと伏せる。
            float minutes = WarningLeadTime.MinutesFor(raw, FeatureHost.FramesPerMinute);
            if (minutes <= 0f)
            {
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            // これは「あと何分で警報が出る」ではなく、**本震の何分前に警報が出るか**
            // という長さである。ポーズしていても縮まない（ゲーム内分の尺度）。
            EarthquakeRows.SetLayer1(_sensorLeadLabel, Strings.EarthquakeWarningLead + ": "
                + minutes.ToString("F1") + " " + Strings.EarthquakeMinutes);
        }

        private static void RefreshCursorCoverageRow(EarthquakeSnapshot snapshot, bool haveCursor)
        {
            // 「カーソルが地形の上に無い」と「読めなかった」を言い分ける。
            // 前者はパネルを読んでいる間ほぼ常に起きる（マウスがパネルの上にある）。
            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_sensorCursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            if (!snapshot.CursorCoverageValid)
            {
                EarthquakeRows.SetPlain(_sensorCursorLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // 上限 100 の注記は付けない。ここは「この場所に地震計が届いているか」を
            // 見る行であって、リードタイムの式に入る値ではない。
            EarthquakeRows.SetLayer1(_sensorCursorLabel,
                Strings.EarthquakeCoverageAtCursor + ": " + snapshot.CursorCoverage);
        }

        /// <summary>
        /// 波形の行を全部消し、プロットを隠す。**注記も消す** —— あれは
        /// 「今出ているこのグラフが何なのか」の説明であって、グラフが無いときに
        /// 残しておくと、存在しない絵の出所を説明していることになる。
        /// </summary>
        internal static void ClearWaveform()
        {
            EarthquakeRows.SetPlain(_waveformLabel, "");
            EarthquakeRows.SetPlain(_waveformModelLabel, "");
            EarthquakeRows.SetPlain(_waveformTremorLabel, "");
            RefreshWaveformAvailability();
            WaveformView.Render(null);
        }

        /// <summary>
        /// 「グラフが出ない理由」の 1 行（全体レビュー I6）。
        ///
        /// **毎回状態を見に行く。** 描画は実行時にも落ちうる（<see cref="WaveformView.Render"/>
        /// の catch が <c>Destroy()</c> を呼んで以後描かない）。構築時にしか判定して
        /// いなかった頃は、そこから先が**説明の無い空白**になっていた。
        ///
        /// 「まだ作っていない」には何も書かない —— パネルが開いていればここは
        /// 構築済みなので通らないが、状態を 4 つに分けている以上、
        /// 未構築を「使えません」と言い換えないことを構造で示しておく。
        /// </summary>
        private static void RefreshWaveformAvailability()
        {
            switch (WaveformView.State)
            {
                case WaveformViewState.BuildFailed:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel,
                        Strings.EarthquakeWaveformUnavailable);
                    break;
                case WaveformViewState.RenderFailed:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel,
                        Strings.EarthquakeWaveformDrawFailed);
                    break;
                default:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel, "");
                    break;
            }
        }

        /// <summary>
        /// **依頼の「地震計があるのに波形グラフが見られない」への回答そのもの。**
        ///
        /// ここに出る線は<b>ゲーム内の地震計が計測した値ではない</b>。
        /// <c>EarthquakeSensorAI</c> は時系列データを一切持たない（§C-1、ABSENT）——
        /// フィールドは <c>m_detectionRange</c> だけで、毎 tick 免疫的リソースを
        /// 撒くだけの装置である。**プロットしているのはバニラ自身の揺れの式**
        /// （§A-7、<c>EarthquakeAI.RenderInstance</c> がカメラを動かすのに使っている
        /// のと同じ式・定数・窓）を、カメラの代わりに地震計の位置で評価した値である。
        /// これは同じ式の別評価であって近似ではない。
        ///
        /// **その区別は必ずグラフの真下に書く**（<c>EarthquakeWaveformNote</c>、
        /// 設計書 §3.5 が設計書と UI の両方に書けと要求している）。書かないと、
        /// この機能は「ゲームが計測しているように見えるが実際は誰も測っていない絵」になる。
        ///
        /// **3 つの「空」を言い分ける**（①から続く、捏造ゼロを作らない規律）:
        ///   - 記録対象の地震が無い … 行ごと出さない
        ///   - 地震はあるが地震計が 0 個 … 「地震計を建ててください」
        ///   - 地震計はあるがサンプル 0 件 … 本震前で揺れの窓がまだ開いていない
        /// 最後の 1 つを平らな線で描くと「揺れていない」に化ける。
        /// </summary>
        internal static void RefreshWaveform(EarthquakeSnapshot snapshot)
        {
            // 進行中（Emerging|Active）の地震が 1 つも無く、**火山も揺れていない**。
            // 揺れの式自体が動かない区間なので、波形について言えることは何も無い。
            //
            // ★★ <b>「地震が無い」だけで畳まないこと</b>（2026-08-22）。
            //    以前はここが <c>WaveformQuakeId == 0</c> だけを見ていたので、
            //    **火山だけが揺れているあいだグラフごと消えていた** ——
            //    「火山性地震が震度計に記録されない」の、表示側の半分である。
            //    記録の側が生きていれば <c>Traces</c> に観測点が入っている。
            if (snapshot.WaveformQuakeId == 0 && snapshot.Traces.Count == 0)
            {
                ClearWaveform();
                return;
            }

            RefreshWaveformAvailability();

            var traces = snapshot.Traces;
            if (traces.Count == 0)
            {
                // ★ カメラ位置や市の中心で代用しない。「地震計があるのに波形が
                //    見られない」への回答なので、地震計に紐づかない波形は意味が違う。
                EarthquakeRows.SetPlain(_waveformLabel, Strings.EarthquakeWaveformNeedsSensor);
                EarthquakeRows.SetPlain(_waveformModelLabel, "");
                EarthquakeRows.SetPlain(_waveformTremorLabel, "");
                WaveformView.Render(null);
                return;
            }

            // 震央に最も近い 1 個だけを描く（SeismographRecorder が近い順に並べている）。
            // グラフを 4 枚並べても読めないので、残りは件数として添えるだけ。
            var trace = traces[0];

            // ★ どの地震の波形なのかを必ず名乗る（全体レビュー I1）。
            //    EarthquakeSnapshot.CursorQuakeId の doc が「表示側はこれを必ず出す」と
            //    要求しているのと同じ理由で、波形の地震にも同じ規律が要る。
            //    地震が 2 個同時に進んでいると、上の 6 行が指す地震
            //    （SelectPrimary）と、この絵の地震（QuakeSelection.SelectDamaging）は
            //    一致しないことがある。
            // ★★ **出所を取り違えない。** バニラの地震が無いのに震央からの距離と
            //    地震の番号を出すと、**起きていない地震の記録**という顔になる
            //    （<c>SeismographTrace.HasQuake</c> がその区別を持っている）。
            //    火山だけが揺れているときは、⑤の中心からの距離を、そう名乗って出す。
            string header = Strings.EarthquakeWaveform + ": #" + trace.BuildingId;
            if (trace.HasQuake)
            {
                header += "   " + trace.DistanceToEpicentre.ToString("F0") + " m"
                          + "   (#" + snapshot.WaveformQuakeId + ")";
            }
            else
            {
                header += "   " + Strings.EarthquakeWaveformTremorSource
                          + " " + trace.DistanceToVolcano.ToString("F0") + " m";
            }
            if (traces.Count > 1)
            {
                header += "   (" + Strings.EarthquakeSensorSection + ": " + traces.Count + ")";
            }

            // ★★ 火山だけが揺らしているときは、ここに**第 1 層の最大振幅 0.00 を
            //    出さない** —— 0 が「揺れていない地震が起きている」に化ける。
            //    揺れの大きさは下の第 3 層の行が名乗る。
            if (trace.Count > 0 && trace.HasQuake)
            {
                // 縦軸は最大振幅で正規化して描くので、その最大振幅を数値でも名乗る。
                // これが無いと、グラフの高さだけを見て地震の強さを比べてしまう。
                //
                // ★ バーの満目盛りは s（0-1）ではなく変位の理論最大 0.60 である
                //    （全体レビュー I4）。s の目盛りを流用していた頃は、最大でも
                //    0.6 にしかならない量を 0-1 の尺度で描いていたため、常に
                //    1〜2 マスしか埋まらず、しかもすぐ上の s のバーと見分けが
                //    付かなかった。満目盛りを数値でも併記する。
                float peak = trace.PeakAbsolute;
                header += "\n" + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]";
            }
            else if (trace.Count <= 0)
            {
                // 観測点はある。まだ揺れの窓（§A-7 の e > 0）が開いていないだけ。
                // 「サンプルが無い」と「サンプルが全部 0」は別のことなので、
                // ここで 0.00 と出してはいけない。
                header += "   " + WaitingReason(snapshot);
            }

            EarthquakeRows.SetLayer1(_waveformLabel, header);
            RefreshWaveformModelRow(snapshot, trace);
            RefreshWaveformTremorRow(trace);

            // 注記はグラフ（あるいは最大振幅の行）が出ているときだけ添える。
            WaveformView.Render(trace);
        }

        /// <summary>
        /// **合成記象の 1 行**（第 2 層）。記録が無ければ空にする。
        ///
        /// ★ <b>この行を <c>SetLayer1</c> で書かないこと。</b> ここに出る値は
        ///   バニラが計算しているものではなく、<c>SeismogramModel</c> が
        ///   その地震の種から作った波形である。すぐ上の行（同じグラフの
        ///   もう 1 本の線）が <c>[measured]</c> を名乗っているぶん、
        ///   取り違えたときの嘘が大きい。
        ///
        /// 出すのは 3 つ:
        ///   - 最大振幅（満目盛りはバニラの線と共通の <c>MaxDisplacement</c>）
        ///   - 初期微動継続時間 S-P（**震源距離とともに開く**、このモデルの看板）
        ///   - どちらの色がどちらの層かの凡例
        /// </summary>
        private static void RefreshWaveformModelRow(EarthquakeSnapshot snapshot,
                                                    SeismographTrace trace)
        {
            if (!trace.HasModel)
            {
                EarthquakeRows.SetPlain(_waveformModelLabel, "");
                return;
            }

            float peak = trace.ModelPeakAbsolute;
            string text = Strings.EarthquakeWaveformModel + ": "
                          + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]";

            // ★ 窓（m_activeDuration）が読めていないときは S-P を出さない。
            //   モデルの到達時刻は窓の長さから決まるので、窓が無ければ数字も無い。
            if (snapshot.Prefab.Resolved && snapshot.Prefab.ActiveDuration != 0u)
            {
                var model = SeismogramModel.For(
                    DeterministicRandom.Hash(snapshot.WaveformQuakeId,
                                             ActivationFrameOf(snapshot)),
                    snapshot.Prefab.ActiveDuration);

                if (model.Valid)
                {
                    text += "   " + Strings.EarthquakeWaveformSMinusP + " "
                            + model.SMinusPFrames(trace.DistanceToEpicentre).ToString("F0")
                            + " " + Strings.EarthquakeFrames;
                }
            }

            EarthquakeRows.SetLayer2(_waveformModelLabel, text);
        }

        /// <summary>
        /// **第 3 層＝火山性微動の行**（2026-08-22、所有者の依頼）。
        ///
        /// ★★ ここに出る線は<b>ゲームが計算している値ではないし、カメラが実際に
        /// 足した変位でもない</b>。<c>Core/Volcano/VolcanicTremor</c> という
        /// この MOD のモデルを、地震計の位置と**記象の時間軸（sim フレーム）**で
        /// 評価したものである（カメラは実時間で評価する。
        /// <c>Game/Volcano/VolcanoTremorTrace</c> のクラス doc に両者の違いがある）。
        /// 第 2 層とまったく同じ扱いにしてあるのはそのためで、
        /// <c>SetLayer2</c> の接頭辞と色が毎回それを名乗る。
        /// </summary>
        private static void RefreshWaveformTremorRow(SeismographTrace trace)
        {
            if (!trace.HasTremor)
            {
                EarthquakeRows.SetPlain(_waveformTremorLabel, "");
                return;
            }

            float peak = trace.TremorPeakAbsolute;
            string text = Strings.EarthquakeWaveformTremor + ": "
                          + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]"
                          + "   " + trace.DistanceToVolcano.ToString("F0") + " m";

            EarthquakeRows.SetLayer2(_waveformTremorLabel, text);
        }

        /// <summary>
        /// 波形を記録している地震の発動フレーム。**種を作るのに要る**
        /// （<c>SeismographRecorder.SeismogramSeed</c> と同じ組み合わせでなければ、
        /// 表示している S-P が描いてある線のものと食い違う）。
        /// 見つからなければ 0 —— そのとき <c>SeismogramModel.For</c> は
        /// 別の形を返すが、S-P の桁は距離で決まるので表示は壊れない。
        /// </summary>
        private static uint ActivationFrameOf(EarthquakeSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Quakes.Count; i++)
            {
                if (snapshot.Quakes[i].DisasterId != snapshot.WaveformQuakeId) continue;
                return snapshot.Quakes[i].ActivationFrame;
            }
            return 0u;
        }

        /// <summary>
        /// サンプルがまだ 1 件も無い理由。**「揺れていない」とは言わない。**
        ///
        /// 記録できない理由は 3 通りあり、どれも観測値ではないので出所の接頭辞を
        /// 付けない語を選んでいる。<c>m_activeDuration</c> はプレハブ値で
        /// **誰もまだ実測していない**（§A-0）ので、読めていない可能性が現実にある。
        /// </summary>
        private static string WaitingReason(EarthquakeSnapshot snapshot)
        {
            if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                // 揺れの窓が分からない。窓を決め打ちで補うと、地震が終わった後も
                // 伸び続ける波形になる（CameraShakeBooster と同じ判断）。
                return Strings.EarthquakeUnavailable;
            }

            for (int i = 0; i < snapshot.Quakes.Count; i++)
            {
                if (snapshot.Quakes[i].DisasterId != snapshot.WaveformQuakeId) continue;
                return snapshot.Quakes[i].ActivationScheduled
                    ? Strings.EarthquakePhaseEmerging     // 本震前。揺れの窓がまだ開いていない。
                    : Strings.EarthquakeTimeUnknown;      // SelfTrigger が立っていない（§A-1）。
            }

            return Strings.EarthquakePhaseEmerging;
        }
    }
}
