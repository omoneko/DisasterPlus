using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class EruptionEffectPlanTests
    {
        [Fact]
        public void ThePlumeGetsDenserAsTheEruptionGetsStronger()
        {
            Assert.Equal(EruptionEffectPlan.PlumeMagnitudeMin,
                         EruptionEffectPlan.PlumeMagnitude(0f), 3);
            Assert.Equal(EruptionEffectPlan.PlumeMagnitudeMax,
                         EruptionEffectPlan.PlumeMagnitude(1f), 3);
            Assert.True(EruptionEffectPlan.PlumeMagnitude(0.7f)
                        > EruptionEffectPlan.PlumeMagnitude(0.3f));
        }

        [Fact]
        public void ThePlumeStaysInsideItsBandForSillyIntensities()
        {
            // NaN も範囲外も帯の外へ出さない。**外へ出ると粒子数が跳ねる。**
            Assert.Equal(EruptionEffectPlan.PlumeMagnitudeMin,
                         EruptionEffectPlan.PlumeMagnitude(float.NaN), 3);
            Assert.Equal(EruptionEffectPlan.PlumeMagnitudeMin,
                         EruptionEffectPlan.PlumeMagnitude(-4f), 3);
            Assert.Equal(EruptionEffectPlan.PlumeMagnitudeMax,
                         EruptionEffectPlan.PlumeMagnitude(9f), 3);
        }

        [Fact]
        public void TheFlamesAreFarLessDenseThanThePlume()
        {
            // Fire Particles の rateOverTime は 200、Factory Smoke は 15。
            // 同じ magnitude を渡すと炎だけが 13 倍濃くなる。
            Assert.True(EruptionEffectPlan.FlameMagnitudeMax
                        < EruptionEffectPlan.PlumeMagnitudeMin);
        }

        [Fact]
        public void ARadiusIsNeverZeroEvenWhenTheCraterCouldNotBeRead()
        {
            // 半径 0 でも粒子は湧く（面積の下限が 100）。0 のまま素通りさせると
            // 1 点から噴くことになるので、必ず下限へ持ち上げる。
            Assert.Equal(EruptionEffectPlan.MinRadiusMetres,
                         EruptionEffectPlan.PlumeRadiusMetres(0f, 1f), 3);
            Assert.Equal(EruptionEffectPlan.MinRadiusMetres,
                         EruptionEffectPlan.PlumeRadiusMetres(float.NaN, 1f), 3);
            Assert.Equal(EruptionEffectPlan.MinRadiusMetres,
                         EruptionEffectPlan.EjectaRadiusMetres(-10f), 3);
        }

        [Fact]
        public void TheRadiusGrowsWithTheCraterAndWithTheIntensity()
        {
            float small = EruptionEffectPlan.PlumeRadiusMetres(100f, 0f);
            float large = EruptionEffectPlan.PlumeRadiusMetres(100f, 1f);
            Assert.True(large > small);
            Assert.True(EruptionEffectPlan.PlumeRadiusMetres(200f, 0.5f)
                        > EruptionEffectPlan.PlumeRadiusMetres(100f, 0.5f));
        }

        [Fact]
        public void TheEjectaFiresMoreOftenWhenTheEruptionIsStronger()
        {
            Assert.Equal(EruptionEffectPlan.EjectaPeriodMaxSeconds,
                         EruptionEffectPlan.EjectaPeriodSeconds(0f), 3);
            Assert.Equal(EruptionEffectPlan.EjectaPeriodMinSeconds,
                         EruptionEffectPlan.EjectaPeriodSeconds(1f), 3);
            Assert.True(EruptionEffectPlan.EjectaPeriodSeconds(1f)
                        < EruptionEffectPlan.EjectaPeriodSeconds(0f));
        }

        [Fact]
        public void TheBurstPhaseWrapsAndNeverLeavesTheWindow()
        {
            Assert.Equal(0.5f, EruptionEffectPlan.BurstPhaseSeconds(4.5f, 2f), 3);
            Assert.Equal(0f, EruptionEffectPlan.BurstPhaseSeconds(4f, 2f), 3);
            // 壊れた入力でも窓の外へ出ない。
            Assert.Equal(0f, EruptionEffectPlan.BurstPhaseSeconds(float.NaN, 2f), 3);
            Assert.Equal(0f, EruptionEffectPlan.BurstPhaseSeconds(3f, 0f), 3);
            Assert.Equal(0f, EruptionEffectPlan.BurstPhaseSeconds(-3f, 2f), 3);
        }

        [Fact]
        public void TheEjectaIsSilentBetweenBurstsSoTheCallCanBeSkipped()
        {
            Assert.Equal(0f, EruptionEffectPlan.EjectaMagnitude(
                1f, EruptionEffectPlan.EjectaBurstSeconds), 4);
            Assert.Equal(0f, EruptionEffectPlan.EjectaMagnitude(
                1f, EruptionEffectPlan.EjectaBurstSeconds + 1f), 4);
            Assert.True(EruptionEffectPlan.EjectaMagnitude(
                1f, EruptionEffectPlan.EjectaBurstSeconds * 0.5f) > 0f);
        }

        [Fact]
        public void TheEjectaWindowRisesAndFallsInsteadOfSwitchingOn()
        {
            // 矩形にすると 1 フレームだけ濃い粒子が出て点滅して見える。
            float peak = EruptionEffectPlan.EjectaMagnitude(
                1f, EruptionEffectPlan.EjectaBurstSeconds * 0.5f);
            float edge = EruptionEffectPlan.EjectaMagnitude(
                1f, EruptionEffectPlan.EjectaBurstSeconds * 0.05f);
            Assert.True(peak > edge);
            Assert.Equal(EruptionEffectPlan.EjectaMagnitudeMax, peak, 2);
            Assert.Equal(0f, EruptionEffectPlan.EjectaMagnitude(1f, 0f), 4);
        }
    }
}
