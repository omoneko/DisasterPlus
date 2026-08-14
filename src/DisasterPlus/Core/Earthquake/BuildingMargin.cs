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

        /// <summary>
        /// 破壊のコードそのものが他 MOD に置き換えられている。
        ///
        /// Natural Disasters Renewal は <c>DisasterHelpers.DestroyBuildings</c> を
        /// **Prefix が false を返す形で完全置換**し、<c>probability == 0.02f</c> を
        /// 目印にバニラ地震だと判定して 0.04 を使う（IL 事実文書 §E-2）。
        /// つまりこの MOD が読んでいる 0.02 のランプは、その環境では
        /// **誰も実行していない式**である。
        ///
        /// ここで「倒れません」と言うと、実際には倒れる建物について、
        /// 実測を名乗ったまま反対のことを断言することになる
        /// （強度 55 / tD = 300 の建物は NDR 下では 775 m まで倒れる）。
        /// **数値ではなく判定のほうを取り下げる。**
        /// </summary>
        DamageModelReplaced,
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
    ///
    /// **他 MOD が破壊コードを置き換えている場合も同じ扱い。**
    /// Natural Disasters Renewal は <c>DisasterHelpers.DestroyBuildings</c> を丸ごと
    /// 置き換えるので（§E-2）、そのときここが読んでいる 0.02 のランプは
    /// **どこでも実行されていない**。<c>damageModelReplaced</c> を立てると
    /// <see cref="CollapseVerdict.DamageModelReplaced"/> になり、距離も伏せる。
    /// 「この MOD は <c>DisasterHelpers</c> を経由しない」という②の方針は
    /// **被害を書く側の話**であって、**読む側にはまったく効かない**。
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

        /// <summary>
        /// この距離より内側なら全体円盤で倒壊する。0 ならどの距離でも倒壊しない。
        /// **<see cref="Verdict"/> が <see cref="CollapseVerdict.DamageModelReplaced"/> の
        /// ときは 0 が入る** —— バニラの 0.02 から出した距離は、その環境では
        /// 誰も使っていない数字だからである。
        /// </summary>
        public readonly float CollapseWithin;

        /// <summary>
        /// この距離より内側なら全体円盤で出火する。0 ならどの距離でも出火しない。
        /// 倒壊と同じランプ・同じ probability で、違うのはしきい値だけ（§A-3）。
        /// </summary>
        public readonly float BurnWithin;

        public readonly CollapseVerdict Verdict;

        /// <summary>
        /// **出火の結論。** 依頼文が明示的に挙げていた「揺れによる火災」の答えで、
        /// 材料（2 回目の引き）は最初から <see cref="BurnThresholdValue"/> にあった。
        ///
        /// <see cref="CollapseVerdict"/> を流用しているのは、分岐の構造が
        /// 倒壊とまったく同じだからである（<see cref="CollapseVerdict.WillCollapse"/> は
        /// 「2 回目の引きが当たる」＝出火する、の意味になる）。表示側は
        /// 出火用の文言に読み替えること。
        ///
        /// **倒壊が優先する。** IL は <c>else if (hitB &amp;&amp; ...)</c> なので、
        /// 同じ建物で倒壊も当たっていればここへは来ない。その順序も表示側が併記する。
        /// </summary>
        public readonly CollapseVerdict BurnVerdict;

        private BuildingMargin(ushort buildingId, float distance, float localFactor,
                               int collapseThreshold, int burnThreshold,
                               float collapseWithin, float burnWithin,
                               CollapseVerdict verdict, CollapseVerdict burnVerdict)
        {
            BuildingId = buildingId;
            Distance = distance;
            LocalFactor = localFactor;
            CollapseThresholdValue = collapseThreshold;
            BurnThresholdValue = burnThreshold;
            CollapseWithin = collapseWithin;
            BurnWithin = burnWithin;
            Verdict = verdict;
            BurnVerdict = burnVerdict;
        }

        /// <summary>カーソルの下に建物が無い、あるいは地震が無い。</summary>
        public static BuildingMargin None()
        {
            return new BuildingMargin(0, 0f, 0f, 0, 0, 0f, 0f,
                                      CollapseVerdict.Unknown, CollapseVerdict.Unknown);
        }

        /// <summary>
        /// 建物が特定できているか。<see cref="None"/> と区別する唯一の手段。
        /// 建物 ID 0 は CS の空スロットなので、実在する建物と衝突しない。
        /// </summary>
        public bool HasBuilding { get { return BuildingId != 0; } }

        /// <summary>
        /// <paramref name="damageModelReplaced"/> は「<c>DisasterHelpers.DestroyBuildings</c> が
        /// 他 MOD に完全置換されている」の意味（実質 Natural Disasters Renewal、§E-2）。
        /// **true のとき、この関数は結論を一切出さない。** 出せば「実測」を名乗ったまま
        /// 誰も実行していない式の答えを断言することになる。
        /// </summary>
        public static BuildingMargin Evaluate(ushort buildingId, ushort disasterId,
                                              Vec2 buildingPos, Vec2 epicentre,
                                              byte intensity, FaultBand band, bool alreadyDown,
                                              bool damageModelReplaced)
        {
            float dx = buildingPos.X - epicentre.X;
            float dz = buildingPos.Z - epicentre.Z;
            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

            var thresholds = CollapseThreshold.For(buildingId, disasterId);
            float local = SeismicIntensity.At(distance, intensity);

            // ★ 他 MOD が破壊コードを置き換えているなら、距離も判定も出さない。
            //    AlreadyDown より先に見ないこと —— 「もう倒れている」は建物の
            //    現在の状態であって、これから何が起きるかの予測ではないので、
            //    どの MOD が破壊を計算していても正しい。
            if (!alreadyDown && damageModelReplaced)
            {
                return new BuildingMargin(buildingId, distance, local,
                                          thresholds.Collapse, thresholds.Burn, 0f, 0f,
                                          CollapseVerdict.DamageModelReplaced,
                                          CollapseVerdict.DamageModelReplaced);
            }

            // probability を渡す余地の無い入口を使う。断層 4 円盤は別のランプ
            // （min = w, max = 2w、しかも中心が毎ステップ振り直される）なので、
            // この距離は全体円盤についてしか意味を持たない。
            float within = CollapseThreshold.GlobalDiscCollapseDistance(
                thresholds.Collapse, intensity);
            float burnWithin = CollapseThreshold.GlobalDiscBurnDistance(
                thresholds.Burn, intensity);

            // 帯の内外は倒壊と出火で同じ答えなので 1 回だけ引く
            // （FaultBand.Contains は u の全域を走査するので、安い呼び出しではない）。
            bool insideBand = band.Known && band.Contains(buildingPos);

            var verdict = VerdictFor(thresholds.Collapse, distance, local,
                                     intensity, band.Known, insideBand, alreadyDown);
            var burnVerdict = VerdictFor(thresholds.Burn, distance, local,
                                         intensity, band.Known, insideBand, alreadyDown);

            return new BuildingMargin(buildingId, distance, local,
                                      thresholds.Collapse, thresholds.Burn,
                                      within, burnWithin, verdict, burnVerdict);
        }

        /// <summary>
        /// しきい値 1 個ぶんの結論。倒壊と出火で**分岐の構造が同じ**なので共有する
        /// （全体円盤の fD と fB はどちらも <c>1 - d/R</c>、probability も同じ 0.02）。
        /// 分岐の順序には意味があるので入れ替えないこと。
        /// </summary>
        private static CollapseVerdict VerdictFor(int threshold, float distance, float local,
                                                  byte intensity, bool bandKnown, bool insideBand,
                                                  bool alreadyDown)
        {
            if (alreadyDown) return CollapseVerdict.AlreadyDown;

            // preRadius による一次カリングの外。バニラは乱数すら引いていない。
            if (!SeismicIntensity.IsInside(distance, intensity)) return CollapseVerdict.OutOfRange;

            // 帯の内外が分からないので、生存も倒壊も断定しない。
            if (!bandKnown) return CollapseVerdict.Unknown;

            // ★ この分岐が Survives へ至る唯一の経路の手前にあること自体が、
            //    「断層帯の内側の建物に『倒れません』と言わない」の保証である。
            //    下の 2 分岐より上から動かさないこと。断層帯の内側では
            //    probability = 1 の破壊円盤が別に判定しており、全体円盤の
            //    しきい値はその判定について何ひとつ語っていない。
            if (insideBand) return CollapseVerdict.InsideFaultZone;

            return CollapseThreshold.GlobalDiscHits(threshold, local)
                ? CollapseVerdict.WillCollapse
                : CollapseVerdict.Survives;
        }
    }
}
