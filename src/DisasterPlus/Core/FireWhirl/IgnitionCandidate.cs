using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>One building considered for fire spread.</summary>
    public struct IgnitionCandidate
    {
        public readonly ushort Id;
        public readonly Vec2 Position;
        public readonly bool AlreadyBurning;

        public IgnitionCandidate(ushort id, Vec2 position, bool alreadyBurning)
        {
            Id = id;
            Position = position;
            AlreadyBurning = alreadyBurning;
        }
    }
}
