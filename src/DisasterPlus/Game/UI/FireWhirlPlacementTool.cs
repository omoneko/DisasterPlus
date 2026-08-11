using ColossalFramework;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// クリックした地点に火災旋風を発生させる。
    ///
    /// CS の地形は Unity Physics に登録されていないので Physics.Raycast は絶対に当たらない。
    /// カメラレイと高さ場の交差を Core の RayGeometry で自前に解く。
    /// </summary>
    public class FireWhirlPlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>手動発生の強度（byte）。自動発生より少し強めにする。</summary>
        private const byte ManualIntensity = 80;

        /// <summary>手動発生の初期規模。自動発生の閾値と揃える。</summary>
        private const int ManualBurningCount = 12;

        public static void Activate()
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null) { Log.Warn("toolController not available"); return; }

            var tool = controller.GetComponent<FireWhirlPlacementTool>();
            if (tool == null) { Log.Warn("placement tool not registered"); return; }
            controller.CurrentTool = tool;
        }

        public static void Deactivate()
        {
            ToolsModifierControl.SetTool<DefaultTool>();
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            if (Input.GetMouseButtonUp(1)) { Deactivate(); return; }
            if (!Input.GetMouseButtonUp(0)) return;
            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit))
            {
                Log.Diag("pick", "ray did not hit the terrain");
                return;
            }

            // 災害の生成は sim スレッドで行う。
            // main スレッドから災害・車両バッファを触ると、スタックトレースの無い
            // IndexOutOfRangeException が後から出て、自分の try/catch にも掛からない。
            SimulationManager.instance.AddAction(() =>
            {
                ushort disasterId;
                if (!FireWhirlSpawner.TrySpawn(hit, ManualIntensity, out disasterId)) return;

                FireWhirlRegistry.Add(disasterId, 0, hit,
                    FireWhirlStrength.RadiusFor(ManualBurningCount), ManualBurningCount);
            });
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
