namespace DisasterPlus.Core.Common
{
    /// <summary>A 3-D vector. Core cannot reference UnityEngine.Vector3, so we carry our
    /// own.</summary>
    public struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec2 ToVec2()
        {
            return new Vec2(X, Z);
        }
    }
}
