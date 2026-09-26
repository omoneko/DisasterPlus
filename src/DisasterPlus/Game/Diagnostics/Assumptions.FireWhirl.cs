using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="Assumptions"/> covering the ③ fire whirl.
    ///
    /// **This file holds nothing but checks.** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> stay private on the main side, and
    /// because this is partial they can be used without raising a single visibility
    /// (that is exactly the requirement behind the split).
    ///
    /// The count is declared inside this file by <see cref="FireWhirlCheckCount"/>.
    /// **If you add a check, bump it here too** — the main file's <c>TotalCheckCount</c> is
    /// the sum of these.
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>The number of checks this file holds.</summary>
        private const int FireWhirlCheckCount = 4;

        private static void RunFireWhirl()
        {
            Check("VortexAI.SimulationStep(6) is patched",
                  "fire whirls will drift instead of staying put",
                  delegate { return HarmonyBootstrap.Installed && VortexStepIsPatched(); });

            Check("BuildingAI.BurnBuilding is resolvable",
                  "fire spread will not work",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("BurnBuilding",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ushort), typeof(Building).MakeByRefType(),
                              typeof(InstanceManager.Group), typeof(bool)
                          },
                          null) != null;
                  });

            // The flame shader.
            //
            // ★ **③ did not have this check.** ④ and ⑤ had the same check, so they could
            //   report "the shader cannot be resolved" cleanly as a FAIL; ③, having no check,
            //   instead fell over on new Material(null) every frame
            //   (6,938 lines of the same NullReferenceException in the log from the game).
            //
            // ★★ **Do not mix "Standard resolved" into the predicate** (the same discipline as
            //   ④⑤). What this looks at is **whether something in the particle family
            //   (additive / alpha-blended) resolved**; if not, we draw with Standard forced
            //   into transparent mode (i.e. visible but not glowing).
            //   If nothing at all resolves, nothing is drawn — and that shows up in the name.
            //
            // ★ The predicate is exactly the expression FireWhirlFlameFx actually gates on
            //   (the same ShaderPool called with the same preference. Copy the ordering over
            //   here and the name the check reports would silently diverge from the shader
            //   actually used).
            //   **⑤'s eruption plume uses the same preference, so this one check is the answer
            //   for that too.**
            ShaderPick flame = ResolveFlameShader();
            Check("an additive or alpha-blended particle shader resolves for the fire whirl "
                  + "flames, by name or by borrowing the shader off a loaded material "
                  + "(resolved: " + flame.Describe() + ")",
                  "the flames fall back to the Standard shader forced into transparent mode, so "
                  + "they draw but do not glow; if nothing resolves at all they are not drawn. "
                  + "The fire whirl still spins, stays pinned and still spreads fire either way",
                  delegate { return flame.Particle; });

            // FAIL is the normal outcome in an environment without the DLC (the prefab does
            // not exist at all).
            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created. This also FAILs when the Natural Disasters "
                  + "DLC is not owned, which is expected.",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); },
                  true);
        }

        /// <summary>
        /// The shader ③'s flames actually use. **It calls the same <c>ShaderPool</c> with the
        /// same preference as <c>FireWhirlFlameFx.FlameMaterial</c>.**
        ///
        /// ★ **It try/catches for itself.** This is called outside <c>Check()</c> (i.e.
        ///   outside that try/catch) in order to assemble the check's *name*.
        ///   <c>Assumptions.Run()</c> is called bare from
        ///   <c>DisasterPlusLoading.OnLevelLoaded</c>, so throwing from here would break the
        ///   level load (④'s <c>ResolveCloudShader</c> has the same shape for the same
        ///   reason).
        /// </summary>
        private static ShaderPick ResolveFlameShader()
        {
            try
            {
                return ShaderPool.Resolve(ShaderPreference.Additive);
            }
            catch
            {
                return new ShaderPick(null, false, false, false);
            }
        }

        private static bool VortexStepIsPatched()
        {
            // Check whether Harmony really holds this method.
            // If even one argument of [HarmonyPatch] disagrees with the real method, the patch
            // silently fails to land, the mod looks fine and only the tornado drifts away.
            var target = typeof(VortexAI).GetMethod("SimulationStep",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(ushort), typeof(Vehicle).MakeByRefType(),
                    typeof(Vehicle.Frame).MakeByRefType(), typeof(ushort),
                    typeof(Vehicle).MakeByRefType(), typeof(int)
                },
                null);
            if (target == null) return false;

            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            if (info == null || info.Postfixes == null) return false;

            // Check for "my postfix is on it", not "somebody's postfix is on it".
            // If another mod happened to have put a postfix on the same private overload, the
            // latter would mask our patch failing silently and still report PASS.
            for (int i = 0; i < info.Postfixes.Count; i++)
            {
                if (info.Postfixes[i].owner == HarmonyBootstrap.HarmonyId) return true;
            }
            return false;
        }
    }
}
