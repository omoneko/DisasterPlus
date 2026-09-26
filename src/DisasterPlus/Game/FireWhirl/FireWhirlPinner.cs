using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Attaching the vortex vehicle, and ending a whirl once its lifetime runs out. Call
    /// all of it from the sim thread.
    /// </summary>
    public static class FireWhirlPinner
    {
        // A reusable buffer, sim thread only. Nothing is allocated per tick.
        // The type is FastList<InstanceID>, the same as DisasterAI.m_tempList (confirmed).
        //
        // Note: measured by reflection, in this build of Assembly-CSharp.dll FastList<T>
        // actually sits in the global namespace, so the build succeeds without this using
        // (confirmed with a clean build after wiping obj/bin: 0 warnings, 0 errors).
        // We keep it here for future game-version differences and for readability during
        // review (it does no harm).
        private static readonly FastList<InstanceID> _tempInstances = new FastList<InstanceID>();

        /// <summary>
        /// The maximum distance at which an attachment is accepted (horizontal distance
        /// from the spawn point).
        ///
        /// Reading the IL of TornadoAI.ActivateDisaster established that the vortex
        /// vehicle is created at a point distance = intensity*10 + 400 away from
        /// targetPosition. This mod's fire whirls are always spawned at a fixed
        /// intensity = 60 (FireWhirlFeature.SpawnIntensityBase), so a legitimate
        /// attachment is always within 1000. We allow nearly three times that (3000) so
        /// that we reject the case where a disasterId has been reused and a different,
        /// unrelated vortex has slipped into this group, while never wrongly rejecting a
        /// legitimate attachment.
        /// Erring on the loose side is safer than erring on the tight side: if it is
        /// loose we merely retry on the next tick, whereas if it is too tight one of
        /// vanilla's tornadoes gets pinned by mistake.
        /// </summary>
        private const float MaxAttachDistance = 3000f;
        private const float MaxAttachDistanceSq = MaxAttachDistance * MaxAttachDistance;

        /// <summary>
        /// Attaches the vortex vehicle to whirls whose vehicle ID is not yet known.
        /// The vehicle is created by ActivateDisaster, so it does not exist immediately
        /// after CreateDisaster.
        /// </summary>
        /// <summary>
        /// Whether this ID is <b>one we synthesised</b> (i.e. not a vanilla disaster).
        ///
        /// ★★ Since 2026-08-29 the fire whirl creates no tornado disaster at all (see the
        ///   class doc on <c>FireWhirlSpawner.TrySpawn</c>). So <b>this type has no work
        ///   left to do</b>. **It is kept rather than deleted because the IL findings
        ///   written down here — how to find the vortex vehicle, the targets in both
        ///   slots, the conditions on DeactivateNow — will be needed on the day we borrow
        ///   the vortex again.**
        /// </summary>
        internal static bool IsSynthetic(ushort disasterId)
        {
            return disasterId >= FireWhirlSpawner.SyntheticIdBase;
        }

        public static void AttachVehicles()
        {
            var views = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < views.Count; i++)
            {
                // ★★ **A synthetic ID has no vehicle.** Go looking for one and
                //    <c>InstanceManager.GetAllGroupInstances</c> <b>treats that number as
                //    a disaster ID</b>, so we could pick up the vehicle of an unrelated
                //    disaster.
                if (IsSynthetic(views[i].DisasterId)) continue;

                if (views[i].VehicleId != 0) continue;

                ushort found = FindVortexVehicle(views[i].DisasterId, views[i].Center);
                if (found == 0) continue;

                FireWhirlRegistry.SetVehicle(views[i].DisasterId, found);
                Log.Info("fire whirl " + views[i].DisasterId + " attached to vortex vehicle " + found);
            }
        }

        /// <summary>
        /// Among the vehicles belonging to the disaster group, finds the one whose
        /// VehicleAI is the vortex. Follows the same route TornadoAI.GetPosition uses
        /// (InstanceID.Disaster → InstanceManager.GetAllGroupInstances).
        ///
        /// If a disasterId has been released and reused for another disaster, an old
        /// registry entry (still waiting with VehicleId==0) risks picking up an unrelated
        /// vortex vehicle. That would pin one of vanilla's tornadoes by mistake — the
        /// accident this patch most wants to avoid — so we always check that the candidate
        /// we found is not too far from our own spawn point (expectedCenter).
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId, Vec3 expectedCenter)
        {
            var id = InstanceID.Empty;
            id.Disaster = disasterId;

            _tempInstances.Clear();
            InstanceManager.GetAllGroupInstances(id, _tempInstances);

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            for (int i = 0; i < _tempInstances.m_size; i++)
            {
                ushort v = _tempInstances.m_buffer[i].Vehicle;
                if (v == 0) continue;

                var info = buffer[v].Info;
                if (info == null || !(info.m_vehicleAI is VortexAI)) continue;

                Vector3 pos = buffer[v].GetLastFrameData().m_position;
                float dx = pos.x - expectedCenter.X;
                float dz = pos.z - expectedCenter.Z;
                if (dx * dx + dz * dz > MaxAttachDistanceSq)
                {
                    // Too far away = we may have picked up an unrelated vortex through a
                    // reused disasterId. Pinning it here would stop one of vanilla's
                    // tornadoes moving, so we do not accept it as a candidate.
                    Log.Diag("fwAttachReject",
                        "vortex vehicle " + v + " for disaster " + disasterId +
                        " is too far from expected centre; rejecting (stale/reused id?)");
                    continue;
                }

                return v;
            }
            return 0;
        }

        /// <summary>
        /// Puts a whirl whose lifetime has run out onto vanilla's teardown path.
        ///
        /// We do not release the vehicle ourselves. Put the movement target at the current
        /// position and in due course VortexAI.ArriveAtDestination returns true
        /// (m_waitCounter > 4), and vanilla correctly performs DisasterAI.DeactivateNow
        /// and Vehicle.Unspawn.
        ///
        /// Slot 0 alone is not enough (established by reading the IL; the old text in §5.6
        /// of the design document was wrong). VortexAI.SimulationStep (the 6-argument one)
        /// opens with
        ///     if (LengthXZ(m_targetPos0 - frame.m_position) &lt; m_info.m_maxSpeed)
        ///         m_targetPos0 = m_targetPos1;
        /// and TornadoAI.ActivateDisaster puts in SetTargetPos(0, m_targetPosition) and
        /// SetTargetPos(1, m_targetPosition - dir*(intensity*10+400)), so writing only
        /// slot 0 with the current position gets overwritten on the next step by slot 1,
        /// 1000 m away, and ArriveAtDestination is never reached. Write both.
        ///
        /// We set w to 0. Vanilla puts the target in through the implicit
        /// Vector3 -&gt; Vector4 conversion (the op_Implicit in ActivateDisaster's IL) and
        /// VortexAI only reads it back through the implicit Vector4 -&gt; Vector3
        /// conversion, so w was 0 to begin with and carries no meaning.
        /// </summary>
        public static void BeginEnding(FireWhirlView v)
        {
            // ★★ **A synthetic ID is folded up on the spot.** There is no borrowed
            //    disaster and no vortex vehicle, so there is no reason whatsoever to wait
            //    for vanilla's teardown. (Waiting for it is what produced the pile of
            //    "deferring teardown" lines in the in-game log.)
            if (IsSynthetic(v.DisasterId))
            {
                FireWhirlRegistry.Remove(v.DisasterId, ModSettings.MaxLifetimeMinutes.value);
                return;
            }

            if (v.VehicleId == 0)
            {
                // Its lifetime ran out before a vehicle was attached. Simply dropping it
                // from the registry would leave vanilla's disaster alive as an untrackable
                // drifting tornado. Drop it only once we have properly stopped it.
                if (!TryDeactivateDisasterNow(v.DisasterId))
                {
                    // It is not Active yet (DisasterAI.DeactivateNow does nothing unless
                    // Active is set in m_flags; confirmed in the IL). Leave it alive
                    // without marking it Ending and let the next tick decide again. The
                    // lifetime verdict is monotonic, so once it becomes Active we are
                    // guaranteed to come back here.
                    Log.Diag("fwEndWait", "fire whirl " + v.DisasterId +
                             " is not active yet; deferring teardown");
                    return;
                }

                FireWhirlRegistry.Remove(v.DisasterId, ModSettings.MaxLifetimeMinutes.value);
                return;
            }

            FireWhirlRegistry.MarkEnding(v.DisasterId);

            var buffer = VehicleManager.instance.m_vehicles.m_buffer;
            Vector3 here = buffer[v.VehicleId].GetLastFrameData().m_position;
            var target = new Vector4(here.x, here.y, here.z, 0f);

            buffer[v.VehicleId].SetTargetPos(0, target);
            buffer[v.VehicleId].SetTargetPos(1, target);

            Log.Info("fire whirl " + v.DisasterId + " ending; both target slots moved to current position");
        }

        /// <summary>
        /// Stops a disaster that has no vortex vehicle, through vanilla's own path. Sim
        /// thread only.
        ///
        /// DisasterAI.DeactivateNow is public (confirmed in the IL), but it only delegates
        /// to DeactivateDisaster when Active(8) is set in m_flags. It has no effect on a
        /// disaster that is still Emerging, so in that case we return false and make the
        /// caller wait.
        /// </summary>
        /// <returns>true if the disaster stopped (or had already gone). false if it cannot
        /// be stopped yet.</returns>
        private static bool TryDeactivateDisasterNow(ushort disasterId)
        {
            try
            {
                var disasters = DisasterManager.instance.m_disasters.m_buffer;
                if (disasterId == 0 || disasterId >= disasters.Length) return true;

                // It has already gone. All that is left is to tidy up.
                if ((disasters[disasterId].m_flags & DisasterData.Flags.Created) == DisasterData.Flags.None)
                    return true;

                if ((disasters[disasterId].m_flags & DisasterData.Flags.Active) == DisasterData.Flags.None)
                    return false;

                var info = disasters[disasterId].Info;
                if (info == null || info.m_disasterAI == null)
                {
                    Log.Warn("fire whirl " + disasterId +
                             " has no vortex vehicle and no DisasterInfo; leaving it to vanilla");
                    return true;   // nothing more we can do, so just stop tracking it
                }

                info.m_disasterAI.DeactivateNow(disasterId, ref disasters[disasterId]);
                Log.Info("fire whirl " + disasterId + " had no vortex vehicle; deactivated directly");
                return true;
            }
            catch (System.Exception e)
            {
                Log.Error("could not deactivate vehicle-less fire whirl " + disasterId, e);
                return true;   // do not let an exception make us re-enter every tick
            }
        }

        /// <summary>Drops whirls that vanilla has torn down from the registry.</summary>
        public static void CollectFinished()
        {
            var views = FireWhirlRegistry.Snapshot();
            var disasters = DisasterManager.instance.m_disasters.m_buffer;
            float cooldown = ModSettings.MaxLifetimeMinutes.value;

            for (int i = 0; i < views.Count; i++)
            {
                ushort d = views[i].DisasterId;

                // ★★ **A synthetic ID is not an index into the disaster buffer.** Go
                //    looking and you either run out of range or read somebody else's
                //    disaster. Folding these up is the job of
                //    <see cref="BeginEnding"/> and the lifetime side.
                if (IsSynthetic(d)) continue;

                if (d >= disasters.Length) { FireWhirlRegistry.Remove(d, cooldown); continue; }

                if ((disasters[d].m_flags & DisasterData.Flags.Created) == DisasterData.Flags.None)
                {
                    FireWhirlRegistry.Remove(d, cooldown);
                    Log.Info("fire whirl " + d + " collected");
                }
            }
        }
    }
}
