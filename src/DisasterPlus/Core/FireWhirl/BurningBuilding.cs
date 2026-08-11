using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>燃焼中の建物 1 棟。Game 層が m_fireIntensity &gt; 0 の建物から作る。</summary>
    public struct BurningBuilding
    {
        public readonly ushort Id;
        public readonly Vec2 Position;

        public BurningBuilding(ushort id, Vec2 position)
        {
            Id = id;
            Position = position;
        }
    }
}
