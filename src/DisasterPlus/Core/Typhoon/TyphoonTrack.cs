using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The typhoon's phase. <c>Idle</c> means "the duration could not be read, so no phase
    /// can be claimed".
    /// **Frame 0 is not <c>Idle</c>** — that value is used to mean "nothing is known yet",
    /// not "it has not started approaching yet" (see <see cref="TyphoonTrack.PhaseAt"/>).
    /// </summary>
    public enum TyphoonPhase
    {
        Idle,
        Approaching,
        Peak,
        Passing,
        Gone,
    }

    /// <summary>
    /// The typhoon's track. **Every number here was invented by this mod.**
    /// Vanilla has no "moving typhoon" and no constraint to refer to (IL findings doc §E-1).
    /// The one exception is <see cref="IntensityAt"/>'s envelope, which is made **the same
    /// trapezium** as ThunderStormAI.SimulationStep's lightning-count ramp
    /// c = min(100, (f-act)&gt;&gt;3, (act+dur-f)&gt;&gt;3) (§A-1).
    /// Give it a different shape and you get the disagreement where vanilla's lightning is
    /// dropping off at the very moment ④ is displaying "peak".
    ///
    /// **The track is a closed-form function of elapsedFrames.** Make it cumulative and the
    /// track depends on the call history: the same save draws a different track at different
    /// game speeds, across a pause, or just after a load — and it can no longer be pinned by
    /// unit tests. Draw one curvature from the seed and make it a circular arc, and a closed
    /// form exists (see <see cref="ArcPosition"/>).
    ///
    /// **The speed alone is not ④'s to choose.** ThunderStormAI.IsStillActive is
    /// currentFrame - m_activationFrame &lt; m_activeDuration (§A-1), so a typhoon cannot
    /// outlive m_activeDuration. <see cref="SpeedFor"/> divides the path length by that
    /// duration. **If m_activeDuration cannot be read it returns 0, and the caller starts no
    /// typhoon at all** (design doc §6).
    ///
    /// The only random source used is
    /// <see cref="DisasterPlus.Core.Common.DeterministicRandom"/>.
    /// VanillaRandomizer is **not used** — what is decided here is not a value vanilla is
    /// about to draw, but a judgement ④ invented (see the rule for telling them apart in
    /// both files' docs).
    ///
    /// The one history-dependent thing is the landfall decay (see <see cref="DecayAfter"/>),
    /// and since that is not the track but "how long it has been over land so far", the
    /// caller carries a single float across. Only one step's worth of it lives here, as a
    /// pure function.
    /// </summary>
    public static class TyphoonTrack
    {
        /// <summary>The map's half-side (m). A CS map is 1080 cells × 16 m = 17280 m
        /// square.</summary>
        public const float MapHalfExtent = 8640f;

        /// <summary>
        /// The assumed total path length (m). <see cref="SpeedFor"/> divides this by
        /// <c>m_activeDuration</c> to get the speed.
        ///
        /// ★★ <b>Halved on 2026-08-22 after the owner's report "make it travel more slowly".</b>
        ///   From the map's side (17280 m) = "cross the map once within the full duration",
        ///   to <b>the map's half-side (8640 m) = "get only halfway across the map within
        ///   the full duration"</b>.
        ///   At the measured <c>m_activeDuration</c> of 8192 the speed is
        ///   <c>8640 / 8192 = 1.055 m/frame</c> (previously 2.109 m/frame), and at that
        ///   speed crossing the map's full side takes <b>16384 frames = 360 in-game minutes
        ///   (6 hours) = exactly twice the typhoon's lifetime</b>.
        ///
        /// ★★ <b>"More slowly" was not achieved by extending the lifetime. Here is why.</b>
        ///   <c>ThunderStormAI.IsStillActive</c> is
        ///   <c>currentFrame - m_activationFrame &lt; m_activeDuration</c>
        ///   (IL findings doc §A-1), so the host storm <b>cannot outlive 8192 frames</b>.
        ///   There are two ways to extend it, and both are expensive:
        ///
        ///   1. Push <c>m_activationFrame</c> forward every tick. Then the <c>num</c> in
        ///      <c>GetFireSpreadProbability</c>'s <c>1500 / (8 + (num &gt;&gt; 10))</c> stays
        ///      small and **the fire-spread probability sits pinned at its maximum**
        ///      (§A-5 — and that formula is inside vanilla).
        ///   2. Let ④ outlive the host. Then <c>WeatherManager</c> decides "there is no
        ///      active thunderstorm" and <b>starts creating thunderstorm disasters of its
        ///      own</b> (§A-3, which breaks the balance whereby ④'s lightning budget holds
        ///      back and leaves room for the host's share).
        ///
        ///   Both cost more than "move slowly". **Shortening the path is the right answer.**
        ///
        ///   Note that with a shorter path <b>the typhoon usually does not leave the map</b>
        ///   — the normal ending becomes "it used up its duration". Both endings go through
        ///   <c>TyphoonController.Stop</c> = <c>Forget</c> (design doc §4.2).
        /// </summary>
        /// <remarks>
        /// ★★ <b>On 2026-09-02, extended from the map's half-side to 12,000 m.</b>
        ///   The owner's request: "rather than appearing suddenly on the spot where you
        ///   click, it should form at the edge of the map, gradually approach the clicked
        ///   point, and then hold its course and leave".
        ///
        ///   The approach leg needs distance. Left at the half-side (8,640 m), it uses up
        ///   most of its lifetime just getting to the clicked point, so it <b>peaks out at
        ///   sea and makes landfall while weakening</b> (the intensity envelope peaks in the
        ///   middle of the lifetime, and that is matched to the host's lightning ramp, so it
        ///   cannot be moved).
        ///
        /// ★★ <b>The distance needed is "a full side", not a half-side.</b>
        ///   What is available for the approach is half the lifetime (see
        ///   <see cref="ApproachFraction"/>; that is where the intensity peaks), so we need
        ///   <b>a speed that covers the map's half-side in half the time</b>.
        ///   When the middle of the map is clicked, the distance to the edge is exactly a
        ///   half-side (8,640 m).
        ///
        ///   <c>speed × lifetime/2 ≥ 8,640</c> ⇔ <c>speed ≥ 2.109</c>
        ///   ⇔ <c>path length ≥ 17,280 = the map's full side</c>.
        ///   12,000 m was tried and failed (clicking the middle only gets 6,000 m back, so
        ///   it wells up from inside the map), so it was put back to a full side.
        ///
        /// ★ This is the speed as it was before being halved for "more slowly" on
        ///   2026-08-22. <b>It means something different now, though</b> — back then it set
        ///   off <b>from the clicked point</b> and left the map briskly. Now half of it goes
        ///   on the approach, so if anything it spends longer over the city. **If it looks
        ///   too fast, tune <see cref="ApproachFraction"/> down rather than this.**
        /// </remarks>
        public const float NominalPathLength = MapHalfExtent * 2f;

        /// <summary>
        /// How many times the host storm's <c>m_activeDuration</c> ④'s lifetime is.
        ///
        /// ── ★★ Why it can be multiplied (2026-08-22, an in-game report) ────────────────────
        ///
        /// &gt; As for the typhoon, the effect disappears straight away.
        /// &gt; Please reproduce the look of a typhoon moving slowly.
        ///
        /// The old doc said "the host storm cannot outlive 8192 frames".
        /// **That is true, but the condition had been misread.** Re-reading the IL:
        ///
        /// <code>
        /// ThunderStormAI.IsStillActive :
        ///     (currentFrame - m_activationFrame) &lt; m_activeDuration
        /// ThunderStormAI.IsStillEmerging :
        ///     m_activationFrame == 0        → true (permanently)
        ///     currentFrame &lt; m_activationFrame → true
        ///     otherwise                     → false
        /// </code>
        ///
        /// It dies when <b>the time elapsed since the activation frame</b> reaches the cap,
        /// not the time since it started. So pushing <c>m_activationFrame</c> forward
        /// <b>to "now" refills the remaining time each time</b>
        /// (<c>TyphoonSlot.KeepAlive</c>).
        ///
        /// ★ Put it at exactly "now".
        ///   - <c>IsStillActive</c> … true, since <c>0 &lt; 8192</c>
        ///   - <c>IsStillEmerging</c> … <c>now &lt; now</c> is false (it does not fall back to
        ///     Emerging)
        ///   - <c>GetFireSpreadProbability</c>'s <c>1500 / (8 + (num &gt;&gt; 10))</c> …
        ///     with <c>num = 0</c> the divisor is 8. **No division by zero**
        ///     (pull it back into the negatives and you get one. It happened in-game once)
        ///
        /// Four times is "about 9 minutes at speed 1" — that is how long it takes to get
        /// halfway across the map. Against the measured <c>m_activeDuration</c> of 8192 that
        /// is 32768 frames, and the speed is <c>8640 / 32768 = 0.264 m/frame</c> (a quarter
        /// of what it was).
        /// </summary>
        public const uint LifetimeMultiplier = 4u;

        /// <summary>
        /// ④'s lifetime (frames). <paramref name="activeDuration"/> is the host storm's
        /// <c>m_activeDuration</c>. **0 gives 0** (and the caller starts no typhoon).
        /// </summary>
        public static uint LifetimeFramesFor(uint activeDuration)
        {
            if (activeDuration == 0u) return 0u;

            // Block the overflow (both .cgs and the prefab can be changed by hand).
            if (activeDuration > uint.MaxValue / LifetimeMultiplier) return activeDuration;
            return activeDuration * LifetimeMultiplier;
        }

        public const float MinSpeedMetresPerFrame = 0.25f;
        public const float MaxSpeedMetresPerFrame = 6f;

        /// <summary>
        /// The cap on the turn per frame (rad). Even this turns 0.4 rad over 20000 frames.
        /// Vanilla's wind direction cannot keep up with the sharp turns real typhoons make
        /// (§A-4), so raising this only makes what is on screen a lie.
        /// </summary>
        public const float MaxCurvatureRadPerFrame = 0.00002f;

        public const float LandDecayPerMinute = 0.9f;
        public const float SeaRecoveryPerMinute = 0.2f;
        public const float MaxDecay = 255f;

        /// <summary>The fraction of the duration taken by the intensity envelope's rise and fall.
        /// §A-1's <c>&gt;&gt;3</c> trapezium rewritten as a ratio.</summary>
        public const float RampFraction = 0.25f;

        /// <summary>
        /// The envelope's floor (as a fraction of the peak). It exists **so that a living
        /// typhoon is never at intensity 0**.
        ///
        /// The bare trapezium returns exactly 0 at frame 0. But that value is written
        /// straight into <c>DisasterData.m_intensity</c> and appears in the log too — it is
        /// why the first in-game test printed <c>intensity=0</c>.
        /// A disaster at intensity 0 has everything on vanilla's side go to zero (the
        /// lightning-count ramp, the hazard map's radius), so you get **a typhoon that is
        /// there and does nothing**, and on top of that it is indistinguishable from "the
        /// value could not be read".
        ///
        /// It diverges slightly from vanilla's trapezium, but only over the few hundred
        /// frames at the ends of the rise and the fall. **Being confusable with 0 costs more.**
        /// </summary>
        public const float MinRampFraction = 0.08f;

        /// <summary>
        /// The smallest intensity a living typhoon will claim. It exists only to stop
        /// rounding dropping it to 0, and <b>a typhoon that has decayed away on land still
        /// returns 0 as before</b> (that one is the fact that it has weakened, not a
        /// rounding error).
        /// </summary>
        public const byte MinLiveIntensity = 1;

        /// <summary>
        /// Curvature below this is treated as a straight line (rad/frame).
        ///
        /// The arc's closed form contains v/κ, which is 0/0 at κ = 0. The curvature actually
        /// used goes up to <see cref="MaxCurvatureRadPerFrame"/> (2e-5), so a track that
        /// falls three orders of magnitude below this threshold is "practically straight",
        /// and run for 20000 frames it differs from a straight line by only a few metres.
        /// </summary>
        private const float CurvatureEpsilon = 1e-8f;

        // Where the magic numbers come from: both are four ASCII characters. They are
        // DeterministicRandom.Unit's second argument (the purpose tag), and exist purely to
        // draw independent values from the same seed.
        private const uint SaltBearing = 0x54595048u;    // "TYPH"
        private const uint SaltCurvature = 0x43555256u;  // "CURV"

        /// <summary>The heading (rad, [0, 2π)). Decided by the seed alone.</summary>
        public static float BearingOf(uint seed)
        {
            return DeterministicRandom.Unit(seed, SaltBearing) * 6.28318531f;
        }

        /// <summary>
        /// The track's curvature (rad/frame, [-Max, +Max]). The sign is the direction of turn.
        /// </summary>
        public static float CurvatureOf(uint seed)
        {
            return (DeterministicRandom.Unit(seed, SaltCurvature) * 2f - 1f)
                   * MaxCurvatureRadPerFrame;
        }

        /// <summary>
        /// The position on the arc. **It is public so that the tests can pin the continuity
        /// at κ→0 directly** (plan §1.1).
        ///
        /// The closed form the plan writes is
        /// <code>
        /// X = entryX + (v/κ)(sin(θ0+κt) − sin θ0)
        /// Z = entryZ + (v/κ)(cos θ0 − cos(θ0+κt))
        /// </code>
        /// and what is used here is the **algebraically equal half-angle form**
        /// <code>
        /// X = entryX + v·t·cos(θ0 + κt/2)·sinc(κt/2)
        /// Z = entryZ + v·t·sin(θ0 + κt/2)·sinc(κt/2)      sinc(x) = sin(x)/x
        /// </code>
        /// The form that takes a difference of sines loses more significance the smaller κ
        /// is (at κ = 1e-7 and v/κ = 1.2e7 the error approaches 1 m), and **that error grows
        /// quietly while the track still looks plausible**. The half-angle form takes no
        /// difference, so it is stable regardless of the size of κ and falls continuously
        /// onto the straight-line expression as κ→0.
        /// </summary>
        public static Vec2 ArcPosition(Vec2 entry, float theta0, float curvature,
                                       float frames, float speed)
        {
            float distance = speed * frames;

            if (curvature > -CurvatureEpsilon && curvature < CurvatureEpsilon)
            {
                return new Vec2(entry.X + distance * (float)Math.Cos(theta0),
                                entry.Z + distance * (float)Math.Sin(theta0));
            }

            double half = curvature * frames * 0.5f;
            double sinc = half == 0.0 ? 1.0 : Math.Sin(half) / half;
            double mid = theta0 + half;

            return new Vec2(entry.X + (float)(distance * Math.Cos(mid) * sinc),
                            entry.Z + (float)(distance * Math.Sin(mid) * sinc));
        }

        /// <summary>
        /// The heading (rad, [0, 2π)). θ(t) = θ0 + κ·t.
        ///
        /// A speed of 0 means "<c>m_activeDuration</c> could not be read" (see
        /// <see cref="SpeedFor"/>), so the typhoon has not moved a metre. It has not turned
        /// either, so the entry bearing comes back as it stands.
        /// </summary>
        public static float HeadingAt(uint seed, uint elapsedFrames, float speed)
        {
            return HeadingAt(new Vec2(0f, 0f), seed, elapsedFrames, speed);
        }

        /// <summary>
        /// As above. **It looks at the starting point and picks a direction that crosses the
        /// map.** The reason is in <see cref="BearingFrom"/>'s class doc.
        /// </summary>
        public static float HeadingAt(Vec2 origin, uint seed, uint elapsedFrames, float speed)
        {
            return HeadingAt(origin, seed, elapsedFrames, speed, 0u);
        }

        /// <summary>
        /// As above, with the clock wound back by <paramref name="approachFrames"/>
        /// (keep this in step with
        /// <see cref="CentreAt(Vec2, uint, uint, float, uint)"/> — forget to shift it and
        /// <b>the direction it is travelling disagrees with the direction displayed</b>).
        /// </summary>
        public static float HeadingAt(Vec2 origin, uint seed, uint elapsedFrames, float speed,
                                      uint approachFrames)
        {
            float theta = BearingFrom(origin, seed);
            if (speed <= 0f) return theta;

            theta += CurvatureOf(seed) * ((float)elapsedFrames - approachFrames);

            // Fold into [0, 2π), so it can be used as it stands both for display (degrees)
            // and for m_angle.
            const float twoPi = 6.28318531f;
            theta = (float)(theta - Math.Floor(theta / twoPi) * twoPi);
            if (theta < 0f) theta = 0f;
            if (theta >= twoPi) theta = 0f;
            return theta;
        }

        /// <summary>
        /// The typhoon's centre (the true position; it can be off the map).
        ///
        /// ★★ <paramref name="origin"/> is **the point the player clicked**.
        ///
        /// It used to draw an entry point from the seed, so the typhoon always came in from
        /// off the map. Now ④'s tile raises a placement cursor just like vanilla's disaster
        /// buttons, and the typhoon forms at the clicked point (design doc §4.1). **All the
        /// seed decides is the heading and the curvature**; it does not decide the starting
        /// point.
        ///
        /// The seed is kept so that the same disaster ID at the same point draws the same
        /// track (§4.1, "it must be reproducible in the same save").
        /// </summary>
        public static Vec2 CentreAt(Vec2 origin, uint seed, uint elapsedFrames, float speed)
        {
            return CentreAt(origin, seed, elapsedFrames, speed, 0u);
        }

        /// <summary>
        /// As above, evaluated with <b>the clock wound back</b> by
        /// <paramref name="approachFrames"/>.
        ///
        /// ★★ This is the substance of "it comes in from the edge of the map" (2026-09-02).
        ///   <paramref name="origin"/> becomes <b>the arrival point rather than the starting
        ///   point</b>, and it passes through there at exactly
        ///   <c>t = approachFrames</c>.
        ///   At <c>t = 0</c> it is wherever tracing the arc backwards leads — usually off
        ///   the map.
        ///
        /// ★ The track itself is unchanged. All that moves is <b>which point on the same arc
        ///   we call t = 0</b>, so this class's promise that "the track is a closed-form
        ///   function of elapsedFrames" (see the class doc) still holds.
        /// </summary>
        public static Vec2 CentreAt(Vec2 origin, uint seed, uint elapsedFrames, float speed,
                                    uint approachFrames)
        {
            return ArcPosition(origin, BearingFrom(origin, seed), CurvatureOf(seed),
                               (float)elapsedFrames - approachFrames, speed);
        }

        /// <summary>
        /// The <b>fraction of the lifetime spent approaching</b>.
        ///
        /// ★★ It is set to 0.5. <see cref="IntensityAt"/>'s envelope <b>peaks in the middle
        ///   of the lifetime</b> (the same trapezium as the host's lightning ramp), so
        ///   <b>the moment it reaches the clicked point is the peak</b>.
        ///
        /// ★★ Since <see cref="RampFraction"/> is 0.25, the intensity trapezium is
        ///   <b>flat from 25% to 75% of the lifetime</b>. Arriving anywhere in there gives
        ///   full strength, so it does not have to be exactly 0.5.
        ///
        ///   So the length of the approach is decided <b>by "the point at which it got back
        ///   to the edge"</b>, not by a hard-coded fraction. The fraction is <b>the cap on
        ///   that search</b>.
        ///
        ///   Hard-coding 0.5 / 0.6 failed twice — an arc's chord is shorter than its arc, so
        ///   clicking the middle of the map does not get all the way back
        ///   (measured: (8154, 2857) at 0.5 and (-7848, 6774) at 0.6. Both still inside).
        ///
        /// ★ The cap is 0.75 because that is how far the intensity trapezium stays flat.
        /// </summary>
        public const float ApproachFraction = 0.75f;

        /// <summary>
        /// The fraction spent approaching <b>at a minimum</b>.
        ///
        /// ★★ Click right next to the edge and it arrives within a few hundred frames, with
        ///   <b>the intensity not yet fully risen</b> (the trapezium reaches full at 25%).
        ///   That ends up as "a weak typhoon went past".
        ///   It then has to start further back than the edge, but that is off the map, where
        ///   nobody is watching.
        /// </summary>
        public const float MinApproachFraction = 0.25f;

        /// <summary>
        /// The step used when searching for the entry point (frames). Coarse is fine —
        /// all we need to know is that it has gone off the map.
        /// </summary>
        private const int ApproachProbeFrames = 64;

        /// <summary>
        /// <b>The number of frames until it reaches the clicked point.</b>
        ///
        /// The typhoon's track is "the arc that passes through the clicked point at
        /// <c>t =</c> this". <see cref="CentreAt"/> evaluates with the clock wound back by
        /// this amount, so at <c>t = 0</c> it is <b>off the map (or near the edge)</b>.
        ///
        /// ★ It is found <b>by simply tracing backwards</b>. The arc is a closed form, so
        ///   negative times can be put straight in (<see cref="ArcPosition"/>'s
        ///   <c>sinc</c> is an even function).
        ///   It stops once it has gone off the map, capped by
        ///   <see cref="ApproachFraction"/>.
        ///
        /// ★★ Hitting the cap means <b>the clicked point is too far from the edge for it to
        ///   get back there in half a lifetime</b>. It then wells up from inside the map
        ///   rather than at the edge, but **it still keeps the shape of "coming from over
        ///   there and passing through"**.
        ///   Extending the lifetime is not an option here (as
        ///   <see cref="NominalPathLength"/>'s doc says, outliving the host breaks something
        ///   else).
        /// </summary>
        public static uint ApproachFramesFor(Vec2 origin, uint seed, float speed,
                                             uint totalFrames)
        {
            if (speed <= 0f || totalFrames == 0u) return 0u;

            int cap = (int)(totalFrames * ApproachFraction);
            if (cap <= 0) return 0u;

            float theta = BearingFrom(origin, seed);
            float curvature = CurvatureOf(seed);

            int floor = (int)(totalFrames * MinApproachFraction);

            for (int back = ApproachProbeFrames; back <= cap; back += ApproachProbeFrames)
            {
                Vec2 at = ArcPosition(origin, theta, curvature, -back, speed);
                if (IsInsideMap(at)) continue;

                // ★ It has left the edge. But avoid arriving too early (MinApproachFraction).
                return (uint)(back < floor ? floor : back);
            }

            return (uint)cap;
        }

        /// <summary>
        /// The spread of entry bearings (radians, one side). At 0 it always runs straight
        /// through the centre.
        /// </summary>
        public const float BearingSpreadRadians = 0.62f;

        /// <summary>
        /// If the starting point is closer to the centre than this, the bearing is decided by
        /// the seed alone (m).
        /// Click the exact centre and "the direction towards the centre" cannot be defined.
        /// </summary>
        public const float CentreDeadZoneMetres = 900f;

        /// <summary>
        /// The entry bearing (rad) of a typhoon setting off from <paramref name="origin"/>.
        ///
        /// ── ★★ Why it must not be decided by the seed alone (2026-08-22, an in-game report) ───
        ///
        /// &gt; The typhoon's cloud effect only appears for a moment and then vanishes
        /// &gt; …as far as possible a huge typhoon cloud should pass slowly overhead, rotating…
        ///
        /// This used to be <see cref="BearingOf"/> (the seed alone). The bearing was
        /// <b>unrelated to the starting point</b>, so clicking near the edge of the map and
        /// drawing an outward roll sent the typhoon off the map within a few hundred metres,
        /// where <c>TyphoonController.Stop()</c> caught it — that is why <b>the cloud
        /// appeared for a moment and vanished</b>. Vanilla's thunderstorm (the host) runs on
        /// a different lifetime, so only that was left and you got "just a thunderstorm".
        ///
        /// Now it <b>takes the direction towards the centre as its baseline and swings it by
        /// ±<see cref="BearingSpreadRadians"/> from the seed</b>. Wherever you click, the
        /// typhoon crosses the map, which is the longest time it can spend over the city.
        ///
        /// ★ If the click is right next to the centre (inside
        ///   <see cref="CentreDeadZoneMetres"/>) then "the direction towards the centre"
        ///   cannot be defined, so the seed alone decides — setting off from there
        ///   **it crosses the map whichever way it goes**, so that is fine.
        /// </summary>
        public static float BearingFrom(Vec2 origin, uint seed)
        {
            float distance = (float)Math.Sqrt(origin.X * origin.X + origin.Z * origin.Z);
            if (distance < CentreDeadZoneMetres) return BearingOf(seed);

            // The direction towards the centre (0,0).
            float toCentre = (float)Math.Atan2(-origin.Z, -origin.X);

            // Swing by ±spread from the seed. **The same point and the same seed give the
            // same track.**
            float offset = (DeterministicRandom.Unit(seed, BearingSpreadSalt) * 2f - 1f)
                           * BearingSpreadRadians;

            const float twoPi = 6.28318531f;
            float theta = toCentre + offset;
            theta = (float)(theta - Math.Floor(theta / twoPi) * twoPi);
            if (theta < 0f) theta = 0f;
            if (theta >= twoPi) theta = 0f;
            return theta;
        }

        /// <summary>The salt used to draw the bearing's swing. **Do not reuse it
        /// elsewhere.**</summary>
        private const uint BearingSpreadSalt = 0x42454152u;

        /// <summary>Is it inside the map's rectangle? **Do the end-of-life test on the
        /// unclamped centre.**</summary>
        public static bool IsInsideMap(Vec2 centre)
        {
            return centre.X >= -MapHalfExtent && centre.X <= MapHalfExtent
                && centre.Z >= -MapHalfExtent && centre.Z <= MapHalfExtent;
        }

        /// <summary>
        /// The travel speed (m/frame).
        ///
        /// ★ If <paramref name="activeDurationFrames"/> is 0 (i.e. the prefab could not be
        /// read) it **returns 0**. The caller treats that as "unknown" and starts no typhoon
        /// at all.
        /// This is the one place that structurally guarantees design doc §6's "if it cannot
        /// be read, do not guess; do nothing".
        /// **Do not rewrite this <c>return 0f</c> into a "safe default".**
        /// </summary>
        public static float SpeedFor(uint activeDurationFrames)
        {
            if (activeDurationFrames == 0u) return 0f;

            float speed = NominalPathLength / activeDurationFrames;
            if (speed < MinSpeedMetresPerFrame) return MinSpeedMetresPerFrame;
            if (speed > MaxSpeedMetresPerFrame) return MaxSpeedMetresPerFrame;
            return speed;
        }

        /// <summary>
        /// One step of the landfall decay. It builds up over land and (slowly) drains away
        /// over the sea.
        /// It goes neither negative nor above <see cref="MaxDecay"/>.
        /// If <paramref name="deltaMinutes"/> is 0 or less, nothing advances.
        /// </summary>
        public static float DecayAfter(float decay, bool overLand, float deltaMinutes)
        {
            if (float.IsNaN(decay)) decay = 0f;
            if (deltaMinutes <= 0f || float.IsNaN(deltaMinutes)) return decay;

            if (overLand)
            {
                decay += LandDecayPerMinute * deltaMinutes;
                if (decay > MaxDecay) decay = MaxDecay;
                return decay;
            }

            decay -= SeaRecoveryPerMinute * deltaMinutes;
            if (decay < 0f) decay = 0f;
            return decay;
        }

        /// <summary>
        /// The current intensity. **The same trapezium as vanilla's lightning-count ramp**
        /// (§A-1, and the class doc), with the landfall decay subtracted.
        ///
        /// ★ The trapezium is held up from below by <see cref="MinRampFraction"/>.
        ///   **A living typhoon never claims 0** (see that doc). It returns 0 only when
        ///   "the duration could not be read" or "it has decayed away".
        ///
        /// ★ Always clamp **before** the cast to <c>(byte)</c>. C#'s float→byte returns
        /// something close to undefined out of range (a negative value turns into something
        /// near 255), which gives the worst possible breakage: a typhoon that has decayed
        /// away becomes the strongest there is.
        /// </summary>
        public static byte IntensityAt(byte peak, uint elapsedFrames, uint activeDurationFrames,
                                       float decay)
        {
            if (activeDurationFrames == 0u) return 0;

            float duration = activeDurationFrames;
            float elapsed = elapsedFrames;
            float rampFrames = duration * RampFraction;

            float ramp = 1f;
            if (rampFrames > 0f)
            {
                float rise = elapsed / rampFrames;
                float fall = (duration - elapsed) / rampFrames;
                if (rise < ramp) ramp = rise;
                if (fall < ramp) ramp = fall;
            }
            // ★ Do not leave the ends of the trapezium at 0 (see MinRampFraction's doc).
            if (ramp < MinRampFraction) ramp = MinRampFraction;
            if (ramp > 1f) ramp = 1f;

            float value = peak * ramp - decay;
            if (float.IsNaN(value) || value <= 0f) return 0;
            if (value >= 255f) return 255;

            // ★ Do not let a "living typhoon" that rounded down to 0 claim 0.
            //   The decayed-away side (value <= 0) returned 0 above and never gets here.
            byte rounded = (byte)value;
            return rounded == 0 ? MinLiveIntensity : rounded;
        }

        /// <summary>
        /// The phase. If <paramref name="activeDurationFrames"/> is 0 it is
        /// <see cref="TyphoonPhase.Idle"/> (the duration could not be read, so no phase is
        /// claimed).
        /// </summary>
        public static TyphoonPhase PhaseAt(uint elapsedFrames, uint activeDurationFrames)
        {
            if (activeDurationFrames == 0u) return TyphoonPhase.Idle;

            float f = (float)elapsedFrames / activeDurationFrames;
            if (f >= 1f) return TyphoonPhase.Gone;
            if (f < 0.3f) return TyphoonPhase.Approaching;
            if (f < 0.7f) return TyphoonPhase.Peak;
            return TyphoonPhase.Passing;
        }
    }
}
