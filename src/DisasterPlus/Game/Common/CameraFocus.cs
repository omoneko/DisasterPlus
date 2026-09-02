using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>いまカメラが見ている地点を、sim スレッドから読めるようにしておく箱。</b>
    ///
    /// ── なぜ要るのか（2026-09-02、所有者の指示）────────────────────────
    ///
    /// &gt; 重くなるのは避けたいので拡大して描画されているものにだけ
    /// &gt; 影響が出るようにするのがいいんでしょうか。
    ///
    /// **その通りである。** マップ全体を相手にすると費用が都市の規模に比例するが、
    /// <b>カメラの周りだけ</b>なら<b>ズームアウトしても増えない</b>（遠景では
    /// 対象そのものが減る）。見えないところの演出は誰も得をしない。
    ///
    /// ★★ ただし <c>Camera.main</c> は<b>main スレッドからしか触れない</b>。
    ///   sim スレッドから呼ぶと、例外が出ないまま値が壊れる類の事故になる
    ///   （このプロジェクトの「スレッド境界」の規則）。だから
    ///   <b>main で書いて、sim で読む</b>。1 フレーム古い位置を読んでも、
    ///   風が当たる場所が 1 フレームぶんずれるだけなので錠は要らない。
    /// </summary>
    public static class CameraFocus
    {
        private static volatile bool _valid;
        private static float _x;
        private static float _z;
        private static float _height;

        /// <summary>カメラの地点が取れているか。</summary>
        public static bool Valid { get { return _valid; } }

        /// <summary>カメラの真下あたりのワールド X。</summary>
        public static float X { get { return _x; } }

        /// <summary>同 Z。</summary>
        public static float Z { get { return _z; } }

        /// <summary>
        /// カメラの高さ（m）。**見えている範囲の広さの目安**に使う ——
        /// 引いているほど広く映るので、影響範囲もそれに比例させる。
        /// </summary>
        public static float Height { get { return _height; } }

        /// <summary>**main スレッド。** 毎フレーム呼んでよい。</summary>
        public static void Update()
        {
            Camera cam = Camera.main;
            if (cam == null) { _valid = false; return; }

            Vector3 at = cam.transform.position;
            Vector3 forward = cam.transform.forward;

            // ★ カメラは斜め下を向いているので、真下ではなく<b>見ている先</b>を採る。
            //   地面と交わるところまで前方へ伸ばす（水平に近いときは伸ばしすぎない）。
            float drop = at.y;
            float down = -forward.y;

            if (down > 0.15f && drop > 0f)
            {
                float t = drop / down;
                if (t > 4000f) t = 4000f;
                at += forward * t;
            }

            _x = at.x;
            _z = at.z;
            _height = drop;
            _valid = true;
        }

        /// <summary>都市を出るときに呼ぶ。</summary>
        public static void Reset()
        {
            _valid = false;
            _height = 0f;
        }
    }
}
