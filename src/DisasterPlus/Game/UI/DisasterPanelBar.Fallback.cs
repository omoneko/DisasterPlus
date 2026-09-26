using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The half of <see cref="DisasterPanelBar"/> that carves out **the fallback** and
    /// nothing else.
    ///
    /// In an environment where vanilla's disaster panel simply cannot be found, it puts the
    /// ④ and ⑤ tiles on a single bar floating over the screen. It is a path that exists on
    /// the judgement that **not showing a single button would be worse**, and normal play
    /// never goes through it once.
    ///
    /// ★ The file was split purely because of the 800-line limit; **the discipline is the
    ///   same as the main file** — one thing decides positions, the search covers a single
    ///   origin point only, and it searches from directly below <see cref="InfoHub"/>
    ///   (see the <see cref="EnsureFallbackBar"/> doc).
    /// </summary>
    public static partial class DisasterPanelBar
    {
        // ------------------------------------------------------------------
        // The fallback (environments where vanilla's panel is not found)
        // ------------------------------------------------------------------

        /// <summary>
        /// Once <see cref="FallbackAfterAttempts"/> attempts have passed without the disaster
        /// panel being found, fall back to a single bar floating over the screen.
        /// **Not showing a single button would be worse.**
        ///
        /// ★★ **This is <see cref="FreeSlotFinder"/>'s second caller.**
        /// The first is <see cref="InfoHub"/> (the top-left shortcut), which is always on
        /// screen. The <c>FreeSlotFinder</c> class doc lays down "only one caller" because
        /// **when none of them can find a free slot, they all fall back to the same
        /// preferred** — which is exactly how four buttons piled up at (8,50) in the first
        /// playtest.
        ///
        /// So this bar **does not decide a preferred of its own.**
        /// It starts searching from the "below the shortcut" point that
        /// <see cref="InfoHub.TryGetBelowAnchor"/> returns. The search only ever moves
        /// downwards, so **it is structurally impossible for this bar to return the
        /// shortcut's position.**
        /// While the shortcut has not been placed yet, **wait rather than building**
        /// (if <see cref="InfoHub.Abandoned"/>, no button exists at all, so it is fine to
        /// search from the old preferred).
        ///
        /// It is called for **the single origin point of the whole bar** and never searches
        /// for two — the buttons inside are laid out at positions relative to the origin in
        /// one loop.
        /// </summary>
        private static void EnsureFallbackBar()
        {
            if (_fallbackBar != null) return;

            UIView view = UIView.GetAView();
            if (view == null)
            {
                Log.Diag("panelBar", "no UIView yet; the fallback bar cannot be created");
                return;
            }

            Vector2 preferred;
            if (!InfoHub.TryGetBelowAnchor(out preferred))
            {
                if (!InfoHub.Abandoned)
                {
                    // Wait until the shortcut's position is settled. **Place first and the
                    // shortcut ends up avoiding this bar afterwards, which brings back the
                    // very state of having two things doing the searching.**
                    Log.Diag("panelBar", "waiting for the info shortcut before placing the fallback bar");
                    return;
                }
                // The shortcut will not be placed (no button exists on screen).
                preferred = new Vector2(8f, 50f);
            }

            int wanted = 0;
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Wanted()) wanted++;
            if (wanted == 0) return;

            const float w = 150f;
            const float h = 28f;
            const float gap = 4f;
            const float pad = 4f;

            Vector2 size = new Vector2(w + pad * 2f, wanted * h + (wanted - 1) * gap + pad * 2f);

            bool foundFree;
            // owner is null. The bar is created after this call, so the "itself" that would
            // be excluded does not exist on screen yet (see the FreeSlotFinder.Find doc).
            Vector2 origin = FreeSlotFinder.Find(preferred, size, h + gap, 30, null, out foundFree);
            _fallbackOrigin = origin;
            _fallbackFoundFreeSlot = foundFree;

            UIPanel bar = (UIPanel)view.AddUIComponent(typeof(UIPanel));
            bar.name = FreeSlotFinder.SelfPrefix + "FallbackBar";
            bar.size = size;
            bar.relativePosition = new Vector3(origin.x, origin.y);
            bar.backgroundSprite = "GenericPanel";
            // The loop below decides the contents completely, so do not leave it to autolayout.
            bar.autoLayout = false;
            _fallbackBar = bar;

            int placed = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (!e.Wanted()) continue;

                UIButton b = bar.AddUIComponent<UIButton>();
                b.name = e.ComponentName;
                b.size = new Vector2(w, h);
                b.relativePosition = new Vector3(pad, pad + placed * (h + gap));
                b.normalBgSprite = "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.text = e.Label();
                ApplyGate(e, b);

                Entry captured = e;
                captured.Handler = delegate(UIComponent c, UIMouseEventParameter p) { OnClick(captured, p); };
                b.eventClick += captured.Handler;

                e.Button = b;
                placed++;
            }

            // ★ Once only. The bar is rebuilt every time a setting is toggled
            //   (RebuildFallbackBar), so warning unconditionally here would repeat a warning
            //   that has no throttle.
            if (_fallbackAnnounced) return;
            _fallbackAnnounced = true;
            Log.Warn("the vanilla disasters panel was not found after " + _attempts
                     + " attempts; the Disaster + buttons were placed on a floating bar at ("
                     + origin.x + "," + origin.y + ") instead");
        }

        /// <summary>
        /// Rebuilds the fallback bar. Called when a feature is switched off or on in the
        /// settings. **The whole bar is rebuilt** for the same reason as when placing in the
        /// row — appending only the missing ones would make the order depend on the order
        /// things were toggled.
        /// </summary>
        private static void RebuildFallbackBar()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, !Entries[i].Wanted());

            UnityEngine.Object.Destroy(_fallbackBar.gameObject);
            _fallbackBar = null;

            EnsureFallbackBar();
        }

        /// <summary>The row was found, so fold the floating bar away. The buttons are rebuilt on the row side in the same maintenance pass.</summary>
        private static void DismissFallbackBar()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, false);

            UnityEngine.Object.Destroy(_fallbackBar.gameObject);
            _fallbackBar = null;
            _fallbackOrigin = Vector2.zero;
            _fallbackFoundFreeSlot = false;
            _manualLayout = false;

            Log.Info("the vanilla disasters panel appeared; moving the Disaster + buttons into it");
        }    }
}
