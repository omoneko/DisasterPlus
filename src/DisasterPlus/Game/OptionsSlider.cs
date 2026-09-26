using System;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Places one slider on the settings screen.</b> **Main thread (OnSettingsUI) only.**
    ///
    /// ── Report from the game (2026-09-02, with a screenshot) ──────────────────
    ///
    /// &gt; Parts of the Options panel UI overlap, which makes them awkward to use
    /// &gt; (… after the fix) it wasn't fixed.
    ///
    /// In the image that came back, the slider's label was still wrapped over three lines and
    /// <b>overlapping the next checkbox</b>. The third line of "Wind damage…" and
    /// "Southern hemisphere" were being drawn in the same place — that is the breakage.
    ///
    /// ── ★★ Why they overlap (settled in the IL) ─────────────────────────────
    ///
    /// The body of <c>UIHelper.AddSlider</c> is
    ///
    /// <code>
    /// UIPanel row = m_Root.AttachUIComponent(GetAsGameObject(kSliderTemplate));
    /// row.Find("Label").text = text;
    /// UISlider slider = row.Find("Slider");
    /// ...
    /// return slider;                      // ★ what comes back is the slider, not the row
    /// </code>
    ///
    /// ★★ <b>The row's height stays at the template's fixed value.</b> Only the label wraps
    ///   and grows, so the second line onwards spills over and <b>eats into the area of the
    ///   next row</b>. The parent's auto-layout only looks at the row height, so nobody
    ///   fixes the overlap.
    ///
    /// ── So do it in two stages ────────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>Shorten the label.</b> If it does not wrap, the problem does not arise.
    ///   Move the long explanation to <see cref="UIComponent.tooltip"/> — this is not
    ///   throwing the explanation away, it is putting it <b>where only those who want it
    ///   read it</b>.</item>
    /// <item><b>If it wraps anyway, grow the row.</b> A translation can run longer than the
    ///   English, so even a short label can take two lines in another language. This is the
    ///   last line of defence.</item>
    /// </list>
    ///
    /// ★ Give up silently on failure. **A little ugliness beats a settings screen that will
    ///   not open** — even if the template's structure changes in future, the mod's settings
    ///   screen goes on opening.
    /// </summary>
    public static class OptionsSlider
    {
        /// <summary>Padding added below the row (px).</summary>
        private const float BottomPadding = 8f;

        /// <summary>The gap between the label and the slider (px).</summary>
        private const float LabelGap = 4f;

        private static bool _warned;

        /// <summary>
        /// Adds a slider with a label and a tooltip.
        /// </summary>
        /// <param name="tooltip">
        /// The long explanation. <c>null</c> means none is attached. **Anything that will not
        /// fit in the label goes here.**
        /// </param>
        public static void Add(UIHelperBase group, string label, string tooltip,
                               float min, float max, float step, float value,
                               OnValueChanged onChanged)
        {
            object added = group.AddSlider(label, min, max, step, value, onChanged);

            var slider = added as UISlider;
            if (slider == null) return;

            Fit(slider, tooltip);
        }

        /// <summary>
        /// Attaches the tooltip and grows the row if the label has wrapped.
        /// </summary>
        private static void Fit(UISlider slider, string tooltip)
        {
            try
            {
                var row = slider.parent as UIPanel;
                if (row == null) return;

                // ★ Attach the tooltip to the whole row. Attach it to the label alone and it
                //   does not appear when you run over the slider.
                if (!string.IsNullOrEmpty(tooltip)) row.tooltip = tooltip;

                // The names "Label" / "Slider" are the ones AddSlider itself uses, so if they
                // are not found here then AddSlider did not work either (see the IL in the
                // class doc).
                var label = row.Find<UILabel>("Label");
                if (label == null) return;

                float labelBottom = label.relativePosition.y + label.height;
                float needed = labelBottom + LabelGap + slider.height + BottomPadding;

                // If it fits on one line, the template's height is enough. Leave it alone.
                if (row.height >= needed) return;

                slider.relativePosition = new Vector3(slider.relativePosition.x,
                                                      labelBottom + LabelGap);
                row.height = needed;
            }
            catch (Exception e)
            {
                // Say it once. A log line per slider would be unreadable.
                if (_warned) return;
                _warned = true;
                Log.Warn("could not fit an options slider row; a long label may overlap "
                         + "the control under it: " + e.GetType().Name);
            }
        }
    }
}
