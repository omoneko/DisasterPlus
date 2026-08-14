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
            return BuildingMargin.Evaluate(
                buildingId, disasterId: 7,
                buildingPos: new Vec2(distance, 0f), epicentre: new Vec2(0f, 0f),
                intensity: intensity, band: band, alreadyDown: alreadyDown);
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
    }
}
