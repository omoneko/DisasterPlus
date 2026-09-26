using ColossalFramework;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Draws the typhoon's track, its gale area and its wind distribution over the city
    /// map.</b> **Main thread only** (inside the camera's <c>OnPostRender</c>).
    ///
    /// ── What the owner asked for (2026-09-02) ──────────────────────────────
    ///
    /// &gt; On the weather forecast panel, please place buttons for a tab for the weather
    /// &gt; to come (unlocked by placing a weather radar) and for showing the typhoon's
    /// &gt; track, its gale area and its wind distribution on the city map, and make them
    /// &gt; work
    ///
    /// The three <b>switch independently</b>. Turning them all on at once makes the map
    /// unreadable, and "I only want to see the track" is likely to be the commonest way
    /// to use this.
    ///
    /// ── The drawing route is the one ② already established, used as it is ──
    ///
    /// <c>OverlayEffect.DrawCircle</c> and <c>DrawQuad</c> are <b>immediate-mode
    /// drawing</b> that calls <c>Graphics.DrawMeshNow</c>, so they can only be issued from
    /// inside <c>IRenderableManager.EndOverlay</c> (the IL is in the class doc on
    /// <see cref="OverlayRenderable"/>). We also share <b>the same single</b> registration
    /// with ② — <c>RenderManager.m_renderables</c> has no API for removing one, so it is
    /// better not to add more.
    ///
    /// ── ★★ The path is the only thing we may draw ahead of time ───────────
    ///
    /// The track <b>can be drawn exactly</b> from <see cref="TyphoonTrackPlan"/>, because
    /// the path is determined by (origin, seed, speed, approach frame) alone, with no
    /// random numbers and no weather in it. So "the road it is going to travel" is not a
    /// guess.
    ///
    /// **We do not look ahead at the intensity.** The intensity includes landfall decay,
    /// and that depends on whether it is going to pass over land. So the gale circles are
    /// drawn only <b>at the present centre, at the present intensity</b>. Drawing a future
    /// radius would put <b>a confident error</b> on the map.
    ///
    /// ── Cost per frame ─────────────────────────────────────────────────────
    ///
    /// At most <see cref="TrackSamples"/> track segments, 2 circles and
    /// <see cref="WindGridSide"/>² wind grid points — **all of them constants**. Nothing
    /// scales with the size of the city or the number of buildings.
    /// The path's points reuse <see cref="_trackBuffer"/>, so <b>there is no per-frame
    /// allocation</b>.
    /// </summary>
    public static class ForecastOverlay
    {
        /// <summary>How many points the track is drawn from. That gives this minus one
        /// segments.</summary>
        private const int TrackSamples = 64;

        /// <summary>The thickness of the track line (m, to one side).</summary>
        private const float TrackHalfWidth = 24f;

        /// <summary>The spacing of the markers placed along the road still to come (in
        /// points).</summary>
        private const int TrackMarkerEvery = 8;

        /// <summary>The size of a marker (m).</summary>
        private const float TrackMarkerSize = 220f;

        /// <summary>One side of the grid the wind distribution is measured on. **This is
        /// the cost ceiling itself.**</summary>
        private const int WindGridSide = 21;

        /// <summary>The size of the circle for one wind grid cell (as a fraction of the
        /// diameter of the strong-wind area).</summary>
        private const float WindDotFraction = 0.055f;

        /// <summary>
        /// The top and bottom of <c>DrawQuad</c>. The same values as ②'s. Unless they span
        /// more than the relief of the terrain, the lines get swallowed by the ground on
        /// top of a hill or at the bottom of a valley.
        /// </summary>
        private const float SlabMinY = -64f;
        private const float SlabMaxY = 1088f;

        // ── Colours ────────────────────────────────────────────────────────
        private static readonly Color TrackColour = new Color(1f, 0.85f, 0.25f, 0.75f);
        private static readonly Color TrackPastColour = new Color(1f, 1f, 1f, 0.28f);
        private static readonly Color MarkerColour = new Color(1f, 0.72f, 0.15f, 0.55f);
        private static readonly Color StormColour = new Color(1f, 0.30f, 0.20f, 0.30f);
        private static readonly Color GaleColour = new Color(1f, 0.62f, 0.20f, 0.20f);

        private static bool _showTrack;
        private static bool _showGale;
        private static bool _showWind;
        private static bool _sessionActive;
        private static bool _errorLogged;

        /// <summary>
        /// Where the path's points are written. **Reused every frame** (nothing is
        /// allocated on the drawing path).
        /// </summary>
        private static readonly Vec2[] _trackBuffer = new Vec2[TrackSamples];

        // ── Diagnostics ────────────────────────────────────────────────────
        private static int _lastDrawCalls;

        /// <summary>How many draw calls were issued in the most recent frame (for the
        /// diagnostics).</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        public static bool ShowTrack { get { return _showTrack; } }
        public static bool ShowGale { get { return _showGale; } }
        public static bool ShowWind { get { return _showWind; } }

        /// <summary>Whether any one of them is showing (used for the panel's look).</summary>
        public static bool AnyVisible
        {
            get { return _sessionActive && (_showTrack || _showGale || _showWind); }
        }

        public static void ToggleTrack() { _showTrack = !_showTrack; }
        public static void ToggleGale() { _showGale = !_showGale; }
        public static void ToggleWind() { _showWind = !_showWind; }

        /// <summary>
        /// On level load (main thread). Rides on <b>the same registration</b> as ② —
        /// <c>RenderManager.m_renderables</c> cannot be unregistered, so we do not add
        /// more.
        /// </summary>
        public static void EnsureRegistered()
        {
            _sessionActive = true;

            // Every city load starts with all of them off (the same as ②'s
            // EnsureRegistered).
            _showTrack = false;
            _showGale = false;
            _showWind = false;
            _lastDrawCalls = 0;

            OverlayRenderable.EnsureRegistered();
        }

        /// <summary>
        /// On level unload. **The registration cannot be removed, so this is where we
        /// guarantee nothing is drawn.**
        /// </summary>
        public static void Reset()
        {
            _sessionActive = false;
            _showTrack = false;
            _showGale = false;
            _showWind = false;
            _lastDrawCalls = 0;
            _errorLogged = false;
        }

        /// <summary>
        /// **Every frame, from inside the camera's <c>OnPostRender</c>.**
        /// Returns immediately when there is no typhoon about.
        /// </summary>
        public static void Render(RenderManager.CameraInfo cameraInfo)
        {
            _lastDrawCalls = 0;

            if (!_sessionActive) return;
            if (!_showTrack && !_showGale && !_showWind) return;
            if (cameraInfo == null) return;
            if (!ModSettings.ForecastEnabled.value) return;

            try
            {
                var snapshot = TyphoonHub.Latest;
                if (snapshot == null || !snapshot.Active) return;

                if (!Singleton<RenderManager>.exists) return;
                var overlay = Singleton<RenderManager>.instance.OverlayEffect;
                if (overlay == null) return;

                if (_showTrack) DrawTrack(overlay, cameraInfo, snapshot);
                if (_showGale) DrawGale(overlay, cameraInfo, snapshot);
                if (_showWind) DrawWind(overlay, cameraInfo, snapshot);
            }
            catch (System.Exception e)
            {
                // A per-frame path. Sound it loudly once, then fall back to the per-key
                // throttle.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("forecast map overlay failed", e);
                }
                else
                {
                    Log.Diag("ForecastOverlay", "overlay draw failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// The track. **The road already travelled is faint, the road still to come is
        /// strong.**
        ///
        /// ★ The path contains no random numbers and no weather, so the line drawn here
        ///   is not a guess (the ★★ in the class doc).
        /// </summary>
        private static void DrawTrack(OverlayEffect overlay,
                                      RenderManager.CameraInfo cameraInfo,
                                      TyphoonSnapshot s)
        {
            TyphoonTrackPlan plan = s.Track;
            if (!plan.Usable) return;

            int n = plan.Sample(_trackBuffer, TrackSamples, 0u, plan.TotalFrames);
            if (n < 2) return;

            uint now = s.ElapsedFrames;

            for (int i = 1; i < n; i++)
            {
                // Has this segment already been passed? Decided by the time at its end
                // point.
                uint at = (uint)((long)plan.TotalFrames * i / (n - 1));
                Color colour = at <= now ? TrackPastColour : TrackColour;

                DrawSegment(overlay, cameraInfo, _trackBuffer[i - 1], _trackBuffer[i], colour,
                            TrackHalfWidth);

                // ★ Only put markers on the road still to come. On the road already
                //   travelled they say nothing about when it arrives, so they would just
                //   clutter the map.
                if (at > now && i % TrackMarkerEvery == 0)
                {
                    overlay.DrawCircle(cameraInfo, MarkerColour,
                                       Ground(_trackBuffer[i]), TrackMarkerSize,
                                       SlabMinY, SlabMaxY, false, true);
                    _lastDrawCalls++;
                }
            }
        }

        /// <summary>
        /// The storm area and the gale area. **At the present centre, at the present
        /// radius** (the ★★ in the class doc).
        /// </summary>
        private static void DrawGale(OverlayEffect overlay,
                                     RenderManager.CameraInfo cameraInfo,
                                     TyphoonSnapshot s)
        {
            Vector3 centre = Ground(new Vec2(s.Centre.X, s.Centre.Z));

            if (s.GaleRadius > 0f)
            {
                overlay.DrawCircle(cameraInfo, GaleColour, centre, s.GaleRadius * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                _lastDrawCalls++;
            }

            if (s.StormRadius > 0f)
            {
                overlay.DrawCircle(cameraInfo, StormColour, centre, s.StormRadius * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                _lastDrawCalls++;
            }
        }

        /// <summary>
        /// The wind distribution. Measures <c>TyphoonProfile.WindAt</c> at each point of a
        /// grid covering the gale area and places a dot whose colour varies with the
        /// strength.
        ///
        /// ★ **This is ④'s actual wind field**, not a picture invented separately — the
        ///   same function is what decides felled trees and building damage. So the strong
        ///   parts of the map really are the parts most likely to break.
        ///
        /// ★ The eye is calm, so it thins out near the centre. That is not a fault; it is
        ///   because <c>TyphoonProfile.WindAt</c> has an eye in it.
        /// </summary>
        private static void DrawWind(OverlayEffect overlay,
                                     RenderManager.CameraInfo cameraInfo,
                                     TyphoonSnapshot s)
        {
            float gale = s.GaleRadius;
            if (!(gale > 0f)) return;

            float prefabRadius = s.Prefab.StormResolved ? s.Prefab.StormRadius : 0f;
            if (!(prefabRadius > 0f)) return;

            float step = gale * 2f / (WindGridSide - 1);
            float dot = gale * 2f * WindDotFraction;

            for (int gz = 0; gz < WindGridSide; gz++)
            {
                float z = s.Centre.Z - gale + gz * step;

                for (int gx = 0; gx < WindGridSide; gx++)
                {
                    float x = s.Centre.X - gale + gx * step;

                    float dx = x - s.Centre.X;
                    float dz = z - s.Centre.Z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance > gale) continue;

                    float wind = TyphoonProfile.WindAt(distance, s.Intensity, prefabRadius);
                    if (wind <= 0.02f) continue;

                    overlay.DrawCircle(cameraInfo, WindColourOf(wind),
                                       Ground(new Vec2(x, z)), dot,
                                       SlabMinY, SlabMaxY, false, true);
                    _lastDrawCalls++;
                }
            }
        }

        /// <summary>
        /// Turns the wind-speed equivalent ([0, 1]) into a colour. Weak = blue, strong =
        /// red. **The opacity rises with the strength too** — so that the weak parts do
        /// not cover up the map.
        /// </summary>
        private static Color WindColourOf(float wind)
        {
            if (wind > 1f) wind = 1f;

            // blue (0.25, 0.55, 1) → yellow (1, 0.85, 0.2) → red (1, 0.25, 0.15)
            float r, g, b;
            if (wind < 0.5f)
            {
                float t = wind * 2f;
                r = 0.25f + (1f - 0.25f) * t;
                g = 0.55f + (0.85f - 0.55f) * t;
                b = 1f + (0.20f - 1f) * t;
            }
            else
            {
                float t = (wind - 0.5f) * 2f;
                r = 1f;
                g = 0.85f + (0.25f - 0.85f) * t;
                b = 0.20f + (0.15f - 0.20f) * t;
            }

            return new Color(r, g, b, 0.20f + 0.45f * wind);
        }

        /// <summary>
        /// A band joining two points, as a single <c>DrawQuad</c>. The same trick as ②'s
        /// fault zone.
        /// </summary>
        private static void DrawSegment(OverlayEffect overlay,
                                        RenderManager.CameraInfo cameraInfo,
                                        Vec2 from, Vec2 to, Color colour, float halfWidth)
        {
            float dx = to.X - from.X;
            float dz = to.Z - from.Z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) return;

            // A unit vector at right angles to the direction of travel.
            float nx = -dz / length * halfWidth;
            float nz = dx / length * halfWidth;

            var quad = new Quad3(
                Ground(new Vec2(from.X + nx, from.Z + nz)),
                Ground(new Vec2(to.X + nx, to.Z + nz)),
                Ground(new Vec2(to.X - nx, to.Z - nz)),
                Ground(new Vec2(from.X - nx, from.Z - nz)));

            overlay.DrawQuad(cameraInfo, colour, quad, SlabMinY, SlabMaxY, false, true);
            _lastDrawCalls++;
        }

        /// <summary>
        /// XZ to world coordinates. **A height of 0 is fine** — <c>DrawCircle</c> and
        /// <c>DrawQuad</c> paint along the terrain between <c>minY</c> and <c>maxY</c>, so
        /// there is no need to look up the ground height here (the usage ② established).
        /// </summary>
        private static Vector3 Ground(Vec2 p)
        {
            return new Vector3(p.X, 0f, p.Z);
        }
    }
}
