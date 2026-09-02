using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>カメラを地図上の 1 点へ飛ばす。</b>**main スレッド専用。**
    ///
    /// ── なぜ <c>CameraController.SetTarget</c> を使わないのか（2026-09-02、IL）──
    ///
    /// <c>SetTarget(InstanceID, Vector3, bool)</c> は<b>インスタンスを追う</b>ための
    /// 入口で、<c>InstanceID</c> が空だと
    ///
    /// <code>
    /// if (InstanceManager.FollowInstance(id) == false) { m_targetInstance = Empty; return; }
    /// </code>
    ///
    /// ★★ と、**何もせずに帰る**（IL_0011–001C → IL_0108）。
    ///   台風の中心は建物でも車両でもないので <c>InstanceID</c> を持てず、
    ///   この経路では<b>1 ピクセルも動かない</b>。
    ///
    /// ── だから目標位置を直接置く ─────────────────────────────────
    ///
    /// <c>m_targetPosition</c> は public フィールドで、
    /// <c>CameraController.UpdateTargetPosition</c> が<b>毎フレーム</b>
    /// <c>GameAreaManager.ClampPoint</c> を掛けている（IL で確認）。つまり
    ///
    /// <list type="bullet">
    /// <item><b>解禁していないタイルの外は、ゲームが自分で引き戻す。</b>
    ///   こちらで範囲を判定しなくてよい —— <b>これは重要である。</b>台風は
    ///   <b>マップの外から近づいてくる</b>ので、接近中の中心は必ず場外にある。
    ///   引き戻された結果は「台風がいる方角のマップ端」で、意味としても正しい。</item>
    /// <item>現在位置からは<b>補間で寄る</b>（<c>m_currentPosition</c> が追う）ので、
    ///   瞬間移動ではなくスクロールして見える。</item>
    /// </list>
    ///
    /// ★ <c>ClearTarget()</c> を先に呼ぶ。何かを追跡中だと、追跡側が
    ///   <c>m_targetPosition</c> を毎フレーム上書きして<b>こちらの指定が消える</b>。
    /// </summary>
    public static class CameraJump
    {
        private static CameraController _controller;

        /// <summary>
        /// <paramref name="position"/> へ寄る。**main スレッド。**
        ///
        /// <paramref name="size"/> が正なら寄り具合（＝カメラ距離）も合わせる。
        /// 0 以下なら今の寄り具合のままで、位置だけ動かす。
        /// </summary>
        /// <returns>飛べたら true。カメラが取れなければ false。</returns>
        public static bool To(Vector3 position, float size)
        {
            try
            {
                CameraController controller = Resolve();
                if (controller == null) return false;

                controller.ClearTarget();
                controller.m_targetPosition = position;

                if (size > 0f)
                {
                    // ★ 端は<b>ゲームの持っている限界</b>で挟む。自前の数字を置かない ——
                    //   置くと、MOD でズーム範囲を広げている人の環境で食い違う。
                    float min = controller.m_minDistance;
                    float max = controller.m_maxDistance;
                    if (max > min)
                    {
                        if (size < min) size = min;
                        if (size > max) size = max;
                        controller.m_targetSize = size;
                    }
                }

                return true;
            }
            catch (System.Exception e)
            {
                Log.Warn("could not move the camera: " + e.GetType().Name);
                return false;
            }
        }

        /// <summary>都市を出るときに呼ぶ（破棄済みの参照を持ち越さない）。</summary>
        public static void Reset()
        {
            _controller = null;
        }

        private static CameraController Resolve()
        {
            // Unity の == null なので、破棄済み（fake-null）なら引き直しになる。
            if (_controller == null) _controller = SceneObjects.FindInScene<CameraController>();
            return _controller;
        }
    }
}
