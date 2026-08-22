using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 所有者の依頼「噴火口のマグマだまり（溶岩同様光る）と噴煙への光の放射」。
    ///
    /// ★★ いちばん大事なのは**点滅しないこと**である。<c>LavaGlow</c> は一度
    ///    UV スクロールで溶岩流を点滅させて指摘を受けている。
    /// </summary>
    public class CraterGlowTests
    {
        [Fact]
        public void ThePoolNeverStrobes()
        {
            // 60 fps で 10 秒ぶん。**1 フレームあたりの変化が目に見える段にならない**こと。
            const float Step = 1f / 60f;
            float previous = CraterGlow.PoolBrightness(1f, 0f);
            float worst = 0f;

            for (int i = 1; i <= 600; i++)
            {
                float now = CraterGlow.PoolBrightness(1f, i * Step);
                float delta = now > previous ? now - previous : previous - now;
                if (delta > worst) worst = delta;
                previous = now;
            }

            // いちばん速い成分は 0.19 Hz。1 フレームの変化は 1% にも満たないはずである。
            Assert.True(worst < 0.01f, "per-frame change was " + worst);
        }

        [Fact]
        public void TheCraterIsAlreadyRedBeforeTheEruptionPeaks()
        {
            // 強さ 0 でも消えない。**火口は噴火前から赤い。**
            for (int i = 0; i < 40; i++)
            {
                Assert.True(CraterGlow.PoolBrightness(0f, i * 0.37f) > 0f);
            }
        }

        [Fact]
        public void AStrongerEruptionIsBrighterAndWider()
        {
            Assert.True(CraterGlow.PoolBrightness(1f, 3f) > CraterGlow.PoolBrightness(0.1f, 3f));
            Assert.True(CraterGlow.PoolRadiusMetres(100f, 1f)
                        > CraterGlow.PoolRadiusMetres(100f, 0f));
        }

        [Fact]
        public void ThePoolNeverSpillsOverTheCraterRim()
        {
            // 縁からあふれて見えるのは溶岩流の仕事であって、だまりの仕事ではない。
            foreach (float u in new[] { 0f, 0.5f, 1f, 5f })
            {
                Assert.True(CraterGlow.PoolRadiusMetres(100f, u) <= 100f);
            }
        }

        [Fact]
        public void TheLightFadesToExactlyZeroBeforeTheTopOfThePlume()
        {
            const float Height = 1000f;
            float reach = Height * CraterGlow.ReachFraction;

            Assert.True(CraterGlow.LightAt(0f, Height, 1f) > 0f);
            Assert.Equal(0f, CraterGlow.LightAt(reach, Height, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(Height, Height, 1f), 5);
        }

        [Fact]
        public void TheLightOnlyGetsDarkerAsItGoesUp()
        {
            const float Height = 1000f;
            float previous = float.MaxValue;

            for (int i = 0; i <= 100; i++)
            {
                float y = Height * (i / 100f);
                float k = CraterGlow.LightAt(y, Height, 1f);
                Assert.True(k <= previous + 1e-5f, "went back up at y=" + y);
                previous = k;
            }
        }

        [Fact]
        public void EverythingStaysInsideZeroToOne()
        {
            foreach (float u in new[] { -1f, 0f, 0.5f, 1f, 9f, float.NaN })
            {
                Assert.InRange(CraterGlow.PoolBrightness(u, 7f), 0f, 1f);
                Assert.InRange(CraterGlow.LightAt(50f, 900f, u), 0f, 1f);
            }
        }

        [Fact]
        public void BrokenInputDoesNotLightAnything()
        {
            Assert.Equal(0f, CraterGlow.PoolRadiusMetres(float.NaN, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(float.NaN, 900f, 1f), 5);
            Assert.Equal(0f, CraterGlow.LightAt(50f, 0f, 1f), 5);
        }
    }
}
