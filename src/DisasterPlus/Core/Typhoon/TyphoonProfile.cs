namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 台風の中心からの距離 → 風速相当（[0, 1] の係数）と、その表示用の段階・バー。
    ///
    /// **この型は m/s を返さない。** ④の「風速相当」はゲームの倒壊確率に掛ける係数で
    /// あって、実在の風速ではない（設計書 §7.3）。②が気象庁震度階級を名乗らなかったのと
    /// 同じ理由で、単位を名乗ると実在の意味があると誤解させる。返すのは [0,1] の係数と
    /// 0〜<see cref="Steps"/> の段階だけである。**m/s を返すメソッドを足さないこと。**
    ///
    /// 半径だけはバニラ由来の式に合わせてある。<see cref="StormRadiusOf"/> は
    /// ThunderStormAI の落雷散布半径・ハザード円盤と**同じ**
    /// R = m_radius × (0.25 + intensity × 0.0075)（IL 事実文書 §A-1 / §A-2）で、
    /// これによりバニラが塗る円盤と④が表示する暴風域の大きさが一致する。
    /// 風の形（眼・壁雲・外側の減衰）は④が発明したものである。
    ///
    /// <c>m_radius</c> はプレハブ値で **DLL に実数値が無い**（§A-0、PARTIAL）。
    /// 読めなければ全部 0 を返し、呼び出し側は何もしない（設計書 §6）。
    /// **0 を「風が無い」の意味にも使うが、半径 0 は「読めなかった」である** ——
    /// 表示側はその 2 つを混ぜないこと（半径が読めたかどうかは
    /// <c>TyphoonPrefabFacts</c> 側で名乗る）。
    /// </summary>
    public static class TyphoonProfile
    {
        /// <summary>眼の半径が暴風域半径に占める割合。</summary>
        public const float EyeFraction = 0.12f;

        /// <summary>壁雲（最も強い環）の半径が暴風域半径に占める割合。</summary>
        public const float WallFraction = 0.34f;

        /// <summary>強風域半径 ÷ 暴風域半径。</summary>
        public const float GaleFactor = 2.2f;

        /// <summary>
        /// 眼の**縁**での風速相当。中心はちょうど 0（実在の台風でも眼の中心は静穏）で、
        /// そこから縁までこの値へ線形に立ち上がり、縁から壁雲までで 1 に達する。
        ///
        /// 計画 §1.2 の擬似コードは眼の枝を <c>EyeWind + (1-EyeWind)·(d/eye)</c> と
        /// 書いているが、それは眼の縁でちょうど 1 に達してしまい、続く壁雲の枝
        /// （同じく「wall で 1f」）と両立しない —— 縁の内側で 1.0、外側で 0.15 という
        /// 不連続になる。壁雲が最強の環であるという構造（テストが固定している）を
        /// 保つために、眼の枝は 0 → EyeWind の立ち上がりにしてある。
        /// </summary>
        public const float EyeWind = 0.15f;

        public const int Steps = 10;
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>
        /// 暴風域半径（m）。**バニラの散布半径・ハザード円盤と同じ式**
        /// R = prefabRadius × (0.25 + intensity × 0.0075)（§A-1 IL_0177–0191 / §A-2）。
        ///
        /// <paramref name="prefabRadius"/> が 0 以下か NaN なら **0**（＝プレハブが
        /// 読めていない）。推測した半径を返さない。
        /// </summary>
        public static float StormRadiusOf(byte intensity, float prefabRadius)
        {
            // NaN も一緒に弾く（NaN > 0 は false）。
            if (!(prefabRadius > 0f)) return 0f;
            return prefabRadius * (0.25f + intensity * 0.0075f);
        }

        /// <summary>強風域半径（m）。暴風域の <see cref="GaleFactor"/> 倍。</summary>
        public static float GaleRadiusOf(byte intensity, float prefabRadius)
        {
            return StormRadiusOf(intensity, prefabRadius) * GaleFactor;
        }

        /// <summary>
        /// 中心から <paramref name="distance"/> m の地点の風速相当（[0, 1]）。
        ///
        /// 眼の中心 0 → 眼の縁 <see cref="EyeWind"/> → 壁雲でちょうど 1 →
        /// 強風域の縁でちょうど 0。境界の 2 点はテストが固定している。
        /// 強風域の外・負の距離・NaN は 0。
        /// </summary>
        public static float WindAt(float distance, byte intensity, float prefabRadius)
        {
            float storm = StormRadiusOf(intensity, prefabRadius);
            if (storm <= 0f) return 0f;

            float gale = storm * GaleFactor;

            // NaN は !(d >= 0) 側で落ちる。
            if (!(distance >= 0f) || distance >= gale) return 0f;

            float eye = storm * EyeFraction;
            float wall = storm * WallFraction;

            if (distance <= eye)
            {
                // eye は storm > 0 かつ EyeFraction > 0 なので必ず正。
                return EyeWind * (distance / eye);
            }

            if (distance <= wall)
            {
                return EyeWind + (1f - EyeWind) * (distance - eye) / (wall - eye);
            }

            return 1f - (distance - wall) / (gale - wall);
        }

        /// <summary>
        /// 風速相当を 0〜<see cref="Steps"/> の段階へ。単調増加。1 でちょうど Steps。
        /// ②の <c>SeismicScale.StepOf</c> と同じ形（NaN は 0、範囲外はクランプ）。
        /// </summary>
        public static int StepOf(float wind)
        {
            if (float.IsNaN(wind) || wind <= 0f) return 0;
            if (wind >= 1f) return Steps;

            int step = (int)(wind * Steps);
            if (step < 0) step = 0;
            if (step > Steps) step = Steps;
            return step;
        }

        /// <summary>
        /// 長さ <see cref="Steps"/> の ASCII バー。埋まった数は <see cref="StepOf"/> と一致する。
        /// ASCII 固定なのは①の <c>HazardLevel</c>・②の <c>SeismicScale</c> と同じ理由で、
        /// CS の UI フォントに罫線素片がある保証が無いため（無ければ豆腐になる）。
        /// </summary>
        public static string BarOf(float wind)
        {
            int filled = StepOf(wind);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }
    }
}
