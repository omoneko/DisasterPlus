using ColossalFramework.UI;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// While ⑤ is armed, and only then, **re-read the number on vanilla's intensity slider
    /// as "the size of the mountain" and show that instead.** Main thread only, every frame.
    ///
    /// ── Why it is needed (2026-08-22, the owner's remark) ─────────────────────
    ///
    /// > I'd like to be able to adjust the size of the volcano with a scale.
    ///
    /// **The slider is already the size knob, and it already works.** The log from the game
    /// shows it:
    ///
    /// <code>
    /// volcano placed at (88,-398): Strato r=829 m h=415 m
    /// </code>
    ///
    /// The defaults are r = 1200 / h = 600, so that is 0.69× — the slider was doing exactly
    /// its job (<see cref="VolcanoSizeScale"/> maps raw 55 to 1.0×).
    /// **What was missing was not a second knob but the screen:**
    ///
    ///   - the label only showed <c>5.5</c>. That is <b>vanilla's display rule for intensity</b>
    ///     (raw ÷ 10), a number that means nothing to ⑤
    ///   - **nowhere did it say how many metres of mountain you would get before pressing**
    ///     (the confirmation window was removed on 2026-08-21. It is not coming back)
    ///
    /// So while the tool is armed this type swaps the label for
    ///
    /// <code>
    /// 1.40x  r1680  h840 m
    /// </code>
    ///
    /// — **the multiplier, and the real dimensions you will actually get (already clamped to
    /// the band for the form)**. The only words go in a single line of slider tooltip (the
    /// owner keeps asking for less prose).
    ///
    /// ── The height is the value before the terrain ceiling is subtracted ────────────────
    ///
    /// <c>VolcanoShape.HeightFor</c> subtracts the ceiling (§C-10) from the terrain height at
    /// the origin, but **before you press there is no chosen spot**, so there is nothing to
    /// subtract. What appears here is only clamped to the band, and somewhere high up it may
    /// come out lower than this. **The volcano tab and the diagnostic dump report that
    /// difference from measurement** (those come after the press). Far better than showing
    /// nothing — showing nothing was the very thing that was raised here.
    ///
    /// ── Putting it back (★ skip this and you break the display for vanilla's disasters) ──
    ///
    /// <c>DisastersOptionPanel</c> shares a single slider across every disaster, and the
    /// label is rewritten **only** by <c>OnSliderValueChanged</c> (measured from the IL).
    /// So even after leaving ⑤ and selecting a vanilla disaster, **our text stays there until
    /// the player moves the slider.** Write it back to vanilla's display rule
    /// (raw ÷ 10 as <c>"F1"</c>) the moment the tool is dropped.
    ///
    /// ── The per-frame cost ─────────────────────────────────
    ///
    /// One <c>ToolsModifierControl.toolController</c> and one <c>UISlider.value</c>.
    /// **The string is only built on frames where the raw value changed**
    /// (<see cref="_lastRaw"/>), so while it is not being moved the heap allocation is 0 bytes.
    /// Looking the panel up (<c>Resources.FindObjectsOfTypeAll</c>) does not happen every
    /// frame — hold individual references and **never an array** (the fake-null trap that ③
    /// shipped).
    /// </summary>
    public static class VolcanoSizeReadout
    {
        /// <summary>The raw value meaning "not read even once yet". Pick one outside 0-255.</summary>
        private const int NoRaw = -1;

        // ★ Not an array. Hold individual references and test each one with Unity's == null
        //   every time (a destroyed object comes back fake-null, so we look it up again on
        //   that frame).
        private static UISlider _slider;
        private static UILabel _label;

        private static bool _applied;
        private static int _lastRaw = NoRaw;
        private static bool _lookupFailedLogged;

        /// <summary>The text last shown (for diagnostics. **Numbers only, not English**).</summary>
        public static string LastText { get; private set; }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update()
        {
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                // A per-frame path. Sound neither Warn nor Error (one line per key).
                Log.Diag("volcanoSize", "size readout failed: " + e.GetType().Name);
                _applied = false;
                _lastRaw = NoRaw;
            }
        }

        private static void Step()
        {
            bool armed = ModSettings.VolcanoEnabled.value && VolcanoPlacementTool.IsActive;
            if (!armed)
            {
                if (_applied) Restore();
                return;
            }

            if (!Resolve()) return;

            int raw = Mathf.RoundToInt(_slider.value);
            if (raw < IntensitySlider.MinRaw) raw = IntensitySlider.MinRaw;
            if (raw > IntensitySlider.MaxRaw) raw = IntensitySlider.MaxRaw;

            if (_applied && raw == _lastRaw) return;

            _lastRaw = raw;
            _applied = true;
            LastText = Compose(raw);
            _label.text = LastText;

            // ★ The only words are this one line. The label stays numbers only.
            _slider.tooltip = Strings.VolcanoSizeSliderTooltip;
            _label.tooltip = Strings.VolcanoSizeSliderTooltip;
        }

        /// <summary>
        /// The one line of "multiplier and real dimensions". Show **the value after clamping
        /// to the band for the form** — show it before the clamp and, once it has pinned to
        /// the ceiling, moving the slider would grow the display alone (i.e. a readout
        /// that lies).
        /// </summary>
        private static string Compose(int raw)
        {
            float scale = VolcanoSizeScale.ScaleFor(raw);
            VolcanoForm form = VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);

            float r = VolcanoShape.RadiusFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form), scale));

            // ★ The terrain ceiling cannot be subtracted (no spot has been chosen). As the
            //   class doc notes, this is from a 0 m origin, i.e. only the band clamp applied.
            float h = VolcanoShape.HeightFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form), scale), 0f);

            // ★★ **Lead with the same number vanilla shows** (raw ÷ 10 as "F1").
            //    It used to show "1.00x", **a unit unique to this mod**, which could not be
            //    compared against the sliders of the other disasters
            //    (the owner's remark: "make it 1.0-10.0 (25.5) like vanilla").
            //    The real dimensions follow — the values after clamping to the band for
            //    the form.
            return (raw / 10f).ToString("F1") + "  r" + r.ToString("F0")
                   + "  h" + h.ToString("F0") + " m";
        }

        /// <summary>
        /// Write back vanilla's display rule (raw ÷ 10 as <c>"F1"</c>). Idempotent.
        /// **Must always run once ⑤ is dropped** (see the class doc).
        /// </summary>
        private static void Restore()
        {
            _applied = false;
            LastText = null;

            int raw = _lastRaw;
            _lastRaw = NoRaw;

            if (_label == null || _slider == null) return;

            try
            {
                if (raw != NoRaw) _label.text = (raw / 10f).ToString("F1");
                _slider.tooltip = null;
                _label.tooltip = null;
            }
            catch (System.Exception e)
            {
                Log.Diag("volcanoSize", "size readout restore failed: " + e.GetType().Name);
            }
        }

        /// <summary>
        /// On level unload, and when the feature is switched off. **Do not touch Unity
        /// objects** (they may already be destroyed). Just let go of the references.
        /// </summary>
        public static void Reset()
        {
            _slider = null;
            _label = null;
            _applied = false;
            _lastRaw = NoRaw;
            LastText = null;
            // _lookupFailedLogged is not reset (it is a fact about the game build).
        }

        /// <summary>
        /// Resolve the slider and the label. **Do not look them up again while they resolve.**
        /// If either has been destroyed (fake-null), look it up again there and then.
        /// </summary>
        private static bool Resolve()
        {
            if (_slider != null && _label != null) return true;

            var panel = SceneObjects.FindInScene<DisastersOptionPanel>();
            if (panel == null)
            {
                if (!_lookupFailedLogged)
                {
                    _lookupFailedLogged = true;
                    Log.Info("volcano size readout: no DisastersOptionPanel in this build, "
                             + "so the slider keeps the vanilla intensity label; "
                             + "the size itself is unaffected");
                }
                return false;
            }

            // ★ The names are exactly the ones <c>DisastersOptionPanel.Awake</c> uses
            //   (measured from the IL: Find<UISlider>("Slider") /
            //   Find<UILabel>("LabelIntensity")).
            //   The m_slider / m_label fields themselves are private, so they are off limits.
            _slider = panel.Find<UISlider>("Slider");
            _label = panel.Find<UILabel>("LabelIntensity");

            return _slider != null && _label != null;
        }
    }
}
