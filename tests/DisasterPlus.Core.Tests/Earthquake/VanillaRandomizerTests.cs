using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// This is not the final check on "does it give the same values as vanilla" (that is
    /// done at run time, where Assumptions compares against the real
    /// ColossalFramework.Math.Randomizer —— Task 5).
    /// What is pinned down here is that the implementation does not depart from the
    /// definition of the LCG, in particular the ordering "the return value is built before
    /// the seed is advanced" and the sign extension in the constructor.
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
            // The IL is conv.i8 (sign extension). Write it as zero extension and you get a
            // different seed, and every combined seed with disasterID >= 0x8000 drifts.
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
            // Swap the order and every draw is off by one. It is the single easiest line in
            // the implementation to break and the hardest to notice.
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
            // Build the expected values from the definition of the LCG alone, independently
            // of the implementation's internals, and compare. This pins it down so that a
            // rewrite to "make it faster" cannot silently turn it into something else.
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
