using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>延焼拡大の判定対象になる建物 1 棟。</summary>
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
