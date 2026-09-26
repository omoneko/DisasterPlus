namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Intersection of a camera ray with the terrain height field.
    ///
    /// CS terrain is not registered with Unity Physics, so Physics.Raycast will never hit it.
    /// March coarsely to find the span where the ray crossed the ground, then switch to
    /// bisection inside it.
    /// </summary>
    public static class RayGeometry
    {
        /// <summary>Coarse march step (metres). The detail map is about 4 m/cell, so 16 m will
        /// not miss a crossing.</summary>
        private const float MarchStep = 16f;

        /// <summary>Bisection iteration count. Splitting 16 m into 2^20 converges more than
        /// far enough.</summary>
        private const int BisectionIterations = 20;

        /// <param name="direction">A normalised direction vector.</param>
        /// <returns>True if it hit the ground. hit receives the intersection point.</returns>
        public static bool IntersectTerrain(
            Vec3 origin,
            Vec3 direction,
            IHeightSampler sampler,
            float maxDistance,
            out Vec3 hit)
        {
            hit = origin;
            if (sampler == null || maxDistance <= 0f) return false;

            // A ray pointing upwards never hits the ground.
            if (direction.Y >= 0f) return false;

            float prevT = 0f;
            bool prevBelow = IsBelowGround(origin, direction, 0f, sampler);
            if (prevBelow)
            {
                // The start point is already underground. Treat it as the intersection.
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

        /// <summary>Narrows a span known to have lo above ground and hi below it.</summary>
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
