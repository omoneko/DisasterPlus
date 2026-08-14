using System.Collections.Generic;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 「どの地震の話をしているのか」を 1 箇所で決める。
    ///
    /// 地震は同時に複数進行しうる（IL 事実文書 §E-1、上限 256）。順位付けが
    /// 複数箇所に散ると、**同じパネルの別の行が別の地震を指す**という壊れ方をする。
    /// 実際、この選定ロジックは以前 3 箇所に写しで存在し、そのうち 2 つ
    /// （<c>EarthquakeReader.SelectDamagingQuake</c> /
    ///  <c>SeismographRecorder.SelectRecordingQuake</c>）は 1 バイトも違わない複製、
    /// 残る 1 つ（<c>EarthquakePanel.SelectPrimary</c>）だけが <c>Clearing</c> を
    /// 含んでいて、収束中の地震について「カーソルの倒壊係数」と「断層帯: 内側」を
    /// 出していた —— <c>DestroyBuildings</c> の呼び出しは <c>Active</c> 分岐に
    /// **しか無い**（§A-3）のに、である。
    ///
    /// **順位付けは 1 つだけ、違いは「どの位相を対象に含めるか」だけにする。**
    /// </summary>
    public static class QuakeSelection
    {
        /// <summary>
        /// 破壊・揺れの計算が**これから走る、あるいは今走っている**地震を 1 個選ぶ。
        /// 対象は <c>Active</c> と <c>Emerging</c> のみ（<c>Clearing</c> は含まない）。
        /// 同位なら強度が大きい方、それも同じなら添字が小さい方。無ければ null。
        ///
        /// <c>Emerging</c> を含めるのは、揺れの窓（§A-7 の <c>e = frame - activation + 128</c>）が
        /// <c>Emerging|Active</c> で開くのと、本震前から余裕度を見せたいためである。
        /// </summary>
        public static EarthquakeReading SelectDamaging(IList<EarthquakeReading> quakes)
        {
            if (quakes == null) return null;

            EarthquakeReading best = null;
            int bestRank = 0;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                if (q == null) continue;

                int rank = RankOf(q.Phase);
                if (rank == 0) continue;

                bool better;
                if (best == null) better = true;
                else if (rank != bestRank) better = rank > bestRank;
                else better = q.Intensity > best.Intensity;

                if (!better) continue;
                best = q;
                bestRank = rank;
            }

            return best;
        }

        /// <summary>
        /// この位相で破壊判定・揺れの式が動くか。
        /// **表示側は、これが false の地震について局所係数や断層帯の内外を出してはいけない。**
        /// </summary>
        public static bool RunsDamage(EarthquakePhase phase)
        {
            return RankOf(phase) != 0;
        }

        private static int RankOf(EarthquakePhase phase)
        {
            if (phase == EarthquakePhase.Active) return 2;
            if (phase == EarthquakePhase.Emerging) return 1;
            return 0;
        }
    }
}
