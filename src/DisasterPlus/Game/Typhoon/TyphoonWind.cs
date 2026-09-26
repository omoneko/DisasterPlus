using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Typhoon wind damage. <b>Sim thread only.</b> On by default.
    ///
    /// ── This is not a visualisation but entirely new physics ──────────────
    ///
    /// Vanilla has no wind destruction mechanism at all, and **there is not even a field
    /// that raises the wind speed** (IL facts document §A-5 / §B5). The buildings that fall
    /// here are **buildings that in vanilla would never have fallen**. What is inside the
    /// model, and the fact that it has no units, are in
    /// <see cref="WindDamageModel"/>'s class doc.
    ///
    /// ── Why it is on by default (a different decision from ②'s second layer) ──
    ///
    /// ②'s second layer was off by default, because "if you turn on behaviour that does not
    /// exist in vanilla by default, the player has no way of realising that the mod is why
    /// a tsunami arrives on its own after an earthquake". **④ does not have that problem.**
    /// Not one typhoon happens unless the player presses "raise a typhoon", so everything
    /// that happens right after they press it is attributable to the typhoon.
    /// The strength can still be taken down to 0 with
    /// <c>ModSettings.TyphoonWindStrength</c>.
    ///
    /// ── Do not go through <c>DisasterHelpers</c> (§F-1) ───────────────────
    ///
    /// Natural Disasters Renewal completely replaces
    /// <c>DisasterHelpers.DestroyBuildings</c> / <c>DestroyNetSegments</c> (plus
    /// <c>DestroyStuff</c>, which calls the former) with a Prefix, and sniffs out
    /// <c>burnRadiusMin == 0 &amp;&amp; burnRadiusMax == 0</c> as "that is a tornado" and
    /// <c>probability == 0.02f</c> as "that is an earthquake".
    /// Call <c>BuildingAI.CollapseBuilding</c> directly and that patch surface is
    /// **completely bypassed**.
    ///
    /// **Never write <c>Building.m_fireIntensity</c> directly.** Only the
    /// <c>CommonBuildingAI</c> family consumes it; write it on any other AI and you get a
    /// permanent ghost fire that nobody puts out, and **because it lives in vanilla's
    /// building array it burns into the save and survives removing the mod**. This project
    /// has shipped that once. The <c>burnAmount</c> passed here is <b>0</b> (the wind blows
    /// things over and crushes them; it does not scorch them).
    ///
    /// **Leave the AIs that refuse alone** (§F-2). Under <c>demolish: false</c>, Shelter /
    /// DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy quietly return
    /// false. **Disaster response facilities not being destroyed by a typhoon is correct
    /// behaviour, so do not run away to <c>demolish: true</c>.** They are counted in
    /// <see cref="LastRefused"/>, so the diagnostics can distinguish "nothing broke" from
    /// "it cannot be broken".
    ///
    /// **Always make the real call even when the dry run returns false.**
    /// <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> perform the real collapse immediately
    /// after <c>if (testOnly) return false;</c> (§F-2). Trust the dry run and skip the call
    /// and you miss power poles that really can be knocked down.
    ///
    /// ── The ceiling on work per tick (stated explicitly) ──────────────────
    ///
    /// The sweep only runs while a typhoon is moving, and the interval is the **elapsed
    /// in-game time** corresponding to <see cref="IntervalFrames"/> frames (not
    /// <c>frameIndex % N</c> — <c>m_currentFrameIndex</c> advances by
    /// <c>FinalSimulationSpeed</c> (1/3/9) per tick, so a modulo makes the test fire
    /// patchily depending on the game speed. ③ appendix A-4).
    ///
    /// One sweep is capped at <b><see cref="MaxCellsPerPass"/> grid cells</b> and
    /// <b><see cref="MaxBuildingsPerPass"/> buildings</b>. The gale radius is 2.2× the
    /// storm radius, so the bounding box can be bigger still than ②'s earthquake. On
    /// reaching a cap we break off and resume from <see cref="_cursorOrdinal"/> next time.
    ///
    /// **The sweep order runs outwards from the typhoon's centre**
    /// (<see cref="OutwardCellOrder"/>). In row-major order the first thing we look at is a
    /// corner of the box, i.e. the furthest point from the centre, where the probability is
    /// close to 0, and the cap then **throws away the buildings most likely to break** (②'s
    /// second-layer review I1).
    ///
    /// There is one difference peculiar to ④: **the centre moves every tick.** Both the box
    /// and the centre of the rings change each time, so carrying
    /// <see cref="_cursorOrdinal"/> over only means "the continuation of a sweep cut short
    /// with the same centre cell". **Reset it to 0 once the centre cell differs from last
    /// time.** But <b>do not rewind the interval accumulator
    /// (<see cref="_minutesSincePass"/>)</b> — rewind it and it returns to 0 every time the
    /// centre moves, so **the sweep never runs once** (②'s second-layer review I3 was
    /// exactly this).
    ///
    /// ── The truncated continuation almost never actually happens (whole-project review I1) ──
    ///
    /// <b>And that "carry over" barely ever holds for a moving typhoon.</b>
    /// <c>TyphoonTrack.SpeedFor</c> clamps the travel speed to <c>[0.25, 6]</c> m/frame,
    /// and the sweep interval is the in-game time corresponding to
    /// <see cref="IntervalFrames"/> = 256 frames. So the eye moves **64-1536 m** during one
    /// sweep — a grid cell is 64 m, so the centre cell changes almost every time and
    /// <see cref="_cursorOrdinal"/> goes back to 0.
    ///
    /// **So "the outer rim cut off by the cap gets rolled from where it stopped on the next
    /// sweep" does not happen.** The next sweep starts at the eye again and licks the inner
    /// rings once more. The diagnostics dump used to say "continues on the next sweep", but
    /// that was a lie, so it was deleted (the panel's wording never mentioned continuation
    /// in the first place). The current wording only says "this sweep did not reach the
    /// outer rim".
    ///
    /// The machinery itself is kept. At the minimum speed and travelling diagonally, there
    /// is rarely a sweep where the centre cell does not change, and only then does it truly
    /// continue. **The cap errs on the safe side** (not tested = not knocked down), so this
    /// does no harm.
    /// It could be changed to carry over by ring radius, but that would be different
    /// behaviour — "do not re-roll the inside even when the centre moves" — so do not swap
    /// it in before watching the behaviour in the game.
    ///
    /// ── The dangerous semicircle on the right of the track (the owner's note) ──
    ///
    /// > Within the area directly under the typhoon, strengthen the damage radius and the
    /// > probability of damage slightly on the [right] of the direction of travel
    ///
    /// A real typhoon is not left-right symmetric. The side where the vortex's rotation and
    /// the storm's own travel add up is called the dangerous semicircle, and in the northern
    /// hemisphere it is **the right of the direction of travel** (<see cref="TrackBias"/>).
    /// There, ④
    ///
    /// - stretches the **damage radius** by up to +18%: it stretches the wind-speed field
    ///   itself (<c>WindAt(distance ÷ RadiusFactor, …)</c>). The radius constants are not
    ///   rewritten
    /// - raises the **collapse probability** by up to +30%: multiplied onto
    ///   <c>CollapseChance</c>'s result
    ///
    /// **Widen the sweep's bounding box by the same factor.** Without that, the buildings on
    /// the outer rim of the stretched side never enter the sweep at all and stretching the
    /// radius means nothing (a breakage with no exception). Only the box is widened;
    /// **the ring order (from the eye outwards) does not change by one bit.**
    ///
    /// The bias <b>re-reads <c>TyphoonController.HeadingRadians</c> on every sweep</b>, so
    /// it turns on the spot when the track bends. Do not cache the heading here.
    ///
    /// The southern hemisphere (where the left is the dangerous semicircle) is switched with
    /// <c>ModSettings.TyphoonSouthernHemisphere</c>.
    ///
    /// ── Do not mix the frame into the random draw ────────────────────────
    ///
    /// Selection is determined by (typhoon ID, building ID) alone. Mix the frame in and the
    /// same building is re-drawn on every sweep, so the number of buildings destroyed grows
    /// without limit over time.
    ///
    /// **And yet the damage still spreads.** Unlike ②, ④'s centre moves, so as the typhoon
    /// approaches the same building's <c>wind</c> rises and, with <c>roll</c> fixed, its
    /// <c>chance</c> rises and eventually crosses the threshold. **That is the
    /// implementation of "the damage spreads as the typhoon comes", and it is also why there
    /// is no need to mix the frame into the random draw.**
    ///
    /// ── The presentation and the felled trees ─────────────────────────────
    ///
    /// Neither <c>DisasterHelpers.AddWind</c> nor <c>DestroyTrees</c> is patched by NDR
    /// (§F-1). **Both declarations were confirmed directly from the IL in this task**
    /// (§B-1 had only derived the parameter order from <c>DestroyStuff</c>'s forwarding):
    ///
    /// ```
    /// public static void DisasterHelpers.AddWind(
    ///     Vector3 position, float radius, Vector3 directionalWind,
    ///     float rotationalWind, float radialWind, InstanceManager.Group group)
    ///
    /// public static void DisasterHelpers.DestroyTrees(
    ///     int seed, InstanceManager.Group group, Vector3 position,
    ///     float totalRadius, float removeRadius,
    ///     float destructionRadiusMin, float destructionRadiusMax,
    ///     float burnRadiusMin, float burnRadiusMax)
    /// ```
    ///
    /// **Pass 0 for <c>burnRadiusMin</c> / <c>burnRadiusMax</c>.** Trees **catching fire**
    /// in a typhoon makes no sense (we do not use <c>TreeManager.BurnTree</c> either).
    /// </summary>
    public static partial class TyphoonWind
    {
        /// <summary>The sweep interval (in-game time equivalent to a frame count). 256, the
        /// same as ②'s long-period damage.</summary>
        private const int IntervalFrames = 256;
        /// <summary>The ceiling on grid cells looked at in one sweep.</summary>
        private const int MaxCellsPerPass = 32768;

        /// <summary>The ceiling on buildings examined in one sweep.</summary>
        private const int MaxBuildingsPerPass = 2048;

        /// <summary>The number of cells along one side of the building grid (one cell is
        /// 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>
        /// The ceiling on how many links of one cell's chain we walk (a guard against
        /// corrupt save data).
        /// 49152, the same as the inner loop of vanilla's
        /// <c>DisasterHelpers.DestroyBuildings</c> = the size of the building buffer.
        /// </summary>
        private const int GridChainGuard = 49152;

        /// <summary>
        /// The flag condition for a candidate. The same as ②'s
        /// <c>LongPeriodDamage.CandidateMask</c>.
        /// We reject <c>Collapsed</c> because <c>CollapseBuilding</c> always returns false
        /// for those; without rejecting them the rubble left behind would be counted in
        /// refused every time and the diagnostic figures would become unreadable.
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>The <c>Degraded</c> self-report key for felling trees
        /// (<c>FeatureHost.NoteDegraded</c>).</summary>
        private const string TreeNoteKey = "typhoonWindTrees";

        /// <summary>
        /// How much of the gale radius the tree destruction radius takes.
        /// **A figure ④ chose.** Wider than for buildings, enough that trees visibly go
        /// down in groups.
        /// </summary>
        private const float TreeRadiusFraction = 0.5f;

        /// <summary>The inner radius within which trees definitely fall (as a fraction of
        /// <see cref="TreeRadiusFraction"/>).</summary>
        private const float TreeInnerFraction = 0.3f;

        /// <summary>
        /// <c>AddWind</c>'s height offset and radius multiplier. Modelled on the tornado's
        /// actual arguments in §B-1 (<c>position.y + rMax * 0.75</c> /
        /// <c>radius = rMax * 1.5</c>).
        /// </summary>
        private const float WindHeightFraction = 0.75f;

        private const float WindRadiusFactor = 1.5f;

        /// <summary>
        /// The vertical component needed to <b>grab</b> cars and pedestrians.
        ///
        /// ── ★★ 19.62 is an immovable wall (2026-09-02, settled from the IL) ──────
        ///
        /// <c>CarAI.AddWind</c> IL_0018-0031 and <c>HumanAI.AddWind</c> IL_000E-002B:
        ///
        /// <code>
        /// if ((v.m_flags2 &amp; 2) == 0 &amp;&amp; wind.y &lt;= 19.62f) return false;
        /// </code>
        ///
        /// <b>19.62 = 2g.</b> Cars and citizens that have not yet been blown about
        /// <b>do not react at all to a wind that does not exceed this</b> — neither their
        /// speed nor their direction changes by one bit; it simply returns false.
        ///
        /// ★★ **This was the whole of "it has no effect on moving cars or walking people"
        ///   either.**
        ///   (We had lowered it 80 → 8 on 2026-08-25 and 8 → 1.5 on 2026-09-02.
        ///    Both were below the wall, and <b>nothing had ever happened to moving cars at
        ///    all</b>. Stationary cars were flying because parked vehicles do not have this
        ///    gate.)
        ///
        /// So we take a value <b>just above</b> the wall. Vanilla's tornado uses 80 (four
        /// times the wall), and that is what "really flying away" is.
        /// </summary>
        private const float WindUpwardLatch = 19.7f;

        /// <summary>
        /// The vertical component <b>after</b> the grab.
        ///
        /// ★ Once <c>m_flags2 |= 2</c> is set, the gate above lets everything through
        ///   (IL_00A7-00C6). In other words <b>from the second time on we can push with a
        ///   lower value</b>.
        ///   The velocity is mixed as <c>v = v*0.875 + wind*0.125</c>, so keep handing it
        ///   19.7 every time and <b>it keeps rising and never comes down</b> — which is what
        ///   the owner complained about on 2026-08-25: "they really are flying away".
        ///
        ///   <b>Grab occasionally, push every time.</b> (<c>LatchEveryNthPush</c>)
        /// </summary>
        private const float WindUpwardSustain = 2f;

        /// <summary>
        /// How often — once every how many rounds — we fire the high
        /// <see cref="WindUpwardLatch"/> wind. The rest use
        /// <see cref="WindUpwardSustain"/> and only push what has already been grabbed.
        /// </summary>
        private const int LatchEveryNthPush = 4;

        /// <summary>
        /// The vortex's <b>tangential speed at the rim</b> (units per frame).
        ///
        /// ── ★★ Do not just reuse the tornado's 0.5 (2026-09-02, from the IL) ──────
        ///
        /// The body of <c>DisasterHelpers.AddWindVehicles</c> (IL_00F8-0145) is
        ///
        /// <code>
        /// delta = car position - wind position   ← <b>metres. Not normalised</b>
        /// w = directional
        /// w.x -= delta.z * rotational            ← <b>grows in proportion to distance</b>
        /// w.z += delta.x * rotational
        /// w += (delta * radial) / Max(1, |delta|)   ← this one is normalised
        /// </code>
        ///
        /// ★★ <b>Only the rotational component is not divided by the distance.</b> A
        ///   tornado's radius is 100-200 m, so 0.5 × 150 = 75 stays contained, but
        ///   <b>a typhoon's radius is thousands of metres</b> — pass the same 0.5 and at the
        ///   rim you get <c>0.5 × 3000 = 1500</c> units per frame.
        ///   <b>That is why cars were flying away through buildings.</b>
        ///   The tornado's constants are only correct together with the tornado's size.
        ///
        /// So here we <b>specify a speed and divide by the radius</b> to turn it into the
        /// rotational component. Then the speed at the rim is the same whatever size the
        /// typhoon is.
        /// </summary>
        private const float WindRimSpeed = 4f;

        /// <summary>
        /// The component drawing in towards the centre (negative is inwards, units per
        /// frame).
        ///
        /// ★ This one is divided by <c>/ Max(1, |delta|)</c>, so <b>the magnitude is
        ///   directly the speed</b> (unlike the rotational component it does not depend on
        ///   the radius).
        ///   A tornado uses -40. Do that on a typhoon and things are fired into the centre.
        /// </summary>
        private const float WindRadial = -3f;

        /// <summary>
        /// How hard we push along the direction of travel (units per frame, at intensity
        /// 255).
        ///
        /// ★ 2.5 -> 6 (2026-09-02, the owner: "if the typhoon could move the cars a little
        ///   more"). <b>This is the only thing that plainly decides how far things are blown
        ///   downwind</b> — rotation and inflow only change the direction, and this is the
        ///   component that carries things across the city.
        /// </summary>
        private const float WindDirectionalScale = 6f;

        /// <summary>
        /// The counter for how many rounds of the "grab" wind we have fired. **Sim thread
        /// only.**
        /// </summary>
        private static int _pushOrdinal;

        /// <summary>
        /// Whether this round should be a "grab" wind. **Call it exactly once per round.**
        ///
        /// ★★ <b>Do not call it per push location.</b> (2026-09-02, noticed right after
        ///   writing it.) One round fires two shots — the centre and <b>what the camera is
        ///   looking at</b> — so counting per location pins them to the even and odd
        ///   positions and <b>the camera's wind never once lands on the "grab" side</b>.
        ///   In the band inside the gale radius that the centre's push does not reach (the
        ///   storm radius is 1/2.2 of the gale radius), it shows up as
        ///   <b>only the cars right in front of you never moving</b>.
        ///   Decide once per round and hand the same answer to every push in that round.
        /// </summary>
        internal static bool NextLatch()
        {
            return (_pushOrdinal++ % LatchEveryNthPush) == 0;
        }

        private static float _minutesSincePass;
        private static ushort _typhoonId;
        private static int _centreCellX = -1;
        private static int _centreCellZ = -1;

        /// <summary>
        /// The ordinal of the next cell to look at (in <see cref="OutwardCellOrder"/>'s
        /// order). 0 is the centre cell.
        /// It only stays non-zero when a sweep was cut short at a cap.
        /// </summary>
        private static int _cursorOrdinal;

        private static bool _errorLogged;
        private static bool _treeNotePosted;

        /// <summary>
        /// Whether we have found that <c>DestroyTrees</c> cannot be resolved in this
        /// environment. Once raised we never call the tree felling again (so it does not
        /// throw on every sweep).
        /// </summary>
        private static bool _treesUnavailable;

        // ── Diagnostic counters (all read and written from the sim thread only) ──
        private static int _passes;
        private static int _lastScanned;
        private static int _lastSelected;
        private static int _lastAttempted;
        private static int _lastRefused;
        private static int _lastCollapsed;
        private static int _lastUnknownHeight;
        private static bool _lastCapped;
        private static int _totalCollapsed;

        /// <summary>How many sweeps have run so far (cumulative for the session).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>How many buildings the most recent sweep examined (those that passed the
        /// candidate mask and were inside the gale radius).</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>How many buildings passed the probability selection in the most recent
        /// sweep.</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>How many buildings vanilla answered "accepted" for in the dry run, in
        /// the most recent sweep.</summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>
        /// How many buildings vanilla answered "refused by design" for in the most recent
        /// sweep (false in both the dry run and the real call, i.e. genuinely not
        /// destroyed).
        /// **Non-zero is normal** (disaster response facilities are not destroyed by a
        /// typhoon. §F-2).
        ///
        /// ★ <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> do not appear here.
        ///   They perform the real collapse immediately after returning false to the dry run
        ///   (§F-2), so they only add to <see cref="LastCollapsed"/>.
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>How many buildings actually collapsed in the most recent sweep.</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>
        /// How many buildings the most recent sweep **could not read a height for**. Unlike
        /// ②, these are not excluded from consideration (they merely forgo the height
        /// bonus).
        /// </summary>
        public static int LastUnknownHeight { get { return _lastUnknownHeight; } }

        /// <summary>Whether the most recent sweep was cut short at a cap (it continues next
        /// time).</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>Buildings collapsed, cumulative for the session.</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>
        /// Call when letting go of a typhoon (<c>TyphoonController.Forget</c>) and on level
        /// unload. It is idempotent (calling it repeatedly is fine).
        /// </summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _minutesSinceGale = 0f;
            _galePushes = 0;
            // ★ Always start the next typhoon's first shot with a "grab" wind (NextLatch).
            _pushOrdinal = 0;
            _typhoonId = 0;
            _centreCellX = -1;
            _centreCellZ = -1;
            _cursorOrdinal = 0;
            _passes = 0;
            _lastScanned = 0;
            _lastSelected = 0;
            _lastAttempted = 0;
            _lastRefused = 0;
            _lastCollapsed = 0;
            _lastUnknownHeight = 0;
            _lastCapped = false;
            _totalCollapsed = 0;

            if (_treeNotePosted)
            {
                _treeNotePosted = false;
                FeatureHost.ClearDegraded(TyphoonFeature.FeatureName, TreeNoteKey);
            }
            // ★ _errorLogged / _treesUnavailable are not reset. Both are "facts about the
            //    game build this DLL is referencing", not per-city state
            //    (the same decision as TyphoonLightning / TyphoonReader).
        }

        /// <summary>
        /// Sim thread. **Always call it from below the pause guard in
        /// <c>TyphoonFeature.OnSimulationTick</c>, and only while a typhoon is running**
        /// (otherwise buildings fall while the game is paused).
        /// When the setting is OFF the caller does not call it.
        ///
        /// <paramref name="snapshot"/> holds **the previous tick's state**, so neither the
        /// position nor the intensity is read from it (the note in
        /// <see cref="TyphoonSnapshot"/>'s T3 section). They are read directly from
        /// <see cref="TyphoonController"/>'s statics on the same thread. It is kept as a
        /// parameter to give every element the same call shape.
        /// </summary>
        public static void Apply(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                Step(deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon wind damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWind",
                             "typhoon wind damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(float deltaMinutes)
        {
            // ★ Advance the interval accumulator **before looking at the typhoon**. Whether
            //    the typhoon changes or disappears, we treat time as still passing
            //    (class doc / I3).
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            if (!TyphoonController.Active)
            {
                _typhoonId = 0;
                _cursorOrdinal = 0;
                _centreCellX = -1;
                _centreCellZ = -1;
                return;
            }

            ushort id = TyphoonController.DisasterId;
            if (id != _typhoonId)
            {
                _typhoonId = id;
                _cursorOrdinal = 0;
                _centreCellX = -1;
                _centreCellZ = -1;
                // **Do not rewind the accumulator** (class doc).
            }

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;

            // Do not carry the remainder over. Even if a large deltaMinutes arrives right
            // after a load, we do not fire repeatedly on consecutive ticks but keep "once
            // per interval".
            _minutesSincePass = 0f;

            int strength = ModSettings.TyphoonWindStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;

            Sweep(id, strength);
        }

        /// <summary>
        /// One sweep. On reaching a cap it breaks off and resumes from
        /// <see cref="_cursorOrdinal"/> next time (the class doc's "ceiling on work per
        /// tick").
        /// </summary>
        private static void Sweep(ushort typhoonId, int strength)
        {
            float range = TyphoonController.GaleRadius;
            // 0 if the prefab radius could not be read. **Do not run on a guessed radius**
            // (design doc §6).
            if (!(range > 0f)) return;

            byte intensity = TyphoonController.Intensity;

            // ★ Do not divide back out of the radius **result** (GaleRadius). At intensity
            //   0, StormRadiusOf is 0 and a division by zero hands a NaN wind-speed
            //   equivalent to every building. Read the input itself (the doc on
            //   TyphoonController.PrefabRadius).
            float prefabRadius = TyphoonController.PrefabRadius;
            if (!(prefabRadius > 0f)) return;

            var centre3 = TyphoonController.Centre;
            if (float.IsNaN(centre3.X) || float.IsNaN(centre3.Z)) return;
            var centre = new Vec2(centre3.X, centre3.Z);

            // ★ Singleton<T>.instance runs FindObjectOfType and new GameObject when
            //    sInstance is null, which makes it a main thread only API, so we check
            //    exists first.
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ Since we stretch the wind-speed field on the dangerous-semicircle side,
            //   **widen the box by the same factor** (class doc). We could widen only one
            //   side, but the box's four edges get rounded to cell boundaries anyway, so
            //   applying it all round reads better, and over-widening only means "counting
            //   nothing and skipping buildings at wind speed 0", which does no harm.
            float scanRange = range * (1f + TrackBias.MaxRadiusBoost);

            // The building grid is 64 m per cell, 270x270 (the same cell size 64, offset
            // 135 and [0,269] clamp as vanilla's DestroyBuildings).
            int minX = Clamp((int)((centre.X - scanRange) / 64f + 135f));
            int maxX = Clamp((int)((centre.X + scanRange) / 64f + 135f));
            int minZ = Clamp((int)((centre.Z - scanRange) / 64f + 135f));
            int maxZ = Clamp((int)((centre.Z + scanRange) / 64f + 135f));

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // The rings are centred on the typhoon's cell. We apply the same clamp as the
            // box, so even with the centre off the map it lands inside the grid.
            int centreX = Clamp((int)(centre.X / 64f + 135f));
            int centreZ = Clamp((int)(centre.Z / 64f + 135f));

            // ★ Throw the sweep position away once the centre cell has moved. Both the box
            //    and the rings become different things, so "continuing" is meaningless
            //    (class doc). **Do not touch the accumulator.**
            if (centreX != _centreCellX || centreZ != _centreCellZ)
            {
                _centreCellX = centreX;
                _centreCellZ = centreZ;
                _cursorOrdinal = 0;
            }

            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(typhoonId);

            // ★ The bias is **re-read every sweep** (class doc). The value cached here is
            //   valid only for this one sweep and is read again on the next — which is why
            //   the bias turns when the track bends.
            float heading = TyphoonController.HeadingRadians;
            bool southern = ModSettings.TyphoonSouthernHemisphere.value;
            int biasedSelected = 0;

            int scanned = 0, selected = 0, attempted = 0, refused = 0, collapsed = 0;
            int unknownHeight = 0, cells = 0;
            bool capped = false;

            int ordinal = _cursorOrdinal;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;
            int startOrdinal = ordinal;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass || scanned >= MaxBuildingsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                bool ok = OutwardCellOrder.Offset(ordinal, out dx, out dz);
                ordinal++;
                if (!ok) continue;

                int x = centreX + dx;
                int z = centreZ + dz;

                // ★ Skip anything outside the box **without counting it**
                //   (OutwardCellOrder's class doc).
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ Take down the next ID **before acting** (the same as ②'s
                    //    LongPeriodDamage). If a third-party AI releases the building on
                    //    collapse, buildings[id] is filled with 0 and the rest of this cell
                    //    is silently skipped.
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        float offsetX = p.x - centre.X;
                        float offsetZ = p.z - centre.Z;
                        float d = Distance(centre, p.x, p.z);

                        // ★★ The dangerous semicircle (class doc). **We do not rewrite the
                        //    radius constants; we stretch the wind-speed field by pretending
                        //    things are closer to the eye.**
                        //    On the left side, and dead ahead and dead astern, the factor is
                        //    exactly 1, so those are unchanged from today by one bit.
                        float radiusFactor = TrackBias.RadiusFactor(heading, offsetX, offsetZ, southern);
                        float wind = TyphoonProfile.WindAt(d / radiusFactor,
                                                           intensity, prefabRadius);
                        if (wind > 0f)
                        {
                            scanned++;

                            // ★ The height is a **coefficient**, not a cut-off (the point
                            //    where we differ from ②). If it cannot be read we simply
                            //    forgo the bonus and keep the building in scope.
                            float metres = BuildingHeight.MetresOf(ref buildings[id]);
                            if (metres <= 0f) unknownHeight++;

                            float chanceFactor = TrackBias.ChanceFactor(heading, offsetX, offsetZ, southern);
                            if (chanceFactor > 1f) biasedSelected++;

                            if (IsSelected(typhoonId, id, wind, metres, strength, chanceFactor))
                            {
                                selected++;
                                bool accepted;
                                bool fell = Collapse(buildings, id, group, out accepted);

                                // ★★ **Do not count what fell as "refused"** (whole-project
                                //    review). The dry run is for diagnostics only, and
                                //    PowerPoleAI / CableCarPylonAI perform the real collapse
                                //    immediately after `if (testOnly) return false;`
                                //    (§F-2). They used to be counted in **both** refused and
                                //    collapsed, which stopped the diagnostics' refused from
                                //    meaning "how many shelters and the like refused by
                                //    design".
                                if (fell) collapsed++;
                                else if (accepted) attempted++;
                                else refused++;
                            }
                        }
                    }

                    id = next;

                    // Insurance against an infinite loop on save data with a corrupt chain.
                    if (++guard > GridChainGuard) break;
                }
            }

            // If we got all the way round, next time starts from the centre. If we were cut
            // short, it continues.
            _cursorOrdinal = ordinal >= ordinalCount ? 0 : ordinal;
            _passes++;
            _lastScanned = scanned;
            _lastSelected = selected;
            _lastAttempted = attempted;
            _lastRefused = refused;
            _lastCollapsed = collapsed;
            _lastUnknownHeight = unknownHeight;
            _lastCapped = capped;
            _totalCollapsed += collapsed;

            // The presentation runs once per sweep, at the typhoon's centre. It is called
            // **independently of the buildings** (the wind blows and trees fall even with 0
            // collapsed).
            //
            // ★★ **The blow-away belongs to <c>TyphoonStormFx</c>'s knob.**
            //    (2026-09-02, Codex review. This had been passing through unconditionally.)
            //    Since that setting's doc states "the blow-away is AddWind (citizens and
            //    vehicles only) … which is why it is on a different knob from wind damage",
            //    <b>cars must not fly about in the city of someone who switched the
            //    presentation off</b>.
            //
            //    Previously the upward component was below the 2g gate (19.62), so even
            //    passing through unconditionally nothing happened to moving cars and nobody
            //    noticed.
            //    Now that we are over the gate, this leak is <b>visible</b>.
            //
            //    ★ When it is switched off we do not advance <see cref="NextLatch"/> either
            //      — advance it and the "grab once every how many" count is thrown off by
            //      the rounds where we fired nothing.
            if (ModSettings.TyphoonStormFx.value)
            {
                PushWind(centre3, group, range, NextLatch());
            }

            FellTrees(typhoonId, centre3, group, range);

            // ★ Do not wrap this in collapsed > 0. That would make "the feature is dead" and
            //    "there are no buildings nearby" indistinguishable in the log (the shape that
            //    actually happened in ③).
            WriteDiag(typhoonId, strength, range, cells, cellCount,
                      startOrdinal, ordinal, scanned, selected, attempted,
                      refused, collapsed, unknownHeight, capped,
                      heading, southern, biasedSelected);
        }

        /// <summary>
        /// Whether to select this building. **The randomness is
        /// <see cref="DeterministicRandom"/>** (not <c>VanillaRandomizer</c>) — this is a
        /// judgement ④ invented and does not need to agree with any value vanilla draws.
        ///
        /// **Do not mix the frame in** (class doc).
        /// </summary>
        private static bool IsSelected(ushort typhoonId, ushort buildingId,
                                       float wind, float heightMetres, int strength,
                                       float chanceFactor)
        {
            float chance = WindDamageModel.CollapseChance(wind, heightMetres, strength);
            if (chance <= 0f) return false;

            // ★ The dangerous semicircle's uplift is applied **outside the model**.
            //   WindDamageModel only knows how to turn wind speed, height and the setting
            //   into a probability; it does not know the typhoon's heading. A broken factor
            //   arriving here must not derange the draw.
            if (!float.IsNaN(chanceFactor) && chanceFactor > 1f) chance *= chanceFactor;
            if (chance > 1f) chance = 1f;

            float roll = DeterministicRandom.Unit(typhoonId, buildingId);
            return roll < chance;
        }

        /// <summary>
        /// Actually knock it down. **Do not go via <c>DisasterHelpers</c>** (class doc).
        ///
        /// **Always make the real call even when the dry run returns false** —
        /// <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> perform the real collapse immediately
        /// after <c>if (testOnly) return false;</c> (§F-2). Filter on the dry run and not one
        /// power pole ever comes down.
        /// </summary>
        /// <param name="accepted">
        /// Whether vanilla itself answered "accepted" to the dry run. false means "refused
        /// by design" (a disaster response facility, or a power pole) and is not a fault.
        /// </param>
        private static bool Collapse(Building[] buildings, ushort id,
                                     InstanceManager.Group group, out bool accepted)
        {
            accepted = false;

            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            var ai = info.m_buildingAI;

            // demolish: false (leave the rubble; disaster response facilities refuse, which
            // is correct behaviour), burnAmount: 0 (the wind blows things over and crushes
            // them; it does not scorch them).
            // ★ Do not touch m_fireIntensity (trap 5).
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// Blow citizens and vehicles about. **Harmless** (it is just the two lines
        /// <c>AddWindCitizens</c> + <c>AddWindVehicles</c> and touches neither buildings,
        /// roads nor trees. §B-1).
        /// The shape of the arguments is modelled on the tornado's actual arguments in §B-1,
        /// **except for the rotational component** (the ★★ on
        /// <see cref="WindRimSpeed"/>).
        ///
        /// </summary>
        /// <param name="latch">
        /// <see cref="NextLatch"/>'s answer. <c>true</c> means **grab the cars and citizens
        /// not yet grabbed** with the high <see cref="WindUpwardLatch"/> wind; <c>false</c>
        /// means push **only what has already been grabbed** with the low wind.
        /// </param>
        private static void PushWind(Vec3 centre, InstanceManager.Group group, float range,
                                     bool latch)
        {

            float heading = TyphoonController.HeadingRadians;
            float speed = TyphoonController.Intensity / 255f * WindDirectionalScale;

            float radius = range * WindRadiusFactor;
            if (!(radius > 0f)) return;

            var position = new Vector3(centre.X,
                                       centre.Y + range * WindHeightFraction,
                                       centre.Z);
            var directional = new Vector3(
                Mathf.Cos(heading) * speed,
                latch ? WindUpwardLatch : WindUpwardSustain,
                Mathf.Sin(heading) * speed);

            // ★★ Pass the rotational component <b>after dividing by the radius</b> (the ★★
            //    in WindRimSpeed's doc). Make it a constant here and cars fly faster the
            //    bigger the typhoon.
            float rotational = WindRimSpeed / radius;

            DisasterHelpers.AddWind(position, radius, directional,
                                    rotational, WindRadial, group);
        }

        /// <summary>
        /// Fell trees. **Do not set them alight** (<c>burnRadiusMin</c> /
        /// <c>burnRadiusMax</c> are 0) — trees catching fire in a typhoon makes no sense.
        /// We do not use <c>TreeManager.BurnTree</c> either.
        ///
        /// This one route may be unresolvable in some environments (in which case we give up
        /// on felling trees and keep the rest of the wind damage running, self-reporting
        /// through <c>FeatureHost.NoteDegraded</c>).
        /// </summary>
        private static void FellTrees(ushort typhoonId, Vec3 centre,
                                      InstanceManager.Group group, float range)
        {
            if (_treesUnavailable) return;

            float outer = range * TreeRadiusFraction;
            if (!(outer > 0f)) return;
            float inner = outer * TreeInnerFraction;

            var position = new Vector3(centre.X, centre.Y, centre.Z);

            try
            {
                DisasterHelpers.DestroyTrees(typhoonId, group, position,
                                             outer,      // totalRadius (the first-pass culling)
                                             0f,         // removeRadius (the range wiped without trace)
                                             inner,      // destructionRadiusMin
                                             outer,      // destructionRadiusMax
                                             0f, 0f);    // ★ do not set alight
            }
            catch (System.Exception e)
            {
                // Once it throws even once, never call it again. **Do not give up
                // silently.**
                _treesUnavailable = true;
                Log.Warn("typhoon wind: DisasterHelpers.DestroyTrees is unusable in this build ("
                         + e.GetType().Name + "); the wind sweep keeps running without felling "
                         + "trees");
                UpdateTreeNote();
            }
        }

        private static void UpdateTreeNote()
        {
            if (_treeNotePosted) return;
            _treeNotePosted = true;
            FeatureHost.NoteDegraded(TyphoonFeature.FeatureName, TreeNoteKey,
                "DisasterHelpers.DestroyTrees could not be called; the typhoon still collapses "
                + "buildings and pushes citizens, but it fells no trees");
        }

        /// <summary>
        /// The disaster group. Passing it makes vanilla's own tally (buildings damaged per
        /// disaster) add up correctly.
        /// <c>null</c> if <c>InstanceManager</c> is not there yet (vanilla itself has routes
        /// that pass null. Only the tally is lost; the collapses and the wind still run).
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            // Singleton<T>.instance runs FindObjectOfType and new GameObject when sInstance
            // is null, which makes it a main thread only API. This is the sim thread.
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **Written every time, even when 0 collapsed.** <c>Log.Diag</c> thins the same key
        /// down to once per 512 sim frames, but **the string concatenation in the arguments
        /// would still run every time**, so we bail out first with <c>DiagEnabled</c>
        /// (C# evaluates the arguments fully before the call).
        /// </summary>
        private static void WriteDiag(ushort typhoonId, int strength, float range,
                                      int cells, int cellCount, int startOrdinal, int ordinal,
                                      int scanned, int selected, int attempted, int refused,
                                      int collapsed, int unknownHeight, bool capped,
                                      float heading, bool southern, int biasedSelected)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWind",
                "wind pass#" + _passes + " typhoon#" + typhoonId
                + " strength=" + strength
                + " galeRadius=" + range.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                // The sweep goes outwards from the centre cell (ordinal 0). We also print
                // where it will resume — so that "it has not even reached the centre" is
                // visible in the diagnostics.
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
                + " scanned=" + scanned + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " collapsed=" + collapsed
                + " unknownHeight=" + unknownHeight
                // ★ Which way the dangerous semicircle faces, and how many buildings were on
                //   that side. The only way to tell "the bias is not working" from "there are
                //   no buildings on that side".
                + " dangerousSide=" + (southern ? "left" : "right")
                + " heading=" + (heading * 57.29578f).ToString("F0") + "deg"
                + " onDangerousSide=" + biasedSelected
                + (_treesUnavailable ? " trees=unavailable" : " trees=felled")
                // ★ Do not write "continues next time" (whole-project review I1). Once the
                //   centre cell changes, _cursorOrdinal goes back to 0 and the next sweep
                //   starts again from the eye — see the class doc's "truncated continuation"
                //   section for details.
                + (capped ? " (capped; the outer edge was not rolled this pass)" : ""));
        }

        private static float Distance(Vec2 centre, float x, float z)
        {
            float dx = x - centre.X;
            float dz = z - centre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > GridSide - 1) return GridSide - 1;
            return v;
        }
    }
}
