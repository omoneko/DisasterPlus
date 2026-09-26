using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The status rows for each stage of a volcano in progress. **Main thread only.**
    /// T5 put in the clearing stage, and **T6–T9 add their stages to this type.**
    ///
    /// Both creating a row and putting text into it go through <see cref="VolcanoRows"/>.
    /// **There is not one <c>UILabel</c> construction or <c>.text</c> assignment in this file.**
    ///
    /// ── only one row carries the provenance marker ─────────────────────────────────────────
    ///
    /// The radius swept and the numbers of buildings and roads destroyed are **⑤'s own tally**.
    /// They are not values the game computed, so <c>VolcanoRows.SetMeasured</c> is not called.
    ///
    /// ★★ <b>The one exception is the "affected range" row</b> (<see cref="RefreshFootprint"/>).
    /// That is a value the survey merely counted out of the game's arrays
    /// (<c>m_buildingGrid</c> / <c>m_segmentGrid</c>); ⑤ computed none of it.
    /// **It is the row that belonged to the confirmation window removed on 2026-08-21** —
    /// the window is gone, but the place to read "what got caught up in it" stays
    /// (grep 5 in <see cref="VolcanoRows"/>).
    ///
    /// ── it goes at the very bottom of the panel ────────────────────────────────────────────
    ///
    /// This set is the bottom of the panel, and when it is not shown the panel's height is shrunk
    /// to <see cref="BlockTop"/> — <c>relativePosition</c> is absolute, so merely hiding them
    /// leaves the blank space behind.
    ///
    /// ── a conditional note takes up no space when it does not apply ────────────────────────
    ///
    /// Just putting an empty string in makes it look like "somewhere that should show something
    /// is empty", so everything is re-stacked each time with <c>Reflow</c>.
    ///
    /// ── ★★ the [Stop] button was removed (2026-08-22) ─────────────────────────────────────
    ///
    /// The owner's call:
    ///
    /// &gt; A stop button isn't needed. After all, you can't actually stop an eruption in real
    /// &gt; life, can you?
    ///
    /// Quite so, and **⑤ is a disaster that, once triggered, runs to the end** (the same as the
    /// vanilla disasters). There is not one disaster in the game that can be folded away midway.
    ///
    /// ★ This used to say "someone watching their own city being destroyed needs a way to stop
    ///   it" (whole-project review I5). **That premise was rejected**, so the receiving end
    ///   (<c>VolcanoRequest.Stop</c> / <c>VolcanoState.HandleStop</c>) was deleted with it —
    ///   do not leave a receiving end for a request nobody can queue.
    ///   If you really must stop it, turn off the "enable volcanoes" setting
    ///   (a half-carved mountain stays, but that is not an "undo").
    ///
    /// ★★ **All that goes here is "what is happening right now"** (2026-08-22).
    ///
    /// The owner's request: "I don't think the detailed volcano explanations and debug info need
    /// showing in-game inside the D+ tab".
    ///
    /// There used to be 24 rows. Most of them were **explanations of the behaviour when things
    /// are working normally** (why the trees do not catch fire, why the roads do not burn, the
    /// lag of the buildable ground, the disclaimer about the pyroclastic stand-in…) and
    /// **in-progress counters** (numbers destroyed, the active radius, the tile count, the number
    /// of points drawn…), and neither is something to read while playing.
    ///
    /// ★ <b>Nothing has been thrown away.</b> It is all in the diagnostic dump in
    ///   <c>VolcanoFeature.WriteDiagnostics</c> and can be written out with one button on the
    ///   diagnostics tab.
    ///   What this mod forbids is "failing silently", not "showing everything on the screen you
    ///   are playing on".
    ///
    /// ★ <b>Only two kinds were kept</b>: the state of the current stage, and **the rows that
    ///   name a failure**. The latter only appear when something is actually broken, so normally
    ///   they take up no space at all.
    /// </summary>
    internal static class VolcanoEffectRows
    {
        /// <summary>The height of an explanatory row (assuming it wraps to three lines).</summary>
        private const float NoteHeight = 52f;

        /// <summary>The height of a long note (the roads disclaimer and the eruption note. Four or five lines).</summary>
        private const float LongNoteHeight = 72f;

        private static UILabel _footprintLabel;
        private static UILabel _clearingLabel;
        private static UILabel _upliftLabel;
        private static UILabel _quakeLabel;
        private static UILabel _eruptionLabel;
        private static UILabel _lavaLabel;

        // The three rows that name a failure. **They only appear when something is actually broken.**
        private static UILabel _eruptionMissingLabel;
        private static UILabel _lavaNoMaterialLabel;
        private static UILabel _clearingPathLabel;

        private static float _blockTop;
        private static float _blockBottom;
        private static bool _showing;

        /// <summary>The y this set starts at (= the bottom of the panel when it is not shown).</summary>
        internal static float BlockTop { get { return _blockTop; } }

        /// <summary>The bottom of this set (= the bottom of the panel when it is shown).</summary>
        internal static float BlockBottom { get { return _blockBottom; } }

        /// <summary>Whether the last <see cref="Refresh"/> showed even one row.</summary>
        internal static bool IsShowing { get { return _showing; } }

        /// <summary>Once, when the panel is built. The rows are always created and shown or hidden by their content.</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            _blockTop = y;

            // ★★ **What is happening right now, and nothing else** (class doc).
            //    The explanations and the counters are in the diagnostic dump.

            // The affected range. It is a real count the survey took out of the game's arrays, so
            // it is the only row in ⑤ that carries [measured] (grep 5 in <c>VolcanoRows</c>).
            _footprintLabel = VolcanoRows.AddMeasuredRow(p, "EffectFootprint", ref y);

            _clearingLabel = VolcanoRows.AddRow(p, "EffectClearing", ref y);
            _upliftLabel = VolcanoRows.AddRow(p, "EffectUplift", ref y);

            // ★ The volcanic earthquakes run **from before the eruption (the uplift) through to
            //   after it (the cooling)**, so this goes above the eruption row (they are ordered
            //   in time).
            _quakeLabel = VolcanoRows.AddRow(p, "EffectQuake", ref y);
            _eruptionLabel = VolcanoRows.AddRow(p, "EffectEruption", ref y);
            _lavaLabel = VolcanoRows.AddRow(p, "EffectLava", ref y);

            // ── The three rows that name a failure. **They only take up space when something is
            //    broken.** Delete these three and you get the thing this mod avoids above all,
            //    "failing silently". That is a different matter from cutting down the
            //    explanations.
            _eruptionMissingLabel =
                VolcanoRows.AddRow(p, "EffectEruptionMissing", ref y, NoteHeight);
            _lavaNoMaterialLabel = VolcanoRows.AddRow(p, "EffectLavaNoMaterial", ref y,
                                                      NoteHeight);
            _clearingPathLabel = VolcanoRows.AddRow(p, "EffectClearingPath", ref y,
                                                    LongNoteHeight);

            _blockBottom = _blockTop;

            // Nothing is in progress right after building. **Hide it the moment it is created.**
            SetVisible(false);
        }

        /// <summary>
        /// Every frame while the panel is shown. <paramref name="s"/> may be null.
        /// </summary>
        internal static void Refresh(VolcanoSnapshot s)
        {
            if (s == null || !s.Valid)
            {
                SetVisible(false);
                _blockBottom = _blockTop;
                return;
            }

            // The disclaimer for an environment with no road path is shown regardless of phase.
            bool clearingPathBroken = !s.ClearingPathAvailable;

            // ★ The tally of a volcano whose uplift has finished **stays on screen after it
            //   finishes**. If everything vanished the instant the phase dropped to Done, there
            //   would never be a chance to read the outcome of "the mountain was made" (the
            //   summit height, the crater, the numbers destroyed).
            //   Start a new volcano or leave the city and Reset puts it back to 0.
            bool clearing = InProgress(s.Phase) || s.UpliftComplete;

            _showing = clearingPathBroken || clearing;
            if (!_showing)
            {
                SetVisible(false);
                _blockBottom = _blockTop;
                return;
            }

            SetVisible(true);
            float y = _blockTop;

            if (clearing)
            {
                y = RefreshFootprint(y, s);

                y = ReflowRow(y, _clearingLabel,
                    Strings.VolcanoClearingRow + ": " + Strings.VolcanoClearedRadius + " "
                    + s.ClearedRadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                    + " / " + s.Footprint.RadiusMetres.ToString("F0") + " "
                    + Strings.VolcanoMetres);

                y = RefreshUplift(y, s);
                y = RefreshQuake(y, s);
                y = RefreshEruption(y, s);
                y = RefreshLava(y, s);
            }
            else
            {
                y = ReflowMeasured(y, _footprintLabel, "");
                y = ReflowRow(y, _clearingLabel, "");
                y = ReflowRow(y, _upliftLabel, "");
                y = ReflowRow(y, _quakeLabel, "");
                y = ReflowRow(y, _eruptionLabel, "");
                y = ReflowNote(y, _eruptionMissingLabel, "");
                y = ReflowRow(y, _lavaLabel, "");
                y = ReflowNote(y, _lavaNoMaterialLabel, "");
            }

            y = Reflow(y, _clearingPathLabel,
                clearingPathBroken ? Strings.VolcanoClearingPathUnavailable : "",
                LongNoteHeight, LongNoteHeight + 4f);

            _blockBottom = y;
        }

        /// <summary>
        /// **The two affected-range rows. Exactly the content the removed confirmation window
        /// held.**
        ///
        /// ★ The first row holds the building count and road segment count the survey took out of
        ///   the game's arrays, and it is the only row in ⑤ where <c>SetMeasured</c> may be called
        ///   (grep 5 in the class doc of <see cref="VolcanoRows"/>).
        ///   The numbers are **those of the moment it was surveyed**, so if the city moves while
        ///   the ground is being levelled they will differ from the number actually removed
        ///   (<c>note: counts</c> in the diagnostic dump).
        ///
        /// ★ For roads, **do not mix "0 of them" with "could not be counted".** When it could not
        ///   be counted, no marker is applied (do not present a value we could not read as a
        ///   measurement from the game).
        ///
        /// ★ The second row holds only the conditional notes that "change the outcome" — the
        ///   sweep being cut short, and being cut down by the 1024 m ceiling. If they do not
        ///   apply, they take up no space.
        /// </summary>
        private static float RefreshFootprint(float y, VolcanoSnapshot s)
        {
            VolcanoFootprint f = s.Footprint;
            if (!f.Valid) return ReflowMeasured(y, _footprintLabel, "");

            string body = Strings.VolcanoFootprintRow + ": "
                          + FormLabel(f.Form)
                          + "    " + Strings.VolcanoRadiusRow + " "
                          + f.RadiusMetres.ToString("F0") + " " + Strings.VolcanoMetres
                          + "    " + Strings.VolcanoHeightRow + " "
                          + f.HeightMetres.ToString("F0") + " " + Strings.VolcanoMetres
                          + "    " + Strings.VolcanoBuildingsRow + " " + f.BuildingCount
                          + " / " + Strings.VolcanoSegmentsRow + " "
                          + (f.SegmentCount < 0 ? "?" : f.SegmentCount.ToString());

            // ★★ **Append to the row only when the summit was clipped by the game's height
            //    ceiling (1024 m).** This is not an "explanation of normal behaviour" but a
            //    difference in the outcome: **the height you set does not arrive as set**
            //    (the "rows that name a failure" side of the class doc).
            //    The reason a mod cannot raise the ceiling is in the doc of
            //    <c>UpliftSchedule.CeilingClipped</c>.
            //    Append it to the end rather than adding a row.
            if (f.HeightLimitedByCeiling) body += "   " + Strings.VolcanoHeightLimited;

            if (f.SegmentCount < 0)
            {
                // It could not be counted, so no marker is applied. **Do not present a value we
                // could not read as a measurement from the game.**
                y = ReflowRow(y, _footprintLabel, body + "   " + Strings.VolcanoSegmentsUnknown);
            }
            else
            {
                y = ReflowMeasured(y, _footprintLabel, body);
            }

            return y;
        }

        /// <summary>
        /// The display name of the form. **It matters that this is a method** —
        /// make it a <c>static readonly string[]</c> and it freezes in the language the game
        /// started in (the class doc of <c>Strings</c>).
        /// </summary>
        private static string FormLabel(DisasterPlus.Core.Volcano.VolcanoForm form)
        {
            switch (form)
            {
                case DisasterPlus.Core.Volcano.VolcanoForm.Shield: return Strings.VolcanoFormShield;
                case DisasterPlus.Core.Volcano.VolcanoForm.Dome: return Strings.VolcanoFormDome;
                default: return Strings.VolcanoFormStrato;
            }
        }

        /// <summary>
        /// The four uplift rows (T6). **They are not shown during the clearing stage** — a row
        /// reading 0 % progress does not mean "not advancing" but "that stage has not been
        /// entered yet".
        ///
        /// ★★ <b>The active radius must always carry "as far as the clearing has reached"</b>
        /// (<c>Strings.VolcanoActiveRadiusRow</c> holds that wording).
        /// This is the visualisation of trap 1, and the only row that lets you verify with your
        /// own eyes in the live game that "if the clearing stops, the uplift stops too".
        /// </summary>
        private static float RefreshUplift(float y, VolcanoSnapshot s)
        {
            bool uplifting = s.Phase != VolcanoPhase.Clearing;
            if (!uplifting) return ReflowRow(y, _upliftLabel, "");

            y = ReflowRow(y, _upliftLabel,
                Strings.VolcanoUpliftRow + ": " + Strings.VolcanoUpliftProgress + " "
                + (s.ProgressUnit * 100f).ToString("F0") + "%"
                // ★ Leave the sign to the number. Back when a "+" was hard-coded, a caldera
                //   foundering was displayed as "+-900 m".
                + "    " + Strings.VolcanoSummitRow + ": "
                + (s.SummitMetres >= 0f ? "+" : "")
                + s.SummitMetres.ToString("F0") + " " + Strings.VolcanoMetres
                + " / " + s.Footprint.HeightMetres.ToString("F0") + " "
                + Strings.VolcanoMetres);

            return y;
        }

        /// <summary>
        /// The two eruption rows (T7). **Not shown until the eruption stage is entered** (handled
        /// the same way as the four uplift rows).
        ///
        /// ★ The strength is reported as <b>a stage from 0 to 10</b>. ⑤ holds neither a
        ///   temperature nor an ejecta volume, and must not quote a real physical unit
        ///   (design doc §7.4 / item 5 of the plan's "the range of assertions we may make").
        ///   No marker (<c>[measured]</c>) is applied either — this is a quantity ⑤ chose, not a
        ///   value the game computed.
        ///
        /// ★ The note is shown **only during the eruption stage**. Show it always and you would
        ///   be saying "the game has no lava" for the whole time the mountain is being built.
        /// </summary>
        /// <summary>
        /// The single volcanic-earthquake row. **A shaking strength of 0 to 10**, which is neither
        /// a real seismic intensity nor a magnitude (it is a quantity ⑤ chose. The discipline of
        /// design doc §7.4).
        ///
        /// ★ What it reads is the value <c>VolcanoTremorShake</c> wrote on the main thread, and
        ///   this panel is on the main thread too (<c>VolcanoPanel.Tick</c>).
        ///
        /// ★ While it is turned off in the settings, **the row is not shown at all** —
        ///   "0 / 10" means "not shaking", not "turned off".
        /// </summary>
        private static float RefreshQuake(float y, VolcanoSnapshot s)
        {
            if (!ModSettings.VolcanoQuake.value)
            {
                return ReflowRow(y, _quakeLabel, "");
            }

            float activity = VolcanoTremorShake.ActivityUnit;
            if (!(activity > 0f))
            {
                return ReflowRow(y, _quakeLabel, "");
            }

            return ReflowRow(y, _quakeLabel,
                Strings.VolcanoQuakeRow + ": " + (activity * 10f).ToString("F1") + " / 10");
        }

        private static float RefreshEruption(float y, VolcanoSnapshot s)
        {
            // ★ T8 narrowed the phases. Shown only during the eruption stage — while the lava is
            //   flowing, the four rows below (<see cref="RefreshLava"/>) use that space.
            bool erupting = s.Phase == VolcanoPhase.Erupting;

            if (!erupting)
            {
                y = ReflowRow(y, _eruptionLabel, "");
                return ReflowNote(y, _eruptionMissingLabel, "");
            }

            // A stage from 0 to 10. Round plainly, so that 0.0 is not called "stage 1".
            float stage = s.EruptionIntensityUnit * 10f;

            y = ReflowRow(y, _eruptionLabel,
                Strings.VolcanoEruptionRow + ": " + stage.ToString("F1") + " / 10");

            // ★ Only disclaim when it could not be looked up. **Do not make people read it every
            //   time in an environment where it resolves.**
            //   Do not show it when it is merely turned off in the settings either (do not mix
            //   "turned off" with "cannot be shown in this environment").
            bool fxOn = ModSettings.VolcanoEruptionFx.value;
            y = ReflowNote(y, _eruptionMissingLabel,
                fxOn && !VolcanoEruptionFx.Facts.EruptionUsable
                    ? Strings.VolcanoEffectsMissing : "");

            return y;
        }

        /// <summary>
        /// The four lava rows (T8). **Not shown until the lava stage is entered.**
        ///
        /// ★ The note about trees is shown <b>only when the ND DLC is not owned</b> (§B-7c).
        ///   Show it always in an environment that owns it and you make people read about a
        ///   constraint that is not even in play.
        /// ★ The note about roads is shown throughout the lava stage — "the roads under the lava
        ///   do not burn" is a fact that will certainly be noticed, and this is the only place we
        ///   can explain that the game has no API for it.
        /// </summary>
        private static float RefreshLava(float y, VolcanoSnapshot s)
        {
            bool flowing = s.Phase == VolcanoPhase.Flowing || s.Phase == VolcanoPhase.Cooling;

            if (!flowing || s.LavaFlowCount <= 0)
            {
                y = ReflowRow(y, _lavaLabel, "");
                return ReflowNote(y, _lavaNoMaterialLabel, "");
            }

            y = ReflowRow(y, _lavaLabel,
                Strings.VolcanoLavaRow + ": " + s.LavaAliveCount + " / " + s.LavaFlowCount
                + "    " + Strings.VolcanoLavaLongest + ": "
                + s.LavaLongestMetres.ToString("F0") + " " + Strings.VolcanoMetres);

            // Explain only when the material could not be built. **Say at the same time that the
            //   flow, the scorching and the ignition are unchanged**
            //   (Strings.VolcanoLavaNoMaterial).
            //   Do not show it when the rendering is turned off in the settings —
            //   do not mix "turned off" with "cannot be shown in this environment".
            y = ReflowNote(y, _lavaNoMaterialLabel,
                ModSettings.VolcanoLavaRender.value && !VolcanoLavaFx.MaterialResolved
                    ? Strings.VolcanoLavaNoMaterial : "");
            return y;
        }


        /// <summary>
        /// Whether the phase counts as "in progress". **The seven that follow the start of
        /// destruction.**
        /// Keep it the same set as <c>VolcanoState.InProgress</c> — let them drift and the screen
        /// will call a volcano in progress "finished".
        /// </summary>
        private static bool InProgress(VolcanoPhase phase)
        {
            return phase == VolcanoPhase.Clearing
                   || phase == VolcanoPhase.Uplifting
                   || phase == VolcanoPhase.Inflating
                   || phase == VolcanoPhase.Collapsing
                   || phase == VolcanoPhase.Erupting
                   || phase == VolcanoPhase.Flowing
                   || phase == VolcanoPhase.Cooling;
        }

        private static float ReflowRow(float y, UILabel label, string text)
        {
            return Reflow(y, label, text, VolcanoRows.RowHeight, VolcanoRows.RowStep);
        }

        /// <summary>
        /// A row that carries the [measured] marker. **The prefix is applied by
        /// <c>VolcanoRows.SetMeasured</c>**, so here we only pass through when the text is empty
        /// (so an empty row is never left with nothing but the marker).
        /// </summary>
        private static float ReflowMeasured(float y, UILabel label, string body)
        {
            if (label == null) return y;
            if (string.IsNullOrEmpty(body)) return ReflowRow(y, label, "");

            VolcanoRows.SetMeasured(label, body);
            label.isVisible = true;
            label.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            label.height = VolcanoRows.RowHeight;
            return y + VolcanoRows.RowStep;
        }

        private static float ReflowNote(float y, UILabel label, string text)
        {
            return Reflow(y, label, text, NoteHeight, NoteHeight + 4f);
        }

        private static float Reflow(float y, UILabel label, string text,
                                    float height, float step)
        {
            if (label == null) return y;

            VolcanoRows.SetPlain(label, text);

            if (string.IsNullOrEmpty(text))
            {
                label.isVisible = false;
                return y;
            }

            label.isVisible = true;
            label.relativePosition = new Vector3(VolcanoRows.RowLeft, y);
            // ★ Pass the same height that was given at build time — wordWrap is decided by the
            //   height at build time (VolcanoRows.AddLabel).
            label.height = height;
            return y + step;
        }

        /// <summary>
        /// Show or hide the whole set at once.
        ///
        /// ★ <b>Do not miss a single row of this set.</b> Up to T6 the four uplift rows were not
        ///   in this list, so on the frame the phase folded away **the stale uplift rows were
        ///   left behind** (<see cref="Refresh"/> returns immediately in the <c>false</c> branch,
        ///   so it never goes through the path that puts empty strings in). When the two eruption
        ///   rows were added in T7, the four uplift rows were put in here as well.
        /// </summary>
        private static void SetVisible(bool visible)
        {
            _showing = visible;
            SetLabelVisible(_footprintLabel, visible);
            SetLabelVisible(_clearingLabel, visible);
            SetLabelVisible(_upliftLabel, visible);
            SetLabelVisible(_quakeLabel, visible);
            SetLabelVisible(_eruptionLabel, visible);
            SetLabelVisible(_eruptionMissingLabel, visible);
            SetLabelVisible(_lavaLabel, visible);
            SetLabelVisible(_lavaNoMaterialLabel, visible);
            SetLabelVisible(_clearingPathLabel, visible);
        }

        private static void SetLabelVisible(UILabel label, bool visible)
        {
            if (label == null) return;
            label.isVisible = visible;
        }

        /// <summary>
        /// On level unload. **Just drop the references** (the objects themselves go with the
        /// panel's GameObject). Carry them over and you write into destroyed labels in the next
        /// city.
        /// </summary>
        internal static void Destroy()
        {
            _footprintLabel = null;
            _clearingLabel = null;
            _upliftLabel = null;
            _quakeLabel = null;
            _eruptionLabel = null;
            _eruptionMissingLabel = null;
            _lavaLabel = null;
            _lavaNoMaterialLabel = null;
            _clearingPathLabel = null;
            _blockTop = 0f;
            _blockBottom = 0f;
            _showing = false;
        }
    }
}
