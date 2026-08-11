using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    public class DeterministicRandomTests
    {
        [Fact]
        public void Unit_SameInputs_ReturnsSameValue()
        {
            Assert.Equal(DeterministicRandom.Unit(42u, 7u), DeterministicRandom.Unit(42u, 7u));
        }

        [Fact]
        public void Unit_IsAlwaysInZeroToOne()
        {
            for (uint tick = 0; tick < 200; tick++)
            {
                float v = DeterministicRandom.Unit(tick, tick * 3u + 1u);
                Assert.True(v >= 0f && v < 1f, "value out of range: " + v);
            }
        }

        [Fact]
        public void Unit_DifferentIds_ProduceDifferentValues()
        {
            // 同一 tick で id 違いが同じ値を返すと、全建物が一斉に発火してしまう。
            int distinct = 0;
            float first = DeterministicRandom.Unit(100u, 0u);
            for (uint id = 1; id < 64; id++)
            {
                if (DeterministicRandom.Unit(100u, id) != first) distinct++;
            }
            Assert.True(distinct >= 60, "too few distinct values: " + distinct);
        }

        [Fact]
        public void Unit_SpreadsAcrossRange()
        {
            // 4 分位すべてに値が落ちること。偏ると延焼確率が意味を失う。
            var buckets = new int[4];
            for (uint id = 0; id < 400; id++)
            {
                int b = (int)(DeterministicRandom.Unit(9u, id) * 4f);
                if (b > 3) b = 3;
                buckets[b]++;
            }
            foreach (int c in buckets) Assert.True(c > 40, "bucket too small: " + c);
        }

        [Fact]
        public void Vec2_DistanceSquared_IsCorrect()
        {
            var a = new Vec2(0f, 0f);
            var b = new Vec2(3f, 4f);
            Assert.Equal(25f, a.DistanceSquaredTo(b), 3);
        }
    }
}
