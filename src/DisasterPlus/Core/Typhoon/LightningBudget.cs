namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The arithmetic for our share of the lightning queue. **This type's job is "do not hit
    /// the cap of 20 strikes".**
    ///
    /// ── What happens if you do hit it (IL findings doc §A-3) ──────────────────────
    ///
    /// <c>WeatherManager.QueueLightningStrike</c>, when there is no free slot and
    /// <c>m_lightningQueue.m_size &gt;= 20</c>, **returns false and silently throws the
    /// request away** (the <c>ldc.i4.s 20</c> at <c>IL_023B–024D</c> was re-confirmed in
    /// this task). And what gets thrown away is not necessarily ④'s — while the queue is
    /// full, **the strikes the host's vanilla thunderstorm was going to fire, and other
    /// mods' strikes too**, disappear in the same way. Not one exception is raised.
    ///
    /// ── The queue cannot be read. So "estimate, and yield" ──────────────────
    ///
    /// <c>m_lightningQueue</c> is a private <c>FastList&lt;LightningStrike&gt;</c>, and
    /// <c>InstanceManager.GetPosition</c>'s switch has no Lightning branch either (§A-2b).
    /// **④ cannot read how much of the queue is left.** Peeking with reflection is an
    /// option, but it is a kind of dependency this mod has never once taken, and in any
    /// case the sim thread repacks the queue every step, so the value at the instant you
    /// peek means little. Instead we <b>pin down both sides by calculation</b>:
    ///
    /// 1. **What ④ has fired** ④ counts itself (the ledger of scheduled frames plus
    ///    <see cref="HasExpired"/>). This is exact.
    /// 2. **What the host's vanilla thunderstorm will fire** has an **upper bound** that
    ///    comes out of the formula.
    ///    <c>ThunderStormAI.SimulationStep</c> (IL_010B–0175 measured in this task):
    ///    <code>
    ///    c = min(100, (f - act) >>u 3, (act + dur - f) >>u 3)
    ///    c = (c * intensity + 50) / 100
    ///    n = Randomizer.Int32(max(1, c / 20), max(1, 1 + c / 10))
    ///    </code>
    ///    The key point is that <b>the upper bound can be derived without reading
    ///    <c>m_randomSeed</c></b> (④ knows <c>intensity</c>, <c>m_activationFrame</c> and
    ///    <c>m_activeDuration</c> in full). **Take the upper bound, not the mean** —
    ///    estimate from the mean and an unlucky step goes just over 20.
    /// 3. ④'s share is <see cref="Allowance"/>.
    ///
    /// ── Which way we went on ambient lightning (design doc §4.2's "choose consciously") ────
    ///
    /// The end of <c>WeatherManager.SimulationStepImpl</c> queues lightning of its own when
    /// <c>m_currentRain &gt; 0.8f &amp;&amp; m_lightningQueue.m_size == 0</c> (§A-3).
    /// **The condition is "the queue is empty", so as long as ④ always has at least one
    /// strike queued, ambient lightning stops entirely.** Therefore:
    ///
    ///   - there is <b>no need to subtract ambient lightning</b> from the budget of 20
    ///     (handed on from T4)
    ///   - **④ goes the "keep scattering" way.** Create a phase that cuts the count to 0
    ///     (inside the eye, say) and ambient lightning comes back just there. A test pins
    ///     that <see cref="Allowance"/> always returns at least 1 when
    ///     <c>inFlight == 0</c>.
    ///
    /// ── Do not set <see cref="MinFreeSlots"/> to 0 ─────────────────────
    ///
    /// Stopping ambient lightning and **filling the queue right up** are different things.
    /// Fill it right up and the game's <c>QueueLightningStrike</c> is in a state of always
    /// returning false, so neither the host's storm nor any other mod can queue lightning.
    /// Always leave two slots free.
    ///
    /// **This type lives in Core.** It is pure arithmetic using neither <c>UnityEngine</c>
    /// nor the Cities API, and eight tests pin the four promises above.
    /// </summary>
    public static class LightningBudget
    {
        /// <summary>
        /// How many strikes can be in flight at once. **The number the game actually uses**
        /// (§A-3 <c>IL_0246: ldc.i4.s 20</c>). Raise it and you are walking into the state
        /// where the excess requests are silently thrown away, of your own accord.
        /// </summary>
        public const int QueueCapacity = 20;

        /// <summary>
        /// Slots always kept free. Left for the game itself and for other mods (see the
        /// class doc). **Do not set this to 0.**
        /// </summary>
        public const int MinFreeSlots = 2;

        /// <summary>
        /// The shortest delay <c>QueueLightningStrike</c> rounds requests up to (§A-3
        /// IL_0000, <c>startFrame = Max(startFrame, currentFrameIndex + 15)</c>).
        ///
        /// ④ **adds this itself** before making a request. Leave it to the rounding and the
        /// scheduled frame in ④'s ledger drifts away from the frame it actually fires on,
        /// which makes <see cref="HasExpired"/> fire early and count fewer in flight than
        /// there really are.
        /// </summary>
        public const uint MinDelayFrames = 15u;

        /// <summary>
        /// How long after the scheduled frame the slot is released (§A-3:
        /// <c>ReleaseInstance</c> at <c>sf + 45 &lt; m_currentFrameIndex</c>).
        /// **It leaves the queue here, not at the moment it fires.**
        /// </summary>
        public const uint ExpiryFrames = 45u;

        /// <summary>
        /// The spread of delays ④ uses. They are scattered between 0 and this value (the
        /// implementation is in <c>TyphoonLightning</c>).
        /// The longer it is, the longer each strike occupies the queue, so in effect the
        /// number of strikes falls.
        /// </summary>
        public const uint MaxDelayFrames = 256u;

        /// <summary>
        /// The ramp value <c>c</c> the host's vanilla thunderstorm uses on this frame
        /// (§A-1 IL_010B–0145).
        ///
        /// Vanilla's formula only runs in the Active branch, so this returns 0 before
        /// activation and after the duration (vanilla's <c>shr.un</c> produces an enormous
        /// value when <c>f &lt; act</c>, but that is a path vanilla itself never takes).
        /// Also 0 when <paramref name="activeDuration"/> is 0 — **if the prefab cannot be
        /// read, there is nothing to estimate from.**
        /// </summary>
        public static int VanillaRampCount(uint frame, uint activationFrame,
                                           uint activeDuration, byte intensity)
        {
            if (activeDuration == 0u) return 0;
            if (frame < activationFrame) return 0;

            // Adding two uints can overflow, so hold it in a ulong.
            ulong end = (ulong)activationFrame + activeDuration;
            if (frame > end) return 0;

            ulong since = frame - activationFrame;
            ulong until = end - frame;

            int c = 100;
            int rise = (int)(since >> 3);
            int fall = (int)(until >> 3);
            if (rise < c) c = rise;
            if (fall < c) c = fall;

            return (c * intensity + 50) / 100;
        }

        /// <summary>
        /// The **maximum** number of strikes the host could queue in one
        /// <c>SimulationStep</c> at <paramref name="rampCount"/>
        /// (§A-1: <c>n = Int32(max(1, c/20), max(1, 1 + c/10))</c>).
        ///
        /// It returns the upper end of <c>Randomizer.Int32(min, max)</c> itself.
        /// **Do not return the mean** — estimate from the mean and an unlucky step goes
        /// just over 20.
        /// </summary>
        public static int VanillaMaxStrikes(int rampCount)
        {
            if (rampCount < 0) rampCount = 0;
            int max = 1 + rampCount / 10;
            return max < 1 ? 1 : max;
        }

        /// <summary>
        /// The cap on how many new strikes ④ may queue this tick.
        ///
        /// <c>QueueCapacity - MinFreeSlots - vanillaReserve - inFlight</c> (0 if negative).
        /// **Returning 0 is a normal state** — the higher the intensity, the larger the
        /// host's share, and at or above <see cref="IntensityWithNoShareAtPeak"/> ④'s share
        /// is 0. What keeps the queue non-empty then is the host's lightning, so ambient
        /// lightning is still suppressed (see the class doc). However **④'s scattering onto
        /// the eyewall does stop**, so that state is named explicitly and put on screen by
        /// <see cref="YieldsCompletely"/>.
        /// </summary>
        public static int Allowance(int inFlight, int vanillaReserve)
        {
            if (inFlight < 0) inFlight = 0;
            if (vanillaReserve < 0) vanillaReserve = 0;

            int allowance = QueueCapacity - MinFreeSlots - vanillaReserve - inFlight;
            return allowance < 0 ? 0 : allowance;
        }

        /// <summary>
        /// Above this intensity, **④'s share is 0 even with an empty queue** (at the peak of
        /// the ramp).
        ///
        /// The derivation (all of it from formulas inside this type; tests pin both sides,
        /// 170 and 169):
        /// <code>
        /// peak of the ramp   c = (100 * intensity + 50) / 100 = intensity
        /// the host's share   reserve = VanillaMaxStrikes(c) = 1 + c / 10
        /// ④'s share          Allowance(0, reserve) = 20 - 2 - reserve = 17 - c / 10
        /// zero when          c / 10 >= 17  →  c >= 170
        /// </code>
        ///
        /// **This is inside the range of intensities that can be set** (the slider runs
        /// 10-255). So if the player puts the intensity at 170 or above, ④ stops queueing a
        /// single strike and <b>the scattering towards the eyewall (the whole point of T6)
        /// disappears, leaving only the host storm's uniform disc</b>. **That is not a
        /// malfunction, but neither is it a change that may happen silently** — the panel
        /// and the diagnostics own up to it there and then
        /// (<c>Strings.TyphoonLightningYielded</c>).
        ///
        /// The reserved slots themselves are not relaxed. Relax them and it tips towards the
        /// host storm's and other mods' lightning being thrown away (the class doc's "do not
        /// hit the cap of 20").
        /// </summary>
        public const int IntensityWithNoShareAtPeak = 170;

        /// <summary>
        /// Whether the host's share alone uses up the budget (i.e. ④ cannot queue even with
        /// an empty queue).
        ///
        /// There are two reasons <see cref="Allowance"/> returns 0: ④'s own in-flight
        /// strikes are filling it (temporary, normal), or the host's share is too large
        /// (which lasts for as long as the intensity is high).
        /// **The display side must tell these two apart** — the former comes back on the
        /// next tick, the latter does not come back until the intensity is lowered. Looking
        /// at <paramref name="vanillaReserve"/> alone gives you that distinction.
        /// </summary>
        public static bool YieldsCompletely(int vanillaReserve)
        {
            return Allowance(0, vanillaReserve) <= 0;
        }

        /// <summary>
        /// The earliest frame the game rounds a request up to (§A-3 IL_0000).
        /// ④ scatters from here out to <see cref="MaxDelayFrames"/>.
        /// </summary>
        public static uint EarliestFrame(uint currentFrame)
        {
            return currentFrame + MinDelayFrames;
        }

        /// <summary>
        /// Whether a strike scheduled for <paramref name="scheduledFrame"/> has left the
        /// queue as of <paramref name="currentFrame"/>
        /// (§A-3: <c>sf + 45 &lt; m_currentFrameIndex</c>).
        ///
        /// **It fires when <c>sf == currentFrame</c>, but the slot frees up here.**
        /// Drop it from the in-flight count the instant it fires and you count a slot that
        /// is in fact still occupied as free, and go on to hit the cap.
        /// </summary>
        public static bool HasExpired(uint scheduledFrame, uint currentFrame)
        {
            // Compare in ulong to avoid uint wrap-around.
            return (ulong)scheduledFrame + ExpiryFrames < currentFrame;
        }
    }
}
