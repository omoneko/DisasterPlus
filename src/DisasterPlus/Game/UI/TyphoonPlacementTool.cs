using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The tool for pointing at where the typhoon starts. **Main thread only.**
    /// Copied straight from ⑤'s <see cref="VolcanoPlacementTool"/>.
    ///
    /// ── Why it exists ─────────────────────────────────────
    ///
    /// A vanilla disaster button goes "press → **the intensity slider appears** → click the
    /// map → the disaster happens at that spot after a spawn delay". ④ lines up with the
    /// same three steps — the entry point is <see cref="Arm"/>, and
    /// <see cref="TyphoonRequestData"/> carries the spot along with <b>the intensity that was
    /// selected at that moment</b>. The slider is borrowed from vanilla as-is
    /// (<see cref="IntensitySlider"/>).
    ///
    /// ★ **No panel is opened from here.** Triggering and reading are separate things, and
    ///   ④'s information lives on the shortcut in the top-left (the owner's remark:
    ///   "you don't need to put out all that explanation").
    ///
    /// ── Unlike ⑤, the click is "make it happen" ──────────────────────
    ///
    /// What ⑤'s placement tool queues is a <c>Survey</c> (look only); the terrain is actually
    /// wrecked only when the player has read the confirmation line and pressed.
    /// That is because **⑤'s terrain change cannot be undone**, and ④ has no such constraint
    /// (a typhoon passes through). So, like a vanilla disaster button, ④ <b>commits at the
    /// moment of the click</b>.
    ///
    /// ── Without registration, <c>SetTool&lt;T&gt;()</c> silently does nothing ─────────────
    ///
    /// <c>ToolController.m_tools</c> is built exactly once in <c>Awake</c>, and
    /// <c>ToolsModifierControl.SetTool&lt;T&gt;</c> only looks up a static dictionary, so
    /// **a tool added after startup is in neither of them.**
    /// Call <see cref="ToolRegistration.Register{T}"/> <b>on every level load</b>
    /// (<c>TyphoonFeature.OnLevelLoaded</c>). Forget it and you get the kind of breakage that
    /// throws no exception: "the button can be pressed but the cursor never changes".
    ///
    /// ── How the spot is picked ─────────────────────────────────────
    ///
    /// The terrain has no Unity collider, so <c>Physics.Raycast</c> **can never hit it**.
    /// The intersection of the camera ray and the height field is solved ourselves with
    /// Core's <see cref="RayGeometry.IntersectTerrain"/> (the same path as ⑤. It has shipped,
    /// it works in the game, and Core's tests cover it).
    ///
    /// ── Do not touch <c>DisasterManager</c> from here ────────────────────
    ///
    /// Creating disasters is the sim thread's job, and ④ already has the request path through
    /// <see cref="TyphoonHub"/>. Add a <c>SimulationManager.AddAction</c> here and there would
    /// be two paths, which creates a question of "which one runs first" (⑤ made the same call).
    /// </summary>
    public class TyphoonPlacementTool : ToolBase
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
                    return controller != null && controller.CurrentTool is TyphoonPlacementTool;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ★★ **What the ④ tile on the disaster panel does. The same three steps as a
        /// vanilla disaster button.**
        ///
        ///   1. press the tile     → the cursor changes and **the intensity slider appears**
        ///   2. move the slider    → the typhoon's intensity (the same meaning as vanilla's
        ///                           <c>m_intensity</c>)
        ///   3. click the map      → the typhoon starts at that spot
        ///
        /// **No explanation panel is opened any more.** The information lives on the shortcut
        /// in the top-left (the owner's remark: "you don't need to put out all that
        /// explanation"). Open one here and "make it happen" and "read about it" collapse back
        /// into a single action.
        ///
        /// In an environment where it cannot be triggered (Natural Disasters not owned) the
        /// tile itself cannot be pressed (<see cref="DisasterPanelBar"/> disables it and puts
        /// the reason in the tooltip). The gate is repeated here in case a path that does let
        /// it be pressed is added in future.
        /// </summary>
        public static void Arm()
        {
            if (!ModSettings.TyphoonEnabled.value) return;
            if (!ModCompat.NaturalDisastersOwned) return;

            Activate();
            if (!IsActive) return;

            // ★ Seed the default every time the tool is armed. There is only one slider, yet
            //   the number means different things for ④ and ⑤ (see the IntensitySlider
            //   class doc).
            IntensitySlider.Seed(ModSettings.TyphoonIntensity.value);
            IntensitySlider.Show();
        }

        public static void Activate()
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null) { Log.Warn("toolController not available"); return; }

            var tool = controller.GetComponent<TyphoonPlacementTool>();
            if (tool == null) { Log.Warn("typhoon placement tool not registered"); return; }
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

                // ★ Fold the intensity slider away **only when this tool was the armed one**.
                //   Do it unconditionally and you take the slider away from someone who has a
                //   vanilla disaster armed.
                IntensitySlider.Hide();
                ToolsModifierControl.SetTool<DefaultTool>();
            }
            catch (System.Exception e)
            {
                Log.Warn("typhoon placement tool deactivate failed: " + e.GetType().Name);
            }
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            // If the feature was switched off but the tool survived, fold it away there
            // (the panel and the tile are already gone, so pointing leads nowhere).
            if (!ModSettings.TyphoonEnabled.value) { Deactivate(); return; }

            if (Input.GetMouseButtonUp(1)) { Deactivate(); return; }
            if (!Input.GetMouseButtonUp(0)) return;
            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit))
            {
                Log.Diag("typhoonPick", "ray did not hit the terrain");
                return;
            }

            // ★ The intensity is **the slider's value at the moment of the click** (same as
            //   vanilla). Where it cannot be read, fall back to the value from the settings
            //   screen — never express "could not read" as 0 (a valid value meaning
            //   "the weakest setting").
            int intensity = IntensitySlider.ReadOr(ModSettings.TyphoonIntensity.value);

            TyphoonHub.Request(new TyphoonRequestData(TyphoonRequest.Start, hit, intensity));

            // Once pointed, the job is done. Do not let a held button point at a second spot
            // (the sim side also makes only one at a time, but that just produces a refusal
            //  message, which looks like "pressing does nothing").
            //
            // ★ **Do not open a panel here.** Vanilla's disaster buttons do not pop up an
            //   explanation window after you point, either. The typhoon's state is read from
            //   the shortcut in the top-left.
            Deactivate();
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
