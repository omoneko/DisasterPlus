using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Shows **the same target marker as vanilla** when pointing at a spot. Main thread only.
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; When picking a spot for the typhoon and the volcano, I'd like to use the same
    /// &gt; targeting mark as the other disasters.
    ///
    /// ── ★★ Do not redraw it. **Call vanilla's method as-is** ─────────────────
    ///
    /// <c>DisasterTool.RenderOverlay(CameraInfo, DisasterInfo, Vector3, float, Color)</c> is
    /// <b><c>public static</c></b> (measured from the IL). This one method is what draws the
    /// marker for vanilla's disasters, and ⑤'s and ④'s tools can call the very same thing.
    /// The point is that **the picture is guaranteed to be identical**, which is not the same
    /// as drawing something that looks similar.
    ///
    /// The body has been read too:
    ///
    /// <code>
    /// if (info == null) return;                       ← info is used for **nothing else**
    /// if (ToolController.m_mode &amp; 16) → DrawCircle(radius 100)        (info view)
    /// else if (m_mode &amp; 1)          → DrawQuad(400×400,
    ///                                     DisasterProperties.m_targetTexture) (normal)
    /// </code>
    ///
    /// ★★ <b><c>info</c> is used only for the null check.</b>
    ///   So there is no need to hunt for "the <c>DisasterInfo</c> that suits ⑤"; borrow any
    ///   one that is loaded and the marker looks the same
    ///   (<see cref="AnyDisasterInfo"/>). **Confirmed in the IL, not guessed.**
    ///
    /// ★ In an environment where none is loaded (ND DLC not owned, and so on) no marker
    ///   appears. <b>The click itself still works</b> even then — the marker is a guide,
    ///   not a gate.
    /// </summary>
    public static class PlacementMarker
    {
        private static DisasterInfo _cached;
        private static bool _searched;
        private static bool _missLogged;

        /// <summary>
        /// Draw one marker. <paramref name="position"/> is in world coordinates.
        /// **Quietly does nothing where it cannot be drawn** (see the class doc).
        /// </summary>
        /// <param name="color">
        /// The marker colour. **The caller must produce it with <c>ToolBase.GetToolColor</c>** —
        /// that is <c>protected</c>, so it can only be called from a tool deriving from
        /// <c>ToolBase</c> (this type is not a tool).
        /// </param>
        public static void Render(RenderManager.CameraInfo cameraInfo, Vector3 position,
                                  Color color)
        {
            if (cameraInfo == null) return;

            DisasterInfo info = AnyDisasterInfo();
            if (info == null) return;

            DisasterTool.RenderOverlay(cameraInfo, info, position, 0f, color);
        }

        /// <summary>On level unload. **Do not carry the reference over.**</summary>
        public static void Reset()
        {
            _cached = null;
            _searched = false;
            // _missLogged is not reset (it is a fact about this environment).
        }

        /// <summary>
        /// Any one loaded <c>DisasterInfo</c>. **It does not matter which**
        /// (measured from the IL in the class doc — it is used only for the null check).
        ///
        /// The search happens only once. Do not sweep <c>PrefabCollection</c> every frame.
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
