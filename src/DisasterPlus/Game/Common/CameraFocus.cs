using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>A box that keeps the spot the camera is currently looking at readable from the sim
    /// thread.</b>
    ///
    /// ── Why it is needed (2026-09-02, the owner's instruction) ────────────────────────
    ///
    /// &gt; I want to avoid it getting heavy, so would it be better to only affect
    /// &gt; the things being drawn up close?
    ///
    /// **Exactly so.** Take on the whole map and the cost scales with the size of the city,
    /// whereas <b>around the camera only</b> <b>does not grow when you zoom out</b> (in a
    /// distant view there are fewer subjects in the first place). Effects nobody can see
    /// benefit nobody.
    ///
    /// ★★ But <c>Camera.main</c> <b>can only be touched from the main thread</b>.
    ///   Call it from the sim thread and you get the kind of accident where the value is
    ///   corrupted with no exception raised (this project's "thread boundary" rule). So
    ///   <b>write on main and read on sim</b>. Reading a position one frame old only shifts
    ///   where the wind lands by one frame, so no lock is needed.
    /// </summary>
    public static class CameraFocus
    {
        private static volatile bool _valid;
        private static float _x;
        private static float _z;
        private static float _height;

        /// <summary>Whether the camera's spot has been obtained.</summary>
        public static bool Valid { get { return _valid; } }

        /// <summary>World X roughly below the camera.</summary>
        public static float X { get { return _x; } }

        /// <summary>The same for Z.</summary>
        public static float Z { get { return _z; } }

        /// <summary>
        /// The camera's height (m). Used as **a guide to how wide the visible area is** —
        /// the further back you are the wider the view, so the affected area scales with it.
        /// </summary>
        public static float Height { get { return _height; } }

        /// <summary>**Main thread.** Safe to call every frame.</summary>
        public static void Update()
        {
            Camera cam = Camera.main;
            if (cam == null) { _valid = false; return; }

            Vector3 at = cam.transform.position;
            Vector3 forward = cam.transform.forward;

            // ★ The camera looks down at an angle, so take <b>where it is looking</b> rather
            //   than straight below. Extend forwards to where it meets the ground (do not
            //   extend too far when it is near horizontal).
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

        /// <summary>Call when leaving the city.</summary>
        public static void Reset()
        {
            _valid = false;
            _height = 0f;
        }
    }
}
