namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of <see cref="Assumptions"/> covering the ⑤ volcano.
    ///
    /// **This file holds nothing but checks.** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> stay private on the main side, and
    /// because this is partial they can be used without raising a single visibility
    /// (that is exactly the requirement behind the split).
    ///
    /// The count is declared inside this file by <see cref="VolcanoCheckCount"/>.
    /// **If you add a check, bump it here too** — the main file's <c>TotalCheckCount</c> is
    /// the sum of these.
    ///
    /// ── ★ About the predicates of these three (the flaw ④'s review and ②'s audit found) ─────
    ///
    /// In ④ there was a case where "a check that was supposed to catch an unmeasured prefab
    /// value PASSed in exactly that case". The cause was that the predicate looked at "did the
    /// field resolve" and **not at "is the value usable", which is what the feature actually
    /// gates on**. The same shape remained in ② as well.
    ///
    /// So ⑤'s three checks **take the expression the feature itself gates on and use it
    /// verbatim as the predicate**:
    ///
    /// <code>
    /// check 1 ← VolcanoTerrainFacts.Usable
    ///           (= HeightsResolved &amp;&amp; RawArrayLength == 1081^2 &amp;&amp; UpdateAreaResolved)
    /// check 2 ← BurnGroundResolved                    (T8's gate for the scorching)
    /// check 3 ← SlopeSampleResolved                   (T8's gate for the lava)
    /// </code>
    ///
    /// Check 1 in particular <b>looks at the length of the array as well</b>. Even if
    /// <c>RawHeights</c> resolves, if the length is not 1081² the index <c>z*1081 + x</c>
    /// points at a different cell and **an unrelated part of the map rises**. A predicate that
    /// only looks at "it resolved" emits a PASS there.
    ///
    /// The scan is delegated to <see cref="VolcanoReader.ScanTerrainFacts"/>. That one is
    /// **side-effect-free** and does not wind back the cache the sim thread is turning
    /// (<see cref="Run"/> is called from the main thread).
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>The number of checks this file holds.</summary>
        private const int VolcanoCheckCount = 9;

        private static void RunVolcano()
        {
            // --- ⑤ volcano (Task 2: the terrain API) starts here ---

            // ★ Scan only once. The three predicates look at that result.
            //   **It is called outside Check(), so it suppresses exceptions itself** (Run() is
            //   called bare from DisasterPlusLoading.OnLevelLoaded, so throwing from here
            //   would break the level load. ④'s FirstResolvableCloudShader has the same shape
            //   for the same reason).
            //   ScanTerrainFacts try/catches per item, but guard it twice.
            VolcanoTerrainFacts facts;
            try
            {
                facts = VolcanoReader.ScanTerrainFacts();
            }
            catch
            {
                // Every default is false = "could not read". All three then FAIL.
                facts = new VolcanoTerrainFacts();
            }

            // 1. The terrain write path. **The whole of ⑤ rests on this one check.**
            //
            //   The predicate is VolcanoTerrainFacts.Usable itself — not one character
            //   different from the expression VolcanoUplift (T6) uses to decide "may the
            //   mountain be raised". The measured length goes into the name because
            //   **when it is not 1081², this is the only place that number appears.**
            Check("TerrainManager.RawHeights is a ushort[1081^2] and "
                  + "TerrainModify.UpdateArea(int,int,int,int,bool,bool,bool) is resolvable "
                  + "(RawHeights: "
                  + (facts.HeightsResolved
                        ? facts.RawArrayLength + " cells"
                        : "unavailable")
                  + ")",
                  "no volcano can be built at all: raising the ground and publishing the "
                  + "change are the two calls the whole feature rests on",
                  delegate { return facts.Usable; });

            // 2. The lava's scorching. **The mountain, the crater and the lava's advance all
            //   work without it**, so the impact says as much (do not cry wolf).
            //   The predicate is the one thing T8's scorching actually gates on. There is no
            //   DLC gate (§B-7b), so expectedWithoutDlc is not set — set it and a genuine
            //   absence would be lost among the "normal FAILs" in an environment without the
            //   DLC.
            //
            //   ★ This used to look at MakeCrater too. The crater became part of the height
            //     profile (Core/Volcano/VolcanoCrater), so that call no longer exists.
            Check("DisasterHelpers.BurnGround(Vector2,float,float) is resolvable",
                  "the ground is not scorched along the lava; the mountain, the crater and "
                  + "the lava still work",
                  delegate { return facts.BurnGroundResolved; });

            // 3. Slope sampling. **This is the lava's only gate** (T8).
            //   ★ Look at the three-argument version (out float slopeX, out float slopeZ),
            //     not the one-argument version. The one-argument version only returns the
            //     height, so even if that resolves the lava cannot find which way is downhill
            //     (§B-6). There are four overloads of the same name, so without specifying the
            //     argument types you grab the wrong one.
            Check("TerrainManager.SampleDetailHeight(Vector3, out float, out float) is resolvable",
                  "the lava cannot find its way downhill, so no lava flows at all. The "
                  + "mountain, the clearing and the eruption are unaffected",
                  delegate { return facts.SlopeSampleResolved; });

            // --- ⑤ volcano (Task 2: the terrain API) ends here ---

            // --- ⑤ volcano (Task 5: the clearing/destruction path) starts here ---

            // 4. The destruction path. **The predicate is the very expression VolcanoClearing
            //   actually gates on** (VolcanoDestructionFacts.Usable). Make "the field
            //   resolved" the predicate and a PASS comes out in an environment where the path
            //   is unusable (④'s review and ②'s audit found the same flaw).
            //
            //   ★ If this check FAILs, ⑤ builds no volcano at all. **This is not degraded** —
            //     raising the ground without clearing it first is not a degraded mode but
            //     precisely the failure design doc §1.2 discovered.
            //
            //   ScanFacts is a pure scan that does not touch the cache, so it is fine to call
            //   from the main thread (see its class doc). Suppressing exceptions outside Check
            //   is for the same reason as the three above (Run is called bare from the level
            //   load).
            VolcanoDestructionFacts destruction;
            try
            {
                destruction = VolcanoClearing.ScanFacts();
            }
            catch
            {
                destruction = new VolcanoDestructionFacts();
            }

            Check("BuildingAI.CollapseBuilding is resolvable and a road destruction path "
                  + "(NetAI.CollapseSegment / NetManager.ReleaseSegment) is reachable",
                  "the volcano refuses to start. Raising the ground without clearing it first "
                  + "is not a degraded mode: the game pins the terrain back to the height of "
                  + "every road and building on every update, so the mountain would come out "
                  + "full of flat trenches and bowls",
                  delegate { return destruction.Usable; });

            // --- ⑤ volcano (Task 5: the clearing/destruction path) ends here ---

            // --- ⑤ volcano (Task 7: the borrowed eruption effects) starts here ---

            // 5. The three vanilla particle effects that are borrowed (plume, flame, ejecta).
            //   **Even if this check FAILs the mountain grows, the lava flows and buildings
            //   burn** — the missing one simply is not drawn. Saying so in the impact sentence
            //   is how it avoids crying wolf (④ made the same call for DestroyTrees).
            //
            //   ★★ The predicate is the very expression VolcanoEruptionFx actually gates on.
            //     VolcanoVanillaFx **draws the original prefab as-is even if the copy fails**,
            //     so "did it resolve" matches "can it be drawn". Where those two do not match
            //     you get "the check passed but the feature does not work" (a shape that has
            //     come up twice in this project).
            //
            //   ★ None of these three needs a DLC. Natural Disasters' explosion and meteor
            //     look better, but they do not exist where the DLC is not owned, so they are
            //     never made the default path.
            //
            //   ScanFacts is main thread only (Run() is main too). As a side effect it may
            //   create a copy once, but that is the same single copy the rendering side would
            //   create, and VolcanoVanillaFx.Destroy folds it away on level unload.
            //   Suppressing exceptions outside Check is for the same reason as the four above.
            VolcanoVanillaFacts vanilla;
            try
            {
                vanilla = VolcanoVanillaFx.ScanFacts();
            }
            catch
            {
                vanilla = new VolcanoVanillaFacts();
            }

            Check("the game's own particle effects \"" + VolcanoVanillaFx.AshName + "\", \""
                  + VolcanoVanillaFx.FlameName + "\" and \"" + VolcanoVanillaFx.EjectaName
                  + "\" can be looked up and rendered (ash: "
                  + (vanilla.AshResolved ? "ok" : "missing")
                  + ", flames: " + (vanilla.FlameResolved ? "ok" : "missing")
                  + ", ejecta: " + (vanilla.EjectaResolved ? "ok" : "missing")
                  + ", cameraInfo: " + (vanilla.CameraInfoResolved ? "ok" : "missing") + ")",
                  "the missing piece of the eruption is simply not drawn. The mountain still "
                  + "rises, the lava still flows and it still sets buildings on fire. None of "
                  + "these effects needs a DLC",
                  delegate { return vanilla.EruptionUsable; });

            // --- ⑤ volcano (Task 7: the borrowed eruption effects) ends here ---

            // --- ⑤ volcano (the stand-in for a pyroclastic flow) starts here ---

            // 6. The band of dust running down the slope. **Vanilla has no pyroclastic flow
            //   effect at all**, so ⑤ sends the dust from a building collapse down the lava's
            //   path. Its success is decided separately from the three eruption effects, so
            //   the check is separate too (one being absent must not drag the other in).
            //
            //   ★ The predicate is the very expression VolcanoPyroclasticFx.Step actually
            //     gates on (VolcanoVanillaFacts.PyroclasticUsable).
            Check("the game's own particle effect \"" + VolcanoVanillaFx.DustName
                  + "\" can be looked up and rendered (dust: "
                  + (vanilla.DustResolved ? "ok" : "missing")
                  + ", cameraInfo: " + (vanilla.CameraInfoResolved ? "ok" : "missing") + ")",
                  "the dust surge that stands in for a pyroclastic flow is not drawn. Nothing "
                  + "else changes - that surge damages nothing, and the game has no real "
                  + "pyroclastic flow effect to fall back to",
                  delegate { return vanilla.PyroclasticUsable; });

            // --- ⑤ volcano (the stand-in for a pyroclastic flow) ends here ---

            // --- ⑤ volcano (Task 8: the lava's ignition path) starts here ---

            // 7. The three for ignition and water. **The lava itself flows and is drawn
            //   without them**, so the impact says as much.
            //
            //   ★ **TreeManager.BurnTree is not part of this check.** It always returning
            //     false without ND is normal (the SupportsExpansion gate in §B-7c), and
            //     making it a FAIL would cry wolf. Owned/not owned is stated on one
            //     diagnostic line.
            //
            //   ★ The predicate is the resolution of the three methods VolcanoLava actually
            //     calls. The argument types are specified so as not to grab the wrong thing
            //     where there are same-named overloads, as with SampleDetailHeight (the shape
            //     ② established).
            Check("DisasterHelpers.BurnGround, BuildingAI.BurnBuilding and "
                  + "TerrainManager.HasWater are resolvable",
                  "the lava still flows and is still drawn, but it neither scorches the ground "
                  + "nor sets buildings on fire, and it does not stop at water",
                  delegate { return LavaIgnitionResolvable(); });

            // --- ⑤ volcano (Task 8: the lava's ignition path) ends here ---

            // --- ⑤ volcano (Task 9: the shader the lava is drawn with) starts here ---

            // 8. The shader for the lava surface.
            //
            //   ★★ **Do not mix "Standard resolved" into the predicate.** Standard is a Unity
            //     built-in and was therefore believed to be effectively always non-null, so a
            //     check of the form "… || Shader.Find("Standard") != null" was **structurally
            //     incapable of failing even once** (④'s review found exactly this. In the game
            //     Shader.Find returned null even for that Standard, but a structural flaw is a
            //     flaw regardless).
            //     What this looks at is **whether something in the particle family (additive /
            //     alpha-blended) resolved**; if not, we draw with Standard forced into
            //     transparent mode (i.e. visible but not glowing).
            //
            //   ★ What it actually resolved to goes into the name because, on a failure, that
            //     information appears nowhere else (the same treatment as T2's RawHeights
            //     count). **It also says whether it was borrowed** — an environment where
            //     Shader.Find worked and one where it was borrowed off a loaded material
            //     differ in what you would suspect next.
            //
            //   ★ The predicate is the very expression VolcanoLavaFx actually gates on
            //     (ScanShaderFacts calls the same ShaderPool as BuildMaterial).
            //     ShaderPool is main thread only, and Run() is main too.
            //     Suppressing exceptions outside Check is for the same reason as the seven
            //     above.
            VolcanoLavaShaderFacts lavaShader;
            try
            {
                lavaShader = VolcanoLavaFx.ScanShaderFacts();
            }
            catch
            {
                lavaShader = new VolcanoLavaShaderFacts(null, false, "NONE (the scan threw)");
            }

            Check("an additive or alpha-blended particle shader resolves for the lava surface, "
                  + "by name or by borrowing the shader off a loaded material (resolved: "
                  + (string.IsNullOrEmpty(lavaShader.ResolvedShaderName)
                        ? "nothing" : lavaShader.Detail) + ")",
                  "the lava surface falls back to the Standard shader forced into transparent "
                  + "mode, so it draws but does not glow; if nothing resolves at all it is not "
                  + "drawn. The lava still flows, still scorches the ground and still sets "
                  + "buildings on fire either way",
                  delegate { return lavaShader.ParticleShaderResolved; });

            // --- ⑤ volcano (Task 9: the shader the lava is drawn with) ends here ---

            // --- ⑤ volcano (the sound of the eruption) starts here ---

            // 9. The audio path. **Even if this check FAILs the eruption appears as usual**
            //   (only the sound goes), so the impact says so (do not cry wolf).
            //
            //   ★ The predicate is the very expression VolcanoEruptionAudio.Update actually
            //     gates on (VolcanoAudioFacts.Usable).
            //
            //   ★★ **Do not mix the presence of the bundled wav into the predicate.** The file
            //     is something the player can delete, and its absence is not "a game update
            //     broke an assumption". Mix it in and you would be declaring a broken
            //     assumption at someone who deleted it themselves.
            //     Put the measurement **in the name** instead — when there is no sound, "there
            //     is no path" and "there is no file" can only be told apart here.
            //
            //   ScanAudioFacts is a side-effect-free scan and main thread only (Run() is main
            //   too). It does not read the clip, so it brings no 6 MB of I/O into the level
            //   load. Suppressing exceptions outside Check is for the same reason as the seven
            //   above.
            VolcanoAudioFacts audio;
            try
            {
                audio = VolcanoEruptionAudio.ScanAudioFacts();
            }
            catch
            {
                audio = new VolcanoAudioFacts();
            }

            Check("AudioManager.EffectGroup and AddEvent(AudioGroup,AudioInfo,Vector3,Vector3,"
                  + "float,float,float,int) are reachable, and AudioClip.Create/SetData resolve "
                  + "(bundled wav: "
                  + (audio.FileFound ? audio.FileBytes + " bytes" : "NOT PRESENT")
                  + ")",
                  "the eruption is silent. Nothing else changes: the mountain, the plume and "
                  + "the lava never touch the audio path. This is also the only route through "
                  + "which the player's effect volume and mute reach the sound, so Disaster + "
                  + "does not fall back to playing it at a raw fixed volume",
                  delegate { return audio.Usable; });

            // --- ⑤ volcano (the sound of the eruption) ends here ---
        }

        /// <summary>
        /// Whether the three methods <see cref="VolcanoLava"/> calls can be resolved.
        /// **This is not one of ⑤'s gates** (the lava flows without them), so it is not rolled
        /// into a single expression like <c>VolcanoTerrainFacts.Usable</c> — roll it in and an
        /// environment where "the lava merely does not scorch" would stop the mountain too.
        /// </summary>
        private static bool LavaIgnitionResolvable()
        {
            try
            {
                bool burnGround = typeof(DisasterHelpers).GetMethod(
                    "BurnGround",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null,
                    new System.Type[]
                    {
                        typeof(UnityEngine.Vector2), typeof(float), typeof(float)
                    },
                    null) != null;

                bool burnBuilding = typeof(BuildingAI).GetMethod(
                    "BurnBuilding",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new System.Type[]
                    {
                        typeof(ushort), typeof(Building).MakeByRefType(),
                        typeof(InstanceManager.Group), typeof(bool)
                    },
                    null) != null;

                bool hasWater = typeof(TerrainManager).GetMethod(
                    "HasWater",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new System.Type[] { typeof(UnityEngine.Vector2) },
                    null) != null;

                return burnGround && burnBuilding && hasWater;
            }
            catch
            {
                return false;
            }
        }
    }
}
