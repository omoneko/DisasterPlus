using ColossalFramework;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The tool for pointing at where a **trench earthquake** happens. **Main thread only.**
    /// Copied straight from ⑤'s <see cref="VolcanoPlacementTool"/>.
    ///
    /// ── The owner's instruction (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; For triggering it: click the icon, then left-click, and have it happen in the
    /// &gt; sea nearest to where you clicked.
    ///
    /// So the same three steps as a vanilla disaster button, except **it does not happen at
    /// the spot you pointed at**:
    ///
    ///   1. press the tile     → the cursor changes and **the slider appears**
    ///   2. move the slider    → the earthquake's intensity (the same raw value as vanilla)
    ///   3. click the map      → the earthquake happens in **the sea nearest to there**
    ///
    /// ★★ <b>Show that the spot pointed at differs from the epicentre, before the click.</b>
    ///   <see cref="RenderOverlay"/> puts the marker <b>over the stretch of sea that will
    ///   actually be the epicentre</b> — put it under the cursor and you only find out it is
    ///   not there after clicking.
    ///   On a frame where no sea is found, <b>draw no marker</b> (that is the signal for
    ///   "nothing will happen here").
    ///
    /// ── Searching for sea is fine on the main thread ───────────────────────
    ///
    /// What <c>TerrainManager.HasWater</c> / <c>WaterLevel</c> read is <b>the water buffer as
    /// of one frame ago</b>, and unlike the building and road grids it is not owned by the
    /// sim thread (②'s <c>LongPeriodDamage</c> reads the same path from main).
    /// **Raising the earthquake is the sim side's job.**
    /// </summary>
    public class TrenchQuakePlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// How often to search for sea to place the marker (frames). **Do not search every
        /// frame** — at worst <c>SeaSearch.CountUpTo(24)</c> licks over 2,401 points, so just
        /// moving the cursor would bog the game down.
        /// </summary>
        private const int PreviewEveryFrames = 6;

        private static int _previewCountdown;
        private static bool _previewValid;

        /// <summary>Whether an order has gone to sim and we are waiting for the answer (<see cref="OnToolUpdate"/>).</summary>
        private static bool _awaitingRaise;

        /// <summary>The <c>TrenchQuakeSlot.AttemptSerial</c> as of when the order was placed.</summary>
        private static int _awaitingSerial;

        /// <summary>Until when (real time) to keep drawing the marker in the "refused" colour.</summary>
        private static float _refusedUntilRealtime;

        /// <summary>How long that lasts (real seconds). **Too short and it is missed, too long and it lies.**</summary>
        private const float RefusedFlashSeconds = 2.5f;
        private static Vector3 _previewSea;

        /// <summary>Whether this tool is currently active. **Call from the main thread.**</summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    var controller = ToolsModifierControl.toolController;
                    return controller != null
                           && controller.CurrentTool is TrenchQuakePlacementTool;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>What the trench earthquake tile on the disaster panel does.</summary>
        public static void Arm()
        {
            if (!ModSettings.TrenchQuakeEnabled.value) return;

            Activate();
            if (!IsActive) return;

            // ★ Seed the default every time the tool is armed. There is only one slider, yet
            //   the number means different things for ④, ⑤ and this feature (see the
            //   IntensitySlider class doc).
            //   Here it means **vanilla's earthquake intensity itself**.
            IntensitySlider.Seed(DefaultIntensity);
            IntensitySlider.Show();

            _previewCountdown = 0;
            _previewValid = false;
            _awaitingRaise = false;
            _refusedUntilRealtime = 0f;
        }

        public static void Activate()
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null) { Log.Warn("toolController not available"); return; }

            var tool = controller.GetComponent<TrenchQuakePlacementTool>();
            if (tool == null) { Log.Warn("trench quake placement tool not registered"); return; }
            controller.CurrentTool = tool;
        }

        /// <summary>Return to the default tool. **Does nothing when not active.**</summary>
        public static void Deactivate()
        {
            try
            {
                if (!IsActive) return;

                IntensitySlider.Hide();
                ToolsModifierControl.SetTool<DefaultTool>();
                _previewValid = false;
            }
            catch (System.Exception e)
            {
                Log.Warn("trench quake tool deactivate failed: " + e.GetType().Name);
            }
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            if (!ModSettings.TrenchQuakeEnabled.value) { Deactivate(); return; }

            if (Input.GetMouseButtonUp(1)) { Deactivate(); return; }

            // ★★ Waiting for sim's answer. Once it ticks up, look at success or failure and
            //    close only on success.
            if (_awaitingRaise)
            {
                if (TrenchQuakeSlot.AttemptSerial != _awaitingSerial)
                {
                    _awaitingRaise = false;
                    if (TrenchQuakeSlot.LastAttemptOk) { Deactivate(); return; }

                    // ★★ **Put the refusal on screen too.** (fifth review round)
                    //    With only a log line, someone watching the game cannot tell.
                    //    Draw the marker in the "refused" colour for a while (RenderOverlay
                    //    below).
                    _refusedUntilRealtime = Time.realtimeSinceStartup + RefusedFlashSeconds;
                }

                // Accept no further click until the answer arrives (do not pile up orders).
                return;
            }

            if (!Input.GetMouseButtonUp(0)) return;
            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit))
            {
                Log.Diag("trenchPick", "ray did not hit the terrain");
                return;
            }

            // ★★ **Do not trigger anywhere the marker is not showing.** (2026-08-30, final
            //    review) It used to pile up an order unconditionally as long as the ray hit
            //    the ground. If no sea is found the sim side refuses, so nothing happens —
            //    but the tool closed anyway, so to the player it could only look like
            //    <b>"I pressed and nothing happened"</b> — the worst kind of breakage there
            //    is, because it makes success and failure indistinguishable in a playtest.
            //
            //    Now it refuses <b>with the tool still open</b>. The cursor still being out
            //    is the signal for "not here"; move to where the marker appears and it can
            //    be triggered.
            //
            //    ★ Do not search for sea here. **Only the sim side searches**
            //      (run the same predicate in two places and the reasons for refusal
            //      diverge). The preview draws the marker with the same <c>SearchRings</c>,
            //      so anywhere the marker shows ought to get through, but **that is not
            //      guaranteed** — if it does not, sim writes the reason and the tool stays
            //      open.
            byte intensity = (byte)Clamp(IntensitySlider.ReadOr(DefaultIntensity), 1, 255);

            // ★★ **Raising the earthquake happens on the sim thread.** The disaster buffer is
            //    owned by sim, so from here we only pile up an order.
            //    ② has no request inlet like ⑤'s, so, as in ③, it is handed over exactly once
            //    via SimulationManager.AddAction.
            Vec3 point = hit;

            // ★★ **Wait for the answer before closing.** (2026-08-30, fourth review round)
            //    It used to call <c>Deactivate()</c> immediately here. When sim refused
            //    (no sea, a tsunami still in progress, DLC not owned, the disaster slots
            //    full), on screen all that happened was <b>the marker showed, the tool closed
            //    and nothing happened</b>, which <b>could not be told apart from a dead
            //    button</b>.
            //
            //    Now it waits for <c>AttemptSerial</c> to tick up and <b>closes only on
            //    success</b>. On a refusal the tool stays open, and the cursor still being out
            //    is the signal for "it did not happen".
            _awaitingSerial = TrenchQuakeSlot.AttemptSerial;
            _awaitingRaise = true;

            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                TrenchQuakeSlot.Raise(point, intensity);
            });
        }

        /// <summary>
        /// Put the marker **over the stretch of sea that will actually be the epicentre**
        /// (see the class doc). Draw nothing on a frame where no sea is found —
        /// <b>that is the signal for "nothing will happen here".</b>
        /// </summary>
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            if (UIView.IsInsideUI()) return;

            // ★ The search is expensive (2,401 points at worst. The lock is taken once per
            //   sweep, but the array is still licked over), so thin it out. On the frames in
            //   between, leave the marker on the sea found last time — clearing it flickers.
            if (--_previewCountdown <= 0)
            {
                _previewCountdown = PreviewEveryFrames;
                _previewValid = false;

                Vec3 hit;
                if (TryPickGround(out hit))
                {
                    Vec3 sea;
                    float distance;
                    if (TrenchQuakeSlot.TryFindNearestSea(hit, out sea, out distance))
                    {
                        _previewSea = new Vector3(sea.X, sea.Y, sea.Z);
                        _previewValid = true;
                    }
                }
            }

            // ★★ **A refusal must always be shown.** (2026-08-31, cross-review)
            //    These two lines sat <b>below</b> `if (!_previewValid) return;` for a long
            //    time. But on a map with no sea the marker never becomes valid, so
            //    <b>the refusal colour was never drawn either</b> — clicking produced no
            //    marker, no colour and no sound, back to a "dead button".
            //    On a refusal, show the not-allowed marker <b>at the cursor position</b>.
            bool refused = Time.realtimeSinceStartup < _refusedUntilRealtime;

            if (!_previewValid)
            {
                if (!refused) return;

                Vec3 here;
                if (!TryPickGround(out here)) return;

                PlacementMarker.Render(cameraInfo,
                    new Vector3(here.X, here.Y, here.Z), GetToolColor(false, true));
                return;
            }

            PlacementMarker.Render(cameraInfo, _previewSea, GetToolColor(false, refused));
        }

        private static bool TryPickGround(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            var cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            return RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out hit);
        }

        /// <summary>The same as the game's own default disaster intensity.</summary>
        private const int DefaultIntensity = 55;

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
