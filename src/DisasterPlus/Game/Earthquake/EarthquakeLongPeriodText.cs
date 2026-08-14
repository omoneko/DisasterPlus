using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 第 2 層（長周期地震動・時間帯係数）の行に入れる**文字列を組み立てるだけ**の型。
    /// main スレッド専用で、ラベルも状態も持たない（<see cref="EarthquakeLayer2Rows"/> が持つ）。
    ///
    /// ── なぜ別ファイルなのか ────────────────────────────────
    ///
    /// <see cref="EarthquakeLayer2Rows"/> は「節の見出しと行の配置」を持つ型で、
    /// そこに 2 つの機能ぶんの文面組み立てまで入れると、行の出し入れ（設定で節が
    /// 畳まれる）の見通しが落ちる。ここは**入力から文字列を作るだけ**なので
    /// 単体で読める。
    ///
    /// ── 出す数字の出所を混ぜない ──────────────────────────────
    ///
    /// ここで作る文字列は 1 つ残らず**本 MOD が発明した量**である。
    /// バニラは建物の高さを揺れにも被害にも使っておらず（§A-7 / §A-3）、
    /// 「夜の方が被害が大きい」という根拠もどこにも無い（設計書 §4.3）。
    /// 呼び出し側は必ず <c>EarthquakeRows.SetLayer2</c> で書き込むこと
    /// （<c>Strings.SourceModel</c> が必ず頭に付く）。
    /// </summary>
    internal static class EarthquakeLongPeriodText
    {
        /// <summary>
        /// カーソル直下の建物についての 1 行。出せないときは null を返す
        /// （呼び出し側が行を空にする）。
        ///
        /// **「高さが読めなかった」と「低いので対象外」を混ぜない。**
        /// 前者は読み取り失敗、後者は実測に基づく結論で、同じ文にすると
        /// 読み取り失敗が結論の顔をして出てくる。
        ///
        /// ── 数字に「いつ効くか」を必ず添える（第 2 層レビュー I1 / M3）──────────
        ///
        /// この行が使う地震は <c>EarthquakeSnapshot.CursorQuakeId</c>、すなわち
        /// <c>QuakeSelection.SelectDamaging</c> の選定（<b>Active または Emerging</b>）である。
        /// 一方、追加被害が実際に走るのは <c>LongPeriodDamage.Step</c> が
        /// <c>Phase == Active</c> を要求するので **Active だけ**である。
        /// 何も断らずに「追加倒壊リスク 6.4%」と出すと、本震前には
        /// **何にも適用されていない確率**が確定値の顔で出る（実機項目 90 が
        /// 被害側の正しい挙動を確かめているのに、表示がそれと食い違う）。
        ///
        /// 走査が上限で打ち切られていた場合も同様に名乗る。走査は震央から外へ
        /// 向かうので打ち切られても震央の周りは評価済みだが、外側はまだである。
        /// </summary>
        internal static string CursorRow(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return null;
            if (!snapshot.CursorBuilding.HasBuilding) return null;

            float height = snapshot.CursorBuildingHeight;
            if (height <= 0f) return Strings.EarthquakeLongPeriodNoHeight;

            var quake = FindQuake(snapshot, snapshot.CursorQuakeId);
            if (quake == null) return null;

            int strength = ModSettings.EarthquakeLongPeriodStrength.value;
            if (strength < 0) strength = 0;

            float chance = LongPeriodResponse.ExtraCollapseChance(
                height, snapshot.CursorBuilding.Distance, quake.Intensity, strength);

            // ★ 時間帯係数は追加被害にだけ掛かる（Task 11）。表示にも同じ係数を
            //    掛けないと、パネルの数字と実際に使われる確率が食い違う。
            //    掛ける順序も LongPeriodDamage.IsSelected と揃える。
            chance *= TimeOfDayFactor.Of(snapshot.HourOfDay);

            float resonance = LongPeriodResponse.Resonance(
                LongPeriodResponse.BuildingPeriodFrames(height));

            return Strings.EarthquakeLongPeriod + ": "
                   + Strings.EarthquakeBuildingHeight + " " + height.ToString("F0") + " m"
                   + " / " + Strings.EarthquakeResonance + " " + resonance.ToString("F2")
                   + " / " + Strings.EarthquakeLongPeriodRisk + " "
                   + (chance * 100f).ToString("F1") + "%"
                   + Caveat(snapshot, quake);
        }

        /// <summary>
        /// 確率の数字に添える但し書き。無いときは空文字。
        ///
        /// **本震前を先に言う。** 「まだ 1 度も適用されていない」の方が
        /// 「走査が途中で止まった」より強い留保なので、両方が当てはまる場面では
        /// 前者だけを出す（2 つ並べても読み手の行動は変わらない）。
        /// </summary>
        private static string Caveat(EarthquakeSnapshot snapshot, EarthquakeReading quake)
        {
            if (quake.Phase != EarthquakePhase.Active)
            {
                return "   (" + Strings.EarthquakeLongPeriodBeforeShock + ")";
            }
            if (snapshot.LongPeriodCapped)
            {
                return "   (" + Strings.EarthquakeLongPeriodCapped + ")";
            }
            return "";
        }

        /// <summary>
        /// 時間帯の 1 行（Task 11）。<c>HH:MM</c> と係数だけを出す。
        ///
        /// **「昼」「夜」の語は出さない。** <see cref="TimeOfDayFactor.Of"/> は境界を
        /// 1 時間かけて滑らかに渡すので、<see cref="TimeOfDayFactor.IsNight"/> の
        /// 硬い境界（ゲーム自身の <c>hour &lt; 5 || hour &gt; 20</c>）と一致しない時間帯が
        /// 生じる。「昼」と書いた横に 1.08 が出るより、数字だけの方が正直である。
        /// </summary>
        internal static string TimeRow(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return null;

            float hour = snapshot.HourOfDay;
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return null;

            return Strings.EarthquakeTimeOfDay + ": " + Clock(hour)
                   + "   " + Strings.EarthquakeTimeFactor + " "
                   + TimeOfDayFactor.Of(hour).ToString("F2");
        }

        /// <summary>
        /// 「日夜サイクルが切ってあるので係数は永久に 1.00 です」の注記。
        /// 出さないときは空文字。
        ///
        /// **これを省くと、この機能は黙って何もしない状態になる**（§F-1 の罠の最終処理）。
        /// 時刻が 12.0 に固定されるのは不具合でもゲームの不整合でもなく
        /// プレイヤーの正当な設定なので <c>Assumptions</c> の FAIL にはしない。
        /// 代わりにここで名乗る。
        /// </summary>
        internal static string TimeNote(EarthquakeSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return "";
            return snapshot.DayNightEnabled ? "" : Strings.EarthquakeNoDayNight;
        }

        /// <summary><c>HH:MM</c>。時刻は [0,24) に畳んでから整形する。</summary>
        private static string Clock(float hour)
        {
            float h = hour % 24f;
            if (h < 0f) h += 24f;

            int hours = (int)h;
            int minutes = (int)((h - hours) * 60f);
            // 端数で 60 分になる経路を塞ぐ（23:60 を出さない）。
            if (minutes > 59) minutes = 59;
            if (minutes < 0) minutes = 0;

            return (hours < 10 ? "0" : "") + hours + ":" + (minutes < 10 ? "0" : "") + minutes;
        }

        private static EarthquakeReading FindQuake(EarthquakeSnapshot snapshot, ushort id)
        {
            if (id == 0) return null;

            var quakes = snapshot.Quakes;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (quakes[i].DisasterId == id) return quakes[i];
            }
            return null;
        }
    }
}
