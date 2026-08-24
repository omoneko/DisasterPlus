using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 実機報告（2026-08-22）「爆発のエフェクトがスケール通りではない、
    /// 特に破局噴火の時の爆発がしょぼすぎます」。
    ///
    /// ★★ <b>ここが固定するのは「大きい火山ほど発が増える」ことである。</b>
    ///    以前は <c>DispatchEffect</c> 1 回きりで、スライダーを上げても
    ///    <c>SpawnArea</c> の半径が広がるだけだった —— <b>粒は大きくならないので、
    ///    同じ大きさの粒が薄く散る</b>（＝しょぼくなる）。
    /// </summary>
    public class BlastClusterTests
    {
        private const float Crater = 351f;   // 成層 25.5 の火口
        private const uint Seed = 4242u;

        [Fact]
        public void ABiggerVolcanoGetsMoreBursts()
        {
            int small = BlastCluster.CountFor(1f, 0f, false);
            int big = BlastCluster.CountFor(1f, 1f, false);

            Assert.True(big > small * 2,
                        "the biggest volcano only gets " + big + " bursts against " + small);
        }

        [Fact]
        public void TheSupereruptionIsOverwhelminglyBiggerThanAnyNormalOne()
        {
            // ★ 「破局噴火の爆発がしょぼすぎる」への答え。
            int normal = BlastCluster.CountFor(1f, 1f, false);
            int climax = BlastCluster.CountFor(1f, 1f, true);

            Assert.True(climax > normal * 2,
                        "the supereruption (" + climax + ") is not overwhelmingly bigger than "
                        + "a normal one (" + normal + ")");

            // ★ いちばん小さい火山の 10 倍以上は出ること（「しょぼい」の反対側）。
            Assert.True(climax > BlastCluster.CountFor(1f, 0f, false) * 10,
                        "the supereruption is only " + climax + " bursts");
        }

        [Fact]
        public void TheCountNeverRunsAway()
        {
            foreach (float u in new[] { -1f, 0f, 0.5f, 1f, 9f, float.NaN })
            {
                foreach (float s in new[] { -1f, 0f, 1f, 9f, float.NaN })
                {
                    foreach (bool c in new[] { false, true })
                    {
                        int n = BlastCluster.CountFor(u, s, c);
                        Assert.InRange(n, BlastCluster.MinBursts, BlastCluster.MaxBursts);
                    }
                }
            }
        }

        [Fact]
        public void TheBurstsDoNotAllLandOnTheSameSpotOrTheSameFrame()
        {
            // 同じ場所・同じフレームに積むと、大きい爆発ではなく
            // **1 個の平たい円盤**に見える。
            int count = BlastCluster.CountFor(1f, 1f, false);

            float firstX = 0f, firstZ = 0f;
            int firstDelay = -1;
            bool movedSomewhere = false, movedInTime = false;

            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);
                if (i == 0) { firstX = b.OffsetX; firstZ = b.OffsetZ; firstDelay = b.DelayFrames; }
                else
                {
                    if (b.OffsetX != firstX || b.OffsetZ != firstZ) movedSomewhere = true;
                    if (b.DelayFrames != firstDelay) movedInTime = true;
                }
            }

            Assert.True(movedSomewhere, "every burst landed on the same spot");
            Assert.True(movedInTime, "every burst landed on the same frame");
        }

        [Fact]
        public void NormalEruptionsStayInsideTheCrater()
        {
            int count = BlastCluster.CountFor(1f, 1f, false);

            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 4000f, Seed);
                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX + b.OffsetZ * b.OffsetZ);

                // 環（4000 m）を渡しても、**大爆発でなければ使わない。**
                Assert.True(d <= Crater * BlastCluster.SpreadRatio + 1f,
                            "a normal eruption threw a burst " + d + " m from the vent");
            }
        }

        [Fact]
        public void TheSupereruptionAlsoEruptsFromTheRingFissure()
        {
            // ★★ カルデラのふちで噴かないと、半径 5 km の穴のどこにも爆発が見えない。
            const float ring = 5563f;
            int count = BlastCluster.CountFor(1f, 1f, true);

            int onRing = 0;
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, true, Crater, ring, Seed);
                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX + b.OffsetZ * b.OffsetZ);
                if (d > Crater * 2f) onRing++;
            }

            Assert.True(onRing > 0, "no burst reached the ring fissure");
            Assert.True(onRing < count, "every burst went to the ring; the central vent is dead");
        }

        [Fact]
        public void NoBurstEverEscapesTheRing()
        {
            const float ring = 5563f;
            int count = BlastCluster.CountFor(1f, 1f, true);

            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, true, Crater, ring, Seed);
                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX + b.OffsetZ * b.OffsetZ);

                Assert.True(d <= ring * (1f + BlastCluster.RingJitterRatio) + 1f,
                            "a burst landed " + d + " m out, past the ring at " + ring);
            }
        }

        [Fact]
        public void EveryBurstIsUsableNoMatterWhatComesIn()
        {
            foreach (float crater in new[] { -5f, 0f, float.NaN, 351f })
            {
                foreach (float ring in new[] { -5f, 0f, float.NaN, 5563f })
                {
                    BlastBurst b = BlastCluster.For(0, 5, 1f, 1f, true, crater, ring, Seed);

                    Assert.True(b.RadiusMetres >= EruptionEffectPlan.MinRadiusMetres);
                    Assert.False(float.IsNaN(b.OffsetX));
                    Assert.False(float.IsNaN(b.OffsetY));
                    Assert.False(float.IsNaN(b.OffsetZ));
                    Assert.False(float.IsNaN(b.Magnitude));
                    Assert.InRange(b.DelayFrames, 0, BlastCluster.SpreadFrames);
                }
            }
        }

        [Fact]
        public void AnOutOfRangeIndexStillReturnsARealBurst()
        {
            BlastBurst b = BlastCluster.For(999, 5, 1f, 1f, false, Crater, 0f, Seed);
            Assert.True(b.RadiusMetres > 0f);
            Assert.True(b.Magnitude > 0f);
        }

        [Fact]
        public void TheSizeUnitIsZeroAtTheRecommendedSizeAndOneAtTheTop()
        {
            Assert.Equal(0f, BlastCluster.SizeUnitOf(1200f, 1200f, 5600f), 4);
            Assert.Equal(1f, BlastCluster.SizeUnitOf(5600f, 1200f, 5600f), 4);
            Assert.InRange(BlastCluster.SizeUnitOf(3400f, 1200f, 5600f), 0.4f, 0.6f);

            // 帯が壊れていても 0（＝「大きくない」）へ落ちる。
            Assert.Equal(0f, BlastCluster.SizeUnitOf(9000f, 5600f, 1200f), 4);
            Assert.Equal(0f, BlastCluster.SizeUnitOf(float.NaN, 1200f, 5600f), 4);
        }

        [Fact]
        public void TheClimaxIsDenserPerBurstNotJustMoreNumerous()
        {
            // 発を増やしたぶん 1 発を薄くしたら、増やした意味が打ち消される。
            BlastBurst normal = BlastCluster.For(0, 10, 1f, 1f, false, Crater, 0f, Seed);
            BlastBurst climax = BlastCluster.For(0, 10, 1f, 1f, true, Crater, 0f, Seed);

            Assert.True(climax.Magnitude > normal.Magnitude,
                        "the climax burst is no denser: " + climax.Magnitude
                        + " against " + normal.Magnitude);
        }
    }
}
