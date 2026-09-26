using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// One lava flow. **An immutable value type**: every advance builds a new one
    /// (this mod's immutability discipline. Never mutate it).
    /// </summary>
    public struct LavaFlow
    {
        /// <summary>Whether it is still moving. If false, <see cref="StopReason"/> holds the reason.</summary>
        public readonly bool Alive;

        /// <summary>World coordinates of the current head (<c>X</c> / <c>Z</c>).</summary>
        public readonly Vec2 Head;

        /// <summary>Distance since leaving the crater (m). Used to compute the spread.</summary>
        public readonly float TravelledMetres;

        /// <summary>Steps taken so far. It always stops at <c>LavaPath.MaxSteps</c>.</summary>
        public readonly int Steps;

        /// <summary>Why it stopped (the <c>Stop*</c> constants on <see cref="VolcanoLava"/>).</summary>
        public readonly int StopReason;

        public LavaFlow(bool alive, Vec2 head, float travelledMetres, int steps, int stopReason)
        {
            Alive = alive;
            Head = head;
            TravelledMetres = travelledMetres;
            Steps = steps;
            StopReason = stopReason;
        }
    }

    /// <summary>
    /// The advance and ignition of lava leaving the crater. **Sim thread only.**
    ///
    /// ── the sign of the gradient was settled in this task by reading IL (not guessed) ───────
    ///
    /// Plan §8.1 named "we have not read whether <c>slopeX</c> / <c>slopeZ</c> points uphill or
    /// downhill". **It has now been read.**
    ///
    /// <code>
    /// TerrainManager.SampleDetailHeight(float x, float z, out slopeX, out slopeZ)
    ///   h00 = GetDetailHeight(x0, z0)   h10 = GetDetailHeight(x0+1, z0)
    ///   h01 = GetDetailHeight(x0, z0+1) h11 = GetDetailHeight(x0+1, z0+1)
    ///   IL_0071  *slopeX = (h10 + h11 - h00 - h01) * 0.5      // = mean (h(x+1) - h(x))
    ///   IL_0084  *slopeZ = (h01 + h11 - h00 - h10) * 0.5      // = mean (h(z+1) - h(z))
    ///
    /// TerrainManager.SampleDetailHeight(Vector3, out slopeX, out slopeZ)
    ///   IL_0033  return value *= 0.015625  // = 1/64. raw -> metres
    ///   IL_003B  *slopeX *= 0.00390625     // = (1/64) / 4 m. **a dimensionless gradient (m/m)**
    ///   IL_0045  *slopeZ *= 0.00390625
    /// </code>
    ///
    /// ★★ <b><c>slopeX</c> / <c>slopeZ</c> are "how much the height increases as you move
    /// towards +x / +z" — that is, the uphill gradient. So the downhill direction is the sign
    /// flipped: <c>(-slopeX, -slopeZ)</c>.</b> The unit is dimensionless (m/m), so
    /// <c>LavaPath.MinSlope = 0.002</c> means exactly "a 0.2 % slope".
    ///
    /// **Even so, do not stop observing it at runtime** (plan §8.1). For the first
    /// <see cref="ObservationSteps"/> steps we watch whether the head's elevation has gone up,
    /// and if it has, that flow is stopped, <see cref="SlopeSignVerified"/> goes false and a
    /// <c>Log.Warn</c> is emitted **exactly once**.
    /// **Do not try to "fix" it by flipping the sign at runtime** — it would feel fixed here and
    /// break the other way in another environment. Stopping and saying so is the right thing.
    ///
    /// ── the per-tick work (bounded explicitly) ─────────────────────────────────────────────
    ///
    /// <code>
    /// number of flows        &lt;= MaxFlows = 8            (the setting's ceiling. 0 disables entirely)
    /// advance interval       IntervalFrames = in-game time worth of 8 sim frames
    /// steps per advance      &lt;= MaxStepsPerTickPerFlow = 2  (per flow)
    ///  => per advance        &lt;= 16 steps
    /// per step               one SampleDetailHeight (4 reads + 3 Lerps, §B-6)
    ///                        one HasWater (4 cells inside BeginRead/EndRead, §A-4)
    ///                        one BurnGround (radius &lt;= 60 m, so at most 5x5 of the 512^2 cells)
    ///                        building grid &lt;= MaxBuildingCellsPerStep = 25 cells
    ///                        tree grid     &lt;= MaxTreeCellsPerStep = 49 cells
    /// allocation             on advancing ticks only: Vec2[&lt;= 8*128] and int[&lt;= 8] (copying the trails)
    /// </code>
    ///
    /// **Do not build the period from `frameIndex % N`** (one in-game minute ≒ 45.51 frames;
    /// firestorm appendix A-4). The interval is decided by accumulating elapsed in-game time.
    ///
    /// ── the three ignition paths, and only one of them branches on the DLC (§B-7) ──────────
    ///
    /// <code>
    /// ground  DisasterHelpers.BurnGround(Vector2, radius, intensity)   no DLC needed
    /// building BuildingAI.BurnBuilding(id, ref b, group, testOnly)     no DLC needed
    /// trees   TreeManager.BurnTree(idx, group, intensity)              ★ ND required
    /// roads   ABSENT. Roads do not burn (there is no API equivalent to BurnSegment)
    /// </code>
    ///
    /// - <c>BurnGround</c>'s <c>intensity</c> is **a normalised 0.0–1.0 value** (it is multiplied
    ///   by 255 at IL_001E in §B-7b). In vanilla, meteors use 1.0 and sinkholes, earthquakes and
    ///   tornadoes use 0.7. **⑤ is lava, so it uses 1.0.** The value is monotonically
    ///   non-decreasing and never goes down
    /// - <c>BurnTree</c>'s <c>intensity</c> is **truncated (not clamped)** by <c>conv.u1</c>, so
    ///   the caller keeps it within <c>[128, 255]</c>
    ///   (256 becomes 0 and 300 becomes 44. §B-7c)
    /// - Without ND, **do not burn trees.** Do not substitute removing them with
    ///   <c>TreeManager.ReleaseTree</c> — "does not burn" and "disappears" are two different
    ///   lies, and explaining that they do not burn is the right thing
    ///   (<c>Strings.VolcanoTreesNeedDlc</c>)
    /// - A tree that has burnt once never burns again (<c>m_flags &amp; 64 FireDamage</c>).
    ///   Do not count those misses as anomalies
    ///
    /// > ★★ **Trap 5. Do not write extra to <c>Building</c>'s fire-intensity field after
    /// > <c>BurnBuilding</c>.** The fire strength is decided per building by
    /// > <c>GetFireParameters</c> (§B-7a). However much you want it fiercer, do not write it —
    /// > only the <c>CommonBuildingAI</c> family consumes it, and writing it on any other AI gives
    /// > a permanent ghost fire that nobody clears; because it goes into the vanilla building
    /// > array it is baked into the save and survives removing the mod. **This project has
    /// > shipped that once already.**
    /// >
    /// > **Review grep (actually run, with 0 hits confirmed)**:
    /// > <code>
    /// > grep -rn "m_fireIntensity" src/DisasterPlus/Game/Volcano --include=*.cs \
    /// >   | grep -v '///' | grep -v '^[^:]*:[0-9]*: *//'
    /// > # -> 0 hits (excluding the mentions inside doc comments. A plain grep hits the doc's own
    /// > #    explanation of "do not write it", so it will not come out as 0)
    /// > </code>
    ///
    /// > **Do not go through <c>DisasterHelpers.DestroyBuildings</c> / <c>DestroyNetSegments</c>**
    /// > (§E-14; going through them collides with Natural Disasters Renewal's wholesale Prefix
    /// > replacement). The only thing ⑤ calls is <c>BuildingAI.BurnBuilding</c>.
    ///
    /// ── why the building and tree sweeps are row-major (and how that relates to ②'s review point I1) ──
    ///
    /// ②, ④ and T5 sweep outwards from the centre with <c>OutwardCellOrder</c>. That is for
    /// **large rectangles that get cut short by a limit**, where the requirement was to be able to
    /// state, in terms of a radius, "how far it definitely got" when it was cut short.
    /// Here the rectangle comes from <c>SpreadRadiusFor</c>, at most
    /// <c>SpreadHardMaxMetres</c> = 96 m, so it is **at most 4×4 cells on the building grid and
    /// at most 7×7 on the tree grid** (raised from 60 m on 2026-08-22, when the width started
    /// varying with the scale). There is structurally no room for the limits (25 / 64) to truncate
    /// anything, so row-major is enough.
    /// **②'s review point was not broken out of ignorance.**
    ///
    /// ── the lava does not change the terrain ───────────────────────────────────────────────
    ///
    /// The only thing that writes <c>RawHeights</c> is T6 (<c>VolcanoUplift</c>).
    ///
    /// **Review grep (actually run, with the counts made to match. Excluding the doc mentions)**:
    /// <code>
    /// grep -rn "RawHeights\|TerrainModify" src/DisasterPlus/Game/Volcano/VolcanoLava*.cs \
    ///   | grep -v '///'
    /// # -> 0 hits (the lava does not change the terrain)
    ///
    /// grep -rn "Physics.Raycast" src/DisasterPlus/Game/Volcano --include=*.cs | grep -v '///'
    /// # -> 0 hits (the terrain has no collider, so it always misses. §B-6)
    /// </code>
    /// </summary>
    public static partial class VolcanoLava
    {
        /// <summary>Not stopped yet.</summary>
        public const int StopNone = 0;

        /// <summary>The gradient is below <c>LavaPath.MinSlope</c> (flat or a hollow) = it pooled.</summary>
        public const int StopFlat = 1;

        /// <summary>It touched water (design doc §4.5).</summary>
        public const int StopWater = 2;

        /// <summary>It hit the step limit.</summary>
        public const int StopSteps = 3;

        /// <summary>It went off the map.</summary>
        public const int StopOffMap = 4;

        /// <summary>★ The elevation rose = the sign of the gradient is different in this environment (§8.1).</summary>
        public const int StopUphill = 5;

        /// <summary>The terrain could not be read.</summary>
        public const int StopNoTerrain = 6;

        /// <summary>Maximum number of flows. It is also the setting's ceiling.</summary>
        public const int MaxFlows = 8;

        /// <summary>Maximum steps one flow takes in a single advance.</summary>
        private const int MaxStepsPerTickPerFlow = 2;

        /// <summary>The advance interval (in sim frames. **Decided by accumulating time**).</summary>
        private const int IntervalFrames = 8;

        /// <summary>Maximum trail points per flow (fold by decimating once it is exceeded).</summary>
        private const int MaxTrailPoints = 128;

        /// <summary>★ How many steps the sign of the gradient is observed for at runtime (class doc).</summary>
        private const int ObservationSteps = 8;

        /// <summary>The height difference that counts as "it went up" in the observation (m). A little over three times the terrain quantum of 1/64 m.</summary>
        private const float UphillToleranceMetres = 0.05f;

        /// <summary>Half the width of the map (m). Stop just inside 17280 / 2 = 8640.</summary>
        private const float MapHalfExtentMetres = 8600f;

        /// <summary>The strength for <c>BurnGround</c>. **A normalised 0.0–1.0 value** (§B-7b).</summary>
        private const float GroundBurnIntensity = 1f;

        /// <summary>In-game time (minutes) from everything stopping to the lava having cooled.</summary>
        private const float CoolMinutes = 15f;

        private static bool _started;
        private static bool _finished;
        private static Vec3 _centre;
        private static float _minutesSinceAdvance;
        private static float _cooledMinutes;

        private static LavaFlow[] _flows = new LavaFlow[MaxFlows];
        private static Vec2[][] _trails;
        private static int[] _trailCounts = new int[MaxFlows];
        private static int[] _trailStride = new int[MaxFlows];
        private static int[] _sinceCommit = new int[MaxFlows];
        private static float[] _lastHeight = new float[MaxFlows];

        private static int _flowCount;
        private static int _aliveCount;
        private static float _longestMetres;

        private static int _buildingsIgnited;
        private static int _buildingsRefused;
        private static int _treesIgnited;
        private static bool _treesAvailable;
        private static bool _outsidePurchasedArea;

        /// <summary>
        /// How much the terrain is still rising from the uplift (m per uplift tick).
        /// **It is a tolerance added to the observation of the downhill gradient's sign**, and it
        /// does not affect the gradient itself.
        /// It is 0 once the uplift has finished, so the observation stays as strict as ever.
        /// </summary>
        private static float _terrainRiseMetres;

        /// <summary>
        /// How many steps one flow may take. **Decided by the scale**
        /// (<c>LavaVolume.StepBudget</c>; always at most <c>LavaPath.MaxSteps</c>).
        /// <see cref="Reset"/> puts it back to <c>LavaPath.MaxSteps</c> — put it back to 0 and if
        /// anything ever advanced without going through <c>Start</c>,
        /// **the flow would not move a single step**.
        /// </summary>
        private static int _stepBudget = LavaPath.MaxSteps;

        /// <summary>
        /// The width factor of a flow. **Decided by the scale** (<c>LavaVolume.WidthFactor</c>).
        /// <see cref="Reset"/> puts it back to 1 — put it back to 0 and if anything ever advanced
        /// without going through <c>Start</c>, you would get **lava of zero width**.
        /// </summary>
        private static float _widthFactor = 1f;

        private static bool _slopeSignVerified;
        private static bool _slopeSignWarned;
        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>The trail points (**an immutable array** concatenating all the flows). Main reads this.</summary>
        private static Vec2[] _trailPoints = new Vec2[0];

        /// <summary>The point count of each flow (**an immutable array**). Used in step with <see cref="_trailPoints"/>.</summary>
        private static int[] _trailPointCounts = new int[0];

        /// <summary>Number of flows emitted from the crater (the setting's value; 0 disables it entirely).</summary>
        public static int FlowCount { get { return _flowCount; } }

        /// <summary>Number of flows still moving.</summary>
        public static int AliveCount { get { return _aliveCount; } }

        /// <summary>The longest distance any flow travelled (m).</summary>
        public static float LongestMetres { get { return _longestMetres; } }

        /// <summary>Number of buildings set alight so far.</summary>
        public static int BuildingsIgnited { get { return _buildingsIgnited; } }

        /// <summary>
        /// The number of **calls** vanilla refused to ignite. **A non-zero value is not an
        /// anomaly** — <c>CommonBuildingAI.BurnBuilding</c> refuses flooded buildings and rubble
        /// (§B-7a).
        ///
        /// ★★ <b>This is not "the number of buildings refused"</b> (whole-project review M17).
        /// The lava only advances 12 m per step while the ignition radius is up to 60 m, so
        /// **the same building is hit about five times by one flow, and up to 40 times across
        /// eight.** From the second hit on it is already burning and gets refused —
        /// so this number is <b>mostly "re-ignitions of buildings that are already burning"</b>
        /// and it is normal for it to be far larger than <see cref="BuildingsIgnited"/>.
        /// **To avoid counting duplicates we would have to remember which buildings we set alight,
        /// and ⑤ does not keep an array for that** (going to read <c>m_fireIntensity</c> would
        /// break trap 5's grep, so we do not). Instead we state the meaning of the number
        /// accurately.
        /// </summary>
        public static int BuildingsRefused { get { return _buildingsRefused; } }

        /// <summary>Number of trees set alight so far. Always 0 without ND.</summary>
        public static int TreesIgnited { get { return _treesIgnited; } }

        /// <summary>Whether trees can be set alight in this environment (i.e. whether the ND DLC is owned. §B-7c).</summary>
        public static bool TreesAvailable { get { return _treesAvailable; } }

        /// <summary>
        /// Whether the lava left the tiles you have purchased. **Not a bug** —
        /// <c>GetDetailHeight</c>'s sampling drops from 4 m to 16 m interpolation there (§B-6).
        /// </summary>
        public static bool OutsidePurchasedArea { get { return _outsidePurchasedArea; } }

        /// <summary>
        /// Whether we were able to **confirm by observation at runtime** that the sign of the
        /// gradient really is "downhill" in this environment (§8.1).
        /// It goes true as soon as any one flow gets through its observation window unharmed.
        /// </summary>
        public static bool SlopeSignVerified { get { return _slopeSignVerified; } }

        /// <summary>Whether all the flows have stopped (i.e. whether cooling may begin).</summary>
        public static bool AllStopped { get { return _started && _aliveCount == 0; } }

        /// <summary>Whether it has cooled out (i.e. whether the next phase may begin).</summary>
        public static bool Finished { get { return _finished; } }

        /// <summary>
        /// How cool it is, <c>[0,1]</c>. 1 is "still hot", 0 is "cooled out".
        /// **T9's rendering fades the colour with this** (so stopped lava does not glow for ever).
        /// </summary>
        public static float CoolUnit
        {
            get
            {
                if (!AllStopped) return 1f;

                // CoolMinutes is a constant greater than 0 (set it to 0 and the cooling stage
                // disappears, so the lava vanishes the instant it stops). Because it is a
                // constant, testing for 0 before the division makes the compiler warn about
                // unreachable code.
                float left = 1f - _cooledMinutes / CoolMinutes;
                if (left < 0f) return 0f;
                if (left > 1f) return 1f;
                return left;
            }
        }

        /// <summary>The most recent failure (**English, for diagnostics**). null if there is none.</summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// The trail points (one array concatenating all the flows). **Not one byte is rewritten
        /// after publishing** — every advance builds a new array and swaps it in. That is why it
        /// is safe for the main thread to hold the reference, and why there is no need for a
        /// separate copy entry point.
        ///
        /// > The plan listed an entry point <c>CopyHeads(Vec2[] into, out int count)</c>, but
        /// > **it was not built.** The plan's intent was "do not let main read an array sim is
        /// > writing into", and that is already satisfied by "swap it, never rewrite it".
        /// > Leaving an unused copy API around would be worse, by exactly the amount of thought
        /// > it would force on the next person about which of the two is the right one to use.
        /// </summary>
        public static Vec2[] TrailPoints { get { return _trailPoints; } }

        /// <summary>The point count of each flow (**an immutable array**). The total is the length of <see cref="TrailPoints"/>.</summary>
        public static int[] TrailPointCounts { get { return _trailPointCounts; } }

        /// <summary>
        /// Call on level unload, on a new volcano, and on cancellation. **Discards all state.**
        /// <c>_errorLogged</c> / <c>_slopeSignWarned</c> are not reset (they are facts about the
        /// build of the game, not per-city state).
        /// </summary>
        public static void Reset()
        {
            _started = false;
            _finished = false;
            _centre = new Vec3(0f, 0f, 0f);
            _minutesSinceAdvance = 0f;
            _cooledMinutes = 0f;

            for (int i = 0; i < MaxFlows; i++)
            {
                _flows[i] = new LavaFlow(false, new Vec2(0f, 0f), 0f, 0, StopNone);
                _trailCounts[i] = 0;
                _trailStride[i] = 1;
                _sinceCommit[i] = 0;
                _lastHeight[i] = 0f;
            }

            // ★ Give back the trails themselves too (8 flows × 128 points = 8 KB). Carry them
            //   across cities and the previous city's lava gets drawn in the next one.
            _trails = null;

            _flowCount = 0;
            _aliveCount = 0;
            _longestMetres = 0f;
            _buildingsIgnited = 0;
            _buildingsRefused = 0;
            _treesIgnited = 0;
            _treesAvailable = false;
            _outsidePurchasedArea = false;
            _slopeSignVerified = false;
            _terrainRiseMetres = 0f;
            _stepBudget = LavaPath.MaxSteps;
            _widthFactor = 1f;
            _lastFailure = null;

            _trailPoints = new Vec2[0];
            _trailPointCounts = new int[0];
        }

        /// <summary>
        /// **Sim thread.** Call it only from <see cref="VolcanoState"/>'s phase branches.
        /// Do not wedge the phase if an exception is thrown (wedge it and the player can never
        /// place a second volcano).
        /// </summary>
        /// <param name="terrainRiseMetresPerFrame">
        /// How much the terrain rises from the uplift in **one sim frame** (m).
        /// **Non-zero only when a flow was emitted partway through the uplift.**
        /// When the elevation rises ahead of the lava in that case it is not "the lava climbed"
        /// but "the mountain grew", so that amount is added to the observation's tolerance
        /// (pass <c>VolcanoUplift.RiseMetresPerFrame</c> straight through).
        ///
        /// ★ **Do not pass a "per tick" value.** ⑤'s stages have different intervals
        ///   (uplift 4 frames, lava <see cref="IntervalFrames"/> = 8 frames), so one uplift tick's
        ///   worth falls short of the rise over one lava step and the flow stops on a
        ///   **false detection of "the lava climbed"**.
        ///   The lava's own interval is multiplied back in here.
        /// </param>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes,
                                float terrainRiseMetresPerFrame)
        {
            try
            {
                Step(footprint, deltaMinutes, terrainRiseMetresPerFrame);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the lava tick threw " + e.GetType().Name;
                _aliveCount = 0;
                _finished = true;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano lava failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcLava",
                             "volcano lava failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes,
                                 float terrainRiseMetresPerFrame)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre))
            {
                Start(footprint);
            }

            // ★ Start goes through Reset, so **set the tolerance after Start**
            //   (set it before and it goes back to 0 on the tick it started).
            // ★ One lava step is IntervalFrames frames long, so convert to the rise over that.
            _terrainRiseMetres = float.IsNaN(terrainRiseMetresPerFrame)
                                 || terrainRiseMetresPerFrame < 0f
                ? 0f : terrainRiseMetresPerFrame * IntervalFrames;

            if (_finished) return;

            if (_aliveCount == 0)
            {
                // All stopped. All that is left is counting the time until it has cooled.
                if (deltaMinutes > 0f) _cooledMinutes += deltaMinutes;
                if (_cooledMinutes >= CoolMinutes) _finished = true;
                return;
            }

            // ★ The interval is decided by accumulating elapsed in-game time.
            //   **Never frameIndex % N.**
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceAdvance += deltaMinutes;
            if (interval > 0f && _minutesSinceAdvance > interval) _minutesSinceAdvance = interval;

            if (framesPerMinute <= 0f) return;
            if (_minutesSinceAdvance < interval) return;

            _minutesSinceAdvance = 0f;
            Advance();
        }

        /// <summary>
        /// Emit at most <see cref="MaxFlows"/> flows from the crater rim.
        /// **A count of 0 means "disabled entirely"**: no ignition, no rendering, nothing.
        /// </summary>
        private static void Start(VolcanoFootprint footprint)
        {
            Reset();
            _started = true;
            _centre = footprint.Centre;
            _treesAvailable = ReadTreesAvailable();

            // ★★ **The number of flows varies with the scale of the eruption** (2026-08-22, the
            //    owner's request "make the amount of magma flowing out vary with the scale of the
            //    eruption too").
            //
            //    What is used for the scale is **the mountain's radius** (the class doc of
            //    <c>LavaVolume</c>). Not the eruption strength (<c>EruptionIntensityUnit</c>) —
            //    that fluctuates during the eruption, and deciding the count from it would mean
            //    **deleting flows that are already running**.
            //    The radius is decided the moment it is placed and never moves again.
            int flows = LavaVolume.FlowCount(ModSettings.VolcanoLavaFlows.value, MaxFlows,
                                             footprint.RadiusMetres);

            // ★ The length limit varies with the scale too. **It never exceeds
            //   <c>LavaPath.MaxSteps</c>, which decides the array length**
            //   (<c>LavaVolume.StepBudget</c> guarantees that).
            _stepBudget = LavaVolume.StepBudget(LavaPath.MaxSteps, footprint.RadiusMetres);

            // ★ The width is decided by the scale too. **The drawing side calls the same pure
            //   function itself**, so what is held here is only for the ignition (the sim side).
            _widthFactor = LavaVolume.WidthFactor(footprint.RadiusMetres);

            if (flows == 0)
            {
                // Turned off in the settings. **It finishes without even waiting to cool.**
                _flowCount = 0;
                _aliveCount = 0;
                _finished = true;
                return;
            }

            _trails = new Vec2[MaxFlows][];

            // ★★ Emit from **outside the crater rim**. Directly on the rim is a ridge line, and
            //    reading the gradient there can make the downhill direction point into the
            //    crater, so the lava falls into the hollow and pools
            //    (the doc of LavaPath.VentRimClearanceFactor).
            float ventRadius = LavaPath.VentRadiusMetres(
                VolcanoShape.CraterRadiusOf(footprint.RadiusMetres));

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(footprint.Centre.Z)));

            for (int i = 0; i < flows; i++)
            {
                Vec2 dir = LavaPath.InitialDirection(seed, i, flows);
                var head = new Vec2(footprint.Centre.X + dir.X * ventRadius,
                                    footprint.Centre.Z + dir.Z * ventRadius);

                _flows[i] = new LavaFlow(true, head, 0f, 0, StopNone);
                _trails[i] = new Vec2[MaxTrailPoints];
                _trails[i][0] = head;
                _trailCounts[i] = 1;
                _trailStride[i] = 1;
                _sinceCommit[i] = 0;

                float h, sx, sz;
                _lastHeight[i] = SampleSlope(head, out h, out sx, out sz) ? h : 0f;
            }

            _flowCount = flows;
            _aliveCount = flows;
            RebuildTrailSnapshot();
        }

        /// <summary>One advance. **The step count is capped at flows × <see cref="MaxStepsPerTickPerFlow"/>.**</summary>
        private static void Advance()
        {
            int alive = 0;

            for (int i = 0; i < _flowCount; i++)
            {
                LavaFlow f = _flows[i];
                if (!f.Alive) continue;

                for (int s = 0; s < MaxStepsPerTickPerFlow && f.Alive; s++)
                {
                    f = StepFlow(i, f);
                }

                _flows[i] = f;
                if (f.Alive) alive++;
                if (f.TravelledMetres > _longestMetres) _longestMetres = f.TravelledMetres;
            }

            _aliveCount = alive;
            RebuildTrailSnapshot();
        }

        /// <summary>
        /// One step. There are six stopping conditions, all recorded in <c>StopReason</c> and
        /// reported in the diagnostics.
        ///
        /// ★ <b>The downhill direction is <c>(-slopeX, -slopeZ)</c></b> (the IL measurements in
        ///   the class doc). <c>LavaPath</c> does not know the sign, so it is corrected here
        ///   before being passed on.
        /// </summary>
        private static LavaFlow StepFlow(int index, LavaFlow f)
        {
            float height, slopeX, slopeZ;
            if (!SampleSlope(f.Head, out height, out slopeX, out slopeZ))
            {
                return Stop(f, StopNoTerrain);
            }

            // ★★ The runtime observation (§8.1). **Do not try to "fix" it by flipping the sign.**
            //    If the elevation rises within the first ObservationSteps steps, that flow is
            //    stopped and we say so. The sign is settled in IL, so this firing means a game
            //    update has changed the behaviour.
            if (f.Steps > 0 && f.Steps <= ObservationSteps
                && height > _lastHeight[index] + UphillToleranceMetres + _terrainRiseMetres)
            {
                NoteUphill();
                return Stop(f, StopUphill);
            }

            if (f.Steps >= ObservationSteps) _slopeSignVerified = true;
            _lastHeight[index] = height;

            Vec2 next;
            if (!LavaPath.NextPosition(f.Head, new Vec2(-slopeX, -slopeZ),
                                       LavaPath.StepMetres, out next))
            {
                // Flat, a hollow, or NaN. **It pools rather than passing through**
                // (the class doc of LavaPath).
                return Stop(f, StopFlat);
            }

            if (OffMap(next)) return Stop(f, StopOffMap);

            // ★ Water is sim-thread only (HasWater takes WaterSimulation.BeginRead/EndRead.
            //   §A-4 / handled the same way as ②'s TsunamiChain.IsUnderWater).
            if (HasWater(next)) return Stop(f, StopWater);

            int steps = f.Steps + 1;
            float travelled = f.TravelledMetres + LavaPath.StepMetres;

            AppendTrail(index, next);
            NoteArea(next);
            Ignite(next, travelled);

            var moved = new LavaFlow(true, next, travelled, steps, StopNone);
            if (steps >= _stepBudget) return Stop(moved, StopSteps);
            return moved;
        }

        private static LavaFlow Stop(LavaFlow f, int reason)
        {
            return new LavaFlow(false, f.Head, f.TravelledMetres, f.Steps, reason);
        }

        /// <summary>
        /// The sign of the gradient is different in this environment. **<c>Log.Warn</c> exactly
        /// once** (this is inside the per-tick path).
        /// </summary>
        private static void NoteUphill()
        {
            _slopeSignVerified = false;
            _lastFailure = "a lava flow gained altitude within the first "
                           + ObservationSteps + " steps; the downhill sign does not hold in "
                           + "this build of the game, so the flow was stopped";

            if (_slopeSignWarned) return;
            _slopeSignWarned = true;
            Log.Warn("volcano lava: " + _lastFailure
                     + " (Disaster + does not flip the sign at runtime - that would look fixed "
                     + "here and break the other way somewhere else)");
        }

        /// <summary>
        /// One point onto the trail. **The last slot is always "the live head"**, and every
        /// <see cref="_trailStride"/> steps it is committed and the next slot opened.
        ///
        /// On hitting the limit (<see cref="MaxTrailPoints"/>) it folds by keeping every other
        /// point and doubles the stride. **The array is never grown** — with
        /// <c>MaxSteps = 512</c> it folds at most twice, and the shape is preserved.
        /// </summary>
        private static void AppendTrail(int index, Vec2 head)
        {
            if (_trails == null || _trails[index] == null) return;

            Vec2[] trail = _trails[index];
            int count = _trailCounts[index];

            if (count <= 0)
            {
                trail[0] = head;
                _trailCounts[index] = 1;
                _sinceCommit[index] = 0;
                return;
            }

            trail[count - 1] = head;

            if (++_sinceCommit[index] < _trailStride[index]) return;
            _sinceCommit[index] = 0;

            if (count >= MaxTrailPoints)
            {
                int folded = (count + 1) / 2;
                for (int k = 0; k < folded; k++) trail[k] = trail[k * 2];
                count = folded;
                _trailCounts[index] = folded;
                _trailStride[index] *= 2;
            }

            trail[count] = head;
            _trailCounts[index] = count + 1;
        }

        /// <summary>
        /// Rebuild the immutable arrays main reads. **Runs only on advancing ticks** (the cost
        /// table in the class doc). Arrays already published are not rewritten by a single byte;
        /// they are swapped out whole.
        /// </summary>
        private static void RebuildTrailSnapshot()
        {
            if (_flowCount <= 0 || _trails == null)
            {
                _trailPoints = new Vec2[0];
                _trailPointCounts = new int[0];
                return;
            }

            int total = 0;
            for (int i = 0; i < _flowCount; i++) total += _trailCounts[i];

            var points = new Vec2[total];
            var counts = new int[_flowCount];

            int cursor = 0;
            for (int i = 0; i < _flowCount; i++)
            {
                int c = _trailCounts[i];
                counts[i] = c;
                if (c > 0 && _trails[i] != null)
                {
                    Array.Copy(_trails[i], 0, points, cursor, c);
                    cursor += c;
                }
            }

            _trailPoints = points;
            _trailPointCounts = counts;
        }

        private static bool OffMap(Vec2 p)
        {
            return p.X < -MapHalfExtentMetres || p.X > MapHalfExtentMetres
                   || p.Z < -MapHalfExtentMetres || p.Z > MapHalfExtentMetres;
        }

        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcLava",
                     "lava flows=" + _flowCount + " alive=" + _aliveCount
                     + " longest=" + _longestMetres.ToString("F0") + " m"
                     + " ignited b=" + _buildingsIgnited + " t=" + _treesIgnited
                     + " refused=" + _buildingsRefused
                     + " slopeSign=" + (_slopeSignVerified ? "verified" : "not verified yet")
                     + " frame=" + frame);
        }
    }
}
