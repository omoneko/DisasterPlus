using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="Assumptions"/> のうち①天気予報タブ の前提。
    ///
    /// **このファイルには検証しか置かない。** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> は本体側の private のままで、
    /// partial なので可視性を 1 つも上げずに使える（分割の要件そのもの）。
    ///
    /// 件数は <see cref="ForecastCheckCount"/> がこのファイルの中で宣言する。
    /// **検証を足したらここも増やすこと** —— 本体の <c>TotalCheckCount</c> は
    /// これらの和である。
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>このファイルが持つ検証の数。</summary>
        private const int ForecastCheckCount = 7;

        private static void RunForecast()
        {
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
        }
    }
}
