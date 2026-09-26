namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// Works out, from the distance to the epicentre and the earthquake's intensity, **the
    /// local factor vanilla actually uses for its collapse test**. Nothing invented here.
    ///
    /// The whole-quake disc call from §A-3 of the IL facts document:
    ///   DestroyBuildings(preRadius: R, destructionRadiusMin: 0, destructionRadiusMax: R,
    ///                    probability: 0.02f)   where R = 2000 + m_intensity * 20
    /// Inside it, for each building it computes
    ///   fD = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    /// With min = 0 and max = R ≧ 2000, Max(1, ...) is always R, so this reduces to
    /// **fD = 1 - dist/R**. That value is what we call s here.
    ///
    /// The request's claim that "there is no notion of intensity varying with distance from
    /// the hypocentre" was only half right. The distance falloff has been there all along.
    /// **What was missing was only the visualisation.**
    /// </summary>
    public static class SeismicIntensity
    {
        /// <summary>The whole-quake disc's base radius (IL: ldc.r4 2000).</summary>
        public const float BaseRadius = 2000f;

        /// <summary>Radius added per point of intensity (IL: ldc.r4 20).</summary>
        public const float RadiusPerIntensity = 20f;

        /// <summary>
        /// The default DisasterManager.CreateDisaster fills in (IL_0028).
        /// It is also the reference point at which the camera-shake correction is zero
        /// (Task 6).
        /// </summary>
        public const byte VanillaDefaultIntensity = 55;

        /// <summary>The whole-quake disc radius R. 3,100 m at intensity 55, 4,000 m at 100,
        /// 7,100 m at 255.</summary>
        public static float RadiusOf(byte intensity)
        {
            return BaseRadius + intensity * RadiusPerIntensity;
        }

        /// <summary>
        /// The local factor s at a point `distance` from the epicentre. A linear ramp: 1 at
        /// the epicentre, 0 at R.
        ///
        /// Outside R it returns 0, but **that does not mean "no shaking" — it means "vanilla
        /// does not even run the test"** (the hard cull by preRadius). Callers must tell the
        /// two apart with <see cref="IsInside"/> and must not display out-of-range as
        /// "intensity 0.0".
        /// </summary>
        public static float At(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return 0f;

            float r = RadiusOf(intensity);
            if (distance >= r) return 0f;
            return 1f - distance / r;
        }

        /// <summary>Whether this is inside the range where vanilla runs its collapse
        /// test.</summary>
        public static bool IsInside(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return false;
            return distance < RadiusOf(intensity);
        }
    }
}
