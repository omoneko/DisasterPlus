using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風の発生地点を指すツール。**main スレッド専用。**
    /// ⑤の <see cref="VolcanoPlacementTool"/> をそのまま写している。
    ///
    /// ── なぜ在るのか ─────────────────────────────────────
    ///
    /// バニラの災害ボタンは「押す → カーソルが構わる → 地図をクリック → その地点に
    /// 出現の遅れを置いて災害が起きる」である。④のタイルは以前パネルを開くだけで、
    /// 発生はパネルの中のボタンだった（＝押した地点という概念が無かった）。
    /// **バニラと同じ挙動にする**というのが所有者の依頼で、その地点を運ぶのが
    /// このツールである（<see cref="TyphoonRequestData"/>）。
    ///
    /// ── ⑤と違い、クリックは「起こす」である ──────────────────────
    ///
    /// ⑤の配置ツールが積むのは <c>Survey</c>（調べるだけ）で、実際に地形を壊すのは
    /// プレイヤーが確認の行を読んでから押したときだけである。**⑤の地形変更は
    /// 取り消せない**からで、④にその制約は無い（台風は通り過ぎる）。
    /// したがって④はバニラの災害ボタンと同じく<b>クリックした時点で確定</b>する。
    ///
    /// ── 登録しないと <c>SetTool&lt;T&gt;()</c> は黙って空振りする ─────────────
    ///
    /// <c>ToolController.m_tools</c> は <c>Awake</c> で一度だけ構築され、
    /// <c>ToolsModifierControl.SetTool&lt;T&gt;</c> は静的辞書を引くだけなので、
    /// **起動後に足したツールはどちらにも入っていない。**
    /// <see cref="ToolRegistration.Register{T}"/> を<b>毎レベルロードで</b>呼ぶこと
    /// （<c>TyphoonFeature.OnLevelLoaded</c>）。呼び忘れると
    /// 「ボタンは押せるのにカーソルが変わらない」という、例外の出ない壊れ方をする。
    ///
    /// ── 地点の取り方 ─────────────────────────────────────
    ///
    /// 地形に Unity のコライダーは無いので <c>Physics.Raycast</c> は**絶対に当たらない**。
    /// カメラレイと高さ場の交差を Core の <see cref="RayGeometry.IntersectTerrain"/> で
    /// 自前に解く（⑤と同じ経路。既に出荷され実機で動いており、Core のテストが掛かっている）。
    ///
    /// ── ここから <c>DisasterManager</c> に触らない ────────────────────
    ///
    /// 災害の生成は sim スレッドの仕事で、④は <see cref="TyphoonHub"/> の依頼経路を
    /// 既に持っている。ここで <c>SimulationManager.AddAction</c> を足すと経路が 2 本になり
    /// 「どちらが先に走るか」が生まれる（⑤が同じ判断をしている）。
    /// </summary>
    public class TyphoonPlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// このツールが今アクティブか。**main スレッドから呼ぶこと**
        /// （<c>ToolsModifierControl</c> は UI 側の型である）。
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

        public static void Activate()
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null) { Log.Warn("toolController not available"); return; }

            var tool = controller.GetComponent<TyphoonPlacementTool>();
            if (tool == null) { Log.Warn("typhoon placement tool not registered"); return; }
            controller.CurrentTool = tool;
        }

        /// <summary>
        /// 既定のツールへ戻す。**アクティブでないときは何もしない** ——
        /// レベルアンロードの後始末から無条件に呼ぶと、他 MOD が選んでいたツールを
        /// 横から既定へ戻すことになる。
        /// </summary>
        public static void Deactivate()
        {
            try
            {
                if (!IsActive) return;
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

            // 機能を切ったのにツールだけ生き残っていたら、そこで畳む
            // （パネルもタイルも既に撤去されているので、指しても行き先が無い）。
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

            TyphoonHub.Request(new TyphoonRequestData(TyphoonRequest.Start, hit));

            // 指したら用は済んでいる。押しっぱなしで 2 つ目を指させない
            // （sim 側も同時に 1 個しか作らないが、それは断り文が出るだけで
            //  「押しても何も起きない」に見える）。
            Deactivate();

            // 発生までには設計上 1 tick の遅れがある。パネルが開いていないと
            // 「押したのに何も起きない」に見えるので、結果の出る場所を開く。
            TyphoonPanel.Show();
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
