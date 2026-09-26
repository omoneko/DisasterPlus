using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>One burning building. The Game layer builds these from buildings with
    /// m_fireIntensity &gt; 0.</summary>
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
