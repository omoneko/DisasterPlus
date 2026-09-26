using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Sends the camera to a point on the map.</b> **Main thread only.**
    ///
    /// ── Why <c>CameraController.SetTarget</c> is not used (2026-09-02, IL) ──
    ///
    /// <c>SetTarget(InstanceID, Vector3, bool)</c> is the entry point for <b>following an
    /// instance</b>, and with an empty <c>InstanceID</c> it
    ///
    /// <code>
    /// if (InstanceManager.FollowInstance(id) == false) { m_targetInstance = Empty; return; }
    /// </code>
    ///
    /// ★★ **goes home having done nothing** (IL_0011–001C → IL_0108).
    ///   A typhoon's centre is neither a building nor a vehicle, so it cannot have an
    ///   <c>InstanceID</c>, and down this path <b>not one pixel moves</b>.
    ///
    /// ── So set the target position directly ─────────────────────────────────
    ///
    /// <c>m_targetPosition</c> is a public field, and
    /// <c>CameraController.UpdateTargetPosition</c> applies
    /// <c>GameAreaManager.ClampPoint</c> to it <b>every frame</b> (confirmed in the IL). So
    ///
    /// <list type="bullet">
    /// <item><b>Outside the unlocked tiles, the game pulls it back itself.</b>
    ///   There is no need to range-check on our side — <b>and this matters.</b> A typhoon
    ///   <b>approaches from off the map</b>, so an incoming centre is always outside the
    ///   playable area. What the pull-back yields is "the map edge in the direction the
    ///   typhoon is in", which is also correct in meaning.</item>
    /// <item>From the current position it <b>eases in by interpolation</b>
    ///   (<c>m_currentPosition</c> follows), so it reads as scrolling rather than a
    ///   teleport.</item>
    /// </list>
    ///
    /// ★ Call <c>ClearTarget()</c> first. If something is being followed, the follow code
    ///   overwrites <c>m_targetPosition</c> every frame and <b>our setting disappears</b>.
    /// </summary>
    public static class CameraJump
    {
        private static CameraController _controller;

        /// <summary>
        /// Move to <paramref name="position"/>. **Main thread.**
        ///
        /// If <paramref name="size"/> is positive, the zoom (i.e. the camera distance) is set
        /// to match as well. At 0 or below the current zoom is kept and only the position
        /// moves.
        /// </summary>
        /// <returns>true if the jump happened. false if the camera could not be obtained.</returns>
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
                    // ★ Clamp the ends with <b>the limits the game itself holds</b>. Do not
                    //   put our own numbers there — do that and it disagrees with the setup
                    //   of anyone who has widened the zoom range with a mod.
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

        /// <summary>Call when leaving the city (do not carry a destroyed reference over).</summary>
        public static void Reset()
        {
            _controller = null;
        }

        private static CameraController Resolve()
        {
            // This is Unity's == null, so a destroyed one (fake-null) is looked up again.
            if (_controller == null) _controller = SceneObjects.FindInScene<CameraController>();
            return _controller;
        }
    }
}
