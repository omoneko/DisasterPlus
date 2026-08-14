namespace DisasterPlus.Core.Forecast
{
    /// <summary>
    /// ハザード値（byte 0-255）を表示段階とバー文字列にする。
    /// バニラは色でしか見せないので、段階と数値にするのが本機能の付加価値。
    /// </summary>
    public static class HazardLevel
    {
        public const int Steps = 10;

        /// <summary>
        /// バーは ASCII のみで組む。CS の UI フォントに罫線素片（▓ ░）がある保証は無く、
        /// 無ければ豆腐になる。見た目より確実に出ることを優先する。
        /// </summary>
        public const char FilledChar = '#';
        public const char EmptyChar = '-';

        /// <summary>0-255 を 0-Steps に写す。単調増加。</summary>
        public static int StepOf(byte hazard)
        {
            // 255 でちょうど Steps になるよう切り上げ側に寄せず、整数除算で素直に割る。
            // 255 * Steps / 255 == Steps なので端は両方とも正確に出る。
            return hazard * Steps / 255;
        }

        /// <summary>長さ Steps のバー。埋まった数は StepOf と一致する。</summary>
        public static string BarOf(byte hazard)
        {
            int filled = StepOf(hazard);
            var sb = new System.Text.StringBuilder(Steps);
            for (int i = 0; i < Steps; i++) sb.Append(i < filled ? FilledChar : EmptyChar);
            return sb.ToString();
        }
    }
}
