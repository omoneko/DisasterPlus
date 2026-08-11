namespace DisasterPlus.Core.Common
{
    /// <summary>3 次元ベクトル。Core は UnityEngine.Vector3 を参照できないので自前で持つ。</summary>
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
