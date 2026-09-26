using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Creates one of vanilla's tornado disasters. That gets us the vortex mesh, the
    /// sound, the destruction, the disaster notification and the evacuation behaviour for
    /// free; rebuilding all of it ourselves would not be worth it.
    ///
    /// Call only from the sim thread. CreateDisaster touches the disaster buffer.
    /// </summary>
    public static class FireWhirlSpawner
    {
        private static DisasterInfo _tornadoInfo;
        private static bool _searched;

        /// <summary>
        /// How many calls have come in since the last failed scan. While a dense fire
        /// continues, TrySpawnNew calls this function every tick, so retrying on every
        /// failure would run a full prefab sweep every tick and bury the log. For this
        /// many calls we let a "failure cache" throttle the calls instead.
        /// </summary>
        private static int _missCallCount;

        /// <summary>For how many calls the failure cache holds. Never 0 (that would mean
        /// retrying every time). Too large and re-detection is slow in cases where the
        /// prefabs only arrive later, such as just after a DLC is enabled.</summary>
        private const int MissRetryCalls = 64;

        public static void Reset()
        {
            _tornadoInfo = null;
            _searched = false;
            _missCallCount = 0;
        }

        /// <summary>
        /// Finds the tornado's DisasterInfo.
        /// It matches on the AI's type, not on a name, so it is unaffected by
        /// localisation or by another mod renaming things. TornadoAI derives from
        /// WeatherDisasterAI; it is not a relative of MeteorAI (VehicleAI).
        /// </summary>
        public static DisasterInfo FindTornadoInfo()
        {
            // Check the object itself and not just the reference every time, to cope with
            // Unity's fake null.
            if (_searched && _tornadoInfo != null) return _tornadoInfo;

            // If the previous scan failed, it may simply be that we are right after a
            // level load and the prefabs are not all there yet. So do not settle on
            // "never look again", but do not sweep every prefab every tick either.
            // Throttle by the number of calls.
            if (_searched)
            {
                _missCallCount++;
                if (_missCallCount < MissRetryCalls) return null;
            }
            _missCallCount = 0;

            _searched = true;
            _tornadoInfo = ScanForTornadoInfo();

            if (_tornadoInfo == null)
            {
                // Warn is not throttled, so it would bury the log for as long as a dense
                // fire lasts. Drop to Diag, which throttles.
                Log.Diag("noTornadoPrefab",
                    "no TornadoAI DisasterInfo found; Natural Disasters DLC required for fire whirls");
            }
            return _tornadoInfo;
        }

        /// <summary>
        /// A pure function that only sweeps the prefabs. It touches neither the cache nor
        /// the miss count. Unlike the other members of this class, it cannot corrupt this
        /// class's state from any thread (all it reads is PrefabCollection).
        /// </summary>
        private static DisasterInfo ScanForTornadoInfo()
        {
            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info;
                try { info = PrefabCollection<DisasterInfo>.GetLoaded(i); }
                catch { continue; }   // the API does no bounds check, so guard each one

                if (info == null) continue;
                if (info.m_disasterAI is TornadoAI) return info;
            }
            return null;
        }

        /// <summary>
        /// For the assumption checks. Reports only whether the prefab resolves, with no
        /// side effects.
        ///
        /// Do not delegate to FindTornadoInfo(). That one writes _searched, _tornadoInfo
        /// and _missCallCount and is a "sim thread only" function, whereas
        /// Assumptions.Run() is called from the main thread once _levelReady is set — i.e.
        /// it overlaps with the sim thread going round TrySpawnNew → FindTornadoInfo. It
        /// is not destructive, but the miss cache can be rewound or a prefab we had found
        /// can be thrown away. In any case "a function that only inspects rewrites what it
        /// is inspecting" is the wrong shape here, so we replace it with a pure sweep.
        /// The cost of the sweep is that of one level load, which is not worth worrying
        /// about.
        /// </summary>
        public static bool HasTornadoPrefab()
        {
            return ScanForTornadoInfo() != null;
        }

        /// <summary>
        /// Raises one fire whirl.
        ///
        /// ── ★★ We no longer create a vanilla tornado disaster (2026-08-29, the
        ///    owner's instruction) ────────────────────────────────────────────
        ///
        /// &gt; I have confirmed a bug where, after a fire whirl has occurred, a tornado
        /// &gt; also occurs when another fire whirl occurs. Please treat the fire whirl and
        /// &gt; the tornado as separate things and remove the cause completely.
        ///
        /// **The cause was borrowing the tornado disaster in the first place.** From the
        /// in-game log:
        ///
        /// <code>
        ///   fire whirl 2 is not active yet; deferring teardown   (over and over)
        ///   fire whirl 2 attached to vortex vehicle 7917         (finally attached)
        ///   fire whirl 2 ending; both target slots moved ...
        ///   fire whirl 2 has been ending for 32.4 in-game minutes;
        ///       vanilla teardown never completed
        /// </code>
        ///
        /// What creates the vortex vehicle is <c>TornadoAI.ActivateDisaster</c>, and that
        /// runs when the disaster comes out of Emerging. In other words:
        ///
        /// <list type="number">
        /// <item><b>Between creation and attachment the vortex is pinned by nobody</b> —
        ///   for that whole time it is <b>just a vanilla tornado</b>, crossing the city
        ///   as it pleases</item>
        /// <item>If the whirl's lifetime runs out before it is attached,
        ///   <c>DeactivateNow</c> does nothing to a disaster that is not Active, so we
        ///   wait for ever, as in the log above</item>
        /// <item>And that leaves a tornado whose teardown never completes</item>
        /// </list>
        ///
        /// Improving the accuracy of the pinning <b>does not remove point 1</b> (the
        /// moment the vortex is born is out of our reach). **Giving up the borrowing is
        /// the only real cure.**
        ///
        /// The fire whirl is now <b>entirely our own</b>:
        ///
        /// <list type="bullet">
        /// <item>appearance … <c>FireWhirlFlameFx</c> (our own whirl of flame)</item>
        /// <item>damage … <c>FireWhirlDamage</c> (our own fire spread)</item>
        /// <item>tornado … <b>none. Not a single one is created.</b></item>
        /// </list>
        ///
        /// ★ The ID returned is <b>a synthesised number</b> (see
        ///   <see cref="SyntheticIdBase"/>). The disaster buffer only goes up to 256, so
        ///   it can never collide with this band — <c>FireWhirlPinner</c> looks at that to
        ///   decide "this is not a disaster".
        /// </summary>
        public static bool TrySpawn(Vec3 center, byte intensity, out ushort disasterId)
        {
            disasterId = NextSyntheticId();
            Log.Info("fire whirl " + disasterId + " created (no vanilla tornado disaster is "
                     + "involved; the fire whirl draws its own vortex)");
            return true;
        }

        /// <summary>
        /// The floor of the synthetic IDs. **The disaster buffer only goes up to 256, so
        /// there can never be a collision.** <c>FireWhirlPinner.IsSynthetic</c> tells them
        /// apart at this boundary.
        /// </summary>
        internal const ushort SyntheticIdBase = 40000;

        private static ushort _nextSynthetic = SyntheticIdBase;

        private static ushort NextSyntheticId()
        {
            if (_nextSynthetic >= 65000) _nextSynthetic = SyntheticIdBase;
            return _nextSynthetic++;
        }

        /// <summary>
        /// **Retired.** The body that used to create the tornado disaster (see the doc
        /// above). There is not a single caller left. **It is kept rather than deleted
        /// because on the day the IL findings written here (SelfTrigger / Significant /
        /// Emerging) are needed again, we would otherwise have to redo that whole
        /// investigation.**
        /// </summary>
        private static bool RetiredCreateTornadoDisaster(Vec3 center, byte intensity,
                                                         out ushort disasterId)
        {
            disasterId = 0;

            var info = FindTornadoInfo();
            if (info == null) return false;

            ushort id;
            if (!DisasterManager.instance.CreateDisaster(out id, info))
            {
                Log.Diag("spawnFail", "CreateDisaster returned false (disaster buffer full?)");
                return false;
            }

            var buffer = DisasterManager.instance.m_disasters.m_buffer;
            buffer[id].m_targetPosition = new Vector3(center.X, center.Y, center.Z);
            buffer[id].m_intensity = intensity;
            buffer[id].m_angle = 0f;

            // ★ Set SelfTrigger(64). **Dropping this was finding I4 of the second-layer
            //   review.**
            //
            //   TornadoAI.StartDisaster calls the base and then branches on m_flags & 64;
            //   if it is not set, none of the four things below happen (from the IL):
            //
            //     IL_0003  call DisasterAI::StartDisaster    ← the base. Clears Significant(256)
            //     IL_000E  if m_flags & 64 is 0, go to IL_0061 (ret)
            //     IL_0016  m_targetPosition.y = TerrainManager.SampleDetailHeight(...)
            //     IL_0031  m_activationFrame = m_startFrame + m_emergingDuration
            //     IL_0044  m_flags |= 256 (Significant)
            //     IL_0056  DisasterManager.m_randomDisasterCooldown = 0
            //
            //   Of these, **the one that was not taking effect was Significant(256)**.
            //   Four things read that bit (confirmed by sweeping the whole assembly):
            //   CommonBuildingAI.HandleCommonConsumption (and the DLCs' overrides of the
            //   same name), CommonBuildingAI.NearObjectInFire,
            //   FirewatchTowerAI.NearObjectInFire and DisasterManager.FollowDisaster.
            //   Without it, nearby buildings never call DetectDisaster and the fire whirl
            //   is **never discovered** — it appears neither on the hazard map nor in the
            //   notifications, and the camera cannot follow it. m_randomDisasterCooldown
            //   = 0 does not happen either, so a disaster this mod raises does not push
            //   back vanilla's random disasters.
            //
            //   We put m_targetPosition.y in ourselves, so even when it is overwritten the
            //   value is the same.
            //
            // **We deliberately accept the 256 frames of Emerging.** Leave the flag unset
            // and m_activationFrame stays 0, and TornadoAI.IsStillEmerging — unlike
            // EarthquakeAI's — has no special case for "true when m_activationFrame == 0"
            // but just does currentFrame < m_activationFrame (clt.un, from the IL), so it
            // becomes Active on the very next step. In other words it could have been
            // explained as "dropped in order to skip the emergence". But the price of
            // losing Significant is far too high, and the difference is at most 256 frames
            // (about 4 seconds at speed 1). **If you want to skip it, skip it
            // explicitly** — not as a side effect of dropping a flag.
            buffer[id].m_flags |= DisasterData.Flags.SelfTrigger;

            // DisasterAI.StartDisaster is protected (confirmed in the IL), so we cannot
            // call it directly. Use the public wrapper StartNow. StartNow simply calls
            // StartDisaster unless data.m_flags & 0x3C
            // (Emerging|Active|Clearing|Finished) is set, and immediately after
            // CreateDisaster m_flags is only Created(0x01), so StartDisaster is always
            // called here (confirmed in the IL). **SelfTrigger(64) is not part of 0x3C,
            // so setting it first does not change StartNow's decision** (the ldc.i4.s 60
            // at IL_0006). Once started, the vortex vehicle is created in the order
            // StartDisaster -> ActivateDisaster.
            info.m_disasterAI.StartNow(id, ref buffer[id]);

            disasterId = id;
            Log.Info("fire whirl disaster created id=" + id + " intensity=" + intensity);
            return true;
        }
    }
}
