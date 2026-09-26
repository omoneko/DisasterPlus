using DisasterPlus.Core.Common;
using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Holds the vortex vehicle in place.
    ///
    /// The order inside VortexAI.SimulationStep (the 6-argument one), as established by
    /// reading the IL (design document, appendix A-1):
    ///   1. write Frame.m_position
    ///   2. measure the distance to m_targetPos0
    ///   3. if ArriveAtDestination is true, DeactivateNow + Unspawn
    ///   4. recompute Frame.m_velocity
    ///   5. Frame.m_position = m_position + m_velocity * dt   ← the actual movement
    ///   6. AddWind / DestroyStuff / BurnGround / UpgradeBuildings
    ///
    /// Put m_position back to the spawn point in a Postfix and step 5's accumulation is
    /// cancelled out every step.
    ///
    /// We do not touch m_velocity. Rewriting it is pointless because step 4 recomputes it
    /// every step, and step 6's AddWind uses it for direction, so zeroing it would kill
    /// the wind effect.
    /// </summary>
    [HarmonyPatch(typeof(VortexAI), "SimulationStep",
        new[] { typeof(ushort), typeof(Vehicle), typeof(Vehicle.Frame),
                typeof(ushort), typeof(Vehicle), typeof(int) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref,
                ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class VortexPinPatch
    {
        public static void Postfix(ushort vehicleID, ref Vehicle.Frame frameData)
        {
            // Pin only the vortices we created. Never touch vanilla's tornadoes.
            Vec3 center;
            if (!FireWhirlRegistry.TryGetPinnedCenter(vehicleID, out center)) return;

            // We do not write y back. Vanilla already fits it to the terrain with
            // SampleRawHeightSmoothWithWater, so pinning the horizontal position alone
            // keeps it from floating above the ground or sinking into it.
            frameData.m_position = new Vector3(center.X, frameData.m_position.y, center.Z);
        }
    }
}
