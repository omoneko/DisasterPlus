using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Local damage areas (patches) that **produce tornado-grade damage without producing
    /// a tornado**. <b>Sim thread only.</b> On by default.
    ///
    /// ── The owner's instruction ──────────────────────────────────────
    ///
    /// > Please produce several instances of tornado damage without producing tornadoes
    ///
    /// <b>We create no disaster object (<c>TornadoAI</c>), no vortex vehicle and no funnel
    /// mesh.</b> All we create is the phenomenon "here and there under the typhoon, for a
    /// short while, a narrow area gets wrecked as a tornado would wreck it". Where they go
    /// and how long they live is <see cref="GustPatchPlan"/>; how things break is
    /// <see cref="GustDamageModel"/> (both in Core, with tests).
    ///
    /// ── How this differs from the retired accompanying tornado ───────────
    ///
    /// The old implementation borrowed vanilla's tornado disaster. The look and the
    /// destruction came free at vanilla quality, but that destruction goes through
    /// <c>DisasterHelpers.DestroyStuff</c>, which **Natural Disasters Renewal replaces
    /// wholesale** (IL facts document §F-1). The patches call
    /// <c>BuildingAI.CollapseBuilding</c> directly (i.e. the same route as ④'s wind
    /// damage), so they are **completely free of conflict with NDR**. The settings keys
    /// <c>typhoonTornado</c> / <c>typhoonTornadoCount</c> are <b>retired</b> and not one
    /// place reads them any more (<c>ModSettings</c>'s doc. **Do not reuse them with a
    /// different meaning**).
    ///
    /// ── It keeps no ledger ───────────────────────────────────────
    ///
    /// A patch's state is <b>the typhoon's elapsed frame count and nothing else</b>.
    /// Position, size and lifetime are all functions of "which patch number this is"
    /// (<see cref="GustPatchPlan"/>), so all this class remembers is "when damage was last
    /// done" and the diagnostic counters.
    /// **So when the typhoon goes, not one patch remains** — the failure mode where you
    /// forget to manage a lifetime and something lingers cannot happen structurally.
    /// <see cref="Reset"/> is called from <c>TyphoonController.Forget</c> and
    /// <c>TyphoonFeature.OnLevelUnloading</c> (both idempotent).
    ///
    /// ── The ceiling on work per tick (all of it written here) ────────────
    ///
    /// - The damage sweep runs at most <b>once per
    ///   <see cref="DamageIntervalFrames"/> of the typhoon's elapsed frames</b>
    /// - One sweep looks at <b>at most
    ///   <see cref="GustPatchPlan.MaxActivePatches"/> patches</b>
    /// - One patch looks at <b>at most <see cref="MaxCellsPerPatch"/> grid cells</b>
    ///   (a 95 m radius plus 64 m cells means 5×5 is enough)
    /// - One sweep examines <b><see cref="MaxBuildingsPerPass"/> buildings across all
    ///   patches</b>
    /// - <c>AddWind</c> / <c>DestroyTrees</c> / <c>DispatchEffect</c> are each called once
    ///   per patch per sweep
    ///
    /// ── The destruction route (exactly the same discipline as wind damage) ──
    ///
    /// - Call <c>BuildingAI.CollapseBuilding(demolish: false, burnAmount: 0)</c> directly.
    ///   **Do not go through** <c>DisasterHelpers.DestroyBuildings</c> /
    ///   <c>DestroyNetSegments</c>
    /// - <b>Do not write a single byte of <c>Building.m_fireIntensity</c>.</b>
    ///   Write it and you get a permanent ghost fire that nobody puts out, burnt into the
    ///   save and surviving even if the mod is removed
    /// - <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> return false to
    ///   <c>testOnly: true</c> and then really do collapse, so **do not filter on the
    ///   dry run** (always make the real call)
    /// - Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy really
    ///   do refuse <c>demolish: false</c>. **That is correct behaviour, so do not run away
    ///   to <c>demolish: true</c>.** Count them in <see cref="LastRefused"/>
    ///
    /// ── Do not mix the frame into the random draw ────────────────────────
    ///
    /// The draw is determined by (the patch's seed, the building ID) alone. Mix the frame
    /// in and the same building is re-drawn every tick, so every building inside a patch
    /// is wiped out with probability 1. Leaving it out means **a building only falls once a
    /// patch comes close and the probability rises**, i.e. "what it passed over is
    /// wrecked".
    /// </summary>
    public static class TyphoonGust
    {
        /// <summary>The interval between damage sweeps (the typhoon's elapsed frames).
        /// Kept short because the patches move.</summary>
        private const uint DamageIntervalFrames = 16u;

        /// <summary>The ceiling on grid cells one patch looks at (with a 95 m radius, 5×5
        /// is enough).</summary>
        private const int MaxCellsPerPatch = 25;

        /// <summary>The ceiling on buildings examined in one sweep (across all
        /// patches).</summary>
        private const int MaxBuildingsPerPass = 256;

        /// <summary>The number of cells along one side of the building grid (one cell is
        /// 64 m).</summary>
        private const int GridSide = 270;

        /// <summary>The ceiling on how many links of one cell's chain we walk (a guard
        /// against corrupt save data).</summary>
        private const int GridChainGuard = 49152;

        /// <summary>The flag condition for a candidate. The same as
        /// <c>TyphoonWind.CandidateMask</c>.</summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>The salt mixed in when building a patch's seed. **A fixed
        /// value.**</summary>
        private const uint PatchSeedSalt = 0x47555354u;   // "GUST"

        /// <summary>The vertical, rotational and centripetal components of the uplift (the
        /// same actual arguments as the tornado's in §B-1).</summary>
        private const float WindUpward = 80f;

        private const float WindRotational = 0.5f;

        private const float WindRadial = -40f;

        /// <summary><c>AddWind</c>'s radius multiplier (relative to the patch
        /// radius).</summary>
        private const float WindRadiusFactor = 1.6f;

        /// <summary>The inner radius within which trees definitely fall, ÷ the patch
        /// radius.</summary>
        private const float TreeInnerFraction = 0.35f;

        /// <summary>
        /// The dust density. From §B-4's one-shot mode formula
        /// <c>count = max(100, πr²) × magnitude × 0.01 × rateOverTime</c>, this value was
        /// chosen to give roughly 150 particles at a radius of 70 m and rate 20.
        /// **Do not make it too large** — <c>Collapse Particles</c> shares its particle
        /// budget (<c>maxParticles</c>) with real building collapses, so eating it up
        /// thins out collapses across the city.
        /// </summary>
        private const float DustMagnitude = 0.05f;

        /// <summary>The <c>Degraded</c> self-report key for felling trees.</summary>
        private const string TreeNoteKey = "typhoonGustTrees";

        private static ushort _typhoonId;
        private static uint _lastDamageElapsed;
        private static bool _damagedOnce;

        /// <summary>The particle effect borrowed for the dust. Held as **a single
        /// reference** and checked with <c>== null</c> each time (a destroyed one compares
        /// equal to null through Unity's fake-null).</summary>
        private static ParticleEffect _dust;

        private static bool _dustMissing;
        private static bool _treesUnavailable;
        private static bool _treeNotePosted;
        private static bool _errorLogged;

        // ── Diagnostic counters (all read and written from the sim thread only) ──
        private static int _passes;
        private static int _lastActive;
        private static int _lastScanned;
        private static int _lastCollapsed;
        private static int _lastRefused;
        private static int _totalCollapsed;
        private static bool _lastCapped;

        /// <summary>How many sweeps have run so far (cumulative for the session).</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>How many patches were alive in the most recent sweep. **0 means "there
        /// are none right now" and is not a fault.**</summary>
        public static int LastActive { get { return _lastActive; } }

        /// <summary>How many buildings the most recent sweep examined (those inside a
        /// patch's circle).</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>How many buildings collapsed in the most recent sweep.</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>How many buildings **vanilla refused by design** in the most recent
        /// sweep. **Non-zero is normal.**</summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>Buildings collapsed, cumulative for the session.</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>Whether the most recent sweep was cut short at its ceiling.</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>
        /// Call when letting go of a typhoon (<c>TyphoonController.Forget</c>) and on level
        /// unload. **Idempotent.**
        ///
        /// ★ No "stop the patches" work is needed here — a patch is not a ledger entry but
        ///   a function of the typhoon's elapsed frames, so the moment the typhoon is gone
        ///   not one of them exists (class doc). All we reset are the counters and the
        ///   sweep position, which must not be carried over to the next typhoon.
        /// </summary>
        public static void Reset()
        {
            _typhoonId = 0;
            _lastDamageElapsed = 0u;
            _damagedOnce = false;
            _passes = 0;
            _lastActive = 0;
            _lastScanned = 0;
            _lastCollapsed = 0;
            _lastRefused = 0;
            _totalCollapsed = 0;
            _lastCapped = false;

            // ★ Do not carry the reference to the borrowed effect across cities (we do not
            //   destroy it, since we never cloned it — destroy it and **the collapse dust
            //   for every building in the city disappears**).
            _dust = null;
            _dustMissing = false;

            if (_treeNotePosted)
            {
                _treeNotePosted = false;
                FeatureHost.ClearDegraded(TyphoonFeature.FeatureName, TreeNoteKey);
            }
            // ★ _errorLogged / _treesUnavailable are not reset. Both are "facts about the
            //    game build this DLL is referencing", not per-city state.
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
        /// <c>TyphoonController</c>'s statics on the same thread. It is kept as a parameter
        /// to give every element the same call shape.
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
        {
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon gust patches failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGust",
                             "typhoon gust patches failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step()
        {
            if (!TyphoonController.Active)
            {
                _typhoonId = 0;
                _lastActive = 0;
                return;
            }

            ushort id = TyphoonController.DisasterId;
            if (id != _typhoonId)
            {
                // A new typhoon. Do not carry the sweep position over (judge "we already
                // did this one" from the previous typhoon's elapsed frames and the first
                // patch disappears entirely).
                _typhoonId = id;
                _damagedOnce = false;
                _lastDamageElapsed = 0u;
            }

            uint elapsed = TyphoonController.ElapsedFrames;
            if (_damagedOnce && elapsed - _lastDamageElapsed < DamageIntervalFrames) return;

            _damagedOnce = true;
            _lastDamageElapsed = elapsed;

            int strength = ModSettings.TyphoonGustStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;
            if (strength == 0)
            {
                // The guarantee that the slider can disable it completely. No effects
                // either.
                _lastActive = 0;
                _lastScanned = 0;
                _lastCollapsed = 0;
                _lastRefused = 0;
                _lastCapped = false;
                return;
            }

            Sweep(id, elapsed, strength);
        }

        private static void Sweep(ushort typhoonId, uint elapsed, int strength)
        {
            uint first, last;
            if (!GustPatchPlan.AliveRange(elapsed, out first, out last))
            {
                _lastActive = 0;
                _lastScanned = 0;
                _lastCollapsed = 0;
                _lastRefused = 0;
                _lastCapped = false;
                return;
            }

            // If the prefab radius could not be read, do nothing (design doc §6: do not
            // guess).
            float stormRadius = TyphoonController.StormRadius;
            if (!(stormRadius > 0f))
            {
                _lastActive = 0;
                return;
            }

            var centre3 = TyphoonController.Centre;
            if (float.IsNaN(centre3.X) || float.IsNaN(centre3.Z))
            {
                _lastActive = 0;
                return;
            }

            // ★ Singleton<T>.instance runs FindObjectOfType and new GameObject when
            //    sInstance is null, which makes it a main thread only API, so we check
            //    exists first.
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ As with the bias, the heading is **re-read every sweep**. The patches'
            //   relative angles come out of GustPatchPlan relative to the heading, so if
            //   the track bends the scatter of patches turns with it.
            float heading = TyphoonController.HeadingRadians;
            bool southern = ModSettings.TyphoonSouthernHemisphere.value;
            var group = GroupOf(typhoonId);

            int active = 0, scanned = 0, collapsed = 0, refused = 0;
            bool capped = false;

            for (uint ordinal = first; ordinal <= last; ordinal++)
            {
                float relative, orbitFraction, patchRadius, strengthFraction;
                GustPatchPlan.Patch(typhoonId, ordinal, southern,
                                    out relative, out orbitFraction,
                                    out patchRadius, out strengthFraction);
                if (!(patchRadius > 0f)) continue;

                float angle = heading + relative;
                float r = orbitFraction * stormRadius;
                float px = centre3.X + Mathf.Cos(angle) * r;
                float pz = centre3.Z + Mathf.Sin(angle) * r;

                active++;

                uint seed = DeterministicRandom.Hash((uint)typhoonId ^ PatchSeedSalt, ordinal);

                if (scanned < MaxBuildingsPerPass)
                {
                    if (!Strike(buildings, grid, px, pz, patchRadius, strengthFraction,
                                strength, seed, group,
                                ref scanned, ref collapsed, ref refused))
                    {
                        capped = true;
                    }
                }
                else
                {
                    capped = true;
                }

                var position = new Vector3(px, centre3.Y, pz);
                PushWind(position, patchRadius, group);
                FellTrees(seed, position, patchRadius, group);
                Dust(position, patchRadius, strengthFraction, typhoonId);
            }

            _passes++;
            _lastActive = active;
            _lastScanned = scanned;
            _lastCollapsed = collapsed;
            _lastRefused = refused;
            _lastCapped = capped;
            _totalCollapsed += collapsed;

            WriteDiag(typhoonId, strength, elapsed, first, last, active, scanned,
                      collapsed, refused, capped);
        }

        /// <summary>
        /// The damage for one patch. false if we hit a ceiling (the caller then raises
        /// capped).
        /// </summary>
        private static bool Strike(Building[] buildings, ushort[] grid,
                                   float px, float pz, float patchRadius,
                                   float strengthFraction, int strength, uint seed,
                                   InstanceManager.Group group,
                                   ref int scanned, ref int collapsed, ref int refused)
        {
            int minX = Clamp((int)((px - patchRadius) / 64f + 135f));
            int maxX = Clamp((int)((px + patchRadius) / 64f + 135f));
            int minZ = Clamp((int)((pz - patchRadius) / 64f + 135f));
            int maxZ = Clamp((int)((pz + patchRadius) / 64f + 135f));

            int cells = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (cells >= MaxCellsPerPatch || scanned >= MaxBuildingsPerPass) return false;
                    cells++;

                    int index = z * GridSide + x;
                    if (index < 0 || index >= grid.Length) continue;

                    ushort id = grid[index];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        // ★ Take down the next ID **before acting** (the same as ②'s
                        //   LongPeriodDamage).
                        ushort next = buildings[id].m_nextGridBuilding;

                        if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            float dx = p.x - px;
                            float dz = p.z - pz;
                            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

                            float chance = GustDamageModel.CollapseChance(
                                distance / patchRadius, strengthFraction, strength);

                            if (chance > 0f)
                            {
                                scanned++;

                                // ★ Do not mix the frame in (class doc).
                                if (DeterministicRandom.Unit(seed, id) < chance)
                                {
                                    bool accepted;
                                    if (Collapse(buildings, id, group, out accepted)) collapsed++;
                                    else if (!accepted) refused++;
                                }
                            }
                        }

                        id = next;
                        if (++guard > GridChainGuard) break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Actually knock it down. **Do not go via <c>DisasterHelpers</c>** (class doc).
        /// **Always make the real call even when the dry run returns false** —
        /// <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> perform the real collapse
        /// immediately after <c>if (testOnly) return false;</c>.
        /// </summary>
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
            // ★ Do not touch m_fireIntensity.
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// Blow citizens and vehicles about. **Harmless** (it is just the two lines
        /// <c>AddWindCitizens</c> + <c>AddWindVehicles</c> and touches neither buildings,
        /// roads nor trees).
        /// </summary>
        private static void PushWind(Vector3 position, float patchRadius,
                                     InstanceManager.Group group)
        {
            var lifted = new Vector3(position.x, position.y + patchRadius, position.z);
            var directional = new Vector3(0f, WindUpward, 0f);
            DisasterHelpers.AddWind(lifted, patchRadius * WindRadiusFactor, directional,
                                    WindRotational, WindRadial, group);
        }

        /// <summary>
        /// Fell trees. **Do not set them alight** (<c>burnRadiusMin</c> /
        /// <c>burnRadiusMax</c> are 0).
        /// This one route may be unresolvable in some environments (in which case we give
        /// up on felling trees and still do the building damage as usual).
        /// </summary>
        private static void FellTrees(uint seed, Vector3 position, float patchRadius,
                                      InstanceManager.Group group)
        {
            if (_treesUnavailable) return;

            try
            {
                DisasterHelpers.DestroyTrees((int)seed, group, position,
                                             patchRadius,                       // totalRadius
                                             0f,                                // removeRadius
                                             patchRadius * TreeInnerFraction,   // destructionMin
                                             patchRadius,                       // destructionMax
                                             0f, 0f);                           // ★ do not set alight
            }
            catch (System.Exception e)
            {
                _treesUnavailable = true;
                Log.Warn("typhoon gust: DisasterHelpers.DestroyTrees is unusable in this build ("
                         + e.GetType().Name + "); the damage patches keep running without "
                         + "felling trees");
                UpdateTreeNote();
            }
        }

        private static void UpdateTreeNote()
        {
            if (_treeNotePosted) return;
            _treeNotePosted = true;
            FeatureHost.NoteDegraded(TyphoonFeature.FeatureName, TreeNoteKey,
                "DisasterHelpers.DestroyTrees could not be called; the tornado-strength damage "
                + "patches still collapse buildings and push citizens, but they fell no trees");
        }

        /// <summary>
        /// One burst of dust. **We only borrow vanilla's <c>Collapse Particles</c>; we
        /// neither clone nor modify it** — it is the same instance as every building
        /// collapse in the city, so changing the colour or particle size here would change
        /// those collapses too (effects measurement document §D-5).
        ///
        /// We borrow **only the particle child**, not the <c>MultiEffect</c>
        /// (<c>Collapse Effect</c>). Fire the bundle and <c>Collapse Sound</c> plays too,
        /// repeating the collapse sound on every sweep.
        ///
        /// <c>DispatchEffect</c> queues through <c>Monitor.TryEnter</c>, so **it may be
        /// called from the sim thread** (IL facts document §C).
        /// If we cannot get it we give up once and never call again. **The damage
        /// continues.**
        /// </summary>
        private static void Dust(Vector3 position, float patchRadius, float strengthFraction,
                                 ushort typhoonId)
        {
            if (_dustMissing) return;

            // ★ Look at the reference itself each time. If it has been destroyed, fake-null
            //   makes it compare equal to null and it is looked up again here (the second
            //   city's self-repair).
            if (_dust == null)
            {
                _dust = ResolveDust();
                if (_dust == null)
                {
                    _dustMissing = true;
                    Log.Warn("typhoon gust: the game's collapse dust particles could not be "
                             + "resolved; the damage patches run without a dust plume");
                    return;
                }
            }

            if (!Singleton<EffectManager>.exists) return;

            InstanceID id = InstanceID.Empty;
            id.Disaster = typhoonId;

            var area = new EffectInfo.SpawnArea(position, Vector3.up, patchRadius * 0.6f);

            // ★ Pass null for the audio group. ParticleEffect's RequirePlay() is false, so
            //   not one entry is queued on the audio queue and the null is never read
            //   (the branching in DispatchEffect, IL facts document §C). There is no reason
            //   to go and touch AudioManager from the sim thread.
            Singleton<EffectManager>.instance.DispatchEffect(
                _dust, id, area, Vector3.zero, 0f,
                DustMagnitude * strengthFraction, null);
        }

        /// <summary>
        /// Get the particle child out of <c>BuildingProperties.m_collapseEffect</c>.
        /// **Return only the particles, not the bundle (<c>MultiEffect</c>)**
        /// (<see cref="Dust"/>'s doc).
        /// It is called from the sim thread, so look at <c>Singleton&lt;T&gt;.exists</c>
        /// first.
        /// </summary>
        private static ParticleEffect ResolveDust()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;

                var properties = Singleton<BuildingManager>.instance.m_properties;
                if (properties == null) return null;

                EffectInfo info = properties.m_collapseEffect;
                if (info == null) return null;

                var direct = info as ParticleEffect;
                if (direct != null) return direct;

                var multi = info as MultiEffect;
                if (multi != null && multi.m_effects != null)
                {
                    for (int i = 0; i < multi.m_effects.Length; i++)
                    {
                        var child = multi.m_effects[i].m_effect as ParticleEffect;
                        if (child != null) return child;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The disaster group. Passing it makes vanilla's own tally (buildings damaged per
        /// disaster) add up correctly.
        /// <c>null</c> if <c>InstanceManager</c> is not there yet.
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **Written every time, even when nothing collapsed.** <c>Log.Diag</c> thins the
        /// same key out, but **the string concatenation in the arguments would still run
        /// every time**, so we bail out first with <c>DiagEnabled</c>.
        /// </summary>
        private static void WriteDiag(ushort typhoonId, int strength, uint elapsed,
                                      uint first, uint last, int active, int scanned,
                                      int collapsed, int refused, bool capped)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGust",
                "gust pass#" + _passes + " typhoon#" + typhoonId
                + " strength=" + strength
                + " elapsed=" + elapsed
                + " patches=" + first + ".." + last + " (" + active + " alive)"
                + " scanned=" + scanned
                + " collapsed=" + collapsed + " (total " + _totalCollapsed + ")"
                + " refused=" + refused
                + (_treesUnavailable ? " trees=unavailable" : " trees=felled")
                + (_dustMissing ? " dust=unavailable" : " dust=ok")
                + (capped ? " (capped; some patches were not rolled this pass)" : ""));
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > GridSide - 1) return GridSide - 1;
            return v;
        }
    }
}
