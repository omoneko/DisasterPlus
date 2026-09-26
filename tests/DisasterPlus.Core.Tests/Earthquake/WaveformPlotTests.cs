using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class WaveformPlotTests
    {
        private static void Fill(out uint[] frames, out float[] values, int n, uint step)
        {
            frames = new uint[n];
            values = new float[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = (uint)(i * step);
                values[i] = 0f;
            }
        }

        [Fact]
        public void ReturnsOneEntryPerColumn()
        {
            uint[] f; float[] v;
            Fill(out f, out v, 100, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 100, 0u, 99u, width: 32, height: 20, scale: 1f);
            Assert.Equal(32, cols.Length);
        }

        [Fact]
        public void EmptyColumnsAreMarkedMinusOne()
        {
            // Drawing a column no sample has reached as "value 0" makes it look like there
            // is no shaking at all.
            uint[] f; float[] v;
            Fill(out f, out v, 2, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 2, 0u, 1000u, width: 16, height: 20, scale: 1f);

            int empty = 0;
            foreach (int c in cols) if (c < 0) empty++;
            Assert.True(empty > 10, "expected mostly empty columns, got " + empty);
        }

        [Fact]
        public void BucketsByFrameNotByIndex()
        {
            // The time axis must not stretch or shrink when the game speed changes the
            // sample interval. Fill the same frame range densely and sparsely, then compare.
            uint[] dense; float[] dv;
            Fill(out dense, out dv, 64, 1u);            // frame 0..63
            uint[] sparse; float[] sv;
            Fill(out sparse, out sv, 8, 9u);            // frame 0..63 (in steps of 9 frames)

            int[] a = WaveformPlot.Columns(dense, dv, 64, 0u, 63u, 8, 10, 1f);
            int[] b = WaveformPlot.Columns(sparse, sv, 8, 0u, 63u, 8, 10, 1f);

            // Both fill every column (= the time axis spans the same range).
            foreach (int c in a) Assert.True(c >= 0);
            foreach (int c in b) Assert.True(c >= 0);
        }

        [Fact]
        public void ZeroValueMapsToTheMiddleRow()
        {
            uint[] f; float[] v;
            Fill(out f, out v, 32, 1u);
            int[] cols = WaveformPlot.Columns(f, v, 32, 0u, 31u, width: 8, height: 20, scale: 1f);
            foreach (int c in cols) Assert.Equal(10, c);
        }

        [Fact]
        public void ValuesAreClampedToTheHeight()
        {
            uint[] f = { 0u, 1u };
            float[] v = { 999f, -999f };
            int[] cols = WaveformPlot.Columns(f, v, 2, 0u, 1u, width: 2, height: 20, scale: 1f);
            foreach (int c in cols) Assert.InRange(c, 0, 20);
        }

        [Fact]
        public void GarbageInputDoesNotThrow()
        {
            Assert.Equal(4, WaveformPlot.Columns(null, null, 0, 0u, 10u, 4, 10, 1f).Length);
            uint[] f = { 0u };
            float[] v = { float.NaN };
            int[] cols = WaveformPlot.Columns(f, v, 1, 0u, 0u, 4, 10, 0f);
            Assert.Equal(4, cols.Length);
        }

        [Fact]
        public void SameColumnKeepsTheLargestMagnitude()
        {
            // Several samples landing in one column always happens at game speed 1, when
            // they are denser than the window. Taking the last one makes the shaking vanish
            // in whichever column happens to land on a zero crossing.
            uint[] f = { 0u, 1u, 2u, 3u };
            float[] v = { 0.1f, -0.9f, 0.2f, 0f };
            int[] cols = WaveformPlot.Columns(f, v, 4, 0u, 3u, width: 1, height: 20, scale: 1f);

            Assert.Single(cols);
            // -0.9 → row = 10 - round(-0.9 * 10) = 10 + 9 = 19
            Assert.Equal(19, cols[0]);
        }

        [Fact]
        public void SamplesOutsideTheWindowAreIgnored()
        {
            // The ring buffer can hold more history than the window. Pushing samples from
            // outside the window into the end column turns that one column into a pillar of
            // piled-up old shaking.
            uint[] f = { 0u, 50u, 100u, 500u };
            float[] v = { 1f, 0f, 0f, 1f };
            int[] cols = WaveformPlot.Columns(f, v, 4, 50u, 100u, width: 2, height: 20, scale: 1f);

            Assert.Equal(2, cols.Length);
            // Both samples inside the window are 0, so the middle row. If the 1.0 from
            // outside the window crept in, it would be row 0.
            Assert.Equal(10, cols[0]);
            Assert.Equal(10, cols[1]);
        }
    }
}
