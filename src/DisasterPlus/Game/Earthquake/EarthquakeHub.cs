using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The sim thread Publishes and the main thread reads Latest.
    /// Same shape as feature ①'s <see cref="ForecastHub"/> (net35 has no Concurrent
    /// collections, so it is a single plain lock).
    /// <see cref="EarthquakeSnapshot"/> is immutable, so handing out the reference is safe.
    ///
    /// **Task 5 added one path in the opposite direction** (the cursor position goes
    /// main → sim). Guard **both directions with the same single <c>_gate</c>**. Add a
    /// second lock and you create a lock-ordering problem — a class of trouble this mod
    /// has never had to deal with. What is inside is only a Vec3 and a bool, so one lock
    /// causes no contention.
    /// </summary>
    public static class EarthquakeHub
    {
        private static readonly object _gate = new object();
        private static EarthquakeSnapshot _latest;

        private static Vec3 _cursor;
        private static bool _cursorValid;

        public static void Publish(EarthquakeSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>Null until something has been published. The caller must check for it.</summary>
        public static EarthquakeSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// **From the main thread.** Hands the terrain position under the cursor to the
        /// sim side.
        ///
        /// The cursor position comes from <c>Input.mousePosition</c> and
        /// <c>Camera.main</c>, so it can only be read on the main thread. Looking up the
        /// building underneath it, on the other hand, needs <c>BuildingManager</c>'s
        /// buffers (owned by the sim thread). This one method is the bridge, and the
        /// sending side passes nothing but the position.
        ///
        /// Pass <c>valid = false</c> when the panel is closed or the cursor is not over
        /// terrain. Fail to, and the sim side goes on probing the last position it saw,
        /// forever.
        /// </summary>
        public static void PublishCursor(Vec3 pos, bool valid)
        {
            lock (_gate) { _cursor = pos; _cursorValid = valid; }
        }

        /// <summary>**From the sim thread.** Reads the last position that was published.</summary>
        public static bool TakeCursor(out Vec3 pos)
        {
            lock (_gate) { pos = _cursor; return _cursorValid; }
        }

        /// <summary>On level load/unload. Never carry state across from one city to the next.</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                _cursor = new Vec3(0f, 0f, 0f);
                // ★ Forget to reset this and the next city spends one tick probing the
                //   previous city's position.
                _cursorValid = false;
            }
        }
    }
}
