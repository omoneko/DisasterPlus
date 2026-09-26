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
            // Neither NaN nor out-of-range values may leave the band.
            // **Leave it and the particle count shoots up.**
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
            // The rateOverTime of Fire Particles is 200, that of Factory Smoke is 15.
            // Pass the same magnitude and the flames alone become 13 times denser.
            Assert.True(EruptionEffectPlan.FlameMagnitudeMax
                        < EruptionEffectPlan.PlumeMagnitudeMin);
        }

        [Fact]
        public void ARadiusIsNeverZeroEvenWhenTheCraterCouldNotBeRead()
        {
            // Particles still appear at radius 0 (the area has a floor of 100). Letting the
            // 0 pass through would make it erupt from a single point, so it is always raised
            // to the floor.
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
            // Even broken input never leaves the window.
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
            // Make it rectangular and a dense burst of particles appears for a single frame,
            // which reads as a flicker.
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
