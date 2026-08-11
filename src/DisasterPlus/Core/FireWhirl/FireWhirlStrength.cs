namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 燃焼棟数から旋風の大きさと破壊力を決める。
    /// 小さい火災なら小さい旋風、大火災なら大きい旋風。
    /// </summary>
    public static class FireWhirlStrength
    {
        public const float MinRadius = 40f;
        public const float MaxRadius = 220f;

        /// <summary>この棟数で MaxRadius に到達する。</summary>
        private const int SaturationCount = 120;

        /// <summary>旋風の半径（メートル）。</summary>
        public static float RadiusFor(int burningCount)
        {
            return MinRadius + (MaxRadius - MinRadius) * Curve(burningCount);
        }

        /// <summary>破壊力の倍率 [0, 1]。Game 層が VortexAI の破壊半径に掛ける。</summary>
        public static float DamageScaleFor(int burningCount)
        {
            return Curve(burningCount);
        }

        /// <summary>
        /// 0 から 1 へ単調増加し、SaturationCount で 1 に達して飽和する曲線。
        /// 平方根なので序盤の伸びが大きく、大火災でも半径が発散しない。
        /// </summary>
        private static float Curve(int burningCount)
        {
            if (burningCount <= 0) return 0f;
            if (burningCount >= SaturationCount) return 1f;
            return (float)System.Math.Sqrt((double)burningCount / SaturationCount);
        }
    }
}
