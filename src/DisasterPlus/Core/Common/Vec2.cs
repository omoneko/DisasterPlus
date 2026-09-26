namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// A 2D vector in the horizontal plane (X-Z).
    /// Core has to stay engine-free, so UnityEngine.Vector2 is off limits.
    /// </summary>
    public struct Vec2
    {
        public readonly float X;
        public readonly float Z;

        public Vec2(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float DistanceSquaredTo(Vec2 other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return dx * dx + dz * dz;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Z + b.Z);
        }

        public static Vec2 operator *(Vec2 a, float s)
        {
            return new Vec2(a.X * s, a.Z * s);
        }
    }
}
