using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="TyphoonWind"/> that fires <b>the blow-away and nothing
    /// else</b> on a short interval. <b>Sim thread only.</b>
    ///
    /// ── Why it is split from the main body ────────────────────────────────
    ///
    /// <b>They are two different features.</b> The main body (<c>Apply</c> →
    /// <c>Sweep</c>) is <b>the sweep that knocks buildings down</b>, toggled by the
    /// "wind damage" setting. This one is only <c>DisasterHelpers.AddWind</c>, i.e.
    /// <b>the show of citizens and vehicles being shoved about</b>, toggled by "show the
    /// storm at ground level" (<c>ModSettings.TyphoonStormFx</c>). **The wind blows even
    /// with wind damage switched off.**
    ///
    /// It also keeps its own accumulator (<see cref="_minutesSinceGale"/>), separate
    /// from the main body's — share one accumulator and the blow-away only fires on the
    /// frames the sweep runs.
    ///
    /// (Splitting the file also keeps us under the 800-line limit.)
    /// </summary>
    public static partial class TyphoonWind
    {
        /// <summary>
        /// The interval for <b>the blow-away alone</b> (in-game time equivalent to a
        /// frame count).
        ///
        /// ★★ One of the answers to the owner's note "I want the storm reproduced
        ///   properly" (2026-08-22). Previously <see cref="PushWind"/> was
        ///   <b>only ever called from inside the sweep</b>, so citizens and vehicles were
        ///   shoved just once every 256 frames — once every five or six in-game minutes.
        ///   **That does not look like a storm raging.**
        ///
        ///   <see cref="Gale"/> does the blow-away on its own every 64 frames. The cost
        ///   has gone down, in fact: the push inside the sweep fired over the <b>gale
        ///   radius</b> (2.2× the storm radius), whereas this one fires over the
        ///   <b>storm radius</b> (1/4.84 of the area), so even at four times the
        ///   frequency the total is only 0.83×. In a real typhoon too, the strongest
        ///   winds are around the eyewall.
        /// </summary>
        private const int GalePushIntervalFrames = 64;

        /// <summary>Time since the last blow-away (in-game minutes). Used by
        /// <see cref="Gale"/>.</summary>
        private static float _minutesSinceGale;

        /// <summary>How many blow-aways have been fired (diagnostics).</summary>
        private static int _galePushes;

        /// <summary>
        /// Fire <b>the blow-away alone</b> every <see cref="GalePushIntervalFrames"/>
        /// frames. <b>Sim thread only</b>; call it only while a typhoon is running.
        ///
        /// ★ **It touches neither buildings, nor roads, nor trees.**
        ///   <c>DisasterHelpers.AddWind</c> is just the two lines
        ///   <c>AddWindCitizens</c> + <c>AddWindVehicles</c> (§B-1). That is why it is
        ///   toggled by the storm-show setting (<c>TyphoonStormFx</c>) rather than by the
        ///   wind-damage setting (<c>TyphoonWindDamage</c>).
        ///
        /// ★ It keeps **its own accumulator** (<see cref="_minutesSinceGale"/>), separate
        ///   from <see cref="Apply"/>'s. Share one and the blow-away only fires on the
        ///   frames the sweep runs.
        ///
        /// An exception names itself once and then stays quiet (this runs every tick).
        /// </summary>
        public static void Gale(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                GaleStep(deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon gale push failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGale",
                             "typhoon gale push failed: " + e.GetType().Name);
                }
            }
        }

        private static void GaleStep(float deltaMinutes)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f
                ? GalePushIntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceGale += deltaMinutes;
            if (interval > 0f && _minutesSinceGale > interval) _minutesSinceGale = interval;

            if (!TyphoonController.Active) return;
            if (framesPerMinute <= 0f) return;
            if (_minutesSinceGale < interval) return;

            // Do not carry the remainder over (same reason as Step).
            _minutesSinceGale = 0f;

            // ★ Fire over the storm radius, not the gale radius (see the doc on
            //   GalePushIntervalFrames).
            float range = TyphoonController.StormRadius;
            if (!(range > 0f)) return;

            var centre = TyphoonController.Centre;
            if (float.IsNaN(centre.X) || float.IsNaN(centre.Z)) return;

            var group = GroupOf(TyphoonController.DisasterId);

            // ★★ **Decide this round's answer exactly once** (the ★★ on
            //    TyphoonWind.NextLatch). Decide it separately for the centre and for what
            //    the camera is looking at, and the camera's view never once lands on the
            //    "grab" side.
            bool latch = NextLatch();

            PushWind(centre, group, range, latch);
            _galePushes++;

            // ★★ **Hit what the player is looking at too.** (2026-09-02, the owner's
            //    instruction.) With only the one shot at the centre, when the camera is
            //    away from the centre <b>the cars and people right in front of you carry
            //    on as if nothing were happening</b>. We do not want to take on the whole
            //    map either, so we fire just one more shot at <b>where the camera is
            //    looking, plus a margin</b>.
            //
            //    ★ The cost is fixed at "one more shot". It does not grow with the city.
            PushAtCamera(centre, group, range, latch);
        }

        /// <summary>
        /// The margin around what the camera is looking at (m). It reaches a little past
        /// the edge of the screen — **stop exactly at the screen edge and you can see the
        /// line**.
        /// </summary>
        private const float CameraMarginMetres = 400f;

        /// <summary>
        /// Ratio of the affected radius to the camera height. The further out you are the
        /// more is on screen, so it scales with that. Up close a small one is enough.
        /// </summary>
        private const float CameraRadiusPerHeight = 1.2f;

        /// <summary>Ceiling on the affected radius (m), so it does not spread over
        /// everything when the camera is pulled right out.</summary>
        private const float CameraRadiusMaxMetres = 2000f;

        /// <summary>
        /// Fire exactly one shot at <b>where the camera is looking</b>. **Sim thread.**
        ///
        /// ★ If the camera is outside the storm radius, do not fire — shaking the cars
        ///   somewhere the storm has not reached, just because you are looking at it, is
        ///   simply wrong.
        /// </summary>
        private static void PushAtCamera(Vec3 centre, InstanceManager.Group group,
                                         float range, bool latch)
        {
            if (!CameraFocus.Valid) return;

            float dx = CameraFocus.X - centre.X;
            float dz = CameraFocus.Z - centre.Z;
            if (dx * dx + dz * dz > range * range) return;

            float radius = CameraFocus.Height * CameraRadiusPerHeight + CameraMarginMetres;
            if (radius > CameraRadiusMaxMetres) radius = CameraRadiusMaxMetres;
            if (!(radius > 0f)) return;

            PushWind(new Vec3(CameraFocus.X, centre.Y, CameraFocus.Z), group, radius,
                     latch);
            _galePushes++;
        }

        /// <summary>How many blow-aways have been fired (diagnostics).</summary>
        public static int GalePushes { get { return _galePushes; } }
    }
}
