using ColossalFramework.UI;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// What state the waveform plot is currently in. **Do not collapse these four into
    /// one bool.**
    ///
    /// "not built yet", "could not be built" and "stopped being drawable part way
    /// through" have different causes and different remedies, yet on screen all three
    /// look identical: there is no picture. The only clue for telling them apart is this
    /// one line in the diagnostic dump.
    /// </summary>
    public enum WaveformViewState
    {
        /// <summary>The panel has never been opened (i.e. nothing has been attempted).</summary>
        NotBuilt,

        /// <summary>Usable.</summary>
        Ready,

        /// <summary>Building it failed (the <c>Texture2D</c> / <c>UITextureSprite</c> could not be made).</summary>
        BuildFailed,

        /// <summary>It was built once, but an exception during drawing means we draw no more.</summary>
        RenderFailed,
    }

    /// <summary>
    /// Draws the waveforms onto a single texture. **Main thread only.**
    ///
    /// ── Why a texture rather than characters ────────────────────────
    ///
    /// ① pinned the hazard bar to ASCII (<c>HazardLevel.FilledChar</c>), on the grounds
    /// that there is no guarantee CS's UI font has box-drawing glyphs. Carry that
    /// discipline through to here and the conclusion is that **graphs must not be built
    /// out of characters** — there is no guarantee UILabel's font is monospaced either,
    /// so the columns would not line up. For a single bar it is only a question of
    /// character count, but a grid of 80 rows by 320 columns simply does not work.
    ///
    /// ── The APIs used were confirmed by disassembly (nothing is guessed) ──────
    ///
    /// <c>ColossalFramework.UI.UITextureSprite</c> (in ColossalManaged.dll, not
    /// Assembly-CSharp):
    ///   - <c>public Texture texture { get; set; }</c> — exists, and is writable
    ///   - <c>OnRebuildRenderData</c> returns doing nothing when <c>m_Texture == null</c>,
    ///     and otherwise does <c>EnsureMaterial()</c> →
    ///     <c>renderMaterial.mainTexture = m_Texture</c>
    ///   - <c>EnsureMaterial</c>, when <c>m_Material == null</c>, makes a copy from
    ///     <c>GetUIView().defaultAtlas.material</c>
    ///     → **so we need not provide a material ourselves; setting <c>texture</c> is enough.**
    ///
    /// <c>UnityEngine.Texture2D</c> (Unity 5.6):
    ///   - <c>.ctor(int, int, TextureFormat, bool)</c> — exists
    ///   - <c>SetPixels32(Color32[])</c> / <c>Apply()</c> — exist
    ///   - <c>TextureFormat.RGBA32</c> = 4 — exists
    ///
    /// Even so, <see cref="Build"/> is wrapped in try/catch and sets
    /// <see cref="Available"/> to false on failure. **When that happens the panel does not
    /// silently go blank: it shows the peak amplitude as a number and a bar, and states
    /// that this build cannot draw the plot** (<c>Strings.EarthquakeWaveformUnavailable</c>).
    /// That is degradation, not a lie.
    ///
    /// ── Redraw only when the data changes ───────────────────────────
    ///
    /// Tasks 6 and 7 cut the cursor ray down to once every 4 frames (from ≤521 to ≤131
    /// samples per frame). Repainting 25,600 pixels every frame here would hand all that
    /// effort straight back. <see cref="Render"/> **returns immediately** when the
    /// station, the sample count and the newest frame are the same as last time. The data
    /// itself is only rebuilt at most every 8 frames on the
    /// <c>SeismographRecorder</c> side, so the real repaint rate tops out at a few per
    /// second.
    ///
    /// ── The <c>Texture2D</c> must be thrown away by hand ────────────────
    ///
    /// A <c>Texture2D</c> is not a <c>Component</c>, so <c>Object.Destroy</c> on the
    /// parent GameObject does not take it with it. Without explicitly destroying it in
    /// <see cref="Destroy"/>, one is left behind every time a city is reloaded. Setting
    /// the fields back to null also avoids the shape that actually bit us in ③: holding
    /// a destroyed reference and silently going invisible in the second city.
    /// </summary>
    public static class WaveformView
    {
        /// <summary>The plot's size in pixels. The UI size matches it so it displays 1:1.</summary>
        public const int PlotWidth = 320;
        public const int PlotHeight = 80;

        private static readonly Color32 Background = new Color32(16, 18, 24, 255);
        private static readonly Color32 AxisColor = new Color32(70, 76, 90, 255);

        /// <summary>
        /// **The line from vanilla's formula** (layer 1, <c>[measured]</c>). White.
        /// This colour only means anything paired with the row's prefix, so do not mix it
        /// up with <see cref="ModelColor"/> — mix them up and a line this mod invented is
        /// drawn wearing the face of a value the game computes.
        /// </summary>
        private static readonly Color32 TraceColor = new Color32(255, 255, 255, 255);

        /// <summary>
        /// **The synthetic seismogram line** (layer 2, <c>[Disaster + model]</c>). Orange.
        /// The panel's legend (<c>Strings.EarthquakeWaveformLegend</c>) states which
        /// colour is which layer, directly under the graph, every time.
        /// </summary>
        private static readonly Color32 ModelColor = new Color32(255, 158, 66, 255);

        /// <summary>
        /// **The volcanic tremor line** (layer 3, <c>[Disaster + volcanic tremor]</c>).
        /// Teal. Its hue is kept well away from the orange (<see cref="ModelColor"/>) so
        /// the two cannot be confused — all three only appear together when an earthquake
        /// happens during an eruption with the synthetic seismogram also on, but if they
        /// could not be told apart then, there would be no point adding the third line.
        ///
        /// ★ This too is on <b>this mod's model</b> side (it is not vanilla's formula).
        ///   See <c>Game/Volcano/VolcanoTremorTrace</c>'s class doc.
        /// </summary>
        private static readonly Color32 TremorColor = new Color32(96, 220, 200, 255);

        private static UITextureSprite _sprite;
        private static Texture2D _texture;
        private static Color32[] _pixels;
        private static bool _available;

        /// <summary>
        /// Whether <see cref="Build"/> has ever run. It exists solely to **separate "not
        /// built yet" from "we tried to build it and failed"**.
        ///
        /// The diagnostic dump can be produced without the panel ever having been opened,
        /// so without this a dump taken right after startup would say "cannot draw
        /// (falling back to the peak amplitude row)" — when in fact nothing has been
        /// attempted, sending the whole diagnosis off in the wrong direction.
        /// </summary>
        private static bool _built;

        /// <summary>Whether drawing failed after it had worked once (distinct from a build failure).</summary>
        private static bool _renderFailed;

        /// <summary>A fingerprint of what was last drawn. If it matches, we do not repaint.</summary>
        private static ushort _drawnBuildingId;
        private static int _drawnCount;
        private static uint _drawnNewestFrame;
        private static bool _drawnAnything;

        /// <summary>
        /// Whether the last picture drawn had a model line on it. **It is part of the
        /// fingerprint.** Leave it out and, after the setting is toggled, nothing is
        /// repainted while the sample count and newest frame stay the same — so the line
        /// you thought you had turned off stays on screen.
        /// </summary>
        private static bool _drawnModel;
        private static bool _drawnTremor;
        private static bool _drawnQuake;

        /// <summary>
        /// Whether texture-based drawing is usable. False until <see cref="Build"/> has
        /// been called at least once. When false, the panel shows a peak-amplitude row
        /// and a one-line reason in place of the waveform.
        /// </summary>
        public static bool Available { get { return _available; } }

        /// <summary>
        /// The current state. Both the panel's "why it cannot be drawn" row and the
        /// diagnostic dump read this.
        /// </summary>
        public static WaveformViewState State
        {
            get
            {
                if (_available) return WaveformViewState.Ready;
                if (_renderFailed) return WaveformViewState.RenderFailed;
                return _built ? WaveformViewState.BuildFailed : WaveformViewState.NotBuilt;
            }
        }

        /// <summary>
        /// Creates the plot's sprite on the parent panel. **Main thread; once, when the
        /// panel is built.** On failure it throws nothing and leaves
        /// <see cref="Available"/> false.
        ///
        /// The plan's list of types had width and height as parameters too, but if the
        /// texture's pixel count and the UI size disagree the waveform gets scaled and
        /// the columns collapse. To make sure the two always match, the size is fixed at
        /// <see cref="PlotWidth"/> / <see cref="PlotHeight"/> and taken out of the
        /// parameters.
        /// </summary>
        public static void Build(UIPanel parent, string suffix, float x, float y)
        {
            Destroy();
            if (parent == null) return;

            try
            {
                _texture = new Texture2D(PlotWidth, PlotHeight, TextureFormat.RGBA32, false);
                _texture.filterMode = FilterMode.Point;
                _texture.wrapMode = TextureWrapMode.Clamp;
                _pixels = new Color32[PlotWidth * PlotHeight];

                _sprite = (UITextureSprite)parent.AddUIComponent(typeof(UITextureSprite));
                _sprite.name = FreeSlotFinder.SelfPrefix + "Earthquake" + suffix;
                _sprite.relativePosition = new Vector3(x, y);
                _sprite.width = PlotWidth;
                _sprite.height = PlotHeight;
                // UITextureSprite.EnsureMaterial makes the material for us, copied from
                // defaultAtlas (see the disassembly above). Setting texture is enough.
                _sprite.texture = _texture;
                _sprite.isVisible = false;

                Fill(Background);
                DrawAxis(0, PlotHeight);
                _texture.SetPixels32(_pixels);
                _texture.Apply(false);

                _available = true;
            }
            catch (System.Exception e)
            {
                // Once, at construction. This path is never taken again, so no throttling
                // is needed.
                Log.Warn("earthquake waveform plot unavailable: " + e.GetType().Name
                         + " (" + e.Message + ")");
                Destroy();
            }
            finally
            {
                // ★ The Destroy() inside the catch resets _built, so set it afterwards.
                //    This keeps "we tried and failed" from being conflated with "we have
                //    not tried yet".
                _built = true;
            }
        }

        /// <summary>
        /// Draws the waveforms. **Main thread.** If <paramref name="trace"/> is null or
        /// has zero samples, the plot is hidden.
        ///
        /// **Never draw zero samples as a flat line meaning "displacement 0".** An empty
        /// graph and a flat graph mean different things (the first is "there is no record
        /// yet", the second "there is a record and it is not shaking").
        /// </summary>
        public static void Render(SeismographTrace trace)
        {
            if (!_available) return;

            if (trace == null || trace.Count <= 0)
            {
                if (_sprite != null) _sprite.isVisible = false;
                _drawnAnything = false;
                return;
            }

            if (_sprite != null) _sprite.isVisible = true;

            // ★ This is the one place that stops a repaint every frame.
            if (_drawnAnything
                && _drawnBuildingId == trace.BuildingId
                && _drawnCount == trace.Count
                && _drawnNewestFrame == trace.NewestFrame
                && _drawnModel == trace.HasModel
                && _drawnTremor == trace.HasTremor
                && _drawnQuake == trace.HasQuake)
            {
                return;
            }

            _drawnBuildingId = trace.BuildingId;
            _drawnCount = trace.Count;
            _drawnNewestFrame = trace.NewestFrame;
            _drawnModel = trace.HasModel;
            _drawnTremor = trace.HasTremor;
            _drawnQuake = trace.HasQuake;
            _drawnAnything = true;

            try
            {
                Draw(trace);
            }
            catch (System.Exception e)
            {
                // Once it throws, never draw again (less harmful than throwing every
                // frame).
                Log.Warn("earthquake waveform draw failed: " + e.GetType().Name);
                Destroy();
                // ★ Destroy() knocks everything down, so afterwards restore both "it did
                //    build" and "it fell over while drawing". Go silently blank here and
                //    the promise in this class's doc — degradation, not a lie — is broken
                //    (the panel reads State to give the reason).
                _built = true;
                _renderFailed = true;
            }
        }

        /// <summary>
        /// On level unload, and on a drawing failure at runtime.
        /// **A <c>Texture2D</c> does not go with the GameObject, so destroy it explicitly.**
        ///
        /// **The sprite is destroyed explicitly too.** It used to just null the field,
        /// which meant that when this was called from a runtime drawing failure (the
        /// catch in <see cref="Render"/>) a hidden child component that nobody referenced
        /// was left on the panel. It only goes away with the panel **when the panel
        /// itself is destroyed**.
        /// </summary>
        public static void Destroy()
        {
            // Hide it first, in case we arrived here from a runtime drawing failure. If
            // it was destroyed before the panel (fake-null), Unity's == null catches it.
            if (_sprite != null)
            {
                _sprite.isVisible = false;
                Object.Destroy(_sprite.gameObject);
            }

            if (_texture != null) Object.Destroy(_texture);

            _texture = null;
            _sprite = null;
            _pixels = null;
            _available = false;
            _built = false;
            _renderFailed = false;
            _drawnAnything = false;
            _drawnBuildingId = 0;
            _drawnCount = 0;
            _drawnNewestFrame = 0u;
            _drawnModel = false;
            _drawnTremor = false;
            _drawnQuake = false;
        }

        private static void Draw(SeismographTrace trace)
        {
            // The window is "the PlotFrameWindow frames up to the newest sample". It is
            // divided by frames, so raising the game speed does not stretch or squash the
            // time axis (see WaveformPlot's class doc).
            uint newest = trace.NewestFrame;
            uint from = newest > SeismographRecorder.PlotFrameWindow
                ? newest - SeismographRecorder.PlotFrameWindow
                : 0u;

            // The vertical axis is normalised by the peak amplitude. The amplitude itself
            // is shown as a number on the label side, so auto-scaling here does not
            // misrepresent "how big" it is.
            // Pass a positive scale even when it is flat (peak == 0) — pass 0 and
            // WaveformPlot marks every column "no sample", turning a flat waveform into
            // an empty one.
            // ★ The vertical scale is **shared across every lane**. Normalise per lane
            //   and the comparison you most want to read — which one is bigger — becomes
            //   impossible.
            float peak = trace.PeakAbsolute;
            if (trace.HasModel && trace.ModelPeakAbsolute > peak) peak = trace.ModelPeakAbsolute;
            if (trace.HasTremor && trace.TremorPeakAbsolute > peak) peak = trace.TremorPeakAbsolute;
            float scale = peak > 0f ? 1f / peak : 1f;

            Fill(Background);

            // ★★ **Decide which lanes to draw.** The reason for stacking them rather
            //    than overlaying is unchanged: squeezing 512 frames into 320 px turns the
            //    principal component into a 6 px band, and overlaid you cannot tell which
            //    line you are looking at (confirmed by rendering offline).
            //
            //    ★ <b>When there is no vanilla earthquake, no layer-1 lane is made</b>
            //      (2026-08-22). Make one and, while only the volcano is shaking,
            //      **a flat white line takes up half the screen** and says something
            //      quite different: "there is also a vanilla earthquake and it is not
            //      shaking". Whether layer 1 means anything is declared by
            //      <c>SeismographTrace.HasQuake</c>.
            int lanes = 0;
            if (trace.HasQuake) lanes++;
            if (trace.HasModel) lanes++;
            if (trace.HasTremor) lanes++;

            // When none of them declares itself (there is a record but every source flag
            // is false), draw layer 1 as it stands rather than **going blank**. Showing
            // it beats silently erasing it.
            bool fallbackToMeasured = lanes == 0;
            if (fallbackToMeasured) lanes = 1;

            int laneHeight = PlotHeight / lanes;
            int laneIndex = 0;

            if (trace.HasQuake || fallbackToMeasured)
            {
                DrawLane(trace.Frames, trace.Values, trace.Count, from, newest, scale,
                         TraceColor, laneIndex, laneHeight, lanes);
                laneIndex++;
            }

            if (trace.HasModel)
            {
                DrawLane(trace.Frames, trace.ModelValues, trace.Count, from, newest, scale,
                         ModelColor, laneIndex, laneHeight, lanes);
                laneIndex++;
            }

            if (trace.HasTremor)
            {
                DrawLane(trace.Frames, trace.TremorValues, trace.Count, from, newest, scale,
                         TremorColor, laneIndex, laneHeight, lanes);
                laneIndex++;
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        /// <summary>One lane (axis, separator and line). Every lane has the same height.</summary>
        private static void DrawLane(uint[] frames, float[] values, int count,
                                     uint from, uint newest, float scale,
                                     Color32 color, int laneIndex, int laneHeight, int lanes)
        {
            int top = laneIndex * laneHeight;

            DrawAxis(top, laneHeight);
            if (laneIndex > 0) DrawSeparator(top);

            DrawTrace(WaveformPlot.Columns(frames, values, count,
                                           from, newest, PlotWidth, laneHeight - 1, scale),
                      color, top, laneHeight);
        }

        /// <summary>
        /// Paints each column's row number outwards from the centre. **Columns marked
        /// <c>Empty</c> (-1) are skipped** — draw 0 as the centre row and a stretch with
        /// no data becomes a stretch with no shaking (see <c>WaveformPlot.Empty</c>'s doc).
        /// </summary>
        private static void DrawTrace(int[] columns, Color32 color, int laneTop, int laneHeight)
        {
            int middle = (laneHeight - 1) / 2;
            for (int x = 0; x < PlotWidth; x++)
            {
                int row = columns[x];
                if (row == WaveformPlot.Empty) continue;

                int lo = row < middle ? row : middle;
                int hi = row < middle ? middle : row;
                for (int r = lo; r <= hi; r++) SetPixel(x, laneTop + r, color);
            }
        }

        private static void Fill(Color32 color)
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = color;
        }

        /// <summary>One lane's zero line. <paramref name="laneTop"/> is the row number of the lane's top edge.</summary>
        private static void DrawAxis(int laneTop, int laneHeight)
        {
            int middle = (laneHeight - 1) / 2;
            for (int x = 0; x < PlotWidth; x++) SetPixel(x, laneTop + middle, AxisColor);
        }

        /// <summary>
        /// The boundary between one lane and the next. **A third cue, so we do not rely
        /// on colour alone**; with it there, the picture can no longer be read as a
        /// single wave jumping up and down.
        /// </summary>
        private static void DrawSeparator(int row)
        {
            for (int x = 0; x < PlotWidth; x += 4) SetPixel(x, row, AxisColor);
        }

        /// <summary>
        /// Translates a row number (0 = top) into a <c>Texture2D</c> pixel (0 = bottom)
        /// and writes it. Forget this vertical flip and the waveform comes out mirrored —
        /// and, worse, it still looks plausible at a glance.
        /// </summary>
        private static void SetPixel(int x, int row, Color32 color)
        {
            if (x < 0 || x >= PlotWidth) return;
            if (row < 0 || row >= PlotHeight) return;

            int y = PlotHeight - 1 - row;
            _pixels[y * PlotWidth + x] = color;
        }
    }
}
