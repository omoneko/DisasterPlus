using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="Assumptions"/> covering the ② earthquake.
    ///
    /// **This file holds nothing but checks.** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> stay private on the main side, and
    /// because this is partial they can be used without raising a single visibility
    /// (that is exactly the requirement behind the split).
    ///
    /// The count is declared inside this file by <see cref="EarthquakeCheckCount"/>.
    /// **If you add a check, bump it here too** — the main file's <c>TotalCheckCount</c> is
    /// the sum of these.
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>The number of checks this file holds.</summary>
        private const int EarthquakeCheckCount = 11;

        private static void RunEarthquake()
        {
            // --- ② earthquake (Task 3) starts here ---

            // The only assumption in this project whose value can be obtained at runtime and
            // nowhere else. The actual values of m_crackLength / m_crackWidth /
            // m_emergingDuration / m_activeDuration are not in the DLL (they are prefab
            // serialised values, IL findings document §A-0), and reading them out of
            // sharedassets with UnityPy has failed too. Every duration ② designs from here on
            // rests on these four numbers, so if they cannot be read there is no breakwater
            // other than naming that fact.
            //
            // FAIL is the normal outcome in an environment without the DLC. The TornadoAI
            // entry already has the same character, and that is the established treatment.
            // It is not taken out of the denominator (no ReportSliderNotApplicable approach
            // here) — the earthquake feature itself depends on the DLC, so naming it
            // "unavailable" is the right thing to do.
            Check("EarthquakeAI disaster prefab exposes its four tuning fields and "
                  + "m_activeDuration is non-zero",
                  "no earthquake durations or fault geometry can be read; every duration in this "
                  + "feature is designed on top of these four numbers. This also FAILs when the "
                  + "Natural Disasters DLC is not owned, which is expected.",
                  delegate
                  {
                      var t = typeof(EarthquakeAI);
                      if (!HasField(t, "m_crackLength", typeof(float))
                          || !HasField(t, "m_crackWidth", typeof(float))
                          || !HasField(t, "m_emergingDuration", typeof(uint))
                          || !HasField(t, "m_activeDuration", typeof(uint)))
                      {
                          return false;
                      }
                      // Use a pure, side-effect-free scan. EarthquakeReader's internal cache
                      // is turned by the sim thread and must not be wound back from here on
                      // the main thread (the same reason as
                      // FireWhirlSpawner.HasTornadoPrefab).
                      //
                      // ★★ **Do not look at Resolved alone** (the third such case found in
                      //    the audit for overall review C1). Resolved is set merely because
                      //    "the object was found and the four values were read"; it
                      //    **has not looked at one byte of the contents**. What actually
                      //    decides ②'s behaviour are the four gates of the form
                      //    `Resolved || ActiveDuration == 0u` (CameraShakeBooster /
                      //    EarthquakeFeature / EarthquakePanel / EarthquakeSensorRows).
                      //    With m_activeDuration at 0, the waveform, the camera correction
                      //    and the seismographs all stop silently, and yet this check alone
                      //    was emitting a PASS.
                      //
                      //    m_crackLength / m_crackWidth are **not part of the gate**.
                      //    Those already have a path where the display side says "unknown"
                      //    when they are 0 (EarthquakeFeature's "fault (L/W)" row), so they
                      //    are not values that break silently.
                      var quake = EarthquakeReader.ScanPrefabFacts();
                      return quake.Resolved && quake.ActiveDuration > 0u;
                  },
                  true);

            Check("DisasterData exposes m_intensity / m_activationFrame / m_startFrame / m_angle",
                  "neither the shaking strength nor the per-building margin can be shown",
                  delegate
                  {
                      var t = typeof(DisasterData);
                      return HasField(t, "m_intensity", typeof(byte))
                          && HasField(t, "m_activationFrame", typeof(uint))
                          && HasField(t, "m_startFrame", typeof(uint))
                          && HasField(t, "m_angle", typeof(float));
                  });

            // Enum members are checked by string. A direct reference in code is folded down
            // to an integer at compile time, so a rename cannot be detected (the same reason
            // as the SubInfoMode check).
            Check("ImmaterialResourceManager.Resource.EarthquakeCoverage exists and "
                  + "CheckLocalResource is resolvable",
                  "seismograph coverage cannot be read, so the mod cannot explain why the hazard map is empty",
                  delegate
                  {
                      if (!Enum.IsDefined(typeof(ImmaterialResourceManager.Resource), "EarthquakeCoverage"))
                      {
                          return false;
                      }
                      return typeof(ImmaterialResourceManager).GetMethod("CheckLocalResource",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ImmaterialResourceManager.Resource),
                              typeof(UnityEngine.Vector3),
                              typeof(int).MakeByRefType()
                          },
                          null) != null;
                  });

            // The sim thread's clock. If this cannot be resolved, all that is left is
            // m_currentDayTimeHour, which the main thread writes — that crosses the thread
            // boundary, and on top of that it is a different quantity, derived from
            // m_referenceFrameIndex (the render-interpolation side) (§F-1).
            //
            // Note that m_enableDayNight being false is not itself a broken assumption
            // (it is a legitimate choice the player can make, and hour is simply pinned at
            // 12.0). Making it a FAIL here would be a false FAIL, so that fact is stated on
            // the "sim clock" row of the diagnostic dump and in the panel.
            Check("SimulationManager exposes m_dayTimeFrame / DAYTIME_FRAME_TO_HOUR / m_enableDayNight",
                  "the sim-thread clock cannot be read; the mod would have to fall back to "
                  + "m_currentDayTimeHour, which is written by the main thread",
                  delegate
                  {
                      var t = typeof(SimulationManager);
                      return HasField(t, "m_dayTimeFrame", typeof(uint))
                          && HasField(t, "m_enableDayNight", typeof(bool))
                          && HasStaticField(t, "DAYTIME_FRAME_TO_HOUR", typeof(float));
                  });

            // --- ② earthquake (Task 3) ends here ---

            // --- ② earthquake (Task 4) starts here ---

            // Both switching to the earthquake hazard view and the side that paints anything
            // on to it. If this FAILs there is nowhere left to show "why the hazard map is
            // empty" — the single most important explanation this feature produces.
            // Enum members are checked by string (a direct reference in code is folded down
            // to an integer at compile time, so a rename cannot be detected).
            Check("SubInfoMode.EarthquakeHazard exists and EarthquakeAI.UpdateHazardMap exists",
                  "the earthquake hazard heatmap cannot be shown, so the mod cannot explain "
                  + "the Located gate",
                  delegate
                  {
                      return Enum.IsDefined(typeof(InfoManager.SubInfoMode), "EarthquakeHazard")
                          && HasUpdateHazardMap(typeof(EarthquakeAI));
                  });

            // --- ② earthquake (Task 4) ends here ---

            // --- ② earthquake (Task 5) starts here ---

            // **This is the only check that actually guarantees Task 1's bit-for-bit match.**
            // Every per-building collapse verdict rests on the reproduction in
            // VanillaRandomizer, and if a single bit is off nothing breaks — plausible numbers
            // go on appearing while every assertion in the panel becomes a lie. Unit tests can
            // only catch a deviation from the definition of the LCG (they cannot reference the
            // real DLL), so agreement with the game itself can only be seen here.
            Check("VanillaRandomizer reproduces ColossalFramework.Math.Randomizer bit for bit",
                  "every per-building collapse verdict is wrong; the panel would keep showing "
                  + "plausible numbers that do not match what the game draws",
                  delegate
                  {
                      // Run the real one and our own implementation side by side. Look not
                      // just at the bits but at "the order of the draws" (always draw at
                      // least twice, so this check catches a breakage that is off by one).
                      // Randomizer is a struct, so always put it in a local variable
                      // (call it through a property or a field and a copy advances instead,
                      // so the sequences diverge).
                      int[] seeds = { 0, 1, -1, 12345, 0x00070000 | 1234, int.MinValue, int.MaxValue };
                      for (int i = 0; i < seeds.Length; i++)
                      {
                          var real = new ColossalFramework.Math.Randomizer(seeds[i]);
                          var ours = new DisasterPlus.Core.Earthquake.VanillaRandomizer(seeds[i]);
                          for (int k = 0; k < 4; k++)
                          {
                              if (real.Int32(10000u) != ours.Int32(10000u)) return false;
                          }
                      }
                      return true;
                  });

            // --- ② earthquake (Task 5) ends here ---

            // --- ② earthquake (Task 6) starts here ---

            // The camera-shake correction stands on nothing but these two public fields.
            // Both are assumed to be touchable without Harmony (§A-7), and if either is made
            // non-public, renamed or retyped, CameraShakeBooster throws once and then quietly
            // adds nothing — and since **an addition of 0 is the normal state at intensity
            // 55**, you cannot tell from the screen that the feature is dead.
            //
            // m_disableCameraShake is the heavier of the two. If it cannot be read we would be
            // adding shake in defiance of the player's explicit choice of "do not shake", so
            // CameraShakeBooster falls to the **add nothing** side when it cannot be read.
            Check("CameraController.m_cameraShake and DisasterManager.m_disableCameraShake "
                  + "are public fields",
                  "camera shake cannot be scaled with intensity and distance, and the mod cannot "
                  + "honour the player's \"disable camera shake\" choice",
                  delegate
                  {
                      var shake = typeof(CameraController).GetField("m_cameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (shake == null || shake.FieldType != typeof(UnityEngine.Vector3)) return false;

                      var disable = typeof(DisasterManager).GetField("m_disableCameraShake",
                          BindingFlags.Public | BindingFlags.Instance);
                      return disable != null && disable.FieldType == typeof(bool);
                  });

            // --- ② earthquake (Task 6) ends here ---

            // --- ② earthquake (the intensity distribution map overlay) starts here ---

            // **This feature rests entirely on these four APIs.**
            // If even one of them disappears, the overlay throws once and then quietly draws
            // nothing — and "draws nothing" is indistinguishable from "there is no earthquake"
            // and "the toggle is OFF".
            //
            // Measured from the IL (disassembled and confirmed by hand when this feature was
            // started):
            //   RenderManager::RegisterRenderableManager  public static, just Adds to m_renderables
            //   OverlayEffect::OnPostRender  IL_00A3  → RenderManager::Managers_RenderOverlay
            //   Managers_RenderOverlay       IL_0050  → each IRenderableManager::EndOverlay
            //   OverlayEffect::DrawCircle / DrawQuad → DrawEffect → Graphics::DrawMeshNow (immediate draw)
            //
            // These are methods rather than enum members, so match on the argument types as
            // well (to avoid GetMethod(name) throwing AmbiguousMatchException and producing a
            // false FAIL once an overload is added).
            Check("RenderManager overlay drawing API is reachable "
                  + "(RegisterRenderableManager / OverlayEffect.DrawCircle / DrawQuad)",
                  "the earthquake intensity distribution cannot be drawn on the map at all; "
                  + "the panel would keep offering a toggle that does nothing",
                  delegate
                  {
                      if (typeof(RenderManager).GetMethod("RegisterRenderableManager",
                              BindingFlags.Public | BindingFlags.Static,
                              null, new Type[] { typeof(IRenderableManager) }, null) == null)
                      {
                          return false;
                      }

                      var effect = typeof(RenderManager).GetProperty("OverlayEffect",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (effect == null || effect.PropertyType != typeof(OverlayEffect)) return false;

                      if (typeof(OverlayEffect).GetMethod("DrawCircle",
                              BindingFlags.Public | BindingFlags.Instance, null,
                              new Type[]
                              {
                                  typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                                  typeof(UnityEngine.Vector3), typeof(float), typeof(float),
                                  typeof(float), typeof(bool), typeof(bool)
                              }, null) == null)
                      {
                          return false;
                      }

                      return typeof(OverlayEffect).GetMethod("DrawQuad",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[]
                          {
                              typeof(RenderManager.CameraInfo), typeof(UnityEngine.Color),
                              typeof(ColossalFramework.Math.Quad3), typeof(float), typeof(float),
                              typeof(bool), typeof(bool)
                          }, null) != null;
                  });

            // --- ② earthquake (the intensity distribution map overlay) ends here ---

            // --- ② earthquake (Task 9: second layer — the tsunami chain from an undersea
            //     epicentre) starts here ---

            // **FAIL is the normal outcome here in an environment without the DLC.** The
            // *type* TsunamiAI ships inside Assembly-CSharp whether or not the DLC is owned,
            // so a type-existence check would pass regardless. What decides whether it really
            // exists is whether PrefabCollection holds a DisasterInfo with a TsunamiAI (§B-5),
            // and ModCompat.NaturalDisastersOwned is no more than an advance check on whether
            // to show the UI.
            // Without writing that expectation into the impact sentence, a FAIL in a perfectly
            // normal environment looks like a fault.
            //
            // The scan uses a pure, side-effect-free query (the same reason as
            // FireWhirlSpawner.HasTornadoPrefab. This is the main thread, and the sim thread's
            // cache must not be wound back).
            Check("TsunamiAI disaster prefab is available",
                  "the tsunami chain cannot run (this also FAILs when the Natural Disasters DLC "
                  + "is not owned, which is expected)",
                  delegate { return TsunamiChain.HasTsunamiPrefab(); },
                  true);

            // The entry and exit of the tsunami chain. Without HasWater resolving we cannot
            // decide "is the epicentre under water", and without reading m_waveIndex we cannot
            // decide "did a wave actually rise" — and if the latter cannot be read, an inland
            // map's perfectly normal "nothing happens" is mistaken for "we think we raised one".
            //
            // Match on the argument types as well (there are two overloads, and looking
            // GetMethod up by name alone gives an AmbiguousMatchException and a false FAIL.
            // Measured: HasWater(Vector2) and HasWater(Segment2, float, bool)).
            Check("TerrainManager.HasWater is resolvable and DisasterData exposes m_waveIndex",
                  "the mod cannot tell whether the epicentre is under water, nor whether a wave "
                  + "was actually raised",
                  delegate
                  {
                      var hasWater = typeof(TerrainManager).GetMethod("HasWater",
                          BindingFlags.Public | BindingFlags.Instance, null,
                          new Type[] { typeof(UnityEngine.Vector2) }, null);
                      if (hasWater == null || hasWater.ReturnType != typeof(bool)) return false;

                      var wave = typeof(DisasterData).GetField("m_waveIndex",
                          BindingFlags.Public | BindingFlags.Instance);
                      return wave != null && wave.FieldType == typeof(ushort);
                  });

            // --- ② earthquake (Task 9) ends here ---

            // --- ② earthquake (Task 10: second layer — long-period ground motion) starts here ---

            // **This feature really does destroy buildings.** So it does not permit "quietly
            // different behaviour" when an assumption breaks. Two things are checked:
            //
            //   1. Whether BuildingAI.CollapseBuilding resolves, argument types included.
            //      Not going through DisasterHelpers is the crux of avoiding NDR (§E-2), so
            //      name whether the detour's destination itself has disappeared.
            //   2. Whether a building's height can be read. **The Building struct has no
            //      height field** (measured from the IL. All it has is m_baseHeight /
            //      m_width / m_length).
            //      The height is the y of BuildingInfo.m_size (Vector3, m) on the prefab side,
            //      which InitializePrefab fills from m_generatedInfo.m_size (IL_09BE).
            //      That the unit is metres is settled by
            //      `waterLevel > m_position.y + Max(4f, m_collisionHeight)` in
            //      CommonBuildingAI.CollapseIfFlooded (m_collisionHeight starts from m_size.y.
            //      The full IL is in the BuildingHeight class doc).
            //
            //      ★ **Do not look at m_collisionHeight** (second-layer review I2). Over
            //      there, CheckReferences folds in the tops of the props and trees on the plot
            //      with Mathf.Max, so a single-storey building claims 20 m or more. Confirming
            //      that something is readable is meaningless unless it is the field actually
            //      used.
            //
            // When this FAILs, LongPeriodDamage does **nothing** (it does not destroy
            // buildings using a guessed height), so the impact sentence says as much.
            Check("BuildingAI.CollapseBuilding is resolvable and building height can be read",
                  "long-period damage cannot be applied; the feature disables itself rather than "
                  + "guessing a height",
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

                      var size = typeof(BuildingInfo).GetField("m_size",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (size == null || size.FieldType != typeof(UnityEngine.Vector3))
                      {
                          return false;
                      }

                      // The very source the fallback path (BuildingHeight.MetresOf) uses.
                      var generated = typeof(BuildingInfo).GetField("m_generatedInfo",
                          BindingFlags.Public | BindingFlags.Instance);
                      return generated != null
                             && typeof(BuildingInfoGen).IsAssignableFrom(generated.FieldType);
                  });

            // --- ② earthquake (Task 10) ends here ---
        }
    }
}
