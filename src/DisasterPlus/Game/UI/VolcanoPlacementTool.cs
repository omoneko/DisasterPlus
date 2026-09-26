using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The tool for pointing at where the volcano goes. **Main thread only.**
    /// Copied straight from ③'s <c>FireWhirlPlacementTool</c>, which has since been
    /// removed — ③ picks its own spot now (see the <see cref="FireWhirlFeature"/> class doc).
    ///
    /// ★★ <b>The click is "make it" (changed 2026-08-21).</b> It queues a single
    /// <see cref="VolcanoRequest.Place"/>, and **there is no confirmation window any more** —
    /// on the owner's instruction, ⑤ is triggered exactly like every other disaster:
    /// "tile → slider → click the map" (see the <see cref="VolcanoState"/> class doc).
    ///
    /// **The terrain change still cannot be undone.** What went away is the gate before
    /// starting, not the irreversibility itself (the permanent warning on the volcano tab
    /// goes on saying so).
    ///
    /// ── Without registration, <c>SetTool&lt;T&gt;()</c> silently does nothing ─────────────
    ///
    /// <c>ToolController.m_tools</c> is built exactly once in <c>Awake</c>, and
    /// <c>ToolsModifierControl.SetTool&lt;T&gt;</c> only looks up a static dictionary, so
    /// **a tool added after startup is in neither of them.**
    /// Call <see cref="ToolRegistration.Register{T}"/> <b>on every level load</b>
    /// (<c>VolcanoFeature.OnLevelLoaded</c>). Forget it and you get the kind of breakage that
    /// throws no exception: "the button can be pressed but the cursor never changes".
    ///
    /// ── Pick the spot ③'s way (do not lean on <c>TerrainManager.RayCast</c>) ────
    ///
    /// The IL findings document §B-6 records that <c>TerrainManager.RayCast(Segment3, out
    /// Vector3)</c> is public and could replace ③'s hand-rolled march. **⑤ does not replace it.**
    ///
    ///   - ③'s <see cref="RayGeometry.IntersectTerrain"/> **has already shipped, works in the
    ///     game, and is covered by Core's unit tests**
    ///   - all that is settled about <c>TerrainManager.RayCast</c> is that the IL says it
    ///     exists; **this mod has never once called it**
    ///   - **as a place for picking the wrong spot, ⑤'s placement is the worst in this mod.**
    ///     The moment you press, the player's city is irreversibly wrecked
    ///   - all that would be gained is dropping the hand-rolled march, and there is no
    ///     measurable difference in cost
    ///
    /// Note also that the terrain has no Unity collider, so <c>Physics.Raycast</c> **can never
    /// hit it**. That is not a fault; it was simply never registered.
    ///
    /// ── Do not use <c>SimulationManager.AddAction</c> from here ─────────────
    ///
    /// ③ did, but ⑤ already has the request path through <see cref="VolcanoHub"/>.
    /// Two paths creates a question of "which one runs first" (plan §4.2).
    /// </summary>
    public class VolcanoPlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// Whether this tool is currently active. **Call from the main thread**
        /// (<c>ToolsModifierControl</c> is a UI-side type).
        /// </summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    var controller = ToolsModifierControl.toolController;
                    return controller != null && controller.CurrentTool is VolcanoPlacementTool;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ★★ **What the ⑤ tile on the disaster panel does. The same three steps as a
        /// vanilla disaster button.**
        ///
        ///   1. press the tile     → the cursor changes and **the slider appears**
        ///   2. move the slider    → the size of the mountain (a multiplier on the configured
        ///                          size. <see cref="VolcanoSizeScale"/>)
        ///   3. click the map      → **a volcano is made there**
        ///
        /// **Not one window opens.** Neither an explanation panel nor a confirmation window —
        /// ⑤'s state, the counts in the affected area and the irreversibility warning all live
        /// on the "Volcano" tab of the top-left shortcut and in the diagnostic dump.
        /// </summary>
        public static void Arm()
        {
            if (!ModSettings.VolcanoEnabled.value) return;
            if (!VolcanoReader.ScanTerrainFacts().Usable) return;

            Activate();
            if (!IsActive) return;

            // ★ Seed the default (multiplier 1.0 = exactly the configured size) every time the
            //   tool is armed. There is only one slider, yet the number means different things
            //   for ④ and ⑤ (see the IntensitySlider class doc).
            IntensitySlider.Seed(VolcanoSizeScale.AnchorRaw);
            IntensitySlider.Show();
        }

        public static void Activate()
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null) { Log.Warn("toolController not available"); return; }

            var tool = controller.GetComponent<VolcanoPlacementTool>();
            if (tool == null) { Log.Warn("volcano placement tool not registered"); return; }
            controller.CurrentTool = tool;
        }

        /// <summary>
        /// Return to the default tool. **Does nothing when not active** — call it
        /// unconditionally from the level-unload cleanup and you would yank whatever tool
        /// another mod had selected back to the default.
        /// </summary>
        public static void Deactivate()
        {
            try
            {
                if (!IsActive) return;

                // ★ Fold the slider away **only when this tool was the armed one**.
                //   Do it unconditionally and you take the slider away from someone who has a
                //   vanilla disaster armed.
                IntensitySlider.Hide();
                ToolsModifierControl.SetTool<DefaultTool>();
            }
            catch (System.Exception e)
            {
                Log.Warn("volcano placement tool deactivate failed: " + e.GetType().Name);
            }
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            // If the feature was switched off but the tool survived, fold it away there
            // (the panel and the button are already gone, so pointing leads nowhere).
            if (!ModSettings.VolcanoEnabled.value) { Deactivate(); return; }

            if (Input.GetMouseButtonUp(1)) { Deactivate(); return; }
            if (!Input.GetMouseButtonUp(0)) return;
            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit))
            {
                Log.Diag("volcanoPick", "ray did not hit the terrain");
                return;
            }

            // ★ The size is **the slider's value at the moment of the click** (same as
            //   vanilla). Where it cannot be read, fall back to multiplier 1.0 = exactly the
            //   configured size.
            int raw = IntensitySlider.ReadOr(VolcanoSizeScale.AnchorRaw);
            float scale = VolcanoSizeScale.ScaleFor(raw);

            // ★ What is queued is a single "make one here" (see the class doc). The surveying
            //   and the actual wrecking happen in VolcanoState.HandlePlace on the sim thread —
            //   **never touch the building, road or terrain buffers from the main thread.**
            VolcanoHub.Request(new VolcanoRequestData(VolcanoRequest.Place, hit, scale, raw));

            // Once pointed, the job is done. Do not let a held button point at a second spot.
            Deactivate();

            // ★ **Do not end in silence.** What happened (the counts in the affected area,
            //   the stage in progress, the reason for a refusal) is reported by the volcano
            //   tab and the diagnostic dump.
        }

        /// <summary>
        /// **Show the same target marker as vanilla** (2026-08-22, the owner's request:
        /// "I'd like to use the same targeting mark as the other disasters").
        ///
        /// Nothing is redrawn — <c>DisasterTool.RenderOverlay</c> is called as-is
        /// (measured from the IL, see the <see cref="PlacementMarker"/> class doc).
        ///
        /// ★ Draw nothing on a frame that is not pointing at the ground. **Do not leave it
        ///   behind at the previous position.**
        /// </summary>
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit)) return;

            // ★ The colour is produced the same way vanilla's disaster tool produces it.
            //   This is neither a warning nor an error, so both are false = the normal colour.
            PlacementMarker.Render(cameraInfo, new Vector3(hit.X, hit.Y, hit.Z),
                                   GetToolColor(false, false));
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
    }
}
