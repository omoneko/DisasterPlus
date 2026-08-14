using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// これは「バニラと同じ値が出るか」の最終検査ではない（それは実行時に
    /// Assumptions が本物の ColossalFramework.Math.Randomizer と突き合わせる、Task 5）。
    /// ここで固定するのは、実装が LCG の定義から外れないこと、特に
    /// 「戻り値は種を進める前に作る」という順序と、ctor の符号拡張。
    /// </summary>
    public class VanillaRandomizerTests
    {
        [Fact]
        public void Ctor_Zero_LeavesTheIncrementAlone()
        {
            // seed = M * 0 + I
            Assert.Equal(VanillaRandomizer.Increment, new VanillaRandomizer(0).Seed);
        }

        [Fact]
        public void Ctor_One_IsMultiplierPlusIncrement()
        {
            ulong expected = unchecked(VanillaRandomizer.Multiplier + VanillaRandomizer.Increment);
            Assert.Equal(expected, new VanillaRandomizer(1).Seed);
        }

        [Fact]
        public void Ctor_NegativeValue_SignExtendsToSixtyFourBits()
        {
            // IL は conv.i8（符号拡張）。ゼロ拡張で書くと別の種になり、
            // disasterID >= 0x8000 の合成種で全部ずれる。
            ulong signExtended = unchecked(VanillaRandomizer.Multiplier * ulong.MaxValue
                                           + VanillaRandomizer.Increment);
            ulong zeroExtended = unchecked(VanillaRandomizer.Multiplier * (ulong)uint.MaxValue
                                           + VanillaRandomizer.Increment);

            Assert.Equal(signExtended, new VanillaRandomizer(-1).Seed);
            Assert.NotEqual(zeroExtended, new VanillaRandomizer(-1).Seed);
        }

        [Fact]
        public void Int32_ReturnsTheValueBuiltBeforeAdvancingTheSeed()
        {
            // 順序を入れ替えると全ての引きが 1 個ずれる。実装の中で
            // いちばん壊しやすく、いちばん気付きにくい 1 行。
            var r = new VanillaRandomizer(12345);
            ulong before = r.Seed;

            int drawn = r.Int32(10000u);

            int expected = (int)(((before >> 32) * 10000UL) >> 32);
            Assert.Equal(expected, drawn);
            Assert.Equal(unchecked(VanillaRandomizer.Multiplier * before + VanillaRandomizer.Increment),
                         r.Seed);
        }

        [Fact]
        public void Int32_IsAlwaysInsideTheRequestedRange()
        {
            for (int v = -3000; v < 3000; v += 7)
            {
                var r = new VanillaRandomizer(v);
                for (int k = 0; k < 8; k++)
                {
                    int drawn = r.Int32(10000u);
                    Assert.InRange(drawn, 0, 9999);
                }
            }
        }

        [Fact]
        public void Int32_WithMaxOne_IsAlwaysZero()
        {
            for (int v = 0; v < 500; v++)
            {
                var r = new VanillaRandomizer(v);
                Assert.Equal(0, r.Int32(1u));
            }
        }

        [Fact]
        public void SameSeed_ProducesTheSameSequence()
        {
            var a = new VanillaRandomizer(777);
            var b = new VanillaRandomizer(777);
            for (int k = 0; k < 32; k++)
            {
                Assert.Equal(a.Int32(10000u), b.Int32(10000u));
            }
        }

        [Fact]
        public void GoldenSequence_MatchesTheLcgDefinitionStepByStep()
        {
            // 実装の内部構造に依存せず、LCG の定義だけから期待値を組み立てて突き合わせる。
            // 実装を「速くする」書き換えで黙って別物にならないための固定。
            ulong seed = unchecked(VanillaRandomizer.Multiplier * 4242UL + VanillaRandomizer.Increment);
            var r = new VanillaRandomizer(4242);

            for (int k = 0; k < 16; k++)
            {
                int expected = (int)(((seed >> 32) * 255UL) >> 32);
                seed = unchecked(VanillaRandomizer.Multiplier * seed + VanillaRandomizer.Increment);
                Assert.Equal(expected, r.Int32(255u));
            }
        }
    }
}
