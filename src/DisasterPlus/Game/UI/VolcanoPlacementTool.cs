using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火山を置く地点を指すツール。**main スレッド専用。**
    /// ③の <see cref="FireWhirlPlacementTool"/> をそのまま写している。
    ///
    /// ★★ <b>クリックが「作る」である（2026-08-21 変更）。</b> 積むのは
    /// <see cref="VolcanoRequest.Place"/> 1 件で、**確認の窓はもう出ない** ——
    /// 所有者の指示により、⑤はほかの災害とまったく同じ
    /// 「タイル → スライダー → 地図をクリック」で起きる
    /// （<see cref="VolcanoState"/> のクラス doc）。
    ///
    /// **地形の変更は今も取り消せない。** 消えたのは着手前の門であって、
    /// 不可逆であることそのものではない（火山タブの常設警告が名乗り続ける）。
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

        /// <summary>
        /// ★★ **災害パネルの⑤タイルの動作。バニラの災害ボタンと同じ 3 手である。**
        ///
        ///   1. タイルを押す      → カーソルが構わり、**スライダーが出る**
        ///   2. スライダーを動かす → 山の大きさ（設定サイズに対する倍率。
        ///                          <see cref="VolcanoSizeScale"/>）
        ///   3. 地図をクリック    → **そこに火山ができる**
        ///
        /// **窓は 1 枚も開かない。** 説明のパネルも確認の窓も出ない ——
        /// ⑤の状態・影響範囲の数・不可逆の警告は、左上のショートカットの
        /// 「火山」タブと診断ダンプにある。
        /// </summary>
        public static void Arm()
        {
            if (!ModSettings.VolcanoEnabled.value) return;
            if (!VolcanoReader.ScanTerrainFacts().Usable) return;

            Activate();
            if (!IsActive) return;

            // ★ 構えるたびに既定値（倍率 1.0 ＝ 設定どおりのサイズ）を入れる。
            //   スライダーは 1 本しかないのに、④と⑤では数字の意味が違う
            //   （IntensitySlider のクラス doc）。
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
        /// 既定のツールへ戻す。**アクティブでないときは何もしない** ——
        /// レベルアンロードの後始末から無条件に呼ぶと、他 MOD が選んでいたツールを
        /// 横から既定へ戻すことになる。
        /// </summary>
        public static void Deactivate()
        {
            try
            {
                if (!IsActive) return;

                // ★ スライダーを畳むのは**このツールが構えていたときだけ**。
                //   無条件に畳むと、バニラの災害を構えている人のスライダーを横から消す。
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

            // ★ 大きさは**クリックした瞬間のスライダーの値**である（バニラと同じ）。
            //   読めない環境では倍率 1.0 ＝ 設定どおりのサイズへ落とす。
            float scale = VolcanoSizeScale.ScaleFor(
                IntensitySlider.ReadOr(VolcanoSizeScale.AnchorRaw));

            // ★ 積むのは「ここに作ってくれ」1 件（クラス doc）。実際に調べて壊し始める
            //   のは sim スレッドの VolcanoState.HandlePlace である ——
            //   **main スレッドから建物・道路・地形のバッファに触らない。**
            VolcanoHub.Request(new VolcanoRequestData(VolcanoRequest.Place, hit, scale));

            // 指したら用は済んでいる。押しっぱなしで 2 つ目を指させない。
            Deactivate();

            // ★ **黙って終わらない。** 何が起きたか（影響範囲の数・進行中の段・
            //   断られた理由）は火山タブと診断ダンプが名乗る。
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
