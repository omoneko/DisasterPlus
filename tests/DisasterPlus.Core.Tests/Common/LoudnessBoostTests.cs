using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// 所有者の依頼（2026-08-22）「噴火の音が…もう倍くらいの音量に」。
    /// 素材のピークは既に -1.55 dBFS なので、**素直に 2 倍すると割れる**。
    /// ここが固定するのは「小さい音は厳密に 2 倍、ピークは割れない」の 2 点である。
    /// </summary>
    public class LoudnessBoostTests
    {
        /// <summary>同梱 erupting-volcano.wav の実測（peak 27412/32767、RMS 5330/32767）。</summary>
        private const float MaterialPeak = 0.8366f;
        private const float MaterialRms = 0.1627f;

        [Fact]
        public void QuietSamplesAreMultipliedExactly()
        {
            // RMS のあたりは膝より遥かに下なので、厳密に gain 倍でなければならない。
            Assert.Equal(MaterialRms * 2f, LoudnessBoost.Shape(MaterialRms, 2f), 5);
            Assert.Equal(-MaterialRms * 2f, LoudnessBoost.Shape(-MaterialRms, 2f), 5);

            // 膝のちょうど手前まで線形である。
            float justBelow = LoudnessBoost.Threshold / 2f;
            Assert.Equal(LoudnessBoost.Threshold, LoudnessBoost.Shape(justBelow, 2f), 5);
        }

        [Fact]
        public void TheMaterialPeakStopsShortOfClipping()
        {
            float shaped = LoudnessBoost.Shape(MaterialPeak, 2f);

            Assert.True(shaped < 1f, "the peak must not reach full scale, got " + shaped);
            Assert.True(shaped > 0.95f, "the peak should still be loud, got " + shaped);
        }

        [Fact]
        public void NothingEverLeavesTheLegalRange()
        {
            // 素材が差し替えられて既に割れかけていても、出力は必ず [-1,1] に収まる。
            foreach (float g in new[] { 1.5f, 2f, 4f, 100f })
            {
                for (int i = -20; i <= 20; i++)
                {
                    float x = i / 20f;
                    float y = LoudnessBoost.Shape(x, g);
                    Assert.InRange(y, -1f, 1f);
                }
            }
        }

        [Fact]
        public void TheSignIsAlwaysKept()
        {
            Assert.True(LoudnessBoost.Shape(0.9f, 2f) > 0f);
            Assert.True(LoudnessBoost.Shape(-0.9f, 2f) < 0f);
            Assert.Equal(0f, LoudnessBoost.Shape(0f, 2f), 6);
        }

        [Fact]
        public void LouderInputStaysLouderOutput()
        {
            // 膝の中でも順序が入れ替わらない（入れ替わると波形が壊れる）。
            float previous = -1f;
            for (int i = 0; i <= 100; i++)
            {
                float y = LoudnessBoost.Shape(i / 100f, 2f);
                Assert.True(y >= previous, "must not go down at x=" + (i / 100f));
                previous = y;
            }
        }

        [Fact]
        public void AGainOfOneOrLessChangesNothing()
        {
            // 「小さくする」用途はこの型に無い。黙って縮めない。
            Assert.Equal(0.4f, LoudnessBoost.Shape(0.4f, 1f), 6);
            Assert.Equal(0.4f, LoudnessBoost.Shape(0.4f, 0.5f), 6);

            var samples = new[] { 0.4f, -0.4f };
            LoudnessBoost.Apply(samples, 1f);
            Assert.Equal(0.4f, samples[0], 6);
            Assert.Equal(-0.4f, samples[1], 6);
        }

        [Fact]
        public void BrokenSamplesBecomeSilenceInsteadOfPoisoningTheClip()
        {
            Assert.Equal(0f, LoudnessBoost.Shape(float.NaN, 2f), 6);
            Assert.Equal(0f, LoudnessBoost.Shape(float.PositiveInfinity, 2f), 6);
        }

        [Fact]
        public void ApplyWritesInPlaceAndReportsThePeak()
        {
            var samples = new[] { 0.1f, -0.2f, MaterialPeak, 0.05f };
            float peak = LoudnessBoost.Apply(samples, 2f);

            Assert.Equal(0.2f, samples[0], 5);
            Assert.Equal(-0.4f, samples[1], 5);
            Assert.Equal(peak, LoudnessBoost.PeakOf(samples), 5);
            Assert.True(peak < 1f);
        }

        [Fact]
        public void AnEmptyOrMissingBufferIsNotAnError()
        {
            Assert.Equal(0f, LoudnessBoost.Apply(null, 2f), 6);
            Assert.Equal(0f, LoudnessBoost.Apply(new float[0], 2f), 6);
            Assert.Equal(0f, LoudnessBoost.PeakOf(null), 6);
        }
    }
}
