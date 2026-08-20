using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    /// <summary>
    /// 「なぜ火災旋風が出ないのか」を出す側の固定。
    ///
    /// ③は自然発生しか経路を持たないので、**既定の状態は「何も起きない」**である。
    /// その状態で「条件が足りない」と「壊れている」を言い分けられなければ、
    /// 実機テストは何も確かめられない（実際に一度そうなった）。
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
            // 閾値を名乗らないと、テスターは「どれだけ燃やせばいいのか」を知りようがない。
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
            // 総数は足りているのに密度が足りない。**総数だけを出す診断はここで嘘になる。**
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
            // Detect が返す prospect ではなく、値そのものの契約を固定する。
            // 「密度は足りている・抑制もされていない・それでも 1 基も出ない」は
            // **条件の側の問題ではない**と言い切れなければ診断の意味が無い。
            var p = new FireWhirlProspect(30, 20, new Vec2(0f, 0f), 150f, 12, 0, 0);
            Assert.Contains("not the fire conditions", p.Describe());
        }
    }
}
