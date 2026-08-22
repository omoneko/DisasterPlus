using ColossalFramework.UI;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤を構えているあいだだけ、**バニラの強度スライダーの数字を「山の大きさ」に
    /// 読み替えて表示する。** main スレッド専用、毎フレーム。
    ///
    /// ── なぜ要るのか（2026-08-22、所有者の指摘）─────────────────────
    ///
    /// > 火山の大きさをスケールで調整できるようにしてほしい。
    ///
    /// **スライダーは既に大きさのつまみで、既に効いている。** 実機ログがそれを示している:
    ///
    /// <code>
    /// volcano placed at (88,-398): Strato r=829 m h=415 m
    /// </code>
    ///
    /// 既定は r = 1200 / h = 600 なので、これは 0.69 倍 —— スライダーはちょうど
    /// 仕事をしていた（<see cref="VolcanoSizeScale"/> は生値 55 を 1.0 倍に写す）。
    /// **足りなかったのは 2 つ目のつまみではなく、画面の側である:**
    ///
    ///   - ラベルには <c>5.5</c> とだけ出ていた。これは<b>バニラの強度の表示規則</b>
    ///     （生値 ÷ 10）であって、⑤にとっては何の意味も無い数である
    ///   - **押す前に何メートルの山になるのかが、どこにも出ていなかった**
    ///     （確認の窓は 2026-08-21 に撤去してある。戻さない）
    ///
    /// そこでこの型は、構えているあいだだけラベルを
    ///
    /// <code>
    /// 1.40x  r1680  h840 m
    /// </code>
    ///
    /// に差し替える。**倍率と、実際に届く実寸（形態ごとの帯でクランプ済み）**である。
    /// 言葉はスライダーのツールチップ 1 行だけに置く（所有者は文章を減らせと繰り返している）。
    ///
    /// ── 高さは「地形の天井」を引く前の値である ────────────────────────
    ///
    /// <c>VolcanoShape.HeightFor</c> は起点の地形高さから天井（§C-10）を引くが、
    /// **押す前はどの地点かが決まっていない**ので引きようがない。ここに出るのは
    /// 帯でクランプしただけの値で、標高の高い場所ではこれより低くなりうる。
    /// **その差は火山タブと診断ダンプが実測で名乗る**（そちらは押した後の話である）。
    /// 出さないよりよほど良い —— 出さなかったのが今回の指摘そのものである。
    ///
    /// ── 元に戻す（★ ここを飛ばすとバニラの災害の表示を壊す）──────────────
    ///
    /// <c>DisastersOptionPanel</c> はスライダー 1 本を全ての災害で共有していて、
    /// ラベルを書き換えるのは <c>OnSliderValueChanged</c> **だけ**である（IL 実測）。
    /// つまり⑤を降りたあとバニラの災害を選んでも、**プレイヤーがスライダーを
    /// 動かすまでこちらの文字が残る。** 降りた瞬間にバニラの表示規則
    /// （生値 ÷ 10 を <c>"F1"</c>）へ書き戻す。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// <c>ToolsModifierControl.toolController</c> 1 回と <c>UISlider.value</c> 1 回。
    /// **文字列を組むのは生値が変わったフレームだけ**（<see cref="_lastRaw"/>）なので、
    /// 動かしていないあいだのヒープ確保は 0 バイトである。
    /// パネルの探索（<c>Resources.FindObjectsOfTypeAll</c>）は毎フレームやらない ——
    /// 参照 1 個ずつで持ち、**配列にはしない**（③が出荷した fake-null の罠）。
    /// </summary>
    public static class VolcanoSizeReadout
    {
        /// <summary>「まだ 1 度も読んでいない」を表す生値。0〜255 に無い値を選ぶ。</summary>
        private const int NoRaw = -1;

        // ★ 配列にしない。参照 1 個ずつ持ち、毎回 Unity の == null で見る
        //   （破棄済みなら fake-null になるので、そのフレームで引き直す）。
        private static UISlider _slider;
        private static UILabel _label;

        private static bool _applied;
        private static int _lastRaw = NoRaw;
        private static bool _lookupFailedLogged;

        /// <summary>直近に出した文字（診断用。**英語ではなく数字だけ**）。</summary>
        public static string LastText { get; private set; }

        /// <summary>**main スレッド、毎フレーム。**</summary>
        public static void Update()
        {
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                // 毎フレームの経路。Warn も Error も鳴らさない（キー単位で 1 行）。
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

            // ★ 言葉はここ 1 行だけ。ラベルは数字だけにする。
            _slider.tooltip = Strings.VolcanoSizeSliderTooltip;
            _label.tooltip = Strings.VolcanoSizeSliderTooltip;
        }

        /// <summary>
        /// 「倍率と実寸」の 1 行。**形態ごとの帯でクランプした後の値**を出す ——
        /// クランプ前を出すと、上限に張り付いてから先はスライダーを動かしても
        /// 表示だけが伸びる（＝嘘の readout になる）。
        /// </summary>
        private static string Compose(int raw)
        {
            float scale = VolcanoSizeScale.ScaleFor(raw);
            VolcanoForm form = VolcanoShape.FormOf(ModSettings.VolcanoShapeSetting.value);

            float r = VolcanoShape.RadiusFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form), scale));

            // ★ 地形の天井は引けない（地点が決まっていない）。クラス doc の注記のとおり、
            //   0 m 起点＝帯のクランプだけを掛けた値である。
            float h = VolcanoShape.HeightFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form), scale), 0f);

            // ★★ **バニラと同じ数字を先頭に出す**（生値 ÷ 10 を "F1"）。
            //    以前は "1.00x" という**この MOD だけの単位**を出しており、
            //    他の災害のスライダーと見比べられなかった
            //    （所有者の指摘「バニラ同様 1.0-10.0(25.5) にしてほしい」）。
            //    実寸はそのあとに添える —— 形態ごとの帯でクランプした後の値である。
            return (raw / 10f).ToString("F1") + "  r" + r.ToString("F0")
                   + "  h" + h.ToString("F0") + " m";
        }

        /// <summary>
        /// バニラの表示規則（生値 ÷ 10 を <c>"F1"</c>）へ書き戻す。冪等。
        /// **⑤を降りたら必ず通ること**（クラス doc）。
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
        /// レベルアンロードと、機能を切ったとき。**Unity オブジェクトには触らない**
        /// （もう破棄されている可能性がある）。参照を手放すだけ。
        /// </summary>
        public static void Reset()
        {
            _slider = null;
            _label = null;
            _applied = false;
            _lastRaw = NoRaw;
            LastText = null;
            // _lookupFailedLogged は戻さない（ゲームのビルドに対する事実である）。
        }

        /// <summary>
        /// スライダーとラベルを引く。**引けているあいだは探し直さない。**
        /// どちらか破棄されていれば（fake-null）その場で引き直す。
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

            // ★ 名前は <c>DisastersOptionPanel.Awake</c> が使っているものそのもの
            //   （IL 実測: Find<UISlider>("Slider") / Find<UILabel>("LabelIntensity")）。
            //   m_slider / m_label のフィールド自体は private なので触れない。
            _slider = panel.Find<UISlider>("Slider");
            _label = panel.Find<UILabel>("LabelIntensity");

            return _slider != null && _label != null;
        }
    }
}
