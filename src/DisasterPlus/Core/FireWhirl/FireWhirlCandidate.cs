using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>発生候補地点。Center は近傍燃焼建物の重心。</summary>
    public struct FireWhirlCandidate
    {
        public readonly Vec2 Center;
        public readonly int BurningCount;

        public FireWhirlCandidate(Vec2 center, int burningCount)
        {
            Center = center;
            BurningCount = burningCount;
        }
    }
}
