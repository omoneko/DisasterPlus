namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 水平面（X-Z）の 2 次元ベクトル。
    /// Core はエンジン非依存でなければならないので UnityEngine.Vector2 は使えない。
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
