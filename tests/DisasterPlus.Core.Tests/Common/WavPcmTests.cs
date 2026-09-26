using System;
using System.Collections.Generic;
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Pinning <see cref="WavPcm"/>.
    ///
    /// Only two things are protected here:
    ///
    /// 1. **What can be read is read correctly** (whatever the bit depth, channel count or
    ///    chunk order)
    /// 2. **What cannot be read is refused rather than thrown** —— the requirement that the
    ///    volcano still works as it does today even with a broken wav lives on this side.
    /// </summary>
    public class WavPcmTests
    {
        // ── Small helpers for assembling byte arrays ──────────────

        private static void PutAscii(List<byte> to, string s)
        {
            for (int i = 0; i < s.Length; i++) to.Add((byte)s[i]);
        }

        private static void PutInt32(List<byte> to, int v)
        {
            to.Add((byte)(v & 0xFF));
            to.Add((byte)((v >> 8) & 0xFF));
            to.Add((byte)((v >> 16) & 0xFF));
            to.Add((byte)((v >> 24) & 0xFF));
        }

        private static void PutUInt16(List<byte> to, int v)
        {
            to.Add((byte)(v & 0xFF));
            to.Add((byte)((v >> 8) & 0xFF));
        }

        /// <summary>A minimal wav with only fmt + data. <paramref name="extraChunk"/>, if
        /// given, is inserted between them.</summary>
        private static byte[] Build(int format, int channels, int sampleRate, int bits,
                                    byte[] data, string extraChunk = null)
        {
            var body = new List<byte>();
            PutAscii(body, "WAVE");

            PutAscii(body, "fmt ");
            PutInt32(body, 16);
            PutUInt16(body, format);
            PutUInt16(body, channels);
            PutInt32(body, sampleRate);
            PutInt32(body, sampleRate * channels * (bits / 8));   // byteRate
            PutUInt16(body, channels * (bits / 8));               // blockAlign
            PutUInt16(body, bits);

            if (extraChunk != null)
            {
                PutAscii(body, extraChunk);
                PutInt32(body, 3);
                body.Add(1);
                body.Add(2);
                body.Add(3);
                body.Add(0);   // padding for an odd length
            }

            PutAscii(body, "data");
            PutInt32(body, data.Length);
            body.AddRange(data);

            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, body.Count);
            all.AddRange(body);
            return all.ToArray();
        }

        private static byte[] Pcm16(params short[] values)
        {
            var b = new List<byte>();
            for (int i = 0; i < values.Length; i++)
            {
                b.Add((byte)(values[i] & 0xFF));
                b.Add((byte)((values[i] >> 8) & 0xFF));
            }
            return b.ToArray();
        }

        // ── What can be read ──────────────────────────────────

        [Fact]
        public void ReadsSixteenBitStereoAndReportsTheHeaderItActuallyFound()
        {
            byte[] wav = Build(1, 2, 44100, 16, Pcm16(0, 32767, -32768, 1000));
            WavPcm pcm = WavPcm.Parse(wav);

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(44100, pcm.SampleRate);
            Assert.Equal(2, pcm.Channels);
            Assert.Equal(16, pcm.BitsPerSample);
            // 4 samples / 2 channels = 2 frames. lengthSamples is the **frame count**.
            Assert.Equal(2, pcm.FrameCount);
            Assert.Equal(4, pcm.Samples.Length);
        }

        [Fact]
        public void SixteenBitValuesLandOnTheExpectedFloats()
        {
            WavPcm pcm = WavPcm.Parse(Build(1, 1, 8000, 16, Pcm16(0, 32767, -32768, 16384)));

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(0f, pcm.Samples[0], 6);
            Assert.Equal(32767f / 32768f, pcm.Samples[1], 6);
            Assert.Equal(-1f, pcm.Samples[2], 6);
            Assert.Equal(0.5f, pcm.Samples[3], 6);
        }

        [Fact]
        public void EightBitPcmIsUnsignedWithSilenceAtOneTwentyEight()
        {
            // ★ Only 8 bit is unsigned. Unless 128 is read as 0, silence becomes full-scale DC.
            byte[] wav = Build(1, 1, 8000, 8, new byte[] { 128, 255, 0, 192 });
            WavPcm pcm = WavPcm.Parse(wav);

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(0f, pcm.Samples[0], 6);
            Assert.Equal(127f / 128f, pcm.Samples[1], 6);
            Assert.Equal(-1f, pcm.Samples[2], 6);
            Assert.Equal(0.5f, pcm.Samples[3], 6);
        }

        [Fact]
        public void TwentyFourBitIsSignExtended()
        {
            // -1 is 0xFFFFFF. Forget the sign extension and it becomes +0.99999, which
            // flips the bottom half of the waveform back up.
            byte[] data = new byte[] { 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x40 };
            WavPcm pcm = WavPcm.Parse(Build(1, 1, 8000, 24, data));

            Assert.True(pcm.Valid, pcm.Error);
            Assert.True(pcm.Samples[0] < 0f, "0xFFFFFF must decode as a negative sample");
            Assert.Equal(-1f / 8388608f, pcm.Samples[0], 6);
            Assert.Equal(0.5f, pcm.Samples[1], 6);
        }

        [Fact]
        public void ThirtyTwoBitFloatIsClampedIntoTheUnitRange()
        {
            var data = new List<byte>();
            data.AddRange(BitConverter.GetBytes(0.25f));
            data.AddRange(BitConverter.GetBytes(4f));      // values above 1 do occur
            data.AddRange(BitConverter.GetBytes(-9f));
            WavPcm pcm = WavPcm.Parse(Build(3, 1, 48000, 32, data.ToArray()));

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(0.25f, pcm.Samples[0], 6);
            Assert.Equal(1f, pcm.Samples[1], 6);
            Assert.Equal(-1f, pcm.Samples[2], 6);
        }

        [Fact]
        public void ChunksBetweenFmtAndDataAreSkipped()
        {
            // ★ Reading at a hard-coded offset gives **noise, not silence**, for this
            //   shape of file.
            byte[] wav = Build(1, 1, 22050, 16, Pcm16(1000, -1000), "LIST");
            WavPcm pcm = WavPcm.Parse(wav);

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(2, pcm.FrameCount);
            Assert.Equal(1000f / 32768f, pcm.Samples[0], 6);
        }

        [Fact]
        public void LengthSecondsFollowsFrameCountAndRate()
        {
            WavPcm pcm = WavPcm.Parse(Build(1, 2, 100, 16, Pcm16(new short[400])));

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(200, pcm.FrameCount);
            Assert.Equal(2f, pcm.LengthSeconds, 4);
        }

        [Fact]
        public void ATruncatedDataChunkReadsTheFramesThatAreActuallyThere()
        {
            // A file whose data chunk claims "there are 1024 bytes" but only contains 4
            // (which is what you get when a recording is cut off part-way). Read what is
            // there rather than refusing.
            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, 4 + 24 + 8 + 4);
            PutAscii(all, "WAVE");
            PutAscii(all, "fmt ");
            PutInt32(all, 16);
            PutUInt16(all, 1);
            PutUInt16(all, 1);
            PutInt32(all, 8000);
            PutInt32(all, 16000);
            PutUInt16(all, 2);
            PutUInt16(all, 16);
            PutAscii(all, "data");
            PutInt32(all, 1024);
            all.AddRange(Pcm16(100, -100));

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(2, pcm.FrameCount);
        }

        [Fact]
        public void ALyingBlockAlignDoesNotMoveTheSampleBoundaries()
        {
            // Encoders that write 0 into blockAlign do exist. Believe it and you get either
            // a division by zero or wildly wrong values.
            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, 4 + 24 + 8 + 4);
            PutAscii(all, "WAVE");
            PutAscii(all, "fmt ");
            PutInt32(all, 16);
            PutUInt16(all, 1);
            PutUInt16(all, 1);
            PutInt32(all, 8000);
            PutInt32(all, 16000);
            PutUInt16(all, 0);      // a lying blockAlign
            PutUInt16(all, 16);
            PutAscii(all, "data");
            PutInt32(all, 4);
            all.AddRange(Pcm16(2048, -2048));

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(2, pcm.FrameCount);
            Assert.Equal(2048f / 32768f, pcm.Samples[0], 6);
        }

        [Fact]
        public void ExtensibleFormatIsResolvedThroughItsSubFormat()
        {
            // Without looking at WAVE_FORMAT_EXTENSIBLE (0xFFFE), ordinary PCM is refused
            // as an unknown format.
            var body = new List<byte>();
            PutAscii(body, "WAVE");
            PutAscii(body, "fmt ");
            PutInt32(body, 40);
            PutUInt16(body, 0xFFFE);
            PutUInt16(body, 1);
            PutInt32(body, 44100);
            PutInt32(body, 88200);
            PutUInt16(body, 2);
            PutUInt16(body, 16);
            PutUInt16(body, 22);       // cbSize
            PutUInt16(body, 16);       // validBitsPerSample
            PutInt32(body, 4);         // channelMask
            PutUInt16(body, 1);        // first 2 bytes of the SubFormat GUID = PCM
            for (int i = 0; i < 14; i++) body.Add(0);
            PutAscii(body, "data");
            PutInt32(body, 4);
            body.AddRange(Pcm16(4096, -4096));

            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, body.Count);
            all.AddRange(body);

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(16, pcm.BitsPerSample);
            Assert.Equal(2, pcm.FrameCount);
        }

        // ── What cannot be read (**none of it may throw**) ─────────────

        [Fact]
        public void NullBytesAreRefusedWithoutThrowing()
        {
            WavPcm pcm = WavPcm.Parse(null);
            Assert.False(pcm.Valid);
            Assert.False(string.IsNullOrEmpty(pcm.Error));
            Assert.Empty(pcm.Samples);
        }

        [Fact]
        public void AnEmptyFileIsRefusedWithoutThrowing()
        {
            WavPcm pcm = WavPcm.Parse(new byte[0]);
            Assert.False(pcm.Valid);
            Assert.Empty(pcm.Samples);
        }

        [Fact]
        public void AnMp3IsRefusedRatherThanReadAsNoise()
        {
            // A different format with nothing but the extension changed to .wav.
            // **Do not carry on reading with plausible-looking defaults.**
            byte[] mp3 = new byte[] { 0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            WavPcm pcm = WavPcm.Parse(mp3);
            Assert.False(pcm.Valid);
            Assert.Contains("RIFF", pcm.Error);
        }

        [Fact]
        public void BigEndianRifxIsNamedRatherThanMisread()
        {
            var all = new List<byte>();
            PutAscii(all, "RIFX");
            PutInt32(all, 4);
            PutAscii(all, "WAVE");
            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.False(pcm.Valid);
            Assert.Contains("RIFX", pcm.Error);
        }

        [Fact]
        public void AFileWithoutADataChunkIsRefused()
        {
            var body = new List<byte>();
            PutAscii(body, "WAVE");
            PutAscii(body, "fmt ");
            PutInt32(body, 16);
            PutUInt16(body, 1);
            PutUInt16(body, 1);
            PutInt32(body, 8000);
            PutInt32(body, 16000);
            PutUInt16(body, 2);
            PutUInt16(body, 16);

            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, body.Count);
            all.AddRange(body);

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.False(pcm.Valid);
            Assert.Contains("data", pcm.Error);
        }

        [Fact]
        public void AFileWithoutAFmtChunkIsRefused()
        {
            var body = new List<byte>();
            PutAscii(body, "WAVE");
            PutAscii(body, "data");
            PutInt32(body, 4);
            body.AddRange(Pcm16(1, 2));

            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, body.Count);
            all.AddRange(body);

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.False(pcm.Valid);
            Assert.Contains("fmt", pcm.Error);
        }

        [Fact]
        public void CompressedWavEncodingsAreNamedRatherThanGuessed()
        {
            // format 17 = IMA ADPCM. Read as PCM it **plays noise at full volume**.
            WavPcm pcm = WavPcm.Parse(Build(17, 1, 8000, 4, new byte[] { 1, 2, 3, 4 }));
            Assert.False(pcm.Valid);
            Assert.Contains("unsupported", pcm.Error);
        }

        [Fact]
        public void ZeroChannelsIsRefused()
        {
            WavPcm pcm = WavPcm.Parse(Build(1, 0, 44100, 16, Pcm16(1, 2)));
            Assert.False(pcm.Valid);
        }

        [Fact]
        public void AnEmptyDataChunkIsRefused()
        {
            WavPcm pcm = WavPcm.Parse(Build(1, 2, 44100, 16, new byte[0]));
            Assert.False(pcm.Valid);
        }

        [Fact]
        public void ANegativeChunkLengthIsRefusedRatherThanIndexed()
        {
            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, 32);
            PutAscii(all, "WAVE");
            PutAscii(all, "fmt ");
            PutInt32(all, -16);
            for (int i = 0; i < 16; i++) all.Add(0);

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            Assert.False(pcm.Valid);
        }

        [Fact]
        public void AHeaderThatClaimsMoreSamplesThanTheCapIsRefusedBeforeAllocating()
        {
            // Allocating on the header's number alone means one broken file takes the whole
            // game down with it.
            var all = new List<byte>();
            PutAscii(all, "RIFF");
            PutInt32(all, 36);
            PutAscii(all, "WAVE");
            PutAscii(all, "fmt ");
            PutInt32(all, 16);
            PutUInt16(all, 1);
            PutUInt16(all, 1);
            PutInt32(all, 44100);
            PutInt32(all, 88200);
            PutUInt16(all, 2);
            PutUInt16(all, 16);
            PutAscii(all, "data");
            PutInt32(all, int.MaxValue);
            // The body only holds 4 bytes, so only 2 frames can really be read.
            all.AddRange(Pcm16(1, 2));

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            // It is decided by the **bytes that actually exist**, not by the declaration,
            // so this one reads.
            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(2, pcm.FrameCount);
        }

        [Fact]
        public void TheCapIsSmallEnoughToKeepTheAllocationBounded()
        {
            // At 4 bytes per sample, the cap itself is the bound on how much is allocated.
            Assert.True(WavPcm.MaxTotalSamples <= 32000000,
                        "the sample cap must stay small enough to bound the float[] allocation");
        }

        [Fact]
        public void FailedCarriesTheReasonAndAnEmptySampleArray()
        {
            WavPcm pcm = WavPcm.Failed("because");
            Assert.False(pcm.Valid);
            Assert.Equal("because", pcm.Error);
            Assert.Empty(pcm.Samples);
            Assert.Equal(0f, pcm.LengthSeconds);
        }
    }
}
