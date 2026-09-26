namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// A **bit-for-bit copy** of the game's <c>ColossalFramework.Math.Randomizer</c>
    /// (from ColossalManaged.dll, not Assembly-CSharp). Appendix A-1 of the design document
    /// is the sole authority, and it carries the IL verbatim. **Do not rewrite it from
    /// guesswork.**
    ///
    /// Why we need a copy: DisasterHelpers.DestroyBuildings builds a
    /// <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> for each building and
    /// draws one collapse threshold and one ignition threshold from it (§A-3 of the IL facts
    /// document). That seed depends on neither the frame nor the step, so **by rebuilding
    /// the same seed we can know in advance the values vanilla is about to draw**. The heart
    /// of ②'s first layer — stating "this building will fall if it is within X m of the
    /// epicentre" as fact rather than prophecy — all rests on this reproduction. One bit out
    /// of place and every number stays plausible while all of it is a lie.
    ///
    /// **This is a different thing from Core/Common/DeterministicRandom. Do not delete
    /// either one.**
    ///   - If the number decides "a judgement this mod invented", use DeterministicRandom.
    ///   - If it "must match the value vanilla draws", use VanillaRandomizer.
    ///
    /// Implementation notes:
    ///   - The multiplication always overflows 64 bits. Spell <c>unchecked</c> out (it is
    ///     C#'s default, but this keeps it working if CheckForOverflowUnderflow is ever
    ///     turned on).
    ///   - The return value is produced **before** the seed advances. Swap the order and
    ///     everything shifts by one.
    ///   - The ctor's multiplication is in long (conv.i8's sign extension applies).
    ///     Multiplication and addition give identical bits in long and ulong under two's
    ///     complement, so we settle on ulong here. The match for negative inputs is pinned
    ///     down by tests.
    ///   - This is a **struct**, and it rewrites its own seed on every draw. Copy it by
    ///     value and the sequence forks from that point. Callers should put it in a local
    ///     and use it to the end.
    ///
    /// That the net35 build and the net8.0 test build give the same results is guaranteed by
    /// it being built from integer arithmetic alone. **The match with the game itself** is
    /// confirmed at runtime, where Assumptions checks it against the real Randomizer
    /// (Task 5).
    /// </summary>
    public struct VanillaRandomizer
    {
        /// <summary>Knuth's MMIX multiplier.</summary>
        public const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Knuth's MMIX increment.</summary>
        public const ulong Increment = 1442695040888963407UL;

        private ulong _seed;

        /// <summary>IL: <c>seed = 6364136223846793005 * (long)v + 1442695040888963407</c></summary>
        public VanillaRandomizer(int value)
        {
            unchecked
            {
                // (ulong)(long)value reproduces conv.i8's sign extension exactly.
                _seed = Multiplier * (ulong)(long)value + Increment;
            }
        }

        /// <summary>Exposed only for tests and assumption checks. Normal use never reads
        /// it.</summary>
        public ulong Seed { get { return _seed; } }

        /// <summary>
        /// IL: <c>result = (int)(((seed &gt;&gt; 32) * (ulong)max) &gt;&gt; 32)</c>, and
        /// then <c>seed = M * seed + I</c>. This UInt32 version is the one DestroyBuildings
        /// calls; an <c>Int32(int)</c> overload **does not exist**.
        /// </summary>
        public int Int32(uint max)
        {
            unchecked
            {
                // ★ Produce the return value first. Move this line down and every draw
                //    shifts by one. Both (seed >> 32) and max are 32-bit, so this
                //    multiplication cannot overflow.
                int result = (int)(((_seed >> 32) * (ulong)max) >> 32);
                _seed = Multiplier * _seed + Increment;
                return result;
            }
        }
    }
}
