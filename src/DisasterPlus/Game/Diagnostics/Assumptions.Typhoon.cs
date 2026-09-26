using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="Assumptions"/> covering the ④ typhoon.
    ///
    /// **This file holds nothing but checks.** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> stay private on the main side, and
    /// because this is partial they can be used without raising a single visibility
    /// (that is exactly the requirement behind the split).
    ///
    /// The count is declared inside this file by <see cref="TyphoonCheckCount"/>.
    /// **If you add a check, bump it here too** — the main file's <c>TotalCheckCount</c> is
    /// the sum of these.
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>The number of checks this file holds.</summary>
        private const int TyphoonCheckCount = 9;

        private static void RunTyphoon()
        {
            // --- ④ typhoon (Task 2: the skeleton, prefab measurements) starts here ---

            // A check of the same character as ②'s EarthquakeAI entry: **the value can be
            // obtained at runtime and nowhere else**. The actual values of m_radius /
            // m_emergingDuration / m_activeDuration are not in the DLL (they are prefab
            // serialised values, IL findings document §A-0, PARTIAL), and ④'s storm-force
            // radius, its lifetime and **its travel speed** all rest on these three numbers
            // (TyphoonTrack.SpeedFor returns 0 when m_activeDuration is 0, and the caller then
            // starts no typhoon at all). If they cannot be read there is no breakwater other
            // than naming that fact.
            //
            // FAIL is the normal outcome in an environment without the DLC. The TornadoAI /
            // TsunamiAI entries already have the same character, and that is the established
            // treatment.
            // It is not taken out of the denominator — the typhoon feature itself depends on
            // the DLC, so naming it "unavailable" is the right thing to do.
            Check("ThunderStormAI disaster prefab exposes m_radius / m_emergingDuration / "
                  + "m_activeDuration, and m_radius / m_activeDuration are non-zero",
                  "no typhoon can be started at all: its radius, its lifetime and its travel "
                  + "speed are all derived from these three numbers, and the mod refuses to "
                  + "guess them. This also FAILs when the Natural Disasters DLC is not owned, "
                  + "which is expected.",
                  delegate
                  {
                      var t = typeof(ThunderStormAI);
                      if (!HasField(t, "m_radius", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // Use a pure, side-effect-free scan. TyphoonReader's internal cache is
                      // turned by the sim thread and must not be wound back from here on the
                      // main thread (the same reason as the EarthquakeAI entry).
                      //
                      // ★★ **Look at Usable, not StormResolved** (overall review C1).
                      //    StormResolved is set merely because "the prefab *object* was found
                      //    and the three fields were read" —
                      //    **it has not looked at one byte of the contents.** What actually
                      //    decides whether a typhoon may be started is
                      //    TyphoonPrefabFacts.Usable
                      //    (= StormResolved && StormRadius > 0 && ActiveDuration > 0), and
                      //    TyphoonController.Start refuses on that.
                      //    Leave this at Resolved and, in an environment where m_radius or
                      //    m_activeDuration deserialised as 0, **pressing the button would do
                      //    nothing while the dump and the settings screen both claim this one
                      //    check PASSed.**
                      //    Do not use "it could be read" in place of "it is usable".
                      return TyphoonReader.ScanPrefabFacts().Usable;
                  },
                  true);

            // --- ④ typhoon (Task 2) ends here ---

            // --- ④ typhoon (Task 3: the logical object and track following) starts here ---

            // ④'s movement mechanism itself. It moves the disaster by rewriting
            // DisasterData.m_targetPosition every sim tick (IL findings document §E-1. In this
            // task every stfld DisasterData::m_targetPosition across the whole assembly was
            // swept again, re-confirming that there is no vanilla code writing that on an
            // existing disaster).
            //
            // m_activationFrame is used as the lookout for trap 1 — if SelfTrigger is not in
            // effect, StartDisaster returns immediately and this value stays 0 (§A-1 IL_003F).
            // If this cannot be read the lookout does not stand at all, so it is checked in
            // the same entry.
            //
            // The methods are checked with their argument types specified (the same shape as
            // ②'s BuildingAI.CollapseBuilding check). Matching on the name alone yields a
            // false PASS when the signature changes.
            Check("DisasterData exposes m_targetPosition / m_angle / m_intensity / "
                  + "m_activationFrame, and DisasterAI.StartNow / DeactivateNow / "
                  + "ClampDisasterTarget are resolvable",
                  "the typhoon cannot be created, moved or stopped; the feature does nothing "
                  + "at all",
                  delegate
                  {
                      var d = typeof(DisasterData);
                      if (!HasField(d, "m_targetPosition", typeof(UnityEngine.Vector3))
                          || !HasField(d, "m_angle", typeof(float))
                          || !HasField(d, "m_intensity", typeof(byte))
                          || !HasField(d, "m_activationFrame", typeof(uint)))
                      {
                          return false;
                      }

                      var byRef = new Type[]
                      {
                          typeof(ushort), typeof(DisasterData).MakeByRefType()
                      };
                      if (typeof(DisasterAI).GetMethod("StartNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }
                      if (typeof(DisasterAI).GetMethod("DeactivateNow",
                              BindingFlags.Public | BindingFlags.Instance, null, byRef,
                              null) == null)
                      {
                          return false;
                      }

                      return typeof(DisasterAI).GetMethod("ClampDisasterTarget",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector3).MakeByRefType() },
                          null) != null;
                  });

            // --- ④ typhoon (Task 3) ends here ---

            // --- ④ typhoon (Task 4: driving the weather) starts here ---

            // The six fields by which ④ takes hold of the weather (§A-4). If any of them is
            // missing **no exception is raised**; the typhoon simply travels under a clear sky.
            //
            // m_forceWeatherOn alone is different in character. Without it, m_targetRain /
            // Cloud / Fog are crushed to 0 on every step in the environment of a player who
            // has weather switched off (the branch at IL_053D). That breaks in the way that is
            // hardest of all to get reported — "quietly does nothing, but only in some
            // environments" — so it is checked by name.
            Check("WeatherManager exposes m_targetRain / m_targetCloud / m_targetFog / "
                  + "m_targetDirection / m_forceWeatherOn / m_enableWeather",
                  "the typhoon cannot drive the weather; it would move across the map under "
                  + "a clear sky",
                  delegate
                  {
                      var w = typeof(WeatherManager);
                      return HasField(w, "m_targetRain", typeof(float))
                             && HasField(w, "m_targetCloud", typeof(float))
                             && HasField(w, "m_targetFog", typeof(float))
                             && HasField(w, "m_targetDirection", typeof(float))
                             && HasField(w, "m_forceWeatherOn", typeof(float))
                             && HasField(w, "m_enableWeather", typeof(bool));
                  });

            // --- ④ typhoon (Task 4) ends here ---

            // --- ④ typhoon (Task 6: lightning) starts here ---

            // Lightning is **substantial**: it causes BurnBuilding / BurnTree /
            // CollapseSegment (IL findings document §A-3). If this method cannot be resolved
            // the typhoon carries not a single bolt, but **no exception is raised, the storm
            // moves and the weather goes on being driven** — it just looks like "a typhoon
            // with not much lightning", and nothing else points at the cause.
            //
            // Check with the argument types specified (a one-argument
            // QueueLightningStrike(uint) exists separately, so matching on the name alone
            // gives a false PASS).
            Check("WeatherManager.QueueLightningStrike(uint, Vector3, Quaternion, "
                  + "InstanceManager.Group) is resolvable",
                  "the typhoon carries no lightning; the storm still moves and drives the "
                  + "weather",
                  delegate
                  {
                      return typeof(WeatherManager).GetMethod("QueueLightningStrike",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(uint), typeof(UnityEngine.Vector3),
                              typeof(UnityEngine.Quaternion), typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④ typhoon (Task 6) ends here ---

            // --- ④ typhoon (Task 7: wind damage) starts here ---

            // The three paths for wind damage. **If any is missing, no exception is raised** —
            // the typhoon simply passes over without a single building falling.
            //
            // CollapseBuilding is checked in the same shape as the six-argument version ②
            // already checks. The argument order of AddWind / DestroyTrees was measured from
            // the IL in Task 7 Step 1, and confirmed to match the order §B-1 had derived from
            // DestroyStuff's forwarding:
            //
            //   public static void AddWind(Vector3, float, Vector3, float, float,
            //                              InstanceManager.Group)
            //   public static void DestroyTrees(int, InstanceManager.Group, Vector3,
            //                                   float, float, float, float, float, float)
            //
            // ★ Do not FAIL this check when only DestroyTrees cannot be resolved.
            //   Wind damage proper (the collapses and AddWind) still works, so a FAIL would
            //   cry wolf. The fact that felling trees was given up is stated by TyphoonWind
            //   through FeatureHost.NoteDegraded.
            //
            // ★★ **But: do not claim in the name something you have not looked at**
            //    (overall review). This check used to call itself
            //    "AddWind / DestroyTrees are reachable" while never once looking
            //    DestroyTrees up — a PASS that asserted something about a thing it had never
            //    examined. It does not count towards pass/fail, but it **is actually looked up
            //    and the result written into the name**. The name is assembled before being
            //    passed to Check (AssumptionResult only displays Name, so this is the only
            //    outlet).
            Check("BuildingAI.CollapseBuilding is resolvable and DisasterHelpers.AddWind is "
                  + "reachable (DisasterHelpers.DestroyTrees: "
                  + (DestroyTreesIsReachable()
                        ? "reachable"
                        : "MISSING - the typhoon fells no trees; the rest of the wind sweep runs")
                  + ")",
                  "wind damage cannot be applied. The typhoon still moves, drives the weather "
                  + "and drops lightning; the mod disables the wind sweep rather than reaching "
                  + "for DisasterHelpers.DestroyBuildings, which Natural Disasters Renewal "
                  + "replaces wholesale",
                  delegate
                  {
                      if (typeof(BuildingAI).GetMethod("CollapseBuilding",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(ushort), typeof(Building).MakeByRefType(),
                                  typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                                  typeof(int)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      // If AddWind is absent that is a sign not merely about the visuals but
                      // that "the whole wind damage path is different", so this one does count
                      // towards a FAIL.
                      return typeof(DisasterHelpers).GetMethod("AddWind",
                          BindingFlags.Public | BindingFlags.Static, null,
                          new Type[]
                          {
                              typeof(UnityEngine.Vector3), typeof(float),
                              typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                              typeof(InstanceManager.Group)
                          },
                          null) != null;
                  });

            // --- ④ typhoon (Task 7) ends here ---

            // --- ④ typhoon (Task 8: river flooding) starts here ---

            // The path flooding reaches through. **If it cannot be resolved, TyphoonFlood
            // writes not one byte.**
            //
            // What was settled by measuring the IL in Task 8 Step 1:
            //   TerrainManager.WaterSimulation  … a public instance property
            //                                     (backed by a private m_waterSimulation)
            //   WaterSimulation.m_waterSources  … public FastList<WaterSource>
            //   LockWaterSource(ushort)         … public, returns m_buffer[source - 1]
            //                                     (**1-based**). Returns while still holding
            //                                     the Monitor
            //   UnlockWaterSource(ushort, WaterSource) … public, writes back then Monitor.Exit
            //   WaterSource                     … a public struct; m_type / m_target are
            //                                     public UInt16, TYPE_NATURAL = 1
            //
            // ★ The impact says that we do not run off to an alternative path. CreateWaterWave
            //   **does nothing at all** inland (a tsunami wave is only evaluated on the map
            //   border ring. §D-3(a)).
            Check("WaterSimulation is reachable and exposes m_waterSources / LockWaterSource / "
                  + "UnlockWaterSource, and WaterSource exposes m_type / m_target",
                  "river flooding cannot run. The mod does nothing rather than reaching for "
                  + "CreateWaterWave, which does nothing at all inland (the tsunami wave is "
                  + "only evaluated on the map border ring)",
                  delegate
                  {
                      var wsProperty = typeof(TerrainManager).GetProperty("WaterSimulation",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (wsProperty == null
                          || wsProperty.PropertyType != typeof(WaterSimulation))
                      {
                          return false;
                      }

                      // Check as far as it being a FastList<WaterSource> (confirmed by
                      // measurement).
                      if (!HasField(typeof(WaterSimulation), "m_waterSources",
                                    typeof(FastList<WaterSource>)))
                      {
                          return false;
                      }

                      if (typeof(WaterSimulation).GetMethod("LockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort) }, null) == null)
                      {
                          return false;
                      }
                      if (typeof(WaterSimulation).GetMethod("UnlockWaterSource",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[] { typeof(ushort), typeof(WaterSource) },
                              null) == null)
                      {
                          return false;
                      }

                      // ★ Check the types as well (overall review). The comment here had long
                      //    asserted "public UInt16", yet the check looked only at the names.
                      //    TyphoonFlood reads and writes them as ushort.
                      return HasField(typeof(WaterSource), "m_type", typeof(ushort))
                             && HasField(typeof(WaterSource), "m_target", typeof(ushort));
                  });

            // --- ④ typhoon (Task 8) ends here ---

            // --- ④ typhoon (tornado-strength local damage) starts here ---

            // ★★ **The two checks for the accompanying tornado (VortexAI's prefab values and
            //    the steering path through Vehicle.SetTargetPos) have gone from here.** The
            //    feature of borrowing vanilla's tornado disaster was itself retired, and
            //    tornado-strength damage is now produced by TyphoonGust itself, so both became
            //    "facts this mod does not depend on".
            //    Keep checking a dependency you do not use and nobody can answer what breaks
            //    when it FAILs.
            //
            // What the local damage actually gates on is the single
            // **BuildingAI.CollapseBuilding**, the same path as ④'s wind damage
            // (DisasterHelpers is never gone through at all = Natural Disasters Renewal's
            // patch surface is bypassed completely).
            // Check with the argument types specified — matching on the name alone gives a
            // false PASS when the signature changes.
            Check("BuildingAI.CollapseBuilding(ushort, ref Building, InstanceManager.Group, "
                  + "bool, bool, ushort) is resolvable",
                  "the typhoon's tornado-strength damage patches cannot collapse anything, "
                  + "and neither can its wind damage. Both call this method directly and "
                  + "never go through DisasterHelpers, which is what keeps them clear of "
                  + "Natural Disasters Renewal. The rain, the lightning, the flooding and "
                  + "the cloud are unaffected",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("CollapseBuilding",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(ushort), typeof(Building).MakeByRefType(),
                              typeof(InstanceManager.Group), typeof(bool), typeof(bool),
                              typeof(ushort)
                          },
                          null) != null;
                  });

            // --- ④ typhoon (tornado-strength local damage) ends here ---

            // --- ④ typhoon (Task 9: the giant rotating cloud) starts here ---

            // ④'s cloud is **entirely our own**. There is not one vanilla cloud that can be
            // reused (DisasterInfo.m_effect does not exist as a field at all. §C-1), and
            // vanilla's clouds are a sky dome with no world coordinates, so they cannot be
            // composited either (§C-2).
            // So whether the cloud lives or dies hangs on nothing but "can we build our own
            // material".
            //
            // ★ When this FAILs, ④ **draws nothing**. It does not take the escape route of
            //   borrowing a CS material — CS's shaders demand per-instance data supplied by
            //   the engine, so putting one on a hand-rolled DrawMesh comes out invisible or
            //   pitch black (VortexAI.RenderExtraStuff is the worked example. §C-1 / fire
            //   whirl §4.9).
            //   When a future game update changes a shader name, this names it rather than the
            //   cloud silently disappearing.
            //
            // ShaderPool is main thread only, but Run() is itself main thread only, so that is
            // fine. **The existence of DayNightDynamicCloudsProperties does not go in here** —
            // its absence is a legitimate environment (DLC, graphics settings) and has no
            // effect on ④'s own cloud (a FAIL would cry wolf).
            //
            // ★★ **Do not mix "Standard resolved" into the predicate** (overall review).
            //    This check used to end with `|| Shader.Find("Standard") != null`.
            //    Standard is a Unity built-in and was believed to resolve effectively always,
            //    so **this check could not FAIL in principle** (in the game Shader.Find
            //    returned null even for that Standard, so it did FAIL in the end, but a
            //    structural flaw is a flaw regardless).
            //    What this looks at, as with ⑤'s lava, is **"did something in the particle
            //    family resolve"**; if not, we draw with Standard forced into transparent mode
            //    (i.e. visible but not glowing).
            //    "Nothing resolved at all" shows up in the name.
            //
            // ★ **The predicate must be the expression ④ actually gates on.**
            //    So the ordering is not copied out here; it calls the **same** ShaderPool with
            //    the same preference as TyphoonCloud (copy the ordering and the name the check
            //    reports silently diverges from the shader actually used).
            ShaderPick cloudShader = ResolveCloudShader();
            Check("Graphics.DrawMesh(Mesh, Matrix4x4, Material, int, Camera, int, "
                  + "MaterialPropertyBlock, bool, bool) is reachable and an additive or "
                  + "alpha-blended particle shader resolves for the cloud, by name or by "
                  + "borrowing the shader off a loaded material (resolved: "
                  + cloudShader.Describe() + ")",
                  "the typhoon's own cloud falls back to the Standard shader forced into "
                  + "transparent mode, so it draws but does not glow; if nothing resolves at "
                  + "all it is not drawn. Every other part of the typhoon is unaffected. The "
                  + "mod draws nothing rather than borrowing a Cities material instance, which "
                  + "renders invisible or black in a hand-rolled DrawMesh - borrowing only the "
                  + "shader off such a material is a different thing and is what it does",
                  delegate
                  {
                      // ★ Look at **the nine-argument version we actually call**, not the
                      //   four-argument one. The four-argument version forwards
                      //   castShadows: true / receiveShadows: true, so ④ moved to the
                      //   nine-argument version that casts no shadow (see the TyphoonCloud
                      //   doc). Checking anything but the overload actually called is
                      //   meaningless.
                      if (typeof(UnityEngine.Graphics).GetMethod("DrawMesh",
                              BindingFlags.Public | BindingFlags.Static, null,
                              new Type[]
                              {
                                  typeof(UnityEngine.Mesh), typeof(UnityEngine.Matrix4x4),
                                  typeof(UnityEngine.Material), typeof(int),
                                  typeof(UnityEngine.Camera), typeof(int),
                                  typeof(UnityEngine.MaterialPropertyBlock),
                                  typeof(bool), typeof(bool)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      return cloudShader.Particle;
                  });

            // --- ④ typhoon (Task 9) ends here ---

            // --- ④ typhoon (building the vortex from cloud particles) starts here ---

            // The vortex's **main path** is to borrow a vanilla particle effect and scatter
            // cumulonimbus particles (TyphoonCloudFx). Whether it can be borrowed is only
            // knowable at runtime — EffectCollection registers 186 effects, but the steam
            // family (Factory Steam) is **not among them** (the 17 unregistered ones in §A-3
            // of the effects measurements document), and the runtime inventory can only be
            // confirmed through EffectsWrapper.m_BuiltinEffects (the result of
            // Resources.FindObjectsOfTypeAll) (§A-2 of the same, PARTIAL).
            //
            // ★ The predicate is **the expression this feature actually gates on**. Copy the
            //   way the asset is chosen out to here and the name reported silently diverges
            //   from the asset actually borrowed — so TyphoonCloudFx.Lookup is shared as-is
            //   (the same shape as the cloud shader check sharing ShaderPool). That one scores
            //   and chooses by **particle material name** rather than by a fixed name, so the
            //   name reported here is "whatever turned out to be the most cloud-like in this
            //   build".
            //
            // ★ A FAIL does not mean "the cloud disappears". It falls back to the old
            //   hand-rolled spiral mesh (whose viability is stated by the check just above).
            //   Not one other part of the typhoon stops.
            string borrowed;
            bool canBorrow = TyphoonCloudFx.CanBorrow(out borrowed);
            Check("EffectInfo.RenderEffect(InstanceID, SpawnArea, Vector3, float, float, "
                  + "float, float, CameraInfo) is reachable and a vanilla cloud-like "
                  + "ParticleEffect can be borrowed for the vortex (resolved: "
                  + (canBorrow ? borrowed : "none") + ")",
                  "the typhoon's vortex falls back to the mod's own spiral mesh, which is a "
                  + "flat ~900 m symbol of a vortex rather than a sky-filling canopy, and "
                  + "which needs a shader of its own. Every other part of the typhoon is "
                  + "unaffected.",
                  delegate
                  {
                      if (typeof(EffectInfo).GetMethod("RenderEffect",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(InstanceID), typeof(EffectInfo.SpawnArea),
                                  typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                                  typeof(float), typeof(float),
                                  typeof(RenderManager.CameraInfo)
                              },
                              null) == null)
                      {
                          return false;
                      }

                      return canBorrow;
                  });

            // --- ④ typhoon (building the vortex from cloud particles) ends here ---
        }

        /// <summary>
        /// The shader ④'s cloud actually uses. **It calls the same <c>ShaderPool</c> with the
        /// same preference as <c>TyphoonCloud.BuildMaterial</c>** — copy the ordering out to
        /// here and the name the check reports silently diverges from the shader actually used.
        /// Main thread only, but <see cref="Run"/> is itself main thread only, so that is fine.
        /// </summary>
        private static ShaderPick ResolveCloudShader()
        {
            // ★ **It try/catches for itself.** This is called outside Check() (i.e. outside
            //   that try/catch) in order to assemble the check's *name*. Assumptions.Run() is
            //   called bare from DisasterPlusLoading.OnLevelLoaded, so throwing from here
            //   would break the level load (DestroyTreesIsReachable has the same shape for the
            //   same reason).
            try
            {
                return ShaderPool.Resolve(ShaderPreference.AlphaBlended);
            }
            catch
            {
                // Fall to the "did not resolve" side. The check then FAILs too, so this never
                // amounts to quietly emitting a PASS.
                return new ShaderPick(null, false, false, false);
            }
        }

        /// <summary>
        /// Whether <c>DisasterHelpers.DestroyTrees</c> resolves, argument types included.
        /// **Not used for pass/fail** (FAILing wind damage in an environment where only the
        /// tree felling is unavailable would cry wolf). It is looked up solely to write the
        /// fact into the check's name.
        /// The argument order is the same as measured from the IL in Task 7 Step 1.
        /// </summary>
        private static bool DestroyTreesIsReachable()
        {
            try
            {
                return typeof(DisasterHelpers).GetMethod("DestroyTrees",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new Type[]
                    {
                        typeof(int), typeof(InstanceManager.Group), typeof(UnityEngine.Vector3),
                        typeof(float), typeof(float), typeof(float), typeof(float),
                        typeof(float), typeof(float)
                    },
                    null) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
