using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class BuildingMarginTests
    {
        private static FaultBand NoBand()
        {
            // 幾何が確定した、しかしどの試験点も含まない小さな帯。
            return new FaultBand(new Vec2(0f, 0f), 0f, length: 10f, width: 1f);
        }

        private static BuildingMargin Eval(ushort buildingId, float distance, byte intensity,
                                           FaultBand band, bool alreadyDown)
        {
            return Eval(buildingId, distance, intensity, band, alreadyDown, false);
        }

        private static BuildingMargin Eval(ushort buildingId, float distance, byte intensity,
                                           FaultBand band, bool alreadyDown,
                                           bool damageModelReplaced)
        {
            return BuildingMargin.Evaluate(
                buildingId, disasterId: 7,
                buildingPos: new Vec2(distance, 0f), epicentre: new Vec2(0f, 0f),
                intensity: intensity, band: band, alreadyDown: alreadyDown,
                damageModelReplaced: damageModelReplaced);
        }

        [Fact]
        public void ThresholdsComeFromTheVanillaSeed()
        {
            var expected = CollapseThreshold.For(1234, 7);
            var m = Eval(1234, 500f, 100, NoBand(), false);
            Assert.Equal(expected.Collapse, m.CollapseThresholdValue);
            Assert.Equal(expected.Burn, m.BurnThresholdValue);
        }

        [Fact]
        public void OutsideTheRadius_IsOutOfRangeNotSurvives()
        {
            // 「圏外」と「耐える」を同じ結論にしない。前者はバニラが判定すらしていない。
            var m = Eval(10, 99999f, 55, NoBand(), false);
            Assert.Equal(CollapseVerdict.OutOfRange, m.Verdict);
        }

        [Fact]
        public void AlreadyDown_ShortCircuitsEverything()
        {
            var m = Eval(10, 100f, 255, NoBand(), true);
            Assert.Equal(CollapseVerdict.AlreadyDown, m.Verdict);
        }

        [Fact]
        public void InsideTheFaultZone_NeverClaimsSurvival()
        {
            // 帯の内側では、全体円盤で耐える建物でも「倒れません」と言ってはいけない。
            // 4 個の破壊円盤は probability = 1 で壊すので、全体円盤のしきい値は
            // その判定について何も語っていない。
            var wideBand = new FaultBand(new Vec2(0f, 0f), 0f, length: 4000f, width: 500f);
            for (ushort id = 1; id < 200; id++)
            {
                var m = Eval(id, 50f, 100, wideBand, false);
                Assert.NotEqual(CollapseVerdict.Survives, m.Verdict);
                Assert.Equal(CollapseVerdict.InsideFaultZone, m.Verdict);
            }
        }

        [Fact]
        public void UnknownFaultGeometry_ProducesUnknownVerdict()
        {
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            var m = Eval(10, 100f, 100, unknown, false);
            Assert.Equal(CollapseVerdict.Unknown, m.Verdict);
            // 数値そのものは出せる（しきい値は幾何と無関係）。
            Assert.True(m.CollapseThresholdValue >= 0);
        }

        [Fact]
        public void VerdictAgreesWithTheCollapseDistance()
        {
            // 「X m 以内で倒れる」という表示と、実際の判定が食い違わないこと。
            // これが食い違うと、いちばんもっともらしい形で嘘をつくことになる。
            var band = NoBand();
            for (ushort id = 1; id < 300; id++)
            {
                var far = Eval(id, 3000f, 255, band, false);
                if (far.Verdict == CollapseVerdict.OutOfRange) continue;

                bool expected = far.Distance < far.CollapseWithin;
                Assert.Equal(expected ? CollapseVerdict.WillCollapse : CollapseVerdict.Survives,
                             far.Verdict);
            }
        }

        // ── 出火（全体レビュー M1）──────────────────────────────────

        [Fact]
        public void BurnVerdictAgreesWithTheBurnDistance()
        {
            // 倒壊とまったく同じ関係が、2 回目の引きについても成り立つこと。
            var band = NoBand();
            for (ushort id = 1; id < 300; id++)
            {
                var m = Eval(id, 3000f, 255, band, false);
                if (m.BurnVerdict == CollapseVerdict.OutOfRange) continue;

                bool expected = m.Distance < m.BurnWithin;
                Assert.Equal(expected ? CollapseVerdict.WillCollapse : CollapseVerdict.Survives,
                             m.BurnVerdict);
            }
        }

        [Fact]
        public void BurnDistanceComesFromTheSecondDraw()
        {
            // 全体円盤では fB も fD も 1 - d/R（burnRadiusMin = 0, burnRadiusMax = R）なので、
            // 2 つの距離が違うのは**しきい値が別の引きだから**でしかない。
            // 1 回目と 2 回目を取り違えていたら（同じ値を 2 回使っていたら）ここで落ちる。
            bool sawDifferentDraws = false;
            for (ushort id = 1; id < 300; id++)
            {
                var m = Eval(id, 500f, 200, NoBand(), false);

                Assert.Equal(CollapseThreshold.GlobalDiscBurnDistance(m.BurnThresholdValue, 200),
                             m.BurnWithin, 3);
                Assert.Equal(CollapseThreshold.GlobalDiscCollapseDistance(
                                 m.CollapseThresholdValue, 200),
                             m.CollapseWithin, 3);

                if (m.CollapseThresholdValue != m.BurnThresholdValue) sawDifferentDraws = true;
            }
            Assert.True(sawDifferentDraws, "the two draws were identical for every building");
        }

        [Fact]
        public void BurnFollowsTheSameGuardsAsCollapse()
        {
            Assert.Equal(CollapseVerdict.OutOfRange,
                         Eval(10, 99999f, 55, NoBand(), false).BurnVerdict);
            Assert.Equal(CollapseVerdict.AlreadyDown,
                         Eval(10, 100f, 255, NoBand(), true).BurnVerdict);
            Assert.Equal(CollapseVerdict.Unknown,
                         Eval(10, 100f, 100, new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f), false)
                             .BurnVerdict);
        }

        // ── 破壊コードが他 MOD に置き換えられている場合（全体レビュー C2）─────

        [Fact]
        public void ReplacedDamageModel_WithholdsBothVerdicts()
        {
            // NDR は DisasterHelpers.DestroyBuildings を完全置換し probability を
            // 0.02 → 0.04 にする（§E-2）。0.02 から導いた結論は、その環境では
            // バニラの答えでも NDR の答えでもない。
            var m = Eval(10, 100f, 55, NoBand(), false, damageModelReplaced: true);
            Assert.Equal(CollapseVerdict.DamageModelReplaced, m.Verdict);
            Assert.Equal(CollapseVerdict.DamageModelReplaced, m.BurnVerdict);
        }

        [Fact]
        public void ReplacedDamageModel_WithholdsTheDistancesToo()
        {
            // 判定だけ伏せて距離を出すと、伏せた意味が無くなる。
            var m = Eval(10, 100f, 55, NoBand(), false, damageModelReplaced: true);
            Assert.Equal(0f, m.CollapseWithin, 4);
            Assert.Equal(0f, m.BurnWithin, 4);
            // 距離としきい値そのもの（バニラの乱数）は読めているので残す。
            Assert.True(m.Distance > 0f);
        }

        [Fact]
        public void ReplacedDamageModel_NeverHidesThatABuildingIsAlreadyDown()
        {
            // 「もう倒れている」は建物の現在の状態であって、これから何が起きるかの
            // 予測ではない。どの MOD が破壊を計算していても正しい。
            var m = Eval(10, 100f, 55, NoBand(), true, damageModelReplaced: true);
            Assert.Equal(CollapseVerdict.AlreadyDown, m.Verdict);
        }

        [Fact]
        public void ReplacedDamageModel_NeverClaimsSurvival()
        {
            // 全体レビュー C2 の失敗例そのもの: 強度 55、遠い建物。
            // NDR 下では倒れうるので、「どの距離でも倒壊しません」と言ってはいけない。
            var band = NoBand();
            for (ushort id = 1; id < 200; id++)
            {
                var m = Eval(id, 2000f, 55, band, false, damageModelReplaced: true);
                Assert.NotEqual(CollapseVerdict.Survives, m.Verdict);
                Assert.NotEqual(CollapseVerdict.Survives, m.BurnVerdict);
            }
        }
    }
}
