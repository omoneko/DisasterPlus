using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>この建物のこの地震に対する結論。</summary>
    public enum CollapseVerdict
    {
        /// <summary>断層の幾何が読めていないので判定を出せない。</summary>
        Unknown,

        WillCollapse,

        /// <summary>**全体円盤では**倒壊しない。断層帯の外側でしか名乗ってはいけない。</summary>
        Survives,

        /// <summary>断層帯の内側。破壊円盤による別判定があるので生存を断定しない。</summary>
        InsideFaultZone,

        AlreadyDown,

        /// <summary>震央距離が R 以上。バニラは判定すらしていない。</summary>
        OutOfRange,
    }

    /// <summary>
    /// 1 建物ぶんの「余裕度」。**予言ではない。**
    ///
    /// バニラは建物ごとに new Randomizer(buildingID | (disasterID &lt;&lt; 16)) から
    /// 固定のしきい値を 2 個引き、局所係数 × probability がそれを超えたら壊す
    /// （IL 事実文書 §A-3）。この種はフレームにもステップにも依存しないので、
    /// 同じ種を再構成すれば**バニラがこれから引く値をここで先に知ることができる**。
    /// しかも全体円盤の震央は動かないので、**倒れるかどうかは地震が始まった瞬間に
    /// 既に決まっている**。依頼文の「倒壊はおそらくランダム」への回答がこれ。
    ///
    /// **限界（呼び出し側は必ず併記すること）:** ここで答えられるのは全体円盤
    /// （probability = 0.02、震央中心）についてだけ。断層に沿った 4 個の円盤は
    /// 毎ステップ位置が振り直され probability = 1 で壊すので、帯の内側では
    /// 「倒れません」と言ってはいけない。<see cref="CollapseVerdict.InsideFaultZone"/>。
    ///
    /// **不明を外側と言い換えない。** 断層の幾何はプレハブの
    /// m_crackLength / m_crackWidth に乗っており、その実数値は DLL に無い（§A-0）。
    /// 読めていないとき <see cref="FaultBand.Known"/> は false になり、ここは
    /// <see cref="CollapseVerdict.Unknown"/> を返す。**しきい値も距離も出せるが、
    /// 判定だけは出せない**——帯の内外が分からない以上、全体円盤の結論を
    /// この建物の結論として名乗る根拠が無いからである。
    /// </summary>
    public struct BuildingMargin
    {
        public readonly ushort BuildingId;

        /// <summary>震央からの水平距離。</summary>
        public readonly float Distance;

        /// <summary>全体円盤の局所係数 s（= バニラの fD）。</summary>
        public readonly float LocalFactor;

        public readonly int CollapseThresholdValue;
        public readonly int BurnThresholdValue;

        /// <summary>この距離より内側なら全体円盤で倒壊する。0 ならどの距離でも倒壊しない。</summary>
        public readonly float CollapseWithin;

        public readonly CollapseVerdict Verdict;

        private BuildingMargin(ushort buildingId, float distance, float localFactor,
                               int collapseThreshold, int burnThreshold,
                               float collapseWithin, CollapseVerdict verdict)
        {
            BuildingId = buildingId;
            Distance = distance;
            LocalFactor = localFactor;
            CollapseThresholdValue = collapseThreshold;
            BurnThresholdValue = burnThreshold;
            CollapseWithin = collapseWithin;
            Verdict = verdict;
        }

        /// <summary>カーソルの下に建物が無い、あるいは地震が無い。</summary>
        public static BuildingMargin None()
        {
            return new BuildingMargin(0, 0f, 0f, 0, 0, 0f, CollapseVerdict.Unknown);
        }

        /// <summary>
        /// 建物が特定できているか。<see cref="None"/> と区別する唯一の手段。
        /// 建物 ID 0 は CS の空スロットなので、実在する建物と衝突しない。
        /// </summary>
        public bool HasBuilding { get { return BuildingId != 0; } }

        public static BuildingMargin Evaluate(ushort buildingId, ushort disasterId,
                                              Vec2 buildingPos, Vec2 epicentre,
                                              byte intensity, FaultBand band, bool alreadyDown)
        {
            float dx = buildingPos.X - epicentre.X;
            float dz = buildingPos.Z - epicentre.Z;
            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

            var thresholds = CollapseThreshold.For(buildingId, disasterId);
            float local = SeismicIntensity.At(distance, intensity);

            // probability を渡す余地の無い入口を使う。断層 4 円盤は別のランプ
            // （min = w, max = 2w、しかも中心が毎ステップ振り直される）なので、
            // この距離は全体円盤についてしか意味を持たない。
            float within = CollapseThreshold.GlobalDiscCollapseDistance(
                thresholds.Collapse, intensity);

            CollapseVerdict verdict;
            if (alreadyDown)
            {
                verdict = CollapseVerdict.AlreadyDown;
            }
            else if (!SeismicIntensity.IsInside(distance, intensity))
            {
                // preRadius による一次カリングの外。バニラは乱数すら引いていない。
                verdict = CollapseVerdict.OutOfRange;
            }
            else if (!band.Known)
            {
                // 帯の内外が分からないので、生存も倒壊も断定しない。
                verdict = CollapseVerdict.Unknown;
            }
            else if (band.Contains(buildingPos))
            {
                // ★ この分岐が Survives へ至る唯一の経路の手前にあること自体が、
                //    「断層帯の内側の建物に『倒れません』と言わない」の保証である。
                //    下の 2 分岐より上から動かさないこと。断層帯の内側では
                //    probability = 1 の破壊円盤が別に判定しており、全体円盤の
                //    しきい値はその判定について何ひとつ語っていない。
                verdict = CollapseVerdict.InsideFaultZone;
            }
            else if (CollapseThreshold.GlobalDiscHits(thresholds.Collapse, local))
            {
                verdict = CollapseVerdict.WillCollapse;
            }
            else
            {
                verdict = CollapseVerdict.Survives;
            }

            return new BuildingMargin(buildingId, distance, local,
                                      thresholds.Collapse, thresholds.Burn, within, verdict);
        }
    }
}
