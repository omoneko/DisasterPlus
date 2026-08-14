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

            float resonance = LongPeriodResponse.Resonance(
                LongPeriodResponse.BuildingPeriodFrames(height));

            return Strings.EarthquakeLongPeriod + ": "
                   + Strings.EarthquakeBuildingHeight + " " + height.ToString("F0") + " m"
                   + " / " + Strings.EarthquakeResonance + " " + resonance.ToString("F2")
                   + " / " + Strings.EarthquakeLongPeriodRisk + " "
                   + (chance * 100f).ToString("F1") + "%";
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
