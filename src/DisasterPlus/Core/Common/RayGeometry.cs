namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// カメラレイと地形高さ場の交差。
    ///
    /// CS の地形は Unity Physics に登録されていないので Physics.Raycast では絶対に当たらない。
    /// 粗くマーチして地面を跨いだ区間を見つけ、その中で二分法に切り替える。
    /// </summary>
    public static class RayGeometry
    {
        /// <summary>粗マーチの刻み幅（メートル）。詳細マップは約 4m/セルなので 16m で跨ぎを見逃さない。</summary>
        private const float MarchStep = 16f;

        /// <summary>二分法の反復回数。16m を 2^20 分割すれば十分に収束する。</summary>
        private const int BisectionIterations = 20;

        /// <param name="direction">正規化済みの方向ベクトル。</param>
        /// <returns>地面と交差したら true。hit に交点が入る。</returns>
        public static bool IntersectTerrain(
            Vec3 origin,
            Vec3 direction,
            IHeightSampler sampler,
            float maxDistance,
            out Vec3 hit)
        {
            hit = origin;
            if (sampler == null || maxDistance <= 0f) return false;

            // 上を向いているレイは地面に当たらない。
            if (direction.Y >= 0f) return false;

            float prevT = 0f;
            bool prevBelow = IsBelowGround(origin, direction, 0f, sampler);
            if (prevBelow)
            {
                // 始点が既に地中。そこを交点として扱う。
                hit = PointAt(origin, direction, 0f);
                return true;
            }

            for (float t = MarchStep; ; t += MarchStep)
            {
                if (t > maxDistance) t = maxDistance;

                bool below = IsBelowGround(origin, direction, t, sampler);
                if (below)
                {
                    hit = Bisect(origin, direction, prevT, t, sampler);
                    return true;
                }

                if (t >= maxDistance) return false;
                prevT = t;
            }
        }

        private static Vec3 PointAt(Vec3 origin, Vec3 direction, float t)
        {
            return new Vec3(
                origin.X + direction.X * t,
                origin.Y + direction.Y * t,
                origin.Z + direction.Z * t);
        }

        private static bool IsBelowGround(Vec3 origin, Vec3 direction, float t, IHeightSampler sampler)
        {
            Vec3 p = PointAt(origin, direction, t);
            return p.Y <= sampler.SampleHeight(p.X, p.Z);
        }

        /// <summary>lo は地上、hi は地中と分かっている区間を詰める。</summary>
        private static Vec3 Bisect(Vec3 origin, Vec3 direction, float lo, float hi, IHeightSampler sampler)
        {
            for (int i = 0; i < BisectionIterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsBelowGround(origin, direction, mid, sampler)) hi = mid;
                else lo = mid;
            }
            return PointAt(origin, direction, hi);
        }
    }
}
