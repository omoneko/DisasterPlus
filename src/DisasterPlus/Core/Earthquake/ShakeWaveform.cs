namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// **バニラ自身の揺れの式**（IL 事実文書 §A-7、<c>EarthquakeAI.RenderInstance</c>）。
    /// 捏造ではなく、ゲームが毎フレーム計算しているものをそのまま写している。
    ///
    ///   amp = 0.3 / (1 + distance * 0.001)
    ///   amp *= 0.5 - 0.5 * cos(t * 0.02454369)      // 2π/0.02454369 = 256 フレーム周期
    ///   s   = (sin(t * 0.63) + sin(t * 0.17)) * amp
    ///
    /// 成分は 0.63 rad/frame（周期 ≒10 フレーム）と 0.17 rad/frame（≒37 フレーム）の
    /// 2 本だけで、**長周期成分は存在しない**（256 フレーム周期のものは振幅の
    /// 包絡線であって地震動ではない）。長周期地震動は Task 10 が別に足す
    /// —— あちらは第 2 層、こちらは第 1 層。
    ///
    /// **振幅に m_intensity は入っていない。** それがこの MOD が補正する空白であり、
    /// <see cref="IntensityFactor"/> がその補正倍率を返す。
    ///
    /// ── distance の意味は呼び出し側で変わる（設計書 §3.5）────────────────
    ///
    ///   - カメラシェイク補正（<c>CameraShakeBooster</c>、Task 6）は
    ///     **カメラからの距離**。バニラが <c>camera.transform.InverseTransformPoint</c>
    ///     （§A-7 IL_003F）で測っているものと同じで、しかも <c>z</c> を 0.25 倍してから
    ///     長さを取るところまで同じにする。バニラの波に**足す**のだから、同じ点で
    ///     評価しないと形が崩れる。
    ///   - 波形グラフ（Task 8）は **震源から観測点までの距離**。
    ///
    /// **これは同じ式の別評価であって近似ではない。** バニラの式そのものを、
    /// 別の点で評価しているだけである。UI にもその差を書くこと（設計書 §3.5）。
    /// 混同すると、波形グラフが「地面の揺れ」を名乗りながら実際にはカメラの動きを
    /// 描いている、という嘘になる。
    ///
    /// Core は <c>UnityEngine.Mathf</c> を使えないので <c>System.Math</c>（double）で
    /// 計算して float に落とす。バニラの <c>Mathf.Sin</c>（float）とは最下位ビットが
    /// 異なりうるが、これは表示と演出のための量であって予測ではないので許容する。
    /// </summary>
    public static class ShakeWaveform
    {
        public const float BaseAmplitude = 0.3f;
        public const float DistanceFalloff = 0.001f;
        public const float EnvelopeRate = 0.02454369f;
        public const int EnvelopePeriodFrames = 256;
        public const float FastRate = 0.63f;
        public const float SlowRate = 0.17f;

        /// <summary>IL: <c>e = m_referenceFrameIndex - m_activationFrame + 128</c>。</summary>
        public const int FrameOffset = 128;

        /// <summary>
        /// 追加できる強度倍率の上限。強度 255 だと素の値は (255/55 - 1) = 3.64 になり、
        /// 合計でバニラの 4.64 倍になる。画面が使い物にならなくなるのは迫力ではなく不具合。
        /// </summary>
        public const float MaxIntensityFactor = 2f;

        /// <summary>
        /// バニラの表示窓。<paramref name="activeDuration"/> はプレハブ値で、
        /// **読めていないとき（0）は false を返す** —— 窓が分からないまま揺らすと
        /// 地震が終わった後も揺れ続ける。
        ///
        /// IL の <c>if (e &lt;= 0) return; if (e &gt;= m_activeDuration) return;</c> を
        /// そのまま裏返したもの。比較演算子を「読みやすく」書き換えないこと。
        /// </summary>
        public static bool IsShaking(long elapsedPlusOffset, uint activeDuration)
        {
            if (activeDuration == 0u) return false;
            return elapsedPlusOffset > 0 && elapsedPlusOffset < activeDuration;
        }

        /// <summary>包絡線込みの振幅。<paramref name="t"/> はフレーム（小数を含む）。</summary>
        public static float AmplitudeAt(float distance, float t)
        {
            if (float.IsNaN(distance) || float.IsNaN(t)) return 0f;
            if (distance < 0f) distance = 0f;

            float amp = BaseAmplitude / (1f + distance * DistanceFalloff);
            amp *= 0.5f - 0.5f * (float)System.Math.Cos(t * EnvelopeRate);
            return amp < 0f ? 0f : amp;
        }

        /// <summary>符号付きの変位。バニラの <c>s</c> そのもの。</summary>
        public static float DisplacementAt(float distance, float t)
        {
            float amp = AmplitudeAt(distance, t);
            if (amp <= 0f) return 0f;
            return (float)(System.Math.Sin(t * FastRate) + System.Math.Sin(t * SlowRate)) * amp;
        }

        /// <summary>
        /// バニラの揺れに掛ける**追加**倍率（抑制はしない）。
        ///
        /// 強度 55（<c>DisasterManager.CreateDisaster</c> の既定値）でちょうど 0 になり、
        /// そのとき合計はバニラと完全に一致する。**この 0 がこの機能を既定 ON に
        /// してよい唯一の根拠**なので、式を「等価に」書き換えるときも
        /// <c>intensity == 55</c> で厳密に 0f が出ることを必ず確認すること
        /// （55f / 55f は IEEE754 で厳密に 1.0f、そこから 1f を引いて厳密に 0f）。
        /// </summary>
        public static float IntensityFactor(byte intensity)
        {
            float f = (float)intensity / SeismicIntensity.VanillaDefaultIntensity - 1f;
            if (f > MaxIntensityFactor) return MaxIntensityFactor;
            if (f < -1f) return -1f;
            return f;
        }
    }
}
