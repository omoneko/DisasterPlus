namespace DisasterPlus.Core.Earthquake
{
    /// <summary>揺れの大きさの粗い区分。実在の震度階級の名前は使わない。</summary>
    public enum SeismicBand
    {
        None,
        Weak,
        Moderate,
        Strong,
        Severe,
    }

    /// <summary>
    /// 局所係数 s（0-1）を表示用の段階・バー・区分名にする。
    ///
    /// **気象庁震度階級を名乗らない**（設計書 §3.1）。s は加速度でも計測震度でもなく、
    /// ゲームの倒壊係数である。実在の尺度の名前を借りると、実在の意味があると
    /// 誤解させる。表示は「強度」「揺れの大きさ」に留め、必ず 0.0-1.0 の生値と併記する。
    ///
    /// 段階が 10 でバンドが 5 なのは意図的。バーの分解能は 10 段階で欲しいが、
    /// 10 個の段階名をローカライズすると、訳語の差がそのまま「意味のある尺度」に
    /// 見えてしまう。名前を付けるのは 5 区分までにする。
    ///
    /// バーは ASCII 固定（①の HazardLevel と同じ理由。CS の UI フォントに
    /// 罫線素片がある保証は無く、無ければ豆腐になる）。
    /// </summary>
    public static class SeismicScale
    {
        public const int Steps = 10;
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>s を 0-Steps に写す。単調増加。s = 1 でちょうど Steps。</summary>
        public static int StepOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return 0;
            if (s >= 1f) return Steps;

            int step = (int)(s * Steps);
            if (step < 0) step = 0;
            if (step > Steps) step = Steps;
            return step;
        }

        /// <summary>長さ Steps のバー。埋まった数は StepOf と一致する。</summary>
        public static string BarOf(float s)
        {
            int filled = StepOf(s);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }

        /// <summary>区分名。境界は下側を含む（0.25 は Moderate）。</summary>
        public static SeismicBand BandOf(float s)
        {
            if (float.IsNaN(s) || s <= 0f) return SeismicBand.None;
            if (s < 0.25f) return SeismicBand.Weak;
            if (s < 0.5f) return SeismicBand.Moderate;
            if (s < 0.75f) return SeismicBand.Strong;
            return SeismicBand.Severe;
        }
    }
}
