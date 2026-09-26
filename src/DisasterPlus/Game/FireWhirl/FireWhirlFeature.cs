using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ③ The fire whirl. Detects a dense fire, creates a vortex, holds it in place and
    /// scatters the fire around.
    ///
    /// ── ★★ A fire whirl is not something the player raises ────────────────
    ///
    /// The disaster panel used to have a tile for ③, and
    /// <c>FireWhirlPlacementTool</c> would force one to spawn wherever you clicked.
    /// **That route has been removed** (design document §5.3). This mod's position is
    /// that a fire whirl is a phenomenon that arises naturally as the **result** of a
    /// large fire, not something you can summon. So <see cref="TrySpawnNew"/> is now the
    /// only route by which one can occur.
    ///
    /// The price of having a single route is that the default state became "nothing
    /// happens", and in an in-game test that leads straight to "I cannot tell whether it
    /// is broken or whether there is simply not enough fire yet" (there is a report of a
    /// session where the only thing printed was <c>DIAG fireWhirl: burning=0 active=0</c>).
    /// So **the shortfall in the spawn condition itself goes into the diagnostics** —
    /// <see cref="DisasterPlus.Core.FireWhirl.FireWhirlProspect"/> is that value, noted
    /// into <see cref="_prospect"/> every tick and printed by
    /// <see cref="WriteDiagnostics"/>.
    /// </summary>
    public class FireWhirlFeature : IDisasterFeature
    {
        /// <summary>The key passed to FeatureHost.NoteDegraded. It must always be the same
        /// string as Name.</summary>
        public const string FeatureName = "FireWhirl";

        /// <summary>
        /// The keys identifying the Degraded states we report ourselves. One feature can
        /// be Degraded for several reasons, so this lets recovery clear only the ones it
        /// raised itself.
        /// </summary>
        private const string EndingStallNote = "endingStall";
        private const string BarrenSpreadNote = "barrenSpread";

        public string Name { get { return FeatureName; } }

        private readonly BurningBuildingScanner _scanner = new BurningBuildingScanner();

        /// <summary>The intensity is a byte. We start at 60, a middling tornado (displayed
        /// as 6.0).</summary>
        /// <summary>
        /// The disaster intensity used when creating the vortex.
        ///
        /// ── ★★ Tripled (2026-08-22, the owner's instruction "make the fire whirl's
        ///    tornado three times bigger") ─────────────────────────────────────
        ///
        /// <b>The vortex's apparent size is the intensity itself.</b> From the IL
        /// (<c>VortexAI.RenderExtraStuff</c>, IL_00C0-00C6):
        ///
        /// <code>
        ///   scale = DisasterData.m_intensity * 0.01454545      // = intensity / 68.75
        ///   ... combined with Mathf.Max(m_destructionRadiusMax, m_upgradeRadiusMax)
        /// </code>
        ///
        /// At 60 that came to only 0.87×. At 180 it is 2.62×, which is **exactly three
        /// times** as big.
        ///
        /// ★ <b>The destruction area does not triple.</b> The destruction radius is capped
        ///   by the prefab's own <c>m_destructionRadiusMin/Max</c>, so only the appearance
        ///   grows (the fire whirl's own damage is decided separately by
        ///   <c>FireWhirlDamage</c>).
        ///
        /// ★ The spawn position is a point <c>intensity × 10 + 400</c> = 2200 m away from
        ///   <c>targetPosition</c> (<c>TornadoAI.ActivateDisaster</c>). That sits inside
        ///   <c>FireWhirlPinner.MaxAttachDistance</c> (3000 m) —
        ///   <b>always check that one too when raising this.</b> Go past it and the vortex
        ///   never attaches, leaving an untrackable drifting tornado.
        ///
        /// ★ Do not exceed 255 (<c>m_intensity</c> is a byte).
        /// </summary>
        private const byte SpawnIntensityBase = 180;

        /// <summary>Whether we have reported a whirl whose ending never completes, even
        /// once. Keeps the log to a single line.</summary>
        private bool _endingStallLogged;

        /// <summary>
        /// How close the spawn condition was to being met, as seen by the most recent
        /// check. **Read and written only by the sim thread**
        /// (<see cref="OnSimulationTick"/> writes it and <c>WriteDiagnostics</c> reads it;
        /// both are on the sim thread, so no lock is needed).
        /// </summary>
        private FireWhirlProspect _prospect;

        /// <summary>Whether a check ran this tick. When false, <see cref="_prospect"/> is
        /// stale.</summary>
        private bool _prospectFresh;

        public void OnLevelLoaded()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            _endingStallLogged = false;
            _prospect = new FireWhirlProspect();
            _prospectFresh = false;
            HarmonyBootstrap.Install();

            // ★ There is no ToolRegistration.Register<...>() here. ③ has no placement
            //   tool, and there is no route by which a player raises a fire whirl (see the
            //   class doc).
            // DisasterPanelBar installs the buttons (FeatureHost calls it). There is no
            // tile for ③.
        }

        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            // Without the Natural Disasters DLC the tornado's DisasterInfo does not exist.
            // FindTornadoInfo warns each time it is called, so do not call it every tick
            // in a city without the DLC.
            if (!ModCompat.NaturalDisastersOwned) { _prospectFresh = false; return; }

            // ★★ **Take vanilla's tornado out of the random draw** (the owner's
            //    instruction). It is idempotent and writes nothing unless the state has
            //    changed. The prefabs are loaded after the level load, so we call it here
            //    rather than in OnLevelLoaded — right after a load LoadedCount can be 0.
            VanillaTornadoSuppressor.Apply(ModSettings.NoVanillaTornado.value);

            // The housekeeping (attaching, collecting the torn down, the cooldowns) always
            // runs, regardless of the setting. Gate it on the setting and the moment the
            // feature is switched off only the registry is left, giving an immortal vortex
            // that VortexPinPatch keeps pinning and FireWhirlFlameFx keeps drawing.
            FireWhirlPinner.AttachVehicles();
            FireWhirlPinner.CollectFinished();
            FireWhirlRegistry.AdvanceCooldowns(deltaMinutes);
            FireWhirlRegistry.AdvanceEnding(deltaMinutes);
            CheckEndingStall();

            if (!ModSettings.FireWhirlEnabled.value)
            {
                // Either it was switched off part-way through, or a save was loaded with
                // it off. Fold up any live whirls by putting them on vanilla's teardown
                // path.
                EndAllLiveWhirls();
                _prospectFresh = false;
                return;
            }

            _scanner.ScanSlice();

            var config = ModSettings.ToFireWhirlConfig();
            var burning = _scanner.Current;

            UpdateExisting(config, burning, deltaMinutes);
            TrySpawnNew(config, burning);

            FireWhirlDamage.Apply(frameIndex, deltaMinutes, config.SpreadStrength);

            // Catch, once only, the moment a run of "buildings are being selected but not
            // one catches" sets in. This is the only sign that the third IL error —
            // writing m_fireIntensity directly — has come back; the spread alone dies,
            // with no exception and no log line.
            if (FireWhirlDamage.ConsumeBarrenAlert())
            {
                Log.Warn("fire spread attempted ignition but lit nothing in "
                         + FireWhirlDamage.BarrenThreshold + " consecutive passes"
                         + " (last pass: candidates=" + FireWhirlDamage.LastCandidates
                         + " selected=" + FireWhirlDamage.LastSelected
                         + " attempted=" + FireWhirlDamage.LastAttempted + ")"
                         + "; BuildingAI.BurnBuilding is refusing every call");
                FeatureHost.NoteDegraded(FeatureName, BarrenSpreadNote,
                    "fire spread lit nothing in " + FireWhirlDamage.BarrenThreshold
                    + " consecutive passes");
            }

            // Withdraw the report once the spread comes back. Resetting only the streak on
            // the Core side would leave the overlay's badge stuck at Degraded, so the
            // "barren passes" line would be gone while the section stayed red — a
            // contradiction.
            if (FireWhirlDamage.ConsumeBarrenRecovery())
            {
                Log.Info("fire spread ignited again; clearing the barren-spread alert");
                FeatureHost.ClearDegraded(FeatureName, BarrenSpreadNote);
            }

            // ★ Make "nothing is happening" readable from a single line. Back when all we
            //   printed was burning=0 active=0, an in-game test could not tell whether
            //   that meant "there is not enough fire yet" or "it is broken" (see the class
            //   doc).
            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count
                + " densest=" + _prospect.DensestCount + "/" + _prospect.RequiredCount
                + " cooldown=" + FireWhirlRegistry.CoolingCount);
        }

        /// <summary>
        /// Detects whirls that entered the ending sequence and never finish.
        ///
        /// This is the exact signature of the m_targetPos0 mix-up coming back — the case
        /// where the ending can never fire in principle. An existence check for whether
        /// the patch is applied would pass, so the only way to see it is by whether it
        /// actually finished.
        /// </summary>
        private void CheckEndingStall()
        {
            int maxLifetime = ModSettings.MaxLifetimeMinutes.value;
            float framesPerMinute = FeatureHost.FramesPerMinute;
            var views = FireWhirlRegistry.Snapshot();

            for (int i = 0; i < views.Count; i++)
            {
                if (!views[i].Ending) continue;
                if (!EndingStall.IsStuck(views[i].EndingMinutes, maxLifetime, framesPerMinute)) continue;

                if (!_endingStallLogged)
                {
                    _endingStallLogged = true;
                    Log.Warn("fire whirl " + views[i].DisasterId + " has been ending for "
                             + views[i].EndingMinutes.ToString("F1") + " in-game minutes"
                             + " (threshold "
                             + EndingStall.ThresholdMinutes(maxLifetime, framesPerMinute)
                                 .ToString("F1")
                             + " min; a healthy teardown is "
                             + (EndingStall.TeardownFrames / framesPerMinute).ToString("F1")
                             + " min); vanilla teardown never completed");
                }

                FeatureHost.NoteDegraded(FeatureName, EndingStallNote,
                    "a fire whirl is stuck in the ending state");
                return;
            }

            // The stall has cleared (or the stalled whirl has been collected). Take the
            // badge down. Leave it up and the overlay stays Degraded while its body text
            // keeps saying there is not a single STUCK whirl — a contradiction.
            FeatureHost.ClearDegraded(FeatureName, EndingStallNote);
            _endingStallLogged = false;
        }

        /// <summary>Sends every live whirl into its ending sequence when the setting is
        /// turned off.</summary>
        private static void EndAllLiveWhirls()
        {
            var views = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Ending) continue;
                FireWhirlPinner.BeginEnding(views[i]);
            }
        }

        private void UpdateExisting(FireWhirlConfig config, IList<BurningBuilding> burning, float deltaMinutes)
        {
            var views = FireWhirlRegistry.Snapshot();
            float r2 = config.DetectRadius * config.DetectRadius;

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v.Ending) continue;

                // Is there still enough fire around this whirl to meet the condition?
                var centre2d = v.Center.ToVec2();
                int near = 0;
                for (int b = 0; b < burning.Count; b++)
                {
                    if (centre2d.DistanceSquaredTo(burning[b].Position) <= r2) near++;
                }
                // A manual spawn is exempt from the interrupt check on the spawn
                // condition. Being able to place one where there is no fire is the whole
                // point of a manual spawn; without the exemption it is counted as near=0
                // on the very next tick and dies after nothing but the grace period (the
                // absolute cap still applies as normal, in the Evaluate below).
                bool conditionMet = v.Manual || near >= config.DetectCount;

                // A manual spawn never shrinks below its initial scale. With no fire
                // around it, it would shrink to RadiusFor(0) and visibly get smaller on
                // the very frame after it spawned.
                int strengthCount = v.Manual && near < v.BurningCount ? v.BurningCount : near;

                FireWhirlRegistry.UpdateStrength(
                    v.DisasterId, FireWhirlStrength.RadiusFor(strengthCount), strengthCount);
                FireWhirlRegistry.AdvanceLife(v.DisasterId, deltaMinutes, conditionMet);
            }

            // The lifetime verdict is taken on the updated values.
            // The verdict itself belongs to Core's FireWhirlLifecycle.Evaluate, and since
            // Life never leaves the registry the evaluation goes through the registry too.
            var updated = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < updated.Count; i++)
            {
                if (updated[i].Ending) continue;
                if (FireWhirlRegistry.EvaluateVerdict(updated[i].DisasterId, config)
                    != FireWhirlVerdict.Dissipate) continue;

                FireWhirlPinner.BeginEnding(updated[i]);
            }
        }

        private void TrySpawnNew(FireWhirlConfig config, IList<BurningBuilding> burning)
        {
            // ★ Take the verdict and "why nothing appeared" from the same single pass.
            //   Count it again separately and the diagnostics disagree with the real
            //   verdict (see the class doc on FireWhirlProspect).
            var candidates = FireWhirlDetector.Detect(burning, config,
                                                      FireWhirlRegistry.Centers(), out _prospect);
            _prospectFresh = true;
            if (candidates.Count == 0) return;

            // At most one per tick. Stops them spawning in a chain and wiping out the
            // city in an instant.
            var c = candidates[0];

            float y = TerrainManager.instance.SampleDetailHeight(new Vector3(c.Center.X, 0f, c.Center.Z));
            var center = new Vec3(c.Center.X, y, c.Center.Z);

            ushort disasterId;
            if (!FireWhirlSpawner.TrySpawn(center, SpawnIntensityBase, out disasterId)) return;

            FireWhirlRegistry.Add(disasterId, 0, center,
                FireWhirlStrength.RadiusFor(c.BurningCount), c.BurningCount);
        }

        public void OnMainThreadUpdate()
        {
            FireWhirlFlameFx.Sync();

            // ★ ③ has no tile on the disaster panel (see the class doc). There is no
            //   longer any retry of the installation here, nor any row for ③ in
            //   DisasterPanelBar.
        }

        public void OnLevelUnloading()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            FireWhirlRegistry.Clear();
            FireWhirlFlameFx.Clear();
            HarmonyBootstrap.Uninstall();
            // ★ Only discard the notes; do not write the values back — the next load has
            //   DisasterManager.InitializeProperties recompute them (see its doc).
            VanillaTornadoSuppressor.Forget();
            // The buttons are removed by FeatureHost.LevelUnloading, via
            // DisasterPanelBar.Remove.
            _endingStallLogged = false;
        }

        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.FireWhirlEnabled.value ? "yes" : "no");
            // ★ ③ has no tile on the disaster panel. State in the diagnostics too that
            //   there is no route by which a player raises one (so that "no button
            //   appeared" is not read as "it is broken").
            b.Line(1, "trigger", "natural only - a fire whirl cannot be placed by hand");

            // ★ Always state whether vanilla's tornado is being stopped. **Without this
            //   there is no telling whether "no tornadoes happen" is a fault or a
            //   setting.**
            b.Line(1, "vanilla tornado", ModSettings.NoVanillaTornado.value
                ? (VanillaTornadoSuppressor.Suppressing
                    ? "suppressed (" + VanillaTornadoSuppressor.SuppressedCount
                      + " prefab(s) removed from the random draw)"
                    : "NOT suppressed yet"
                      + (VanillaTornadoSuppressor.Detail != null
                         ? " (" + VanillaTornadoSuppressor.Detail + ")" : ""))
                : "allowed (setting)");
            b.Line(1, "scan", _scanner.DiagnosticSummary());
            WriteConditionDiagnostics(b);

            var views = FireWhirlRegistry.Snapshot();
            b.Line(1, "active", views.Count.ToString());

            int maxLifetime = ModSettings.MaxLifetimeMinutes.value;
            float framesPerMinute = FeatureHost.FramesPerMinute;

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                string s = "#" + v.DisasterId
                    + "  (" + (int)v.Center.X + "," + (int)v.Center.Z + ")"
                    + "  r=" + (int)v.Radius
                    + "  " + v.ElapsedMinutes.ToString("F1")
                    + "/" + maxLifetime + "min"
                    + "  n=" + v.BurningCount
                    + (v.VehicleId != 0 ? "  pinned" : "  NO VEHICLE")
                    + (v.Manual ? "  manual" : "")
                    + (v.Ending ? "  ending " + v.EndingMinutes.ToString("F1") + "min" : "")
                    + (v.Ending && EndingStall.IsStuck(v.EndingMinutes, maxLifetime, framesPerMinute)
                        ? "  STUCK" : "");
                b.Line(2, s);
            }

            // ★ The flame shader. **It is the only clue to "the vortex is there but
            //   nothing is visible".** ④ and ⑤ had this line from the start and only ③
            //   did not (and only ③ threw every frame instead of reporting a FAIL).
            b.Line(1, "flame material", FireWhirlFlameFx.ShaderDetail);

            // The end of the overlay example in §7.1 of the design document. Always print
            // it: it is the most likely cause of "a huge fire but no whirl appears".
            b.Line(1, "cooldown", FireWhirlRegistry.CoolingCount.ToString());

            WriteSpreadDiagnostics(b);
        }

        /// <summary>
        /// **The lines that tell "there is not enough fire yet" apart from "it is
        /// broken".**
        ///
        /// ③ has no route but natural spawning, so the default state in an in-game test is
        /// "nothing happens". If all we can report in that state is
        /// <c>burning=0 active=0</c>, the tester can judge neither how much needs to burn
        /// nor whether it is running at all — and indeed one whole session ended without
        /// ③'s fix being confirmed for exactly that reason.
        ///
        /// The order is **what the player can act on first**: the requirement → how far
        /// along we are → the suppressors (cooldown / separation) → the prerequisites (the
        /// DLC, the prefab).
        /// </summary>
        private void WriteConditionDiagnostics(DiagnosticBuilder b)
        {
            var config = ModSettings.ToFireWhirlConfig();

            b.Line(1, "requirement",
                   config.DetectCount + " buildings burning within "
                   + config.DetectRadius.ToString("F0") + " m of each other"
                   + "  (min separation " + config.MinSeparation.ToString("F0")
                   + " m from a live or cooling fire whirl)");

            if (!ModCompat.NaturalDisastersOwned)
            {
                // ★ Do not talk about the conditions when the prerequisite is missing.
                //   Print "not enough fire" here and a tester on a setup without the DLC
                //   will go on adding fire for ever.
                b.Line(1, "conditions", "not evaluated: " + Strings.FireWhirlNeedsDlc);
                return;
            }

            if (!ModSettings.FireWhirlEnabled.value)
            {
                b.Line(1, "conditions", "not evaluated: fire whirls are switched off in the settings");
                return;
            }

            if (!_prospectFresh)
            {
                b.Line(1, "conditions", "not evaluated yet (no simulation tick since the city loaded)");
                return;
            }

            b.Line(1, "conditions", _prospect.Describe());
            if (_prospect.DensestCount > 0)
            {
                b.Line(2, "densest group",
                       _prospect.DensestCount + "/" + _prospect.RequiredCount
                       + " at (" + _prospect.DensestCentre.X.ToString("F0")
                       + "," + _prospect.DensestCentre.Z.ToString("F0") + ")");
            }
        }

        /// <summary>
        /// The diagnostics for the fire spread.
        ///
        /// While this was missing, WriteDiagnostics printed only enabled / scan / active,
        /// and "the spread has not lit a single building" could not be told apart from
        /// "there is nothing nearby to burn". Apply() returns immediately when
        /// SpreadStrength is 0, so we state that explicitly too.
        /// </summary>
        private static void WriteSpreadDiagnostics(DiagnosticBuilder b)
        {
            int strength = ModSettings.SpreadStrength.value;
            b.Line(1, "spread strength",
                   strength + (strength <= 0 ? "  (OFF - no ignition at all)" : ""));

            b.Line(1, "spread passes", FireWhirlDamage.Passes.ToString());
            b.Line(2, "last pass",
                   "candidates=" + FireWhirlDamage.LastCandidates
                   + " selected=" + FireWhirlDamage.LastSelected
                   + " attempted=" + FireWhirlDamage.LastAttempted
                   + " refused=" + FireWhirlDamage.LastRefused
                   + " ignited=" + FireWhirlDamage.LastIgnited);
            b.Line(2, "session ignited", FireWhirlDamage.TotalIgnited.ToString());

            if (FireWhirlDamage.BarrenStreak > 0 || FireWhirlDamage.SpreadLooksBroken)
            {
                b.Line(2, "barren passes",
                       FireWhirlDamage.BarrenStreak + "/" + FireWhirlDamage.BarrenThreshold
                       + (FireWhirlDamage.SpreadLooksBroken ? "  SPREAD LOOKS BROKEN" : ""));
            }
        }
    }
}
