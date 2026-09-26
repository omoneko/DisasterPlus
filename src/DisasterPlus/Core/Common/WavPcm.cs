using System;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// A **pure** parser turning a RIFF/WAVE <c>byte[]</c> into an interleaved
    /// <c>float[]</c> that can be handed straight to <c>AudioClip.SetData</c>.
    ///
    /// ── Why it lives in Core ──────────────────────────────────
    ///
    /// The standard way to read a wav at runtime is <c>WWW</c> + <c>file://</c> +
    /// <c>GetAudioClip</c>, but that is **asynchronous** and brings both a
    /// <c>MonoBehaviour</c> to run the coroutine and a "not loaded yet" state into the mod's
    /// lifecycle. <c>AudioClip.Create</c> + <c>SetData</c> is **completely synchronous**, so
    /// neither of those is needed.
    ///
    /// And once it is synchronous, **not one bit of the engine is needed to turn bytes into
    /// samples**. So this lives in Core (no <c>UnityEngine</c>, no Cities API, no
    /// <c>System.Random</c>, no LINQ; works on both net35 and net8.0) and can be pinned by
    /// unit tests. Only the part that creates the <c>AudioClip</c> is on the Game side.
    ///
    /// ── The contract ─────────────────────────────────────────
    ///
    /// <see cref="Parse"/> **never throws**. If it cannot read the file it returns an
    /// instance whose <see cref="Valid"/> is false, with the reason in <see cref="Error"/>
    /// in English.
    /// The caller (<c>VolcanoEruptionAudio</c>) suffers no consequence other than "no
    /// sound" — **the volcano works exactly as it does today even if the file has been
    /// deleted**, and that is a requirement of this design.
    ///
    /// ★ <b>Do not invent "plausible defaults".</b> Treating a corrupt header as 44.1 kHz
    ///   16 bit stereo and reading on gives the worst possible failure: noise at full
    ///   volume. If it is not clear, return nothing.
    /// </summary>
    public sealed class WavPcm
    {
        /// <summary>
        /// The cap on the total sample count accepted (i.e. <c>FrameCount × Channels</c>).
        ///
        /// Input past it is **refused without being read**. One <c>float[]</c> is 4 bytes a
        /// sample, so 32 million comes to 128 MB. Allocate on the header's numbers alone and
        /// a single corrupt (or malicious) file takes the whole game down.
        /// </summary>
        public const int MaxTotalSamples = 32000000;

        /// <summary>WAVE_FORMAT_PCM.</summary>
        private const int FormatPcm = 1;

        /// <summary>WAVE_FORMAT_IEEE_FLOAT.</summary>
        private const int FormatFloat = 3;

        /// <summary>WAVE_FORMAT_EXTENSIBLE (the real format is in the first two bytes of the
        /// SubFormat GUID).</summary>
        private const int FormatExtensible = 0xFFFE;

        /// <summary>Whether it was read. When false, <see cref="Samples"/> has length 0.</summary>
        public readonly bool Valid;

        /// <summary>Why it could not be read (**English, for diagnostics**). Null if it was
        /// read.</summary>
        public readonly string Error;

        /// <summary>Sample rate (Hz). 0 if it could not be read.</summary>
        public readonly int SampleRate;

        /// <summary>Channel count. 0 if it could not be read.</summary>
        public readonly int Channels;

        /// <summary>The source file's bit depth (**the converted <see cref="Samples"/> is
        /// always float**).</summary>
        public readonly int BitsPerSample;

        /// <summary>Samples per channel (i.e. <c>AudioClip.Create</c>'s lengthSamples).</summary>
        public readonly int FrameCount;

        /// <summary>
        /// The interleaved samples, <c>[-1,1]</c>. Its length is <c>FrameCount × Channels</c>.
        /// It is never null even when <see cref="Valid"/> is false (so as not to add another
        /// null check on the caller).
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

        /// <summary>Playing time (seconds). 0 if it could not be read.</summary>
        public float LengthSeconds
        {
            get
            {
                if (!Valid || SampleRate <= 0) return 0f;
                return FrameCount / (float)SampleRate;
            }
        }

        /// <summary>
        /// Builds a "could not be read" instance. Public **so the tests and the callers
        /// share the same shape**.
        /// </summary>
        public static WavPcm Failed(string error)
        {
            return new WavPcm(string.IsNullOrEmpty(error) ? "unknown error" : error);
        }

        /// <summary>
        /// Interprets a RIFF/WAVE. **It never throws.**
        ///
        /// The chunks are walked in order from the top (files with <c>LIST</c> /
        /// <c>fact</c> / <c>bext</c> and the like between <c>fmt </c> and <c>data</c> are
        /// not unusual. Read at a hard-coded offset and such a file gives you **noise, not
        /// silence**).
        /// </summary>
        public static WavPcm Parse(byte[] bytes)
        {
            if (bytes == null) return Failed("no bytes were read");
            if (bytes.Length < 12) return Failed("the file is too short to be a RIFF/WAVE file");

            if (!Matches(bytes, 0, 'R', 'I', 'F', 'F'))
            {
                // RIFX is big-endian WAVE. **Do not guess and reinterpret it.**
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

                // Prune both the negative case (over 2 GB wrapped round in an int) and a
                // length that runs past the end of the file. A data chunk that runs over is
                // common (a recording that was cut off), so rather than refusing it we read
                // **only as much as is actually there**.
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

                    // For WAVE_FORMAT_EXTENSIBLE the real format is in the first two bytes
                    // of the SubFormat GUID. Without looking here, ordinary 24 bit PCM gets
                    // refused as "an unknown format".
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

                // A chunk's body is padded to an even boundary (an odd length gets one
                // byte of padding).
                //
                // ★ position always advances by 8 bytes even for a zero-length chunk, so
                //   this cannot stall. The only way it could is if advance went negative
                //   through an int wrap-around, so we check **that alone** and break out
                //   (no infinite loop).
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

            // Do not trust blockAlign (encoders that write 0, or a lie, really exist).
            // Derive it ourselves from the channel count and the bit depth.
            int frameBytes = bytesPerSample * channels;
            if (frameBytes <= 0) return Failed("the fmt chunk describes a zero-length frame");
            if (blockAlign > 0 && blockAlign != frameBytes)
            {
                // A mismatch is abnormal, but refusing here would throw away audio we can
                // read. Take our own calculation and read on (the value does not go into
                // the diagnostics — the information worth reporting is the sample count).
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
        /// The bytes in one sample. **Unsupported combinations return 0**
        /// (we do not carry on with "probably 2 bytes").
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
                    // A 32 bit float wav can hold values above 1.0.
                    // Without clamping it distorts downstream of SetData.
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
                    // 8 bit PCM is the one that is **unsigned** (0..255, with silence at 128).
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
                        // Sign extension for 24 bit. Forget it and the bottom half of the
                        // waveform is reflected upwards.
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
        /// Reads four little-endian bytes as a float.
        /// <c>BitConverter</c> follows the environment's endianness, so **check that
        /// first** before handing bytes to it (Core does not depend on the engine, so it
        /// does not hard-code the runtime environment).
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
