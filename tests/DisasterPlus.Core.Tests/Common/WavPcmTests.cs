using System;
using System.Collections.Generic;
using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// <see cref="WavPcm"/> の固定。
    ///
    /// ここが守っているのは 2 つだけである:
    ///
    /// 1. **読めたものは正しく読める**（ビット数・チャンネル数・チャンクの並びを変えても）
    /// 2. **読めないものは投げずに断る** —— 火山は wav が壊れていても今日どおり動く、
    ///    という要件の実体がこちら側にある。
    /// </summary>
    public class WavPcmTests
    {
        // ── バイト列を組み立てる小道具 ──────────────────────────

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

        /// <summary>fmt ＋ data だけの最小 wav。<paramref name="extraChunk"/> があれば間に挟む。</summary>
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
                body.Add(0);   // 奇数長のパディング
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

        // ── 読めるもの ───────────────────────────────────

        [Fact]
        public void ReadsSixteenBitStereoAndReportsTheHeaderItActuallyFound()
        {
            byte[] wav = Build(1, 2, 44100, 16, Pcm16(0, 32767, -32768, 1000));
            WavPcm pcm = WavPcm.Parse(wav);

            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(44100, pcm.SampleRate);
            Assert.Equal(2, pcm.Channels);
            Assert.Equal(16, pcm.BitsPerSample);
            // 4 サンプル ÷ 2 チャンネル = 2 フレーム。lengthSamples は**フレーム数**である。
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
            // ★ 8 bit だけ符号なし。128 を 0 と読まないと、無音が全開の直流になる。
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
            // -1 は 0xFFFFFF。符号拡張を忘れると +0.99999 になり、波形の下半分が跳ね返る。
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
            data.AddRange(BitConverter.GetBytes(4f));      // 1 を超える値は実在する
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
            // ★ 決め打ちのオフセットで読むと、この形のファイルで**無音ではなく雑音**になる。
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
            // data が「1024 バイトある」と名乗っているが 4 バイトしか入っていないファイル
            // （録音が途中で切れるとこうなる）。断らずに在るぶんだけ読む。
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
            // blockAlign に 0 を書くエンコーダが実在する。信じると 0 除算か大化けする。
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
            PutUInt16(all, 0);      // 嘘の blockAlign
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
            // WAVE_FORMAT_EXTENSIBLE(0xFFFE) を見ないと、ふつうの PCM を未知の形式と断る。
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
            PutUInt16(body, 1);        // SubFormat GUID の先頭 2 バイト = PCM
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

        // ── 読めないもの（**どれも投げてはいけない**）────────────────

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
            // 拡張子を .wav に変えただけの別形式。**それらしい既定値で読み進めない。**
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
            // format 17 = IMA ADPCM。PCM として読むと**雑音を全開で鳴らす**。
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
            // ヘッダの数字だけを信じて確保すると、壊れたファイル 1 個でゲームごと落ちる。
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
            // 本体は 4 バイトしか無いので、実際に読めるのは 2 フレームだけ。
            all.AddRange(Pcm16(1, 2));

            WavPcm pcm = WavPcm.Parse(all.ToArray());
            // 宣言ではなく**実在するバイト数**で決まるので、これは読める。
            Assert.True(pcm.Valid, pcm.Error);
            Assert.Equal(2, pcm.FrameCount);
        }

        [Fact]
        public void TheCapIsSmallEnoughToKeepTheAllocationBounded()
        {
            // 4 バイト／サンプルなので、上限そのものが確保量の上限である。
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
