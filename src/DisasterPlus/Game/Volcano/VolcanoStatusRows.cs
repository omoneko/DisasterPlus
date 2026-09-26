using ColossalFramework.UI;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The rows showing the volcano's own state (status, form, radius, final height, and
    /// **the permanent irreversibility warning**).
    /// **Main thread only.**
    ///
    /// Both creating a row and putting text into it go through <see cref="VolcanoRows"/>.
    /// **There is not one <c>UILabel</c> construction or <c>.text</c> assignment in this file.**
    ///
    /// ── the four promises this file keeps (design doc §7) ──────────────────────────────────
    ///
    /// 1. **No per-row provenance markers.** ⑤'s numbers are in principle all this mod's own
    ///    (the two heading rows were removed on 2026-08-22. See the doc of
    ///    <see cref="VolcanoPanel"/>).
    ///    <b>There is not one call to <c>SetMeasured</c> in this file</b> —
    ///    the form, the radius and the final height are all numbers ⑤ derived from the settings.
    /// 2. **The irreversibility warning was taken off this tab** (2026-08-22, the owner's request
    ///    "the detailed explanations and debug info do not need showing in-game").
    ///    The same sentence is kept in **the options screen's heading**
    ///    (<c>Strings.VolcanoIrreversibleWarning</c> in <c>Mod.cs</c>) and is in the diagnostic
    ///    dump too.
    ///    **We are not silent about it** — only the place to read it has moved off the screen you
    ///    play on.
    /// 3. **Do not turn an unreadable value into a number.** When there is no snapshot yet,
    ///    <c>VolcanoWaiting</c>; when it could not be read, <c>VolcanoUnavailable</c>.
    ///    Do not give "not read yet" and "cannot be read" the same wording (the discipline ① and ②
    ///    established).
    /// 4. **Do not quote real physical units.** All ⑤ reports is distance (m), height (m),
    ///    in-game time and a stage from 0 to 10 (design doc §7.5).
    ///
    /// ── do not put the form labels in an array ─────────────────────────────────────────────
    ///
    /// <see cref="FormLabel"/> re-reads <c>Strings</c> through a <c>switch</c> every time.
    /// Put them in a <c>static readonly string[]</c> and they **freeze in the language at type
    /// initialisation**, staying English even if the language is switched mid-game
    /// (the class doc of <c>Strings</c>).
    /// </summary>
    internal static class VolcanoStatusRows
    {
        private static UILabel _stateLabel;
        private static UILabel _shapeLabel;

        /// <summary>Once, when the panel is built. The rows are always created and shown or hidden by their content.</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // "There is no volcano", "not read yet", "cannot be read", "surveying" and
            // "the reason it was refused" all come out here. **Give it a wrapping height** —
            // refusal is an English sentence, so putting it in a non-wrapping row cuts the reason
            // off partway.
            _stateLabel = VolcanoRows.AddRow(p, "State", ref y, 40f);

            _shapeLabel = VolcanoRows.AddRow(p, "Shape", ref y);

            // ★★ **The permanent irreversibility warning was removed** (2026-08-22, the owner's
            //    request "the detailed explanations and debug info do not need showing in-game").
            //    The content remains in the diagnostic dump (note: unfinished volcano, among
            //    others) and on the options screen.
        }

        /// <summary>Every frame while the panel is shown. <paramref name="s"/> may be null.</summary>
        internal static void Refresh(VolcanoSnapshot s)
        {
            // The form, the radius and the final height do not depend on the snapshot (they are
            // decided by the settings and Core's clamping alone), so they can be shown even when
            // nothing could be read.
            RefreshShapeRow();

            if (s == null)
            {
                // Do not give "never read once" and "read but unreadable" the same wording
                // (the discipline ①, ② and ④ established). Staying paused right after a load
                // makes the former happen routinely.
                VolcanoRows.SetPlain(_stateLabel, Strings.VolcanoWaiting);
                return;
            }

            if (!s.Valid || !s.Terrain.Usable)
            {
                VolcanoRows.SetPlain(_stateLabel, Strings.VolcanoUnavailable);
                return;
            }

            VolcanoRows.SetPlain(_stateLabel, StateText(s));
        }

        /// <summary>
        /// The single status row. **It must let you tell "nothing is happening" from "it could not
        /// be triggered".**
        ///
        /// The priority order is "the request was just issued (waiting for the next sim tick)" >
        /// "in progress" > "refused, with a reason" > "simply not happening".
        /// <see cref="VolcanoHub.PendingRequest"/> is checked **to stop the player pressing twice
        /// because nothing appears to change when they press**, since there is a designed one-tick
        /// lag between the request and it being reflected (the doc of <see cref="VolcanoHub"/>).
        /// </summary>
        private static string StateText(VolcanoSnapshot s)
        {
            // While the placement tool is out, say what to do. **If nothing changes on screen
            // right after they press, the player reads the button as broken.**
            if (VolcanoPlacementTool.IsActive) return Strings.VolcanoPlaceHint;

            VolcanoRequest pending = VolcanoHub.PendingRequest.Kind;
            // ★ The one tick between queuing the request and sim picking it up. ⑤ counts the
            //   affected range within that tick before it starts destroying, so what comes out
            //   here is "surveying".
            if (pending == VolcanoRequest.Place) return Strings.VolcanoSurveying;
            if (pending != VolcanoRequest.None) return Strings.VolcanoWaiting;

            switch (s.Phase)
            {
                case VolcanoPhase.Idle:
                case VolcanoPhase.Done:
                case VolcanoPhase.Refused:
                    return string.IsNullOrEmpty(s.Refusal)
                        ? Strings.VolcanoInactive
                        : Strings.VolcanoInactive + "  (" + s.Refusal + ")";

                default:
                    // ★ An in-progress phase. **There are no translation keys until T5–T8
                    //   implement each stage**, so the enum's name (English) is shown as is.
                    //   Knowing where it is stuck is better than showing nothing
                    //   (the same call as showing ④'s refusal in English).
                    return Strings.VolcanoPhaseRow + ": " + s.Phase;
            }
        }

        /// <summary>
        /// The single row for the form, the radius and the final height. **Show the clamped
        /// values** (the <c>.cgs</c> is a public contract and may have been edited by hand, so do
        /// not put raw setting values on screen).
        ///
        /// **The cut-down by the ceiling (§C-10) is not applied here** — that is a quantity only
        /// known once the placement point's terrain height is settled, and after placement the
        /// survey row in <see cref="VolcanoEffectRows"/> states it.
        ///
        /// ★ **The slider's scale is not applied here either.** The scale is decided at the moment
        ///   the map is clicked (<c>Core.Volcano.VolcanoSizeScale</c>), and this row is "the
        ///   baseline size currently selected in the settings". The real size with the scale
        ///   applied is shown by <see cref="VolcanoEffectRows"/> after placement.
        /// </summary>
        private static void RefreshShapeRow()
        {
            VolcanoForm form = CurrentForm();
            // ★★ The baseline is **the recommended value for each form** (2026-08-22).
            //    The radius and final-height sliders on the settings screen were removed
            //    (the history is in the class doc of <c>Core.Volcano.VolcanoSizeScale</c>).
            float radius = VolcanoShape.RadiusFor(form, VolcanoShape.DefaultRadiusOf(form));
            // The height is clamped by the form's band alone (the ceiling comes once the location
            // is settled).
            float height = ClampHeightToForm(form, VolcanoShape.DefaultHeightOf(form));

            VolcanoRows.SetPlain(_shapeLabel,
                Strings.VolcanoFormRow + ": " + FormLabel(form)
                + "    " + Strings.VolcanoRadiusRow + ": " + radius.ToString("F0")
                + " " + Strings.VolcanoMetres
                + "    " + Strings.VolcanoHeightRow + ": " + height.ToString("F0")
                + " " + Strings.VolcanoMetres);
        }

        /// <summary>
        /// The form currently selected. Out-of-range values are dropped to the default by
        /// <c>VolcanoShape.FormOf</c> (the <c>.cgs</c> is a public contract and may have been
        /// edited by hand).
        /// </summary>
        private static VolcanoForm CurrentForm()
        {
            return VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);
        }

        /// <summary>
        /// The height clamped by the form's band alone. **Do not use
        /// <c>VolcanoShape.HeightFor</c>** — that one also looks at the ceiling (§C-10), so
        /// running it here, before the placement point is settled, gives a value that assumes
        /// "a base of 0 m".
        /// </summary>
        private static float ClampHeightToForm(VolcanoForm form, float requested)
        {
            float min = VolcanoShape.MinHeightOf(form);
            float max = VolcanoShape.MaxHeightOf(form);
            if (float.IsNaN(requested)) return VolcanoShape.DefaultHeightOf(form);
            if (requested < min) return min;
            if (requested > max) return max;
            return requested;
        }

        /// <summary>
        /// The display name of the form. **It matters that this is a method** (class doc).
        /// Make it a <c>static readonly string[]</c> and it freezes in the language the game
        /// started in.
        /// </summary>
        private static string FormLabel(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return Strings.VolcanoFormShield;
                case VolcanoForm.Dome: return Strings.VolcanoFormDome;
                default: return Strings.VolcanoFormStrato;
            }
        }

        /// <summary>
        /// On level unload. **Just drop the references** (the objects themselves go with the
        /// panel's GameObject). Carry them over and you write into destroyed labels in the next
        /// city.
        /// </summary>
        internal static void Destroy()
        {
            _stateLabel = null;
            _shapeLabel = null;
        }
    }
}
