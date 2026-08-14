namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 震央からの距離と地震の強度から、**バニラが実際に倒壊判定へ使っている
    /// 局所係数**を出す。捏造ではない。
    ///
    /// IL 事実文書 §A-3 の全体円盤の呼び出し:
    ///   DestroyBuildings(preRadius: R, destructionRadiusMin: 0, destructionRadiusMax: R,
    ///                    probability: 0.02f)   ただし R = 2000 + m_intensity * 20
    /// その中で建物ごとに
    ///   fD = (destructionRadiusMax - dist) / Max(1, destructionRadiusMax - destructionRadiusMin)
    /// を計算する。min = 0、max = R ≧ 2000 なので Max(1, ...) は常に R になり、
    /// **fD = 1 - dist/R** に簡約される。この値がここでいう s である。
    ///
    /// 依頼文の「震源からの距離に応じた震度分布の概念が無い」は半分だけ正しかった。
    /// 距離減衰は最初から効いている。**欠けていたのは可視化だけ。**
    /// </summary>
    public static class SeismicIntensity
    {
        /// <summary>全体円盤の基底半径（IL: ldc.r4 2000）。</summary>
        public const float BaseRadius = 2000f;

        /// <summary>強度 1 あたりの半径増分（IL: ldc.r4 20）。</summary>
        public const float RadiusPerIntensity = 20f;

        /// <summary>
        /// DisasterManager.CreateDisaster が入れる既定値（IL_0028）。
        /// カメラシェイクの補正がここでゼロになる基準点でもある（Task 6）。
        /// </summary>
        public const byte VanillaDefaultIntensity = 55;

        /// <summary>全体円盤の半径 R。強度 55 で 3100 m、100 で 4000 m、255 で 7100 m。</summary>
        public static float RadiusOf(byte intensity)
        {
            return BaseRadius + intensity * RadiusPerIntensity;
        }

        /// <summary>
        /// 震央から distance の地点の局所係数 s。震央で 1、R で 0 の線形ランプ。
        ///
        /// R の外では 0 を返すが、**それは「揺れていない」ではなく「バニラが判定すら
        /// していない」**（preRadius によるハードカリング）。呼び出し側は
        /// <see cref="IsInside"/> で区別し、圏外を「強度 0.0」と表示しないこと。
        /// </summary>
        public static float At(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return 0f;

            float r = RadiusOf(intensity);
            if (distance >= r) return 0f;
            return 1f - distance / r;
        }

        /// <summary>バニラが倒壊判定を行う範囲の内側か。</summary>
        public static bool IsInside(float distance, byte intensity)
        {
            if (float.IsNaN(distance) || distance < 0f) return false;
            return distance < RadiusOf(intensity);
        }
    }
}
