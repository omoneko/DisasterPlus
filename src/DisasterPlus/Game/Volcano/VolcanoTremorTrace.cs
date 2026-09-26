using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The volcanic earthquakes as far as the seismograph records them. Sim thread only.**
    ///
    /// ── the request (2026-08-22) ───────────────────────────────────────────────────────────
    ///
    /// > Please also fix the volcanic earthquakes not being recorded on the seismograph.
    ///
    /// ②'s <c>SeismographRecorder</c> was built to <b>throw the observation point away entirely if
    /// there is not a single vanilla earthquake</b>, so while only the volcano was shaking the
    /// seismograph stayed blank. **Shaking but a blank record** is precisely the shape this mod
    /// most wants to avoid (mixing up "could not be read" and "the value is 0").
    ///
    /// ── ★★ it is evaluated on <b>a different clock</b> from the camera shake ──────────────
    ///
    /// <see cref="VolcanoTremorShake"/> (main) advances its clock on <b>real time</b>
    /// (<c>EffectTimeDelta</c>) — the camera shake is something you look at, and otherwise the
    /// shaking would get too fast when you speed the game up.
    /// This one is a seismogram, so it takes <b>sim frames</b> as its x-axis (all of ②'s plots do;
    /// see the class doc of <c>WaveformPlot</c>). The two are
    /// <b>the same closed-form expression evaluated on different clocks</b>; neither is a copy of
    /// the other.
    ///
    /// So <b>this trace is not "the displacement the camera actually added"</b>.
    /// Its attribution is <c>[Disaster + volcanic tremor]</c> too —
    /// it is on the same side as ②'s second layer (the composite seismogram), "this mod's model",
    /// and it must never claim the first layer (vanilla's formula).
    ///
    /// ── what it holds ──────────────────────────────────────────────────────────────────────
    ///
    /// The only state is "when the shaking started" (<see cref="_startFrame"/>).
    /// Both the activity level and the attenuation are closed-form expressions in
    /// <see cref="VolcanicTremor"/>, so skipping frames still gives the same value.
    /// </summary>
    public static class VolcanoTremorTrace
    {
        /// <summary>
        /// Sim frames → seconds. **At speed 1 there are 60 frames per second.**
        /// Speed the game up and the frame index advances faster, so the seismogram's time axis
        /// appears compressed — all of ②'s plots are drawn on that convention
        /// (the x-axis is frames, not real time).
        /// </summary>
        private const float FramesPerSecond = 60f;

        private static bool _active;
        private static uint _startFrame;
        private static float _activityUnit;
        private static Vec3 _centre;
        private static float _reachMetres;
        private static uint _seed;

        /// <summary>Whether the volcano is shaking right now.</summary>
        public static bool Active { get { return _active; } }

        /// <summary>The centre of the affected range (not the crater. The same reason as <see cref="VolcanoTremorShake"/>).</summary>
        public static Vec3 Centre { get { return _centre; } }

        /// <summary>The current activity level <c>[0,1]</c>. For the diagnostics and the panel.</summary>
        public static float ActivityUnit { get { return _activityUnit; } }

        /// <summary>Call on level unload and when a volcano is cancelled.</summary>
        public static void Reset()
        {
            _active = false;
            _startFrame = 0u;
            _activityUnit = 0f;
            _centre = new Vec3(0f, 0f, 0f);
            _reachMetres = 0f;
            _seed = 0u;
        }

        /// <summary>
        /// **Sim thread, below the pause guard** (the shaking is an advance of state).
        /// Derives the current activity level from ⑤'s phase and remembers the frame the shaking
        /// started on.
        /// </summary>
        public static void Update(uint frame)
        {
            if (!ModSettings.VolcanoEnabled.value || !ModSettings.VolcanoQuake.value)
            {
                if (_active) Reset();
                return;
            }

            VolcanoFootprint footprint = VolcanoState.Footprint;
            if (!footprint.Valid)
            {
                if (_active) Reset();
                return;
            }

            float activity = VolcanoTremorActivity.For(VolcanoState.Phase,
                                                       VolcanoState.ProgressUnit,
                                                       VolcanoEruption.IntensityUnit,
                                                       VolcanoLava.CoolUnit);
            if (!(activity > 0f))
            {
                if (_active) Reset();
                return;
            }

            if (!_active)
            {
                _active = true;
                _startFrame = frame;
                _centre = footprint.Centre;
                _reachMetres = footprint.RadiusMetres * VolcanoTremorActivity.ReachRadiusFactor;
                // ★ The seed is built from ⑤'s centre (the same formula as
                //   <see cref="VolcanoTremorShake"/>).
                //   The same volcano gives the same shaking on the seismogram and on the camera.
                _seed = DeterministicRandom.Hash(
                    unchecked((uint)Round(footprint.Centre.X)),
                    unchecked((uint)Round(footprint.Centre.Z)));
            }

            _activityUnit = activity;
        }

        /// <summary>
        /// The ground motion <c>[-1,1]</c> at frame <paramref name="frame"/>, at an observation
        /// point <paramref name="distanceMetres"/> away.
        /// <b>0</b> if nothing is shaking, or if the point is outside the reach.
        ///
        /// ★ The 0 here is **the value "not shaking"**, not "could not be read"
        ///   (whether to record at all is decided by <see cref="Active"/>).
        /// </summary>
        public static float DisplacementAt(float distanceMetres, uint frame)
        {
            if (!_active) return 0f;
            if (frame < _startFrame) return 0f;

            float attenuation = VolcanicTremor.AttenuationAt(distanceMetres, _reachMetres);
            if (!(attenuation > 0f)) return 0f;

            float seconds = (frame - _startFrame) / FramesPerSecond;

            // ★★ **Convert into the same displacement unit as ②'s**
            //    (<c>VolcanoTremorActivity.DisplacementGain</c>).
            //    The seismogram's vertical scale is shared across all three traces, so putting the
            //    raw <c>[-1,1]</c> in would give **a picture where the volcanic tremor alone is
            //    1.7 times larger than vanilla's main shock**.
            //    Using the same factor as the camera side is what guarantees against that.
            return VolcanicTremor.DisplacementAt(_seed, seconds, _activityUnit)
                   * attenuation * VolcanoTremorActivity.DisplacementGain;
        }

        /// <summary>The horizontal distance (m) from an observation point to ⑤'s centre.</summary>
        public static float DistanceFromCentre(Vec3 position)
        {
            float dx = position.X - _centre.X;
            float dz = position.Z - _centre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Round(float v)
        {
            return (int)System.Math.Floor(v + 0.5f);
        }
    }
}
