using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地点を指すときの**バニラと同じ的（まと）**を出す。main スレッド専用。
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 台風・火山それぞれ場所を指定するときにほかの災害と同様の
    /// &gt; ターゲティングマークを使いたいです。
    ///
    /// ── ★★ 描き直さない。**バニラのメソッドをそのまま呼ぶ** ─────────────────
    ///
    /// <c>DisasterTool.RenderOverlay(CameraInfo, DisasterInfo, Vector3, float, Color)</c> は
    /// <b><c>public static</c></b> である（IL 実測）。バニラの災害が的を出しているのは
    /// この 1 本で、⑤と④のツールからも同じものを呼べる。
    /// **同じ絵になることが保証される**のが要点で、似せて描くのとは違う。
    ///
    /// 中身も読んである:
    ///
    /// <code>
    /// if (info == null) return;                       ← info は**これだけにしか使わない**
    /// if (ToolController.m_mode &amp; 16) → DrawCircle(半径 100)          （情報ビュー）
    /// else if (m_mode &amp; 1)          → DrawQuad(400×400,
    ///                                     DisasterProperties.m_targetTexture) （通常）
    /// </code>
    ///
    /// ★★ <b><c>info</c> は null 検査にしか使われない。</b>
    ///   だから「⑤にふさわしい <c>DisasterInfo</c>」を探す必要は無く、
    ///   読み込まれているものを 1 つ借りれば的の絵は同じである
    ///   （<see cref="AnyDisasterInfo"/>）。**推測ではなく IL で確かめてある。**
    ///
    /// ★ 1 つも読み込まれていない環境（ND DLC 非所持など）では的が出ない。
    ///   そのときも<b>クリックそのものは効く</b> —— 的は目印であって、門ではない。
    /// </summary>
    public static class PlacementMarker
    {
        private static DisasterInfo _cached;
        private static bool _searched;
        private static bool _missLogged;

        /// <summary>
        /// 的を 1 つ描く。<paramref name="position"/> は世界座標。
        /// **描けない環境では黙って何もしない**（クラス doc）。
        /// </summary>
        /// <param name="color">
        /// 的の色。**呼び出し元が <c>ToolBase.GetToolColor</c> で作ること** ——
        /// あれは <c>protected</c> なので、<c>ToolBase</c> を継いでいるツールからしか
        /// 呼べない（この型はツールでは無い）。
        /// </param>
        public static void Render(RenderManager.CameraInfo cameraInfo, Vector3 position,
                                  Color color)
        {
            if (cameraInfo == null) return;

            DisasterInfo info = AnyDisasterInfo();
            if (info == null) return;

            DisasterTool.RenderOverlay(cameraInfo, info, position, 0f, color);
        }

        /// <summary>レベルアンロード時。**参照を持ち越さない。**</summary>
        public static void Reset()
        {
            _cached = null;
            _searched = false;
            // _missLogged は戻さない（この環境に対する事実である）。
        }

        /// <summary>
        /// 読み込まれている <c>DisasterInfo</c> を 1 つ。**どれでもよい**
        /// （クラス doc の IL 実測 —— null 検査にしか使われない）。
        ///
        /// 探すのは 1 度だけ。<c>PrefabCollection</c> の走査を毎フレームやらない。
        /// </summary>
        private static DisasterInfo AnyDisasterInfo()
        {
            if (_cached != null) return _cached;
            if (_searched) return null;

            _searched = true;

            try
            {
                int count = PrefabCollection<DisasterInfo>.LoadedCount();
                for (uint i = 0; i < count; i++)
                {
                    DisasterInfo info = PrefabCollection<DisasterInfo>.GetLoaded(i);
                    if (info != null)
                    {
                        _cached = info;
                        return _cached;
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Diag("placementMarker", "prefab scan failed: " + e.GetType().Name);
                return null;
            }

            if (!_missLogged)
            {
                _missLogged = true;
                Log.Info("placement marker: no DisasterInfo is loaded in this build, so the "
                         + "vanilla targeting mark is not drawn. Clicking still places the "
                         + "disaster");
            }
            return null;
        }
    }
}
