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
            // サンプルが届いていない列を「値 0」として描くと、揺れていないように見える。
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
            // ゲーム速度でサンプル間隔が変わっても、時間軸が伸び縮みしないこと。
            // 同じフレーム範囲を、密なサンプルと疎なサンプルで埋めて比べる。
            uint[] dense; float[] dv;
            Fill(out dense, out dv, 64, 1u);            // frame 0..63
            uint[] sparse; float[] sv;
            Fill(out sparse, out sv, 8, 9u);            // frame 0..63（9 フレーム刻み）

            int[] a = WaveformPlot.Columns(dense, dv, 64, 0u, 63u, 8, 10, 1f);
            int[] b = WaveformPlot.Columns(sparse, sv, 8, 0u, 63u, 8, 10, 1f);

            // どちらも全列が埋まる（＝時間軸の広がりが同じ）。
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
            // 1 列に複数サンプルが落ちるのは、ゲーム速度 1 で窓より密なとき常に起きる。
            // 最後の 1 個を採ると、たまたま零交差に当たった列で揺れが消える。
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
            // リングバッファは窓より長い履歴を持ちうる。窓の外のサンプルを
            // 端の列へ寄せると、そこだけ古い揺れが積み上がった柱になる。
            uint[] f = { 0u, 50u, 100u, 500u };
            float[] v = { 1f, 0f, 0f, 1f };
            int[] cols = WaveformPlot.Columns(f, v, 4, 50u, 100u, width: 2, height: 20, scale: 1f);

            Assert.Equal(2, cols.Length);
            // 窓内の 2 件はどちらも 0 なので中央行。窓外の 1.0 が混ざれば 0 行になる。
            Assert.Equal(10, cols[0]);
            Assert.Equal(10, cols[1]);
        }
    }
}
