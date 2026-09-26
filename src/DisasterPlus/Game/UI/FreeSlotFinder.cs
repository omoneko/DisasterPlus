using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Finds a position at runtime that does not overlap other mods' or vanilla's buttons.
    ///
    /// On CS1 it is the convention for mod buttons to pile up in the top-left, and fixed
    /// coordinates are bound to collide eventually (an explicit requirement from the user).
    ///
    /// ★★ **There are only two callers, and those two are not independent of each other.**
    ///
    ///   1. <see cref="InfoHub"/> — one shortcut in the top-left. **Always on screen.**
    ///   2. <see cref="DisasterPanelBar"/>'s fallback bar — appears only in an environment
    ///      where vanilla's disaster panel simply cannot be found. **It does not decide a
    ///      preferred of its own; it starts searching directly below 1**
    ///      (see <c>EnsureFallbackBar</c> over there).
    ///
    /// 2 starts directly below 1 so as to make the accident below structurally impossible —
    /// **the search only ever moves downwards, so there is no path by which 2 returns 1's
    /// position.** Do not go back to a shape where ① to ⑤ each call in here individually.
    /// **If you add a new caller, have it search from directly below one of the existing ones**
    /// (do not share the same preferred).
    ///
    /// This doc used to say "② to ⑤ have the same problem, so they share this". What actually
    /// happened when ② to ⑤ each called with the same preferred coordinates is on record in
    /// the output_log.txt from the first playtest —
    /// <c>no free UI slot found after 30 tries</c> came out four times and four buttons piled
    /// up at (8,50). **The search was not wrong; having four things doing the searching was.**
    /// The ①②④⑤ buttons now line up inside vanilla's disaster panel (the panel's autolayout
    /// decides the positions), and this is called only **in an environment where that panel
    /// simply cannot be found, to decide the origin of a single fallback bar, once**.
    /// The buttons in that bar are laid out relative to the origin in one loop, so there is no
    /// longer any question of asking here five times.
    ///
    /// ── And then it broke a second time (second playtest) ─────────────────────────
    ///
    /// Having fixed the above, it then missed on the other side —
    /// <c>Disaster + info button installed at (8,1094)</c>. On a 1080-high screen, y = 1094.
    /// The search descended 30 steps unconditionally and **correctly judged the point where it
    /// left the screen to be "free"** (being outside, it overlaps nothing).
    /// Unusable because it overlaps and unusable because it is invisible are equally broken.
    ///
    /// So <b>"do not take a single step off the screen" was made a higher-priority constraint
    /// on the search</b>. The number of candidates is decided by
    /// <see cref="DisasterPlus.Core.Common.ScreenSlot"/> (in Core, with tests pinning the
    /// boundaries), and the size of the screen is read from
    /// <c>UIView.GetScreenResolution()</c> — **do not assume 1080.**
    /// If there is not a single free slot on screen, it falls back to preferred (which is
    /// likewise rounded into the screen). <b>That branch really is reached</b> now. Before,
    /// even when it was reached, an off-screen "free" slot was found first, so it was
    /// effectively dead.
    ///
    /// API measurements (ColossalManaged.dll / UnityEngine.dll inspected directly with
    /// docs/tools/ilload.ps1 + ildasm.ps1. Details in task-2-report.md):
    ///   - UIView.GetAView() is static.
    ///   - UIComponent derives from UnityEngine.MonoBehaviour (Behaviour → Component → Object),
    ///     so GetComponentsInChildren&lt;T&gt;() resolves to the generic method on
    ///     UnityEngine.Component (not a ColossalManaged-specific overload). The default
    ///     includeInactive=false is used as-is (for the reason see the Find comment below).
    ///   - UIComponent.isVisible is implemented by looking recursively at the m_IsVisible field
    ///     and the parent's isVisible, and never consults GameObject.activeSelf (measured from
    ///     the IL).
    ///   - name comes from UnityEngine.Object (string, readable).
    /// </summary>
    public static class FreeSlotFinder
    {
        /// <summary>
        /// The naming prefix for DisasterPlus UI components. Used to keep names consistent for
        /// logging and identification. Later tasks should build component names from this
        /// constant (do not write the string "DisasterPlus" out again in each place).
        ///
        /// **Do not use it to exclude things from overlap testing** (raised in the overall
        /// review, I4). Components with this prefix (and their descendants) used to be
        /// excluded from the sweep wholesale; that was meant as "do not collide with my own
        /// insides", but what it actually did was **make every button this mod had placed
        /// invisible to all subsequent placement searches**. Design doc §6 designates this
        /// class as shared infrastructure for ② to ⑤, so when ② called Find with the same
        /// preferred it would not see the forecast button and would place itself right on top
        /// of it — this file was causing the very thing it exists to prevent. Exclusion is
        /// limited to "the one being placed right now" (the owner argument of
        /// <see cref="Find"/>).
        /// </summary>
        public const string SelfPrefix = "DisasterPlus";

        /// <summary>The limit when walking up parents. A safety valve so a reference cycle cannot stall sim.</summary>
        private const int MaxAncestorDepth = 32;

        /// <summary>
        /// Anything smaller than this is decoration, not an obstacle (px).
        /// Kept at the same value as SIREN Alert's <c>SirenButton.MinWidgetSize</c>.
        /// </summary>
        private const float MinWidgetSize = 8f;

        /// <summary>
        /// Anything wider or taller than this fraction of the screen is ignored as **a
        /// container**. Its contents are picked up individually, so nothing is missed.
        /// This too is the same value as SIREN Alert's.
        /// </summary>
        private const float ContainerRatio = 0.5f;

        /// <summary>
        /// Steps down from preferred by stepY and returns the first position that does not
        /// overlap a visible element. If none is found it returns preferred with
        /// foundFree=false (better to be visible and overlapping than hidden and unfindable).
        ///
        /// ★★ **Candidates are always confined to the screen** (<see cref="ScreenSlot"/>).
        ///   The first playtest produced <c>installed at (8,1094)</c> — y = 1094 on a
        ///   1080-high screen. The search walked past the bottom edge, off the screen, and
        ///   judged that **correctly** to be "free". It was free because it was off the screen.
        ///   The old breakage (four piled up at the same coordinates) and this one (invisible)
        ///   are both unusable, but **an overlapping one can at least still be pressed**.
        ///   So "do not take a single step off the screen" is the higher-priority constraint
        ///   here, and with no free slot it falls back to a preferred rounded into the screen.
        /// </summary>
        /// <param name="owner">
        /// The component being placed right now. Only this itself and its descendants are
        /// excluded from overlap testing (so a re-placement does not collide with its own
        /// current position).
        ///
        /// **Pass null when the component does not exist yet, as on a first placement.**
        /// Nothing is excluded in that case. Passing null is a normal use, not a shortcut.
        ///
        /// Other DisasterPlus components (another feature's button, an open forecast panel and
        /// so on) **must not be excluded**. They really do occupy space on screen, and they
        /// are exactly what should be avoided (see the SelfPrefix doc).
        /// </param>
        public static Vector2 Find(Vector2 preferred, Vector2 size, float stepY,
                                   int maxTries, UIComponent owner, out bool foundFree)
        {
            return Find(preferred, size, 0f, stepY, maxTries, owner, out foundFree);
        }

        /// <summary>
        /// The two-axis version of the above. Steps one at a time along
        /// <paramref name="stepX"/> and/or <paramref name="stepY"/> and returns the first
        /// position that does not overlap a visible element.
        ///
        /// ── Why a horizontal search is needed (2026-08-22, the owner's request) ───────────────
        ///
        /// > The D＋ button sits where it overlaps the left side menu, so…
        /// > could it be shown lined up at the same height as the CSWARFRONT button and the
        /// > SIREN Alert button
        ///
        /// **Vanilla's left side menu is a vertical column.** A search that can only move
        /// vertically traces that column from top to bottom, and even when it finds a free
        /// slot it can only place **somewhere that hides part of the column** (which is what
        /// actually happened in the game). The two other mods sit in the **horizontal** row at
        /// the very top of the screen, and lining up there means searching with
        /// <c>stepX &gt; 0, stepY = 0</c>.
        ///
        /// The higher-priority constraint of **not taking a single step off the screen** is
        /// unchanged, and the number of candidates is decided for both axes together by
        /// <see cref="ScreenSlot.CandidatesInside(float,float,float,float,float,float,float,float,int)"/>.
        /// </summary>
        public static Vector2 Find(Vector2 preferred, Vector2 size, float stepX, float stepY,
                                   int maxTries, UIComponent owner, out bool foundFree)
        {
            foundFree = false;
            try
            {
                if (maxTries <= 0)
                {
                    Log.Warn("FreeSlotFinder.Find called with maxTries=" + maxTries
                             + " (<=0); no candidate was tested, using preferred position");
                    return preferred;
                }

                var view = UIView.GetAView();
                if (view == null) return preferred;

                // ★ The size of the screen. **Do not assume 1080.**
                //   Measured from the IL (ColossalManaged), UIView.GetScreenResolution()
                //   returns "the resolution in the UI coordinate system" — with a uiCamera,
                //   pixelSize / (pixelHeight / fixedHeight * scale); without one,
                //   (fixedWidth, fixedHeight). It is the same space as absolutePosition /
                //   relativePosition, so it can be compared directly.
                //   Where it cannot be read (0 or NaN), ScreenSlot falls to the
                //   "do not constrain" side.
                Vector2 screen = ReadScreenSize(view);

                // preferred itself may point off the screen (the caller starts searching from
                // directly below another button, so it can happen if that one is low down).
                preferred = new Vector2(ScreenSlot.ClampInto(preferred.x, size.x, screen.x),
                                        ScreenSlot.ClampInto(preferred.y, size.y, screen.y));

                // includeInactive is left at its default of false. Measuring
                // UIComponent.set_isVisible in the IL shows it only writes m_IsVisible and
                // refreshes the visibility cache; it never calls GameObject.SetActive. So a
                // panel that has been Hide()n (i.e. "resident but not displayed") is always
                // active as a GameObject and comes into the array even with
                // includeInactive=false. Rejecting it via isVisible is the correct path.
                //
                // Conversely, with includeInactive=true it would also pick up template copies
                // that UITemplateManager.Get/Instantiate has not attached to the screen yet
                // (the GameObject is inactive; GameObject.SetActive(true) is called later by
                // UIComponent.AttachUIComponent). These still hold m_IsVisible at the prefab's
                // serialised value (usually true), and isVisible does not consult activeSelf,
                // so the filter cannot reject them. Their absolutePosition also tends to be an
                // unsettled value from before they were placed on screen (near the origin =
                // the top-left region this search runs over), so they would be misjudged as
                // "occupied" when they occupy nowhere on screen at all, burning maxTries and
                // falling back to preferred (i.e. exactly the overlapping position this
                // feature wants to avoid).
                var all = view.GetComponentsInChildren<UIComponent>();
                if (all == null || all.Length == 0) { foundFree = true; return preferred; }

                // With stepY <= 0 the same candidate would be tested every time, which is
                // pointless as a search. Test once and stop. Otherwise the warning "tried
                // maxTries times" would disagree with reality (the same point, repeated).
                // The bottom edge of the screen stops it for the same reason — candidates
                // beyond that cannot be pressed even when free (see (8,1094) in the class doc).
                int effectiveTries = ScreenSlot.CandidatesInside(
                    preferred.x, preferred.y, size.x, size.y, stepX, stepY,
                    screen.x, screen.y, maxTries);
                if (effectiveTries <= 0)
                {
                    // Not even the first candidate fits on screen. Going further down only
                    // gets worse, so do not search.
                    Log.Warn("no on-screen UI slot is available for a "
                             + size.x + "x" + size.y + " button in a "
                             + screen.x + "x" + screen.y + " view; placing it at "
                             + preferred.x + "," + preferred.y + " (it may overlap)");
                    return preferred;
                }

                for (int attempt = 0; attempt < effectiveTries; attempt++)
                {
                    var candidate = new Vector2(preferred.x + stepX * attempt,
                                                preferred.y + stepY * attempt);
                    if (!OverlapsAny(all, candidate, size, owner))
                    {
                        foundFree = true;
                        return candidate;
                    }
                }

                // ★ This is where the class doc's "better to be visible and overlapping than
                //   hidden and unfindable" actually lives. **This branch really is reached**
                //   (in an environment where the top-left column is full of other mods).
                //   preferred was rounded into the screen above, so the return value never
                //   points off screen.
                Log.Warn("no free UI slot found after " + effectiveTries
                         + " on-screen tries (view " + screen.x + "x" + screen.y
                         + "); placing the button at the preferred position (it may overlap)");
                return preferred;
            }
            catch (System.Exception e)
            {
                Log.Error("free slot search failed", e);
                return preferred;
            }
        }

        /// <summary>
        /// The size of the screen in the UI coordinate system. **Returns (0,0) if it cannot be
        /// read** — <see cref="ScreenSlot"/> reads that as "do not constrain", so in an
        /// environment where the dimensions cannot be read it never becomes impossible to
        /// place a button at all.
        ///
        /// The path by which <c>GetScreenResolution()</c> throws (uiCamera already destroyed,
        /// and so on) has not been measured, so it is swallowed and falls to (0,0).
        /// **No Warn is emitted** — this only runs once per placement, and there would be
        /// nothing to do about it anyway.
        /// <c>fixedHeight</c> is the fallback, and that one is always a readable integer.
        /// </summary>
        /// <summary>
        /// The size of the screen in the UI coordinate system. <c>(0,0)</c> if it cannot be
        /// read — <see cref="ScreenSlot"/> reads that as "do not constrain".
        ///
        /// <see cref="InfoHub"/> uses it for rounding during a drag. **Do not make this a
        /// second thing that decides where to place** (see the class doc) — it only answers
        /// "how big is the screen", never "where does it go".
        /// </summary>
        public static Vector2 ScreenSize()
        {
            var view = UIView.GetAView();
            if (view == null) return new Vector2(0f, 0f);
            return ReadScreenSize(view);
        }

        private static Vector2 ReadScreenSize(UIView view)
        {
            try
            {
                Vector2 res = view.GetScreenResolution();
                if (ScreenSlot.IsUsableExtent(res.x) && ScreenSlot.IsUsableExtent(res.y))
                {
                    return res;
                }
            }
            catch (System.Exception e)
            {
                Log.Diag("freeSlot", "screen resolution unreadable: " + e.GetType().Name);
            }

            try
            {
                return new Vector2(view.fixedWidth, view.fixedHeight);
            }
            catch (System.Exception e)
            {
                Log.Diag("freeSlot", "fixed view size unreadable: " + e.GetType().Name);
                return new Vector2(0f, 0f);
            }
        }

        /// <summary>
        /// Returns **the right edge of the run that continues from the left** within the band
        /// <c>[bandTop, bandBottom)</c> at the top of the screen. 0 if there is no run.
        ///
        /// ── ★★ Not "the rightmost edge" (2026-08-22, second playtest) ───────
        ///
        /// The owner's instruction was <b>WF ＞ ！＞ D＋</b> in that order from the start.
        ///
        ///   - first attempt (search from the left edge)… **whoever gets there first takes
        ///     the leftmost spot**, so when this mod was first it stuck to the left edge of
        ///     the screen
        ///   - second attempt (just right of the rightmost edge in the band)… CS **puts
        ///     vanilla UI in the top-right too**, so that right edge was chosen and it
        ///     **overlapped the settings button**
        ///
        /// What is correct is <b>the right edge of the run that continues from the left</b>,
        /// and the arithmetic for that lives in <see cref="TopRowCluster"/> (in Core, with
        /// tests pinning both ways of getting it wrong).
        /// This only **collects the left and right edges of the elements in the band**.
        ///
        /// The filters match SIREN Alert's <c>SirenButton.FindTopRowPosition</c>: anything too
        /// small (decoration) and anything more than half the screen (a container) does not
        /// count as an obstacle. A container's contents are picked up individually, so nothing
        /// is missed.
        ///
        /// ★ <paramref name="owner"/> (and its descendants) do not count. Count them and every
        ///   re-placement would **push itself along by its own right edge**, escaping to the
        ///   right.
        /// </summary>
        public static float ClusterRightEdge(float bandTop, float bandBottom,
                                             UIComponent owner)
        {
            try
            {
                var view = UIView.GetAView();
                if (view == null) return 0f;

                Vector2 screen = ReadScreenSize(view);

                var all = view.GetComponentsInChildren<UIComponent>();
                if (all == null || all.Length == 0) return 0f;

                var starts = new float[all.Length];
                var ends = new float[all.Length];
                int count = 0;

                for (int i = 0; i < all.Length; i++)
                {
                    var c = all[i];
                    if (c == null) continue;
                    if (!c.isVisible) continue;
                    if (IsOwnedBy(c, owner)) continue;

                    Vector2 cs = c.size;
                    if (cs.x < MinWidgetSize || cs.y < MinWidgetSize) continue;

                    // Anything more than half the screen is a container and does not occupy
                    // the space.
                    if (ScreenSlot.IsUsableExtent(screen.x)
                        && cs.x > screen.x * ContainerRatio) continue;
                    if (ScreenSlot.IsUsableExtent(screen.y)
                        && cs.y > screen.y * ContainerRatio) continue;

                    Vector2 cp = c.absolutePosition;
                    if (cp.y >= bandBottom) continue;          // below the band
                    if (cp.y + cs.y <= bandTop) continue;      // above the band
                    if (cp.x + cs.x <= 0f) continue;           // off to the left
                    if (ScreenSlot.IsUsableExtent(screen.x) && cp.x >= screen.x) continue;

                    starts[count] = cp.x;
                    ends[count] = cp.x + cs.x;
                    count++;
                }

                return TopRowCluster.RightEdge(starts, ends, count, 0f,
                                               TopRowCluster.DefaultMaxGapPixels);
            }
            catch (System.Exception e)
            {
                Log.Diag("freeSlot", "top row scan failed: " + e.GetType().Name);
                return 0f;
            }
        }

        private static bool OverlapsAny(UIComponent[] all, Vector2 pos, Vector2 size, UIComponent owner)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;

                // Ignore elements that are not displayed. CS keeps UI templates resident while
                // hidden, and counting those would mean never finding a free slot.
                if (!c.isVisible) continue;

                // Ignore only the one being placed (and its descendants).
                // When re-placing a composite panel, its internal labels and icons carry
                // default, prefix-less names, so walk the ancestor chain rather than relying
                // on reference equality with owner alone.
                if (IsOwnedBy(c, owner)) continue;

                Vector2 cp = c.absolutePosition;
                Vector2 cs = c.size;
                if (cs.x <= 0f || cs.y <= 0f) continue;

                // Edges merely touching (coincident boundaries) do not count as an overlap.
                bool separated = pos.x + size.x <= cp.x
                              || cp.x + cs.x <= pos.x
                              || pos.y + size.y <= cp.y
                              || cp.y + cs.y <= pos.y;
                if (!separated) return true;
            }
            return false;
        }

        /// <summary>
        /// true if c is owner itself or a descendant of owner.
        /// If owner is null (a first placement, where it does not exist yet) always false,
        /// i.e. nothing is excluded.
        ///
        /// The point is to test by reference equality rather than by name. Test by name
        /// (prefix) and **other** components of this mod get caught up in the exclusion, so
        /// later features end up placed right on top of existing buttons (see the SelfPrefix
        /// doc, raised in the overall review as I4).
        ///
        /// MaxAncestorDepth stops it, so even if parent somehow formed a cycle it would not
        /// hang.
        /// </summary>
        private static bool IsOwnedBy(UIComponent c, UIComponent owner)
        {
            if (owner == null) return false;

            UIComponent cur = c;
            int depth = 0;
            while (cur != null && depth < MaxAncestorDepth)
            {
                // Compare through UnityEngine.Object's == overload
                // (so a destroyed fake-null is not mistaken by a raw reference comparison).
                if (cur == owner) return true;
                cur = cur.parent;
                depth++;
            }
            return false;
        }
    }
}
