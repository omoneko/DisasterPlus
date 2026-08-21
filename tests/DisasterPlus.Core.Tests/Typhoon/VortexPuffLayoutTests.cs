using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class VortexPuffLayoutTests
    {
        [Fact]
        public void TheEyeStaysAHole()
        {
            // ★★ 「粒の中心を眼の外に置く」だけでは足りない。1 粒は円盤の半径ぶん
            //   ばらまかれ、粒径の半分だけ外へ広がる。旧実装はその約束を Game 側に
            //   預けていて（＝テストで捕まらず）、実際に 1 度眼を埋めた。
            //   いまは円盤半径も粒径も **Core が宣言している**ので、ここで固定できる。
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float clearance = VortexPuffLayout.EyeClearanceOf(i);
                Assert.True(clearance >= -1e-5f,
                            "puff " + i + " reaches into the eye (clearance=" + clearance + ")");
            }
        }

        [Fact]
        public void TheEyeIsBigEnoughToRead()
        {
            // 穴が粒 1 つより小さいと、そもそも穴として見えない。
            Assert.True(VortexPuffLayout.EyeFraction
                        >= VortexPuffLayout.TowerSizeFraction * 0.5f);
            // 眼の壁雲が外周の半分より外にあると、渦ではなく輪に見える。
            Assert.True(VortexPuffLayout.EyeWallFraction < 0.5f);
            // 壁雲は眼より外にあること。
            Assert.True(VortexPuffLayout.EyeWallFraction > VortexPuffLayout.EyeFraction);
        }

        [Fact]
        public void EveryPuffIsInsideTheDrawableRanges()
        {
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff p = VortexPuffLayout.PuffAt(i);

                Assert.InRange(p.AngleRadians, 0f, 6.2831854f);
                Assert.InRange(p.HeightFraction, 0f, 1f);
                Assert.InRange(p.DiscFraction, 0f, 1f);
                Assert.InRange(p.BandFraction, 0f, 1f);
                Assert.InRange(p.DensityFraction, 0f, 1f);
                Assert.InRange(p.SwirlFraction, 0f, 1f);
                Assert.InRange(p.RiseFraction, 0f, 1f);
                Assert.InRange(p.RadialFraction, -1f, 1f);

                // 揺らぎと段の下駄のぶんだけ外周をはみ出しうるが、際限は無い。
                Assert.True(p.RadiusFraction
                            <= 1f + VortexPuffLayout.RadiusJitterFraction + 0.07f);
            }
        }

        [Fact]
        public void TheLayoutIsTheSameEveryTime()
        {
            // 添字だけの関数。毎フレーム同じ形にならないと渦が沸騰して見える。
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                VortexPuff a = VortexPuffLayout.PuffAt(i);
                VortexPuff b = VortexPuffLayout.PuffAt(i);

                Assert.Equal(a.AngleRadians, b.AngleRadians, 6);
                Assert.Equal(a.RadiusFraction, b.RadiusFraction, 6);
                Assert.Equal(a.HeightFraction, b.HeightFraction, 6);
                Assert.Equal(a.DensityFraction, b.DensityFraction, 6);
                Assert.Equal(a.Layer, b.Layer);
            }
        }

        [Fact]
        public void EveryColumnCarriesTheThreeLayersOfACumulonimbus()
        {
            // 雲底 1・塔 2・かなとこ 1。**塔が 2 段あることが「もこもこ」の実体**で、
            // 1 段に減らすと入道雲ではなく平らな雲片に戻る。
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                int deck = 0, tower = 0, canopy = 0;
                for (int level = 0; level < VortexPuffLayout.LevelsPerColumn; level++)
                {
                    VortexPuff p = VortexPuffLayout.PuffAt(
                        column * VortexPuffLayout.LevelsPerColumn + level);
                    if (p.Layer == VortexCloudLayer.Deck) deck++;
                    else if (p.Layer == VortexCloudLayer.Tower) tower++;
                    else canopy++;
                }

                Assert.Equal(1, deck);
                Assert.Equal(2, tower);
                Assert.Equal(1, canopy);
            }
        }

        [Fact]
        public void EveryColumnStandsUpFromADarkBaseToABrightTop()
        {
            // 積乱雲は鉛直に伸びる。段の順に必ず高くなること（揺らぎより刻みが大きい）。
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                float previous = -1f;
                for (int level = 0; level < VortexPuffLayout.LevelsPerColumn; level++)
                {
                    VortexPuff p = VortexPuffLayout.PuffAt(
                        column * VortexPuffLayout.LevelsPerColumn + level);
                    Assert.True(p.HeightFraction > previous,
                                "column " + column + " level " + level + " does not rise");
                    previous = p.HeightFraction;
                }
            }
        }

        [Fact]
        public void TheEyeWallIsTheTallestAndTheArmsRunOutward()
        {
            // 眼の壁雲の柱（先頭 EyeWallColumns 本）がいちばん高いこと。
            float wallTop = TopOf(0);
            for (int column = VortexPuffLayout.EyeWallColumns;
                 column < VortexPuffLayout.ColumnCount; column++)
            {
                Assert.True(TopOf(column) <= wallTop + 1e-3f,
                            "arm column " + column + " is taller than the eyewall");
            }

            // 腕は外へ伸びる（同じ腕の中で柱の半径が単調に増える）。
            for (int arm = 0; arm < VortexPuffLayout.ArmCount; arm++)
            {
                float previous = -1f;
                for (int step = 0; step < VortexPuffLayout.ColumnsPerArm; step++)
                {
                    int column = VortexPuffLayout.EyeWallColumns
                                 + arm * VortexPuffLayout.ColumnsPerArm + step;
                    // 塔下の段（段 1）は下駄が 0 なので柱の半径そのものが読める。
                    float r = VortexPuffLayout
                        .PuffAt(column * VortexPuffLayout.LevelsPerColumn + 1).RadiusFraction;
                    Assert.True(r > previous);
                    previous = r;
                }
            }
        }

        [Fact]
        public void TheLowLevelInflowTurnsIntoHighLevelOutflow()
        {
            // 台風の二次循環。下層は吸い込み、かなとこは吹き出す。
            // ここが逆だと「渦が外へ散っていく」ように見える。
            for (int column = 0; column < VortexPuffLayout.ColumnCount; column++)
            {
                int at = column * VortexPuffLayout.LevelsPerColumn;
                Assert.True(VortexPuffLayout.PuffAt(at).RadialFraction < 0f);
                Assert.True(VortexPuffLayout.PuffAt(at + 3).RadialFraction > 0f);

                // 下層ほど速く回る（地表付近の暴風）。
                Assert.True(VortexPuffLayout.PuffAt(at).SwirlFraction
                            > VortexPuffLayout.PuffAt(at + 3).SwirlFraction);
            }
        }

        [Fact]
        public void TheDeckIsWiderAndDenserThanTheTowers()
        {
            // 「暗く平らな下面」は広い円盤と高い密度で出す。ここが塔より狭いと
            // 入道雲ではなく綿あめになる。
            VortexPuff deck = VortexPuffLayout.PuffAt(0);
            VortexPuff towerLow = VortexPuffLayout.PuffAt(1);
            VortexPuff canopy = VortexPuffLayout.PuffAt(3);

            Assert.True(deck.DiscFraction > towerLow.DiscFraction);
            // 「下は平ら・上はもこもこ」＝ 雲底の段は薄く、塔の段は厚い。
            Assert.True(deck.BandFraction < towerLow.BandFraction * 0.5f);
            Assert.True(deck.DensityFraction > canopy.DensityFraction);
            Assert.True(VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Canopy)
                        > VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Deck));
            Assert.True(VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Deck)
                        > VortexPuffLayout.SizeFractionOf(VortexCloudLayer.Tower));
        }

        [Fact]
        public void OutOfRangeIndicesDoNotThrow()
        {
            // 毎フレーム回る経路なので、数え違いでレベルロードを壊さない。
            VortexPuff a = VortexPuffLayout.PuffAt(-1);
            Assert.True(VortexPuffLayout.EyeClearanceOf(-1) >= -1e-5f);
            Assert.Equal(VortexCloudLayer.Deck, a.Layer);

            VortexPuff b = VortexPuffLayout.PuffAt(VortexPuffLayout.PuffCount + 100);
            Assert.Equal(a.RadiusFraction, b.RadiusFraction, 6);
        }

        [Fact]
        public void MagnitudeHitsTheRequestedParticleBudget()
        {
            // §B-4 の式を前へ回して、頼んだ本数がそのまま出ることを確かめる。
            const float discRadius = 120f;
            const float rate = 20f;
            const float perSecond = 600f;

            float magnitude = VortexPuffLayout.MagnitudeFor(discRadius, rate, perSecond,
                                                            VortexPuffLayout.PuffCount);

            const float timeDelta = 1f / 60f;
            float area = 3.14159265f * discRadius * discRadius;
            float pps = timeDelta * magnitude * 0.01f * rate;
            float perFramePerPuff = area * pps;
            float perSecondTotal = perFramePerPuff * VortexPuffLayout.PuffCount / timeDelta;

            Assert.Equal(perSecond, perSecondTotal, 1);
        }

        [Fact]
        public void BrokenInputsProduceNoParticlesAtAll()
        {
            // 「粒子数 NaN で空が埋まる」を作らない。
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(0f, 20f, 600f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 0f, 600f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 20f, 0f, 30), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(120f, 20f, 600f, 0), 6);
            Assert.Equal(0f, VortexPuffLayout.MagnitudeFor(float.NaN, 20f, 600f, 30), 6);
        }

        private static float TopOf(int column)
        {
            // かなとこの段の高さがそのまま柱の背の高さの代理になる（揺らぎは入る）。
            return VortexPuffLayout
                .PuffAt(column * VortexPuffLayout.LevelsPerColumn + 3).HeightFraction;
        }
    }
}
