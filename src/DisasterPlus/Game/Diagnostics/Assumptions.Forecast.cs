using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="Assumptions"/> covering the ① forecast tab.
    ///
    /// **This file holds nothing but checks.** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> stay private on the main side, and
    /// because this is partial they can be used without raising a single visibility
    /// (that is exactly the requirement behind the split).
    ///
    /// The count is declared inside this file by <see cref="ForecastCheckCount"/>.
    /// **If you add a check, bump it here too** — the main file's <c>TotalCheckCount</c> is
    /// the sum of these.
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>The number of checks this file holds.</summary>
        private const int ForecastCheckCount = 7;

        private static void RunForecast()
        {
            // --- ① forecast tab (Task 5) starts here ---

            Check("InfoManager.SetCurrentMode is resolvable",
                  "cannot switch to the vanilla disaster hazard heatmap view",
                  delegate
                  {
                      return typeof(InfoManager).GetMethod("SetCurrentMode",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[] { typeof(InfoManager.InfoMode), typeof(InfoManager.SubInfoMode) },
                          null) != null;
                  });

            Check("SubInfoMode.LightningHazard / TornadoHazard exist",
                  "lightning/tornado hazard display cannot be shown",
                  delegate
                  {
                      var t = typeof(InfoManager.SubInfoMode);
                      // Check by string. A direct reference to an enum member in code is
                      // folded down to an integer at compile time, so if the game later
                      // renames or removes it we could not detect that without a rebuild.
                      // This asks the question afresh at runtime: "does a member of that
                      // name actually exist in the game assembly as it is now?"
                      return Enum.IsDefined(t, "LightningHazard") && Enum.IsDefined(t, "TornadoHazard");
                  });

            // The impact sentence was corrected in the overall review. It used to say
            // "the hazard map does not get filled in = empty or stale data is shown", but
            // that explanation was wrong: it assumed a static risk surface that ought to be
            // filled in. Measured from the IL (re-confirmed with this very mod), both of
            // these UpdateHazardMap methods open with a two-stage gate
            //   (m_flags & 4096) == 0 -> return   … 4096 = DisasterData.Flags.Located
            //   (m_flags & 12)   == 0 -> return   … 12   = Emerging|Active
            // consult neither the terrain nor the buildings, and, only if they get past it,
            // paint a disc around m_targetPosition whose size follows the radius and the
            // intensity. So this map is not a static risk surface but "the predicted damage
            // area of a located, in-progress storm", and being empty is itself a normal
            // state (i.e. no storm is currently detected).
            // The impact only states what is lost if the method disappears.
            Check("ThunderStormAI/TornadoAI.UpdateHazardMap exist",
                  "a located, in-progress storm's predicted impact area cannot be painted, "
                  + "so the hazard map would stay empty even while a storm is detected",
                  delegate
                  {
                      return HasUpdateHazardMap(typeof(ThunderStormAI))
                          && HasUpdateHazardMap(typeof(TornadoAI));
                  });

            // Raised in the overall review (I5): this used to demand m_groundWetness /
            // m_lastLightningIntensity / m_targetDirection as well, but the mod reads none
            // of those three anywhere. An entry carrying an impact as heavy as "the core of
            // the forecast feature breaks" could FAIL purely because a field the feature
            // does not use got renamed — a layer built to remove false positives producing
            // false positives defeats its own purpose, so narrow it down to what
            // WeatherReader actually reads.
            // (The reads of m_groundWetness / m_lastLightningIntensity were removed
            //  altogether. m_currentFog / m_targetFog stay, since a new Fog row was added
            //  and they now feed the display.)
            Check("WeatherManager current/target fields are resolvable",
                  "trend cannot be computed (the core of the forecast feature)",
                  delegate
                  {
                      // Confirmed against the real game assembly that all of these are Single.
                      var t = typeof(WeatherManager);
                      return HasField(t, "m_currentRain", typeof(float))
                          && HasField(t, "m_targetRain", typeof(float))
                          && HasField(t, "m_currentCloud", typeof(float))
                          && HasField(t, "m_targetCloud", typeof(float))
                          && HasField(t, "m_currentFog", typeof(float))
                          && HasField(t, "m_targetFog", typeof(float))
                          && HasField(t, "m_currentTemperature", typeof(float))
                          && HasField(t, "m_targetTemperature", typeof(float))
                          && HasField(t, "m_windDirection", typeof(float));
                  });

            // What is needed to walk the located disasters (WeatherReader.CountLocatedStorms).
            // If this cannot be resolved, the grounds for saying "no storm is detected"
            // disappear, and the panel falls back to showing an all-zero grid as
            // "Lightning: 0" again.
            Check("DisasterManager.m_disasters exposes m_buffer / m_size",
                  "located storms cannot be counted, so an all-zero hazard grid would again be "
                  + "shown as a real '0' instead of 'no storm detected'",
                  delegate
                  {
                      var f = typeof(DisasterManager).GetField("m_disasters",
                          BindingFlags.Public | BindingFlags.Instance);
                      if (f == null) return false;
                      // Check as far as it being a FastList<DisasterData>. On the name alone
                      // this would still pass if the contents were swapped for a FastList of
                      // some other type.
                      return HasField(f.FieldType, "m_buffer", typeof(DisasterData[]))
                          && HasField(f.FieldType, "m_size", typeof(int));
                  });

            // Grid geometry. HazardMapReader's GridSize / WorldUnitsPerCell are written as
            // references to DisasterManager's consts, but a C# const is baked into the
            // caller at compile time, so inside the shipped DLL they are plain literals of
            // 256 / 38.4 (see the comment in HazardMapReader). "It follows along
            // automatically because it is a reference" therefore does not hold.
            // The only way to detect a mismatch without a rebuild in between is to read
            // **the metadata of the game currently loaded** at runtime.
            // GetRawConstantValue() takes the const's declared value straight from the
            // metadata, so it is a different path from the baked-in literal.
            Check("DisasterManager hazard grid geometry is 256 cells x 38.4 m",
                  "hazard values would be sampled from the wrong cell (the label would be right "
                  + "but the number would belong to somewhere else)",
                  delegate
                  {
                      var res = typeof(DisasterManager).GetField("HAZARDMAP_RESOLUTION",
                          BindingFlags.Public | BindingFlags.Static);
                      var cell = typeof(DisasterManager).GetField("HAZARDMAP_CELL_SIZE",
                          BindingFlags.Public | BindingFlags.Static);
                      if (res == null || cell == null) return false;
                      if (!res.IsLiteral || !cell.IsLiteral) return false;
                      return (int)res.GetRawConstantValue() == 256
                          && (float)cell.GetRawConstantValue() == 38.4f;
                  });

            // Carried over from the Task 4 review. HazardMapReader reads m_hazardAmount
            // directly via reflection (the public API only returns a Color).
            // If a game update renames or retypes this field, HazardMapReader itself does
            // not throw but quietly falls to 0 / ok=false, so unless it is named here the
            // failure is silent: "plausible-looking hazard numbers that are always 0" would
            // never show up in the ASSUMPTIONS summary at startup.
            Check("DisasterManager.m_hazardAmount is a private Byte[] field",
                  "hazard numbers may silently read wrong data if the game renames or retypes this field",
                  delegate
                  {
                      var f = typeof(DisasterManager).GetField("m_hazardAmount",
                          BindingFlags.NonPublic | BindingFlags.Instance);
                      return f != null && f.FieldType == typeof(byte[]);
                  });

            // --- ① forecast tab (Task 5) ends here ---
        }
    }
}
