namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// バニラの粒子数の式を<b>「1 秒あたり何粒出したいか」から逆に解く</b>だけの純関数。
    /// **エンジン非依存**（④の渦と暴風雨の両方が使う）。
    ///
    /// エフェクト実測文書 §B-4 の式:
    ///
    /// <code>
    /// count = max(100, π·r²) × (timeDelta × magnitude × 0.01 × rateOverTime)
    /// </code>
    ///
    /// <c>timeDelta</c> は両辺で打ち消し合うので、返す <c>magnitude</c> は
    /// <b>フレームレートにもゲーム速度にも依らない</b>。
    ///
    /// ★ 円盤ごとに解き直すこと。式は面積で効くので、円盤を広げたぶんだけ
    ///   <c>magnitude</c> を下げないと大きい円盤だけ濃くなる。
    ///
    /// 壊れた入力（0 以下・NaN）には 0 を返す ——「粒子数 NaN で空が埋まる」を作らない。
    /// </summary>
    public static class ParticleBudget
    {
        /// <param name="discRadius">1 粒（1 回の <c>RenderEffect</c>）の円盤半径（m）。</param>
        /// <param name="rateOverTime">エフェクト側の <c>emission.rateOverTime.constant</c>。</param>
        /// <param name="particlesPerSecond">**全体**で 1 秒あたりに出したい粒子数。</param>
        /// <param name="emitterCount">1 フレームに撃つ <c>RenderEffect</c> の本数。</param>
        public static float MagnitudeFor(float discRadius, float rateOverTime,
                                         float particlesPerSecond, int emitterCount)
        {
            if (!(discRadius > 0f) || !(rateOverTime > 0f)) return 0f;
            if (!(particlesPerSecond > 0f) || emitterCount <= 0) return 0f;

            float area = 3.14159265f * discRadius * discRadius;
            if (area < 100f) area = 100f;      // §B-4 の max(100, πr²)

            float perEmitter = particlesPerSecond / emitterCount;
            float magnitude = perEmitter / (area * 0.01f * rateOverTime);

            if (float.IsNaN(magnitude) || magnitude <= 0f) return 0f;
            return magnitude;
        }
    }
}
