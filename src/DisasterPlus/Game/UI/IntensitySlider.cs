using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **Reuse vanilla's intensity slider from the ④ and ⑤ tiles too.** Main thread only.
    ///
    /// ── What vanilla does (measured from the IL) ──────────────────────
    ///
    /// <c>DisastersPanel.OnButtonClicked</c> only acts when the pressed tile's
    /// <c>objectUserData</c> is a <c>DisasterInfo</c>; it does
    /// <c>SetTool&lt;DisasterTool&gt;()</c> → assign <c>m_prefab</c> → and, if
    /// <c>SupportIntensity()</c>, call <c>ShowDisastersOptionPanel()</c>.
    /// The body of that <c>ShowDisastersOptionPanel</c> is nothing but
    /// <c>GetOptionPanel("DisastersOptionPanel").ShowPanel()</c>.
    ///
    /// <c>DisastersOptionPanel</c> (derives from <c>OptionPanelBase</c>, public)
    ///   - grabs <c>Find&lt;UISlider&gt;("Slider")</c> and
    ///     <c>Find&lt;UILabel&gt;("LabelIntensity")</c> in <c>Awake</c>, and
    ///   - in <c>OnSliderValueChanged</c> writes <c>value / 10</c> to the label as
    ///     <c>"F1"</c> and sets <c>DisasterTool.m_intensity = (int)value</c>.
    ///
    /// So <b>the slider's raw value is the byte intensity as-is, and only the display is
    /// divided by 10</b> (the same fact behind <see cref="IntensityUnlock"/> opening the
    /// ceiling up to 255).
    ///
    /// ── Why ④ and ⑤ can borrow this outright ──────────────────────
    ///
    /// <c>ShowPanel()</c> / <c>HidePanel()</c> are **public** methods on
    /// <c>OptionPanelBase</c>, so no reflection is needed (measured from the IL).
    /// The slider itself is reachable through <c>UICustomControl.Find&lt;T&gt;(name)</c>
    /// (<see cref="IntensityUnlock"/> already rewrites the ceiling through the same path).
    ///
    /// ★ This type **does not make <c>DisasterTool</c> the current tool**. ④ and ⑤ use their
    ///   own placement tools, so the <c>DisasterTool.m_intensity</c> the slider writes moves
    ///   nothing there and then — **we only read the value**. The slider value carrying over
    ///   the next time a vanilla disaster is selected is the same behaviour as switching
    ///   between two vanilla tiles.
    ///
    /// ── The "meaning" differs per tile, so seed it every time the tool is armed ──────
    ///
    /// There is only one slider, yet for ④ it means **the typhoon's intensity** and for ⑤
    /// **a multiplier on the configured size** (<c>VolcanoSizeScale</c>). Seeding that
    /// feature's default via <see cref="Seed"/> the moment the tool is armed keeps
    /// **the number on screen being the number for the feature currently armed**.
    /// Re-arming resets it to the default, but that beats leaving a number behind that means
    /// something else.
    ///
    /// ── Holds no state ────────────────────────────────
    ///
    /// **Never hold a Unity object in a static.** This is only called when the tool is armed
    /// and when the map is clicked, so looking it up each time is fine. Since nothing is
    /// held, there is no path by which a destroyed reference survives across cities.
    /// </summary>
    public static class IntensitySlider
    {
        /// <summary>Lower bound of the slider's raw value.</summary>
        public const int MinRaw = 0;

        /// <summary>Upper bound of the raw value (<c>DisasterData.m_intensity</c> is a byte).</summary>
        public const int MaxRaw = 255;

        /// <summary>Whether the slider is reachable in this environment (does not change the value).</summary>
        public static bool Available { get { return FindSlider() != null; } }

        /// <summary>
        /// Show the intensity slider. The same thing appears in the same place as when a
        /// vanilla tile is pressed. In an environment where it cannot be shown (no
        /// <c>DisastersOptionPanel</c>, and so on) it quietly does nothing —
        /// **④ and ⑤ work off their default values without the slider.**
        /// </summary>
        public static void Show()
        {
            var panel = FindPanel();
            if (panel == null) { Log.Diag("intensitySlider", "no DisastersOptionPanel to show"); return; }
            try { panel.ShowPanel(); }
            catch (System.Exception e) { Log.Warn("intensity slider show failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// Fold the intensity slider away. **Only call this when leaving the ④/⑤ placement
        /// tool** — calling it while a vanilla disaster is armed yanks their slider away
        /// from under them.
        /// </summary>
        public static void Hide()
        {
            var panel = FindPanel();
            if (panel == null) { Log.Diag("intensitySlider", "no DisastersOptionPanel to hide"); return; }
            try { panel.HidePanel(); }
            catch (System.Exception e) { Log.Warn("intensity slider hide failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// Seed the slider with a starting value (see "the meaning differs per tile" in the
        /// class doc). Other mods may move the ceiling, so leave the clamping to
        /// <c>UISlider</c> itself.
        /// </summary>
        public static void Seed(int raw)
        {
            var slider = FindSlider();
            if (slider == null)
            {
                // ★ Do not drop this silently. In the first playtest the only available
                //   inference was "no warning line came out, so the slider must be readable".
                //   **Leave a line when it could not be read, too.**
                Log.Diag("intensitySlider", "no slider to seed; the tile will fall back to the options value");
                return;
            }
            try { slider.value = Clamp(raw); }
            catch (System.Exception e) { Log.Warn("intensity slider seed failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// The current raw value. Returns <paramref name="fallback"/> unchanged if it cannot
        /// be read.
        ///
        /// ★ **Do not express "could not read" as 0.** 0 is a valid value meaning
        ///   "the weakest setting", which is not the same thing as "could not read".
        /// </summary>
        public static int ReadOr(int fallback)
        {
            var slider = FindSlider();
            if (slider == null)
            {
                Log.Diag("intensitySlider", "slider unreadable; using the options value " + fallback);
                return fallback;
            }

            try { return Clamp(Mathf.RoundToInt(slider.value)); }
            catch (System.Exception e)
            {
                Log.Warn("intensity slider read failed: " + e.GetType().Name);
                return fallback;
            }
        }

        private static int Clamp(int raw)
        {
            if (raw < MinRaw) return MinRaw;
            if (raw > MaxRaw) return MaxRaw;
            return raw;
        }

        /// <summary>
        /// <c>Object.FindObjectOfType</c> is not used, because on Unity 5.6 it does not
        /// return inactive GameObjects (see the <see cref="SceneObjects"/> class doc).
        /// </summary>
        private static DisastersOptionPanel FindPanel()
        {
            try { return SceneObjects.FindInScene<DisastersOptionPanel>(); }
            catch (System.Exception e)
            {
                Log.Warn("disasters option panel lookup failed: " + e.GetType().Name);
                return null;
            }
        }

        private static UISlider FindSlider()
        {
            var panel = FindPanel();
            if (panel == null) return null;
            try { return panel.Find<UISlider>("Slider"); }
            catch (System.Exception e)
            {
                Log.Warn("intensity slider lookup failed: " + e.GetType().Name);
                return null;
            }
        }
    }
}
