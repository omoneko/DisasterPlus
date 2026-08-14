using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// カーソル直下の地面を求める。**main スレッド専用。**
    ///
    /// <see cref="EarthquakePanel"/> から切り出したのは、あのファイルがプロジェクト規約の
    /// 800 行を超えていたためで、**計算も間引きの間隔も 1 つも変えていない**。
    ///
    /// 計算そのものは①の <c>ForecastPanel.TryPickCursorGround</c> と同じで、
    /// **引く頻度だけ**を <see cref="RepickIntervalFrames"/> で縛ってある。
    ///
    /// **共通化はしていないが、①側も同じ形に揃えてある**（Task 7 で
    /// <c>ForecastPanel.TryPickCursorGround</c> にも同じ間引きを入れた）。
    /// 片方だけ直すと、この MOD に無制限なサンプリング経路が 1 本残る。
    /// 一方を変えるときはもう一方も見ること。
    /// </summary>
    internal static class EarthquakeCursorPicker
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// カーソル地点のレイを実際に引き直す間隔（描画フレーム数）。
        ///
        /// ── なぜ間引くのか（①からの持ち越しの是正）─────────────────────
        ///
        /// <see cref="TryPick"/> は地形と交差するまで
        /// <c>MaxRayDistance / 16m</c> ＝ **最大 500 回**の高さサンプリングを行い、
        /// 当たれば二分法が 20 回追加される。**いちばん高くつくのは「外す」場合**
        /// （地平線をかすめるレイ）で、これは視点を動かしている間に普通に起きる。
        ///
        /// ①はこの費用を「ハザード情報ビューを開いている間しか走らないので許容」と
        /// して意図的に未最適化のまま残した。**Task 4 でその前提が変わった** ——
        /// 地震が進行中ならパネルは毎フレームこのレイを引くので、カメラが揺れ、
        /// 建物が倒れ、パーティクルが出ている**いちばん重いフレーム**に重なる。
        ///
        /// 直し方は「引く回数を上限で縛る」。4 フレームに 1 回だけ引き、それ以外の
        /// フレームは直前の結果を返す。**表示する値そのものは変えない**（同じ計算の
        /// 結果を、最大 3 フレーム（60fps で 50ms 未満）遅れて出すだけ）。
        /// 建物の余裕度が既に 1 sim tick 遅れて届く設計（<see cref="BuildingProbe"/>）と
        /// 同じ性質の、目に見えない遅延である。
        ///
        /// 1 にすると毎フレーム引く（＝この是正が無効になる）。大きくすると
        /// カーソル追従が目に見えて遅れる。
        /// </summary>
        private const int RepickIntervalFrames = 4;

        /// <summary>
        /// Unity 5.6 の <c>Camera.main</c> はタグ検索で、パネル表示中は毎フレーム
        /// 呼ばれうるパスなのでキャッシュする（①のレビュー指摘）。fake-null
        /// （破棄済みカメラ）を拾えるよう Unity の <c>== null</c> 判定に任せ、
        /// 素の参照比較はしない。
        /// </summary>
        private static Camera _mainCameraCache;

        /// <summary>直近に実際にレイを引いたフレーム（<c>Time.frameCount</c>）と、その結果。</summary>
        private static int _pickFrame;
        private static bool _pickCached;
        private static Vec3 _pickHit;
        private static bool _pickOk;

        /// <summary>
        /// レベルアンロード時。**セッション状態を 1 つも持ち越さない** ——
        /// 次の都市が前の都市のカーソル地点を 1 回でも返さないようにする。
        /// </summary>
        internal static void Reset()
        {
            _pickCached = false;
            _pickFrame = 0;
            _pickOk = false;
            _pickHit = new Vec3(0f, 0f, 0f);
            // fake-null 経由でも次回 Camera.main を引き直せるが、都市をまたいで
            // 古い参照を抱え続けない、という本プロジェクトの原則を明示的に守る。
            _mainCameraCache = null;
        }

        /// <summary>
        /// <c>UIView.IsInsideUI()</c> の 1 行は間引きの**外**に置く。パネルを読んでいる間
        /// ——マウスがパネルの上にある間——はサンプリング自体が起きないので、
        /// これがいちばん効く早期打ち切りであり、キャッシュより先に判定したい。
        /// またこの経路では「カーソルが無効になったこと」を遅らせずに伝えられる
        /// （遅らせると、UI の上にマウスを載せた後も数フレーム古い地点を指し続ける）。
        /// </summary>
        internal static bool TryPick(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // パネルやその他の UI の上にマウスがあるときは意味のある地点が無い。
            if (UIView.IsInsideUI())
            {
                // 次にカーソルが地形へ戻ったとき、UI の上に載る前の古い地点を
                // そのまま返さないよう、キャッシュを捨てる。
                _pickCached = false;
                return false;
            }

            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            var cam = _mainCameraCache;
            if (cam == null)
            {
                _pickCached = false;
                return false;
            }

            // ★ ここが持ち越しの是正。最大 501 回の高さサンプリングは
            //    RepickIntervalFrames フレームに 1 回しか走らない。
            int frame = Time.frameCount;
            if (_pickCached && frame - _pickFrame < RepickIntervalFrames)
            {
                hit = _pickHit;
                return _pickOk;
            }

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 d = ray.direction.normalized;

            _pickOk = RayGeometry.IntersectTerrain(
                new Vec3(ray.origin.x, ray.origin.y, ray.origin.z),
                new Vec3(d.x, d.y, d.z),
                TerrainHeightSampler.Instance,
                MaxRayDistance,
                out _pickHit);
            _pickFrame = frame;
            _pickCached = true;

            hit = _pickHit;
            return _pickOk;
        }
    }
}
