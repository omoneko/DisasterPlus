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
    /// バニラの災害ボタンは「押す → **強度スライダーが出る** → 地図をクリック →
    /// その地点に出現の遅れを置いて災害が起きる」である。④もこの 3 手に揃える ——
    /// 入口は <see cref="Arm"/> で、地点と<b>そのとき選ばれていた強度</b>を
    /// <see cref="TyphoonRequestData"/> が運ぶ。スライダーはバニラのものをそのまま
    /// 借りる（<see cref="IntensitySlider"/>）。
    ///
    /// ★ **ここからパネルは開かない。** 起こすことと読むことは別で、
    ///   ④の情報は左上のショートカットの側にある（所有者の指摘
    ///   「あれこれ説明は出さなくていい」）。
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

        /// <summary>
        /// ★★ **災害パネルの④タイルの動作。バニラの災害ボタンと同じ 3 手である。**
        ///
        ///   1. タイルを押す      → カーソルが構わり、**強度スライダーが出る**
        ///   2. スライダーを動かす → 台風の強度（バニラの <c>m_intensity</c> と同じ意味）
        ///   3. 地図をクリック    → その地点で台風が始まる
        ///
        /// **説明のパネルはもう開かない。** 情報は左上のショートカットの側にある
        /// （所有者の指摘「あれこれ説明は出さなくていい」）。ここで開くと
        /// 「起こす」と「読む」が同じ操作に戻る。
        ///
        /// 起こせない環境（Natural Disasters 非所持）ではタイル自体が押せない
        /// （<see cref="DisasterPanelBar"/> が無効化し、理由をツールチップに出す）。
        /// それでも押せてしまう経路が将来足されたときのために、ここでも門を置く。
        /// </summary>
        public static void Arm()
        {
            if (!ModSettings.TyphoonEnabled.value) return;
            if (!ModCompat.NaturalDisastersOwned) return;

            Activate();
            if (!IsActive) return;

            // ★ 構えるたびに既定値を入れる。スライダーは 1 本しかないのに、
            //   ④と⑤では数字の意味が違う（IntensitySlider のクラス doc）。
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
        /// 既定のツールへ戻す。**アクティブでないときは何もしない** ——
        /// レベルアンロードの後始末から無条件に呼ぶと、他 MOD が選んでいたツールを
        /// 横から既定へ戻すことになる。
        /// </summary>
        public static void Deactivate()
        {
            try
            {
                if (!IsActive) return;

                // ★ 強度スライダーを畳むのは**このツールが構えていたときだけ**。
                //   無条件に畳むと、バニラの災害を構えている人のスライダーを横から消す。
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

            // ★ 強度は**クリックした瞬間のスライダーの値**である（バニラと同じ）。
            //   読めない環境では設定画面の値へ落とす —— 読めなかったことを
            //   0（＝いちばん弱い、という有効な値）で表さない。
            int intensity = IntensitySlider.ReadOr(ModSettings.TyphoonIntensity.value);

            TyphoonHub.Request(new TyphoonRequestData(TyphoonRequest.Start, hit, intensity));

            // 指したら用は済んでいる。押しっぱなしで 2 つ目を指させない
            // （sim 側も同時に 1 個しか作らないが、それは断り文が出るだけで
            //  「押しても何も起きない」に見える）。
            //
            // ★ **ここでパネルを開かない。** バニラの災害ボタンも、指したあとに
            //   説明の窓を出したりしない。台風の状態は左上のショートカットから読む。
            Deactivate();
        }

        /// <summary>
        /// **バニラと同じ的（まと）を出す**（2026-08-22、所有者の依頼
        /// 「ほかの災害と同様のターゲティングマークを使いたいです」）。
        ///
        /// 描き直してはいない —— <c>DisasterTool.RenderOverlay</c> をそのまま呼ぶ
        /// （<see cref="PlacementMarker"/> のクラス doc に IL 実測）。
        ///
        /// ★ 地面を指していないフレームは何も描かない。**前の位置に置き去りにしない。**
        /// </summary>
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            if (UIView.IsInsideUI()) return;

            Vec3 hit;
            if (!TryPickGround(out hit)) return;

            // ★ 色はバニラの災害ツールと同じ作り方。警告でも異常でも
            //   ないので両方 false ＝ 通常色である。
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
