using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The trench earthquake.** It raises an earthquake in <b>the sea nearest</b> the
    /// point the player clicked, and <b>that earthquake alone</b> brings a tsunami.
    /// **Sim thread only.**
    ///
    /// ── The owner's instruction (2026-08-22) ─────────────────────────────
    ///
    /// &gt; On earthquakes: I'd like to add a trench earthquake alongside vanilla's fault
    /// &gt; earthquake. At the moment fault earthquakes also produce tsunamis; please make
    /// &gt; it so vanilla earthquakes produce none, and only the newly added trench
    /// &gt; earthquake does (with a new icon too). For triggering it: click the icon, then
    /// &gt; left-click, and it happens in the sea nearest where you clicked.
    ///
    /// ── ★★ What "trench" means (the definition in the implementation) ───────
    ///
    /// The game has only one type of this disaster (<c>EarthquakeAI</c>). **A new
    /// disaster type cannot be added.** So "trench" is defined as
    /// <b>an earthquake this mod raised over the sea itself</b>. It is identified by
    /// remembering <b>the pair of disaster ID and random seed</b>
    /// (<see cref="LastId"/> and <see cref="IsTrenchQuake"/>).
    ///
    /// ★★ **The ID alone is not enough.** (2026-08-30, reproduced in the final
    ///   verification) <c>DisasterManager.CreateDisaster</c> <b>reuses the first slot it
    ///   finds with <c>m_flags == None</c></b>, scanning from index 1, and
    ///   <c>ReleaseDisaster</c> wipes the slot with <c>default(DisasterData)</c>. So once
    ///   a trench quake ends, <b>the next disaster usually takes the same number</b>.
    ///   Because we were watching the ID alone, that disaster was mistaken for a trench
    ///   quake too — <b>vanilla earthquakes' faults were suppressed, and over water they
    ///   even got a tsunami.</b> That flatly contradicts the owner's instruction that
    ///   vanilla earthquakes produce no tsunami.
    ///
    ///   <c>CreateDisaster</c> draws a fresh <c>m_randomSeed</c> from the sim's RNG for
    ///   each slot (measured in the IL). **If the seed has changed, it is a different
    ///   disaster.**
    ///
    /// ★ <b>Never identify it by the hypocentre's position.</b> The player can place an
    ///   earthquake over the sea from vanilla's disaster panel, and that was placed as a
    ///   fault quake. Decide by position and that earthquake gets a tsunami too, against
    ///   the instruction that vanilla earthquakes produce none.
    ///
    /// ── Traps (the same ones ③④⑤ hit. All measured in the IL) ─────────────
    ///
    /// <list type="number">
    /// <item><b>Always check <c>CreateDisaster</c>'s return value.</b> On false the output
    ///   is 0, and writing to it anyway <b>overwrites somebody else's disaster slot
    ///   (number 0)</b></item>
    /// <item><c>m_flags |= SelfTrigger (64)</c> is required.
    ///   <c>EarthquakeAI.StartDisaster</c> checks that bit, and without it the disaster
    ///   <b>sits in Emerging forever</b></item>
    /// <item>Where the prefab cannot be found (the ND DLC is not owned), <b>refuse and
    ///   record the reason</b></item>
    /// </list>
    /// </summary>
    public static class TrenchQuakeSlot
    {
        /// <summary>
        /// <c>DisasterData.Flags.SelfTrigger</c>. **Without it, the disaster sticks in Emerging.**
        /// </summary>
        private const ushort SelfTrigger = 64;

        private static ushort _id;

        /// <summary>
        /// The <c>DisasterData.m_randomSeed</c> at the moment it was raised.
        /// **The only clue that the number has been reused** (see the ★★ in the class doc).
        /// </summary>
        private static ulong _seed;
        private static Vec3 _epicentre;
        private static float _searchDistanceMetres;

        /// <summary>
        /// The disaster ID of the most recent trench earthquake raised. 0 means "none has
        /// ever been raised". <c>TsunamiChain</c> only attaches a tsunami to an earthquake
        /// matching this.
        /// </summary>
        public static ushort LastId { get { return _id; } }

        /// <summary>The position in the sea that actually became the hypocentre.</summary>
        public static Vec3 Epicentre { get { return _epicentre; } }

        /// <summary>Distance from the clicked point to the hypocentre (m). **Stated when it is further than expected.**</summary>
        public static float SearchDistanceMetres { get { return _searchDistanceMetres; } }

        /// <summary>How the last attempt turned out (**English, for diagnostics**). Always populated on a refusal.</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// How many times <see cref="Raise"/> has been attempted. **The tool waits for
        /// this to increase.**
        ///
        /// ★★ The earthquake is raised on the sim thread, so the tool (on main)
        ///   <b>cannot know there and then whether it worked</b>. It used to close the
        ///   tool the instant you clicked, so when the sim refused you got
        ///   <b>the marker appearing, the tool closing, and nothing happening</b> —
        ///   on screen, indistinguishable from <b>a dead button</b> (2026-08-30, fourth
        ///   round of verification).
        /// </summary>
        public static int AttemptSerial { get; private set; }

        /// <summary>Whether the most recent <see cref="Raise"/> succeeded.</summary>
        public static bool LastAttemptOk { get; private set; }

        /// <summary>
        /// Whether this disaster ID is a trench quake. Always false when
        /// <paramref name="id"/> is 0.
        ///
        /// ★★ **Do not decide by number alone.** <c>m_randomSeed</c> is checked as well —
        ///   numbers are reused, so without it <b>the next vanilla earthquake is mistaken
        ///   for a trench quake</b> (see the ★★ in the class doc).
        ///   Where the check cannot be made (the buffer cannot be read) it returns
        ///   <b>false</b>. Not attaching a tsunami is closer to the instruction than
        ///   attaching one by mistake.
        /// </summary>
        public static bool IsTrenchQuake(ushort id)
        {
            if (id == 0 || id != _id) return false;

            try
            {
                DisasterManager manager = Singleton<DisasterManager>.instance;
                if (manager == null || manager.m_disasters == null) return false;

                DisasterData[] buffer = manager.m_disasters.m_buffer;
                if (buffer == null || id >= buffer.Length) return false;

                // ★ The slot is free, so it is no longer our disaster. Forget it.
                if (buffer[id].m_flags == DisasterData.Flags.None)
                {
                    _id = 0;
                    _seed = 0UL;
                    return false;
                }

                if (buffer[id].m_randomSeed == _seed) return true;

                // ★★ **A different seed means the number was reused.** Forget it.
                //    Without forgetting, we repeat the same check every time we see this
                //    number, and <c>LastId</c> never returns to 0 for the rest of the
                //    session (2026-08-30, fifth round of verification).
                _id = 0;
                _seed = 0UL;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Call this on level load and unload. **Carry nothing across cities.**</summary>
        public static void Reset()
        {
            _id = 0;
            _seed = 0UL;
            _epicentre = new Vec3(0f, 0f, 0f);
            _searchDistanceMetres = 0f;
            AttemptSerial = 0;
            LastAttemptOk = false;
            Detail = null;
        }

        /// <summary>
        /// **Sim thread.** Raises an earthquake in the sea nearest
        /// <paramref name="point"/>. Returns false if it could not, leaving the reason in
        /// <see cref="Detail"/>.
        /// </summary>
        /// <param name="intensity">The raw value of vanilla's intensity slider (0-255).</param>
        public static bool Raise(Vec3 point, byte intensity)
        {
            bool ok;

            try
            {
                ok = RaiseCore(point, intensity);
            }
            catch (System.Exception e)
            {
                Detail = "raising the trench earthquake threw " + e.GetType().Name;
                Log.Error("trench earthquake failed", e);
                ok = false;
            }

            // ★★ **Always report.** The tool reads these two to decide whether to close
            //    or stay open.
            LastAttemptOk = ok;
            AttemptSerial++;

            if (!ok)
            {
                Log.Info("trench earthquake NOT raised: "
                         + (Detail ?? "no reason was recorded")
                         + ". The tool stays armed so it can be tried again");
            }

            return ok;
        }

        private static bool RaiseCore(Vec3 point, byte intensity)
        {
            DisasterInfo info = DisasterManager.FindDisasterInfo<EarthquakeAI>();
            if (info == null)
            {
                Detail = "no EarthquakeAI prefab is loaded; the Natural Disasters DLC is "
                         + "required for the trench earthquake";
                return false;
            }

            // ★★ **Refuse only while the tsunami is still under way.** (fourth round of
            //    verification)
            //
            //    The previous version refused on <c>IsTrenchQuake(_id)</c>, i.e. whether
            //    the disaster slot was still alive. But <c>EarthquakeAI</c>'s Clearing
            //    ends at <c>(|x| + 4800) &gt; elapsed*0.125 - 1000 - L</c>, so the slot is
            //    held for <b>17-35 real minutes from the click</b>. The tsunami finished
            //    after 7 minutes 40, yet we went on refusing with "it is still running"
            //    for <b>nearly 30 minutes afterwards.</b>
            //
            //    The reason to refuse is <b>that only one tsunami can be tracked</b>, not
            //    that the earthquake is long-lived. So we look at the tsunami's state.
            //    (By then the first earthquake is past Active and no longer cracking the
            //     ground, so moving the trench marker to the second one is fine.)
            // ★★ **This must not be an &&.** (2026-08-31, cross-checked)
            //    <c>IsTrenchQuake</c> becomes false the moment the earthquake's disaster
            //    slot is freed, but the tsunami travels for over 20 minutes after that.
            //    With &&, short-circuiting means <c>StillOwes</c> (i.e.
            //    <c>TsunamiRing.Running</c>) is never evaluated and a second one gets
            //    through. <c>Begin</c> then refuses it with "one is already running", so
            //    <b>you get the earthquake and no tsunami</b>.
            if (TsunamiRing.Running || (IsTrenchQuake(_id) && TsunamiChain.StillOwes(_id)))
            {
                Detail = "the tsunami from the previous trench earthquake (#" + _id
                         + ") has not finished yet; only one is tracked at a time. "
                         + "Wait for it - the wave is still on its way";
                return false;
            }

            Vec3 sea;
            float distance;
            bool sawDeepWater;

            // ★★ **Run exactly the same search as the preview does.** (2026-08-31,
            //    seventh round of verification) The search used to also check "does the
            //    tsunami's circle fit here?" and <b>skip past</b> points where it did not.
            //    That made <b>the marker and the actual hypocentre differ by up to
            //    2,304 m</b>, while the tool closed as though it had succeeded — the
            //    earthquake happened off screen, so to the player it was
            //    indistinguishable from "I clicked and nothing happened". It is exactly
            //    the shape this project itself names as "a breakage that invalidates
            //    in-game testing".
            //
            //    The circle is checked <b>on the single chosen point only, immediately
            //    before placing</b>. The sweep runs once (so the render thread's load does
            //    not come back), the marker and the hypocentre always agree, and if it
            //    does not fit we refuse on the spot with a reason.
            if (!NearestSea(point, SearchRings, out sea, out distance, out sawDeepWater))
            {
                // ★★ **On an inland map, "there is no sea" is the right answer.**
                //    Never invent a plausible-looking point and raise it there — that
                //    empties the trench earthquake of meaning.
                //
                // ★★ **Do not mix up the reasons.** "There was deep water but it was too
                //    narrow" and "there was no deep water at all" call for different next
                //    moves from the player (move further out to sea, versus give up).
                float reach = SeaSearch.StepMetres * SearchRings;

                Detail = sawDeepWater
                    ? ("the water within " + reach.ToString("F0")
                       + " m of the point you clicked is deep enough but too narrow: a "
                       + "trench earthquake needs open sea for "
                       + OpenSeaRadiusMetres.ToString("F0")
                       + " m in every direction, or the source resonates instead of "
                       + "radiating. Click further out to sea")
                    : ("no sea at least " + MinDepthMetres.ToString("F0")
                       + " m deep within " + reach.ToString("F0")
                       + " m of the point you clicked; a trench earthquake needs "
                       + "deep open water");
                return false;
            }

            // ★★ If the circle does not fit, refuse here (see the ★★ above).
            TerrainManager ringTerrain = Singleton<TerrainManager>.instance;
            if (ringTerrain != null
                && TsunamiRing.OpenWaterRadius(ringTerrain, sea.X, sea.Z) <= 0f)
            {
                Detail = "the sea at (" + sea.X.ToString("F0") + "," + sea.Z.ToString("F0")
                         + ") is deep enough but too narrow for the wave: the source needs "
                         + "a stretch of open water around it, and here the nearest shore "
                         + "or shallow is too close. Click further out to sea";
                return false;
            }

            ushort id;
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                // ★★ **Ignore the return value and we overwrite slot number 0.**
                Detail = "CreateDisaster refused (the disaster buffer is full?)";
                return false;
            }

            DisasterData[] buffer = Singleton<DisasterManager>.instance.m_disasters.m_buffer;

            buffer[id].m_targetPosition = new Vector3(sea.X, sea.Y, sea.Z);
            buffer[id].m_intensity = intensity;

            // ★★ Without this, StartDisaster returns immediately and it **sticks in Emerging.**
            buffer[id].m_flags |= (DisasterData.Flags)SelfTrigger;

            // ★ <c>DisasterAI.StartDisaster</c> is protected (confirmed in the IL), so it
            //   cannot be called directly. Use the public wrapper <c>StartNow</c> — all it
            //   does is call <c>StartDisaster</c> when <c>m_flags &amp; 0x3C</c>
            //   (Emerging|Active|Clearing|Finished) is clear, and straight after
            //   <c>CreateDisaster</c> only <c>Created(0x01)</c> is set, so it always gets
            //   through. <b>SelfTrigger(64) is not part of 0x3C</b>, so setting it first
            //   does not change that check (the same route as ③'s
            //   <c>FireWhirlSpawner</c>).
            info.m_disasterAI.StartNow(id, ref buffer[id]);

            // ★★ **Switch what we are tracking.** (Codex P1) If the previous earthquake
            //    is still alive in Clearing, the chain keeps tracking the old one and
            //    <b>misses this earthquake's Emerging→Active</b>. The previous tsunami
            //    has already been delivered (guaranteed by the StillOwes above), so it is
            //    safe to drop it here.
            TsunamiChain.Retarget();

            _id = id;
            _seed = buffer[id].m_randomSeed;
            _epicentre = sea;
            _searchDistanceMetres = distance;
            Detail = null;

            Log.Info("trench earthquake " + id + " raised at (" + sea.X.ToString("F0") + ","
                     + sea.Z.ToString("F0") + "), " + distance.ToString("F0")
                     + " m from the point that was clicked, intensity " + intensity
                     + ". This is the ONLY kind of earthquake that brings a tsunami");
            return true;
        }

        /// <summary>
        /// The sea nearest <paramref name="point"/>.
        /// The order is decided by <see cref="SeaSearch"/> (in Core, with tests); this
        /// file <b>only asks whether there is water there</b>.
        ///
        /// ★ <b>Take sea, not a river or a lake.</b> <c>HasWater</c> returns true for
        ///   flowing water too, so we also check that **the water surface is at sea
        ///   level** (<c>WaterSimulation.m_currentSeaLevel</c>). A trench earthquake in a
        ///   river through the city makes nonsense of the tsunami's explanation.
        /// </summary>
        internal static bool TryFindNearestSea(Vec3 point, out Vec3 sea,
                                               out float distanceMetres)
        {
            try
            {
                // ★★ **The preview limits the number of rings.** (2026-08-30, final
                //    verification) A full sweep is 37,249 points, and for each one
                //    HasWater and WaterLevel re-take the water simulation's read lock.
                //    RenderOverlay calls this every 6 frames, which worked out at 700,000
                //    lock operations per second.
                return NearestSea(point, SearchRings, out sea, out distanceMetres);
            }
            catch
            {
                sea = new Vec3(0f, 0f, 0f);
                distanceMetres = 0f;
                return false;
            }
        }

        /// <summary>
        /// Searches for sea usable as a hypocentre. <paramref name="maxRings"/> limits the
        /// number of rings (for the preview). <b>If nothing is found, refuse</b> —
        /// better not to raise one at all than to drop it into shallow sea (see the ★★
        /// below).
        /// </summary>
        private static bool NearestSea(Vec3 point, int maxRings,
                                       out Vec3 sea, out float distanceMetres)
        {
            bool ignored;
            return NearestSea(point, maxRings, out sea, out distanceMetres, out ignored);
        }

        /// <summary>
        /// As above. <paramref name="sawDeepWater"/> reports whether there was sea deep
        /// enough but it failed on <see cref="IsOpenSea"/>.
        ///
        /// ★★ **Do not mix up the reason for refusing.** (2026-08-30, final
        ///   verification) A 30 m fjord or a wide river passes the depth condition and
        ///   fails for not being open. Calling that "there is no sea that deep" is <b>a
        ///   lie</b> — this one sentence is the only explanation the player gets, so it
        ///   must not miss.
        /// </summary>
        private static bool NearestSea(Vec3 point, int maxRings,
                                       out Vec3 sea, out float distanceMetres,
                                       out bool sawDeepWater)
        {
            sea = new Vec3(0f, 0f, 0f);
            distanceMetres = 0f;
            sawDeepWater = false;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) return false;

            float seaLevel = terrain.WaterSimulation != null
                ? terrain.WaterSimulation.m_currentSeaLevel
                : DefaultSeaLevelMetres;

            ushort[] block = terrain.BlockHeights;
            int seaUnits = (int)(seaLevel * 64f);
            int minDepthUnits = (int)(MinDepthMetres * 64f);

            // ★★ **Look at the water column itself.** (2026-08-31, fifth round of
            //    verification) The version just before used <c>seaUnits - block[cell]</c>,
            //    i.e. it only looked at <b>how far the ground is below sea level</b>. That
            //    is not "there is sea" — <b>polders and craters enclosed by embankments
            //    sit below sea level and are dry</b>. Put a source with a radius of 3.8 km
            //    there and, since the drawdown circle is only 160 m, <b>water that cannot
            //    be taken back is left there forever</b>.
            //
            // ★ <c>WaterSimulation.Cell.m_height</c> is the water column itself (in
            //   1/64 m). <c>BeginRead</c> is taken <b>exactly once at each end of the
            //   sweep</b> — calling <c>HasWater</c> per point means thousands of lock
            //   operations (fourth round of verification).
            if (block == null) return false;
            if (terrain.WaterSimulation == null) return false;

            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
            if (cells == null) return false;

            // ★★ **Take the nearest "deep" sea, not simply the nearest sea.**
            //    (2026-08-30, learned from the offline reproduction)
            //
            //    The game's shallow-water solver <b>caps the flow at the depth</b> with
            //    <c>v = min(v, m_height)</c>. So shallow sea cannot carry a big wave
            //    however hard you push it. Measured (tools/WaterSolverSim, a 1081 grid):
            //
            //        40 m deep: an uplift of 17.8 m, the ring still 2.1 m at 5.3 km out
            //        10 m deep: an uplift of  4.2 m, the ring all but gone by 2.4 km
            //
            //    A "trench" quake happens in <b>deep open water offshore</b> in the first
            //    place, so taking the deeper option is also physically correct.
            //
            //    Sweep nearest-first and **settle the moment sea deep enough is found**.
            //    ★ **If none is found, refuse (false).** There is no fallback of
            //      "drop it wherever was deepest" — shallow sea cannot carry a wave in
            //      this solver and merely digs a hole at the hypocentre (see
            //      TsunamiSource's class doc).
            int count = SeaSearch.CountUpTo(maxRings < 0 ? 0 : (maxRings > SeaSearch.MaxRing ? SeaSearch.MaxRing : maxRings));
            for (int i = 0; i < count; i++)
            {
                float dx, dz;
                if (!SeaSearch.At(i, out dx, out dz)) break;

                float x = point.X + dx;
                float z = point.Z + dz;
                if (x < -MapHalfExtent || x > MapHalfExtent) continue;
                if (z < -MapHalfExtent || z > MapHalfExtent) continue;

                // ★★ **The lock is taken exactly once at each end of the sweep.**
                //    (fourth round of verification) This runs from <c>RenderOverlay</c> on
                //    <b>the main thread</b>, every 6 frames, over up to 2,401 points. It
                //    used to call <c>HasWater</c> / <c>WaterLevel</c> / <c>DepthAt</c> per
                //    point, and every one of those takes
                //    <c>WaterSimulation.BeginRead</c> (a <c>Monitor.TryEnter</c> spin) —
                //    **hundreds of times per frame, fighting the water thread for the
                //    lock.** Now we <c>BeginRead</c> once at the top of the sweep and
                //    reuse the borrowed array right through (the same shape as
                //    <c>SeaWatch</c>).
                int cell = CellOf(z) * (GridCells + 1) + CellOf(x);
                if (cell < 0 || cell >= block.Length || cell >= cells.Length) continue;

                // ★★ "Is this open sea?" is unified into a single expression (see
                //    <c>TsunamiRing.IsOpenSeaCell</c>'s doc).
                if (!TsunamiRing.IsOpenSeaCell(block, cells, cell,
                                               seaUnits, minDepthUnits)) continue;

                // ★ Getting this far means there was sea deep enough. That changes the
                //   reason we refuse.
                sawDeepWater = true;

                // ★★ **The water around the source must be open sea.** (2026-08-30,
                //    final verification) What is checked is the 8 azimuths at
                //    <c>OpenSeaRadiusMetres</c> (2,000 m). With land inside that,
                //    <b>the source itself becomes a bucket and resonates</b> (a sweep
                //    made it jump to +111 m). The offline reproduction only ever verified
                //    <b>open sea</b>, so we do not raise one on terrain we cannot vouch
                //    for.
                if (!IsOpenSea(terrain, cells, seaUnits, x, z)) continue;

                // ★ Whether the circle fits is <b>not checked here</b>. Check it and we
                //   skip past points where it does not, so the preview's marker and the
                //   hypocentre part company (see the ★★ in <c>RaiseCore</c>, seventh
                //   round of verification). <c>RaiseCore</c> makes that check once, on
                //   the single chosen point.
                sea = new Vec3(x, seaLevel, z);
                distanceMetres = SeaSearch.DistanceMetres(dx, dz);
                return true;
            }

            return false;
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }
        }

        /// <summary>
        /// The number of rings searched for sea. **The preview and the trigger must use
        /// the same value.**
        ///
        /// ★★ Having them differ was what 2026-08-30 flagged. The preview used 24 rings
        ///   (2,304 m) and the trigger 96 rings (9,216 m), so <b>clicking where no marker
        ///   appeared raised an earthquake 9 km away</b>. It happened off screen, so to
        ///   the player it looked like "I clicked and nothing happened" — precisely the
        ///   kind of breakage that invalidates in-game testing.
        ///
        /// ★ They are aligned on <b>the narrower one</b>. <c>SeaSearch.MaxRing</c> (96) is
        ///   37,249 points, and for each one <c>HasWater</c> and <c>WaterLevel</c> re-take
        ///   <c>WaterSimulation.BeginRead</c> (a <c>Monitor.TryEnter</c> spin). The
        ///   preview is called at close to frame rate, so aligning on the wider one means
        ///   <b>fighting the water thread for the lock.</b>
        ///   24 rings = 2,304 m = 2,401 points.
        /// </summary>
        private const int SearchRings = 24;

        /// <summary>The number of 16 m cells. <c>BlockHeights</c> is indexed <c>z*(1080+1)+x</c>.</summary>
        private const int GridCells = 1080;

        /// <summary>World coordinate to a 16 m cell (the same formula as <c>TsunamiWave.CellOf</c>).</summary>
        private static int CellOf(float world)
        {
            int c = (int)((world + MapHalfExtent) / 16f + 0.5f);
            return c < 0 ? 0 : (c > GridCells ? GridCells : c);
        }

        /// <summary>The number of azimuths probed around the source (8 of them).</summary>
        private const int OpenSeaProbes = 8;

        /// <summary>
        /// How much <b>open sea</b> is required around the source (m).
        ///
        /// ★★ **The old implementation's 1,088 m was too short for the current circle.**
        ///   (2026-08-31, cross-checked) The force at that time had a radius of 1,280 m,
        ///   but the current water source has a <b>radius of 3,840 m</b>, and within that
        ///   circle any land below the target water level is <b>filled directly up to
        ///   that level</b>. With the hypocentre near the shore, what appears is not a
        ///   wave arriving but <b>a circular flood</b>.
        ///
        /// ★ So why not require 3,840 m? Because demanding that rejects almost every map
        ///   with a bay or an inland sea. 2,000 m is the compromise. With a shore closer
        ///   in than that, the rise starts earlier by however much the circle's edge
        ///   overlaps land. <b>It is not broken, but it does not look like a wave.</b>
        ///
        /// ★★ <b>Use this same value in the refusal's wording.</b> The check used to be
        ///   1,088 m while the wording gave a different number, telling the player a
        ///   distance that was not true.
        /// </summary>
        public const float OpenSeaRadiusMetres = 2000f;

        /// <summary>
        /// Whether the area around the force's disc is <b>open sea</b>.
        ///
        /// ★★ Without this we raise one on terrain the offline reproduction never
        ///   verified (inlets, shallows, the map's edge). The reproduction tool's sea is
        ///   <b>flat sea with not one cell of land</b>, so the guarantees obtained there
        ///   extend to open sea and no further.
        /// </summary>
        private static bool IsOpenSea(TerrainManager terrain, WaterSimulation.Cell[] cells,
                                      int seaUnits, float x, float z)
        {
            // ★★ The caller already holds <c>BeginRead</c> (NearestSea). Do not re-take
            //    it here; use the array that was passed in.
            ushort[] block = terrain.BlockHeights;
            if (block == null || cells == null) return false;

            int minDepthUnits = (int)(MinDepthMetres * 0.5f * 64f);

            float r = OpenSeaRadiusMetres;

            for (int i = 0; i < OpenSeaProbes; i++)
            {
                float a = 6.2831853f * i / OpenSeaProbes;
                float px = x + Mathf.Cos(a) * r;
                float pz = z + Mathf.Sin(a) * r;

                if (px < -MapHalfExtent || px > MapHalfExtent) return false;
                if (pz < -MapHalfExtent || pz > MapHalfExtent) return false;

                // ★★ Use the same expression here too (sixth round of verification: this
                //    one place was still going by the sea floor's height).
                if (!TsunamiRing.IsOpenSeaCell(block, cells,
                        CellOf(pz) * (GridCells + 1) + CellOf(px),
                        seaUnits, minDepthUnits)) return false;
            }

            return true;
        }

        /// <summary>
        /// Half the map's extent (m). The same 8640 found in <c>TsunamiAI.FindSea</c>'s IL.
        /// </summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// The default sea level (m) used when it cannot be read. It is the measured value
        /// of <c>WaterSimulation.DEFAULT_SEA_LEVEL</c> (confirmed by reflection).
        /// </summary>
        private const float DefaultSeaLevelMetres = 40f;

        /// <summary>A water surface higher than this is taken to be a river or a lake (m).</summary>
        private const float RiverToleranceMetres = 6f;

        /// <summary>Water shallower than this does not count as "sea" (m).</summary>
        private const float MinDepthMetres =
            DisasterPlus.Core.Earthquake.TsunamiSource.ReferenceDepthMetres * 0.6f;
    }
}
