using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤'s phases. **It does not occupy a vanilla disaster slot** (design doc §2), so ⑤ runs on
    /// its own phase machine.
    ///
    /// The three of <see cref="Idle"/> / <see cref="Done"/> / <see cref="Refused"/> mean
    /// "nothing is in progress right now", and a new volcano can only start from those.
    ///
    /// ★★ <b>Number 1 (Surveying) and number 2 (AwaitingConfirmation) were retired on 2026-08-21.</b>
    /// The confirmation window is gone, so a single click goes from
    /// <c>Idle</c> to <c>Clearing</c> (or <c>Refused</c>) **within the same tick**.
    /// The survey itself is still there, but **it is no longer a phase observable from outside**,
    /// so it is not a phase either (no state is left that nobody can reach).
    /// **The numbers have not been closed up** — so that a retired number is never reused with a
    /// different meaning.
    /// </summary>
    public enum VolcanoPhase
    {
        /// <summary>Nothing at all.</summary>
        Idle = 0,

        // 1 = Surveying (retired) / 2 = AwaitingConfirmation (retired). Do not reuse.

        /// <summary>Clearing (the staged destruction of roads and buildings). T5.</summary>
        Clearing = 3,

        /// <summary>Uplift. T6.</summary>
        Uplifting = 4,

        /// <summary>Eruption. T7.</summary>
        Erupting = 5,

        /// <summary>The lava advancing. T8.</summary>
        Flowing = 6,

        /// <summary>The lava cooling. T8.</summary>
        Cooling = 7,

        /// <summary>Finished. **The terrain stays as it is (irreversible).**</summary>
        Done = 8,

        /// <summary>Refused. The reason is in <see cref="VolcanoState.LastRefusal"/>.</summary>
        Refused = 9,

        /// <summary>
        /// **Super-eruption only.** A huge magma chamber grows and ground wider than the mountain
        /// swells into a dome (<c>SuperEruption.InflationAt</c>).
        /// Only reached when the slider is at its top (display 25.5).
        /// </summary>
        Inflating = 10,

        /// <summary>
        /// **Super-eruption only.** The roof of the emptied magma chamber gives way under its own
        /// weight and a flat-bottomed caldera drops (<c>SuperEruption.BowlProfileAt</c>).
        /// </summary>
        Collapsing = 11,
    }

    /// <summary>
    /// ⑤'s phase machine. **Sim thread only.**
    ///
    /// ── one click goes all the way to starting work (changed 2026-08-21) ──────────────
    ///
    /// The owner's instruction:
    ///
    /// > make it like the other disasters: pick it from the tab, pick the scale, press the spot
    /// > where it happens, and the disaster happens.
    ///
    /// **The confirmation window has been removed.** The click *is* "make it", and there is no
    /// gap for a human beyond that point.
    ///
    /// <code>
    /// [main] click → take the point with RayGeometry.IntersectTerrain
    ///              → VolcanoHub.Request(Place, point, sizeScale)
    ///              → release the tool (the panel is not opened)
    ///         v
    /// [sim ] VolcanoState.HandleRequest picks it up with TakeRequest()
    ///              → VolcanoSurvey.Run(...) counts the buildings and roads and builds a Footprint
    ///              → Phase = Clearing (T5 starts running)
    /// </code>
    ///
    /// ★ **The survey has not gone away.** <c>BuildingManager.m_buildingGrid</c> and
    /// <c>NetManager.m_segmentGrid</c> are owned by the sim thread, so
    /// **they cannot be counted from the main thread (the tool's click handler)**.
    /// All that changed is that the step of "show the survey result to a human and wait" is gone;
    /// the one main → sim round trip is still required.
    ///
    /// ★ The counts in the surveyed affected range (buildings, roads), the summit height actually
    ///   reached, and the reason for a refusal are all in **the volcano tab
    ///   (<c>VolcanoEffectRows</c> / <c>VolcanoStatusRows</c>) and the diagnostic dump
    ///   (<c>VolcanoFeature</c>)**. What went away is the step of "make them read it first and
    ///   stop", not the information itself.
    ///
    /// ── a click while paused is not thrown away ─────────────────────────
    ///
    /// Taking the request (<see cref="HandleRequest"/>) sits **above the pause guard** in
    /// <c>VolcanoFeature.OnSimulationTick</c>, and only advancing the phase (<see cref="Tick"/>)
    /// sits below it. So a click while paused gets <b>as far as setting the phase to
    /// <c>Clearing</c></b>, and with **not one building, road or terrain cell changed**, it
    /// starts moving the moment the pause is lifted — the same behaviour as triggering a vanilla
    /// disaster while paused.
    /// **Do not throw it away silently** (the thing this class doc forbids most).
    ///
    /// ── one at a time ───────────────────────────────────
    ///
    /// While one is in progress (<see cref="VolcanoPhase.Clearing"/> onwards), <c>Place</c> is
    /// ignored and the reason is left in <see cref="LastRefusal"/>.
    ///
    /// ★★ <b>There is no way to stop it midway (removed on 2026-08-22).</b> The owner's call:
    /// "a stop button isn't needed. After all, you can't actually stop an eruption in real life,
    /// can you?" — ⑤ is a disaster that, once triggered, runs to the end (the same as the vanilla
    /// disasters). If you really must fold it away, turn off the "enable volcanoes" setting.
    /// **A half-carved mountain stays. This is not an undo.**
    ///
    /// **Do not fail silently.** On a refusal, always leave one English sentence in
    /// <see cref="LastRefusal"/> (handled the same way as ④'s <c>TyphoonSnapshot.Refusal</c>).
    /// </summary>
    public static class VolcanoState
    {
        /// <summary>
        /// The uplift progress at which the lava starts flowing. **This is a presentation value
        /// chosen by this mod.** Set it to 0 and you are emitting lava onto flat ground, where it
        /// simply pools in place because there is no gradient (<c>LavaPath.MinSlope</c>).
        /// At 0.6 the cone stands at 60 % of its final shape.
        /// </summary>
        private const float LavaDuringUpliftFrom = 0.6f;

        private static VolcanoPhase _phase = VolcanoPhase.Idle;
        private static VolcanoFootprint _footprint = VolcanoFootprint.None;
        private static string _lastRefusal;

        /// <summary>
        /// Whether this volcano is a super-eruption (whether the slider was at its top).
        /// **Decided exactly once, at the moment it is placed** — moving the slider midway does
        /// not change the script of a volcano already in progress.
        /// </summary>
        private static bool _super;

        /// <summary>The current phase.</summary>
        public static VolcanoPhase Phase { get { return _phase; } }

        /// <summary>
        /// Whether the volcano in progress is a super-eruption. The display and the diagnostics
        /// use it to state "why the ground is moving this much".
        /// </summary>
        public static bool IsSupereruption { get { return _super; } }

        /// <summary>
        /// The affected range of the inflation stage (wider than the mountain). Same as
        /// <see cref="Footprint"/> unless this is a super-eruption.
        /// </summary>
        private static VolcanoFootprint InflationFootprint
        {
            get
            {
                return _footprint.Resized(
                    SuperEruption.InflationRadiusMetres(_footprint.RadiusMetres),
                    SuperEruption.InflationHeightMetres(_footprint.HeightMetres));
            }
        }

        /// <summary>
        /// The radius of the ring of fissures (m). **Non-zero only during a super-eruption's
        /// collapse.**
        ///
        /// An eruption in the caldera-forming stage does not come up through the central vent but
        /// along the ring fault at the edge of the foundering roof. The blast
        /// (<c>Core.Volcano.BlastCluster</c>) looks at this and distributes a share of its bursts
        /// around the ring.
        /// </summary>
        public static float RingFissureRadiusMetres
        {
            get
            {
                if (!_super || _phase != VolcanoPhase.Collapsing) return 0f;
                if (!_footprint.Valid) return 0f;
                return SuperEruption.CalderaRadiusMetres(_footprint.RadiusMetres);
            }
        }

        /// <summary>The affected range of the caldera stage (wider than the mountain, and **the depth goes in positive**).</summary>
        private static VolcanoFootprint CalderaFootprint
        {
            get
            {
                return _footprint.Resized(
                    SuperEruption.CalderaRadiusMetres(_footprint.RadiusMetres),
                    SuperEruption.CalderaDepthMetres(_footprint.HeightMetres));
            }
        }

        /// <summary>The latest survey result. <c>Valid == false</c> means "not surveyed yet".</summary>
        public static VolcanoFootprint Footprint { get { return _footprint; } }

        /// <summary>
        /// Uplift progress [0,1]. From T6 onwards it is <see cref="VolcanoUplift.ProgressUnit"/>
        /// itself. <b>Do not show a row for it while it is 0</b> — "progress 0%" does not mean
        /// "not advancing" but "the uplift stage has not been entered yet".
        /// </summary>
        public static float ProgressUnit { get { return VolcanoUplift.ProgressUnit; } }

        /// <summary>
        /// The most recent reason for a refusal (**English, for diagnostics**). null if nothing
        /// was refused. There is no translation, but not reporting it would be worse — this is
        /// the only clue as to "why it could not be triggered" (the same call as ④'s
        /// <c>TyphoonSnapshot.Refusal</c>).
        /// </summary>
        public static string LastRefusal { get { return _lastRefusal; } }

        /// <summary>
        /// Call on level load and unload. **Discards all state.**
        /// Carry it over and a volcano at the previous city's location keeps running in the next
        /// one.
        /// </summary>
        public static void Reset()
        {
            _phase = VolcanoPhase.Idle;
            _footprint = VolcanoFootprint.None;
            _lastRefusal = null;
            _super = false;
            // The clearing's tally is not carried over either. **A volcano in progress is not
            // saved**, so leaving and re-entering the city restarts the clearing from 0 (the
            // terrain stays in whatever shape it was in).
            VolcanoClearing.Reset();
            // ★ Give back the uplift's snapshot array too (279 KB at a 3 km radius).
            //   **The terrain does not come back.**
            VolcanoUplift.Reset();
            // ★ Fold away the eruption's plan as well. **The drawing side's (main) cleanup does
            //   not happen here** — destroying Unity objects is the main thread's job, and
            //   VolcanoEruptionFx folds itself away by looking at the snapshot
            //   (on level unload VolcanoFeature calls Destroy).
            VolcanoEruption.Reset();
            // ★ Give back the lava's trails too (8 flows × 128 points = 8 KB). **The scorched
            //   ground and the burnt buildings do not come back** — all that is discarded is
            //   "what was going to happen next".
            VolcanoLava.Reset();
            // ★ Fold away the volcanic earthquakes being recorded on the seismograph as well.
            //   **Do not carry the previous shaking into the next city or the next volcano.**
            VolcanoTremorTrace.Reset();
        }

        /// <summary>
        /// Pick up exactly one queued request and answer it. **Sim thread.**
        ///
        /// ★★ <b>Call this from <u>above</u> the pause guard in
        /// <c>VolcanoFeature.OnSimulationTick</c></b> (whole-project review I1). Back when it sat
        /// below the guard, **a player who clicked the ground while paused got nothing at all** —
        /// the status rows stayed still and nothing was left in the log or the diagnostics.
        /// **Pausing before making a mountain is the most natural thing to do**, and there it was
        /// "failing silently" (exactly the thing the class doc forbids).
        ///
        /// A <c>Place</c> is accepted while paused, but **all it does is set the phase to
        /// <c>Clearing</c>** — not one building, road or terrain cell changes. What actually
        /// starts destroying things is <see cref="Tick"/>, and that sits below the pause guard.
        /// The same behaviour as triggering a vanilla disaster while paused.
        ///
        /// <see cref="VolcanoHub.TakeRequest"/> is called <b>exactly once per tick</b>
        /// (call it twice and the second is always None, creating a call-order-dependent dropped
        /// request; see its own doc). **That one call is here.**
        /// </summary>
        public static void HandleRequest(VolcanoSnapshot snapshot)
        {
            VolcanoRequestData request = VolcanoHub.TakeRequest();
            if (request.Kind == VolcanoRequest.None) return;

            // ★ The predicate is ⑤'s gate itself (VolcanoTerrainFacts.Usable).
            //   Let it through on "the field resolved" and work could start in an environment
            //   where the value is unusable.
            bool terrainUsable = snapshot != null && snapshot.Valid && snapshot.Terrain.Usable;
            if (!terrainUsable)
            {
                Refuse("the terrain write path is not usable in this build of the game; "
                       + "no volcano can be placed");
                return;
            }

            // ★ There is only one kind of request, Place (Stop was removed; see the note on
            //   <c>VolcanoRequest</c>). Do not make it a switch — lining up unreachable cases
            //   makes a path we supposedly deleted read as if it were still alive.
            if (request.Kind == VolcanoRequest.Place)
            {
                HandlePlace(request.Point, request.SizeScale, request.SizeRaw);
            }
        }

        /// <summary>
        /// Advance the phase. **Always call it from below the pause guard in
        /// <c>VolcanoFeature.OnSimulationTick</c>** (otherwise the mountain grows and buildings
        /// vanish while paused).
        ///
        /// Taking the request is not here (<see cref="HandleRequest"/> has done it above the
        /// guard).
        /// </summary>
        public static void Tick(VolcanoSnapshot snapshot, uint frame, float deltaMinutes)
        {
            // ★ The predicate is ⑤'s gate itself (VolcanoTerrainFacts.Usable).
            bool terrainUsable = snapshot != null && snapshot.Valid && snapshot.Terrain.Usable;
            if (!terrainUsable) return;

            StepPhase(frame, deltaMinutes);
        }

        /// <summary>
        /// Per-phase advance. **Do not write the real work in this file** — it belongs in
        /// <see cref="VolcanoClearing"/> / <c>VolcanoUplift</c> / <c>VolcanoLava</c>
        /// (the 800-line rule). All that may be written here is "which one to call in which
        /// order".
        ///
        /// ★★ <b>The clearing → uplift ordering can only be broken here.</b> Swap them and the
        /// roads and buildings push the terrain back on every flush, leaving flat trenches and
        /// funnels inside the mountain (design doc §1.2 / §A-2). The type-side guarantee is
        /// <c>UpliftSchedule.ActiveRadiusMetres(R, VolcanoClearing.ClearedRadiusMetres)</c>,
        /// which returns 0 while the clearing has not reached.
        /// </summary>
        private static void StepPhase(uint frame, float deltaMinutes)
        {
            if (!_footprint.Valid) return;

            if (_phase == VolcanoPhase.Clearing)
            {
                // The uplift has not moved once yet, so the progress is 0. The front runs ahead
                // by ModSettings.VolcanoClearingLeadMetres only.
                VolcanoClearing.Tick(_footprint, 0f, deltaMinutes);

                if (!VolcanoClearing.FrontReached) return;

                // It reached the first front. From here the uplift holds the progress and the
                // clearing runs ahead of it (ring lockstep).
                _phase = VolcanoPhase.Uplifting;
                Log.Info("volcano clearing reached its first front ("
                         + VolcanoClearing.ClearedRadiusMetres.ToString("F0")
                         + " m); the uplift starts now");
                return;
            }

            if (_phase == VolcanoPhase.Inflating)
            {
                // ★★ **The magma chamber growing over tens of thousands of years** (the owner's
                //    request). Ground wider than the mountain swells into a dome far lower than
                //    the mountain. Nothing is erupting yet here — no plume and no lava.
                VolcanoFootprint bulge = InflationFootprint;
                VolcanoClearing.Tick(bulge, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
                VolcanoUplift.Tick(bulge, frame, deltaMinutes);

                if (!VolcanoUplift.Complete) return;

                _phase = VolcanoPhase.Erupting;
                Log.Info("supereruption: the magma chamber finished inflating (+"
                         + bulge.HeightMetres.ToString("F0") + " m over "
                         + bulge.RadiusMetres.ToString("F0")
                         + " m); the chamber is now at its pressure limit and erupts");
                return;
            }

            if (_phase == VolcanoPhase.Collapsing)
            {
                // ★★ **The edifice explodes as it founders** (2026-08-22, owner's report).
                //
                //    &gt; when the caldera forms, doesn't the cone body drop a long way and
                //    &gt; explode massively…?
                //
                //    Quite so, and the right thing is to run these three **at the same time**.
                //    The old ordering was "sink quietly once the eruption has finished", which
                //    made the plume disappear at what should be the most violent moment.
                //    The eruption is pinned to the ceiling of its sustain phase by
                //    VolcanoEruption.BeginClimax, so **it does not end until the ground has
                //    finished falling**.
                VolcanoFootprint caldera = CalderaFootprint;
                VolcanoEruption.Tick(_footprint, frame, deltaMinutes, 1f);
                VolcanoClearing.Tick(caldera, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
                VolcanoUplift.Tick(caldera, frame, deltaMinutes);

                // The lava keeps flowing through the collapse too (stop it and the picture
                // freezes here alone).
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);

                if (!VolcanoUplift.Complete) return;

                // ★ It has finished falling. Only now does the eruption head into its decline.
                VolcanoEruption.EndClimax();
                _phase = VolcanoPhase.Erupting;
                Log.Info("supereruption: the edifice foundered ("
                         + VolcanoUplift.SummitMetres.ToString("F0") + " m at the deepest, over r="
                         + caldera.RadiusMetres.ToString("F0")
                         + " m) after " + VolcanoEruption.BurstsSoFar
                         + " bursts; the eruption now wanes. The terrain stays as it is "
                         + "(this is irreversible)");
                return;
            }

            if (_phase == VolcanoPhase.Erupting)
            {
                // T7. **It only decides the plan for the ejecta**; it changes not one terrain
                //   cell or building. The mountain is finished, so 1 is passed as the progress
                //   (i.e. the envelope moves from sustain into decline). The eruption itself has
                //   been running since the start of the uplift.
                VolcanoEruption.Tick(_footprint, frame, deltaMinutes, 1f);

                // ★ The lava has been coming out since partway through the uplift, so keep
                //   advancing it here too — stop it and the flow freezes for the duration of the
                //   eruption alone. The terrain no longer moves, so the tolerance is 0
                //   (VolcanoUplift.RiseMetresPerFrame returns 0 once complete).
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);

                // ★★ **The foundering starts "during" the eruption, not "after" it**
                //    (2026-08-22, owner's report). The roof falls once enough has erupted for the
                //    magma chamber to start emptying, and that fall is itself what causes the
                //    huge explosion. The timing is held by VolcanoEruption.ReadyForCollapse.
                //
                //    ★ Checking that the stage is not Collapse is how we decide "it has not
                //      fallen yet" — this phase is returned to once it has finished falling, so
                //      without that check it would fall for ever.
                if (_super
                    && VolcanoUplift.Stage != UpliftStage.Collapse
                    && VolcanoEruption.ReadyForCollapse)
                {
                    VolcanoFootprint caldera = CalderaFootprint;
                    // ★ Pass the radius of the cone body too. Outside the cone body the caldera
                    //   floor drops relative to **that cell's real ground** (keeping the original
                    //   terrain).
                    if (VolcanoUplift.StartStage(caldera, UpliftStage.Collapse,
                                                 _footprint.RadiusMetres))
                    {
                        VolcanoEruption.BeginClimax();
                        _phase = VolcanoPhase.Collapsing;
                        Log.Info("supereruption: the chamber has emptied enough after "
                                 + VolcanoEruption.BurstsSoFar
                                 + " bursts; THE EDIFICE IS NOW FOUNDERING into a caldera of r="
                                 + caldera.RadiusMetres.ToString("F0") + " m, floor "
                                 + caldera.HeightMetres.ToString("F0")
                                 + " m below the original ground - the eruption is pinned at its "
                                 + "peak and will not end until it has fallen");
                        return;
                    }

                    // ★ **Do not skip it silently.** Leave the reason it could not be dropped,
                    //   and fall through to the ordinary eruption ending.
                    Refuse("the caldera collapse could not start ("
                           + (VolcanoUplift.LastFailure ?? "unknown reason")
                           + "); the volcano finishes without one");
                }

                if (!VolcanoEruption.Finished) return;

                // ★ T8 replaced <c>Done</c> here with <c>Flowing</c>.
                //   That the phase does not get stuck in progress is guaranteed by two
                //   finiteness properties on the lava side — one flow always stops at
                //   <c>LavaPath.MaxSteps</c>, and once they have all stopped everything is
                //   certain to cool out within <c>CoolMinutes</c>.
                _phase = VolcanoPhase.Flowing;
                Log.Info("volcano eruption finished after "
                         + VolcanoEruption.BurstsSoFar + " bursts; the lava starts now");
                return;
            }

            if (_phase == VolcanoPhase.Flowing)
            {
                // T8. **It does not change the terrain** (only T6 writes RawHeights).
                VolcanoLava.Tick(_footprint, frame, deltaMinutes, 0f);

                // With a flow count of 0 (disabled in the settings) it passes through here
                // without a single flow.
                if (!VolcanoLava.AllStopped && !VolcanoLava.Finished) return;

                _phase = VolcanoPhase.Cooling;
                Log.Info("volcano lava stopped: " + VolcanoLava.FlowCount + " flows, longest "
                         + VolcanoLava.LongestMetres.ToString("F0") + " m, ignited "
                         + VolcanoLava.BuildingsIgnited + " buildings and "
                         + VolcanoLava.TreesIgnited + " trees");
                return;
            }

            if (_phase == VolcanoPhase.Cooling)
            {
                // Just waiting for it to cool. **No new flows are emitted.**
                VolcanoLava.Tick(_footprint, frame, deltaMinutes, 0f);

                if (!VolcanoLava.Finished) return;

                _phase = VolcanoPhase.Done;
                Log.Info("volcano finished; the terrain stays as it is (this is irreversible)");
                return;
            }

            if (_phase != VolcanoPhase.Uplifting) return;

            // ★★ **The ordering is these two lines themselves** (design doc §1.2 / trap 1).
            //    The clearing first, handed the uplift's progress so it runs ahead, and only then
            //    does the uplift raise the inside of "the radius the clearing reached".
            //    They must not be swapped.
            //   ★ What gets passed is not the progress but **the uplift front** (the cone is
            //     rebuilt to allow for the crater, so the front runs ahead of the progress; see
            //     the doc of VolcanoClearing.Tick).
            VolcanoClearing.Tick(_footprint, VolcanoUplift.GrowthFrontUnit, deltaMinutes);
            VolcanoUplift.Tick(_footprint, frame, deltaMinutes);

            // ★★ **SimCity 4's ordering. The eruption comes first and the mountain is piled up
            //    by it.** This used to be "erupt once the uplift has finished", so the smoke
            //    appeared only after the finished mountain had silently inflated out of the
            //    ground. The plume and the glow are emitted from the uplift's very first tick.
            //    T7 **only decides the plan** and changes not one terrain cell or building, so it
            //    does not touch the clearing → uplift ordering (trap 1).
            VolcanoEruption.Tick(_footprint, frame, deltaMinutes, VolcanoUplift.ProgressUnit);

            // ★ The lava starts flowing before the mountain is finished too. It is held back
            //   **until the cone is reasonably far up** because emitting it onto flat ground
            //   just pools in place for want of a gradient; the threshold itself is a
            //   presentation value (LavaDuringUpliftFrom).
            //   The terrain is still rising, so that amount is passed as the tolerance
            //   (without it, the flow stops because it wrongly observes "the lava climbed").
            if (VolcanoUplift.ProgressUnit >= LavaDuringUpliftFrom)
            {
                VolcanoLava.Tick(_footprint, frame, deltaMinutes,
                                 VolcanoUplift.RiseMetresPerFrame);
            }

            if (!VolcanoUplift.Complete) return;

            // ★ T7 replaced <c>Done</c> here with <c>Erupting</c>. That the phase does not get
            //   stuck in progress is guaranteed by <c>VolcanoEruption.Finished</c> (the eruption
            //   always ends within a finite amount of in-game time, and ends even if an exception
            //   is thrown).
            _lastRefusal = null;

            // ★★ **Only in a super-eruption does the magma chamber grow after the mountain is
            //    built** (the second stage of the owner's request). An ordinary eruption goes
            //    straight on to Erupting.
            if (_super && VolcanoUplift.Stage == UpliftStage.Cone)
            {
                VolcanoFootprint bulge = InflationFootprint;
                if (VolcanoUplift.StartStage(bulge, UpliftStage.Inflation))
                {
                    _phase = VolcanoPhase.Inflating;
                    Log.Info("supereruption: the cone is up; a magma chamber now inflates the "
                             + "ground over r=" + bulge.RadiusMetres.ToString("F0") + " m by "
                             + bulge.HeightMetres.ToString("F0") + " m");
                    return;
                }

                Refuse("the magma chamber inflation could not start ("
                       + (VolcanoUplift.LastFailure ?? "unknown reason")
                       + "); the volcano erupts without one");
            }

            _phase = VolcanoPhase.Erupting;
            Log.Info("volcano uplift complete (the eruption has been running since it started): summit +"
                     + VolcanoUplift.SummitMetres.ToString("F0")
                     + " m, crater " + (VolcanoUplift.CraterFormed ? "at full depth" : "SHALLOW")
                     + "; the eruption starts now");

            // ★★ **If the summit was clipped by the game's height ceiling, say so.**
            //    Present a flat summit silently and all the player can see is
            //    "the height setting is not working".
            //    The ceiling cannot be raised by a mod (the doc of UpliftSchedule.CeilingClipped).
            if (VolcanoUplift.CeilingClippedCells > 0)
            {
                Log.Info("volcano summit was clipped by the game's terrain ceiling ("
                         + UpliftSchedule.CeilingMetres.ToString("F0")
                         + " m) on " + VolcanoUplift.CeilingClippedCells
                         + " cells; the top is flat there. The ceiling cannot be raised by a "
                         + "mod - place the volcano on lower ground or reduce its height");
            }
        }

        /// <summary>
        /// **The map was clicked. This is "make it".**
        ///
        /// The survey (<c>VolcanoSurvey.Run</c>) and starting work are done in one call — with
        /// the confirmation window gone, no human judgement comes between the two (class doc).
        /// **Do not split these two into separate phases**: nobody can observe the state in
        /// between, and all it adds is one more unreachable phase.
        ///
        /// The order of refusals is "already in progress → no destruction path → the survey
        /// failed", and **all of them return before destroying a single thing**. The reason is
        /// always left in <see cref="LastRefusal"/>.
        /// </summary>
        private static void HandlePlace(Vec3 point, float sizeScale, int sizeRaw)
        {
            if (InProgress())
            {
                Refuse("a volcano is already in progress (phase=" + _phase
                       + "); the placement request was ignored. Stop it first");
                return;
            }

            // ★★ **In an environment without the clearing's destruction path, refuse here
            //    without destroying a single thing** (design doc §1.2 / T5 Step 1). Do not choose
            //    "give up on the roads and uplift anyway" — that would mean shipping, knowingly,
            //    the failure §1.2 discovered (flat trenches inside the mountain).
            //    **Put the test immediately before starting work, ahead of any destruction.**
            //
            //    ★ The predicate is the same expression <c>VolcanoClearing.Sweep</c> actually
            //      gates on (whole-project review M9). Back when it only looked at the road side,
            //      in an environment where the building side could not be resolved
            //      **the volcano was confirmed and stalled for good in <c>Clearing</c>.**
            if (!VolcanoClearing.ClearingPathAvailable)
            {
                RefuseAndForget("no usable destruction path for the roads and buildings in "
                                + "this build of the game; raising the ground would leave flat "
                                + "trenches and bowls where they stand");
                return;
            }

            // ★ Use the scale **the request carried in**. The slider cannot be re-read here
            //   (the sim thread does not touch the UI), and even if it could, it would no longer
            //   be "the value at the moment of the click".
            VolcanoForm form = CurrentForm();
            VolcanoFootprint footprint;

            // ★★ **A super-eruption only when the slider is at its top** (the owner's request).
            //    Test it on the raw value — the scale is the value after clamping into a band, so
            //    it no longer tells you whether it was at the top.
            //    **Decide it before the survey** (the way the radius is read changes below).
            _super = SuperEruption.IsSuper(sizeRaw);

            // ★★ **In a super-eruption the size requested is the size of the CALDERA.**
            //
            //    Otherwise it does not read as a picture. The caldera is 1.9× the cone, so
            //    raising a cone at the full requested radius (5564 m for a stratovolcano) would
            //    demand a 10.6 km caldera, which is cut at the cost ceiling (6 km) and gives
            //    **a hole roughly the same size as the mountain** — the foundering is invisible.
            //
            //    Real supervolcanoes (Yellowstone, Toba) **have no big cone** either.
            //    What they have is a caldera. So at 25.5 the requested radius is read as the
            //    caldera's radius, and the cone is divided back out of it:
            //
            //      cone = R / 1.9 → inflation = cone × 2.4 → caldera = cone × 1.9 = R
            //
            //    ★ The height is not divided back out. A low mountain falling does not look like
            //      a foundering, so the cone is raised at the requested height.
            float requestedRadius =
                VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form), sizeScale);
            //
            //    ★ Divide the ceiling back out as well. What <c>SuperEruption.MaxRadiusMetres</c>
            //      caps is **the caldera**, so raising a cone larger than that leaves only the
            //      caldera hitting the ceiling, and we are back to "a hole the same size as the
            //      mountain" (a shield volcano's recommended radius is 2 km, so without this it
            //      always happens).
            if (_super)
            {
                float coneCeiling =
                    SuperEruption.MaxRadiusMetres / SuperEruption.CalderaRadiusFactor;
                requestedRadius /= SuperEruption.CalderaRadiusFactor;
                if (requestedRadius > coneCeiling) requestedRadius = coneCeiling;
            }

            if (!VolcanoSurvey.Run(
                    point, form,
                    // ★★ The baseline is **the recommended value for each form** (2026-08-22).
                    //    The radius and final-height sliders on the settings screen were removed —
                    //    they were making the same quantity depend on two knobs
                    //    (<c>VolcanoSizeScale</c>).
                    requestedRadius,
                    VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form), sizeScale),
                    out footprint))
            {
                RefuseAndForget(VolcanoSurvey.LastFailure
                                ?? "the survey failed for an unknown reason");
                return;
            }

            _footprint = footprint;
            _lastRefusal = null;
            // ★ Everything from here on is "destroying". T5's VolcanoClearing drives this phase.
            //   While paused the phase merely advances this far, and the real destruction does
            //   not start until the pause is lifted (VolcanoFeature's pause guard).
            _phase = VolcanoPhase.Clearing;
            Log.Info("volcano placed at (" + _footprint.Centre.X.ToString("F0") + ","
                     + _footprint.Centre.Z.ToString("F0") + "): " + _footprint.Form
                     + " r=" + _footprint.RadiusMetres.ToString("F0")
                     + " m h=" + _footprint.HeightMetres.ToString("F0")
                     + " m; clearing starts now"
                     + (_super
                        ? ". THIS IS A SUPERERUPTION (the slider is at its top). The cone is "
                          + "deliberately SMALLER than at lower settings - at 25.5 the size you "
                          + "picked is the size of the CALDERA, not of the mountain (real "
                          + "supervolcanoes have no big cone). It will grow, then a magma "
                          + "chamber will inflate the ground over r="
                          + SuperEruption.InflationRadiusMetres(_footprint.RadiusMetres)
                                .ToString("F0")
                          + " m, then it erupts and collapses into a caldera of r="
                          + SuperEruption.CalderaRadiusMetres(_footprint.RadiusMetres)
                                .ToString("F0")
                          + " m"
                        : ""));
        }

        /// <summary>
        /// Whether the phase is one where "no new volcano can be started". **The five that follow
        /// the start of destruction.**
        /// </summary>
        private static bool InProgress()
        {
            return _phase == VolcanoPhase.Clearing
                   || _phase == VolcanoPhase.Uplifting
                   || _phase == VolcanoPhase.Inflating
                   || _phase == VolcanoPhase.Collapsing
                   || _phase == VolcanoPhase.Erupting
                   || _phase == VolcanoPhase.Flowing
                   || _phase == VolcanoPhase.Cooling;
        }

        private static void Refuse(string reason)
        {
            _lastRefusal = reason;
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcState", reason);
        }

        /// <summary>
        /// Refuse the attempted location itself. **Drops the phase to
        /// <see cref="VolcanoPhase.Refused"/> and discards the survey result too** — if the
        /// previous volcano's affected range survived a refusal, the volcano tab would keep
        /// quoting the figures for "the mountain that was never made".
        /// </summary>
        private static void RefuseAndForget(string reason)
        {
            _footprint = VolcanoFootprint.None;
            // ★ Do not carry over the script of a volcano that was never made.
            _super = false;
            _phase = VolcanoPhase.Refused;
            Refuse(reason);
        }

        /// <summary>The form from the settings. Out-of-range values are dropped to the default by <c>VolcanoShape.FormOf</c>.</summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);
        }

    }
}
