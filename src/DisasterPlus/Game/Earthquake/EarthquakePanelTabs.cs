using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The earthquake panel's tabs. **Main thread only.**
    ///
    /// ── Why tabs (the vertical budget had already run out) ──────────────
    ///
    /// UIView's coordinate system is normalised to a height of 1080. By the time layer 1
    /// was finished the panel had already stacked up to **about 1050**, and the one trick
    /// available — widening it from 520 to 640 so the reserved height for wrapped rows
    /// could be cut — had already been spent to fit the overlay's three rows.
    /// <see cref="EarthquakePanel.ClampToView"/> only warns once when the bottom edge
    /// runs off; **it cannot prevent the overflow itself**. Layer 2 (Tasks 9-11) adds
    /// three sections, so carrying on appending rows would certainly have pushed the
    /// explanatory text off screen — and text that was written but cannot be read is
    /// worse than text that was never written.
    ///
    /// ── Why tabs, out of the three options ──────────────────────────
    ///
    /// | Option | Why it was not taken |
    /// |---|---|
    /// | A scrolling region | Measured in the IL, <c>UIScrollablePanel</c> derives
    ///   directly from <c>UIComponent</c>, not from <c>UIPanel</c>. That would mean
    ///   rebuilding the row helpers' parameter type (<c>UIPanel</c>) together with what
    ///   <c>WaveformView.Build</c> and <see cref="EarthquakeOverlayRows"/> accept, and on
    ///   top of that the scrollbar needs its thumb and track sprites assembled by hand,
    ///   so **it is not just a structural change**. |
    /// | Collapsible sections | Every open and close rebuilds the y of every row, i.e.
    ///   more layout at runtime. "What is where" becomes state-dependent, and the
    ///   explanatory text stays folded away without anyone noticing. |
    /// | **Tabs (chosen)** | The only types involved are <c>UIPanel</c> and
    ///   <c>UIButton</c>, both already in use. The row helpers' parameter type does not
    ///   change either (a page is itself a <c>UIPanel</c>). The y values are computed
    ///   once at construction, with no rearranging at runtime. |
    ///
    /// ── Layer 2 does not go inside the tabs ─────────────────────────
    ///
    /// The plan states that layer 2 is built **below** layer 1, with no rearranging at
    /// runtime. Splitting it across tabs would make "below" meaningless, so **layer 2's
    /// sections sit outside the tab area, beneath it** (<see cref="EarthquakeLayer2Rows"/>).
    /// Whichever tab you are looking at, the behaviour this mod adds is always in the
    /// same place, below everything layer 1 has to say.
    /// </summary>
    internal sealed class EarthquakePanelTabs
    {
        private const float TabHeight = 26f;
        private const float TabGap = 4f;

        private readonly UIPanel _root;
        private readonly float _tabY;
        private readonly float _pageTop;
        private readonly List<UIButton> _buttons = new List<UIButton>();
        private readonly List<UIPanel> _pages = new List<UIPanel>();

        private float _maxPageHeight;
        private int _active;

        /// <summary>
        /// Places the row of tabs at <paramref name="y"/> and advances
        /// <paramref name="y"/> to the top of the page area.
        /// </summary>
        internal EarthquakePanelTabs(UIPanel root, ref float y)
        {
            _root = root;
            _tabY = y;
            y += TabHeight + 6f;
            _pageTop = y;
        }

        /// <summary>
        /// Adds one page. The <c>UIPanel</c> returned becomes the parent of that page's
        /// rows. **Within a page, y counts from 0** (a page's origin is its own top-left
        /// corner). The first page added is the one shown initially.
        /// </summary>
        internal UIPanel AddPage(string suffix, string caption)
        {
            int index = _pages.Count;

            var page = (UIPanel)_root.AddUIComponent(typeof(UIPanel));
            page.name = FreeSlotFinder.SelfPrefix + "EarthquakePage" + suffix;
            page.width = EarthquakeRows.PanelWidth;
            page.height = 0f;
            page.relativePosition = new Vector3(0f, _pageTop);
            page.isVisible = index == _active;

            var button = (UIButton)_root.AddUIComponent(typeof(UIButton));
            button.name = FreeSlotFinder.SelfPrefix + "EarthquakeTab" + suffix;
            button.text = caption;
            button.tooltip = caption;
            button.height = TabHeight;
            // The widths are divided evenly once every page has been added (Finish).
            // This is a placeholder.
            button.width = EarthquakeRows.RowWidth;
            button.relativePosition = new Vector3(EarthquakeRows.RowLeft, _tabY);
            // Japanese captions run wider than English ones. At the default scale even
            // two tabs can overflow.
            button.textScale = 0.85f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Center;
            button.eventClick += (c, e) => Select(index);

            _pages.Add(page);
            _buttons.Add(button);
            ApplySprites(index);
            return page;
        }

        /// <summary>Once a page's rows are stacked up, hand its height over.</summary>
        internal void FinishPage(UIPanel page, float height)
        {
            if (page == null) return;
            page.height = height;
            if (height > _maxPageHeight) _maxPageHeight = height;
        }

        /// <summary>
        /// Divides the tab buttons' width evenly and advances <paramref name="y"/> to the
        /// bottom of the tab area.
        ///
        /// **The height matches the tallest page.** If the panel's height and position
        /// changed every time you switched tabs, the row you were reading would jump
        /// around on screen.
        /// </summary>
        internal void Finish(ref float y)
        {
            int n = _buttons.Count;
            if (n > 0)
            {
                float width = (EarthquakeRows.RowWidth - TabGap * (n - 1)) / n;
                for (int i = 0; i < n; i++)
                {
                    _buttons[i].width = width;
                    _buttons[i].relativePosition =
                        new Vector3(EarthquakeRows.RowLeft + i * (width + TabGap), _tabY);
                }
            }
            y = _pageTop + _maxPageHeight;
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _active = index;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].isVisible = i == index;
                ApplySprites(i);
            }
        }

        /// <summary>
        /// Makes the selected tab look pressed in.
        ///
        /// The sprites used are limited to the three this mod already uses
        /// (<c>ButtonMenu</c> / <c>ButtonMenuHovered</c> / <c>ButtonMenuPressed</c>). Add
        /// a sprite name whose existence has not been verified and, if the name turns out
        /// to be wrong, **the button goes transparent and cannot be clicked** — a
        /// breakage you will not spot until you look at the screen.
        /// </summary>
        private void ApplySprites(int index)
        {
            var button = _buttons[index];
            bool active = index == _active;
            button.normalBgSprite = active ? "ButtonMenuPressed" : "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.pressedBgSprite = "ButtonMenuPressed";
        }
    }
}
