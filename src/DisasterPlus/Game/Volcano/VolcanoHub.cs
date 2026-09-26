using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The kinds of request passed from the main thread to the sim thread.
    ///
    /// **⑤ only begins once the player picks a spot.** There is no automatic triggering —
    /// terrain changes are irreversible (§E-13), they are baked into the save, and ⑤ has no undo,
    /// so "I turned round and there was a mountain in the middle of my city" must never happen
    /// (design doc §7.1).
    ///
    /// ★★ <b>The confirmation window (<c>Start</c> / <c>Cancel</c>) was removed on 2026-08-21.</b>
    /// The owner's instruction was "like the other disasters: tile → slider → click the map and it
    /// happens". So the only request main queues is <see cref="Place"/>
    /// ([Stop] was removed on 2026-08-22 as well. See the note below).
    /// **Do not add a request that waits for a "yes" back again.**
    /// </summary>
    public enum VolcanoRequest
    {
        None,

        /// <summary>
        /// Make a volcano at this spot. **It is settled the moment it is pressed** (there is no
        /// confirmation).
        ///
        /// On this one item the sim side goes all the way from "survey" to "start the clearing"
        /// (<c>VolcanoState.HandlePlace</c>). The survey itself remains —
        /// the building and road grids are owned by the sim thread, so
        /// **the affected range cannot be counted on main**.
        /// </summary>
        Place,

        // ★ Stop was removed on 2026-08-22. The owner's call:
        //   "a stop button isn't needed. After all, you can't actually stop an eruption in real
        //    life, can you?"
        //   Once triggered, ⑤ runs to the end (the same as the vanilla disasters).
        //   **Do not leave just the receiving end** — a request nobody can queue gets misread by
        //   the next person as "the place to press it is missing".
    }

    /// <summary>
    /// One request. Unlike ④, ⑤'s requests **carry coordinates**, so this value type is passed
    /// rather than an enum.
    /// When <see cref="Kind"/> is <see cref="VolcanoRequest.None"/>, <see cref="Point"/> is not
    /// read.
    /// </summary>
    public struct VolcanoRequestData
    {
        public readonly VolcanoRequest Kind;

        /// <summary>The world coordinates clicked (meaningful only for <see cref="VolcanoRequest.Place"/>).</summary>
        public readonly Vec3 Point;

        /// <summary>
        /// **The size scale vanilla's slider was pointing at** at the moment of the click
        /// (meaningful only for <see cref="VolcanoRequest.Place"/>). 1.0 is the size as set on the
        /// settings screen, and at 2.0 both the radius and the final height double
        /// (<c>Core.Volcano.VolcanoSizeScale</c>).
        ///
        /// ★ How many metres it actually becomes is further clamped by each form's band.
        ///   The real size after clamping is stated by the survey row on the volcano tab and by
        ///   the diagnostic dump.
        /// </summary>
        public readonly float SizeScale;

        /// <summary>
        /// The slider's **raw value** at the moment of the click (0–255; the display is a tenth of
        /// this).
        ///
        /// ★★ <b>Do not divide it back out of <see cref="SizeScale"/>.</b> That one is the value
        ///   after <c>VolcanoSizeScale</c> clamped it into a band, so at the top end several raw
        ///   values collapse onto the same scale. **"Is the slider at the very top" can only be
        ///   decided from the raw value** (<c>SuperEruption.IsSuper</c>).
        /// </summary>
        public readonly int SizeRaw;

        public VolcanoRequestData(VolcanoRequest kind, Vec3 point, float sizeScale, int sizeRaw)
        {
            Kind = kind;
            Point = point;
            SizeScale = sizeScale;
            SizeRaw = sizeRaw;
        }

        /// <summary>"No request". The same as <c>default(VolcanoRequestData)</c>, but it states the intent.</summary>
        public static VolcanoRequestData None
        {
            get
            {
                return new VolcanoRequestData(
                    VolcanoRequest.None, new Vec3(0f, 0f, 0f), 1f,
                    DisasterPlus.Core.Volcano.VolcanoSizeScale.AnchorRaw);
            }
        }
    }

    /// <summary>
    /// The sim thread publishes and the main thread reads <see cref="Latest"/>.
    /// The same shape as ①'s <c>ForecastHub</c>, ②'s <c>EarthquakeHub</c> and ④'s
    /// <see cref="TyphoonHub"/> (net35 has no <c>System.Collections.Concurrent</c>, so it is one
    /// plain lock).
    /// <see cref="VolcanoSnapshot"/> is immutable, so passing the reference is safe.
    ///
    /// The reverse path (passing the placement tool's and the panel's requests from main to sim)
    /// is guarded by **the same single <c>_gate</c>. Do not add a second lock** —
    /// that would create a lock-acquisition-order problem, a kind of problem this mod has never
    /// once had (the class doc of <see cref="TyphoonHub"/> records the same call).
    /// </summary>
    public static class VolcanoHub
    {
        private static readonly object _gate = new object();
        private static VolcanoSnapshot _latest;
        private static VolcanoRequestData _request;

        public static void Publish(VolcanoSnapshot snapshot)
        {
            lock (_gate) { _latest = snapshot; }
        }

        /// <summary>null until something has been published. The caller must check.</summary>
        public static VolcanoSnapshot Latest
        {
            get { lock (_gate) { return _latest; } }
        }

        /// <summary>
        /// **From the main thread.** Queue exactly one request.
        ///
        /// If the previous request has not yet been picked up by sim, it is **overwritten**
        /// (depth 1, last wins). "Last pressed wins" rather than "in the order pressed" is
        /// correct — someone who pressed place and then cancel in quick succession wants only the
        /// latter.
        /// </summary>
        public static void Request(VolcanoRequestData request)
        {
            lock (_gate) { _request = request; }
        }

        /// <summary>
        /// **From the main thread (display only).** A request not yet picked up by sim.
        ///
        /// It exists purely so the panel can show "requested". There is **a designed one-tick
        /// delay** between pressing and anything actually responding, so without this entry point
        /// the panel right after the press stays in its previous state and
        /// **the player presses again**.
        ///
        /// Unlike <see cref="TakeRequest"/>, it **does not take it**. Consume it here and sim's
        /// behaviour would change depending on whether the panel is open.
        /// </summary>
        public static VolcanoRequestData PendingRequest
        {
            get { lock (_gate) { return _request; } }
        }

        /// <summary>
        /// **From the sim thread.** Takes the queued request and resets it to
        /// <see cref="VolcanoRequest.None"/>. **Call it exactly once per tick**
        /// (call it twice and the second is always None, creating a call-order-dependent dropped
        /// request).
        /// </summary>
        public static VolcanoRequestData TakeRequest()
        {
            lock (_gate)
            {
                var r = _request;
                _request = VolcanoRequestData.None;
                return r;
            }
        }

        /// <summary>
        /// On level load and unload. Do not carry state across cities.
        /// </summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _latest = null;
                // ★ Forget to reset it and a mountain starts growing at the spot picked in the
                //    previous city the instant the next city loads. **Terrain cannot be undone.**
                _request = VolcanoRequestData.None;
            }
        }
    }
}
