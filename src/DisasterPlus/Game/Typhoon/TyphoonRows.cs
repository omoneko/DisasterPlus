using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The only place that builds rows** for the typhoon panel, and **the only place
    /// that puts text into a row**. Main thread only. Same shape as ②'s
    /// <see cref="EarthquakeRows"/>, but **the prefix convention is different**.
    ///
    /// ── ④'s display convention (unlike ②; design doc §1.2 / §7.1) ────────
    ///
    /// ① showed vanilla's hazard map and ② showed vanilla's deterministic damage model.
    /// **④ has no such source material.** Neither the typhoon phenomenon, nor wind
    /// destruction, nor clouds with world coordinates, nor a flood disaster exists in
    /// vanilla (IL facts document §B5 / §C7 / §D9). In other words the numbers ④ shows
    /// are **as a rule all of them this mod's own**, and if everything has the same
    /// provenance then a per-row marker carries no information.
    ///
    ///   - The headings that named the provenance have been **taken off the panel**
    ///     (2026-08-22, at the owner's request: "the many explanations on the typhoon
    ///     tab are not needed either"). What still names itself is the marker for the
    ///     "one exception" below, and the diagnostics dump
    ///   - <b>No per-row markers</b> (<see cref="AddRow(UIPanel,string,ref float)"/> /
    ///     <see cref="SetPlain"/>)
    ///   - The <b>one exception</b> is the rainfall and cloud cover read out of
    ///     <c>WeatherManager</c>. Those are vanilla's own measured values, just as in ①,
    ///     so they get <c>Strings.SourceVanilla</c>
    ///     (<see cref="AddMeasuredRow"/> / <see cref="SetMeasured"/>)
    ///
    /// ── Guarantees you can check mechanically (five greps) ────────────────
    ///
    /// ★ **The commands and the counts have actually been run and made to agree**
    ///   (whole-project review). The procedure that used to be written here hit the doc
    ///   comments themselves when grepped naively, and did not match the counts written
    ///   down — the sentence stating the rule was a counter-example to the rule
    ///   (the line saying <c>Strings.SourceModel</c> "must never appear once" had
    ///   <c>Strings.SourceModel</c> written on it). **A procedure that does not agree
    ///   teaches reviewers the habit of skimming past hits by hand**, so it has been
    ///   rewritten to exclude comment lines. Run all of these from the repository root.
    ///
    /// <code>
    /// # The target is all of ④'s display code (Game/Typhoon/. The button belongs to
    /// # DisasterPanelBar, so it is no longer part of ④'s display code.)
    /// #   T='src/DisasterPlus/Game/Typhoon'
    /// #   grep -v '///' drops the doc comments (this doc itself would otherwise match).
    ///
    /// # 1. AddLabel is the only place that creates a UILabel                 -> 1
    /// grep -rn --include=*.cs "AddUIComponent(typeof(UILabel))" $T | grep -v '///' | wc -l
    ///
    /// # 2. SetPlain is the only place that assigns to UILabel.text           -> 1
    /// grep -rn --include=*.cs "label.text = " $T | grep -v '///' | wc -l
    ///
    /// # 3. SetMeasured is the only place that references Strings.SourceVanilla -> 1
    /// grep -rn --include=*.cs "Strings.SourceVanilla" $T | grep -v '///' | wc -l
    ///
    /// # 4. Strings.SourceModel never appears at all                         -> 0
    /// grep -rn --include=*.cs "Strings.SourceModel" $T | grep -v '///' | wc -l
    ///
    /// # 5. SetMeasured is called only for the rainfall and cloud rows       -> 2
    /// grep -rn --include=*.cs "TyphoonRows.SetMeasured(" $T | grep -v '///' | wc -l
    /// </code>
    ///
    /// The only five ways to create a row are <see cref="AddTitleRow"/> /
    /// <see cref="AddSectionHeader"/> /
    /// <see cref="AddRow(UIPanel,string,ref float)"/> /
    /// <see cref="AddRow(UIPanel,string,ref float,float)"/> /
    /// <see cref="AddMeasuredRow"/>.
    ///
    /// <see cref="SetMeasured"/> may only be called for <b>the rainfall and cloud rows,
    /// and nothing else</b>. If any other row claims the measured marker, ④ is lying
    /// that "vanilla calculated this".
    ///
    /// **Why ②'s <see cref="EarthquakeRows"/> is not reused.** That one has two prefixes
    /// ④ must not use, <c>SetLayer1</c> / <c>SetLayer2</c>, baked into its type, and it
    /// burns <c>"Earthquake"</c> into the label names. Generalising and sharing it would
    /// mean rebuilding the grep guarantees ②'s review settled, to suit ④.
    ///
    /// **There is only one colour.** ② had two layers so it distinguished them by
    /// colour; ④ has only one layer. A second colour would be a false signal that "the
    /// colour means something".
    /// </summary>
    internal static class TyphoonRows
    {
        /// <summary>
        /// Panel width. 640, the same as ②'s <see cref="EarthquakeRows.PanelWidth"/>.
        ///
        /// **Height is precious and width is not.** The UIView coordinate system is
        /// normalised to a height of 1080, so there was never any room to grow
        /// vertically. ④ has a lot of explanatory text (provenance headings, the basis
        /// for the landfall forecast, the constraints on wind direction), and a narrow
        /// panel doubles the line count on that alone.
        /// </summary>
        internal const float PanelWidth = 640f;

        /// <summary>Left edge of a row. The margin from the panel's left edge.</summary>
        internal const float RowLeft = 12f;

        /// <summary>The width of one row. <see cref="RowLeft"/> of margin on each
        /// side.</summary>
        internal const float RowWidth = PanelWidth - 2f * RowLeft;

        /// <summary>How far one non-wrapping row advances y.</summary>
        internal const float RowStep = 22f;

        /// <summary>The height of one non-wrapping row. Rows taller than this wrap
        /// automatically.</summary>
        internal const float RowHeight = 20f;

        /// <summary>④'s row colour. **There is only one** (class doc).</summary>
        private static readonly Color32 RowColor = new Color32(255, 255, 255, 255);

        // ── Label creation (this is the only place allowed to create a UILabel) ──

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Typhoon" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = RowColor;
            label.autoSize = false;
            // Keep explanatory text that does not fit on one line from being cut off.
            label.wordWrap = height > RowHeight;
            return label;
        }

        /// <summary>
        /// The panel heading. The only row whose position and width the caller decides;
        /// it exists so the width can be narrowed to avoid overlapping the close button
        /// (every other row lines up with <see cref="RowLeft"/> /
        /// <see cref="RowWidth"/>). The contents are put in by
        /// <see cref="SetPlain"/>.
        /// </summary>
        internal static UILabel AddTitleRow(UIPanel p, string suffix, float x, float y,
                                            float width, float height)
        {
            return AddLabel(p, suffix, x, y, width, height);
        }

        /// <summary>A section heading. Its contents are fixed at build time, so we put
        /// them in right here.</summary>
        internal static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y, string text)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight);
            SetPlain(label, "-- " + text + " --");
            y += 26f;
            return label;
        }

        /// <summary>
        /// An ordinary row. **It gets no provenance marker** — as a rule all of ④'s
        /// numbers are this mod's own, and the panel heading says so once (class doc).
        /// Put the contents in with <see cref="SetPlain"/>.
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// A wrapping row. **Not a new label-creation path** (it shares
        /// <see cref="AddLabel"/>). It exists for explanatory text that will not fit on
        /// one line.
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **A row holding a measured value read from <c>WeatherManager</c>.** In ④ only
        /// the rainfall and cloud rows may use this (class doc). Geometrically it is
        /// identical to <see cref="AddRow(UIPanel,string,ref float)"/>; **it has a
        /// separate name so that the call sites can be counted**.
        /// Write its contents only through <see cref="SetMeasured"/>.
        /// </summary>
        internal static UILabel AddMeasuredRow(UIPanel p, string suffix, ref float y)
        {
            return AddRow(p, suffix, ref y);
        }

        // ── Setting text (this is the only assignment to UILabel.text) ──────────

        internal static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>
        /// Write into a row holding a vanilla measured value. **The caller does not get
        /// to choose the prefix.** This is the only place in ④ that references
        /// <c>Strings.SourceVanilla</c>.
        /// </summary>
        internal static void SetMeasured(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }
    }
}
