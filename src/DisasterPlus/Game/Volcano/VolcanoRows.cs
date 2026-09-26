using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The only place the volcano panel's rows are created**, and **the only place text is put
    /// into a row**. Main thread only. The same shape as ④'s <see cref="TyphoonRows"/>, but
    /// **the rows that may claim [measured] are different**.
    ///
    /// ── ⑤'s display convention (design doc §7.4) ──────────────────────────────────────────
    ///
    /// ① showed vanilla's hazard map and ② vanilla's deterministic damage model.
    /// **⑤ has no such material to build on.** Volcanoes do not exist in vanilla, and there is
    /// not one prefab, material or shader for lava, magma or melt — not even in the DLL's string
    /// heap (IL facts doc §B-5). So the numbers ⑤ shows are **in principle all this mod's own**,
    /// and if everything has the same provenance a per-row marker carries no information.
    ///
    ///   - The heading stating provenance was **taken off the panel** (2026-08-22).
    ///     What states it now is the marker on "the one exception" below, and the diagnostic dump
    ///   - <b>No per-row markers</b> (<see cref="AddRow(UIPanel,string,ref float)"/> /
    ///     <see cref="SetPlain"/>)
    ///   - <b>The one exception</b> is a value merely counted out of the game's arrays that ⑤
    ///     computed nothing of: namely <b>the single "affected range" row on the volcano tab</b>
    ///     (the building count and the road segment count).
    ///     Only that one gets <c>Strings.SourceVanilla</c>
    ///     (<see cref="AddMeasuredRow"/> / <see cref="SetMeasured"/>)
    ///
    /// <see cref="SetMeasured"/> may be called for <b>that one row only</b>.
    /// The uplift progress, the lava's position and the crater's depth are **all numbers ⑤ chose**,
    /// not things the game computed. Review counts the call sites of <c>SetMeasured</c>.
    ///
    /// ★ **There used to be three rows** (ground height, building count, road count). The ground
    ///   height moved to the diagnostic dump when the confirmation window was first narrowed down,
    ///   and the buildings and roads were merged into one row. When **the confirmation window
    ///   itself was removed** on 2026-08-21, the remaining row moved to
    ///   <see cref="VolcanoEffectRows"/> (the volcano tab).
    ///   **Only where it lives has changed; neither the count nor the convention has.**
    ///
    /// ★ When the roads could not be counted (<c>SegmentCount &lt; 0</c>), emit it with
    ///   <see cref="SetPlain"/>. **Do not present a value we could not read as a measurement from
    ///   the game.**
    ///
    /// ── the mechanically checkable guarantee (five greps) ──────────────────────────────────
    ///
    /// ★ **The commands and the counts have actually been run and made to match.** ④'s review
    ///   found that a written procedure did not produce the written count — the sentences of the
    ///   rule (a doc comment) were being caught by the grep as counterexamples to the rule.
    ///   **A procedure that does not match trains the reviewer to skip hits by hand**, so they are
    ///   written to exclude the comment lines.
    ///   Run all of these from the repository root.
    ///
    /// <code>
    /// # The target is all of ⑤'s display code.
    /// #   V='src/DisasterPlus/Game/Volcano src/DisasterPlus/Game/UI/VolcanoPlacementTool.cs'
    /// #   (the button is held by DisasterPanelBar, so it is no longer ⑤'s display code)
    /// #   grep -v '///' drops the doc comments (because this doc itself gets caught).
    ///
    /// # 1. UILabel is created in exactly one place, AddLabel                 -> 1
    /// grep -rn --include=*.cs "AddUIComponent(typeof(UILabel))" $V | grep -v '///' | wc -l
    ///
    /// # 2. UILabel.text is assigned in exactly one place, SetPlain           -> 1
    /// grep -rn --include=*.cs "label.text = " $V | grep -v '///' | wc -l
    ///
    /// # 3. SetMeasured is the only place that references Strings.SourceVanilla     -> 1
    /// grep -rn --include=*.cs "Strings.SourceVanilla" $V | grep -v '///' | wc -l
    ///
    /// # 4. Strings.SourceModel never appears                                 -> 0
    /// grep -rn --include=*.cs "Strings.SourceModel" $V | grep -v '///' | wc -l
    ///
    /// # 5. SetMeasured is called for exactly one row (the volcano tab's "affected range")  -> 1
    /// grep -rn --include=*.cs "VolcanoRows.SetMeasured(" $V | grep -v '///' | wc -l
    /// </code>
    ///
    /// Rows can only be created through the four families
    /// <see cref="AddTitleRow"/> /
    /// <see cref="AddRow(UIPanel,string,ref float)"/> /
    /// <see cref="AddRow(UIPanel,string,ref float,float)"/> /
    /// <see cref="AddMeasuredRow"/>. A section heading is an ordinary row written
    /// through <see cref="SetSectionHeader"/> — unlike ④'s, it creates nothing.
    ///
    /// **Why ④'s <see cref="TyphoonRows"/> is not reused as a type.** That one bakes
    /// <c>"Typhoon"</c> into <c>UILabel.name</c>, and its class doc states ④'s own specific
    /// guarantee, "<c>SetMeasured</c> may be called for only the two rows, rainfall and cloud
    /// cover". Generalise it and share it, and the grep guarantee ④'s review settled would have to
    /// be rebuilt to suit ⑤ (the same call was made going from ② to ④).
    /// Design doc §5's "reuse ④'s <c>TyphoonRows</c>" is implemented as
    /// **meaning: copy the convention and the framework**.
    ///
    /// **There is only one colour.** ② had two layers so it separated the colours, but ⑤ has only
    /// one layer. Introduce a second colour and it becomes a false signal that "the colour means
    /// something".
    /// </summary>
    internal static class VolcanoRows
    {
        /// <summary>
        /// The panel width. 640, the same as ② and ④.
        ///
        /// **Vertical space is precious and horizontal space is going spare.** UIView's coordinate
        /// system is normalised to a height of 1080, so there was never any room to grow
        /// vertically. ⑤ has even more explanatory text than ④ (the provenance heading, the
        /// irreversibility warning, the explanation of the destruction, the catch-up lag), and a
        /// narrow width doubles the number of lines by itself.
        /// </summary>
        internal const float PanelWidth = 640f;

        /// <summary>The left edge of a row. The margin from the panel's left edge.</summary>
        internal const float RowLeft = 12f;

        /// <summary>The width of one row. A <see cref="RowLeft"/> margin is left on each side.</summary>
        internal const float RowWidth = PanelWidth - 2f * RowLeft;

        /// <summary>How far one non-wrapping row advances y.</summary>
        internal const float RowStep = 22f;

        /// <summary>The height of one non-wrapping row. A row taller than this wraps automatically.</summary>
        internal const float RowHeight = 20f;

        /// <summary>The colour of ⑤'s rows. **There is only one** (class doc).</summary>
        private static readonly Color32 RowColor = new Color32(255, 255, 255, 255);

        // ── creating labels (the only place a UILabel may be created) ───────────────────────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Volcano" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = RowColor;
            label.autoSize = false;
            // So that explanatory text too long for one line is not cut off partway.
            label.wordWrap = height > RowHeight;
            return label;
        }

        /// <summary>
        /// The panel heading. The only row whose position and width the caller decides; it exists
        /// so the width can be narrowed to avoid overlapping the close button (every other row
        /// lines up with <see cref="RowLeft"/> / <see cref="RowWidth"/>). The content is put in by
        /// <see cref="SetPlain"/>.
        /// </summary>
        internal static UILabel AddTitleRow(UIPanel p, string suffix, float x, float y,
                                            float width, float height)
        {
            return AddLabel(p, suffix, x, y, width, height);
        }

        /// <summary>
        /// Put it in with the section heading's decoration (<c>-- ... --</c>).
        /// **Do not make the caller write the decoration** — write it in two places and one day
        /// only one of them will change.
        /// </summary>
        internal static void SetSectionHeader(UILabel label, string text)
        {
            SetPlain(label, "-- " + text + " --");
        }

        /// <summary>
        /// An ordinary row. **It carries no provenance marker** — ⑤'s numbers are in principle all
        /// this mod's own, and the panel heading states that once (class doc).
        /// The content is put in with <see cref="SetPlain"/>.
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// A wrapping row. **It is not a new label-creation path** (it shares
        /// <see cref="AddLabel"/>). It exists for explanatory text too long for one line.
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **A row for a value merely counted out of the game's arrays.** In ⑤ this may be used
        /// for only the single "affected range" row on the volcano tab (class doc). Geometrically
        /// it is identical to <see cref="AddRow(UIPanel,string,ref float)"/>, and
        /// **it has a separate name so that the call sites can be counted**.
        /// Its content must only ever be written with <see cref="SetMeasured"/>.
        /// </summary>
        internal static UILabel AddMeasuredRow(UIPanel p, string suffix, ref float y)
        {
            return AddRow(p, suffix, ref y);
        }

        // ── setting text (the only place UILabel.text is assigned) ─────────────────────────

        internal static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>
        /// Writes a row holding a value merely read out of the game's arrays. **Do not let the
        /// caller choose the prefix.**
        /// This is the only place in ⑤ that references <c>Strings.SourceVanilla</c>.
        /// </summary>
        internal static void SetMeasured(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }

    }
}
