using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 1 回きりの録音から、**継ぎ目の無いループ**を切り出す純粋な関数。
    /// エンジンに依らない（<c>UnityEngine</c> も Cities API も無し、net35 / net8.0 両対応）。
    ///
    /// ── なぜ要るのか（実測）──────────────────────────────
    ///
    /// 同梱した <c>erupting-volcano.wav</c> は、名前に反して**定常のアンビエンスではない**。
    /// 1 秒ごとの RMS を測ると
    ///
    /// <code>
    /// 0.04 0.11 0.21 0.26 0.28 0.27 0.27 0.24 … 0.21 … 0.08 … 0.01 0.006 0.002 0.001
    /// └ 立ち上がり ┘└──── 山（3〜13 秒）────┘└──── 減衰して無音（36 秒）────┘
    /// </code>
    ///
    /// つまり**噴火 1 回ぶんの録音**である。そのままループすると、36 秒ごとに
    /// 「無音まで落ちて、いきなり大音量に戻る」を繰り返す。継ぎ目を消しても
    /// この脈打ちは消えない —— 消すには**山の部分だけを回す**しかない。
    ///
    /// 録音全体が持っている「立ち上がり → 山 → 減衰」の弧は捨てていない。
    /// **その弧は⑤自身が既に持っている**（<c>VolcanoEruption.Envelope</c>）ので、
    /// 音量の変化はそちらが与え、この波形は**質感だけ**を与える。役割を二重に持たせない。
    ///
    /// ── 継ぎ目の消し方 ─────────────────────────────────
    ///
    /// ループ本体を <c>[start, start+length)</c> とし、その**先頭 <c>fade</c> フレーム**へ
    /// 「ループ末尾のすぐ後ろに続いていたはずの音」<c>[start+length, start+length+fade)</c> を
    /// 混ぜる:
    ///
    /// <code>
    /// out[i] = src[start+i]                                        (i >= fade)
    /// out[i] = src[start+i]·√t + src[start+length+i]·√(1−t)        (i &lt; fade, t = i/fade)
    /// </code>
    ///
    /// 再生が末尾から先頭へ戻る瞬間、出力は <c>src[start+length]</c>（＝末尾の続き）から
    /// 始まり、<c>fade</c> かけて <c>src[start]</c> へ移る。**波形が跳ばないのでクリックが出ない。**
    /// 重みが <c>√</c> なのは、無相関な雑音的素材でエネルギーを一定に保つためである
    /// （線形にすると継ぎ目で音圧が凹む）。
    ///
    /// ★ <b>切り出せないときは元の配列をそのまま返す。</b> 短い wav に差し替えた人へ
    ///   「無音」ではなく「継ぎ目のあるループ」を返すほうが、常にましである。
    /// </summary>
    public static class LoopSlice
    {
        /// <summary>
        /// ループを切り出す。**投げない。**
        /// </summary>
        /// <param name="samples">インターリーブ済みのサンプル。</param>
        /// <param name="channels">チャンネル数（1 以上）。</param>
        /// <param name="startFrame">ループ先頭のフレーム番号。</param>
        /// <param name="lengthFrames">ループの長さ（フレーム）。</param>
        /// <param name="fadeFrames">継ぎ目に使うクロスフェード長（フレーム）。</param>
        /// <returns>
        /// 切り出したインターリーブ配列。素材が足りない・引数がおかしいときは
        /// <paramref name="samples"/> をそのまま返す（**null は返さない**）。
        /// </returns>
        public static float[] Build(float[] samples, int channels,
                                    int startFrame, int lengthFrames, int fadeFrames)
        {
            if (samples == null) return new float[0];
            if (channels <= 0) return samples;
            if (startFrame < 0 || lengthFrames <= 0 || fadeFrames < 0) return samples;
            if (fadeFrames > lengthFrames) return samples;

            int totalFrames = samples.Length / channels;

            // 継ぎ目に混ぜる素材はループ末尾の**後ろ**から取る。そこまで無いなら切らない。
            long needed = (long)startFrame + lengthFrames + fadeFrames;
            if (needed > totalFrames) return samples;

            float[] output = new float[lengthFrames * channels];

            int from = startFrame * channels;
            Array.Copy(samples, from, output, 0, output.Length);

            if (fadeFrames == 0) return output;

            int tail = (startFrame + lengthFrames) * channels;
            for (int f = 0; f < fadeFrames; f++)
            {
                // t は 0 → 1。t=0 で「末尾の続き」だけ、t=1 でループ先頭だけになる。
                double t = (f + 1) / (double)(fadeFrames + 1);
                float headWeight = (float)Math.Sqrt(t);
                float tailWeight = (float)Math.Sqrt(1.0 - t);

                int at = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    output[at + c] = output[at + c] * headWeight
                                     + samples[tail + at + c] * tailWeight;
                }
            }

            return output;
        }

        /// <summary>
        /// 秒で指定する版。<paramref name="sampleRate"/> が 0 以下なら切らない。
        /// **これが実際に使われる口**である（定数を秒で書けるほうが読める）。
        /// </summary>
        public static float[] Build(float[] samples, int channels, int sampleRate,
                                    float startSeconds, float lengthSeconds, float fadeSeconds)
        {
            if (samples == null) return new float[0];
            if (sampleRate <= 0) return samples;
            if (float.IsNaN(startSeconds) || float.IsNaN(lengthSeconds)
                || float.IsNaN(fadeSeconds))
            {
                return samples;
            }

            return Build(samples, channels,
                         (int)(startSeconds * sampleRate),
                         (int)(lengthSeconds * sampleRate),
                         (int)(fadeSeconds * sampleRate));
        }
    }
}
