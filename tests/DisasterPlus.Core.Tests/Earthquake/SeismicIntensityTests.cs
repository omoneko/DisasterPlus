using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class SeismicIntensityTests
    {
        [Fact]
        public void RadiusMatchesTheVanillaFormula()
        {
            // IL 事実文書 §A-3: R = 2000 + m_intensity * 20
            Assert.Equal(2000f, SeismicIntensity.RadiusOf(0), 3);
            Assert.Equal(3100f, SeismicIntensity.RadiusOf(55), 3);   // バニラ既定
            Assert.Equal(4000f, SeismicIntensity.RadiusOf(100), 3);  // バニラのランダム発生上限
            Assert.Equal(7100f, SeismicIntensity.RadiusOf(255), 3);  // 本 MOD が解放した上限
        }

        [Fact]
        public void AtEpicentre_IsOne()
        {
            Assert.Equal(1f, SeismicIntensity.At(0f, 55), 4);
        }

        [Fact]
        public void AtTheRadius_IsZero()
        {
            Assert.Equal(0f, SeismicIntensity.At(SeismicIntensity.RadiusOf(55), 55), 4);
        }

        [Fact]
        public void HalfwayOut_IsAHalf()
        {
            // 線形ランプであることを固定する。2 次にしたり滑らかにしたりしない。
            // バニラが倒壊判定に使っているのはこの直線そのもの。
            Assert.Equal(0.5f, SeismicIntensity.At(1550f, 55), 4);
        }

        [Fact]
        public void BeyondTheRadius_IsZeroAndOutside()
        {
            // R の外はバニラが preRadius で先に弾くので、判定自体が起きない。
            // 0 を返すが、呼び出し側はこれを「揺れていない」ではなく「圏外」と表示すること。
            Assert.Equal(0f, SeismicIntensity.At(9999f, 55), 4);
            Assert.False(SeismicIntensity.IsInside(9999f, 55));
            Assert.True(SeismicIntensity.IsInside(0f, 55));
            Assert.False(SeismicIntensity.IsInside(SeismicIntensity.RadiusOf(55), 55));
        }

        [Fact]
        public void IsMonotonicallyDecreasingWithDistance()
        {
            float prev = 2f;
            for (float d = 0f; d < 3200f; d += 10f)
            {
                float s = SeismicIntensity.At(d, 55);
                Assert.True(s <= prev, "s increased at " + d);
                Assert.InRange(s, 0f, 1f);
                prev = s;
            }
        }

        [Fact]
        public void GarbageInput_IsZeroNotNaN()
        {
            // 壊れた読み取りで「強度 NaN」を表示しない。
            Assert.Equal(0f, SeismicIntensity.At(float.NaN, 55), 4);
            Assert.Equal(0f, SeismicIntensity.At(-1f, 55), 4);
        }
    }
}
