using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WaveformBufferTests
    {
        [Fact]
        public void StartsEmpty()
        {
            var b = new WaveformBuffer(8);
            Assert.Equal(8, b.Capacity);
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void KeepsSamplesInChronologicalOrder()
        {
            var b = new WaveformBuffer(8);
            for (uint f = 0; f < 5; f++) b.Add(f, f * 0.5f);

            var frames = new uint[8];
            var values = new float[8];
            int n = b.CopyTo(frames, values);

            Assert.Equal(5, n);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal((uint)i, frames[i]);
                Assert.Equal(i * 0.5f, values[i], 4);
            }
        }

        [Fact]
        public void WrapsAroundAndDropsTheOldest()
        {
            var b = new WaveformBuffer(4);
            for (uint f = 0; f < 10; f++) b.Add(f, f);

            var frames = new uint[4];
            var values = new float[4];
            int n = b.CopyTo(frames, values);

            Assert.Equal(4, n);
            Assert.Equal(4, b.Count);
            // 直近 4 件（6,7,8,9）が古い順に並ぶこと。
            Assert.Equal(6u, frames[0]);
            Assert.Equal(9u, frames[3]);
            Assert.Equal(6u, b.OldestFrame);
            Assert.Equal(9u, b.NewestFrame);
        }

        [Fact]
        public void CopyToSmallerArraysDoesNotOverflow()
        {
            var b = new WaveformBuffer(8);
            for (uint f = 0; f < 8; f++) b.Add(f, f);

            var frames = new uint[3];
            var values = new float[3];
            int n = b.CopyTo(frames, values);

            // 落ちないこと、書いた件数を正直に返すこと。
            Assert.Equal(3, n);
        }

        [Fact]
        public void ClearResetsEverything()
        {
            var b = new WaveformBuffer(4);
            b.Add(1u, 1f);
            b.Clear();
            Assert.Equal(0, b.Count);
            Assert.Equal(0f, b.PeakAbsolute, 4);
        }

        [Fact]
        public void PeakTracksTheLargestMagnitudeHeld()
        {
            var b = new WaveformBuffer(4);
            b.Add(0u, 0.2f);
            b.Add(1u, -0.9f);
            b.Add(2u, 0.5f);
            Assert.Equal(0.9f, b.PeakAbsolute, 4);
        }

        [Fact]
        public void RejectsNonsenseCapacity()
        {
            // 0 や負の容量でも落ちない。最低 1 件は持つ。
            var b = new WaveformBuffer(0);
            b.Add(1u, 1f);
            Assert.True(b.Capacity >= 1);
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void PeakForgetsSamplesThatHaveFallenOutOfTheBuffer()
        {
            // Add() で最大値を更新して覚えっぱなしにすると、もう保持していない
            // サンプルの振幅を「今の最大振幅」として出し続ける嘘になる。
            var b = new WaveformBuffer(2);
            b.Add(0u, 5f);
            b.Add(1u, 0.1f);
            b.Add(2u, 0.2f);

            Assert.Equal(2, b.Count);
            Assert.Equal(0.2f, b.PeakAbsolute, 4);
        }

        [Fact]
        public void NeverReadsItsUnfilledRegion()
        {
            // 未使用の領域は 0 で初期化されているので、「読んでしまった」ことは
            // 値では見えない。件数と最古・最新フレームで縛る。
            var b = new WaveformBuffer(16);
            b.Add(1000u, 0.5f);

            var frames = new uint[16];
            var values = new float[16];
            int n = b.CopyTo(frames, values);

            Assert.Equal(1, n);
            Assert.Equal(1000u, b.OldestFrame);
            Assert.Equal(1000u, b.NewestFrame);
            Assert.Equal(0.5f, b.PeakAbsolute, 4);
        }
    }
}
