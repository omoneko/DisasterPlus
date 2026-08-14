using System.Collections.Generic;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <c>EarthquakeAI</c> プレハブに焼き込まれている 4 つの調整値。
    ///
    /// **この 4 個の実数値は DLL に存在しない**（IL 事実文書 §A-0）。プレハブの
    /// シリアライズ値なので IL 逆アセンブルでは見えず、UnityPy による
    /// <c>sharedassets</c> の読み出しも型ツリーが読めずに失敗している。したがって
    /// **実行時に <c>DisasterManager.FindDisasterInfo&lt;EarthquakeAI&gt;()</c> から読んで
    /// 診断ダンプに出すのが唯一の入手経路**であり、それが Task 3 の主目的である。
    /// ②の以後の持続時間の設計は全てこの 4 個の上に乗る。
    ///
    /// struct にしているのは、キャッシュしても Unity の fake-null 自己修復問題を
    /// 持ち込まないため（float / uint しか持たないので <c>DisasterInfo</c> の参照を
    /// 抱え込まずに済む）。既定値は <see cref="Resolved"/> == false ＝「まだ／もう読めていない」。
    /// </summary>
    public struct EarthquakePrefabFacts
    {
        /// <summary>4 値を実際に読めたか。false のとき他のフィールドは全て 0 で、意味を持たない。</summary>
        public readonly bool Resolved;

        public readonly float CrackLength;
        public readonly float CrackWidth;
        public readonly uint EmergingDuration;
        public readonly uint ActiveDuration;

        public EarthquakePrefabFacts(float crackLength, float crackWidth,
                                     uint emergingDuration, uint activeDuration)
        {
            Resolved = true;
            CrackLength = crackLength;
            CrackWidth = crackWidth;
            EmergingDuration = emergingDuration;
            ActiveDuration = activeDuration;
        }
    }

    /// <summary>
    /// sim スレッドで作り main スレッドで読む不変スナップショット。
    /// ①の <see cref="WeatherSnapshot"/> と同じ規律で、**一度作ったら書き換えない**。
    ///
    /// </summary>
    public class EarthquakeSnapshot
    {
        /// <summary>
        /// 地震が 1 個も無いときに全スナップショットで共有する空リスト。
        /// sim tick ごとの空リスト確保を避けるためだけにある。
        ///
        /// <c>ReadOnlyCollection</c> で包んでいるのは意図的。素の <c>List</c> のまま
        /// <c>IList</c> として配ると <c>snapshot.Quakes.Add(...)</c> がコンパイルも実行も
        /// 通ってしまい、**共有された空リストを 1 箇所が汚すと以後の全スナップショットが
        /// 壊れる**。ここで包んでおけば、その誤用は最初の 1 回で即座に例外になる。
        /// </summary>
        private static readonly IList<EarthquakeReading> NoQuakes =
            new List<EarthquakeReading>(0).AsReadOnly();

        /// <summary>
        /// 今この瞬間、生きている（Created かつ Deleted でない）地震。
        /// **構築後に変更しないこと**（<see cref="EarthquakeReader"/> は読み取り専用に
        /// 包んでから渡している）。破られると main スレッドが列挙している最中に
        /// sim スレッドが書き換えることになり、スタックトレースの出ない例外に化ける。
        /// </summary>
        public readonly IList<EarthquakeReading> Quakes;

        public readonly EarthquakePrefabFacts Prefab;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>。表示側が残り時間を出す基準。</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// sim スレッドの時刻（0 〜 23.99963）。
        /// <c>m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR</c> であって
        /// <c>m_currentDayTimeHour</c> ではない（§F-1。詳細は <see cref="EarthquakeReader"/>）。
        /// </summary>
        public readonly float HourOfDay;

        /// <summary>
        /// <c>SimulationManager.m_enableDayNight</c>。
        ///
        /// **false のとき <see cref="HourOfDay"/> は永久に 12.0 に固定される**
        /// （sim スレッドが毎フレーム <c>m_dayTimeOffsetFrames</c> を再設定するため、§F-1）。
        /// これはバグでもゲームの不整合でもなく、プレイヤーが選べる正当な設定なので
        /// 前提検証の FAIL にはしない。ただし**表示側はこの事実を隠さないこと**
        /// （12.0 という数字だけを出すと「正午に固定された時計」が読めた値に見える）。
        /// </summary>
        public readonly bool DayNightEnabled;

        /// <summary>読み取りに成功したか。false なら表示側は「読み取れません」と出す。</summary>
        public readonly bool Valid;

        public EarthquakeSnapshot(IList<EarthquakeReading> quakes, EarthquakePrefabFacts prefab,
                                  uint currentFrame, float hourOfDay, bool dayNightEnabled, bool valid)
        {
            Quakes = quakes == null ? NoQuakes : quakes;
            Prefab = prefab;
            CurrentFrame = currentFrame;
            HourOfDay = hourOfDay;
            DayNightEnabled = dayNightEnabled;
            Valid = valid;
        }

        public static EarthquakeSnapshot Invalid()
        {
            return new EarthquakeSnapshot(NoQuakes, new EarthquakePrefabFacts(), 0u, 0f, false, false);
        }

        /// <summary>地震が 1 個も無いときに使う共有の空リスト。読み取り側専用。</summary>
        public static IList<EarthquakeReading> EmptyQuakeList
        {
            get { return NoQuakes; }
        }
    }
}
