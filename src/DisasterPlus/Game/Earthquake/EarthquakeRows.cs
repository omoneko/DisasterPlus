using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The only place the earthquake panel's rows are made**, and **the only place text
    /// is put into them**. Main thread only (the same as <see cref="EarthquakePanel"/>).
    ///
    /// ── Why it was pulled out into a type ───────────────────────────────
    ///
    /// This feature's honesty rests on never confusing layer 1 (values measured out of
    /// vanilla) with layer 2 (things this mod invented). Layer 1's whole-feature review
    /// left that guarantee in a form you could only verify by grepping inside the single
    /// file <c>EarthquakePanel.cs</c>, and handed the next person a note saying: "when
    /// layer 2 adds its rows, pull out a small type that owns nothing but label creation
    /// and <c>.text</c> assignment, and move the guarantee into the type"
    /// (the "tensions left behind" section of <c>layer1-fix-report.md</c>).
    /// **Task 9 is that moment.**
    ///
    /// ── The guarantee, checkable mechanically (three greps) ─────────────
    ///
    ///   1. <c>AddUIComponent(typeof(UILabel))</c> appears in exactly one place,
    ///      <see cref="AddLabel"/>. Labels can only be made through the four families
    ///      <see cref="AddSectionHeader"/> / <see cref="AddPlainRow"/> /
    ///      <see cref="AddLayer1Row(UIPanel,string,ref float)"/> /
    ///      <see cref="AddLayer2Row(UIPanel,string,ref float)"/>.
    ///   2. Assignment to <c>UILabel.text</c> appears in exactly one place,
    ///      <see cref="SetPlain"/>.
    ///   3. <c>Strings.SourceVanilla</c> / <c>Strings.SourceModel</c> appear only inside
    ///      <see cref="SetLayer1"/> / <see cref="SetLayer2"/>.
    ///      **The caller never gets to choose the prefix** — leave it free to declare
    ///      itself as either and this separation will break sooner or later.
    ///
    /// Splitting the files does not weaken the guarantee. What was discipline within one
    /// file has merely become **a grep across the whole assembly** for "no <c>UILabel</c>
    /// creation and no <c>.text</c> assignment exists outside this type" — if anything
    /// the check is stronger now.
    ///
    /// **Do not rely on colour alone.** The prefix, the section heading and the colour
    /// are all used together. This guarantee is not staked on a distinction that colour
    /// vision or a different UI theme could erase.
    /// </summary>
    internal static class EarthquakeRows
    {
        /// <summary>
        /// The panel's width. 420 → (whole-feature review) 520 → (intensity overlay) **640**.
        ///
        /// **Vertical space is precious, horizontal space is going spare.** UIView's
        /// coordinate system is normalised to a height of 1080, so there was never any
        /// room to grow downwards. x=600 + 640 = 1240, on the other hand, fits inside
        /// 16:9 (1920 wide), 4:3 (1440) and 5:4 (1350) alike, and does not overlap ①'s
        /// forecast panel (x=200, width 380, so its right edge is 580).
        ///
        /// Going from 520 to 640 wide fits about 24% more characters per line, so the
        /// same explanatory text takes fewer rows.
        ///
        /// **The vertical problem itself was solved with tabs**
        /// (<see cref="InfoHub"/>). There is no longer any need to widen
        /// the panel further to buy vertical space.
        /// </summary>
        internal const float PanelWidth = 640f;

        /// <summary>A row's left edge: the margin from the left edge of the panel (or page).</summary>
        internal const float RowLeft = 12f;

        /// <summary>The width of one row. A <see cref="RowLeft"/> margin is left on each side.</summary>
        internal const float RowWidth = PanelWidth - 2f * RowLeft;

        /// <summary>How far one non-wrapping row advances y.</summary>
        internal const float RowStep = 22f;

        /// <summary>The height of one non-wrapping row. Rows taller than this wrap automatically.</summary>
        internal const float RowHeight = 20f;

        /// <summary>Layer 1 = a quantity vanilla actually computes.</summary>
        private static readonly Color32 Layer1Color = new Color32(255, 255, 255, 255);

        /// <summary>
        /// Layer 2 = a number this mod invented. Clearly distinct from white, but
        /// **colour alone is not relied on** (the prefix and the section heading do the
        /// real work).
        /// </summary>
        private static readonly Color32 Layer2Color = new Color32(150, 190, 255, 255);

        // ── Label creation (this is the only place allowed to make a UILabel) ────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height, Color32 color)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Earthquake" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = color;
            label.autoSize = false;
            // Stop explanatory text that does not fit on one line from being cut off.
            label.wordWrap = height > RowHeight;
            return label;
        }

        /// <summary>
        /// The panel's title. The only row whose position and width the caller decides,
        /// and it exists so the width can be narrowed enough not to overlap the close
        /// button (every other row lines up with <see cref="RowLeft"/> /
        /// <see cref="RowWidth"/>). The content is put in by <see cref="SetPlain"/>.
        /// </summary>
        internal static UILabel AddTitleRow(UIPanel p, string suffix, float x, float y,
                                            float width, float height)
        {
            return AddLabel(p, suffix, x, y, width, height, Layer1Color);
        }

        internal static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y, string text)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer1Color);
            SetPlain(label, "-- " + text + " --");
            y += 26f;
            return label;
        }

        /// <summary>A row with no source prefix (notes under a heading, explanations of state).</summary>
        internal static UILabel AddPlainRow(UIPanel p, string suffix, ref float y,
                                            string text, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer1Color);
            SetPlain(label, text);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **A layer-1 row.** Use it only for quantities derived purely from vanilla's own
        /// formulae and constants. Its contents can only be written by
        /// <see cref="SetLayer1"/>.
        /// </summary>
        internal static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer1Color);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// A wrapping layer-1 row. **Not a new label-creation path** (it shares
        /// <see cref="AddLabel"/>). It exists for rows whose content does not fit on one
        /// line, and its contents can likewise only be written by <see cref="SetLayer1"/>.
        /// </summary>
        internal static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer1Color);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **A layer-2 row.** Use it only for physics this mod invented.
        /// Its first caller is Task 9 (<see cref="EarthquakeLayer2Rows"/>).
        /// </summary>
        internal static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer2Color);
            y += RowStep;
            return label;
        }

        /// <summary>A wrapping layer-2 row. It exists for the same reason as <see cref="AddLayer1Row(UIPanel,string,ref float,float)"/>.</summary>
        internal static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer2Color);
            y += height + 4f;
            return label;
        }

        // ── Setting text (the only assignment to UILabel.text in the mod) ───────

        internal static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>Writes into a layer-1 row. The caller never gets to choose the prefix.</summary>
        internal static void SetLayer1(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }

        /// <summary>Writes into a layer-2 row. The caller never gets to choose the prefix.</summary>
        internal static void SetLayer2(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceModel + " " + body);
        }
    }
}
