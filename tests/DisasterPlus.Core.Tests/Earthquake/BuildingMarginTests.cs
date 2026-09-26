using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class BuildingMarginTests
    {
        private static FaultBand NoBand()
        {
            // A small band with settled geometry that contains none of the test points.
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
            // Do not give "out of range" and "survives" the same conclusion. In the former
            // case vanilla has not even made a judgement.
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
            // Inside the band, we must not say "will not collapse" even for a building that
            // survives under the overall disc. The 4 destruction discs destroy with
            // probability = 1, so the overall disc's threshold says nothing about that
            // judgement.
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
            // The numbers themselves can still be shown (the thresholds have nothing to do
            // with the geometry).
            Assert.True(m.CollapseThresholdValue >= 0);
        }

        [Fact]
        public void VerdictAgreesWithTheCollapseDistance()
        {
            // The displayed "collapses within X m" must not disagree with the actual verdict.
            // If those two disagree, we end up lying in the most plausible-looking way there is.
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

        // ── Fires (full review M1) ──────────────────────────────────

        [Fact]
        public void BurnVerdictAgreesWithTheBurnDistance()
        {
            // Exactly the same relation must hold for the second draw.
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
            // On the overall disc both fB and fD are 1 - d/R (burnRadiusMin = 0,
            // burnRadiusMax = R), so the only reason the two distances differ is **that the
            // thresholds come from different draws**. Mix up the first and the second draw
            // (i.e. use the same value twice) and this one fails.
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

        // ── When the destruction code is replaced by another mod (full review C2) ─────

        [Fact]
        public void ReplacedDamageModel_WithholdsBothVerdicts()
        {
            // NDR replaces DisasterHelpers.DestroyBuildings wholesale and changes the
            // probability from 0.02 to 0.04 (§E-2). A conclusion derived from 0.02 is, in
            // that environment, neither vanilla's answer nor NDR's.
            var m = Eval(10, 100f, 55, NoBand(), false, damageModelReplaced: true);
            Assert.Equal(CollapseVerdict.DamageModelReplaced, m.Verdict);
            Assert.Equal(CollapseVerdict.DamageModelReplaced, m.BurnVerdict);
        }

        [Fact]
        public void ReplacedDamageModel_WithholdsTheDistancesToo()
        {
            // Withholding the verdict but still showing the distance defeats the point of
            // withholding it.
            var m = Eval(10, 100f, 55, NoBand(), false, damageModelReplaced: true);
            Assert.Equal(0f, m.CollapseWithin, 4);
            Assert.Equal(0f, m.BurnWithin, 4);
            // The distance and the thresholds themselves (vanilla's random numbers) can
            // still be read, so they stay.
            Assert.True(m.Distance > 0f);
        }

        [Fact]
        public void ReplacedDamageModel_NeverHidesThatABuildingIsAlreadyDown()
        {
            // "Already down" is the building's current state, not a prediction of what is
            // about to happen. It is correct whichever mod is computing the destruction.
            var m = Eval(10, 100f, 55, NoBand(), true, damageModelReplaced: true);
            Assert.Equal(CollapseVerdict.AlreadyDown, m.Verdict);
        }

        [Fact]
        public void ReplacedDamageModel_NeverClaimsSurvival()
        {
            // The very failing case from full review C2: intensity 55, a distant building.
            // Under NDR it can collapse, so we must not say "will not collapse at any
            // distance".
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
