using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 波形を**素材を差し替えずに大きくする**。<b>Core なのでエンジンには一切触らない</b>
    /// （<c>UnityEngine</c> も <c>Mathf</c> も出てこない）。
    ///
    /// ── なぜ要るのか（2026-08-22、所有者の依頼「噴火の音が小さい」）─────────
    ///
    /// > あと噴火の音が小さいです。もう倍くらいの音量にしてください。
    ///
    /// **音量を掛ける場所は 3 つあり、そのうち 2 つはこちらのものではない。**
    /// ⑤の音は <c>AudioManager.EffectGroup</c> を通っており（IL 事実文書 §H-23）、
    /// 最終的な音量は
    ///
    /// <code>
    /// m_targetVolume = info.m_volume * volume * m_cachedVolume
    ///                                           ↑ プレイヤーの効果音スライダー
    /// source.volume ← m_targetVolume * (1 - 使用中スロット番号 / m_maxActiveCount)
    /// </code>
    ///
    /// である。<c>volume</c> は既に噴出の強さで <c>[0.35, 1.0]</c> まで上げきっており、
    /// **1 を超えて渡しても <c>AudioSource.volume</c> がそれを受け付けるかは
    /// Unity 側の実装依存で、この MOD からは実測できない**（native 呼び出しなので
    /// IL には出てこない）。確かめられないものに 2 倍を賭けない。
    ///
    /// ── では波形そのものを大きくする。**ただし素材にはもう余白が無い** ────────
    ///
    /// 同梱の <c>erupting-volcano.wav</c> を実測すると:
    ///
    /// | 量 | 値 |
    /// |---|---|
    /// | ピーク | 27412 / 32767 ＝ 0.837（**-1.55 dBFS**） |
    /// | RMS | 5330 / 32767 ＝ 0.163（-15.8 dBFS） |
    ///
    /// **ピークはもう天井の 1.2 倍手前にある**ので、単純に 2 倍すると割れる
    /// （0.837 × 2 = 1.674 が <c>[-1,1]</c> をはみ出す）。
    /// 一方 RMS はピークより 14 dB も下 —— **平均は小さいのにピークだけ大きい**
    /// 素材である。だから「ピークを少しだけ抑えて、それ以外を素直に 2 倍する」が
    /// この素材に対する正しい大きくし方になる。
    ///
    /// ── やっていること ────────────────────────────────
    ///
    /// <see cref="Threshold"/> までは<b>厳密に <paramref name="gain"/> 倍</b>で、
    /// そこから上は 1 に漸近する指数の膝で潰す（<see cref="Apply"/>）。
    ///
    /// <code>
    /// |y| = gain * |x|
    /// |y| &lt;= t : そのまま          ← RMS 0.163 の素材はほとんどここを通る
    /// |y| &gt;  t : t + (1-t)(1 - e^-((|y|-t)/(1-t)))
    /// </code>
    ///
    /// 実測（gain = 2, t = 0.7）: 0.163（RMS）→ 0.326 でちょうど 2 倍、
    /// ピーク 0.837 → 0.988 で**割れない**。つまり<b>体感の大きさはほぼ 2 倍、
    /// 潰れるのはいちばん大きい山だけ</b>である。
    ///
    /// ★ <b>素材のファイルは 1 バイトも書き換えない。</b> 読み込んだあとの
    ///   <c>float[]</c> に掛けるだけなので、所有者が渡した wav はそのまま残る
    ///   （差し替えたときに前の加工が二重に掛かることも無い）。
    /// </summary>
    public static class LoudnessBoost
    {
        /// <summary>
        /// ここまでは**厳密に gain 倍**。上は膝に入る。
        /// 0 &lt; t &lt; 1 でなければならない（1 にすると膝が消えて割れる）。
        /// </summary>
        public const float Threshold = 0.7f;

        /// <summary>
        /// 波形（<c>[-1,1]</c> のインターリーブ）を <paramref name="gain"/> 倍する。
        /// **配列はその場で書き換える**（36 秒 × 2ch で 3.2 M 要素あり、
        /// もう 1 本作るのは 12 MB の無駄である）。
        ///
        /// <paramref name="samples"/> が null なら何もしない。
        /// <paramref name="gain"/> が 1 以下・NaN・∞ なら**何もしない**
        /// （「小さくする」用途はこの型に無い。あるなら別の型にすること）。
        ///
        /// 戻り値は加工後のピーク（診断用）。空配列なら 0。
        /// </summary>
        public static float Apply(float[] samples, float gain)
        {
            if (samples == null || samples.Length == 0) return 0f;
            if (IsBad(gain) || gain <= 1f) return PeakOf(samples);

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = Shape(samples[i], gain);
                samples[i] = v;

                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }
            return peak;
        }

        /// <summary>
        /// 1 サンプルぶん。<see cref="Threshold"/> までは線形、そこから上は
        /// 1 へ漸近する。**符号は必ず保つ**（保たないと波形が別物になる）。
        /// 異常な入力は 0（無音）—— 0 で埋めるのはここだけで、
        /// 「読めなかった」ではなく「その 1 サンプルが壊れていた」である。
        /// </summary>
        public static float Shape(float sample, float gain)
        {
            if (IsBad(sample)) return 0f;
            if (IsBad(gain) || gain <= 1f) return Clamp(sample);

            float sign = sample < 0f ? -1f : 1f;
            float magnitude = sample < 0f ? -sample : sample;

            float y = magnitude * gain;
            if (y <= Threshold) return sign * y;

            // t から上は (1 - t) の幅を使って 1 へ漸近する。
            const float Knee = 1f - Threshold;
            float over = (y - Threshold) / Knee;
            float shaped = Threshold + Knee * (1f - (float)Math.Exp(-over));
            return sign * Clamp(shaped);
        }

        /// <summary>今のピーク（絶対値の最大）。</summary>
        public static float PeakOf(float[] samples)
        {
            if (samples == null) return 0f;

            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i];
                if (IsBad(v)) continue;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }
            return peak;
        }

        private static float Clamp(float v)
        {
            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
