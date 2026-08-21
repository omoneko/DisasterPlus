using System;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 噴火柱（実機の指摘③「噴煙がただの煙だまりになってしまっています」）。
    ///
    /// ここで固定するのは、**キノコ雲ではなく噴火柱である**ことを決めている 5 つ:
    ///   1. 柱は火口から傘まで**途切れずに積み上がる**（段が隙間なく並ぶ）
    ///   2. 上へ行くほど**太る**（巻き込み）。傘はそれよりずっと広い
    ///   3. 上昇は中立浮力高度で**止まる**（傘では 0）
    ///   4. 風下へ**倒れる**。倒れ方は高いほど強い
    ///   5. 粒子の総量は今までの噴煙 1 回ぶんと同じ（面積で正規化してある）
    /// </summary>
    public class EruptionColumnTests
    {
        private const float Vent = 144f;

        private static EruptionColumn Default(float unit)
        {
            return new EruptionColumn(Vent, unit, 1f, 0f,
                                      EruptionColumn.ReferenceWindMetresPerSecond);
        }

        [Fact]
        public void TheColumnIsContinuousFromTheVentToTheUmbrella()
        {
            EruptionColumn c = Default(1f);
            Assert.Equal(EruptionColumn.MaxSegments, c.SegmentCount);

            float expected = 0f;
            for (int i = 0; i < EruptionColumn.ColumnSegments; i++)
            {
                EruptionColumnSegment s = c.SegmentAt(i);
                Assert.False(s.Umbrella);
                Assert.Equal(expected, s.OffsetY, 2);
                Assert.True(s.HalfHeightMetres > 0f, "segment " + i + " has no height");
                expected += s.HalfHeightMetres;
            }

            // 最後の柱の段の天面が、そのまま傘の下端である。
            Assert.Equal(c.UmbrellaBaseMetres, expected, 1);

            // 傘は天面が柱の高さにちょうど届く。
            EruptionColumnSegment core = c.SegmentAt(EruptionColumn.ColumnSegments);
            Assert.True(core.Umbrella);
            Assert.Equal(c.HeightMetres, core.OffsetY + core.HalfHeightMetres, 1);
        }

        [Fact]
        public void TheColumnWidensWithHeightAndTheUmbrellaIsWiderStill()
        {
            EruptionColumn c = Default(1f);

            float previous = 0f;
            for (float z = 0f; z <= c.UmbrellaBaseMetres; z += 20f)
            {
                float r = c.RadiusAt(z);
                Assert.True(r >= previous - 0.001f, "the column got narrower at " + z);
                previous = r;
            }

            float top = c.RadiusAt(c.UmbrellaBaseMetres);
            Assert.True(c.UmbrellaRadiusMetres > top * 2f,
                "the umbrella is not much wider than the column");

            // 噴出口では火口いっぱいには噴かない（口の内側から立ち上がる）。
            Assert.True(c.RadiusAt(0f) < Vent);
        }

        [Fact]
        public void TheRiseStopsAtTheNeutralBuoyancyHeight()
        {
            EruptionColumn c = Default(1f);

            Assert.True(c.RiseAt(0f) > c.RiseAt(c.UmbrellaBaseMetres * 0.5f));
            Assert.True(c.RiseAt(c.UmbrellaBaseMetres * 0.5f) > 0f);
            Assert.Equal(0f, c.RiseAt(c.UmbrellaBaseMetres), 4);
            Assert.Equal(0f, c.RiseAt(c.HeightMetres), 4);

            // 傘の本体は上がらない。風下の 2 つは灰が落ちるので沈む。
            Assert.Equal(0f, c.SegmentAt(EruptionColumn.ColumnSegments).DriftY, 4);
            Assert.True(c.SegmentAt(EruptionColumn.ColumnSegments + 1).DriftY < 0f);
            Assert.True(c.SegmentAt(EruptionColumn.ColumnSegments + 2).DriftY
                        < c.SegmentAt(EruptionColumn.ColumnSegments + 1).DriftY);
        }

        [Fact]
        public void TheColumnBendsDownwindAndTheUmbrellaTrailsFurther()
        {
            EruptionColumn c = Default(1f);

            // 風は +X。倒れ方は高いほど強い。
            Assert.Equal(0f, c.BendAt(0f), 4);
            Assert.True(c.BendAt(c.HeightMetres * 0.5f) > 0f);
            Assert.True(c.BendAt(c.HeightMetres) > c.BendAt(c.HeightMetres * 0.5f) * 2f);

            float lower = c.SegmentAt(0).OffsetX;
            float upper = c.SegmentAt(EruptionColumn.ColumnSegments - 1).OffsetX;
            Assert.True(upper > lower);

            // 風下側の傘は、本体より遠くて低い（＝底から灰が落ちる側）。
            EruptionColumnSegment core = c.SegmentAt(EruptionColumn.ColumnSegments);
            EruptionColumnSegment tail = c.SegmentAt(EruptionColumn.ColumnSegments + 2);
            Assert.True(tail.OffsetX > core.OffsetX);
            Assert.True(tail.OffsetY < core.OffsetY);
            Assert.True(tail.Magnitude * tail.RadiusMetres * tail.RadiusMetres
                        < core.Magnitude * core.RadiusMetres * core.RadiusMetres,
                "the fallout tail should be thinner than the umbrella itself");

            // 無風なら 1 mm も倒れない。
            var calm = new EruptionColumn(Vent, 1f, 0f, 0f, 0f);
            Assert.Equal(0f, calm.BendMetres, 4);
            for (int i = 0; i < calm.SegmentCount; i++)
            {
                Assert.Equal(0f, calm.SegmentAt(i).DriftX, 4);
                Assert.Equal(0f, calm.SegmentAt(i).DriftZ, 4);
            }
        }

        [Fact]
        public void TheParticleBudgetIsTheSameAsTheOldSinglePlume()
        {
            // ★ 粒子数は max(100, PI r^2) x dt x magnitude x 0.01 x rate（IL §B-4）なので、
            //   r^2 x magnitude が「その段の粒子数」に比例する。全段の和が
            //   従来の 1 回ぶん（refRadius^2 x PlumeMagnitude）に一致すること。
            for (float unit = 0f; unit <= 1.0001f; unit += 0.25f)
            {
                EruptionColumn c = Default(unit);

                float total = 0f;
                for (int i = 0; i < c.SegmentCount; i++)
                {
                    EruptionColumnSegment s = c.SegmentAt(i);
                    total += s.Magnitude * s.RadiusMetres * s.RadiusMetres;
                }

                float refRadius = EruptionEffectPlan.PlumeRadiusMetres(Vent, unit);
                float budget = refRadius * refRadius * EruptionEffectPlan.PlumeMagnitude(unit);

                Assert.InRange(total, budget * 0.98f, budget * 1.02f);
            }
        }

        [Fact]
        public void TheColumnGrowsWithTheEruptionIntensity()
        {
            EruptionColumn weak = Default(0f);
            EruptionColumn strong = Default(1f);

            Assert.True(strong.HeightMetres > weak.HeightMetres * 2f);
            Assert.True(strong.UmbrellaRadiusMetres > weak.UmbrellaRadiusMetres);
            Assert.True(strong.BendMetres > weak.BendMetres);

            // 火口が大きいほど柱も高い（小さい山に巨大な柱は乗らない）。
            var small = new EruptionColumn(40f, 1f, 1f, 0f, 12f);
            Assert.True(small.HeightMetres < strong.HeightMetres);
        }

        [Fact]
        public void TheSwayIsOneSlowSineAndNothingElse()
        {
            // **点滅ではなくゆらぎ**。振れ幅は宣言どおりで、周期でちょうど戻る。
            Assert.Equal(0f, EruptionColumn.SwayAt(0f), 4);
            Assert.Equal(0f, EruptionColumn.SwayAt(EruptionColumn.SwaySeconds), 3);
            Assert.Equal(EruptionColumn.SwayRadians,
                         EruptionColumn.SwayAt(EruptionColumn.SwaySeconds * 0.25f), 3);

            for (float t = 0f; t < 200f; t += 0.37f)
            {
                float v = EruptionColumn.SwayAt(t);
                Assert.InRange(v, -EruptionColumn.SwayRadians - 0.001f,
                               EruptionColumn.SwayRadians + 0.001f);
            }

            Assert.Equal(0f, EruptionColumn.SwayAt(float.NaN), 4);
        }

        [Fact]
        public void GarbageInputIsNeverNaN()
        {
            var columns = new[]
            {
                new EruptionColumn(float.NaN, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(0f, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(-10f, 0.5f, 1f, 0f, 10f),
                new EruptionColumn(Vent, float.NaN, 1f, 0f, 10f),
                new EruptionColumn(Vent, 9f, 1f, 0f, 10f),
                new EruptionColumn(Vent, -9f, 1f, 0f, 10f),
                new EruptionColumn(Vent, 0.5f, float.NaN, float.NaN, 10f),
                new EruptionColumn(Vent, 0.5f, 0f, 0f, float.NaN),
                new EruptionColumn(Vent, 0.5f, 1f, 0f, -50f),
                new EruptionColumn(Vent, 0.5f, 1f, 0f, 9999f),
            };

            for (int c = 0; c < columns.Length; c++)
            {
                EruptionColumn column = columns[c];
                Assert.False(Bad(column.HeightMetres));
                Assert.False(Bad(column.UmbrellaRadiusMetres));
                Assert.False(Bad(column.BendMetres));

                for (int i = -2; i < column.SegmentCount + 2; i++)
                {
                    EruptionColumnSegment s = column.SegmentAt(i);
                    Assert.False(Bad(s.OffsetX) || Bad(s.OffsetY) || Bad(s.OffsetZ),
                                 "segment " + i + " of column " + c + " has a bad offset");
                    Assert.False(Bad(s.RadiusMetres) || Bad(s.HalfHeightMetres));
                    Assert.False(Bad(s.DriftX) || Bad(s.DriftY) || Bad(s.DriftZ));
                    Assert.False(Bad(s.Magnitude));

                    Assert.True(s.RadiusMetres >= EruptionColumn.MinRadiusMetres);
                    Assert.True(s.HalfHeightMetres >= 0f);
                    Assert.True(s.Magnitude >= 0f);

                    // 範囲外は「密度 0 の段」であって、例外でも作り話でもない。
                    if (i < 0 || i >= column.SegmentCount) Assert.Equal(0f, s.Magnitude, 4);
                }
            }
        }

        private static bool Bad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
