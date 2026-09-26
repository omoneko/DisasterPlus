using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Whether ⑤ can write terrain. **This is the whole point of Task 2.**
    ///
    /// ① showed vanilla's hazard map, ② vanilla's deterministic damage model, and ④ measured the
    /// real values of the storm prefabs in the live game. **What only the live game can tell ⑤ is
    /// this** — the real dimensions of <c>RawHeights</c> and the four reach paths that rewrite the
    /// terrain.
    /// Without all of this, ⑤ does not raise the ground by a single metre (<see cref="Usable"/>).
    ///
    /// It is a struct for the same reason as ④'s <see cref="TyphoonPrefabFacts"/>
    /// (it holds nothing but bools and ints, so caching it does not drag in Unity's fake-null
    /// self-repair problem). All defaults are false = "not read yet / no longer readable".
    ///
    /// **Do not merge the flags into one.** The mountain (<see cref="HeightsResolved"/> /
    /// <see cref="UpdateAreaResolved"/>), the lava's scorching (<see cref="BurnGroundResolved"/>)
    /// and the lava's path (<see cref="SlopeSampleResolved"/>) can break independently. Merge them
    /// and an environment where only the lava cannot flow stops the mountain too.
    /// </summary>
    public struct VolcanoTerrainFacts
    {
        /// <summary>Raw cells across the whole map (1081²). Measured in <c>TerrainManager.Awake</c>, §A-1.</summary>
        public const int ExpectedRawArrayLength = 1168561;

        /// <summary>Whether <c>TerrainManager.RawHeights</c> (<c>ushort[]</c>) could be obtained.</summary>
        public readonly bool HeightsResolved;

        /// <summary>
        /// <c>RawHeights.Length</c>. **Unless it is 1081² = 1168561, every subsequent cell
        /// calculation is off** (<c>index = z*1081 + x</c>). 0 when it could not be read.
        /// </summary>
        public readonly int RawArrayLength;

        /// <summary>
        /// Whether <c>TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool)</c> could be
        /// resolved. **The only path that gets written heights into the game** (§A-1).
        /// </summary>
        public readonly bool UpdateAreaResolved;

        // ★ CraterResolved (whether DisasterHelpers.MakeCrater could be looked up) used to live
        //   here. The crater became part of the height profile (Core/Volcano/VolcanoCrater) and ⑤
        //   no longer calls MakeCrater from anywhere. **Do not keep measuring a fact nobody
        //   gates on.**

        /// <summary><c>DisasterHelpers.BurnGround(Vector2,float,float)</c> (§B-7b. **No DLC needed**).</summary>
        public readonly bool BurnGroundResolved;

        /// <summary>
        /// <c>TerrainManager.SampleDetailHeight(Vector3, out float, out float)</c> (§B-6).
        /// **The only path by which the lava finds downhill.** The mountain, the clearing and the
        /// eruption do not depend on it.
        /// </summary>
        public readonly bool SlopeSampleResolved;

        /// <summary>
        /// Whether the Natural Disasters DLC is owned. **⑤ does not need the DLC** (design doc
        /// §1.4). The only thing that branches on it is igniting trees
        /// (<c>TreeManager.BurnTree</c> is DLC-gated, §B-7c), so
        /// **false is not treated as a FAIL.**
        /// </summary>
        public readonly bool NaturalDisastersOwned;

        public VolcanoTerrainFacts(bool heightsResolved, int rawArrayLength,
                                   bool updateAreaResolved,
                                   bool burnGroundResolved, bool slopeSampleResolved,
                                   bool naturalDisastersOwned)
        {
            HeightsResolved = heightsResolved;
            RawArrayLength = rawArrayLength;
            UpdateAreaResolved = updateAreaResolved;
            BurnGroundResolved = burnGroundResolved;
            SlopeSampleResolved = slopeSampleResolved;
            NaturalDisastersOwned = naturalDisastersOwned;
        }

        /// <summary>
        /// Whether ⑤ may build even one mountain. **This is the gate for the whole of ⑤.**
        ///
        /// Two things are needed: the array itself, and the path that gets written values into the
        /// game. Do a "write it anyway" when the length is not 1081² and the
        /// <c>z*1081 + x</c> index points at a different cell, so
        /// **an unrelated part of the map is uplifted**.
        /// Do not substitute a guess (design doc §6).
        ///
        /// **The assumption check (<c>Assumptions.Volcano</c>) must use this very expression as
        /// its predicate.** Use "did the field resolve" as the predicate and a PASS comes out when
        /// the value is unusable (④'s review and ②'s audit found the same defect).
        /// </summary>
        public bool Usable
        {
            get
            {
                return HeightsResolved
                       && RawArrayLength == ExpectedRawArrayLength
                       && UpdateAreaResolved;
            }
        }
    }

    /// <summary>
    /// The immutable snapshot built on the sim thread and read on the main thread.
    /// The same discipline as ①'s <c>WeatherSnapshot</c>, ②'s <c>EarthquakeSnapshot</c> and ④'s
    /// <see cref="TyphoonSnapshot"/>: **once built it is never rewritten**.
    ///
    /// **T3 onwards add fields. Always append them at the end of the ctor** (so that every
    /// existing call site does not have to be changed).
    ///
    /// ⑤'s display convention: of the values carried here, the only vanilla measurements are
    /// <see cref="Terrain"/> (the array's real dimensions and the reach paths) and
    /// <see cref="GameMode"/>; everything else is a quantity this mod chose (design doc §7.4).
    /// </summary>
    public class VolcanoSnapshot
    {
        /// <summary>Whether the read succeeded. If false, the display side says "cannot be read".</summary>
        public readonly bool Valid;

        /// <summary>The terrain API measurements. **Whether ⑤ can run at all is decided here and nowhere else.**</summary>
        public readonly VolcanoTerrainFacts Terrain;

        /// <summary><c>SimulationManager.m_currentFrameIndex</c>.</summary>
        public readonly uint CurrentFrame;

        /// <summary>
        /// Whether this is game mode (false in the map editor).
        /// The catch-up speed of <c>m_blockHeights</c> upwards changes from 2 m in the game to
        /// 8 m in the editor (§A-2).
        /// **If it cannot be read, assume game mode** (report the slower one).
        /// </summary>
        public readonly bool GameMode;

        /// <summary>
        /// The phase at the top of this tick (T4 onwards).
        ///
        /// ★ <b>This is the state of one tick ago.</b> <c>VolcanoFeature.OnSimulationTick</c> does
        /// "read and publish" above the pause guard, and <c>VolcanoState.Tick</c>, which advances
        /// the phase, sits below it. That ordering is there so the panel does not freeze while
        /// paused, so by design the phase is reflected one tick late
        /// (④'s <see cref="TyphoonSnapshot"/> is handled the same way).
        /// </summary>
        public readonly VolcanoPhase Phase;

        /// <summary>The latest survey result. <c>Valid == false</c> means "not surveyed yet".</summary>
        public readonly VolcanoFootprint Footprint;

        /// <summary>
        /// Uplift progress [0,1]. **Always 0 until T6 moves it.**
        /// <b>Do not show a row for it while it is 0</b> (the doc of
        /// <see cref="VolcanoState.ProgressUnit"/>).
        /// </summary>
        public readonly float ProgressUnit;

        /// <summary>The most recent reason for a refusal (**English, for diagnostics**). null if nothing was refused.</summary>
        public readonly string Refusal;

        // ── the seven T5 (clearing) added ───────────────────────────────────────────────────

        /// <summary>
        /// The radius the clearing sweep reached (m). **It is precisely the radius T6 may uplift**
        /// (<c>VolcanoClearing.ClearedRadiusMetres</c>). **It is a display-only copy, and T6 must
        /// read the sim-side static rather than this** (the snapshot is one tick late by design).
        /// </summary>
        public readonly float ClearedRadiusMetres;

        /// <summary>Whether the clearing reached the mountain's radius.</summary>
        public readonly bool ClearingComplete;

        /// <summary>Buildings removed so far for this volcano.</summary>
        public readonly int BuildingsDestroyed;

        /// <summary>Road segments removed so far for this volcano.</summary>
        public readonly int SegmentsDestroyed;

        /// <summary>
        /// Buildings vanilla refused to remove in the last sweep. **A non-zero value is not an
        /// anomaly**, but the terrain under them alone stays at its original height (the class doc
        /// of <c>VolcanoClearing</c>).
        /// </summary>
        public readonly int BuildingsRefused;

        /// <summary>Whether the last sweep was cut short by the per-sweep limit.</summary>
        public readonly bool ClearingCapped;

        /// <summary>
        /// Whether the clearing path (removing roads and buildings) holds in this build of the
        /// game. **If false, ⑤ creates no volcano at all** (design doc §1.2).
        ///
        /// ★ It covers the building side (<c>CollapseBuilding</c>) as well as the roads
        /// (whole-project review M9. The doc of
        /// <c>VolcanoClearing.ClearingPathAvailable</c>).
        /// </summary>
        public readonly bool ClearingPathAvailable;

        // ── the six T6 (uplift) added ───────────────────────────────────────────────────────
        //
        // ★ The uplift progress itself does not get a new field; it uses
        //   <see cref="ProgressUnit"/> (which has existed since T2 and was always 0 up to T5).
        //   Hold two progresses and one day only one of them will be updated.

        /// <summary>The current rise of the summit (m). **Relative to the original terrain height.**</summary>
        public readonly float SummitMetres;

        /// <summary>
        /// The radius that may be raised this tick (m) = **the range the clearing has reached**.
        /// The panel says so alongside it — it is the only row that lets you verify trap 1
        /// (raising ahead of the clearing) with your own eyes in the live game.
        /// </summary>
        public readonly float ActiveRadiusMetres;

        /// <summary>Whether the uplift has finished.</summary>
        public readonly bool UpliftComplete;

        /// <summary>
        /// Whether the summit hollow has reached its full depth. **Not "whether it was carved"** —
        /// the crater is part of the height profile and is there from the uplift's very first tick
        /// (<c>Core/Volcano/VolcanoCrater</c>).
        /// </summary>
        public readonly bool CraterFormed;

        /// <summary>The number of tiles covering the affected rectangle.</summary>
        public readonly int UpliftTileCount;

        /// <summary>The index of the tile to <c>UpdateArea</c> next.</summary>
        public readonly int UpliftTileCursor;

        // ── the three T7 (eruption) added ───────────────────────────────────────────────────
        //
        // ★ These three exist **purely for drawing**. They represent no game state at all, so the
        //   sim side (VolcanoState / VolcanoClearing / VolcanoUplift) does not read them.

        /// <summary>Whether an eruption is in progress. The only gate for main's <c>VolcanoEruptionFx.Update</c>.</summary>
        public readonly bool EruptionActive;

        /// <summary>
        /// The eruption strength <c>[0,1]</c>. **A quantity this mod chose**, not a value the
        /// game computed (design doc §7.4). It does not quote a real physical unit.
        /// </summary>
        public readonly float EruptionIntensityUnit;

        /// <summary>
        /// The vent's world coordinates. <c>Y</c> is <b>the crater floor</b>
        /// (<c>SampleDetailHeight</c> plus a small lift), **not the summit rim** (live report ②).
        /// Main uses the value sim read, as is —
        /// **the main thread does not re-sample the terrain** (keep it to one path).
        /// </summary>
        public readonly Vec3 VentWorld;

        /// <summary>
        /// **Whether we are in the middle of the caldera-forming great explosion**
        /// (<c>VolcanoEruption.InClimax</c>).
        /// Only ever true for a super-eruption (the slider at its top).
        /// </summary>
        public readonly bool SupereruptionClimax;

        /// <summary>
        /// The radius of the ring of fissures (m). **0 means "there is no ring"** (the central
        /// vent only).
        ///
        /// An eruption in the caldera-forming stage does not come up through the central vent but
        /// <b>along the ring fault at the edge of the foundering roof</b>. Without distributing
        /// some share of the blasts around this ring, no blast is visible anywhere on a caldera
        /// 5 km across (the class doc of <c>Core.Volcano.BlastCluster</c>).
        /// </summary>
        public readonly float RingFissureRadiusMetres;

        // ── the nine T8 (lava) added ────────────────────────────────────────────────────────

        /// <summary>Number of flows emitted from the crater (the setting's value; 0 disables it entirely).</summary>
        public readonly int LavaFlowCount;

        /// <summary>Number of flows still moving.</summary>
        public readonly int LavaAliveCount;

        /// <summary>The longest distance any flow travelled (m).</summary>
        public readonly float LavaLongestMetres;

        /// <summary>Number of buildings set alight so far.</summary>
        public readonly int LavaBuildingsIgnited;

        /// <summary>Number of trees set alight so far (always 0 without ND).</summary>
        public readonly int LavaTreesIgnited;

        /// <summary>Whether trees can be set alight in this environment (i.e. whether the ND DLC is owned. §B-7c).</summary>
        public readonly bool LavaTreesAvailable;

        /// <summary>
        /// An **immutable array** concatenating the trail points of all the flows. It is the only
        /// entry point T9's rendering reads.
        ///
        /// ★ <b>The plan said <c>LavaHeads</c> (a single <c>Vec2[]</c>), but this is split into
        /// the points and the counts.</b> A single array **cannot represent N separate
        /// polylines** — a ribbon joining the heads of different flows comes out as a band flying
        /// back and forth between them. <see cref="LavaTrailCounts"/> holds the boundaries.
        /// </summary>
        public readonly Vec2[] LavaTrailPoints;

        /// <summary>The point count of each flow (**an immutable array**). The total is the length of <see cref="LavaTrailPoints"/>.</summary>
        public readonly int[] LavaTrailCounts;

        /// <summary>
        /// How cool it is, <c>[0,1]</c>. 1 is "still hot", 0 is "cooled out".
        /// T9's rendering fades the colour with this (so stopped lava does not glow for ever).
        /// </summary>
        public readonly float LavaCoolUnit;

        public VolcanoSnapshot(bool valid, VolcanoTerrainFacts terrain,
                               uint currentFrame, bool gameMode,
                               VolcanoPhase phase, VolcanoFootprint footprint,
                               float progressUnit, string refusal,
                               float clearedRadiusMetres, bool clearingComplete,
                               int buildingsDestroyed, int segmentsDestroyed,
                               int buildingsRefused, bool clearingCapped,
                               bool clearingPathAvailable,
                               float summitMetres, float activeRadiusMetres,
                               bool upliftComplete, bool craterFormed,
                               int upliftTileCount, int upliftTileCursor,
                               bool eruptionActive, float eruptionIntensityUnit,
                               Vec3 ventWorld,
                               bool supereruptionClimax, float ringFissureRadiusMetres,
                               int lavaFlowCount, int lavaAliveCount, float lavaLongestMetres,
                               int lavaBuildingsIgnited, int lavaTreesIgnited,
                               bool lavaTreesAvailable, Vec2[] lavaTrailPoints,
                               int[] lavaTrailCounts, float lavaCoolUnit)
        {
            Valid = valid;
            Terrain = terrain;
            CurrentFrame = currentFrame;
            GameMode = gameMode;
            Phase = phase;
            Footprint = footprint;
            ProgressUnit = progressUnit;
            Refusal = refusal;
            ClearedRadiusMetres = clearedRadiusMetres;
            ClearingComplete = clearingComplete;
            BuildingsDestroyed = buildingsDestroyed;
            SegmentsDestroyed = segmentsDestroyed;
            BuildingsRefused = buildingsRefused;
            ClearingCapped = clearingCapped;
            ClearingPathAvailable = clearingPathAvailable;
            SummitMetres = summitMetres;
            ActiveRadiusMetres = activeRadiusMetres;
            UpliftComplete = upliftComplete;
            CraterFormed = craterFormed;
            UpliftTileCount = upliftTileCount;
            UpliftTileCursor = upliftTileCursor;
            EruptionActive = eruptionActive;
            EruptionIntensityUnit = eruptionIntensityUnit;
            VentWorld = ventWorld;
            SupereruptionClimax = supereruptionClimax;
            RingFissureRadiusMetres = ringFissureRadiusMetres;
            LavaFlowCount = lavaFlowCount;
            LavaAliveCount = lavaAliveCount;
            LavaLongestMetres = lavaLongestMetres;
            LavaBuildingsIgnited = lavaBuildingsIgnited;
            LavaTreesIgnited = lavaTreesIgnited;
            LavaTreesAvailable = lavaTreesAvailable;
            LavaTrailPoints = lavaTrailPoints;
            LavaTrailCounts = lavaTrailCounts;
            LavaCoolUnit = lavaCoolUnit;
        }

        /// <summary>
        /// The one used when the read failed. **Do not fabricate "plausible" values out of a row
        /// of zeroes.**
        ///
        /// ★ Only <see cref="ClearingPathAvailable"/> is set to <b>true</b>. With that false, the
        /// panel emits a **definitive refusal**, "they cannot be removed, so no volcano will be
        /// built", whereas all this object can say is "this particular read failed".
        /// Do not present "could not be read" as a conclusion arrived at by measurement.
        /// </summary>
        public static VolcanoSnapshot Invalid()
        {
            return new VolcanoSnapshot(false, new VolcanoTerrainFacts(), 0u, true,
                                       VolcanoPhase.Idle, VolcanoFootprint.None, 0f, null,
                                       0f, false, 0, 0, 0, false, true,
                                       0f, 0f, false, false, 0, 0,
                                       false, 0f, new Vec3(0f, 0f, 0f),
                                       false, 0f,
                                       0, 0, 0f, 0, 0, false,
                                       new Vec2[0], new int[0], 0f);
        }
    }
}
