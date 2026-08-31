using ColossalFramework;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **海溝型地震**を起こす地点を指すツール。**main スレッド専用。**
    /// ⑤の <see cref="VolcanoPlacementTool"/> をそのまま写している。
    ///
    /// ── 所有者の指示（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 発生は、アイコンクリック→左クリックした場所に一番近い海で発生に
    /// &gt; してください。
    ///
    /// つまりバニラの災害ボタンと同じ 3 手だが、**指した場所そのものでは起きない**:
    ///
    ///   1. タイルを押す      → カーソルが構わり、**スライダーが出る**
    ///   2. スライダーを動かす → 地震の強度（バニラと同じ生値）
    ///   3. 地図をクリック    → **そこにいちばん近い海**で地震が起きる
    ///
    /// ★★ <b>指した場所と震源が違うことを、指す前に見せる。</b>
    ///   <see cref="RenderOverlay"/> は<b>実際に震源になる海の上</b>に的を出す ——
    ///   カーソルの下に的を出すと、クリックしてから「そこじゃない」と分かる。
    ///   海が見つからないフレームは<b>的を出さない</b>（それが「ここでは起きない」の合図）。
    ///
    /// ── 海の探索は main スレッドでやってよい ───────────────────────
    ///
    /// <c>TerrainManager.HasWater</c> / <c>WaterLevel</c> が読むのは
    /// <b>1 フレーム遅れた水のバッファ</b>で、建物や道路のグリッドと違い
    /// sim スレッドの所有物ではない（②の <c>LongPeriodDamage</c> が同じ経路を
    /// main から読んでいる）。**地震を起こすのは sim 側**である。
    /// </summary>
    public class TrenchQuakePlacementTool : ToolBase
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// 的を出すために海を探す間隔（フレーム）。**毎フレームは探さない** ——
        /// 最悪 <c>SeaSearch.CountUpTo(24)</c> ＝ 2,401 点を舐めるので、
        /// カーソルを動かすだけで実機が重くなる。
        /// </summary>
        private const int PreviewEveryFrames = 6;

        private static int _previewCountdown;
        private static bool _previewValid;

        /// <summary>sim へ注文を出して返事を待っているか（<see cref="OnToolUpdate"/>）。</summary>
        private static bool _awaitingRaise;

        /// <summary>注文を出した時点の <c>TrenchQuakeSlot.AttemptSerial</c>。</summary>
        private static int _awaitingSerial;

        /// <summary>断られた印を「不可」の色で描き続ける時刻（実時間）。</summary>
        private static float _refusedUntilRealtime;

        /// <summary>その長さ（実秒）。**短すぎると見落とす、長すぎると嘘になる。**</summary>
        private const float RefusedFlashSeconds = 2.5f;
        private static Vector3 _previewSea;

        /// <summary>このツールが今アクティブか。**main スレッドから呼ぶこと。**</summary>
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

        /// <summary>災害パネルの海溝型地震タイルの動作。</summary>
        public static void Arm()
        {
            if (!ModSettings.TrenchQuakeEnabled.value) return;

            Activate();
            if (!IsActive) return;

            // ★ 構えるたびに既定値を入れる。スライダーは 1 本しかないのに、
            //   ④⑤とこの機能では数字の意味が違う（IntensitySlider のクラス doc）。
            //   ここでの意味は**バニラの地震の強度そのもの**である。
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

        /// <summary>既定のツールへ戻す。**アクティブでないときは何もしない。**</summary>
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

            // ★★ sim の返事待ち。増えたら成否を見て、成功なら閉じる。
            if (_awaitingRaise)
            {
                if (TrenchQuakeSlot.AttemptSerial != _awaitingSerial)
                {
                    _awaitingRaise = false;
                    if (TrenchQuakeSlot.LastAttemptOk) { Deactivate(); return; }

                    // ★★ **断られたことを画面にも出す。**（第 5 回検証）
                    //    ログだけだと、ゲームを見ている人には分からない。
                    //    しばらく印を「不可」の色で描く（下の RenderOverlay）。
                    _refusedUntilRealtime = Time.realtimeSinceStartup + RefusedFlashSeconds;
                }

                // 返事が来るまでは次のクリックを受け付けない（注文を積み上げない）。
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

            // ★★ **印が出ていない所では起こさない。**（2026-08-30、最終検証）
            //    以前は地面に当たりさえすれば無条件に注文を積んでいた。
            //    海が見つからなければ sim 側が断るので何も起きないのだが、
            //    ツールは閉じてしまうので、プレイヤーには
            //    <b>「押したのに何も起きない」</b>としか映らない ——
            //    実機テストで成功と失敗を見分けられなくする、いちばん悪い壊れ方である。
            //
            //    いまは<b>ツールを開いたまま</b>断る。カーソルが出しっぱなしなのが
            //    「ここではない」の合図で、印が出る所まで動かせば起こせる。
            //
            //    ★ ここでは海を探さない。**探すのは sim 側だけ**である
            //      （同じ述語を 2 か所で走らせると、断りの理由が食い違う）。
            //      印はプレビューが同じ <c>SearchRings</c> で描いているので、
            //      印が出ている所なら通るはずだが、**保証はしない** ——
            //      通らなかったときは sim が理由を書き、ツールは開いたままになる。
            byte intensity = (byte)Clamp(IntensitySlider.ReadOr(DefaultIntensity), 1, 255);

            // ★★ **地震を起こすのは sim スレッドである。** 災害バッファは
            //    sim の所有物なので、ここからは注文を積むだけにする。
            //    ②には⑤のような依頼の口が無いので、③と同じく
            //    SimulationManager.AddAction で 1 回だけ渡す。
            Vec3 point = hit;

            // ★★ **返事を待ってから閉じる。**（2026-08-30、第 4 回検証）
            //    以前はここで即 <c>Deactivate()</c> していた。sim が断ったとき
            //    （海が無い・津波がまだ途中・DLC 非所持・災害枠が満杯）、
            //    画面では<b>印が出てツールが閉じて何も起きない</b>だけになり、
            //    <b>死んだボタンと見分けが付かなかった</b>。
            //
            //    いまは <c>AttemptSerial</c> が増えるのを待ち、
            //    <b>成功したときだけ閉じる</b>。断られたらツールは開いたままで、
            //    出しっぱなしのカーソルが「起きなかった」の合図になる。
            _awaitingSerial = TrenchQuakeSlot.AttemptSerial;
            _awaitingRaise = true;

            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                TrenchQuakeSlot.Raise(point, intensity);
            });
        }

        /// <summary>
        /// **実際に震源になる海の上**に的を出す（クラス doc）。
        /// 海が見つからないフレームは何も描かない ——
        /// <b>それが「ここでは起きない」の合図である。</b>
        /// </summary>
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            base.RenderOverlay(cameraInfo);

            if (UIView.IsInsideUI()) return;

            // ★ 探索は重い（最悪 2,401 点。錠は走査ごとに 1 回だが配列は舐める）ので
            //   間引く。あいだのフレームは
            //   前に見つけた海に的を出したままにする —— 消すとちらつく。
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

            // ★★ **断りは必ず見せる。**（2026-08-31、相互検証）
            //    この 2 行は長らく `if (!_previewValid) return;` の<b>下</b>に在った。
            //    ところが海の無いマップでは的が一度も有効にならないので、
            //    <b>断りの色も一度も描かれなかった</b> ——
            //    クリックしても、印も色も音も出ない「死んだボタン」に戻っていた。
            //    断られたときは<b>カーソルの位置に</b>不可の印を出す。
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

        /// <summary>ゲーム自身の災害の既定強度と同じ。</summary>
        private const int DefaultIntensity = 55;

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
