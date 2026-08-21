namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="TyphoonWind"/> のうち<b>「吹き飛ばしだけ」</b>を短い間隔で撃つ部分。
    /// <b>sim スレッド専用。</b>
    ///
    /// ── なぜ本体と分けてあるのか ────────────────────────────────
    ///
    /// <b>2 つは別の機能である。</b> 本体（<c>Apply</c> → <c>Sweep</c>）は
    /// <b>建物を倒す走査</b>で、設定の「風害」で入り切りする。こちらは
    /// <c>DisasterHelpers.AddWind</c> だけ ＝ <b>市民と車を押す演出</b>で、
    /// 設定の「地上の暴風雨を見せる」（<c>ModSettings.TyphoonStormFx</c>）で
    /// 入り切りする。**風害を切っていても風は吹く。**
    ///
    /// 累積（<see cref="_minutesSinceGale"/>）も本体と別に持つ ——
    /// 同じ累積を使うと、走査が走ったフレームだけ吹き飛ばしが飛ぶ。
    ///
    /// （ファイルを分けているのは 800 行の上限のためでもある。）
    /// </summary>
    public static partial class TyphoonWind
    {
        /// <summary>
        /// <b>吹き飛ばしだけ</b>の間隔（フレーム相当のゲーム内時間）。
        ///
        /// ★★ 持ち主の指摘「暴風雨を再現してほしい」への対応の 1 つ（2026-08-22）。
        ///   以前は <see cref="PushWind"/> が<b>走査の中でしか呼ばれず</b>、
        ///   市民と車が押されるのは 256 フレームに 1 回だけだった ——
        ///   ゲーム内で 5〜6 分に 1 度である。**吹き荒れているようには見えない。**
        ///
        ///   <see cref="Gale"/> は 64 フレームごとに吹き飛ばしだけを行う。
        ///   費用は下がっている: 走査の中の押しは<b>強風域</b>（暴風域の 2.2 倍）で
        ///   撃っていたが、こちらは<b>暴風域</b>（面積で 1/4.84）なので、
        ///   4 倍の頻度でも合計は 0.83 倍にしかならない。
        ///   実在の台風でも最も強い風は眼の壁雲の周りにある。
        /// </summary>
        private const int GalePushIntervalFrames = 64;

        /// <summary>前回の吹き飛ばしからの経過（ゲーム内分）。<see cref="Gale"/> が使う。</summary>
        private static float _minutesSinceGale;

        /// <summary>吹き飛ばしを撃った回数（診断用）。</summary>
        private static int _galePushes;

        /// <summary>
        /// <b>吹き飛ばしだけ</b>を <see cref="GalePushIntervalFrames"/> フレームごとに撃つ。
        /// <b>sim スレッド専用</b>で、台風が動いている間だけ呼ぶ。
        ///
        /// ★ **建物にも道路にも樹木にも触れない。** <c>DisasterHelpers.AddWind</c> は
        ///   <c>AddWindCitizens</c> ＋ <c>AddWindVehicles</c> の 2 行だけである（§B-1）。
        ///   だから風害の設定（<c>TyphoonWindDamage</c>）とは別に、
        ///   暴風雨の演出の設定（<c>TyphoonStormFx</c>）で入り切りする。
        ///
        /// ★ <see cref="Apply"/> とは**別の累積**（<see cref="_minutesSinceGale"/>）を持つ。
        ///   同じ累積を使うと、走査が走ったフレームだけ吹き飛ばしが飛ぶ。
        ///
        /// 例外は 1 度だけ名乗って以後は黙る（毎 tick の経路である）。
        /// </summary>
        public static void Gale(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                GaleStep(deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon gale push failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGale",
                             "typhoon gale push failed: " + e.GetType().Name);
                }
            }
        }

        private static void GaleStep(float deltaMinutes)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f
                ? GalePushIntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceGale += deltaMinutes;
            if (interval > 0f && _minutesSinceGale > interval) _minutesSinceGale = interval;

            if (!TyphoonController.Active) return;
            if (framesPerMinute <= 0f) return;
            if (_minutesSinceGale < interval) return;

            // 余りを繰り越さない（Step と同じ理由）。
            _minutesSinceGale = 0f;

            // ★ 暴風域で撃つ（強風域ではない。GalePushIntervalFrames の doc）。
            float range = TyphoonController.StormRadius;
            if (!(range > 0f)) return;

            var centre = TyphoonController.Centre;
            if (float.IsNaN(centre.X) || float.IsNaN(centre.Z)) return;

            PushWind(centre, GroupOf(TyphoonController.DisasterId), range);
            _galePushes++;
        }

        /// <summary>吹き飛ばしを撃った回数（診断用）。</summary>
        public static int GalePushes { get { return _galePushes; } }
    }
}
