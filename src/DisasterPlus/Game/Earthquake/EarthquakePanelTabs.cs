using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地震パネルのタブ。**main スレッド専用。**
    ///
    /// ── なぜタブなのか（縦の予算はもう無かった） ────────────────────
    ///
    /// UIView の座標系は高さ 1080 に正規化されている。第 1 層を作り終えた時点で
    /// パネルは既に**約 1050** まで積み上がっており、震度分布オーバーレイの 3 行を
    /// 足すために幅を 520 → 640 へ広げて折り返し行の予約高さを削るという、
    /// 一度しか使えない手を既に使っていた。<see cref="EarthquakePanel.ClampToView"/> は
    /// 下端がはみ出したら警告を 1 回出すだけで、**はみ出しそのものは防げない**。
    /// 第 2 層（Task 9〜11）は 3 つの節を足すので、このまま行を追記すれば
    /// 説明文が確実に画面外へ出る —— 書いたのに読めないのは、書いていないより悪い。
    ///
    /// ── 3 案のうちタブを選んだ理由 ────────────────────────────
    ///
    /// | 案 | 採らなかった理由 |
    /// |---|---|
    /// | スクロール領域 | <c>UIScrollablePanel</c> は IL 実測で <c>UIComponent</c> 直系であり
    ///   <c>UIPanel</c> ではない。行ヘルパーの引数型（<c>UIPanel</c>）と
    ///   <c>WaveformView.Build</c> / <see cref="EarthquakeOverlayRows"/> の受け口を
    ///   まとめて作り替えることになるうえ、スクロールバーはサムとトラックの
    ///   スプライトを自前で組む必要があり、**構造の変更だけで済まない**。 |
    /// | 折りたたみ | 開閉のたびに全行の y を組み直す＝実行時レイアウトが増える。
    ///   「どこに何があるか」が状態依存になり、説明文が畳まれたまま気付かれない。 |
    /// | **タブ（採用）** | 使う型は <c>UIPanel</c> と <c>UIButton</c> だけで、
    ///   どちらも既に使っている。行ヘルパーの引数型も変えなくてよい
    ///   （ページ自体が <c>UIPanel</c> なので）。y の計算は構築時に 1 回だけで、
    ///   実行時の並べ替えは無い。 |
    ///
    /// ── 第 2 層はタブの中に入れない ───────────────────────────
    ///
    /// 計画は「第 2 層は第 1 層の**下**に構築し、実行時の並べ替えはしない」と
    /// 定めている。タブに分けると「下」が成立しなくなるので、**第 2 層の節は
    /// タブ領域の外側・その下**に置く（<see cref="EarthquakeLayer2Rows"/>）。
    /// どのタブを見ていても、本 MOD が足した挙動は常に同じ場所に、
    /// 第 1 層の全内容より下に見えている。
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
        /// タブ行を <paramref name="y"/> の位置に置き、<paramref name="y"/> を
        /// ページ領域の先頭へ進める。
        /// </summary>
        internal EarthquakePanelTabs(UIPanel root, ref float y)
        {
            _root = root;
            _tabY = y;
            y += TabHeight + 6f;
            _pageTop = y;
        }

        /// <summary>
        /// ページを 1 枚足す。返る <c>UIPanel</c> がそのページの行の親になる。
        /// **ページ内の y は 0 から数える**（ページの原点はページ自身の左上）。
        /// 最初に足したページが初期表示になる。
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
            // 幅はページを全部足してから均等割りする（Finish）。ここでは仮の値。
            button.width = EarthquakeRows.RowWidth;
            button.relativePosition = new Vector3(EarthquakeRows.RowLeft, _tabY);
            // 日本語の見出しは英語より横に長い。既定倍率だと 2 タブでも溢れうる。
            button.textScale = 0.85f;
            button.textHorizontalAlignment = UIHorizontalAlignment.Center;
            button.eventClick += (c, e) => Select(index);

            _pages.Add(page);
            _buttons.Add(button);
            ApplySprites(index);
            return page;
        }

        /// <summary>ページの行を積み終わったら、その高さを渡す。</summary>
        internal void FinishPage(UIPanel page, float height)
        {
            if (page == null) return;
            page.height = height;
            if (height > _maxPageHeight) _maxPageHeight = height;
        }

        /// <summary>
        /// タブボタンの幅を均等割りし、<paramref name="y"/> をタブ領域の下端へ進める。
        ///
        /// **高さはいちばん高いページに合わせる。** タブを切り替えるたびに
        /// パネルの高さと位置が変わると、読んでいる行が画面上で飛ぶ。
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
        /// 選択中のタブを押し込んだ見た目にする。
        ///
        /// 使うスプライトは既にこの MOD が使っている 3 種だけに限る
        /// （<c>ButtonMenu</c> / <c>ButtonMenuHovered</c> / <c>ButtonMenuPressed</c>）。
        /// 存在を確かめていないスプライト名を増やすと、名前が違ったときに
        /// **ボタンが透明になって押せなくなる**という、画面を見るまで分からない
        /// 壊れ方をする。
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
