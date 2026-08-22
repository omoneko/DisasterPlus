using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 火山性地震。**固定するのは「切れ目が無いこと」「噴火で最大になること」
    /// 「距離で消えること」「折り返さないこと」**である。
    /// </summary>
    public class VolcanicTremorTests
    {
        private const uint Seed = 0x1A2B3C4Du;

        [Fact]
        public void TheActivityRisesBeforeTheEruptionPeaksDuringItAndDiesAwayAfter()
        {
            // 火山性地震の看板そのもの。**噴火の前から揺れていること**が要点である。
            float early = VolcanicTremor.ActivityUnit(0.05f, false, 0f, false, 0f);
            float late = VolcanicTremor.ActivityUnit(0.95f, false, 0f, false, 0f);
            float erupting = VolcanicTremor.ActivityUnit(1f, true, 1f, false, 0f);
            float afterEarly = VolcanicTremor.ActivityUnit(1f, false, 0f, true, 0.1f);
            float afterLate = VolcanicTremor.ActivityUnit(1f, false, 0f, true, 0.95f);

            Assert.True(early > 0f, "the volcano is silent while the magma rises");
            Assert.True(late > early, "the swarm does not build up during the uplift");
            Assert.True(erupting > late, "the eruption is not the peak");
            Assert.True(afterEarly < erupting && afterEarly > afterLate,
                "the tremor does not die away after the eruption");
            Assert.True(afterLate >= 0f);
        }

        [Fact]
        public void ZeroActivityIsExactlyStill()
        {
            // **切ったら 1 mm も揺れない。** 「弱く揺れる」ではない。
            for (float t = 0f; t < 20f; t += 0.13f)
            {
                Assert.Equal(0f, VolcanicTremor.DisplacementAt(Seed, t, 0f));
                Assert.Equal(0f, VolcanicTremor.TremorAt(Seed, t, 0f));
            }
        }

        [Fact]
        public void TheTremorIsContinuousAndNeverStopsWhileTheVolcanoIsActive()
        {
            // ★ 火山性微動の定義そのもの —— **切れ目が無い。**
            //   1 秒ごとの窓を取って、どの窓にも動きが在ることを見る。
            const float activity = 0.6f;
            for (float start = 0f; start < 120f; start += 1f)
            {
                float peak = 0f;
                for (float t = start; t < start + 1f; t += 1f / 60f)
                {
                    float v = Math.Abs(VolcanicTremor.DisplacementAt(Seed, t, activity));
                    if (v > peak) peak = v;
                }
                Assert.True(peak > 0.01f,
                    "the ground is completely still in the window at " + start + " s");
            }
        }

        [Fact]
        public void StrongerActivityShakesHarder()
        {
            Assert.True(Rms(0.9f) > Rms(0.3f), "activity does not drive the amplitude");
            Assert.True(Rms(0.3f) > Rms(0.1f));
        }

        [Fact]
        public void MostEventsAreSmallAndBigOnesAreRare()
        {
            // 規模別頻度の向き（Gutenberg–Richter と同じ向き。マグニチュードではない）。
            int small = 0, big = 0;
            for (int slot = 0; slot < 600; slot++)
            {
                float m = VolcanicTremor.MagnitudeUnit(Seed, slot, 1f);
                if (m < 0.2f) small++;
                if (m > 0.6f) big++;
            }
            Assert.True(small > big * 4,
                "big events are not rare: " + small + " small vs " + big + " big");
            Assert.True(big > 0, "no big event ever happens");
        }

        [Fact]
        public void TheWaveformNeverLeavesTheUnitBand()
        {
            for (float t = 0f; t < 200f; t += 1f / 60f)
            {
                float v = VolcanicTremor.DisplacementAt(Seed, t, 1f);
                Assert.False(float.IsNaN(v));
                Assert.True(v >= -1f && v <= 1f, "displacement " + v + " at " + t);
            }
        }

        [Fact]
        public void NothingFasterThanSixSamplesPerCycleIsUsed()
        {
            // ★ カメラの揺れは描画フレームごと（60 fps ⇒ ナイキスト 30 Hz）に評価する。
            //   いちばん速い成分でも 1 周期あたり 6 点は要る。
            const float renderHz = 60f;
            Assert.True(renderHz / VolcanicTremor.EventFastHz >= 6f,
                "the event carrier aliases at 60 fps");
            Assert.True(renderHz / VolcanicTremor.TremorHz >= 6f,
                "the tremor carrier aliases at 60 fps");
        }

        [Fact]
        public void TheShakeStopsAtTheDeclaredReach()
        {
            const float reach = 5000f;
            Assert.Equal(1f, VolcanicTremor.AttenuationAt(0f, reach), 3);
            Assert.True(VolcanicTremor.AttenuationAt(1000f, reach)
                        > VolcanicTremor.AttenuationAt(3000f, reach));

            // **外はきっかり 0。** 街の反対側は揺れない。
            Assert.Equal(0f, VolcanicTremor.AttenuationAt(reach, reach));
            Assert.Equal(0f, VolcanicTremor.AttenuationAt(reach * 2f, reach));
        }

        [Fact]
        public void BadInputIsZeroAndNeverNaN()
        {
            foreach (float bad in new float[] { float.NaN, float.PositiveInfinity, -1f })
            {
                Assert.Equal(0f, VolcanicTremor.DisplacementAt(Seed, bad, 1f));
                Assert.Equal(0f, VolcanicTremor.DisplacementAt(Seed, 10f, bad));
                Assert.Equal(0f, VolcanicTremor.AttenuationAt(bad, 1000f));
                Assert.Equal(0f, VolcanicTremor.AttenuationAt(100f, bad));
                Assert.False(float.IsNaN(VolcanicTremor.ActivityUnit(bad, false, bad, false, bad)));
            }

            Assert.Equal(0f, VolcanicTremor.EventAt(-1f, 1f));
            Assert.Equal(0f, VolcanicTremor.EventAt(1f, 0f));
            Assert.Equal(0f, VolcanicTremor.EventAt(float.NaN, 1f));
        }

        [Fact]
        public void TheSameVolcanoAlwaysShakesTheSameWay()
        {
            // フレーム番号を種に混ぜていないことの担保（t の閉じた式であること）。
            for (float t = 0f; t < 30f; t += 0.7f)
            {
                Assert.Equal(VolcanicTremor.DisplacementAt(Seed, t, 0.8f),
                             VolcanicTremor.DisplacementAt(Seed, t, 0.8f));
            }

            uint other = DeterministicRandom.Hash(Seed, 1u);
            bool differs = false;
            for (float t = 0f; t < 30f; t += 0.7f)
            {
                if (VolcanicTremor.DisplacementAt(Seed, t, 0.8f)
                    != VolcanicTremor.DisplacementAt(other, t, 0.8f)) differs = true;
            }
            Assert.True(differs, "two different volcanoes shake identically");
        }

        private static float Rms(float activity)
        {
            double sum = 0.0;
            int n = 0;
            for (float t = 0f; t < 60f; t += 1f / 60f)
            {
                float v = VolcanicTremor.DisplacementAt(Seed, t, activity);
                sum += v * v;
                n++;
            }
            return (float)Math.Sqrt(sum / n);
        }
    }
}
