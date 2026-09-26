using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>One live fire whirl. This is the registry's internal representation, so it
    /// never leaks outside.</summary>
    internal class ActiveFireWhirl
    {
        public ushort DisasterId;
        public ushort VehicleId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public FireWhirlLifecycle Life;

        /// <summary>It has entered its ending sequence: the movement target has been put
        /// at its current position and it is waiting for vanilla to tear it down.</summary>
        public bool Ending;

        /// <summary>
        /// How much in-game time (in minutes) has passed since the ending sequence began.
        ///
        /// Life.ElapsedMinutes cannot stand in for this. FireWhirlFeature.UpdateExisting
        /// skips whirls that are Ending, so Life's clock stops the moment Ending is set.
        /// Measuring "it entered the ending sequence but never ends" — the signature of
        /// the m_targetPos0 mix-up coming back — needs a separate clock of its own.
        /// </summary>
        public float EndingMinutes;

        /// <summary>
        /// ★★ **A retired flag. Nothing sets it true any more.**
        ///
        /// It used to mark a whirl the player had placed by hand from the disaster panel,
        /// and it exempted that whirl from the interrupt check on the spawn condition (N
        /// buildings within R). The manual spawn route itself has been removed (see the
        /// class doc on <see cref="FireWhirlFeature"/>), so the only writer left that can
        /// set it true is <see cref="FireWhirlRegistry.RestoreFromSave"/> — that is,
        /// **a save from the days when manual spawning existed**.
        ///
        /// **Do not delete it.** Save format version 2 carries this one byte, and the save
        /// format is a public contract. Stop reading and writing it and whirls from old
        /// saves lose their exemption, so they vanish on load with only the grace period
        /// to run out.
        /// </summary>
        public bool Manual;
    }

    /// <summary>
    /// The immutable copy handed outside the registry.
    ///
    /// Return a reference to a mutable object and duplicating the list still leaves the
    /// contents shared, so the sim thread can rewrite Center or Ending while the main
    /// thread is reading them outside the lock. Only copying by value establishes the
    /// boundary.
    /// </summary>
    public struct FireWhirlView
    {
        public readonly ushort DisasterId;
        public readonly ushort VehicleId;
        public readonly Vec3 Center;
        public readonly float Radius;
        public readonly int BurningCount;
        public readonly float ElapsedMinutes;
        public readonly bool Ending;

        /// <summary>How much in-game time (in minutes) has passed since the ending
        /// sequence began. 0 unless Ending.</summary>
        public readonly float EndingMinutes;

        public readonly bool Manual;

        internal FireWhirlView(ActiveFireWhirl w)
        {
            DisasterId = w.DisasterId;
            VehicleId = w.VehicleId;
            Center = w.Center;
            Radius = w.Radius;
            BurningCount = w.BurningCount;
            ElapsedMinutes = w.Life.ElapsedMinutes;
            Ending = w.Ending;
            EndingMinutes = w.EndingMinutes;
            Manual = w.Manual;
        }
    }

    /// <summary>
    /// The only shared mutable state in this mod.
    /// It is touched by three parties: the sim thread (the checks, the spawning, the
    /// damage), the main thread (the drawing) and the Harmony patch (pinning the
    /// position).
    ///
    /// net35 has no System.Collections.Concurrent, so we snapshot-then-render behind a
    /// plain lock.
    /// </summary>
    public static class FireWhirlRegistry
    {
        /// <summary>A spot where a whirl died. For this long, nothing respawns in the same
        /// place.</summary>
        private struct CoolingSpot
        {
            public Vec2 Center;
            public float RemainingMinutes;
        }

        private static readonly object _gate = new object();
        private static readonly List<ActiveFireWhirl> _active = new List<ActiveFireWhirl>();
        private static readonly List<CoolingSpot> _cooling = new List<CoolingSpot>();

        /// <summary>
        /// Registers one naturally spawned whirl.
        ///
        /// ★ There is no <c>manual</c> argument any more. **No route exists for a player
        ///   to place a fire whirl** (see the class doc on
        ///   <see cref="FireWhirlFeature"/>), so anything created from here is by
        ///   definition a natural spawn. <see cref="ActiveFireWhirl.Manual"/> survives
        ///   **for the sake of old saves**, and only the restore route
        ///   (<see cref="RestoreFromSave"/>) can put true in it.
        /// </summary>
        public static void Add(ushort disasterId, ushort vehicleId, Vec3 center, float radius,
                               int burningCount)
        {
            lock (_gate)
            {
                _active.Add(new ActiveFireWhirl
                {
                    DisasterId = disasterId,
                    VehicleId = vehicleId,
                    Center = center,
                    Radius = radius,
                    BurningCount = burningCount,
                    Life = FireWhirlLifecycle.Start(),
                    Ending = false,
                    EndingMinutes = 0f,
                    // ★ Natural spawning is the only route, so this is always false. Only
                    //   whirls read back from an old save can carry true
                    //   (RestoreFromSave).
                    Manual = false,
                });
            }
        }

        /// <param name="cooldownMinutes">
        /// How long this spot stays suppressed. By design it equals the maximum lifetime
        /// (the caller passes ModSettings.MaxLifetimeMinutes).
        /// </param>
        public static void Remove(ushort disasterId, float cooldownMinutes)
        {
            lock (_gate)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (_active[i].DisasterId != disasterId) continue;

                    if (cooldownMinutes > 0f)
                    {
                        _cooling.Add(new CoolingSpot
                        {
                            Center = _active[i].Center.ToVec2(),
                            RemainingMinutes = cooldownMinutes,
                        });
                    }
                    _active.RemoveAt(i);
                }
            }
        }

        public static int Count
        {
            get { lock (_gate) { return _active.Count; } }
        }

        /// <summary>
        /// How many spots are in cooldown (i.e. where respawning is suppressed).
        ///
        /// The overlay example in §7.1 of the design document prints cooldown at the end,
        /// and this is the most likely cause of "a huge fire is burning but no whirl
        /// appears", so we do not leave it unreadable from outside.
        /// </summary>
        public static int CoolingCount
        {
            get { lock (_gate) { return _cooling.Count; } }
        }

        /// <summary>
        /// Returns immutable views copied by value. The caller may walk them freely
        /// outside the lock. A reference to an ActiveFireWhirl never leaves this class.
        /// </summary>
        public static List<FireWhirlView> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<FireWhirlView>(_active.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(new FireWhirlView(_active[i]));
                return list;
            }
        }

        /// <summary>Advances the lifetime. Call from the sim thread.</summary>
        public static void AdvanceLife(ushort disasterId, float deltaMinutes, bool conditionMet)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Life = _active[i].Life.Advance(deltaMinutes, conditionMet);
                    return;
                }
            }
        }

        /// <summary>Applies a change in the fire's scale. Call from the sim thread.</summary>
        public static void UpdateStrength(ushort disasterId, float radius, int burningCount)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Radius = radius;
                    _active[i].BurningCount = burningCount;
                    return;
                }
            }
        }

        /// <summary>The lifetime verdict. So that Life never leaks outside, the evaluation
        /// is done inside the registry too.</summary>
        public static FireWhirlVerdict EvaluateVerdict(ushort disasterId, FireWhirlConfig config)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    return _active[i].Life.Evaluate(config);
                }
            }
            return FireWhirlVerdict.Dissipate;   // not found = it has already gone
        }

        /// <summary>
        /// Associates the vortex vehicle. The vehicle is created by ActivateDisaster, so
        /// it does not exist yet immediately after CreateDisaster; at the moment of Add
        /// the entry is registered with vehicleId = 0.
        /// </summary>
        public static void SetVehicle(ushort disasterId, ushort vehicleId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].VehicleId = vehicleId;
                    return;
                }
            }
        }

        /// <summary>Records that the ending sequence has begun. From here on we stop
        /// pinning the position.</summary>
        public static void MarkEnding(ushort disasterId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Ending = true;
                    _active[i].EndingMinutes = 0f;   // start the ending clock here
                    return;
                }
            }
        }

        /// <summary>
        /// Advances the "time since ending began" for whirls in the ending sequence. Call
        /// every tick from the sim thread. Call it even when the feature's setting is off
        /// (turning it off puts every whirl into Ending at that moment).
        /// </summary>
        public static void AdvanceEnding(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return;
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (!_active[i].Ending) continue;
                    _active[i].EndingMinutes += deltaMinutes;
                }
            }
        }

        /// <summary>
        /// The list of spots where spawning should be suppressed. Returns both the centres
        /// of live whirls and the spots where a whirl was until recently (in cooldown).
        ///
        /// Without the cooldown, the same huge fire keeps respawning in the same place the
        /// instant one dies, giving an effectively permanent whirl (and making the
        /// absolute cap meaningless).
        /// </summary>
        public static List<Vec2> Centers()
        {
            lock (_gate)
            {
                var list = new List<Vec2>(_active.Count + _cooling.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(_active[i].Center.ToVec2());
                for (int i = 0; i < _cooling.Count; i++) list.Add(_cooling[i].Center);
                return list;
            }
        }

        /// <summary>
        /// Advances the cooldowns and drops the spots that have cleared. Call every tick
        /// from the sim thread.
        /// </summary>
        public static void AdvanceCooldowns(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return;
            lock (_gate)
            {
                for (int i = _cooling.Count - 1; i >= 0; i--)
                {
                    var c = _cooling[i];
                    c.RemainingMinutes -= deltaMinutes;
                    if (c.RemainingMinutes <= 0f) _cooling.RemoveAt(i);
                    else _cooling[i] = c;
                }
            }
        }

        /// <summary>
        /// Called every step from the Harmony patch (the Postfix on
        /// VortexAI.SimulationStep). Decides from the vehicle ID whether this is a vortex
        /// we created, and returns the coordinates to pin it to.
        ///
        /// Pinning continues even during the ending sequence (Ending). Release it here and
        /// the vortex wanders freely throughout the spin-down to the target — the angular
        /// velocity falls from 1.0 at -0.05/step until it is under 0.05, about 20 steps,
        /// and then ArriveAtDestination takes 5 more until m_waitCounter > 4 — leaving the
        /// flame effect behind on its own at the spawn point. Keep it pinned and the
        /// distance to the target stays 0, so the spin-down reliably makes progress.
        /// The entry is removed by CollectFinished after the Unspawn.
        /// </summary>
        public static bool TryGetPinnedCenter(ushort vehicleId, out Vec3 center)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].VehicleId != vehicleId) continue;
                    center = _active[i].Center;
                    return true;
                }
            }
            center = new Vec3(0f, 0f, 0f);
            return false;
        }

        /// <summary>On level unload. State is never carried across cities.</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _active.Clear();
                _cooling.Clear();
            }
        }

        /// <summary>
        /// Restores from a save. Replaces the current _active (whatever was there before
        /// the call is lost). Vehicle IDs are left at 0, and
        /// FireWhirlPinner.AttachVehicles reattaches them after the load. Elapsed time is
        /// accumulated again from the saved value, so loading does not extend a lifetime.
        ///
        /// A note on ordering: DisasterPlusSerialization.OnLoadData finishes before
        /// DisasterPlusLoading.OnLevelLoaded (which calls FireWhirlRegistry.Clear()
        /// internally). So calling this directly from OnLoadData means Clear() wipes it.
        /// Make the call on the OnLevelLoaded side, after Clear() (by way of
        /// DisasterPlusSerialization.TakePendingRestore()).
        /// </summary>
        public static void RestoreFromSave(List<SavedFireWhirl> saved)
        {
            if (saved == null) return;

            lock (_gate)
            {
                _active.Clear();
                for (int i = 0; i < saved.Count; i++)
                {
                    _active.Add(new ActiveFireWhirl
                    {
                        DisasterId = saved[i].DisasterId,
                        VehicleId = 0,
                        Center = saved[i].Center,
                        Radius = saved[i].Radius,
                        BurningCount = saved[i].BurningCount,
                        Life = FireWhirlLifecycle.Start().Advance(saved[i].ElapsedMinutes, true),
                        Ending = false,
                        EndingMinutes = 0f,
                        Manual = saved[i].Manual,
                    });
                }
            }
        }
    }
}
