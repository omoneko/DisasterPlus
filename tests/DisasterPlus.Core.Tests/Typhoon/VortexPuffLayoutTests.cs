using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class VortexPuffLayoutTests
    {
        [Fact]
        public void TheEyeStaysAHole()
        {
            // 「眼を穴として読める」ことの実装は「EyeFraction より内側に 1 個も置かない」。
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float angle, radius, height, size, density;
                VortexPuffLayout.Puff(i, out angle, out radius, out height, out size, out density);
                Assert.True(radius >= VortexPuffLayout.EyeFraction,
                            "puff " + i + " sits inside the eye (r=" + radius + ")");
            }
        }

        [Fact]
        public void EveryPuffIsInsideTheDrawableRanges()
        {
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float angle, radius, height, size, density;
                VortexPuffLayout.Puff(i, out angle, out radius, out height, out size, out density);

                Assert.InRange(angle, 0f, 6.2831854f);
                Assert.InRange(height, 0f, 1f);
                Assert.InRange(size, 0f, 1f);
                Assert.InRange(density, 0f, 1f);
                // 揺らぎのぶんだけ外周をはみ出しうるが、際限は無い。
                Assert.True(radius <= 1f + VortexPuffLayout.RadiusJitterFraction + 0.001f);
            }
        }

        [Fact]
        public void TheLayoutIsTheSameEveryTime()
        {
            // 添字だけの関数。毎フレーム同じ形にならないと渦が沸騰して見える。
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float a1, r1, h1, s1, d1;
                float a2, r2, h2, s2, d2;
                VortexPuffLayout.Puff(i, out a1, out r1, out h1, out s1, out d1);
                VortexPuffLayout.Puff(i, out a2, out r2, out h2, out s2, out d2);

                Assert.Equal(a1, a2, 6);
                Assert.Equal(r1, r2, 6);
                Assert.Equal(h1, h2, 6);
                Assert.Equal(s1, s2, 6);
                Assert.Equal(d1, d2, 6);
            }
        }

        [Fact]
        public void ArmsRunOutwardAndTheEyeWallRingDoesNot()
        {
            // 腕は外へ伸びる（同じ腕の中で半径が単調に増える）。揺らぎより刻みが大きい。
            for (int arm = 0; arm < VortexPuffLayout.ArmCount; arm++)
            {
                float previous = -1f;
                for (int step = 0; step < VortexPuffLayout.PuffsPerArm; step++)
                {
                    float angle, radius, height, size, density;
                    VortexPuffLayout.Puff(arm * VortexPuffLayout.PuffsPerArm + step,
                                          out angle, out radius, out height, out size, out density);
                    Assert.True(radius > previous);
                    previous = radius;
                }
            }

            // 環は 1 本の半径の上に乗る（腕とは別の構造）。
            int ringStart = VortexPuffLayout.ArmCount * VortexPuffLayout.PuffsPerArm;
            for (int k = 0; k < VortexPuffLayout.EyeWallPuffs; k++)
            {
                float angle, radius, height, size, density;
                VortexPuffLayout.Puff(ringStart + k,
                                      out angle, out radius, out height, out size, out density);
                Assert.InRange(radius,
                               VortexPuffLayout.EyeWallFraction - VortexPuffLayout.RadiusJitterFraction,
                               VortexPuffLayout.EyeWallFraction + VortexPuffLayout.RadiusJitterFraction);
                Assert.Equal(1f, height, 6);
            }
        }

        [Fact]
        public void OutOfRangeIndicesDoNotThrow()
        {
            // 毎フレーム回る経路なので、数え違いでレベルロードを壊さない。
            float angle, radius, height, size, density;
            VortexPuffLayout.Puff(-1, out angle, out radius, out height, out size, out density);
            Assert.True(radius >= VortexPuffLayout.EyeFraction);

            VortexPuffLayout.Puff(VortexPuffLayout.PuffCount + 100,
                                  out angle, out radius, out height, out size, out density);
            Assert.True(radius >= VortexPuffLayout.EyeFraction);
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
    }
}
