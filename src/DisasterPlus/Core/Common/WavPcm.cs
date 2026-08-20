using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// RIFF/WAVE の <c>byte[]</c> を、そのまま <c>AudioClip.SetData</c> へ渡せる
    /// インターリーブ済みの <c>float[]</c> へ変換する**純粋な**パーサ。
    ///
    /// ── なぜ Core に置くのか ──────────────────────────────────
    ///
    /// 実行時に wav を読む定番は <c>WWW</c> ＋ <c>file://</c> ＋ <c>GetAudioClip</c> だが、
    /// あれは**非同期**で、コルーチンを回す <c>MonoBehaviour</c> と「まだ読めていない」
    /// 状態を MOD のライフサイクルへ持ち込む。<c>AudioClip.Create</c> ＋ <c>SetData</c> は
    /// **完全に同期**なので、その 2 つがどちらも要らない。
    ///
    /// そして同期にすると、**バイト列 → サンプルの変換にエンジンが 1 つも要らなくなる**。
    /// だからここは Core（<c>UnityEngine</c> も Cities API も <c>System.Random</c> も
    /// LINQ も無し、net35 と net8.0 の両対応）に置き、単体テストで固定できる。
    /// <c>AudioClip</c> を作る部分だけが Game 側にある。
    ///
    /// ── 契約 ─────────────────────────────────────────
    ///
    /// <see cref="Parse"/> は**決して投げない**。読めなければ <see cref="Valid"/> が
    /// false の 1 個を返し、<see cref="Error"/> に英語の理由が入る。
    /// 呼び出し側（<c>VolcanoEruptionAudio</c>）は「音が出ない」以外の影響を受けない ——
    /// **ファイルが消されていても火山は今日どおりに動く**というのがこの設計の要件である。
    ///
    /// ★ <b>「それらしい既定値」を作らない。</b> 壊れたヘッダを 44.1 kHz 16 bit ステレオと
    ///   みなして読み進めると、雑音を全開で鳴らすという最悪の失敗になる。
    ///   分からなければ何も返さない。
    /// </summary>
    public sealed class WavPcm
    {
        /// <summary>
        /// 受け付ける総サンプル数（＝ <c>FrameCount × Channels</c>）の上限。
        ///
        /// 超えた入力は**読まずに断る**。<c>float[]</c> 1 本ぶんで 4 バイト／サンプルなので、
        /// 3200 万で 128 MB になる。ヘッダの数字だけを信じて確保すると、
        /// 壊れた（あるいは悪意のある）ファイル 1 個でゲームごと落ちる。
        /// </summary>
        public const int MaxTotalSamples = 32000000;

        /// <summary>WAVE_FORMAT_PCM。</summary>
        private const int FormatPcm = 1;

        /// <summary>WAVE_FORMAT_IEEE_FLOAT。</summary>
        private const int FormatFloat = 3;

        /// <summary>WAVE_FORMAT_EXTENSIBLE（実体は SubFormat GUID の先頭 2 バイト）。</summary>
        private const int FormatExtensible = 0xFFFE;

        /// <summary>読めたか。false のとき <see cref="Samples"/> は長さ 0 である。</summary>
        public readonly bool Valid;

        /// <summary>読めなかった理由（**英語・診断用**）。読めていれば null。</summary>
        public readonly string Error;

        /// <summary>サンプリング周波数（Hz）。読めていなければ 0。</summary>
        public readonly int SampleRate;

        /// <summary>チャンネル数。読めていなければ 0。</summary>
        public readonly int Channels;

        /// <summary>元ファイルの量子化ビット数（**変換後の <see cref="Samples"/> は常に float**）。</summary>
        public readonly int BitsPerSample;

        /// <summary>1 チャンネルあたりのサンプル数（＝ <c>AudioClip.Create</c> の lengthSamples）。</summary>
        public readonly int FrameCount;

        /// <summary>
        /// インターリーブ済みのサンプル <c>[-1,1]</c>。長さは <c>FrameCount × Channels</c>。
        /// <see cref="Valid"/> が false でも null にはしない（呼び出し側の null 判定を増やさない）。
        /// </summary>
        public readonly float[] Samples;

        private WavPcm(string error)
        {
            Valid = false;
            Error = error;
            Samples = new float[0];
        }

        private WavPcm(int sampleRate, int channels, int bitsPerSample,
                       int frameCount, float[] samples)
        {
            Valid = true;
            Error = null;
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            FrameCount = frameCount;
            Samples = samples;
        }

        /// <summary>再生時間（秒）。読めていなければ 0。</summary>
        public float LengthSeconds
        {
            get
            {
                if (!Valid || SampleRate <= 0) return 0f;
                return FrameCount / (float)SampleRate;
            }
        }

        /// <summary>
        /// 読めなかった 1 個を作る。**テストと呼び出し側が同じ形を共有するため**に公開する。
        /// </summary>
        public static WavPcm Failed(string error)
        {
            return new WavPcm(string.IsNullOrEmpty(error) ? "unknown error" : error);
        }

        /// <summary>
        /// RIFF/WAVE を解釈する。**投げない。**
        ///
        /// チャンクは頭から順に歩く（<c>fmt </c> と <c>data</c> の間に <c>LIST</c> /
        /// <c>fact</c> / <c>bext</c> などが挟まるファイルは珍しくない。決め打ちのオフセットで
        /// 読むと、そういうファイルで**無音ではなく雑音**になる）。
        /// </summary>
        public static WavPcm Parse(byte[] bytes)
        {
            if (bytes == null) return Failed("no bytes were read");
            if (bytes.Length < 12) return Failed("the file is too short to be a RIFF/WAVE file");

            if (!Matches(bytes, 0, 'R', 'I', 'F', 'F'))
            {
                // RIFX はビッグエンディアンの WAVE。**推測で読み替えない。**
                if (Matches(bytes, 0, 'R', 'I', 'F', 'X'))
                {
                    return Failed("this is a big-endian RIFX file, which is not supported");
                }
                return Failed("the file does not start with a RIFF header");
            }

            if (!Matches(bytes, 8, 'W', 'A', 'V', 'E'))
            {
                return Failed("the RIFF file is not a WAVE file");
            }

            int format = 0;
            int channels = 0;
            int sampleRate = 0;
            int bits = 0;
            int blockAlign = 0;
            bool haveFormat = false;

            int dataOffset = -1;
            int dataLength = 0;

            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                int chunkSize = ReadInt32(bytes, position + 4);
                int bodyStart = position + 8;

                // 負（＝ 2 GB 超が int で折り返した）と、ファイル末尾をはみ出す長さの両方を
                // ここで刈る。はみ出す data はよくある（録音が途中で切れた形）ので、
                // 断らずに**実際に在るぶんだけ**読む。
                if (chunkSize < 0) return Failed("a RIFF chunk declares a negative length");

                int available = bytes.Length - bodyStart;
                if (available < 0) break;
                int bodyLength = chunkSize < available ? chunkSize : available;

                if (Matches(bytes, position, 'f', 'm', 't', ' '))
                {
                    if (bodyLength < 16) return Failed("the fmt chunk is too short");

                    format = ReadUInt16(bytes, bodyStart);
                    channels = ReadUInt16(bytes, bodyStart + 2);
                    sampleRate = ReadInt32(bytes, bodyStart + 4);
                    blockAlign = ReadUInt16(bytes, bodyStart + 12);
                    bits = ReadUInt16(bytes, bodyStart + 14);

                    // WAVE_FORMAT_EXTENSIBLE の実体は SubFormat GUID の先頭 2 バイトにある。
                    // ここを見ないと、ふつうの 24 bit PCM を「未知の形式」と断ってしまう。
                    if (format == FormatExtensible && bodyLength >= 40)
                    {
                        format = ReadUInt16(bytes, bodyStart + 24);
                    }

                    haveFormat = true;
                }
                else if (Matches(bytes, position, 'd', 'a', 't', 'a'))
                {
                    dataOffset = bodyStart;
                    dataLength = bodyLength;
                }

                // チャンクの本体は偶数境界へパディングされる（長さが奇数なら 1 バイト詰まる）。
                //
                // ★ 長さ 0 のチャンクでも position は必ず 8 バイト進むので、ここは止まらない。
                //   止まるとしたら int の折り返しで advance が負になったときだけなので、
                //   **そこだけ**を見て抜ける（無限ループにしない）。
                int advance = chunkSize + (chunkSize & 1);
                if (advance < 0) break;
                position = bodyStart + advance;
            }

            if (!haveFormat) return Failed("the file has no fmt chunk");
            if (dataOffset < 0) return Failed("the file has no data chunk");

            if (channels <= 0 || channels > 8)
            {
                return Failed("unsupported channel count " + channels);
            }
            if (sampleRate <= 0 || sampleRate > 384000)
            {
                return Failed("unsupported sample rate " + sampleRate);
            }

            int bytesPerSample = BytesPerSampleOf(format, bits);
            if (bytesPerSample == 0)
            {
                return Failed("unsupported wav encoding (format " + format + ", "
                              + bits + " bit); only PCM 8/16/24/32 and 32-bit float are read");
            }

            // blockAlign を信じない（0 や嘘を書くエンコーダが実在する）。
            // チャンネル数とビット数から自分で出す。
            int frameBytes = bytesPerSample * channels;
            if (frameBytes <= 0) return Failed("the fmt chunk describes a zero-length frame");
            if (blockAlign > 0 && blockAlign != frameBytes)
            {
                // 一致しないのは異常だが、ここで断ると読める音まで捨てることになる。
                // 自分の計算を採用して読み進める（値は診断に出ない ——
                // 出すべき情報はサンプル数のほうである）。
                blockAlign = frameBytes;
            }

            int frameCount = dataLength / frameBytes;
            if (frameCount <= 0) return Failed("the data chunk holds no complete sample frame");

            long total = (long)frameCount * channels;
            if (total > MaxTotalSamples)
            {
                return Failed("the file holds " + total + " samples, more than the "
                              + MaxTotalSamples + " this mod will load");
            }

            float[] samples = new float[(int)total];
            bool ok = Decode(bytes, dataOffset, format, bytesPerSample, samples);
            if (!ok) return Failed("the sample data could not be decoded");

            return new WavPcm(sampleRate, channels, bits, frameCount, samples);
        }

        /// <summary>
        /// 1 サンプルのバイト数。**対応していない組み合わせは 0** を返す
        /// （「たぶん 2 バイト」で進めない）。
        /// </summary>
        private static int BytesPerSampleOf(int format, int bits)
        {
            if (format == FormatPcm)
            {
                if (bits == 8) return 1;
                if (bits == 16) return 2;
                if (bits == 24) return 3;
                if (bits == 32) return 4;
                return 0;
            }
            if (format == FormatFloat)
            {
                if (bits == 32) return 4;
                return 0;
            }
            return 0;
        }

        private static bool Decode(byte[] bytes, int offset, int format,
                                   int bytesPerSample, float[] samples)
        {
            int count = samples.Length;
            int at = offset;

            if (format == FormatFloat)
            {
                for (int i = 0; i < count; i++)
                {
                    float v = ToSingle(bytes, at);
                    // 32 bit float の wav は 1.0 を超える値を持ちうる。
                    // クランプしないと SetData の先で歪む。
                    if (float.IsNaN(v)) v = 0f;
                    else if (v > 1f) v = 1f;
                    else if (v < -1f) v = -1f;
                    samples[i] = v;
                    at += 4;
                }
                return true;
            }

            switch (bytesPerSample)
            {
                case 1:
                    // 8 bit PCM だけは**符号なし**（0..255、無音が 128）。
                    for (int i = 0; i < count; i++)
                    {
                        samples[i] = (bytes[at] - 128) / 128f;
                        at++;
                    }
                    return true;

                case 2:
                    for (int i = 0; i < count; i++)
                    {
                        int v = (short)(bytes[at] | (bytes[at + 1] << 8));
                        samples[i] = v / 32768f;
                        at += 2;
                    }
                    return true;

                case 3:
                    for (int i = 0; i < count; i++)
                    {
                        int v = bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16);
                        // 24 bit の符号拡張。忘れると波形の下半分が上へ跳ね返る。
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        samples[i] = v / 8388608f;
                        at += 3;
                    }
                    return true;

                case 4:
                    for (int i = 0; i < count; i++)
                    {
                        int v = ReadInt32(bytes, at);
                        samples[i] = v / 2147483648f;
                        at += 4;
                    }
                    return true;
            }

            return false;
        }

        /// <summary>
        /// リトルエンディアンの 4 バイトを float として読む。
        /// <c>BitConverter</c> は環境のエンディアンに従うので、**そこを見てから**渡す
        /// （Core はエンジンに依らないので、実行環境を決め打ちにしない）。
        /// </summary>
        private static float ToSingle(byte[] bytes, int at)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(bytes, at);

            byte[] swapped = new byte[4];
            swapped[0] = bytes[at + 3];
            swapped[1] = bytes[at + 2];
            swapped[2] = bytes[at + 1];
            swapped[3] = bytes[at];
            return BitConverter.ToSingle(swapped, 0);
        }

        private static int ReadInt32(byte[] bytes, int at)
        {
            return bytes[at]
                   | (bytes[at + 1] << 8)
                   | (bytes[at + 2] << 16)
                   | (bytes[at + 3] << 24);
        }

        private static int ReadUInt16(byte[] bytes, int at)
        {
            return bytes[at] | (bytes[at + 1] << 8);
        }

        private static bool Matches(byte[] bytes, int at, char a, char b, char c, char d)
        {
            if (at + 4 > bytes.Length) return false;
            return bytes[at] == (byte)a
                   && bytes[at + 1] == (byte)b
                   && bytes[at + 2] == (byte)c
                   && bytes[at + 3] == (byte)d;
        }
    }
}
