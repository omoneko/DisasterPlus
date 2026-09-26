using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    /// <summary>
    /// Pins down the side that reports "why no fire whirl is appearing".
    ///
    /// Feature no. 3 has no path other than spontaneous formation, so **the default state is
    /// "nothing happens"**. Unless we can tell "the conditions are not met" apart from
    /// "it is broken" in that state, an in-game test can confirm nothing at all
    /// (which is exactly what happened once).
    /// </summary>
    public class FireWhirlProspectTests
    {
        private static FireWhirlConfig Config(float radius = 150f, int count = 12, float sep = 300f)
        {
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = radius;
            c.DetectCount = count;
            c.MinSeparation = sep;
            return c;
        }

        private static List<BurningBuilding> Cluster(int n, Vec2 center, float r, ushort startId)
        {
            var list = new List<BurningBuilding>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * System.Math.PI * i / n;
                var p = new Vec2(
                    center.X + (float)System.Math.Cos(a) * r,
                    center.Z + (float)System.Math.Sin(a) * r);
                list.Add(new BurningBuilding((ushort)(startId + i), p));
            }
            return list;
        }

        [Fact]
        public void NothingBurning_SaysSoAndNamesTheRequirement()
        {
            FireWhirlProspect p;
            FireWhirlDetector.Detect(new List<BurningBuilding>(), Config(), new List<Vec2>(), out p);

            Assert.Equal(0, p.BurningCount);
            Assert.Equal(0, p.DensestCount);
            Assert.Equal(12, p.RequiredCount);
            Assert.Equal(12, p.Shortfall);

            string text = p.Describe();
            Assert.Contains("nothing is on fire", text);
            // Without naming the threshold the tester has no way of knowing "how much do I
            // have to set alight".
            Assert.Contains("12", text);
            Assert.Contains("150", text);
        }

        [Fact]
        public void TooFewBurning_CountsTheDensestGroupAndTheShortfall()
        {
            var burning = Cluster(7, new Vec2(1000f, 1000f), 40f, 1);

            FireWhirlProspect p;
            FireWhirlDetector.Detect(burning, Config(), new List<Vec2>(), out p);

            Assert.Equal(7, p.BurningCount);
            Assert.Equal(7, p.DensestCount);
            Assert.Equal(5, p.Shortfall);
            Assert.Equal(1000f, p.DensestCentre.X, 0);

            string text = p.Describe();
            Assert.Contains("densest group", text);
            Assert.Contains("5 more", text);
        }

        [Fact]
        public void ScatteredFires_ReportDensityNotTotals()
        {
            // The total is sufficient but the density is not. **A diagnostic that reports
            // only the total becomes a lie here.**
            var burning = new List<BurningBuilding>();
            for (ushort i = 0; i < 20; i++)
                burning.Add(new BurningBuilding((ushort)(i + 1), new Vec2(i * 2000f, 0f)));

            FireWhirlProspect p;
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>(), out p);

            Assert.Empty(result);
            Assert.Equal(20, p.BurningCount);
            Assert.Equal(1, p.DensestCount);
            Assert.Contains("20 burning", p.Describe());
        }

        [Fact]
        public void SuppressedByAnExistingWhirl_SaysThatAndNotThatTheFireIsTooSmall()
        {
            var burning = Cluster(12, new Vec2(1000f, 1000f), 40f, 1);
            var existing = new List<Vec2> { new Vec2(1000f, 1000f) };

            FireWhirlProspect p;
            var result = FireWhirlDetector.Detect(burning, Config(), existing, out p);

            Assert.Empty(result);
            Assert.Equal(12, p.DensestCount);
            Assert.True(p.SuppressedCount > 0);
            Assert.Equal(0, p.AcceptedCount);

            string text = p.Describe();
            Assert.Contains("dense enough", text);
            Assert.Contains("cooling down", text);
        }

        [Fact]
        public void ConditionsMet_SaysTheCauseIsNotTheFire()
        {
            var burning = Cluster(12, new Vec2(1000f, 1000f), 40f, 1);

            FireWhirlProspect p;
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>(), out p);

            Assert.Single(result);
            Assert.Equal(1, p.AcceptedCount);
            Assert.Equal(0, p.Shortfall);
            Assert.Contains("met the conditions", p.Describe());
        }

        [Fact]
        public void MetButNothingAccepted_PointsAwayFromTheFireConditions()
        {
            // Pins down the contract of the value itself rather than the prospect Detect
            // returns. Unless we can state outright that "dense enough, not suppressed, and
            // still not a single whirl" is **not a problem on the conditions' side**, the
            // diagnostic is pointless.
            var p = new FireWhirlProspect(30, 20, new Vec2(0f, 0f), 150f, 12, 0, 0);
            Assert.Contains("not the fire conditions", p.Describe());
        }
    }
}
