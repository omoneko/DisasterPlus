using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火山を置く地点を指すツール。**main スレッド専用。**
    /// ③の <see cref="FireWhirlPlacementTool"/> をそのまま写している。
    ///
    /// ★★ <b>このツールは「調べてくれ」としか言わない。</b> クリックが積むのは
    /// <see cref="VolcanoRequest.Survey"/> だけで、<c>Start</c> は 1 度も積まない。
    /// 実際に着手するのは、プレイヤーが確認の行を読んで
    /// <c>[この場所に火山を作る]</c> を押したときだけである
    /// （<see cref="VolcanoState"/> のクラス doc「確認は迂回できない」）。
    ///
    /// ── 登録しないと <c>SetTool&lt;T&gt;()</c> は黙って空振りする ─────────────
    ///
    /// <c>ToolController.m_tools</c> は <c>Awake</c> で一度だけ構築され、
    /// <c>ToolsModifierControl.SetTool&lt;T&gt;</c> は静的辞書を引くだけなので、
    /// **起動後に足したツールはどちらにも入っていない。**
    /// <see cref="ToolRegistration.Register{T}"/> を<b>毎レベルロードで</b>呼ぶこと
    /// （<c>VolcanoFeature.OnLevelLoaded</c>）。呼び忘れると
    /// 「ボタンは押せるのにカーソルが変わらない」という、例外の出ない壊れ方をする。
    ///
    /// ── 地点の取り方は③のものを使う（<c>TerrainManager.RayCast</c> に寄せない）────
    ///
    /// IL 事実文書 §B-6 は <c>TerrainManager.RayCast(Segment3, out Vector3)</c> が
    /// public であり、③の自前マーチを置き換えられると書いている。**⑤は置き換えない。**
    ///
    ///   - ③の <see cref="RayGeometry.IntersectTerrain"/> は**既に出荷され、実機で
    ///     動いており、Core のユニットテストが掛かっている**
    ///   - <c>TerrainManager.RayCast</c> は「存在する」ことが IL で確定しているだけで、
    ///     **本 MOD は一度も呼んだことがない**
    ///   - **地点の取り違えが起きる場所として、⑤の配置は本 MOD で最悪である。**
    ///     押した瞬間にプレイヤーの都市が不可逆に壊れる
    ///   - 得られるものは「自前マーチをやめられる」だけで、実測できるコストの差は無い
    ///
    /// なお地形に Unity のコライダーは無いので <c>Physics.Raycast</c> は**絶対に
    /// 当たらない**。当たらないのは不具合ではなく、そもそも登録されていない。
    ///
    /// ── ここから <c>SimulationManager.AddAction</c> を使わない ─────────────
    ///
    /// ③は使ったが、⑤は <see cref="VolcanoHub"/> の依頼経路を既に持っている。
    /// 経路を 2 本にすると「どちらが先に走るか」が生まれる（計画 §4.2）。
    /// </summary>
    public class VolcanoPlacementTool : ToolBase
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
                    return controller != null && controller.CurrentTool is VolcanoPlacementTool;
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

            var tool = controller.GetComponent<VolcanoPlacementTool>();
            if (tool == null) { Log.Warn("volcano placement tool not registered"); return; }
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
                Log.Warn("volcano placement tool deactivate failed: " + e.GetType().Name);
            }
        }

        protected override void OnToolUpdate()
        {
            base.OnToolUpdate();

            // 機能を切ったのにツールだけ生き残っていたら、そこで畳む
            // （パネルもボタンも既に撤去されているので、指しても行き先が無い）。
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

            // ★ 積むのは「調べてくれ」だけ（クラス doc）。ここで壊す判断はしない。
            VolcanoHub.Request(new VolcanoRequestData(VolcanoRequest.Survey, hit));

            // 指したら用は済んでいる。押しっぱなしで 2 つ目を指させない。
            Deactivate();

            // 調査の結果はパネルにしか出ない。開いていなければ開く ——
            // **黙って調べて黙って終わる**のが、⑤でいちばんしてはいけないことである。
            VolcanoPanel.Show();
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
