using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **バニラの強度スライダーを、④⑤のタイルからも使う。** main スレッド専用。
    ///
    /// ── バニラの流れ（IL 実測） ──────────────────────────────
    ///
    /// <c>DisastersPanel.OnButtonClicked</c> は、押されたタイルの
    /// <c>objectUserData</c> が <c>DisasterInfo</c> のときだけ動き、
    /// <c>SetTool&lt;DisasterTool&gt;()</c> → <c>m_prefab</c> 代入 →
    /// <c>SupportIntensity()</c> なら <c>ShowDisastersOptionPanel()</c> を呼ぶ。
    /// その <c>ShowDisastersOptionPanel</c> の実体は
    /// <c>GetOptionPanel("DisastersOptionPanel").ShowPanel()</c> だけである。
    ///
    /// <c>DisastersOptionPanel</c>（<c>OptionPanelBase</c> 派生、public）は
    ///   - <c>Awake</c> で <c>Find&lt;UISlider&gt;("Slider")</c> と
    ///     <c>Find&lt;UILabel&gt;("LabelIntensity")</c> を掴み、
    ///   - <c>OnSliderValueChanged</c> でラベルに <c>value / 10</c> を <c>"F1"</c> で出し、
    ///     <c>DisasterTool.m_intensity = (int)value</c> を書く。
    ///
    /// つまり<b>スライダーの生値がそのまま byte 強度で、表示だけが /10</b>
    /// （<see cref="IntensityUnlock"/> が上限を 255 まで開けているのと同じ事実）。
    ///
    /// ── ④⑤がこれをそのまま借りられる理由 ──────────────────────
    ///
    /// <c>ShowPanel()</c> / <c>HidePanel()</c> は <c>OptionPanelBase</c> の
    /// **public** メソッドで、リフレクションは要らない（IL 実測）。
    /// スライダー本体も <c>UICustomControl.Find&lt;T&gt;(name)</c> で取れる
    /// （<see cref="IntensityUnlock"/> が既に同じ経路で上限を書き換えている）。
    ///
    /// ★ この型は <c>DisasterTool</c> を**現在のツールにしない**。④⑤は自前の
    ///   配置ツールを使うので、スライダーが書く <c>DisasterTool.m_intensity</c> は
    ///   その場では何も動かさない —— **こちらは値を読むだけ**である。
    ///   バニラの災害を次に選んだときにスライダーの値が引き継がれるのは、
    ///   バニラのタイルどうしで切り替えたときと同じ挙動である。
    ///
    /// ── 「意味」はタイルごとに違う。だから構えるたびに初期値を入れる ──────
    ///
    /// スライダーは 1 本しか無いのに、④では**台風の強度**、⑤では
    /// **設定サイズに対する倍率**（<c>VolcanoSizeScale</c>）を意味する。
    /// 構えた瞬間に <see cref="Seed"/> でその機能の既定値を入れることで、
    /// **画面に出ている数字が、今構えている機能の数字であること**を保つ。
    /// 構え直すと既定へ戻るが、それは「別の意味の数字が残っている」より良い。
    ///
    /// ── 状態を持たない ────────────────────────────────
    ///
    /// **Unity オブジェクトを static に持たない。** 呼ばれるのは「構えたとき」と
    /// 「地図をクリックしたとき」だけなので、そのつど探して構わない。
    /// 持たないので、都市をまたいで破棄済みの参照が残る経路が存在しない。
    /// </summary>
    public static class IntensitySlider
    {
        /// <summary>スライダーの生値の下限。</summary>
        public const int MinRaw = 0;

        /// <summary>生値の上限（<c>DisasterData.m_intensity</c> は byte）。</summary>
        public const int MaxRaw = 255;

        /// <summary>この環境でスライダーに手が届くか（値は変えない）。</summary>
        public static bool Available { get { return FindSlider() != null; } }

        /// <summary>
        /// 強度スライダーを出す。バニラのタイルを押したときと同じ場所に同じものが出る。
        /// 出せない環境（<c>DisastersOptionPanel</c> が無い等）では黙って何もしない ——
        /// **④⑤はスライダーが無くても既定値で動く。**
        /// </summary>
        public static void Show()
        {
            var panel = FindPanel();
            if (panel == null) { Log.Diag("intensitySlider", "no DisastersOptionPanel to show"); return; }
            try { panel.ShowPanel(); }
            catch (System.Exception e) { Log.Warn("intensity slider show failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// 強度スライダーを畳む。**④⑤の配置ツールを降りるときだけ呼ぶこと** ——
        /// バニラの災害を構えている最中に呼ぶと、あちらのスライダーを横から消す。
        /// </summary>
        public static void Hide()
        {
            var panel = FindPanel();
            if (panel == null) { Log.Diag("intensitySlider", "no DisastersOptionPanel to hide"); return; }
            try { panel.HidePanel(); }
            catch (System.Exception e) { Log.Warn("intensity slider hide failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// スライダーに初期値を入れる（クラス doc の「意味はタイルごとに違う」）。
        /// 上限は他 MOD が動かしうるので、<c>UISlider</c> 自身のクランプに任せる。
        /// </summary>
        public static void Seed(int raw)
        {
            var slider = FindSlider();
            if (slider == null)
            {
                // ★ 黙って落とさない。初回の実機テストでは
                //   「警告が 1 行も出ていないのでスライダーは読めているはず」と
                //   推論するしか無かった。**読めなかったことも 1 行残す。**
                Log.Diag("intensitySlider", "no slider to seed; the tile will fall back to the options value");
                return;
            }
            try { slider.value = Clamp(raw); }
            catch (System.Exception e) { Log.Warn("intensity slider seed failed: " + e.GetType().Name); }
        }

        /// <summary>
        /// 今の生値。読めなければ <paramref name="fallback"/> をそのまま返す。
        ///
        /// ★ **読めなかったことを「0」で表さない。** 0 は「いちばん弱い」という
        ///   有効な値であり、「読めなかった」とは違う。
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
        /// <c>Object.FindObjectOfType</c> は Unity 5.6 では非アクティブな GameObject を
        /// 返さないので使わない（<see cref="SceneObjects"/> のクラス doc）。
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
