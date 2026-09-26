namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// Decides the whirl's size and destructive power from the number of burning buildings.
    /// A small fire gives a small whirl, a large fire a large one.
    /// </summary>
    public static class FireWhirlStrength
    {
        public const float MinRadius = 40f;
        public const float MaxRadius = 220f;

        /// <summary>At this many buildings the radius reaches MaxRadius.</summary>
        private const int SaturationCount = 120;

        /// <summary>The whirl's radius (metres).</summary>
        public static float RadiusFor(int burningCount)
        {
            return MinRadius + (MaxRadius - MinRadius) * Curve(burningCount);
        }

        /// <summary>Destructive-power multiplier [0, 1]. The Game layer multiplies VortexAI's
        /// destruction radius by it.</summary>
        public static float DamageScaleFor(int burningCount)
        {
            return Curve(burningCount);
        }

        /// <summary>
        /// A curve rising monotonically from 0 to 1, reaching 1 and saturating at
        /// SaturationCount. Being a square root, it climbs fast early on and the radius
        /// does not run away even in a huge fire.
        /// </summary>
        private static float Curve(int burningCount)
        {
            if (burningCount <= 0) return 0f;
            if (burningCount >= SaturationCount) return 1f;
            return (float)System.Math.Sqrt((double)burningCount / SaturationCount);
        }
    }
}
