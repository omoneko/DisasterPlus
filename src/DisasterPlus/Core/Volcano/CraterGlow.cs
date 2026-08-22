using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **火口のマグマだまりと、そこから噴煙へ届く光。**
    /// <b>Core なのでエンジンには一切触らない</b>（純関数だけで、状態を 1 つも持たない）。
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// > 噴火口のマグマだまり（溶岩同様光る）と噴煙への光の放射
    ///
    /// ── ★★ 点滅させないこと ───────────────────────────────
    ///
    /// <see cref="LavaGlow"/> は一度この失敗をしている ——
    /// UV をスクロールさせたら**溶岩流が点滅して見えた**（所有者の指摘）。
    /// だからここも明るさは<b>時刻の滑らかな関数</b>だけで作り、
    /// いちばん速い成分は <see cref="BreathHz"/> ＝ 0.19 Hz（周期 5 秒強）である。
    /// **これより速い成分を足さないこと。**
    ///
    /// マグマだまりが揺らぐのは対流であって明滅ではない。周期の違う 2 本の
    /// 正弦波を足して、繰り返しに聞こえないようにしてある（通約でない比）。
    ///
    /// ── 光の届き方 ────────────────────────────────────
    ///
    /// 火口から上へ行くほど暗い。逆二乗にはしない ——
    /// 実際の噴煙は<b>散乱体</b>なので、光源から離れても急には消えず、
    /// かつ<b>噴煙自体の厚みで奥が隠れる</b>。両方が効いた結果は指数に近い形になる。
    /// <see cref="ReachFraction"/> は「柱の高さの何割まで届くか」で、
    /// そこで<b>きっかり 0</b> になる —— 上まで薄く光らせると、
    /// 噴煙全体が発光しているように見えて夜景が壊れる。
    /// </summary>
    public static class CraterGlow
    {
        /// <summary>マグマだまりの半径（火口半径に対する比）の下限。</summary>
        public const float PoolRadiusFloorRatio = 0.45f;

        /// <summary>噴出の強さで足される半径（火口半径に対する比）。</summary>
        public const float PoolRadiusGainRatio = 0.35f;

        /// <summary>いちばん速い成分（Hz）。**ここより速い成分を足さないこと。**</summary>
        public const float BreathHz = 0.19f;

        /// <summary>2 本目のうねり（Hz）。<see cref="BreathHz"/> と通約でない値。</summary>
        public const float Breath2Hz = 0.071f;

        /// <summary>うねりの深さ（0 で一定、1 で 0 まで落ちる）。</summary>
        public const float BreathDepth = 0.22f;

        /// <summary>噴出の強さ 0 のときの明るさ。**0 にしない**（火口は噴火前から赤い）。</summary>
        public const float MinBrightness = 0.35f;

        /// <summary>噴出の強さ 1 のときの明るさ。</summary>
        public const float MaxBrightness = 1.0f;

        /// <summary>光が届く高さ（柱の高さに対する比）。ここで<b>きっかり 0</b>。</summary>
        public const float ReachFraction = 0.34f;

        /// <summary>火口の真上での光の強さ（マグマだまりの明るさに対する比）。</summary>
        public const float LightAtVentRatio = 0.85f;

        /// <summary>光の減り方の鋭さ。大きいほど早く暗くなる。</summary>
        public const float LightDecay = 2.6f;

        /// <summary>
        /// マグマだまりの半径（m）。<paramref name="craterRadiusMetres"/> は火口の半径。
        /// **火口より大きくしない** —— 縁からあふれて見えると、それは溶岩流の仕事である。
        /// </summary>
        public static float PoolRadiusMetres(float craterRadiusMetres, float unit)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return 0f;

            float u = Clamp01(unit);
            float ratio = PoolRadiusFloorRatio + PoolRadiusGainRatio * u;
            if (ratio > 1f) ratio = 1f;
            return craterRadiusMetres * ratio;
        }

        /// <summary>
        /// マグマだまりの明るさ <c>[0,1]</c>。<paramref name="seconds"/> は⑤の効果時計。
        /// **時刻の滑らかな関数だけ**（クラス doc）。
        /// </summary>
        public static float PoolBrightness(float unit, float seconds)
        {
            float u = Clamp01(unit);
            float baseline = MinBrightness + (MaxBrightness - MinBrightness) * u;

            if (IsBad(seconds)) seconds = 0f;

            // 2 本のうねりを平均する。**片方だけだと周期がはっきり見える。**
            double a = Math.Sin(6.2831853 * BreathHz * seconds);
            double b = Math.Sin(6.2831853 * Breath2Hz * seconds + 1.3);
            float wave = (float)((a * 0.6 + b * 0.4) * 0.5 + 0.5);   // [0,1]

            float envelope = 1f - BreathDepth + BreathDepth * wave;
            return Clamp01(baseline * envelope);
        }

        /// <summary>
        /// 火口から <paramref name="heightMetres"/> だけ上の噴煙が、マグマの光で
        /// どれだけ明るくなるか <c>[0,1]</c>。
        ///
        /// <paramref name="plumeHeightMetres"/> は噴煙柱の全高で、
        /// <c>ReachFraction</c> を超えた高さは<b>きっかり 0</b> である。
        /// </summary>
        public static float LightAt(float heightMetres, float plumeHeightMetres,
                                    float poolBrightness)
        {
            if (IsBad(heightMetres) || heightMetres < 0f) return 0f;
            if (IsBad(plumeHeightMetres) || plumeHeightMetres <= 0f) return 0f;

            float reach = plumeHeightMetres * ReachFraction;
            if (reach <= 0f || heightMetres >= reach) return 0f;

            float t = heightMetres / reach;

            // 指数で落ちてから、届く距離でちょうど 0 になるよう窓を掛ける
            // （窓が無いと reach の直前で切れて、輪郭が線になって見える）。
            float decay = (float)Math.Exp(-LightDecay * t);
            float window = 1f - t * t;

            float k = Clamp01(poolBrightness) * LightAtVentRatio * decay * window;
            return Clamp01(k);
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
