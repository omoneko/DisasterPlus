using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>A tsunami that rises in concentric rings from the epicentre.</b>
    /// **Sim thread only.**
    ///
    /// ── What it actually does ─────────────────────────────────────────────
    ///
    /// Place one <c>WaterSource</c> of <c>TYPE_NATURAL</c> at the epicentre and move its
    /// <c>m_target</c> (the water level the disc fills to) up and down on the <b>same
    /// waveform</b> the DLC tsunami uses. Below the target the disc <b>creates water</b>,
    /// above it the disc <b>takes water away</b>, so <b>drawback → crest → drawback</b>
    /// comes out into the sea directly. The waveform itself lives in
    /// <see cref="TsunamiRingShape"/> (the Core layer).
    ///
    /// ── Why <c>TYPE_IMPACT</c> was abandoned ──────────────────────────────
    ///
    /// The predecessor, <c>TsunamiWave</c>, used <c>TYPE_IMPACT</c>: a <b>virtual hill</b>
    /// placed under the sea. A hill only displaces water, it <b>never creates any</b>, so
    /// all it can produce is a dipole that nets to zero. Put side by side offline
    /// (2026-08-31, 174 m of water, shoreline 6.1 km away):
    ///
    /// <list type="bullet">
    /// <item>the DLC tsunami (boundary condition at the map rim, intensity 100) …
    ///   <b>84.8 m</b> at the shoreline</item>
    /// <item>the <c>TYPE_IMPACT</c> hill (drive 3857) … <b>19.3 m</b> at the shoreline</item>
    /// </list>
    ///
    /// The difference was not amplitude but <b>whether water is created at all</b>. No
    /// amount of hill imitates a spring; it only digs through the seabed.
    ///
    /// ★ It cannot compete with the DLC's 84.8 m head on, though. That is a line source
    ///   using <b>the whole side of the map</b>, and this is a point source, so it loses
    ///   whatever the geometry spreads. What matters is not the number but
    ///   <b>whether it looks like a great tsunami</b>.
    ///
    /// ── What it can produce (measured 2026-08-31; only layouts where the disc does
    ///    not touch the shoreline) ───────────────────────────────────────────
    ///
    /// **Deep sea** (174 m of water, i.e. a map with a high sea level. Crest cap 40 m):
    ///
    /// <list type="bullet">
    /// <item>5.2 km from the epicentre … <b>35.5 m</b> at the shore, <b>1,328 m</b> inland</item>
    /// <item>8.7 km from the epicentre … 25.9 m at the shore, 912 m inland</item>
    /// <item>13.0 km from the epicentre … 23.0 m at the shore, 848 m inland</item>
    /// </list>
    ///
    /// **A standard map** (40 m of water, the default 40 m sea level. The cap is bound by
    /// the depth, so 20 m):
    ///
    /// <list type="bullet">
    /// <item>5.2 km from the epicentre … <b>20.6 m</b> at the shore, 704 m inland</item>
    /// <item>8.7 km from the epicentre … 13.1 m at the shore, 432 m inland</item>
    /// </list>
    ///
    /// ★★ **Both are settings confirmed not to overflow int32 in the real game.**
    ///   The offline reproduction counts <b>how many times the game's int32 would have
    ///   overflowed</b> (<c>Int32Overflows</c> in <c>tools/WaterSolverSim/SourceDisc.cs</c>).
    ///   Until that counter existed, a setting that <b>only the reproduction returned a
    ///   clean answer for</b> was mistaken for a good setting.
    ///
    /// ★★ **But simply calling the DLC tsunami is not analysis.** (the owner, 2026-08-31)
    ///   That one is <b>only evaluated at the map rim</b>, so it never forms rings around
    ///   an epicentre. <c>WaterSource</c> carries the same "fill up to a target level"
    ///   machinery in a form that can be <b>placed anywhere</b>, so <b>keep the DLC's
    ///   waveform and move only where it is applied</b> — to the epicentre.
    ///
    /// ── The dangers (the same as <c>TyphoonFlood</c>'s class doc, one notch worse) ──
    ///
    /// <list type="number">
    /// <item><c>LockWaterSource</c> <b>returns while still holding the Monitor</b>.
    ///   <c>UnlockWaterSource</c> is <b>the only way to release it</b>, so it must sit in a
    ///   <c>finally</c>. Drop it and <b>the water thread stops for good</b>.</item>
    /// <item><c>LockWaterSource</c> does not bounds-check the index. Never call it without
    ///   going through <see cref="OwnsSource"/> first.</item>
    /// <item>★★ <b>A <c>WaterSource</c> is baked into the save</b>
    ///   (<c>WaterSimulation+Data.Serialize</c> writes it — confirmed in the IL).
    ///   Save with one still placed and that city has <b>a spring that runs for ever</b>,
    ///   even after the mod is removed. So release it in all three places:
    ///   <list type="bullet">
    ///   <item>when the waveform ends</item>
    ///   <item>when leaving the city (<c>Reset</c>)</item>
    ///   <item><b>immediately before a save</b> (<see cref="SuspendForSave"/>)</item>
    ///   </list>
    ///   <c>TyphoonFlood</c> says "do not reach for <c>CreateWaterSource</c>", and what it
    ///   means is: not unless you can honour those three.</item>
    /// <item><c>CreateWaterSource</c> <b>reuses</b> any slot with <c>m_type == 0</c>
    ///   (first fit, IL_0020-005A). So <b>holding the index is not enough</b> — always
    ///   check the type and the position before writing.</item>
    /// </list>
    /// </summary>
    public static class TsunamiRing
    {
        /// <summary><c>WaterSource.TYPE_NATURAL</c>.</summary>
        private const ushort TypeNatural = 1;

        /// <summary>
        /// One water step = 64 sim frames (measured in the IL, <c>SetCurrentWaterFrame</c>).
        /// </summary>
        private const int FramesPerWaterStep = 64;

        /// <summary>Half the width of the map (m).</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// Radius of the disc at the epicentre (m). **What carries a wave a long way is
        /// volume, not height**, so this is the constant that matters (measured offline
        /// 2026-08-31: radius 1280 m → 35 m at the shore, radius 3840 m → 83 m at the
        /// shore, both at intensity 100, 174 m of water, shoreline 6.1 km away).
        /// </summary>
        public const float RadiusMetres = 3840f;

        /// <summary>
        /// <b>The cap on the crest (m, absolute).</b> The value at intensity 255.
        ///
        /// ★★ **Raised from 40 m to 130 m on 2026-09-02.**
        ///   The owner reported from the game that "even at maximum it is no more than a
        ///   storm surge". <b>This cap</b> was the cause.
        ///
        ///   The table below — the one that says raising it makes the wave weaker — was
        ///   measured <b>back when the drawback disc was 3,840 m</b>. That setting
        ///   corrupts the game's int32 and cannot be used
        ///   (<see cref="DrainRadiusMetres"/>), and with today's safe setting
        ///   <b>the relationship is the other way round</b>. Measured again (165 m of
        ///   water, radius 2,048 m — the same conditions as the owner's map — land
        ///   sloping 2 m per cell):
        ///
        /// <list type="bullet">
        /// <item>cap 40 m … 32.8 m at the shore, 288 m inland, no seabed exposed</item>
        /// <item>cap 80 m … 57.8 m at the shore, 528 m inland, none exposed</item>
        /// <item><b>cap 130 m … 82.2 m at the shore, 768 m inland, 5 steps exposed</b></item>
        /// <item>cap 190 m … 89.9 m at the shore, 944 m inland, <b>63 steps</b> exposed</item>
        /// <item>cap 260 m … 89.7 m at the shore, 944 m inland, 65 steps exposed</item>
        /// </list>
        ///
        /// ★★ **130 m is the turning point.** Up to there the wave grows roughly in step
        ///   with the cap; past it the wave levels off while <b>the bare seabed alone grows
        ///   twelve-fold</b> — in exchange for 9% more reach. So it stops here.
        ///
        /// ★ Raising the cap shrinks the drawback disc automatically (the overflow guard,
        ///   which rounds <c>_drainRate</c> down). At 130 m it is 101 m. Even so there are
        ///   no overflows at all, and the figures above are what comes out.
        ///
        /// ── Below is the table from the old setting (drawback disc 3,840 m).
        ///    **Do not use it as a guide.**
        ///
        /// (measured offline 2026-08-31, 1081 grid, shoreline 13 km away, 768 water steps)
        ///
        /// <list type="bullet">
        /// <item>cap 40 m … <b>66.98 m</b> at the shore, <b>2592 m</b> of inundation,
        ///   189.7 m at the epicentre</item>
        /// <item>cap 55 m … 65.38 m at the shore, 2384 m of inundation, 233.2 m at the epicentre</item>
        /// <item>cap 70 m … 64.56 m at the shore, 2240 m of inundation, 255.4 m at the epicentre</item>
        /// <item>cap 20 m … 25.44 m at the shore, 1280 m of inundation, 100.0 m at the epicentre</item>
        /// </list>
        ///
        /// Building a taller tower does not carry the water further — what counts is
        /// <b>volume</b>.
        ///
        /// ★★ <b>This table is from when the drawback disc was 3,840 m.</b>
        ///   That setting corrupts the game's int32 and cannot be used
        ///   (<see cref="DrainRadiusMetres"/>). For the real figures under a usable
        ///   setting, see the table in the class doc. It is kept here only for
        ///   <b>the direction it shows — that raising the cap weakened the wave</b>.
        ///
        /// ★★ It is also <b>bound by the depth</b> (<see cref="MaxRiseFraction"/>).
        ///   The cap itself works regardless of depth, but <b>in shallow water the wave
        ///   leaving carries off the water at the epicentre and lays the seabed bare</b>.
        ///   Measured (epicentre 5.2 km away):
        ///
        /// <list type="bullet">
        /// <item>25 m of water, cap 40 m … seabed exposed for
        ///   <b>297 water steps (about 5 real minutes)</b></item>
        /// <item>25 m of water, cap 12 m … <b>never</b> exposed, 12.8 m at the shore</item>
        /// <item>40 m of water, cap 20 m … <b>never</b> exposed, 20.6 m at the shore</item>
        /// <item>174 m of water, cap 40 m … <b>never</b> exposed, 35.5 m at the shore</item>
        /// </list>
        ///
        ///   ★ There is no escaping this by demanding deep water — the vanilla sea level
        ///     defaults to 40 m and <b>the seabed cannot go below elevation 0</b>, so a
        ///     standard map has at most 40 m of sea.
        /// </summary>
        private const float MaxRiseMetres = 130f;

        /// <summary>
        /// The cap on the crest as a fraction of the depth. <b>The smaller of this and
        /// <see cref="MaxRiseMetres"/> wins.</b>
        ///
        /// ★★ **Raised from 0.5 to 0.8** (2026-09-02). At 0.5, even a map with 165 m of
        ///   water capped out at 82 m, which threw away the point of setting
        ///   <see cref="MaxRiseMetres"/> to 130 m. This fraction exists to protect
        ///   shallow water, so there is no need for it to bind deep water too.
        ///
        /// ★ Checked on the shallow side: 40 m of water with a 32 m cap (= 0.8) exposes
        ///   the seabed for 4 steps; 25 m of water with a 20 m cap exposes it for none
        ///   (measured 2026-08-31).
        ///
        /// ★ The upshot is that <b>deeper water gives a bigger tsunami</b>. That is right
        ///   physically, and it gives a reason to put a trench quake out to sea.
        /// </summary>
        private const float MaxRiseFraction = 0.8f;

        /// <summary>
        /// <b>Radius of the drawback disc (m). Not the same as the crest disc.</b>
        ///
        /// ★★ **This is the limit the game's integer arithmetic imposes.**
        ///   (2026-08-31, verified in the IL)
        ///
        ///   The take path (<c>SimulateWater</c> IL_1B4C-1B58) computes
        ///   <c>share * take</c> with an <b>int32 <c>mul</c></b> (there is no
        ///   <c>conv.i8</c> anywhere). <c>take = min(inputRate, total&gt;&gt;1)</c>, and
        ///   <c>inputRate</c> follows from the radius as <c>((r-10)/0.4)^2</c>, so
        ///   <b>enlarging the disc always pushes the product past 2^31</b>. Past that,
        ///   <c>m_height = (ushort)(h - share)</c>, so <b>the cell's height is corrupted</b>.
        ///
        ///   The crest (output) side has no such product, so 3840 m is fine there. The
        ///   reason vanilla never trips over this is that a map's river sources are small.
        ///
        /// ★ Measured (the offline reproduction counts whether the game's int32 would have
        ///   overflowed): a radius of 250 m overflows in some layouts (worst product
        ///   2.35e9). <b>160 m overflowed in none of the 8 combinations of 174/60 m of
        ///   water × 2.6-13.0 km to the epicentre.</b>
        ///
        /// ★ Not being able to enlarge the drawback costs some force, but little: the
        ///   shoreline still sees 54 m at 2.6 km, 35 m at 5.2 km and 23 m at 13 km. If
        ///   anything the epicentre gets calmer (a 190 m column becomes 63 m).
        /// </summary>
        private const float DrainRadiusMetres = 160f;

        /// <summary>
        /// Ceiling on the take disc's <c>total</c> (the sum in units of 1/64 m).
        /// A 160 m radius is 10 cells, so 314 cells, and one cell holds at most 65535.
        /// Rounded up to leave room.
        /// </summary>
        private const long MaxTotalUnits = 22000000L;

        /// <summary>
        /// The fraction of the depth the drawback may take. **Never bare the seabed.**
        /// Unlike the crest, this one is <b>bound by the depth</b> — with 60 m of water and
        /// a 60 m cap the whole column drained and the seabed lay exposed for 94 water
        /// steps (measured 2026-08-31). Using 0.5 costs deep water nothing (still 66.98 m
        /// at the shore).
        /// </summary>
        private const float MaxDrawFraction = 0.5f;

        /// <summary>
        /// <summary>
        /// The largest number of discs laid along the fault. **5 is the ceiling**
        /// (see the table on <see cref="_sources"/>).
        /// </summary>
        private const int MaxSegments = 5;

        /// Length of the waveform (water steps). Vanilla uses 256.
        ///
        /// ★★ **Length matters.** At the same 40 m cap, 512 steps gives 52.77 m at the
        ///   shore and 768 steps gives 66.98 m (measured 2026-08-31). But 1024 steps drops
        ///   back to 29.80 m — the amplitude's decay term <c>(65536 - t)/65536</c>
        ///   approaches 0 towards the end, so the later crest disappears.
        ///   768 water steps = 49,152 sim frames, roughly 13.6 real minutes.
        /// </summary>
        private const int DurationSteps = 768;

        /// <summary>
        /// The same length in ticks. This is <b>also the waveform's period</b>
        /// (see the doc on LevelOffsetUnits).
        /// </summary>
        private const int DurationTicks = DurationSteps * TsunamiRingShape.TicksPerWaterStep;

        /// <summary>
        /// The lock guarding <c>_source</c> and <c>_running</c>.
        ///
        /// ★★ **It is necessary.** (2026-08-31, cross-verified) <c>Reset</c> is called
        ///   from <c>LoadingExtensionBase.OnLevelUnloading</c> on the <b>main thread</b>,
        ///   and <c>SuspendForSave</c> from <c>OnSaveData</c>. Both <b>run at the same time
        ///   as</b> <see cref="Tick"/> on the sim thread (<c>LoadingManager.UnloadLevel</c>
        ///   calls <c>OnLevelUnloading</c> synchronously before it stops the sim).
        ///
        /// ★★ <b>The game never takes this lock</b>, so no cycle can form with the
        ///   <c>m_waterSources</c> Monitor. The order must nonetheless <b>always be
        ///   _gate → m_waterSources</b>.
        /// </summary>
        private static readonly object _gate = new object();

        // ★★ **Never put a never-again latch here.** (2026-08-31, third review)
        //    A <c>_shutDown</c> flag was added in response to the second review, and
        //    **that turned out to be the worst defect of the lot.** <c>OnReleased</c> does
        //    not only arrive when the mod is removed — read the IL
        //    (<c>ThreadingWrapper.GetImplementations</c>) and it arrives on
        //    <c>eventPluginsChanged</c> / <c>eventPluginsStateChanged</c> and
        //    <b>every time you return to the main menu</b>, after which the object is
        //    <b>immediately rebuilt</b>. So merely toggling an unrelated mod, or leaving a
        //    city, would throw the latch and <b>no tsunami would ever happen again until
        //    the process was restarted</b>. Far more likely, and far worse, than the leak
        //    it was meant to prevent.
        //
        //    What stops the leak today is <c>OnReleased</c> calling
        //    <c>TsunamiChain.Reset()</c> (clearing the booking) plus
        //    <c>TsunamiRing.Reset()</c> (releasing the water source and tidying up). If the
        //    extension really is being removed, the booking is gone so <c>Begin</c> is
        //    never called; if it is being rebuilt, the next tick handles it as usual.

        // ★ The four below are read and written by the sim thread outside the lock, and
        //   read by the main thread for the panel. **Correctness rests on re-checking
        //   through _gate inside Drive / Release, not on volatile** — volatile only stops
        //   a loop spinning on a stale value.
        /// <summary>
        /// Indices of the water sources placed. **A 0 entry means "we do not hold one".**
        ///
        /// ★★ **A line, not a single point.** (2026-09-02, the answer to the owner's
        ///   report that "even at maximum it is no more than a storm surge".) A point
        ///   source thins out in proportion to the circumference, so it must fall away
        ///   with distance — the reason the DLC tsunami is strong is that it is
        ///   <b>a line source using the whole side of the map</b>.
        ///
        ///   A megathrust fault really does rupture in a line hundreds of kilometres long,
        ///   so representing it as a point is the less natural choice. Measured (165 m of
        ///   water, radius 2,048 m, cap 130 m, shoreline 5.2 km away):
        ///
        /// <list type="bullet">
        /// <item>1 (a point) … 81.8 m at the shore, 768 m inland, seabed exposed for
        ///   5 steps, 100% drawdown at the epicentre</item>
        /// <item>3 …… <b>126.8 m</b> at the shore, 1,280 m inland, <b>never</b> exposed,
        ///   29% drawdown</item>
        /// <item><b>5 …… 152.2 m at the shore, 1,504 m inland, never exposed,
        ///   25% drawdown</b></item>
        /// <item>9 …… 148.2 m at the shore (<b>levelled off</b>), 1,424 m inland</item>
        /// </list>
        ///
        /// ★ It does not only get stronger, it <b>behaves better</b> — drawing the water
        ///   from a wide front instead of sucking it up at one point stops the seabed
        ///   being exposed at all.
        ///
        /// ★★ The int32 limit (<see cref="DrainRadiusMetres"/>) applies <b>to each disc
        ///   separately</b>, so lining them up breaks nothing.
        /// </summary>
        private static ushort[] _sources = new ushort[MaxSegments];

        /// <summary>How many discs are lined up (0 means nothing is placed).</summary>
        private static volatile int _segmentCount;

        /// <summary>
        /// The number of segments that were lifted for a save (needed to put them back).
        /// </summary>
        private static int _suspendedCount;

        /// <summary>
        /// The epicentre. <b>Must not be cleared in <see cref="Reset"/>.</b>
        ///
        /// ★★ This is the fingerprint of ownership. Clear it and <see cref="OwnsSource"/>
        ///   mistakes somebody else's water source sitting at <c>Vector3.zero</c> for one
        ///   of ours, and <b>deletes their river</b>. Carrying it over into the next city
        ///   is harmless: nothing happens unless the position matches.
        /// </summary>
        private static Vector3 _centre;

        /// <summary>
        /// The centre of each disc. <b>Must not be cleared in <see cref="Reset"/></b>
        /// (for the same reason as <see cref="_centre"/>).
        /// </summary>
        private static Vector3[] _centres = new Vector3[MaxSegments];
        private static long _rate;
        private static int _deltaUnits;
        private static volatile int _ticks;
        private static int _seaUnits;
        private static int _depthUnits;
        private static int _riseCapUnits;
        private static int _drawCapUnits;
        private static long _drainRate;
        private static volatile uint _lastFrame;
        private static volatile bool _running;

        /// <summary>Whether a tsunami is running right now.</summary>
        public static bool Running { get { return _running; } }

        /// <summary>The most recent reason (shown on the panel and in diagnostics).</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// <b>The kind of refusal.</b> Needed to choose the one line shown to the player.
        ///
        /// ★★ <see cref="Detail"/> is a sentence of English, so it cannot go on screen as
        ///   it is. For a long time the panel said <b>"this is an inland map" whatever the
        ///   reason</b> — the worst possible line, sending someone refused out in deep
        ///   water in exactly the wrong direction (2026-08-31, cross-verified).
        /// </summary>
        public enum Refusal
        {
            None = 0,
            NotSea,       // the epicentre is not at sea (normal on an inland map)
            NotEnoughRoom,// the sea is too narrow for the disc to fit
            Busy,         // the previous tsunami is still running
            NoRoomInGame, // the game would not give us a water-source slot
            TooWeak,      // the quake is too weak for the wave to be visible
        }

        /// <summary>
        /// Record why we refused, when the refusal happened before <see cref="Begin"/> was
        /// ever called.
        ///
        /// ★★ Without this, a refusal by <c>TsunamiChain</c> on the intensity leaves
        ///   <see cref="LastRefusal"/> at <c>None</c> and the panel says
        ///   <b>"this is an inland map"</b> (2026-08-31, fifth review).
        /// </summary>
        public static void NoteRefusal(Refusal reason, string detail)
        {
            LastRefusal = reason;
            Detail = detail;
        }

        /// <summary>The kind of the most recent refusal.</summary>
        public static Refusal LastRefusal { get; private set; }

        /// <summary>
        /// How far the current target level sits from the resting level (m). For diagnostics.
        /// </summary>
        public static float OffsetMetres { get; private set; }

        /// <summary>Depth of the water at the epicentre (m).</summary>
        public static float DepthMetres { get { return _depthUnits / 64f; } }

        /// <summary>
        /// The highest crest seen so far (m, measured on the target level).
        /// </summary>
        public static float PeakRiseMetres { get; private set; }

        /// <summary>Which step of the waveform we are on.</summary>
        public static int ElapsedSteps
        {
            get { return _ticks / TsunamiRingShape.TicksPerWaterStep; }
        }

        /// <summary>Length of the waveform (water steps).</summary>
        public static int TotalSteps { get { return DurationSteps; } }

        /// <summary>
        /// Call when leaving the city or switching the feature off. **Idempotent. Never
        /// throws.**
        /// ★★ Miss this and the water source is left in the save (class doc, §3).
        /// </summary>
        public static void Reset()
        {
            lock (_gate)
            {
                ReleaseLocked();

                // ★★ Even if the index we held has come loose for some reason,
                //    **always delete any source carrying our fingerprint**. Skip this and
                //    the city is left with a spring that survives removing the mod
                //    (class doc, §3).
                SweepOursLocked();

                _running = false;
                _ticks = 0;
                _lastFrame = 0u;
                // ★ Do not carry a refusal reason over into the next city (fifth review).
                LastRefusal = Refusal.None;
                Detail = null;
                _deltaUnits = 0;
                _rate = 0L;
                OffsetMetres = 0f;
                PeakRiseMetres = 0f;
                // ★ _centre is not cleared (see that field's doc).
            }
        }

        /// <summary>
        /// Raise a tsunami. **Call from the sim thread.**
        /// </summary>
        /// <returns>
        /// Whether it was raised. On false, <see cref="Detail"/> holds the reason.
        /// </returns>
        public static bool Begin(Vec3 epicentre, byte intensity, uint frame)
        {
            Detail = null;
            LastRefusal = Refusal.None;

            lock (_gate)
            {
            if (_running)
            {
                Detail = "a tsunami is already running";
                LastRefusal = Refusal.Busy;
                return false;
            }

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                Detail = "the water simulation is not there";
                LastRefusal = Refusal.NoRoomInGame;
                return false;
            }

            float depth = TsunamiWave.DepthAt(terrain, epicentre.X, epicentre.Z);
            if (depth <= 0f)
            {
                Detail = "the epicentre is not in the sea";
                LastRefusal = Refusal.NotSea;
                return false;
            }

            _seaUnits = (int)(terrain.WaterSimulation.m_currentSeaLevel * 64f);
            _depthUnits = (int)(depth * 64f);
            _centre = new Vector3(epicentre.X, 0f, epicentre.Z);
            // ★★ **Fit the disc to how much sea there is.** (2026-08-31, second review)
            //    The output path <b>puts water on land cells too, wherever they sit below
            //    the target</b> (IL_1E51: the only cells skipped are those with
            //    <c>terrain &gt;= target</c>). So if the disc overlaps the shore, no wave
            //    arrives — <b>the low ground simply fills, in the shape of the disc</b>.
            //    And since the take disc is only 160 m, it <b>cannot be undone</b>.
            float toLandX, toLandZ;
            float radius = OpenWaterRadius(terrain, epicentre.X, epicentre.Z,
                                           out toLandX, out toLandZ);
            if (radius <= 0f)
            {
                Detail = "there is not enough open water around the epicentre - a source "
                         + "circle needs at least "
                         + MinUsefulRadiusMetres.ToString("F0")
                         + " m of sea at least " + MinSourceDepthMetres.ToString("F0")
                         + " m deep in every direction, or it would simply fill the low "
                         + "ground around it instead of making a wave";
                LastRefusal = Refusal.NotEnoughRoom;
                return false;
            }

            _rate = TsunamiRingShape.RateForRadiusMetres(radius);
            _drainRate = TsunamiRingShape.RateForRadiusMetres(DrainRadiusMetres);
            _deltaUnits = TsunamiRingShape.VanillaDeltaUnits(intensity);

            // ★ The cap follows the intensity: 40 m at 255. **Raising it beyond that
            //   made the wave weaker**, so a stronger quake does not mean a taller tower
            //   (see the doc on MaxRiseMetres).
            // ★★ **Dividing by 255 was wrong.** (2026-08-31, fourth review)
            //    The default the tile sends is vanilla's own slider default of 55.
            //    Divide by 255 and the cap comes to just 8.6 m, which is a completely
            //    different wave from the one in the class doc's table (measured at caps of
            //    40 m and 20 m) — <b>of course anyone playing on the default called it
            //    weak.</b>
            //
            //    Vanilla's slider tops out at 100, so **saturate at 100**:
            //    the default 55 gives 71 m, the maximum 100 gives 130 m.
            //
            // ★ Going above 100 does not raise the cap any further. Past 130 m the reach
            //   levels off and only the bare seabed grows (see the ★★ table on
            //   MaxRiseMetres). The unlocked intensity goes into the shaking and the
            //   damage instead.
            int forCap = intensity > 100 ? 100 : intensity;
            _riseCapUnits = (int)(MaxRiseMetres * 64f * forCap / 100f);

            // ★ A floor, so the waveform does not vanish for a weak quake. It goes
            //   **before** the depth cap — put it after and, in under 2 m of water, the
            //   floor cancels the cap and a large crest comes back into shallow water
            //   (2026-08-31, caught by Codex).
            if (_riseCapUnits < 64) _riseCapUnits = 64;

            // ★★ In shallow water the depth binds it (see the doc on MaxRiseFraction).
            //    **This must come last.**
            int byDepth = (int)(_depthUnits * MaxRiseFraction);
            if (byDepth < 0) byDepth = 0;
            if (_riseCapUnits > byDepth) _riseCapUnits = byDepth;

            // ★ The drawback is bound by the depth, and never draws deeper than the
            //   crest cap.
            _drawCapUnits = (int)(_depthUnits * MaxDrawFraction);
            if (_drawCapUnits > _riseCapUnits) _drawCapUnits = _riseCapUnits;
            if (_drawCapUnits < 0) _drawCapUnits = 0;

            // ★★ **Round the rate down to one that cannot overflow.** (see the doc on
            //    DrainRadiusMetres) Estimate the worst excess a single cell can reach, and
            //    never emit a rate beyond the point where `share * take` still fits in an
            //    int32. The estimate is the measured worst case (102 m at a 40 m cap) with
            //    three times the headroom.
            // ★★ What the game holds in an int32 is <c>share*take + total - 1</c>.
            //
            // ★ **Dividing by 4 was overkill.** (2026-08-31, third review)
            //   <c>total</c> is not a factor in the product, it is <b>an addend</b>, so the
            //   headroom needed is a subtraction, not a division. <c>total</c> tops out at
            //   "cells in the take disc × 65535" — at a 160 m radius that is 314 cells,
            //   roughly 2.1e7, just 1% of an int. Dividing by 4 shrinks the disc to 85 m
            //   and <b>leaves the 160 m that the measurements actually vouch for</b>.
            long worstShare = 3L * (_riseCapUnits + _drawCapUnits);
            if (worstShare > 0L)
            {
                long safe = (int.MaxValue - MaxTotalUnits) / worstShare;
                if (_drainRate > safe) _drainRate = safe;
                if (_drainRate < 1L) _drainRate = 1L;
            }

            _ticks = 0;
            _lastFrame = frame;
            OffsetMetres = 0f;
            PeakRiseMetres = 0f;

            // ★★ **Lay out the fault.** (see the ★★ on <see cref="_sources"/>)
            //    Its bearing is perpendicular to the direction of the nearest land, so the
            //    wave front runs parallel to the coast and comes in towards it — the same
            //    shape as a real trench. The number of segments follows the intensity
            //    (a weak quake is a point, a strong one gets 5 segments).
            int wanted = 1 + (int)(MaxSegments - 1) * (intensity > 100 ? 100 : intensity) / 100;
            if (wanted < 1) wanted = 1;
            if (wanted > MaxSegments) wanted = MaxSegments;

            float alongX = -toLandZ;   // perpendicular to the direction of land (turn 90 degrees)
            float alongZ = toLandX;
            float alongLen = Mathf.Sqrt(alongX * alongX + alongZ * alongZ);
            if (alongLen < 0.001f) { alongX = 0f; alongZ = 1f; }
            else { alongX /= alongLen; alongZ /= alongLen; }

            _segmentCount = 0;

            for (int k = 0; k < wanted; k++)
            {
                float offset = (k - (wanted - 1) * 0.5f) * radius;
                float px = epicentre.X + alongX * offset;
                float pz = epicentre.Z + alongZ * offset;

                // ★★ **Check every segment.** (2026-09-02, caught by Codex)
                //    `k != 0` used to be exempt, but once there are two or more segments
                //    <b>k == 0 is the end of the fault, not the epicentre</b>. Reusing the
                //    check that passed at the epicentre for an end puts a disc on land or
                //    at the map edge — precisely what this check exists to prevent.
                //    A segment that does not fit is simply dropped; the tsunami itself is
                //    not abandoned (a fault does taper at its ends).
                if (OpenWaterRadius(terrain, px, pz) < radius) continue;

                Vector3 at = new Vector3(px, 0f, pz);

                WaterSource src = new WaterSource();
                src.m_type = TypeNatural;
                src.m_inputPosition = at;
                src.m_outputPosition = at;
                src.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);
                src.m_inputRate = 0u;
                src.m_outputRate = 0u;
                src.m_water = 0u;
                src.m_pollution = 0u;
                src.m_flow = 0u;

                ushort handle;
                if (!terrain.WaterSimulation.CreateWaterSource(out handle, src) || handle == 0)
                {
                    // ★ Failing to place any at all is a failure. Having placed some,
                    //   carry on with those.
                    break;
                }

                _centres[_segmentCount] = at;
                _sources[_segmentCount] = handle;
                _segmentCount++;
            }

            // ★★ Even if every end was dropped, **the epicentre itself passed the check
            //    above**, so place a single source there and let it stand (degrading to a
            //    point source).
            if (_segmentCount == 0)
            {
                WaterSource only = new WaterSource();
                only.m_type = TypeNatural;
                only.m_inputPosition = _centre;
                only.m_outputPosition = _centre;
                only.m_target = (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);

                ushort onlyHandle;
                if (terrain.WaterSimulation.CreateWaterSource(out onlyHandle, only)
                    && onlyHandle != 0)
                {
                    _centres[0] = _centre;
                    _sources[0] = onlyHandle;
                    _segmentCount = 1;
                }
            }

            if (_segmentCount == 0)
            {
                Detail = "the game would not give us a water source slot";
                LastRefusal = Refusal.NoRoomInGame;
                return false;
            }

            _running = true;

            // ★ Measure our own wave with the same ruler as the vanilla tsunami
            //   (see the SeaWatch class doc).
            SeaWatch.Arm("Disaster+ concentric tsunami from the epicentre", frame);

            Log.Info("tsunami rising at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): the open water around it reaches "
                     + radius.ToString("F0") + " m of the " + RadiusMetres.ToString("F0")
                     + " m the source would like, so it uses a water source of radius "
                     + TsunamiRingShape.RadiusMetresForRate(_rate).ToString("F0")
                     + " m, " + _segmentCount + " of them in a line along the rupture "
                     + "(a megathrust tears open along a fault, it is not a point), and "
                     + "their target sea level is driven with "
                     + "the DLC's own waveform (retreat, crest, retreat) for "
                     + DurationSteps + " water steps = "
                     + (DurationSteps * FramesPerWaterStep / 3600f).ToString("F1")
                     + " real minutes. The raw amplitude for intensity " + intensity
                     + " would be " + (_deltaUnits / 64f).ToString("F0")
                     + " m but it is held to " + (_riseCapUnits / 64f).ToString("F0")
                     + " m up and " + (_drawCapUnits / 64f).ToString("F0")
                     + " m down (the water is " + depth.ToString("F1")
                     + " m deep here). The push covers "
                     + TsunamiRingShape.RadiusMetresForRate(_rate).ToString("F0")
                     + " m and the retreat only "
                     + TsunamiRingShape.RadiusMetresForRate(_drainRate).ToString("F0")
                     + " m - the game's own take-water maths is int32 and would corrupt "
                     + "cell heights if the retreat circle were any bigger. **A taller source does not travel further - what "
                     + "travels is volume - and unlike the old impact wave this MAKES "
                     + "water instead of borrowing it from the hole it digs.**");

            return true;
            }
        }

        /// <summary>
        /// **Sim thread.** Safe to call every tick (it throttles itself).
        /// </summary>
        public static void Tick(uint frame)
        {
            if (!_running) return;
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            // ★ During a save the sources are lifted. Stop the clock too until they are
            //   back — let it run on and the waveform jumps when they return.
            if (_segmentCount == 0) return;

            _ticks += TsunamiRingShape.TicksPerWaterStep;

            // ★★ **DurationTicks is already in ticks.** (2026-08-31, second review)
            //    Multiplying by 64 again here made the cut-off 3,145,728 ticks
            //    = 49,152 water steps = <b>14.5 real hours</b>. The waveform itself
            //    returns to 0 after 768 steps, so <b>it looked finished</b> while the
            //    water source alone lived on, holding 3.8 km of sea pinned exactly at sea
            //    level (flattening the next wave). <b>Every exceptional release path had
            //    been hardened while the normal one was broken.</b>
            if (_ticks >= DurationTicks)
            {
                lock (_gate)
                {
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                }

                Log.Info("tsunami source finished after " + DurationSteps
                         + " water steps and is released. The highest the target got was "
                         + PeakRiseMetres.ToString("F1")
                         + " m above the normal sea. From here the wave is carried by the "
                         + "solver alone.");
                return;
            }

            int offset = TsunamiRingShape.LevelOffsetUnits(
                _ticks, _deltaUnits, DurationTicks);

            // ★ The crest is bound in absolute terms and the drawback by the depth
            //   (see the docs on each constant).
            if (offset > _riseCapUnits) offset = _riseCapUnits;
            if (offset < -_drawCapUnits) offset = -_drawCapUnits;

            OffsetMetres = offset / 64f;
            if (OffsetMetres > PeakRiseMetres) PeakRiseMetres = OffsetMetres;

            int target = Clamp(_seaUnits + offset, 0, TsunamiRingShape.MaxLevelUnits);
            Drive(target);
        }

        /// <summary>
        /// Push and pull the discs towards the current target level. **The only place that
        /// takes the lock.**
        ///
        /// ★★ <b>Always set both the push and the pull.</b> With only one of them there is
        ///   no force to draw off the water that piles up, and the epicentre heaps to two
        ///   or three times the target (measured offline 2026-08-31: a target of +102 m
        ///   measured +330 m). With both, the disc sticks to the target level and behaves
        ///   exactly like the Dirichlet boundary the DLC applies to the rim cells.
        /// </summary>
        private static void Drive(int target)
        {
            lock (_gate)
            {
                if (!_running || _segmentCount == 0) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;

                // ★★ **Read the index once and use that copy from then on.**
                //    Re-read it either side of the lock and, if it became 0 in between,
                //    <c>LockWaterSource(0)</c> throws IndexOutOfRange <b>after taking the
                //    Monitor</b> and never reaches <c>Monitor.Exit</c> — the water thread
                //    stops for good (raised in the 2026-08-31 cross-review).
                // ★★ If even one source in the line stops being ours, fold them all up.
                //    Leaving some still live puts them beyond the reach of every release
                //    path.
                for (int k = 0; k < _segmentCount; k++)
                {
                    if (OwnsSource(sim, _sources[k], _centres[k])) continue;

                    // ★ The slot is no longer ours. **Do not simply drop the index** —
                    //   drop it and no release path can ever reach it again, leaving a
                    //   spring behind.
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                    Detail = "the water source slot was taken by something else";
                    return;
                }

                for (int k = 0; k < _segmentCount; k++)
                {
                ushort handle = _sources[k];
                bool foreign = false;

                // ★ LockWaterSource returns while still holding the Monitor.
                //   UnlockWaterSource is the only way to release it, so it must sit in a
                //   finally.
                WaterSource src = sim.LockWaterSource(handle);

                try
                {
                    // ★★ Check again inside the lock. OwnsSource reads outside it, so
                    //    the slot could have been swapped between then and now.
                    // ★★ **The type alone is not enough.** (2026-08-31, second review)
                    //    <c>OwnsSource</c> outside the lock checks both the type and the
                    //    position, so if the check inside only looks at the type,
                    //    <b>the weaker of the two is the one that stands</b>. Were the slot
                    //    swapped for another TYPE_NATURAL, we would write our target level
                    //    and 3.8 km worth of flow into somebody else's river.
                    if (src.m_type != TypeNatural
                        || src.m_inputPosition != _centres[k]
                        || src.m_outputPosition != _centres[k])
                    {
                        foreign = true;
                    }
                    else
                    {
                        src.m_target = (ushort)target;

                        // ★★ The take and the output use different rates. **They must
                        //    not be made equal** (see the doc on DrainRadiusMetres: the
                        //    game's int32 gets corrupted).
                        src.m_inputRate = (uint)_drainRate;
                        src.m_outputRate = (uint)_rate;
                    }
                }
                finally
                {
                    sim.UnlockWaterSource(handle, src);
                }

                if (foreign)
                {
                    ReleaseLocked();
                    SweepOursLocked();
                    _running = false;
                    Detail = "the water source slot was taken by something else";
                    return;
                }
                }
            }
        }

        /// <summary>
        /// The number of 16 m cells. <c>BlockHeights</c> is indexed as <c>z*(1080+1)+x</c>.
        /// </summary>
        private const int GridCells = 1080;

        /// <summary>
        /// The least depth that counts as "sea" inside the disc (m).
        /// </summary>
        private const float MinSourceDepthMetres = 2f;

        /// <summary>
        /// Below this radius no tsunami is raised at all (m). Claiming a 3.8 km disc in a
        /// body of water narrower than this means nothing.
        /// </summary>
        private const float MinUsefulRadiusMetres = 320f;

        /// <summary>
        /// <b>How far the disc can be opened out from the epicentre without hitting
        /// land</b> (m). 0 means no tsunami can be raised.
        ///
        /// ★★ The output disc <b>puts water on land too</b> (see the ★★ in
        ///   <see cref="Begin"/>). So rather than use <see cref="RadiusMetres"/> as it
        ///   stands, <b>shrink it to however much sea there really is</b>. A narrow bay
        ///   gets a weak tsunami, and that is <b>right</b> — no open-ocean wave rises at
        ///   the head of a bay.
        ///
        /// ★★ **The ray-casting implementation was thrown away.** (2026-08-31, third
        ///   review) With 16 bearings stepped every 160 m, neighbouring rays are 1,508 m
        ///   apart by the time they reach 3,840 m. Any island, headland or breakwater in
        ///   between is <b>invisible</b>, and a sandbar thinner than 160 m is
        ///   <b>stepped straight over</b>. Either one answers "safe" and floods the land.
        ///   Worse, when the very first sample was on land, the floor pushed 0 back up to
        ///   160 m — <b>it returned exactly 160 m in the case where it had just proved
        ///   land was within 160 m.</b>
        ///
        ///   It now <b>sweeps the grid directly</b>. <c>BlockHeights</c> is a raw array and
        ///   takes no lock, so looking at 241×241 every other cell is only some fifteen
        ///   thousand array reads (once, when placing). The only things missed are
        ///   structures under 32 m across.
        ///
        /// ★ <c>DepthAt</c> is not used. That answers "sea" whenever the surface is
        ///   0.125 m above the terrain, so it walks straight through tidal flats and
        ///   ditches. Here <see cref="MinSourceDepthMetres"/> is required instead.
        /// </summary>
        public static float OpenWaterRadius(TerrainManager terrain, float x, float z)
        {
            float ignoreX, ignoreZ;
            return OpenWaterRadius(terrain, x, z, out ignoreX, out ignoreZ);
        }

        /// <summary>
        /// As above, and returns <b>the direction of the nearest land</b> (not normalised)
        /// in <paramref name="toLandX"/> / <paramref name="toLandZ"/>. The fault is laid
        /// perpendicular to it (see the ★★ on <see cref="_sources"/>).
        /// </summary>
        public static float OpenWaterRadius(TerrainManager terrain, float x, float z,
                                            out float toLandX, out float toLandZ)
        {
            toLandX = 1f;
            toLandZ = 0f;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return 0f;
            if (terrain.WaterSimulation == null) return 0f;

            int seaUnits = (int)(terrain.WaterSimulation.m_currentSeaLevel * 64f);
            int minDepthUnits = (int)(MinSourceDepthMetres * 64f);

            // ★★ **Look at the water column itself** (see the ★★ on
            //    <c>TrenchQuakeSlot.NearestSea</c>). Going by ground height alone reads
            //    dry land that happens to sit below sea level — reclaimed land, craters —
            //    as "sea". <c>BeginRead</c> is taken once per disc, no more.
            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
            if (cells == null) return 0f;

            int cx = CellOf(x);
            int cz = CellOf(z);
            int reach = (int)(RadiusMetres / 16f);
            int best = reach * reach;   // kept as a squared distance in cells (avoids a square root)

            for (int dz = -reach; dz <= reach; dz += 2)
            {
                int gz = cz + dz;
                int row = gz * (GridCells + 1);

                for (int dx = -reach; dx <= reach; dx += 2)
                {
                    int square = dx * dx + dz * dz;
                    if (square >= best) continue;      // further than land we already found

                    int gx = cx + dx;

                    bool land;
                    if (gx < 0 || gx > GridCells || gz < 0 || gz > GridCells)
                    {
                        land = true;                  // treat anything off the map as land
                    }
                    else
                    {
                        land = !IsOpenSeaCell(block, cells, row + gx,
                                              seaUnits, minDepthUnits);
                    }

                    if (land)
                    {
                        best = square;
                        toLandX = dx;
                        toLandZ = dz;
                    }
                }
            }

            float metres = Mathf.Sqrt(best) * 16f;

            // ★ Stop one cell short so the disc never covers the land cell we found.
            metres -= 16f;

            // ★ best <= reach^2, so metres is always below RadiusMetres. No cap needed.
            return metres < MinUsefulRadiusMetres ? 0f : metres;
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }
        }

        /// <summary>
        /// <b>Is that cell open sea?</b> One single test, used everywhere.
        ///
        /// <code>column &gt;= minimum depth   AND   seabed &lt;= sea level - minimum depth</code>
        ///
        /// ★★ **Either half on its own gets something wrong.** (2026-08-31, fifth and
        ///   sixth reviews)
        ///
        /// <list type="bullet">
        /// <item>Look at <b>the seabed alone</b> and diked reclaimed land or a crater reads
        ///   as "sea" — even though it sits below sea level and is <b>dry</b>. Put a
        ///   3.8 km source there and, with a take disc of only 160 m,
        ///   <b>water is left that can never be taken back</b> (it corrupts the save).</item>
        /// <item>Look at <b>the column alone</b> and rivers, high lakes and <b>a city
        ///   flooded by the previous tsunami</b> read as "sea". A second tsunami opens its
        ///   disc over them and fills whatever land lies below the target — the same
        ///   failure, entered by the back door.</item>
        /// </list>
        ///
        /// ★ Requiring both means <b>the sea is still the sea during a storm surge</b>
        ///   (the seabed does not move). The old implementation, which rejected rivers by
        ///   their height above sea level, answered <b>"not sea" for the whole ocean</b>
        ///   while a vanilla tsunami was running.
        /// </summary>
        internal static bool IsOpenSeaCell(ushort[] block, WaterSimulation.Cell[] cells,
                                           int at, int seaUnits, int minDepthUnits)
        {
            if (at < 0 || at >= block.Length || at >= cells.Length) return false;

            // ★★ The test itself lives in Core (SeaCell). It was rewritten three times
            //    and mistook a different landform each time, so it was moved somewhere
            //    tests could pin it down.
            return DisasterPlus.Core.Earthquake.SeaCell.IsOpenSea(
                block[at], cells[at].m_height, seaUnits, minDepthUnits);
        }

        /// <summary>
        /// World coordinate to a 16 m cell (the same formula as <c>TsunamiWave.CellOf</c>).
        /// </summary>
        private static int CellOf(float world)
        {
            int c = (int)((world + MapHalfExtent) / 16f + 0.5f);
            return c < 0 ? 0 : (c > GridCells ? GridCells : c);
        }

        /// <summary>
        /// Is that slot <b>still ours</b>? <c>CreateWaterSource</c> reuses slots with
        /// <c>m_type == 0</c>, so the index alone is not enough (class doc, §4).
        /// </summary>
        private static bool OwnsSource(WaterSimulation sim, ushort handle, Vector3 centre)
        {
            if (handle == 0) return false;

            FastList<WaterSource> list = sim.m_waterSources;
            if (list == null || list.m_buffer == null) return false;

            int at = handle - 1;
            if (at < 0 || at >= list.m_size) return false;

            WaterSource s = list.m_buffer[at];
            if (s.m_type != TypeNatural) return false;

            // The position is the exact value we wrote, so it can be matched bit for bit.
            return s.m_outputPosition == centre && s.m_inputPosition == centre;
        }

        /// <summary>
        /// Release the water sources we placed. **Idempotent. Never throws.**
        /// This is the last line of defence — miss it and a source is left in the save.
        /// </summary>
        private static void ReleaseLocked()
        {
            // ★★ **Let go of the indices first.** That way, whatever happens below, no
            //    other path can mistake them for still-held and touch the same slots.
            ushort[] handles = new ushort[MaxSegments];
            Vector3[] centres = new Vector3[MaxSegments];
            int count = _segmentCount;

            for (int k = 0; k < count; k++)
            {
                handles[k] = _sources[k];
                centres[k] = _centres[k];
                _sources[k] = 0;
            }

            _segmentCount = 0;
            if (count == 0) return;

            try
            {
                if (!Singleton<TerrainManager>.exists) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;

                for (int k = 0; k < count; k++)
                {
                    ushort handle = handles[k];
                    if (handle == 0) continue;
                    if (!OwnsSource(sim, handle, centres[k])) continue;

                    // ★ Zero the rates before releasing. ReleaseWaterSource only sets
                    //   m_type to 0, so this removes any chance of whoever picks the slot
                    //   up next seeing the old rates.
                    WaterSource src = sim.LockWaterSource(handle);
                    try
                    {
                        if (src.m_type != TypeNatural) continue;
                        src.m_inputRate = 0u;
                        src.m_outputRate = 0u;
                    }
                    finally
                    {
                        sim.UnlockWaterSource(handle, src);
                    }

                    sim.ReleaseWaterSource(handle);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water source failed ("
                         + e.GetType().Name + "); it may persist in this save");
            }
        }

        /// <summary>
        /// <b>Delete every water source matching our fingerprint.</b> The caller must hold
        /// <c>_gate</c>.
        ///
        /// ★★ **The only way back if the index is lost.** (2026-08-31, cross-verified)
        ///   <c>CreateWaterSource</c> succeeded but the index was dropped, the slot was
        ///   taken from under us, an exception skipped the path — however it happened, a
        ///   source we placed has a shape nothing else has: <b>a TYPE_NATURAL whose two
        ///   positions both match the epicentre bit for bit</b>. The scan walks an array of
        ///   a few dozen entries, so it costs nothing.
        ///
        /// ★ Does nothing when the epicentre is <c>Vector3.zero</c>: either nothing has
        ///   been placed yet, or we would risk sweeping up somebody else's river that
        ///   happens to sit at the centre of the map.
        /// </summary>
        /// <summary>
        /// Is that position one of the discs we placed (checking every segment of the line)?
        /// </summary>
        private static bool IsOneOfOurs(Vector3 at)
        {
            if (at == _centre) return true;

            for (int k = 0; k < _centres.Length; k++)
            {
                if (_centres[k] == Vector3.zero) continue;
                if (_centres[k] == at) return true;
            }

            return false;
        }

        private static void SweepOursLocked()
        {
            if (_centre == Vector3.zero) return;

            try
            {
                if (!Singleton<TerrainManager>.exists) return;

                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain == null || terrain.WaterSimulation == null) return;

                WaterSimulation sim = terrain.WaterSimulation;
                FastList<WaterSource> list = sim.m_waterSources;
                if (list == null || list.m_buffer == null) return;

                // ★★ **Scan and release inside one lock.** (2026-08-31, second review)
                //    Read the array outside the lock and then call
                //    <c>ReleaseWaterSource</c>, and a slot can move in between and leave
                //    the index stale. Hand the game a stale index and it throws
                //    <b>after taking the Monitor</b> and never reaches
                //    <c>Monitor.Exit</c> — the water thread stops for good.
                //
                // ★ <c>Monitor</c> is re-entrant for the same thread, so it is fine to
                //   call <c>ReleaseWaterSource</c> while holding it here. We take the same
                //   object the game itself takes (<c>m_waterSources</c>).
                // ★ Spinning without yielding starves the water thread when this is
                //   entered from the main thread (third review).
                while (!System.Threading.Monitor.TryEnter(list, 0))
                {
                    System.Threading.Thread.Sleep(0);
                }

                try
                {
                    int size = list.m_size;
                    if (size > list.m_buffer.Length) size = list.m_buffer.Length;

                    for (int i = size - 1; i >= 0; i--)
                    {
                        WaterSource s = list.m_buffer[i];
                        if (s.m_type != TypeNatural) continue;
                        if (s.m_outputPosition != s.m_inputPosition) continue;
                        if (!IsOneOfOurs(s.m_outputPosition)) continue;

                        sim.ReleaseWaterSource((ushort)(i + 1));
                        Log.Info("tsunami: swept a stray water source at the epicentre "
                                 + "(slot " + (i + 1) + "). It would have kept making "
                                 + "water in this city forever.");
                    }
                }
                finally
                {
                    System.Threading.Monitor.Exit(list);
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: sweeping stray water sources failed ("
                         + e.GetType().Name + ")");
            }
        }

        /// <summary>
        /// **Call immediately before a save.** Lifts the water sources and returns whether
        /// they should be put back.
        ///
        /// ★★ A <c>WaterSource</c> is baked into the save. A mod's <c>OnSaveData</c> runs
        ///   before vanilla writes its arrays, so lifting them here keeps them out.
        ///   <b>Do not put them straight back</b> — defer with <c>AddAction</c> (the same
        ///   reason as <c>DisasterPlusSerialization.RestoreFloodedRiversForSave</c>).
        /// </summary>
        public static bool SuspendForSave()
        {
            lock (_gate)
            {
                if (!_running) return false;

                // ★★ **Return true even when they are already lifted.**
                //    (2026-08-31, second review) Return false and the caller reads it as
                //    "nothing to put back" and <b>forgets to restore</b> in the window
                //    where a second save has begun. <c>ReapplyAfterSave</c> does nothing
                //    when <c>_source != 0</c>, so an extra booking is harmless.
                if (_segmentCount == 0) return true;

                // ★ Remember how many segments there were, for the restore
                //   (ReleaseLocked sets the count to 0).
                _suspendedCount = _segmentCount;

                ReleaseLocked();
                SweepOursLocked();
                return true;
            }
        }

        /// <summary>
        /// Call through <c>AddAction</c> once the save has finished. **Sim thread.**
        /// </summary>
        public static void ReapplyAfterSave()
        {
            lock (_gate)
            {
                if (!_running || _segmentCount != 0) return;

                try
                {
                    // ★★ <c>.instance</c> runs FindObjectOfType and new GameObject
                    //    when sInstance is null. This deferred work can arrive after the
                    //    city has been left, so **always check exists first** (the same
                    //    reason as the comment in DisasterPlusSerialization).
                    if (!Singleton<TerrainManager>.exists) { _running = false; return; }

                    TerrainManager terrain = Singleton<TerrainManager>.instance;
                    if (terrain == null || terrain.WaterSimulation == null)
                    {
                        _running = false;
                        return;
                    }

                    // ★★ **Put back every segment of the line.** (2026-09-02) Restore
                    //    only one and the remaining segments' centres stay in
                    //    <c>_centres</c> with no water source behind them, leaving the
                    //    sweep's fingerprints pointing at nothing.
                    int back = 0;

                    for (int k = 0; k < _suspendedCount; k++)
                    {
                        WaterSource src = new WaterSource();
                        src.m_type = TypeNatural;
                        src.m_inputPosition = _centres[k];
                        src.m_outputPosition = _centres[k];
                        src.m_target =
                            (ushort)Clamp(_seaUnits, 0, TsunamiRingShape.MaxLevelUnits);

                        ushort handle;
                        if (!terrain.WaterSimulation.CreateWaterSource(out handle, src)
                            || handle == 0)
                        {
                            break;
                        }

                        _sources[back] = handle;
                        back++;
                    }

                    _segmentCount = back;

                    if (back == 0)
                    {
                        _running = false;
                        Log.Warn("tsunami: could not put the water source back after saving; "
                                 + "the wave stops here");
                    }
                }
                catch (System.Exception e)
                {
                    _running = false;

                    // ★★ If creation succeeded and then this threw, we never received
                    //    the index, so <b>only the sweep can recover it</b> (see the doc
                    //    on SweepOursLocked).
                    SweepOursLocked();
                    Log.Error("tsunami: putting the water source back after saving failed", e);
                }
            }
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
