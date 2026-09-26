using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The seismograph section and the waveform graph section. **Main thread only.**
    ///
    /// It was split out of <see cref="EarthquakePanel"/> because that file had grown far
    /// past the project's 800-line rule, and **not one character of the content was
    /// changed**. Neither row creation nor assignment to <c>.text</c> appears in this
    /// file; both can only go through <see cref="EarthquakeRows"/> (the guarantee set out
    /// in its class doc).
    ///
    /// The two sections are in one type because each explains the other: straight after
    /// "what building a seismograph changes" comes what the seismograph you built is
    /// actually seeing.
    /// </summary>
    internal static class EarthquakeSensorRows
    {
        private static UILabel _sensorEpicentreLabel;
        private static UILabel _sensorLeadLabel;
        private static UILabel _sensorCursorLabel;
        private static UILabel _waveformLabel;
        private static UILabel _waveformModelLabel;
        private static UILabel _waveformTremorLabel;
        private static UILabel _waveformUnavailableLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            // ── Seismographs (Task 7) ───────────────────────────────
            // This is layer 1 too. Both the lead-time formula and the cap of 100 are
            // vanilla's own literals (§A-2); this mod has not added a single coefficient.
            EarthquakeRows.AddSectionHeader(p, "SensorSection", ref y,
                Strings.EarthquakeSensorSection);
            _sensorEpicentreLabel = EarthquakeRows.AddLayer1Row(p, "SensorEpicentre", ref y);
            _sensorLeadLabel = EarthquakeRows.AddLayer1Row(p, "SensorLead", ref y);
            _sensorCursorLabel = EarthquakeRows.AddLayer1Row(p, "SensorCursor", ref y);

            // ★ This one row explains **what building one changes**, not a reading, so it
            //    is shown with no earthquake happening and with the snapshot unreadable.
            //    Accordingly, once written here it is never rewritten from anywhere
            //    (having no "clear" path on the Refresh side is what guarantees that).
            EarthquakeRows.AddPlainRow(p, "SensorEffect", ref y,
                Strings.EarthquakeSensorEffect, 42f);

            // ── Waveforms (Task 8) ──────────────────────────────────
            // This is layer 1 too. What is plotted is **vanilla's own shaking formula**
            // (§A-7: the same formula, the same constants and the same window that move
            // the camera), evaluated at the seismograph's position instead of the
            // camera's; this mod has not added a single piece of physics.
            // **But it is not a value the game's seismograph measured** —
            // EarthquakeSensorAI holds no time-series data whatsoever (§C-1).
            // EarthquakeWaveformNote states that distinction directly under the graph,
            // every time.
            //
            // It goes directly below the seismograph section. Straight after the
            // explanation of what building a seismograph changes comes what the one you
            // built is actually seeing.
            // It takes three rows' worth of height. At its longest this row holds the
            // whole of Strings.EarthquakeWaveformNeedsSensor (about 150 characters).
            _waveformLabel = EarthquakeRows.AddLayer1Row(p, "Waveform", ref y, 42f);

            WaveformView.Build(p, "WaveformPlot", 12f, y);
            if (WaveformView.Available) y += WaveformView.PlotHeight + 6f;

            // ★★ **Exactly one layer-2 row lives in this section** (the synthetic
            //    seismogram, off by default). Banish it to the layer-2 section away from
            //    the graph and there is no way to tell, on the spot, what the orange line
            //    in front of you is. Instead of moving it away we use
            //    <c>AddLayer2Row</c>, so that both the prefix ([Disaster + model]) and the
            //    colour declare that this one row belongs to a different layer.
            //    **Only EarthquakeRows can attach the prefix**, so this row cannot
            //    accidentally be presented as measured (the guarantee in its class doc).
            _waveformModelLabel = EarthquakeRows.AddLayer2Row(p, "WaveformModel", ref y, 40f);

            // ★★ **Layer 3: the volcanic tremor row** (2026-08-22, at the owner's
            //    request: "also fix volcanic earthquakes not being recorded on the
            //    seismograph"). Treated exactly like layer 2 — this too is <b>this mod's
            //    model</b>, not a value the game computes (see
            //    <c>Game/Volcano/VolcanoTremorTrace</c>'s class doc). That is why it uses
            //    <c>AddLayer2Row</c>: the prefix and the colour declare "not measured"
            //    every time.
            _waveformTremorLabel = EarthquakeRows.AddLayer2Row(p, "WaveformTremor", ref y, 40f);

            // ★ Never go silently blank. Show the peak-amplitude row (_waveformLabel)
            //    and then state why the graph is not there. Degradation, not a lie.
            //
            //    **This row is created regardless of whether the build succeeded**
            //    (whole-feature review I6). It used to be created only when the build
            //    failed, so when drawing fell over **at runtime** and
            //    WaveformView.Destroy() hid the sprite, there was nowhere to write the
            //    reason — the player was left with a blank and no explanation at all,
            //    the exact opposite of what WaveformView's class doc promises ("does not
            //    silently go blank… degradation, not a lie"). The content is filled in
            //    by the Refresh side according to the state.
            _waveformUnavailableLabel = EarthquakeRows.AddPlainRow(p, "WaveformUnavailable",
                ref y, "", 28f);

            // Say what the graph is a picture of, directly under the graph, every time.
            // The content is only filled in while the graph is showing (set on the
            // Refresh side).
        }

        /// <summary>On level unload. Just drop the references (the objects go with the panel).</summary>
        internal static void Destroy()
        {
            _sensorEpicentreLabel = null;
            _sensorLeadLabel = null;
            _sensorCursorLabel = null;
            _waveformLabel = null;
            _waveformModelLabel = null;
            _waveformTremorLabel = null;
            _waveformUnavailableLabel = null;
        }

        /// <summary>
        /// **What building a seismograph changes.** It names the two effects that are
        /// written down nowhere in the game (§A-2 / §C-2):
        ///
        ///   1. the warning lead time goes from 1755 to at most 8192 frames
        ///      (38.6 minutes to exactly 3.0 hours)
        ///   2. <c>located</c> gets set, and **the earthquake starts being painted on the
        ///      hazard map at all**
        ///
        /// **Do not get the causation backwards.** The seismograph does not call
        /// <c>DetectDisaster</c>. <c>EarthquakeAI.SimulationStep</c> does, and what it
        /// decides on is **the coverage at the single point of the epicentre**. So the
        /// only thing the lead time may be derived from here is
        /// <see cref="EarthquakeReading.CoverageAtEpicentre"/>, never the value at the
        /// cursor (a seismograph whose range does not reach the epicentre contributes
        /// nothing at all for that earthquake).
        ///
        /// **Do not give a coverage of 0 and "could not be read" the same face.** 0 is
        /// this feature's headline measurement — "not one seismograph reaches the
        /// epicentre" — and in that case the number is shown. The number is withheld only
        /// when the read failed (<see cref="EarthquakeReading.CoverageKnown"/> /
        /// <see cref="EarthquakeSnapshot.CursorCoverageValid"/>).
        /// </summary>
        internal static void RefreshSensor(EarthquakeSnapshot snapshot,
                                           EarthquakeReading primary, bool haveCursor)
        {
            RefreshEpicentreCoverageRows(primary);
            RefreshCursorCoverageRow(snapshot, haveCursor);
        }

        /// <summary>
        /// Clears only the values in the seismograph rows. **The explanation
        /// (<c>SensorEffect</c>) is not cleared** — it is "what building one changes",
        /// not a reading, so it is at its most worth reading precisely when nothing can
        /// be read.
        /// </summary>
        internal static void ClearSensor()
        {
            EarthquakeRows.SetPlain(_sensorEpicentreLabel, "");
            EarthquakeRows.SetPlain(_sensorLeadLabel, "");
            EarthquakeRows.SetPlain(_sensorCursorLabel, "");
        }

        private static void RefreshEpicentreCoverageRows(EarthquakeReading primary)
        {
            // No earthquake means no epicentre. Print 0 here and it looks like "there is
            // no seismograph".
            if (primary == null)
            {
                EarthquakeRows.SetPlain(_sensorEpicentreLabel, "");
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            if (!primary.CoverageKnown)
            {
                // ★ Never manufacture a zero. Say that the read failed and withhold the
                //    lead time (printing 38.6 minutes with the coverage unknown would be
                //    asserting that it is 0).
                EarthquakeRows.SetPlain(_sensorEpicentreLabel,
                    Strings.EarthquakeCoverageAtEpicentre + ": " + Strings.EarthquakeUnavailable);
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            int raw = primary.CoverageAtEpicentre;
            int used = WarningLeadTime.ClampCoverage(raw);

            string text = Strings.EarthquakeCoverageAtEpicentre + ": " + raw;
            // Show the fact that vanilla caps this with Min(cov, 100) only when it is
            // actually being capped (§A-2).
            if (raw != used) text += " -> " + used;
            if (used == 0) text += "   (" + Strings.EarthquakeNoSensor + ")";
            EarthquakeRows.SetLayer1(_sensorEpicentreLabel, text);

            // Always derive the conversion from FeatureHost.FramesPerMinute (we once
            // hard-coded the constant and came out a factor of 4 wrong). When the
            // conversion is unavailable it returns 0, so the whole row is withheld.
            float minutes = WarningLeadTime.MinutesFor(raw, FeatureHost.FramesPerMinute);
            if (minutes <= 0f)
            {
                EarthquakeRows.SetPlain(_sensorLeadLabel, "");
                return;
            }

            // This is not "how many minutes until the warning"; it is a duration —
            // **how many minutes before the main shock the warning is issued**. It does
            // not shrink while paused (it is measured in game minutes).
            EarthquakeRows.SetLayer1(_sensorLeadLabel, Strings.EarthquakeWarningLead + ": "
                + minutes.ToString("F1") + " " + Strings.EarthquakeMinutes);
        }

        private static void RefreshCursorCoverageRow(EarthquakeSnapshot snapshot, bool haveCursor)
        {
            // Tell "the cursor is not over terrain" apart from "it could not be read".
            // The first happens almost constantly while the panel is being read (the
            // mouse is over the panel).
            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_sensorCursorLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            if (!snapshot.CursorCoverageValid)
            {
                EarthquakeRows.SetPlain(_sensorCursorLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // No note about the cap of 100 here. This row is for seeing whether a
            // seismograph reaches this spot; it is not a value that goes into the
            // lead-time formula.
            EarthquakeRows.SetLayer1(_sensorCursorLabel,
                Strings.EarthquakeCoverageAtCursor + ": " + snapshot.CursorCoverage);
        }

        /// <summary>
        /// Clears every waveform row and hides the plot. **The note is cleared too** —
        /// it explains what the graph currently on screen is, so leaving it up with no
        /// graph means explaining the provenance of a picture that does not exist.
        /// </summary>
        internal static void ClearWaveform()
        {
            EarthquakeRows.SetPlain(_waveformLabel, "");
            EarthquakeRows.SetPlain(_waveformModelLabel, "");
            EarthquakeRows.SetPlain(_waveformTremorLabel, "");
            RefreshWaveformAvailability();
            WaveformView.Render(null);
        }

        /// <summary>
        /// The one row giving the reason the graph is not showing (whole-feature review I6).
        ///
        /// **Check the state every time.** Drawing can fall over at runtime too (the
        /// catch in <see cref="WaveformView.Render"/> calls <c>Destroy()</c> and never
        /// draws again). Back when this was only decided at construction, everything from
        /// that point on was **a blank with no explanation**.
        ///
        /// Nothing is written for "not built yet" — with the panel open we are already
        /// built, so that case is never reached, but since the state is split four ways
        /// this makes it structurally explicit that "not built" is never restated as
        /// "unavailable".
        /// </summary>
        private static void RefreshWaveformAvailability()
        {
            switch (WaveformView.State)
            {
                case WaveformViewState.BuildFailed:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel,
                        Strings.EarthquakeWaveformUnavailable);
                    break;
                case WaveformViewState.RenderFailed:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel,
                        Strings.EarthquakeWaveformDrawFailed);
                    break;
                default:
                    EarthquakeRows.SetPlain(_waveformUnavailableLabel, "");
                    break;
            }
        }

        /// <summary>
        /// **The answer, in itself, to the request "I have a seismograph but I cannot see
        /// a waveform graph".**
        ///
        /// The lines shown here are <b>not values measured by an in-game seismograph</b>.
        /// <c>EarthquakeSensorAI</c> holds no time-series data whatsoever (§C-1, ABSENT)
        /// — its only field is <c>m_detectionRange</c>, and it is a device that does
        /// nothing but scatter an immaterial resource each tick. **What is plotted is
        /// vanilla's own shaking formula** (§A-7: the same formula, constants and window
        /// that <c>EarthquakeAI.RenderInstance</c> uses to move the camera), evaluated at
        /// the seismograph's position instead of the camera's. That is a different
        /// evaluation of the same formula, not an approximation.
        ///
        /// **Always write that distinction directly under the graph**
        /// (<c>EarthquakeWaveformNote</c>; design doc §3.5 requires it in both the design
        /// doc and the UI). Without it, this feature becomes "a picture that looks as
        /// though the game is measuring something when in fact nobody is".
        ///
        /// **Tell the three kinds of "empty" apart** (the no-manufactured-zeros
        /// discipline carried over from ①):
        ///   - no earthquake to record … the whole row is omitted
        ///   - an earthquake but zero seismographs … "please build a seismograph"
        ///   - seismographs but zero samples … before the main shock, the shaking window
        ///     has not opened yet
        /// Draw that last one as a flat line and it turns into "it is not shaking".
        /// </summary>
        internal static void RefreshWaveform(EarthquakeSnapshot snapshot)
        {
            // There is no earthquake in progress (Emerging|Active) and **no volcano
            // shaking either**. The shaking formula itself is not running over this
            // stretch, so there is nothing that can be said about a waveform.
            //
            // ★★ <b>Do not fold this away on "there is no earthquake" alone</b>
            //    (2026-08-22). This used to look only at <c>WaveformQuakeId == 0</c>, so
            //    **the whole graph vanished while only a volcano was shaking** — that was
            //    the display half of "volcanic earthquakes are not recorded on the
            //    seismograph". If the recording side is alive, <c>Traces</c> has stations
            //    in it.
            if (snapshot.WaveformQuakeId == 0 && snapshot.Traces.Count == 0)
            {
                ClearWaveform();
                return;
            }

            RefreshWaveformAvailability();

            var traces = snapshot.Traces;
            if (traces.Count == 0)
            {
                // ★ Do not substitute the camera position or the city centre. This is the
                //    answer to "I have a seismograph but I cannot see a waveform", so a
                //    waveform not tied to a seismograph means something else entirely.
                EarthquakeRows.SetPlain(_waveformLabel, Strings.EarthquakeWaveformNeedsSensor);
                EarthquakeRows.SetPlain(_waveformModelLabel, "");
                EarthquakeRows.SetPlain(_waveformTremorLabel, "");
                WaveformView.Render(null);
                return;
            }

            // Draw only the one nearest the epicentre (SeismographRecorder has ordered
            // them nearest first). Four graphs side by side would be unreadable, so the
            // rest appear only as a count.
            var trace = traces[0];

            // ★ Always state which earthquake the waveform belongs to (whole-feature
            //    review I1). For the same reason EarthquakeSnapshot.CursorQuakeId's doc
            //    requires "the display must always state this", the waveform's earthquake
            //    needs the same discipline. With two earthquakes running at once, the one
            //    the six rows above refer to (SelectPrimary) and the one in this picture
            //    (QuakeSelection.SelectDamaging) can differ.
            // ★★ **Do not misattribute the source.** Printing a distance from the
            //    epicentre and an earthquake number with no vanilla earthquake present
            //    makes it look like **a recording of an earthquake that is not happening**
            //    (<c>SeismographTrace.HasQuake</c> holds that distinction). When only a
            //    volcano is shaking, show the distance from ⑤'s centre, labelled as such.
            string header = Strings.EarthquakeWaveform + ": #" + trace.BuildingId;
            if (trace.HasQuake)
            {
                header += "   " + trace.DistanceToEpicentre.ToString("F0") + " m"
                          + "   (#" + snapshot.WaveformQuakeId + ")";
            }
            else
            {
                header += "   " + Strings.EarthquakeWaveformTremorSource
                          + " " + trace.DistanceToVolcano.ToString("F0") + " m";
            }
            if (traces.Count > 1)
            {
                header += "   (" + Strings.EarthquakeSensorSection + ": " + traces.Count + ")";
            }

            // ★★ When only a volcano is shaking, **do not print a layer-1 peak amplitude
            //    of 0.00 here** — that 0 turns into "an earthquake is happening and it is
            //    not shaking". The size of the shaking is declared by the layer-3 row
            //    below.
            if (trace.Count > 0 && trace.HasQuake)
            {
                // The vertical axis is normalised by the peak amplitude, so state that
                // peak as a number too. Without it, people compare earthquake strength
                // from the graph's height alone.
                //
                // ★ The bar's full scale is the theoretical maximum displacement of 0.60,
                //    not s (0-1) (whole-feature review I4). Back when it reused s's scale,
                //    a quantity that never exceeds 0.6 was drawn on a 0-1 scale, so only
                //    one or two cells were ever filled — and it was indistinguishable
                //    from the s bar directly above it. The full scale is printed as a
                //    number alongside.
                float peak = trace.PeakAbsolute;
                header += "\n" + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]";
            }
            else if (trace.Count <= 0)
            {
                // There are stations. The shaking window (§A-7's e > 0) simply has not
                // opened yet. "There are no samples" and "all the samples are 0" are
                // different things, so never print 0.00 here.
                header += "   " + WaitingReason(snapshot);
            }

            EarthquakeRows.SetLayer1(_waveformLabel, header);
            RefreshWaveformModelRow(snapshot, trace);
            RefreshWaveformTremorRow(trace);

            // The note is only attached while the graph (or the peak-amplitude row) is
            // showing.
            WaveformView.Render(trace);
        }

        /// <summary>
        /// **The synthetic seismogram row** (layer 2). Blanked when there is no recording.
        ///
        /// ★ <b>Never write this row with <c>SetLayer1</c>.</b> The value shown here is
        ///   not something vanilla computes but a waveform <c>SeismogramModel</c> built
        ///   from that earthquake's seed. Because the row directly above (the other line
        ///   on the same graph) declares itself <c>[measured]</c>, getting this wrong
        ///   makes for a much bigger lie.
        ///
        /// Three things are shown:
        ///   - the peak amplitude (full scale is the same <c>MaxDisplacement</c> as
        ///     vanilla's line)
        ///   - the S-P interval, the duration of the preliminary tremor (**it widens with
        ///     hypocentral distance**, which is this model's headline feature)
        ///   - a legend saying which colour is which layer
        /// </summary>
        private static void RefreshWaveformModelRow(EarthquakeSnapshot snapshot,
                                                    SeismographTrace trace)
        {
            if (!trace.HasModel)
            {
                EarthquakeRows.SetPlain(_waveformModelLabel, "");
                return;
            }

            float peak = trace.ModelPeakAbsolute;
            string text = Strings.EarthquakeWaveformModel + ": "
                          + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]";

            // ★ Do not show S-P when the window (m_activeDuration) could not be read. The
            //   model's arrival times are derived from the window's length, so with no
            //   window there is no number.
            if (snapshot.Prefab.Resolved && snapshot.Prefab.ActiveDuration != 0u)
            {
                var model = SeismogramModel.For(
                    DeterministicRandom.Hash(snapshot.WaveformQuakeId,
                                             ActivationFrameOf(snapshot)),
                    snapshot.Prefab.ActiveDuration);

                if (model.Valid)
                {
                    text += "   " + Strings.EarthquakeWaveformSMinusP + " "
                            + model.SMinusPFrames(trace.DistanceToEpicentre).ToString("F0")
                            + " " + Strings.EarthquakeFrames;
                }
            }

            EarthquakeRows.SetLayer2(_waveformModelLabel, text);
        }

        /// <summary>
        /// **Layer 3: the volcanic tremor row** (2026-08-22, at the owner's request).
        ///
        /// ★★ The line here is <b>neither a value the game computes nor the displacement
        /// the camera actually applied</b>. It is this mod's own model,
        /// <c>Core/Volcano/VolcanicTremor</c>, evaluated at the seismograph's position on
        /// **the seismogram's time axis (sim frames)** — the camera evaluates it in real
        /// time; the difference between the two is set out in
        /// <c>Game/Volcano/VolcanoTremorTrace</c>'s class doc. That is why it is treated
        /// exactly like layer 2, with <c>SetLayer2</c>'s prefix and colour declaring it
        /// every time.
        /// </summary>
        private static void RefreshWaveformTremorRow(SeismographTrace trace)
        {
            if (!trace.HasTremor)
            {
                EarthquakeRows.SetPlain(_waveformTremorLabel, "");
                return;
            }

            float peak = trace.TremorPeakAbsolute;
            string text = Strings.EarthquakeWaveformTremor + ": "
                          + peak.ToString("F2")
                          + " / " + ShakeWaveform.MaxDisplacement.ToString("F2")
                          + "  [" + SeismicScale.BarOf(
                              ShakeWaveform.NormalisedDisplacement(peak)) + "]"
                          + "   " + trace.DistanceToVolcano.ToString("F0") + " m";

            EarthquakeRows.SetLayer2(_waveformTremorLabel, text);
        }

        /// <summary>
        /// The activation frame of the earthquake whose waveform is being recorded.
        /// **It is needed to build the seed** (unless it is the same combination as
        /// <c>SeismographRecorder.SeismogramSeed</c>, the S-P shown disagrees with the
        /// line that is drawn). 0 if it cannot be found — <c>SeismogramModel.For</c> then
        /// returns a different shape, but the order of magnitude of S-P is set by the
        /// distance, so the display does not break.
        /// </summary>
        private static uint ActivationFrameOf(EarthquakeSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Quakes.Count; i++)
            {
                if (snapshot.Quakes[i].DisasterId != snapshot.WaveformQuakeId) continue;
                return snapshot.Quakes[i].ActivationFrame;
            }
            return 0u;
        }

        /// <summary>
        /// Why there is not a single sample yet. **Never say "it is not shaking".**
        ///
        /// There are three reasons recording cannot happen, and since none of them is a
        /// reading, the wording chosen carries no source prefix.
        /// <c>m_activeDuration</c> is a prefab value that **nobody has yet measured**
        /// (§A-0), so it genuinely may not have been read.
        /// </summary>
        private static string WaitingReason(EarthquakeSnapshot snapshot)
        {
            if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u)
            {
                // We do not know the shaking window. Fill it in with a hard-coded guess
                // and you get a waveform that keeps extending after the earthquake has
                // ended (the same judgement as CameraShakeBooster).
                return Strings.EarthquakeUnavailable;
            }

            for (int i = 0; i < snapshot.Quakes.Count; i++)
            {
                if (snapshot.Quakes[i].DisasterId != snapshot.WaveformQuakeId) continue;
                return snapshot.Quakes[i].ActivationScheduled
                    ? Strings.EarthquakePhaseEmerging     // Before the main shock; the shaking window has not opened.
                    : Strings.EarthquakeTimeUnknown;      // SelfTrigger is not set (§A-1).
            }

            return Strings.EarthquakePhaseEmerging;
        }
    }
}
