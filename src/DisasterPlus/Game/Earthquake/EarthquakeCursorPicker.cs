using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Works out the ground under the cursor. **Main thread only.**
    ///
    /// It was split out of <see cref="EarthquakePanel"/> because that file had grown past
    /// the project's 800-line rule, and **neither the maths nor the throttle interval was
    /// changed by so much as one value**.
    ///
    /// The calculation itself is the same as feature ①'s
    /// <c>ForecastPanel.TryPickCursorGround</c>; **only how often the ray is cast** is
    /// bounded, by <see cref="RepickIntervalFrames"/>.
    ///
    /// **The two are not shared code, but ① has been brought into line** (Task 7 put the
    /// same throttle into <c>ForecastPanel.TryPickCursorGround</c>). Fix only one of them
    /// and this mod is left with one unbounded sampling path. When you change one, look
    /// at the other.
    /// </summary>
    internal static class EarthquakeCursorPicker
    {
        private const float MaxRayDistance = 8000f;

        /// <summary>
        /// How often the cursor ray is actually re-cast (in rendered frames).
        ///
        /// ── Why throttle at all (fixing something carried over from ①) ───────
        ///
        /// <see cref="TryPick"/> samples the height <c>MaxRayDistance / 16m</c> =
        /// **up to 500 times** before it meets the terrain, plus 20 more bisection steps
        /// once it hits. **The most expensive case is a miss** (a ray that grazes the
        /// horizon), and that happens routinely while the camera is being moved.
        ///
        /// ① deliberately left this cost unoptimised on the grounds that it only runs
        /// while the hazard info view is open. **Task 4 changed that assumption**: with
        /// an earthquake in progress the panel casts this ray every frame, so it lands on
        /// **the heaviest frames of all** — camera shaking, buildings collapsing,
        /// particles spawning.
        ///
        /// The fix is to put a ceiling on how often it is cast. Cast once every 4 frames
        /// and return the previous result on the others. **The displayed value itself is
        /// unchanged** — it is the result of the same calculation, just up to 3 frames
        /// late (under 50 ms at 60 fps). It is the same kind of invisible lag as the
        /// design where a building's headroom already arrives one sim tick late
        /// (<see cref="BuildingProbe"/>).
        ///
        /// Set it to 1 and the ray is cast every frame (i.e. this fix is disabled). Make
        /// it larger and the cursor tracking lags visibly.
        /// </summary>
        private const int RepickIntervalFrames = 4;

        /// <summary>
        /// <c>Camera.main</c> in Unity 5.6 is a tag search, and this path can be hit every
        /// frame while the panel is up, so it is cached (raised in ①'s review). Leave the
        /// check to Unity's <c>== null</c> operator so that a fake-null (a destroyed
        /// camera) is caught; never compare references directly.
        /// </summary>
        private static Camera _mainCameraCache;

        /// <summary>The frame the ray was last actually cast (<c>Time.frameCount</c>), and its result.</summary>
        private static int _pickFrame;
        private static bool _pickCached;
        private static Vec3 _pickHit;
        private static bool _pickOk;

        /// <summary>
        /// On level unload. **Carry over no session state whatsoever** — make sure the
        /// next city never once returns the previous city's cursor position.
        /// </summary>
        internal static void Reset()
        {
            _pickCached = false;
            _pickFrame = 0;
            _pickOk = false;
            _pickHit = new Vec3(0f, 0f, 0f);
            // The fake-null path would re-fetch Camera.main next time anyway, but this
            // spells out the project's rule: never hold on to a stale reference across
            // cities.
            _mainCameraCache = null;
        }

        /// <summary>
        /// The one <c>UIView.IsInsideUI()</c> line sits **outside** the throttle. While
        /// the panel is being read — while the mouse is over it — no sampling happens at
        /// all, so this is the most effective early-out there is and it wants to be
        /// checked before the cache. This path also lets "the cursor has become invalid"
        /// through without delay (delay it, and the readout keeps pointing at a stale
        /// position for several frames after the mouse moves onto the UI).
        /// </summary>
        internal static bool TryPick(out Vec3 hit)
        {
            hit = new Vec3(0f, 0f, 0f);

            // With the mouse over the panel or any other UI there is no meaningful
            // ground position.
            if (UIView.IsInsideUI())
            {
                // Throw the cache away, so that when the cursor next returns to the
                // terrain we do not hand back the stale position from before it moved
                // onto the UI.
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

            // ★ This is the fix for what was carried over. The up-to-501 height samples
            //    only run once every RepickIntervalFrames frames.
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
