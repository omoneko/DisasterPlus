using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The sole owner of the tiles that raise disasters (④ typhoon, ⑤ volcano) inside
    /// vanilla's disaster panel.** The only tiles that may go here are ones where
    /// "pressing makes something happen".
    ///
    /// | tile | pressing it |
    /// |---|---|
    /// | ④ typhoon | **changes the cursor and brings up the intensity slider** |
    /// | ⑤ volcano | **changes the cursor and brings up the slider** (a size multiplier) |
    ///
    /// These are **the same three steps** as a vanilla disaster button —
    /// press, choose a scale, click the map. The entry points are
    /// <c>TyphoonPlacementTool.Arm</c> / <c>VolcanoPlacementTool.Arm</c>, and the slider is
    /// borrowed from vanilla as-is (<c>IntensitySlider</c>).
    ///
    /// ★★ **The ④ and ⑤ tiles open not one explanation panel.** (the owner's remark:
    ///    "you don't need to put out all that explanation, just make it like the other
    ///    disasters…")
    ///    Raising and reading are separate things, and the information for ①②④⑤ lives on the
    ///    top-left shortcut (<c>InfoShortcut</c>). Do not put a line that opens a panel back
    ///    in here.
    ///
    /// In an environment where it cannot be raised (④ = the ND DLC is not owned, ⑤ = the
    /// terrain cannot be written), the tile is **made unpressable and the reason goes in the
    /// tooltip** (<see cref="ApplyGate"/>).
    /// It is the gate that stops us creating "pressable but nothing happens", and it is also
    /// where the explanation went when the panel was removed.
    ///
    /// Right-click releases the cursor.
    ///
    /// There is **no** tile for the ③ fire whirl (it only ever arises on its own. See the
    /// <c>FireWhirlFeature</c> doc).
    ///
    /// Why one type owns all four
    /// --------------------------
    /// The five buttons used to be five separate types, each searching for a free slot with
    /// <see cref="FreeSlotFinder"/> from the same preferred coordinates of (8,50). The
    /// output_log.txt from the first playtest records the result verbatim:
    ///
    ///   no free UI slot found after 30 tries; ... (it may overlap)
    ///   forecast panel button installed at (8,50)
    ///   earthquake panel button installed at (8,50)
    ///   typhoon panel button installed at (8,50)
    ///   volcano panel button installed at (8,50)
    ///
    /// Four of them piled up on one point. **As long as more than one thing decides positions,
    /// this accident will keep happening in a new form.** So there is now one thing that
    /// decides positions, laying out a single ordered list in one loop — it becomes
    /// structurally impossible for two to land in the same place. The fixed (8,8) that ③'s
    /// button (the old FireWhirlPanelButton) used to hold is folded into this list too, with
    /// no exception left behind.
    ///
    /// Where they go (measured from the IL)
    /// ------------------------------------
    /// Vanilla's disaster panel is <c>DisastersPanel</c> (in Assembly-CSharp, no namespace,
    /// public sealed, derives from <c>GeneratedScrollPanel</c>). Do not guess the name string
    /// "DisastersPanel"; find it by type (through
    /// <see cref="SceneObjects.FindInScene{T}"/>. On Unity 5.6,
    /// <c>Object.FindObjectOfType</c> does not return inactive GameObjects).
    ///
    /// <c>DisastersPanel</c> is not the UIPanel itself but derives from
    /// <c>UICustomControl</c>, and <c>component</c> returns the real UI component on the same
    /// GameObject. The disaster icons are laid out in a <c>UIScrollablePanel</c> beneath it,
    /// and, measured from the IL, <c>GeneratedScrollPanel.Awake</c> does nothing but
    /// <c>m_ScrollablePanel = this.GetComponentInChildren&lt;UIScrollablePanel&gt;()</c>, so
    /// the same path (<c>GetComponentInChildren</c>, which includes the object itself) reaches
    /// the same one. <c>m_ScrollablePanel</c> is private — which is precisely why no
    /// reflection is needed.
    ///
    /// **Add a child to that row and vanilla's autolayout decides the position.** Vanilla's
    /// own <c>CreateButton</c> never writes <c>relativePosition</c> and touches only
    /// <c>zOrder</c> (measured from the IL), so the IL itself tells us the row is
    /// auto-arranged. The row is a <c>UIScrollablePanel</c> (with a horizontal scrollbar;
    /// Awake sets <c>horizontalScrollbar.incrementAmount = 109</c>), so
    /// **there is no need to widen the panel when more tiles will not fit. The overflow
    /// scrolls.**
    ///
    /// The one condition for not being caught in vanilla's index reuse
    /// ---------------------------------------------------------------
    /// Measured from the IL, <c>GeneratedScrollPanel.CreateButton</c> reads like this:
    ///
    ///   if (m_ScrollablePanel.childCount &gt; m_ObjectIndex)
    ///       button = m_ScrollablePanel.components[m_ObjectIndex] as UIButton;   // ← reused
    ///   else
    ///       button = ... AttachUIComponent(UITemplateManager.GetAsGameObject(kPlaceableItemTemplate));
    ///
    /// In other words, **vanilla may at any time remake the first m_ObjectIndex children of
    /// the row into its own tiles** (the name and the sprites are overwritten; only the
    /// eventClick we attached survives, so it breaks by running our action when a vanilla
    /// disaster tile is pressed).
    /// There is exactly one condition that avoids this:
    /// **confirm that at least one vanilla tile is already there, and add after it.**
    /// Our children are then always at index m_ObjectIndex or beyond.
    /// While <see cref="FirstVanillaTile"/> returns null, not one is added.
    ///
    /// For the case where one is remade anyway (an environment where the number of vanilla
    /// tiles grows later), <see cref="DropRecycled"/> checks every pass and, if the name has
    /// changed, **lets it go by detaching only the eventClick, without destroying it**.
    /// Destroying it would break vanilla's layout.
    ///
    /// About the icons
    /// ---------------
    /// **Do not specify a single foreground sprite name.** Sprite names are atlas data and
    /// cannot be read from the assembly, so guessing at a name can give you "an invisible
    /// button". Only the background is borrowed from the neighbouring vanilla tile, *through
    /// the real object* (no string is invented, so it cannot become a name that does not
    /// exist).
    /// They are told apart by <c>text</c> (each feature's short name) and the tooltip.
    /// Measured from the IL, <c>UITextComponent.font</c> fills in
    /// <c>GetUIView().defaultFont</c> when it is null and returns that, so the text appears
    /// even without specifying a font. **Even if the background cannot be obtained, the text
    /// always survives.**
    ///
    /// Language switching
    /// ------------------
    /// The labels are not frozen into a <c>static readonly string[]</c>; they are re-read from
    /// <c>Strings</c> through the <see cref="Entry.Label"/> delegate on every maintenance pass.
    /// Change the language in-game and it follows on the next maintenance pass (within 0.5 s).
    ///
    /// Threading
    /// ---------
    /// Main thread only. Never call it from the sim thread.
    /// </summary>
    public static partial class DisasterPanelBar
    {
        // The identifiers diagnostics use to look up each feature's installation state. These
        // are not displayed strings.
        //
        // ★ The identifiers for ① forecast and ② earthquake are **not** here. Those two tiles
        //   were removed from the disaster panel (read-only things open from InfoHub), and the
        //   diagnostics now query InfoHub.IsInstalled / InfoHub.Placement.
        //   Do not keep an unused constant on the grounds that "we might need it one day".
        public const string IdTyphoon = "typhoon";
        public const string IdVolcano = "volcano";

        /// <summary>The trench earthquake (②'s second layer. **The only quake that brings a tsunami**).</summary>
        public const string IdTrenchQuake = "trenchQuake";

        /// <summary>The retry interval until the row is found (in main-thread updates).</summary>
        private const int SearchIntervalFrames = 120;

        /// <summary>The maintenance interval once installed. Detecting reuse, refreshing labels, following setting changes.</summary>
        private const int MaintainIntervalFrames = 30;

        /// <summary>How many attempts before giving up on finding the row. Do not search forever.</summary>
        private const int MaxSearchAttempts = 100;

        /// <summary>If the row is not found in this many attempts, fall back to the floating bar.</summary>
        private const int FallbackAfterAttempts = 20;

        /// <summary>The default size in an environment with no tiles at all (a vanilla tile is roughly this).</summary>
        private static readonly Vector2 DefaultTileSize = new Vector2(109f, 100f);

        /// <summary>The spacing used when laying them out ourselves, if autolayout was switched off.</summary>
        private const float ManualGap = 4f;

        private delegate string TextSource();
        private delegate bool Gate();
        private delegate void Command();

        /// <summary>
        /// The description of one button. **Do not hold displayed strings here as values**
        /// (they would freeze on a language change). <see cref="Button"/> is a Unity object, so
        /// **test <c>!= null</c> per element** rather than "the list is not null"
        /// (a destroyed fake-null only shows up on the element).
        /// </summary>
        private sealed class Entry
        {
            public readonly string Id;
            public readonly string ComponentName;
            public readonly TextSource Label;
            public readonly TextSource Tooltip;
            public readonly Gate Wanted;
            public readonly Command Activate;

            /// <summary>
            /// Whether this tile **can actually make something happen** in this environment.
            /// While false the tile becomes unpressable and the tooltip states the reason
            /// (<see cref="Reason"/>). null means it is always pressable.
            ///
            /// ★ This is the gate that stops us creating "pressable but nothing happens".
            ///   Pressing a tile used to open a panel, and the reason appeared there. Now that
            ///   the tiles only raise things, there is no panel to open — the only home for
            ///   the reason is the tooltip. The requirement was **not to add explanation to
            ///   the screen**, not to remove the reason.
            /// </summary>
            public readonly Gate Usable;

            /// <summary>The reason put in the tooltip when it cannot be pressed. Paired with <see cref="Usable"/>.</summary>
            public readonly TextSource Reason;

            /// <summary>The panel to close when the button goes away. null if there is none.</summary>
            public readonly Command HideBody;

            public UIButton Button;
            public MouseEventHandler Handler;

            public Entry(string id, TextSource label, TextSource tooltip,
                         Gate wanted, Command activate, Command hideBody,
                         Gate usable, TextSource reason)
            {
                Id = id;
                ComponentName = FreeSlotFinder.SelfPrefix + id + "Button";
                Label = label;
                Tooltip = tooltip;
                Wanted = wanted;
                Activate = activate;
                HideBody = hideBody;
                Usable = usable;
                Reason = reason;
            }

            /// <summary>Whether this tile may be pressed right now. Always true if there is no gate.</summary>
            public bool IsUsable() { return Usable == null || Usable(); }
        }

        /// <summary>
        /// **This one list is the only thing that decides the order.** To add one, add a line
        /// here (do not invent coordinates). In numbered order (④ typhoon, ⑤ volcano).
        ///
        /// ★★ **The only tiles that may be added here are ones where "pressing raises a
        ///    disaster".** Read-only things (① forecast, ② earthquake, and the state of ④ and
        ///    ⑤) became tabs on <see cref="InfoHub"/> (the top-left shortcut).
        ///
        /// ★★ **There is no ③ fire whirl tile here.** On the owner's decision that "a fire
        ///    whirl is not something you raise deliberately; it only arises on its own during
        ///    a great fire", it was removed along with the whole manual path (the old
        ///    <c>FireWhirlPlacementTool</c>) (see the <see cref="FireWhirlFeature"/> class
        ///    doc). The tile itself was deleted so as **not to leave a tile that does
        ///    nothing**, so do not add ③'s line back in here.
        ///    ③'s state is read from the diagnostics (the overlay / the dump).
        ///
        /// This list is a static that survives across cities, but <see cref="Remove"/> resets
        /// the Unity objects it holds to null per element (do not look at the list itself and
        /// conclude "it is still alive").
        /// </summary>
        private static readonly List<Entry> Entries = new List<Entry>
        {
            // ★★ The ① forecast and ② earthquake tiles are **not here.** Read-only things
            //    moved to the top-left shortcut (<c>InfoHub</c>) (the owner's request:
            //    "make the information screens open from a shortcut button in the top left").
            //    **Do not move them back to the disaster panel** — that panel is the place for
            //    "raising".
            //
            // ★★ ④ and ⑤ are **tiles that raise disasters**. Pressing changes the cursor to
            //    the placement cursor and **brings up vanilla's intensity slider**
            //    (IntensitySlider). Click the map and it happens at that spot — the same three
            //    steps as a vanilla disaster button, and **not one explanation panel opens.**
            new Entry(IdTyphoon,
                      delegate { return Strings.TyphoonTitle; },
                      delegate { return Strings.TyphoonButtonTooltip; },
                      delegate { return ModSettings.TyphoonEnabled.value; },
                      TyphoonPlacementTool.Arm,
                      TyphoonPanel.Hide,
                      delegate { return ModCompat.NaturalDisastersOwned; },
                      delegate { return Strings.TyphoonNeedsDlc; }),

            new Entry(IdVolcano,
                      delegate { return Strings.VolcanoButtonLabel; },
                      delegate { return Strings.VolcanoButtonTooltip; },
                      delegate { return ModSettings.VolcanoEnabled.value; },
                      VolcanoPlacementTool.Arm,
                      VolcanoPanel.Hide,
                      TerrainWritable,
                      delegate { return Strings.VolcanoTerrainUnavailable; }),

            // ★★ The trench earthquake (2026-08-22, the owner's request).
            //    **It happens not where you pressed but in the sea nearest to it**
            //    (see the TrenchQuakePlacementTool class doc).
            //    Vanilla's earthquakes come with no tsunami, so this tile is the only way in
            //    when you want to see one.
            new Entry(IdTrenchQuake,
                      delegate { return Strings.TrenchQuakeButtonLabel; },
                      delegate { return Strings.TrenchQuakeButtonTooltip; },
                      delegate { return ModSettings.TrenchQuakeEnabled.value; },
                      TrenchQuakePlacementTool.Arm,
                      null,
                      delegate { return ModCompat.NaturalDisastersOwned; },
                      delegate { return Strings.TrenchQuakeNeedsDlc; }),
        };

        /// <summary>
        /// Whether this environment lets ⑤ raise the ground by even one metre. **Checked once
        /// per city and remembered** — <c>VolcanoReader.ScanTerrainFacts</c> is a reflection
        /// sweep, not something to call on every maintenance pass (every 0.5 s).
        ///
        /// The answer is a fact about the game build and does not change within a city, but it
        /// is thrown away by <see cref="Remove"/> (so the next city always checks again).
        /// </summary>
        private static bool _terrainWritableKnown;
        private static bool _terrainWritable;

        private static bool TerrainWritable()
        {
            if (_terrainWritableKnown) return _terrainWritable;
            _terrainWritable = VolcanoReader.ScanTerrainFacts().Usable;
            _terrainWritableKnown = true;
            return _terrainWritable;
        }

        private static UIScrollablePanel _row;
        private static UIPanel _fallbackBar;

        /// <summary>Stood down after an exception. This type does nothing from then on.</summary>
        private static bool _dead;

        /// <summary>The search for the row was abandoned. Only maintenance of the floating bar continues.</summary>
        private static bool _searchStopped;

        private static bool _manualLayout;
        private static int _frames;
        private static int _attempts;
        private static Vector2 _fallbackOrigin;
        private static bool _fallbackFoundFreeSlot;

        /// <summary>Warn about falling back exactly once (Log.Warn has no throttle).</summary>
        private static bool _fallbackAnnounced;

        /// <summary>
        /// For diagnostics. Whether the button for this id is on screen right now.
        ///
        /// ★ This and <see cref="Placement"/> are the only things read from the sim thread
        ///   (each feature's <c>WriteDiagnostics</c>). **They only read; they never touch the
        ///   UI** — <c>UnityEngine.Object</c>'s <c>==</c> is a comparison of native pointer
        ///   fields and involves not a single Unity API call.
        ///   The same treatment as the old <c>*PanelButton.Installed</c>.
        ///   **Do not add an inlet here that modifies the UI.**
        /// </summary>
        public static bool IsInstalled(string id)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id == id) return Entries[i].Button != null;
            }
            return false;
        }

        /// <summary>
        /// For diagnostics. Where the buttons are right now, in one line.
        /// **There is no longer any need to suspect them of overlapping each other** —
        /// positions are decided by a single loop over a single list, so there is no path by
        /// which two land in the same place.
        /// </summary>
        public static string Placement
        {
            get
            {
                if (_fallbackBar != null)
                {
                    return "floating fallback bar at (" + _fallbackOrigin.x + "," + _fallbackOrigin.y
                           + ") " + (_fallbackFoundFreeSlot ? "(free slot found)" : "(may overlap other mods)");
                }
                if (_dead) return "disabled after an error (see the log)";
                if (_row == null)
                {
                    return _searchStopped
                        ? "not installed (gave up looking for the vanilla disasters panel)"
                        : "not installed yet (still looking for the vanilla disasters panel)";
                }
                return _manualLayout
                    ? "inside the vanilla disasters panel, laid out by Disaster + (row autoLayout is off)"
                    : "inside the vanilla disasters panel, laid out by the panel's own autoLayout";
            }
        }

        /// <summary>
        /// Called every frame from the main thread. The real work is thinned out.
        ///
        /// **Do not emit <c>Log.Warn</c> / <c>Log.Error</c> every time on this path**
        /// (neither has a throttle). "Not there yet" goes to <c>Log.Diag</c>, and a permanent
        /// abandonment is closed off to a single occurrence by a state flag.
        /// </summary>
        public static void Tick()
        {
            if (_dead) return;

            int interval = _row != null ? MaintainIntervalFrames : SearchIntervalFrames;
            if (_frames++ < interval) return;
            _frames = 0;

            try
            {
                Maintain();
            }
            catch (Exception e)
            {
                // Stand down permanently here, so we do not re-enter on every frame after an
                // exception.
                _dead = true;
                Log.Error("disaster panel bar failed", e);
            }
        }

        /// <summary>On level unload. Makes sure the next city always starts with one of each.</summary>
        public static void Remove()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, true);

            if (_fallbackBar != null)
            {
                UnityEngine.Object.Destroy(_fallbackBar.gameObject);
                _fallbackBar = null;
            }

            _row = null;
            _dead = false;
            _searchStopped = false;
            _manualLayout = false;
            _frames = 0;
            _attempts = 0;
            _fallbackOrigin = Vector2.zero;
            _fallbackFoundFreeSlot = false;
            _fallbackAnnounced = false;
            _terrainWritableKnown = false;
            _terrainWritable = false;
        }

        // ------------------------------------------------------------------
        // Maintenance
        // ------------------------------------------------------------------

        private static void Maintain()
        {
            DropRecycled();

            if (_row == null && !_searchStopped) _row = FindRow();

            if (_row == null)
            {
                // While there is no row, keep the floating bar side maintained (following a
                // language change works here too).
                if (!_searchStopped) CountFailedSearch();

                // Follow setting changes on the fallback too. Skip this and, only in an
                // environment where the row is never found, "the button remains after the
                // feature was switched off" — and since the button remains it can be pressed,
                // so the panel you thought you had switched off opens.
                if (_fallbackBar != null && !WantedMatchesInstalled()) RebuildFallbackBar();

                RefreshText();
                return;
            }

            // Once the row is found, fold the floating bar away. Never create a state where
            // the same button appears in both.
            if (_fallbackBar != null) DismissFallbackBar();

            UIButton sample = FirstVanillaTile(_row);
            if (sample == null)
            {
                // Not one vanilla tile is in the row yet. Adding here would get us caught in
                // CreateButton's index reuse (see the class doc).
                Log.Diag("panelBar", "the disaster row has no vanilla tile yet; not attaching");
                return;
            }

            SyncButtons(_row, sample);
            RefreshText();
        }

        private static void CountFailedSearch()
        {
            _attempts++;

            if (_attempts >= FallbackAfterAttempts) EnsureFallbackBar();

            if (_attempts > MaxSearchAttempts)
            {
                _searchStopped = true;
                Log.Warn("gave up looking for the vanilla disasters panel after "
                         + MaxSearchAttempts + " attempts");
                return;
            }

            Log.Diag("panelBar", "vanilla disasters panel not found yet (attempt " + _attempts + ")");
        }

        /// <summary>
        /// Brings the set that is wanted and the set that is actually there into agreement.
        /// When they disagree, **rebuild all of our buttons** — appending only the missing
        /// ones would make our order depend on the order in which the settings were toggled.
        /// There are four, so it is cheap.
        /// </summary>
        private static void SyncButtons(UIScrollablePanel row, UIButton sample)
        {
            if (WantedMatchesInstalled()) return;

            // Let go only while rebuilding. **Do not close the panels of the ones about to be
            // rebuilt** (this avoids an open panel closing merely because an unrelated
            // feature's setting was touched).
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, !Entries[i].Wanted());

            Vector2 size = (sample.size.x > 1f && sample.size.y > 1f) ? sample.size : DefaultTileSize;
            _manualLayout = !row.autoLayout;

            // The origin, for environments where autolayout is switched off and nowhere else.
            // **They are placed in order by a single loop, so two can never overlap, with or
            // without autolayout.**
            float x = RightEdgeOfTiles(row) + ManualGap;
            float y = sample.relativePosition.y;

            int placed = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (!e.Wanted()) continue;

                Create(e, row, sample, size);
                if (_manualLayout && e.Button != null)
                {
                    e.Button.relativePosition = new Vector3(x + placed * (size.x + ManualGap), y);
                }
                placed++;
            }

            Log.Info("disaster panel buttons installed: " + placed + " inside the vanilla disasters panel ("
                     + (_manualLayout ? "laid out by Disaster +" : "panel autoLayout") + ")");
        }

        private static void Create(Entry e, UIScrollablePanel row, UIButton sample, Vector2 size)
        {
            // If anything was left behind by a previous city or another path, always throw it
            // away before building. Throw away **only children whose names are ours** (a child
            // vanilla remade and renamed now belongs to vanilla, so do not touch it).
            UIComponent stale = row.Find<UIComponent>(e.ComponentName);
            if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);

            UIButton b = row.AddUIComponent<UIButton>();
            b.name = e.ComponentName;
            b.size = size;

            // The background is borrowed from the neighbouring vanilla tile, *through the real
            // object*. We invent no sprite-name string, so it cannot become a name that does
            // not exist.
            b.atlas = sample.atlas;
            b.normalBgSprite = sample.normalBgSprite;
            b.hoveredBgSprite = sample.hoveredBgSprite;
            b.pressedBgSprite = sample.pressedBgSprite;
            b.focusedBgSprite = sample.focusedBgSprite;
            b.disabledBgSprite = sample.disabledBgSprite;

            // **Do not specify a single vanilla foreground sprite** (see the class doc).
            // Guessing at a name can give you "an invisible tile", and ④ and ⑤ are disasters
            // that do not exist in vanilla, so there is no artwork to guess at in the first
            // place.
            b.textScale = 0.7f;
            b.wordWrap = true;
            b.textHorizontalAlignment = UIHorizontalAlignment.Center;
            b.textVerticalAlignment = UIVerticalAlignment.Middle;
            b.textPadding = new RectOffset(4, 4, 4, 4);
            b.text = e.Label();

            // ★★ **Put the artwork on** (2026-08-22, the owner's request: "I'd like the
            //    volcano and typhoon tab icons to be illustrations"). The artwork is drawn by
            //    us (<c>Core/Common/DisasterIconArt</c>) and attached as a
            //    <c>UITextureSprite</c>.
            //
            //    ★ If it does not go on, **leave the text as it is**. That is why <c>text</c>
            //      was set first above — never leave a silently empty tile.
            if (AttachIcon(e, b)) b.text = "";

            ApplyGate(e, b);

            // Hold it in a field rather than passing the lambda directly. Unless it can be
            // detached from eventClick when reuse is detected, our action stays on vanilla's
            // tile.
            Entry captured = e;
            captured.Handler = delegate(UIComponent c, UIMouseEventParameter p) { OnClick(captured, p); };
            b.eventClick += captured.Handler;

            e.Button = b;
        }

        /// <summary>
        /// Attaches our own artwork to the tile. Returns true if it went on.
        ///
        /// ★ Make sure the <c>UITextureSprite</c> **does not swallow clicks**
        ///   (<c>isInteractive = false</c>). If it does, pressing over the artwork gets no
        ///   response from the tile — the siren mod does the same thing in the same place.
        ///
        /// ★ The size is 70% of the tile. The same thinking as the text padding
        ///   (<c>textPadding</c>): vanilla's tile artwork is not drawn right to the edges
        ///   either.
        /// </summary>
        private static bool AttachIcon(Entry e, UIButton button)
        {
            try
            {
                Texture2D tex = null;
                if (e.Id == IdVolcano) tex = DisasterTileIcons.Volcano;
                else if (e.Id == IdTyphoon) tex = DisasterTileIcons.Typhoon;
                else if (e.Id == IdTrenchQuake) tex = DisasterTileIcons.TrenchQuake;

                if (tex == null) return false;

                var icon = button.AddUIComponent<UITextureSprite>();
                icon.name = e.ComponentName + "Icon";
                icon.texture = tex;
                icon.isInteractive = false;

                float side = Mathf.Min(button.size.x, button.size.y) * 0.70f;
                icon.size = new Vector2(side, side);
                icon.relativePosition = new Vector3((button.size.x - side) * 0.5f,
                                                    (button.size.y - side) * 0.5f);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Warn("the disaster tile icon could not be attached ("
                         + ex.GetType().Name + "); the tile keeps its text label");
                return false;
            }
        }

        /// <summary>
        /// Re-reads the displayed strings. This is what follows an in-game language change
        /// (the values in <c>Strings</c> are rewritten by <c>LocaleLoader</c>).
        /// </summary>
        private static void RefreshText()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (e.Button == null) continue;

                // ★ Do not put text back on a tile that has artwork. **Show both and they
                //   overlap.** The tooltip (ApplyGate and Tooltip below) still follows the
                //   language.
                bool hasIcon = e.Button.Find<UIComponent>(e.ComponentName + "Icon") != null;

                string label = hasIcon ? "" : e.Label();
                if (e.Button.text != label) e.Button.text = label;

                ApplyGate(e, e.Button);
            }
        }

        /// <summary>
        /// Keeps "can it be pressed" and the tooltip in step.
        ///
        /// ★ **A tile that cannot be pressed must look unpressable.** If it can be pressed you
        ///   get "pressing does nothing", and since the tile no longer opens an explanation
        ///   panel, the reason appears nowhere. The look of <c>isEnabled = false</c> is
        ///   carried by the <c>disabledBgSprite</c> borrowed from the neighbouring vanilla
        ///   tile, and the reason is carried by the tooltip.
        /// </summary>
        private static void ApplyGate(Entry e, UIButton b)
        {
            if (b == null) return;

            bool usable = e.IsUsable();
            if (b.isEnabled != usable) b.isEnabled = usable;

            string tip = usable ? e.Tooltip() : (e.Reason != null ? e.Reason() : e.Tooltip());
            if (b.tooltip != tip) b.tooltip = tip;
        }

        /// <summary>
        /// Lets go of a button that was destroyed, or **remade by vanilla**.
        ///
        /// Never <c>Object.Destroy</c> a remade one — it is now part of vanilla's layout, and
        /// removing it shifts the indices. Detach only the eventClick (leave it and our action
        /// runs when a vanilla disaster tile is pressed).
        /// The rebuild is done by <see cref="SyncButtons"/> on the same maintenance pass, so
        /// there is no need to close a panel here.
        /// </summary>
        private static void DropRecycled()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (e.Button == null) { Release(e); continue; }
                if (e.Button.name == e.ComponentName) continue;

                Log.Diag("panelBar", "the game reused the " + e.Id + " button as one of its own tiles; releasing it");
                Detach(e, false, false);
            }
        }

        /// <summary>
        /// Lets go of a button.
        /// </summary>
        /// <param name="destroy">
        /// Whether to actually destroy it. **Pass false for a button vanilla has remade**
        /// (see the <see cref="DropRecycled"/> doc).
        /// </param>
        /// <param name="closePanel">
        /// Whether to close the panel this button opens as well. If the button alone goes
        /// first, the screen is left with no way to close a panel that is still open (raised
        /// in ①'s review).
        /// false when it is about to be rebuilt.
        /// </param>
        private static void Detach(Entry e, bool destroy, bool closePanel)
        {
            if (e.Button != null)
            {
                if (e.Handler != null) e.Button.eventClick -= e.Handler;
                if (destroy) UnityEngine.Object.Destroy(e.Button.gameObject);
            }

            Release(e);

            if (closePanel && e.HideBody != null)
            {
                try { e.HideBody(); }
                catch (Exception ex) { Log.Error("closing the " + e.Id + " panel failed", ex); }
            }
        }

        private static void Release(Entry e)
        {
            e.Button = null;
            e.Handler = null;
        }

        /// <summary>
        /// Whether the set the settings want agrees with the set that is actually there.
        /// **Test <c>Button != null</c> per element** (a destroyed fake-null only shows up on
        /// the element, so do not decide by looking at the list).
        /// </summary>
        private static bool WantedMatchesInstalled()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Wanted() != (Entries[i].Button != null)) return false;
            }
            return true;
        }

        private static void OnClick(Entry e, UIMouseEventParameter p)
        {
            try
            {
                // Our button is a child of the row, so the press also reaches the row.
                //
                // Measured from the IL: DisastersPanel.OnButtonClicked **does nothing** when
                // <c>component.objectUserData as DisasterInfo</c> is null.
                // We never set objectUserData on our buttons, so a vanilla disaster can never
                // be armed down this path. The two lines below are belt and braces on top of
                // that; what they actually achieve is "the pressed tile does not stay
                // selected" (GeneratedScrollPanel.SelectByIndex takes -1 through Mathf.Max and
                //  merely returns every tile's state to Normal. Measured from the IL; it does
                //  not throw).
                if (p != null) p.Use();
                ClearVanillaSelection();

                // ★ A disabled tile should not be pressable, but do not trust a single input
                //   path. Slip past here and "pressing does nothing" goes straight through.
                if (!e.IsUsable()) return;

                e.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("the " + e.Id + " button click failed", ex);
            }
        }

        /// <summary>
        /// Clears vanilla's "which tile is currently selected".
        /// Measured from the IL, <c>GeneratedScrollPanel.selectedIndex</c> has a public setter
        /// (no reflection needed).
        /// </summary>
        private static void ClearVanillaSelection()
        {
            DisastersPanel panel = SceneObjects.FindInScene<DisastersPanel>();
            if (panel != null) panel.selectedIndex = -1;
        }

        // ------------------------------------------------------------------
        // Finding the row
        // ------------------------------------------------------------------

        private static UIScrollablePanel FindRow()
        {
            DisastersPanel panel = SceneObjects.FindInScene<DisastersPanel>();
            if (panel == null) return null;

            UIComponent container = panel.component;
            if (container == null) return null;

            // The same path as GeneratedScrollPanel.Awake (which includes the object itself).
            // There is no need to peek at the private m_ScrollablePanel by reflection.
            return container.GetComponentInChildren<UIScrollablePanel>();
        }

        /// <summary>
        /// Returns one **vanilla** tile from the row, or null if there is none.
        /// It is what the size, the atlas and the background sprites are borrowed from, and at
        /// the same time the confirmation that "we are now out of index-reuse range" (see the
        /// class doc).
        /// </summary>
        private static UIButton FirstVanillaTile(UIScrollablePanel row)
        {
            IList<UIComponent> children = row.components;
            if (children == null) return null;

            for (int i = 0; i < children.Count; i++)
            {
                UIButton b = children[i] as UIButton;
                if (b == null) continue;
                if (IsOurs(b.name)) continue;
                if (b.size.x <= 1f || b.size.y <= 1f) continue;
                return b;
            }
            return null;
        }

        /// <summary>The right edge of the vanilla tiles in the row. Used only where autolayout is switched off.</summary>
        private static float RightEdgeOfTiles(UIScrollablePanel row)
        {
            float right = 0f;
            IList<UIComponent> children = row.components;
            if (children == null) return right;

            for (int i = 0; i < children.Count; i++)
            {
                UIComponent c = children[i];
                if (c == null) continue;
                if (IsOurs(c.name)) continue;

                float edge = c.relativePosition.x + c.size.x;
                if (edge > right) right = edge;
            }
            return right;
        }

        private static bool IsOurs(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith(FreeSlotFinder.SelfPrefix, StringComparison.Ordinal);
        }
    }
}
