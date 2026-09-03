using System;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>設定画面のスライダーを 1 本置く。</b>**main スレッド（OnSettingsUI）専用。**
    ///
    /// ── 実機報告（2026-09-02、スクリーンショット付き）──────────────────
    ///
    /// &gt; Option パネルの UI で一部重複して操作がしづらいところがあります
    /// &gt; （…修正後）治ってなかったです。
    ///
    /// 送られてきた画像では、スライダーのラベルが 3 行に折り返したまま
    /// <b>次のチェックボックスに重なって</b>いた。「風害…」の 3 行目と
    /// 「Southern hemisphere」が同じ場所に描かれている、という壊れ方である。
    ///
    /// ── ★★ なぜ重なるのか（IL で確定）─────────────────────────────
    ///
    /// <c>UIHelper.AddSlider</c> の中身は
    ///
    /// <code>
    /// UIPanel row = m_Root.AttachUIComponent(GetAsGameObject(kSliderTemplate));
    /// row.Find("Label").text = text;
    /// UISlider slider = row.Find("Slider");
    /// ...
    /// return slider;                      // ★ 返るのは行ではなくスライダー
    /// </code>
    ///
    /// ★★ <b>行の高さはテンプレートの固定値のままである。</b>ラベルだけが
    ///   折り返して伸びるので、はみ出した 2 行目以降が<b>次の行の領域へ食い込む</b>。
    ///   親のオートレイアウトは行の高さしか見ないので、重なりは誰にも直されない。
    ///
    /// ── だから 2 段構えにする ────────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>ラベルを短くする。</b>折り返さなければ問題は起きない。
    ///   長い説明は<see cref="UIComponent.tooltip"/> へ移す ——
    ///   説明を捨てるのではなく、<b>読みたい人だけが読む場所</b>へ置く。</item>
    /// <item><b>それでも折り返したら行を伸ばす。</b>翻訳は英語より長くなることが
    ///   あり、短いラベルでも他言語で 2 行になりうる。ここが最後の砦である。</item>
    /// </list>
    ///
    /// ★ 失敗しても黙って諦める。**設定画面が開かなくなるより、少し不格好な方がまし**
    ///   —— テンプレートの構造が将来変わっても、MOD の設定画面自体は開き続ける。
    /// </summary>
    public static class OptionsSlider
    {
        /// <summary>行の下に足す余白（px）。</summary>
        private const float BottomPadding = 8f;

        /// <summary>ラベルとスライダーのあいだ（px）。</summary>
        private const float LabelGap = 4f;

        private static bool _warned;

        /// <summary>
        /// ラベルとツールチップを持つスライダーを足す。
        /// </summary>
        /// <param name="tooltip">
        /// 長い説明。<c>null</c> なら付けない。**ラベルに入り切らない話はここへ。**
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
        /// ツールチップを付け、ラベルが折り返していれば行を伸ばす。
        /// </summary>
        private static void Fit(UISlider slider, string tooltip)
        {
            try
            {
                var row = slider.parent as UIPanel;
                if (row == null) return;

                // ★ ツールチップは行ぜんぶに付ける。ラベルだけに付けると、
                //   スライダーの上をなぞったときに出ない。
                if (!string.IsNullOrEmpty(tooltip)) row.tooltip = tooltip;

                // "Label" / "Slider" という名前は AddSlider 自身が使っているので、
                // ここで見つからないなら AddSlider も動いていない（クラス doc の IL）。
                var label = row.Find<UILabel>("Label");
                if (label == null) return;

                float labelBottom = label.relativePosition.y + label.height;
                float needed = labelBottom + LabelGap + slider.height + BottomPadding;

                // 1 行で収まっているならテンプレートの高さで足りている。触らない。
                if (row.height >= needed) return;

                slider.relativePosition = new Vector3(slider.relativePosition.x,
                                                      labelBottom + LabelGap);
                row.height = needed;
            }
            catch (Exception e)
            {
                // 1 度だけ言う。スライダーの本数だけログが出ても読めない。
                if (_warned) return;
                _warned = true;
                Log.Warn("could not fit an options slider row; a long label may overlap "
                         + "the control under it: " + e.GetType().Name);
            }
        }
    }
}
