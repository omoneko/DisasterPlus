namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Makes a reproducible random number out of (tick, id).
    /// Use System.Random and you will not get the same result across a save, a load and a
    /// unit test.
    ///
    /// **This is a different thing from Core/Earthquake/VanillaRandomizer. Do not confuse
    /// the two.** This one is a stateless hash, used for **things this mod decides for
    /// itself** (fire spread selection in ③, second-layer damage selection in ②). Its
    /// innards are this mod's own business, so they may be changed. The other one is a
    /// bit-for-bit copy of the game's ColossalFramework.Math.Randomizer, exists solely to
    /// **predict the values vanilla is about to draw**, and must not be changed by a
    /// single bit.
    ///
    /// The rule for telling them apart:
    ///   if the number decides "a judgement this mod invented", it is DeterministicRandom;
    ///   if it "has to match the value vanilla draws", it is VanillaRandomizer.
    /// </summary>
    public static class DeterministicRandom
    {
        /// <summary>A 32-bit mixing function (MurmurHash3's finalizer extended to two
        /// inputs).</summary>
        public static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u;
                h ^= b + 0x85EBCA6Bu + (h << 6) + (h >> 2);
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>A uniform random number in [0, 1).</summary>
        public static float Unit(uint a, uint b)
        {
            // Use the top 24 bits. A float's mantissa is 24 bits, so taking any more buys
            // no extra precision.
            return (Hash(a, b) >> 8) * (1.0f / 16777216.0f);
        }
    }
}
