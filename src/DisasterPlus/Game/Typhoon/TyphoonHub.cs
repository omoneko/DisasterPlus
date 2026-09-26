using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The only request that travels from the main thread to the sim thread.
    ///
    /// **A typhoon is something the player raises explicitly** (plan §3.1). The idea of
    /// finding a vanilla thunderstorm disaster and promoting it to a typhoon was
    /// rejected — once ④ raises the rain, the game itself can create a thunderstorm
    /// disaster under the <c>m_currentRain &gt; 0.8</c> condition (IL facts document
    /// §A-3), so a promotion scheme becomes self-breeding: ④'s rain makes the game
    /// create a storm, ④ promotes that to a typhoon, and that typhoon raises the rain
    /// again — and on top of that the player cannot tell that the mod is the cause.
    /// </summary>
    public enum TyphoonRequest
    {
        None,
        Start,

        // ★★ **Stop has been removed** (2026-09-02, the owner: "Please do not bring the
        //    subject of stopping back up. Nobody can stop a natural disaster. Delete the
        //    very concept of stopping.")
        //
        //    ★ The <b>method</b> <c>TyphoonController.Stop()</c> is still there. That one
        //      gathers into a single place the cleanup for how a typhoon <b>ends</b>
        //      (it left the map, or it used up its duration) and has nothing to do with
        //      anything the player does. What disappeared is only <b>the notion that the
        //      player can stop it</b>.
    }

    /// <summary>
    /// One request. **Shaped exactly like ⑤'s <see cref="VolcanoRequestData"/>.**
    ///
    /// ★★ ④'s tile now **arms a placement cursor** just like the vanilla disaster
    ///    buttons do, so the request <b>carries coordinates</b>
    ///    (<see cref="TyphoonPlacementTool"/>). Leave it as a bare enum and the sim side
    ///    would have to invent the "where" for itself.
    ///
    /// <see cref="Point"/> is not read when <see cref="Kind"/> is
    /// <see cref="TyphoonRequest.None"/>.
    /// </summary>
    public struct TyphoonRequestData
    {
        public readonly TyphoonRequest Kind;

        /// <summary>The world coordinate that was clicked (only meaningful for
        /// <c>Start</c>).</summary>
        public readonly Vec3 Point;

        /// <summary>
        /// **The raw value the vanilla intensity slider was pointing at** at the moment
        /// of the click (only meaningful for <c>Start</c>). Same meaning and same scale
        /// as vanilla's <c>DisasterData.m_intensity</c>; only the display divides it
        /// by 10.
        ///
        /// ★ What goes in here is "the value that was selected at the time", not the
        ///   value from the settings screen. The settings value is **the fallback for
        ///   environments with no slider**, and re-reading it on the sim side would make
        ///   the storm "start at a different intensity from the one you pressed".
        /// </summary>
        public readonly int Intensity;

        public TyphoonRequestData(TyphoonRequest kind, Vec3 point, int intensity)
        {
            Kind = kind;
            Point = point;
            Intensity = intensity;
        }

        /// <summary>A request that needs neither a point nor an intensity
        /// (<c>Stop</c>).</summary>
        public static TyphoonRequestData Of(TyphoonRequest kind)
        {
            return new TyphoonRequestData(kind, new Vec3(0f, 0f, 0f), 0);
        }

        /// <summary>"No request". The same as <c>default(TyphoonRequestData)</c>, but it
        /// states its intent.</summary>
        public static TyphoonRequestData None
        {
            get { return Of(TyphoonRequest.None); }
        }
    }

    /// <summary>
    /// The sim thread Publishes and the main thread reads <see cref="Latest"/>.
    /// Same shape as ①'s <c>ForecastHub</c> and ②'s <see cref="EarthquakeHub"/>
    /// (net35 has no <c>System.Collections.Concurrent</c>, so it is one plain lock).
    /// <see cref="TyphoonSnapshot"/> is immutable, so handing the reference over is safe.
    ///
    /// **T3 added a route in the opposite direction** (the panel's "raise a typhoon /
    /// stop it" as a <see cref="TyphoonRequest"/> passed main → sim). **It is guarded by
    /// the same single <c>_gate</c>.** Do not add a second lock — that would create a
    /// lock-ordering problem, a kind of problem this mod has never once had
    /// (<see cref="EarthquakeHub"/>'s class doc records the same decision).
    /// </summary>
    public static class TyphoonHub
    {
        private static readonly object _gate = new object();
        private static TyphoonSnapshot _latest;
        private static TyphoonRequestData _request;

        public static void Publish(TyphoonSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>null until something has been published. The caller must check.</summary>
        public static TyphoonSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// **From the main thread.** Queue exactly one button press.
        ///
        /// If the previous request has not been picked up by the sim yet, this
        /// **overwrites it**. "Last press wins" rather than "in press order" is the
        /// correct behaviour — someone who presses raise and then stop in quick
        /// succession only wants the latter.
        /// </summary>
        public static void Request(TyphoonRequestData request)
        {
            lock (_gate) { _request = request; }
        }

        /// <summary>
        /// **From the main thread (display only).** The request the sim has not picked
        /// up yet.
        ///
        /// It exists purely so the panel can show "requested". **By design it takes one
        /// tick** between the press and the typhoon actually appearing (the work happens
        /// in <see cref="TyphoonController"/> on the next sim tick), so without this
        /// accessor the panel would still read "no typhoon" right after the press and
        /// **the player would press it again**.
        ///
        /// Unlike <see cref="TakeRequest"/> this **does not take the request**.
        /// Consuming it here would make the sim's behaviour depend on whether the panel
        /// is open.
        /// </summary>
        public static TyphoonRequestData PendingRequest
        {
            get { lock (_gate) { return _request; } }
        }

        /// <summary>
        /// **From the sim thread.** Take the queued request and reset it to
        /// <see cref="TyphoonRequest.None"/>. **Call it exactly once per tick** (call it
        /// twice and the second call always sees None, which creates drops that depend
        /// on call order).
        /// </summary>
        public static TyphoonRequestData TakeRequest()
        {
            lock (_gate)
            {
                var r = _request;
                _request = TyphoonRequestData.None;
                return r;
            }
        }

        /// <summary>
        /// On level load/unload. Do not carry state across cities.
        /// </summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                // ★ Forget to reset this and a button pressed in the previous city fires
                //    the moment the next city finishes loading.
                _request = TyphoonRequestData.None;
            }
        }
    }
}
