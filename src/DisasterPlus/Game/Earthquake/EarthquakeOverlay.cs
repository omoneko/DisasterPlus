using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **Draws the seismic-intensity distribution on the map.** The only part that
    /// answers the request's "there is also no notion of intensity varying with distance
    /// from the hypocentre within the city" with an actual map.
    /// **Main (render) thread only.**
    ///
    /// ── What is drawn (all of it quantities vanilla computes) ───────────────
    ///
    /// | What | Quantity | Source |
    /// |---|---|---|
    /// | Teal concentric circles | <c>s = 1 - d/R</c>, <c>R = 2000 + 20·intensity</c> | §A-3's whole-quake disc |
    /// | Magenta band | Where the four fault discs could land (<c>1.5·w</c>, along strike <c>0.4L + w</c>) | §A-3 + whole-feature review C1 |
    /// | White dot | The epicentre, <c>m_targetPosition</c> | — |
    /// | White line | The fault's strike <c>m_angle</c>, length <c>L</c> | §A-3 / §A-6 |
    ///
    /// <b>s is not "the shaking".</b> It is the local factor applied to the collapse and
    /// fire checks (whole-feature review C3). Vanilla's shaking has no radius cut-off
    /// (§A-7), so the ground still shakes outside this disc. The panel's legend states
    /// as much.
    ///
    /// ── Drawing them differently so the two discs are not conflated ──────
    ///
    /// The whole-quake disc is a **ramp** at <c>probability = 0.02</c>; the four fault
    /// discs are **flat** at <c>probability = 1</c> (§A-3). So **only the concentric
    /// circles are shaded**, and the band is painted at a uniform density. Draw the band
    /// dark, as though it were "where s is high", and two separate models start to look
    /// like one scale (which the last paragraph of design doc §3.1 forbids).
    ///
    /// ── Not the same thing as vanilla's hazard map ──────────────────────
    ///
    /// The panel's "Show on map" only switches to vanilla's info view
    /// (<c>SubInfoMode.EarthquakeHazard</c>), and what gets painted there is a
    /// **different shape** — distance to the crack **line segment**, quadratic falloff,
    /// <c>Rmax = R + 400</c>, and without <c>Located</c> (i.e. a seismograph) not a single
    /// cell is painted (§A-6). This overlay appears with no seismograph and has a
    /// different shape. The legend's job is to **stop them being read as the same thing**.
    ///
    /// ── Constraints that come with being a per-frame path ───────────────────
    ///
    ///   - **No allocation.** <c>Color</c>, <c>Vector3</c> and <c>Quad3</c> are all
    ///     structs, earthquakes are enumerated by index, and the fault polylines are
    ///     reused from <see cref="_outlines"/>.
    ///   - **No <c>Log.Warn</c> / <c>Log.Error</c>** (they are not throttled). Shout once
    ///     for an exception and then drop to <c>Log.Diag</c>'s per-key throttle (the shape
    ///     established by <c>CameraShakeBooster</c>).
    ///   - **A ceiling on the draw calls** (<see cref="MaxDrawCallsPerFrame"/>). Up to 256
    ///     earthquakes can exist at once (§E-1).
    /// </summary>
    public static class EarthquakeOverlay
    {
        /// <summary>The diameter of the epicentre marker (m).</summary>
        private const float EpicentreMarkerSize = 160f;

        /// <summary>Half the width of the strike line (m).</summary>
        private const float StrikeLineHalfWidth = 12f;

        /// <summary>
        /// The vertical slab that gets drawn (world Y).
        ///
        /// <c>DrawCircle</c> / <c>DrawQuad</c> draw nothing outside <c>minY</c> /
        /// <c>maxY</c> (<c>ID_LimitsY</c>, IL_0069 / IL_00D2). CS1's terrain keeps
        /// <c>RawHeights</c> as <c>ushort/64</c>, so it fits in **0-1024 m** (IL facts
        /// doc §D-1). Without a slab that spans all of it, the overlay cuts off on top of
        /// mountains and they look as though they are outside the distribution.
        /// </summary>
        private const float SlabMinY = -64f;

        private const float SlabMaxY = 1088f;

        /// <summary>
        /// The total number of draw calls allowed in one frame.
        ///
        /// That breaks down as at most 22 calls per earthquake (10 for the ramp, 1 for
        /// the epicentre, <see cref="FaultBandOutline.Segments"/>=10 for the fault band
        /// and 1 for the strike line), times **4 earthquakes**. Up to 256 earthquakes can
        /// exist at once (§E-1), so without a ceiling this could stretch to 5,632 calls.
        /// The budget is spent **one whole earthquake at a time** (never leave a
        /// half-drawn earthquake behind). When it runs out we stop drawing and the panel
        /// says so (<see cref="BudgetExhausted"/>).
        ///
        /// The frames during an earthquake are the heaviest in the game. Following the
        /// same discipline with which Tasks 6-7 cut the cursor ray from 521 to 131
        /// samples per frame, this has a ceiling from the start.
        /// </summary>
        public const int MaxDrawCallsPerFrame = 4 * DrawCallsPerQuake;

        /// <summary>Maximum calls per earthquake (the breakdown of <see cref="MaxDrawCallsPerFrame"/>).</summary>
        public const int DrawCallsPerQuake =
            IntensityRamp.Steps + 1 + FaultBandOutline.Segments + 1;

        /// <summary>The cache of fault polylines. The same 4 earthquakes as <see cref="MaxDrawCallsPerFrame"/>.</summary>
        private static readonly FaultBandOutline[] _outlines = CreateOutlines();

        private static bool _enabled;
        private static bool _sessionActive;
        private static bool _errorLogged;

        private static int _lastDrawCalls;
        private static int _drawnQuakes;
        private static bool _budgetExhausted;
        private static bool _faultGeometryMissing;

        /// <summary>
        /// Whether it is on screen. **Session state**: it is kept neither in the save nor
        /// in the settings (treated like an info view; it starts off every time a city is
        /// loaded).
        /// </summary>
        public static bool Enabled { get { return _enabled; } }

        /// <summary>How many draw calls were actually issued on the most recent frame. For the diagnostic dump.</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        /// <summary>How many earthquakes were actually drawn on the most recent frame.</summary>
        public static int DrawnQuakes { get { return _drawnQuakes; } }

        /// <summary>Whether the earthquakes to draw did not fit in the budget (the panel says so).</summary>
        public static bool BudgetExhausted { get { return _budgetExhausted; } }

        /// <summary>
        /// Whether any of the earthquakes drawn had fault-band geometry that could not be
        /// read. **When it cannot be read, not a single band is drawn** (never draw at a
        /// guessed size), so this is what lets the panel state why no band is showing.
        /// </summary>
        public static bool FaultGeometryMissing { get { return _faultGeometryMissing; } }

        /// <summary>
        /// Whether we are hooked into the render loop (diagnostic).
        ///
        /// ★★ **Keep no flag of our own.** This used to set <c>_registered = true</c>
        ///   here, but registration is done by <see cref="OverlayRenderable"/>, which
        ///   swallows an exception on failure and returns still false — so a flag of our
        ///   own would <b>report "registered" when registration had failed</b>. Lying in
        ///   a diagnostic is the breakage this foundation most wants to avoid.
        /// </summary>
        public static bool Registered { get { return OverlayRenderable.Registered; } }

        public static void Toggle()
        {
            _enabled = !_enabled;
            if (!_enabled) ClearStats();
        }

        /// <summary>
        /// Stops the display. **Always call this when the panel is closed.**
        ///
        /// The legend only exists inside the panel. If the panel is closed and the
        /// overlay stays on the map, **the only thing saying what quantity you are
        /// looking at disappears from the screen** — and that is precisely the state in
        /// which it is easiest to mistake it for vanilla's hazard view. This guarantees
        /// structurally that the picture and the legend always appear together.
        /// </summary>
        public static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;
            ClearStats();
        }

        /// <summary>
        /// On level load (main thread). **This is where we register, for the first time.**
        ///
        /// <c>RenderManager.m_renderables</c> is static and has no API to remove one (see
        /// <see cref="OverlayRenderable"/>'s class doc). So registration is limited to
        /// once per process, and from then on <see cref="_sessionActive"/> switches
        /// whether we draw.
        /// </summary>
        public static void EnsureRegistered()
        {
            _sessionActive = true;
            // It starts off every time a city is loaded. Even on a path that reaches the
            // next city without Reset() being called (recovering from a crash, say), the
            // previous city's toggle is not inherited.
            _enabled = false;
            ClearStats();

            // ★ There is only one registration, shared with ①
            //   (OverlayRenderable.EnsureRegistered). Never create a second one here —
            //   with no API to remove them, the count only ever grows.
            OverlayRenderable.EnsureRegistered();
        }

        /// <summary>
        /// On level unload. **Never carry on drawing after the earthquake has ended or
        /// after leaving the city.**
        ///
        /// The registration itself cannot be removed, so dropping
        /// <see cref="_sessionActive"/> here to make <see cref="Render"/> return
        /// immediately is the only way to stop it.
        /// </summary>
        public static void Reset()
        {
            _sessionActive = false;
            _enabled = false;
            ClearStats();
            for (int i = 0; i < _outlines.Length; i++) _outlines[i].Rebuild(default(FaultBand));
        }

        private static void ClearStats()
        {
            _lastDrawCalls = 0;
            _drawnQuakes = 0;
            _budgetExhausted = false;
            _faultGeometryMissing = false;
        }

        private static FaultBandOutline[] CreateOutlines()
        {
            var outlines = new FaultBandOutline[MaxDrawCallsPerFrame / DrawCallsPerQuake];
            for (int i = 0; i < outlines.Length; i++) outlines[i] = new FaultBandOutline();
            return outlines;
        }

        /// <summary>
        /// **Every frame, from the render thread (inside <c>OverlayEffect.OnPostRender</c>).**
        /// The IL for the call path is in <see cref="OverlayRenderable"/>'s class doc.
        /// </summary>
        public static void Render(RenderManager.CameraInfo cameraInfo)
        {
            ClearStats();

            if (!_enabled || !_sessionActive) return;
            if (!ModSettings.EarthquakeEnabled.value) return;
            if (cameraInfo == null) return;

            try
            {
                var snapshot = EarthquakeHub.Latest;
                if (snapshot == null || !snapshot.Valid) return;

                var quakes = snapshot.Quakes;
                if (quakes.Count == 0) return;

                if (!Singleton<RenderManager>.exists) return;
                var overlay = Singleton<RenderManager>.instance.OverlayEffect;
                if (overlay == null) return;

                DrawAll(overlay, cameraInfo, quakes);
            }
            catch (System.Exception e)
            {
                // A per-frame path. Shout once, then drop to the per-key throttle.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("earthquake intensity overlay failed", e);
                }
                else
                {
                    Log.Diag("EqOverlay", "overlay draw failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// Picks which earthquakes to draw and draws them within the budget.
        /// **Allocates nothing.**
        ///
        /// It considers only earthquakes for which <c>QuakeSelection.RunsDamage</c> is
        /// true — i.e. <c>Active</c> and <c>Emerging</c>. <c>Clearing</c> is excluded
        /// because the call to <c>DestroyBuildings</c> exists **only** in
        /// <c>SimulationStep</c>'s <c>Active</c> branch (§A-3), and this uses the same
        /// decision that makes the panel's cursor row bow out for the same reason
        /// (whole-feature review I2).
        ///
        /// <c>Emerging</c> is included because by then the epicentre, the intensity and
        /// the fault's orientation are already fixed (<c>StartDisaster</c> in §A-1), so
        /// the area that is going to be damaged **has already been decided**. Vanilla's
        /// hazard map likewise paints for <c>Emerging|Active</c> (§A-6).
        /// </summary>
        private static void DrawAll(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                    IList<EarthquakeReading> quakes)
        {
            int budget = MaxDrawCallsPerFrame;
            int slot = 0;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];
                if (q == null) continue;
                if (!QuakeSelection.RunsDamage(q.Phase)) continue;

                if (budget < DrawCallsPerQuake)
                {
                    // ★ Cut off a whole earthquake at a time. Draw part of one with the
                    //    budget that is left and you put a ramp on screen that is missing
                    //    its dark inner steps — i.e. a falloff that is not the real
                    //    distribution.
                    _budgetExhausted = true;
                    break;
                }

                var outline = slot < _outlines.Length ? _outlines[slot] : null;
                slot++;

                int used = DrawQuake(overlay, cameraInfo, q, outline);
                budget -= used;
                _lastDrawCalls += used;
                _drawnQuakes++;
            }
        }

        /// <summary>One earthquake. Returns the number of draw calls issued.</summary>
        private static int DrawQuake(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                     EarthquakeReading q, FaultBandOutline outline)
        {
            var epicentre = new Vector3(q.Epicentre.X, q.Epicentre.Y, q.Epicentre.Z);
            int calls = DrawRamp(overlay, cameraInfo, epicentre, q.Radius);

            var band = new FaultBand(new Vec2(q.Epicentre.X, q.Epicentre.Z),
                                     q.AngleRadians, q.CrackLength, q.CrackWidth);

            if (!band.Known)
            {
                // We do not know the band's size. **Never draw a guess** (the four prefab
                // values are not in the DLL and can only be read in the running game,
                // §A-0). The panel gives the reason.
                _faultGeometryMissing = true;
            }
            else if (outline != null)
            {
                // The polyline is a function of L and W alone, so measuring it once per
                // earthquake is enough (see FaultBandOutline's class doc). **Checking
                // Matches before calling** is the mechanism that holds it to that one
                // time, and it is the only reason it is acceptable to call Rebuild from
                // the render path (layer-2 review M1: the 209 Contains calls, roughly
                // 30,000 Gap evaluations, are paid on the single first frame an
                // earthquake appears).
                if (!outline.Matches(band.Length, band.Width)) outline.Rebuild(band);
                if (outline.Known) calls += DrawFaultBand(overlay, cameraInfo, band, outline, epicentre.y);
            }

            // The epicentre goes last, on top of the ramp and the band.
            overlay.DrawCircle(cameraInfo, MarkerColour, epicentre, EpicentreMarkerSize,
                               SlabMinY, SlabMaxY, false, true);
            calls++;

            return calls;
        }

        /// <summary>
        /// The whole-quake disc's ramp: <see cref="IntensityRamp.Steps"/> circles,
        /// **largest first**. The alpha is decided by
        /// <see cref="IntensityRamp.DrawAlpha"/> (so the density is proportional to s).
        /// </summary>
        private static int DrawRamp(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                    Vector3 epicentre, float radius)
        {
            int calls = 0;
            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float r = IntensityRamp.RadiusOf(k, radius);
                if (r <= 0f) continue;

                var colour = new Color(RampR, RampG, RampB, IntensityRamp.DrawAlpha(k));
                // size is the diameter (IL_007E: the bounds are center ± size*0.5, and
                // ID_CenterPos.w = size*0.5).
                overlay.DrawCircle(cameraInfo, colour, epicentre, r * 2f,
                                   SlabMinY, SlabMaxY, false, true);
                calls++;
            }
            return calls;
        }

        /// <summary>
        /// Draws the fault band as a strip of trapezia. **The density is uniform** (a
        /// <c>probability = 1</c> model has no ramp). Plus one strike line, of length
        /// <c>L</c>.
        /// </summary>
        private static int DrawFaultBand(OverlayEffect overlay, RenderManager.CameraInfo cameraInfo,
                                         FaultBand band, FaultBandOutline outline, float y)
        {
            int calls = 0;
            var colour = new Color(BandR, BandG, BandB, BandAlpha);

            for (int i = 0; i < FaultBandOutline.Segments; i++)
            {
                float u0 = outline.AlongAt(i);
                float u1 = outline.AlongAt(i + 1);
                float h0 = outline.HalfWidthAt(i);
                float h1 = outline.HalfWidthAt(i + 1);
                if (h0 <= 0f && h1 <= 0f) continue;

                var quad = new Quad3(
                    LocalToWorld(band, u0, h0, y),
                    LocalToWorld(band, u1, h1, y),
                    LocalToWorld(band, u1, -h1, y),
                    LocalToWorld(band, u0, -h0, y));

                overlay.DrawQuad(cameraInfo, colour, quad, SlabMinY, SlabMaxY, false, true);
                calls++;
            }

            // The strike (m_angle). The length is exactly L — the same quantity vanilla
            // uses for the hazard map's line segment (§A-6's
            // seg.a/seg.b = c ∓ dir*(L*0.5)).
            float half = band.Length * 0.5f;
            var strike = new Quad3(
                LocalToWorld(band, -half, StrikeLineHalfWidth, y),
                LocalToWorld(band, half, StrikeLineHalfWidth, y),
                LocalToWorld(band, half, -StrikeLineHalfWidth, y),
                LocalToWorld(band, -half, -StrikeLineHalfWidth, y));
            overlay.DrawQuad(cameraInfo, MarkerColour, strike, SlabMinY, SlabMaxY, false, true);
            calls++;

            return calls;
        }

        /// <summary>
        /// Converts the fault's local coordinates (along strike / across) into world
        /// space. The across basis is <c>(Direction.Z, -Direction.X)</c>, matching
        /// <c>FaultBand.Contains</c>'s <c>across</c> right down to the sign (the same
        /// orientation <see cref="FaultBandOutline"/> measured in).
        /// </summary>
        private static Vector3 LocalToWorld(FaultBand band, float along, float across, float y)
        {
            float x = band.Centre.X + band.Direction.X * along + band.Direction.Z * across;
            float z = band.Centre.Z + band.Direction.Z * along - band.Direction.X * across;
            return new Vector3(x, y, z);
        }

        // ── Colours ───────────────────────────────────────────
        //
        // **Never use a sequence of hues that evokes a real intensity scale**
        // (design doc §3.1 / §7-4). A green→yellow→orange→red sequence, all by itself,
        // claims to be "a scale with real-world meaning, like the JMA seismic intensity
        // scale". Here it is expressed **purely as shades of a single hue** — a step
        // means one thing only: "s is this large".
        //
        // The hue is chosen on the far side from vanilla's disaster hazard info view
        // (yellows and reds), so that with both on screen at once one does not look like
        // a continuation of the other.

        private const float RampR = 0.16f;
        private const float RampG = 0.85f;
        private const float RampB = 1.00f;

        // The fault band. It uses **a different hue** from the ramp. Make it a darker
        // version of the same colour and it looks like "where s is high", erasing the
        // fact that it is a separate model at probability = 1.
        private const float BandR = 1.00f;
        private const float BandG = 0.30f;
        private const float BandB = 0.80f;

        /// <summary>The band is uniform. A <c>probability = 1</c> model has no falloff (§A-3).</summary>
        private const float BandAlpha = 0.40f;

        private static readonly Color MarkerColour = new Color(1f, 1f, 1f, 0.85f);
    }
}
