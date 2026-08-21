using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 影響範囲の行に出す「範囲の広さ」と「概数への丸め」。
    ///
    /// ★★ <b>これは「壊れる数」ではない。</b> <see cref="RoundedEstimate"/> に渡すのは
    /// <b>調査した瞬間に範囲内にあった実数</b>で、返るのはそれを丸めた概数である。
    /// 準備（破壊）は数ゲーム内分かけて外側へ広がり、**その間に都市は変化する** ——
    /// 建物は建ち、道路は引かれ、火災や他の災害でも消える。したがって実際に壊れる数は
    /// この値と一致しない。設計書 §7.2 が「概数であることも明示する」と要求している
    /// 理由がこれである（<c>Strings.VolcanoEstimateNote</c> がその 1 文）。
    ///
    /// <b>実数をそのまま出してはいけない。</b> 「243 棟」と書くとプレイヤーは
    /// 「ぴったり 243 棟だけ壊れる」と読む。⑤にその保証は無い。
    ///
    /// 丸めは<b>有効数字 2 桁の四捨五入</b>で、100 未満はそのまま出す
    /// （「およそ 3 棟」は嘘くさいうえ、1 桁の値を丸めると 0 になりうる ——
    /// 「0 棟」と「数えたが少なかった」は別物である）。
    ///
    /// > **切り捨てではなく四捨五入である。** 切り捨ては壊れる量を必ず小さく見せる。
    /// > これはプレイヤーの資産を不可逆に壊す操作の直前に出す数字なので、
    /// > 過小に寄せる丸めを選ばない。単調非減少であることはテストが固定している。
    ///
    /// エンジン非依存（<c>UnityEngine</c> も LINQ も <c>System.Random</c> も使わない）。
    /// </summary>
    public static class ClearanceEstimate
    {
        /// <summary>raw セルの一辺（m）。§A-1 / §C-8 の <c>cell = 16</c>。</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>100 未満はそのまま出す境目（クラス doc）。</summary>
        private const int ExactBelow = 100;

        /// <summary>有効数字の桁数。</summary>
        private const int SignificantDigits = 2;

        /// <summary>
        /// 半径 <paramref name="radiusMetres"/> の円が覆う raw セルの概数。
        ///
        /// **これは面積の目安であって、走査したセル数ではない**（走査は
        /// 64 m の建物グリッドで行う）。NaN・0 以下は 0。
        /// </summary>
        public static int CellsInside(float radiusMetres)
        {
            double area = FootprintAreaSquareMetres(radiusMetres);
            if (area <= 0d) return 0;

            double cells = area / (RawCellSizeMetres * (double)RawCellSizeMetres);
            if (cells >= int.MaxValue) return int.MaxValue;
            return (int)cells;
        }

        /// <summary>
        /// 影響範囲の面積（m²）。NaN・0 以下は 0。
        /// </summary>
        public static float FootprintAreaSquareMetres(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return 0f;
            return (float)(Math.PI * radiusMetres * (double)radiusMetres);
        }

        /// <summary>
        /// 実数 → 概数（クラス doc）。0 以下は 0、100 未満はそのまま、
        /// それ以上は有効数字 2 桁で四捨五入する。**単調非減少**。
        /// </summary>
        public static int RoundedEstimate(int exactCount)
        {
            if (exactCount <= 0) return 0;
            if (exactCount < ExactBelow) return exactCount;

            // unit = 10^(桁数 - SignificantDigits)、lead は [10, 99]。
            long unit = 1L;
            long lead = exactCount;
            while (lead >= ExactBelow)
            {
                lead /= 10L;
                unit *= 10L;
            }

            long remainder = exactCount - lead * unit;
            // remainder * 2 >= unit で「半分以上なら 1 つ上げる」。
            // 除算で 0.5 を作らないので、丸め方向がプラットフォームに依らない。
            long rounded = (remainder * 2L >= unit) ? (lead + 1L) * unit : lead * unit;

            return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
        }
    }
}
