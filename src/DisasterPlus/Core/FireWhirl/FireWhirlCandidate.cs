using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>A candidate spot for one to form. Center is the centroid of the burning
    /// buildings nearby.</summary>
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
