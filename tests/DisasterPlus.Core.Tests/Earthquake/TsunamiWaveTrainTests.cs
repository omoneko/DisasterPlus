using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 所有者の指示（2026-08-25）「震源地を中心に２-3 個の連続する山状：
    /// 実際の津波メカニズムで」。
    ///
    /// ★★ いちばん大事なのは<b>終わりに 0 へ戻ること</b>である。
    ///    戻らないと、呼び出し側が水位を下げる合図を受け取れず、
    ///    <b>持ち上げた海がセーブに残る</b>。
    /// </summary>
    public class TsunamiWaveTrainTests
    {
        private const float Amplitude = 6f;

        [Fact]
        public void TheWaveTravelsOutwardFromTheEpicentre()
        {
            // ★★ **いちばん高い点を追ってはいけない。**（テストを 1 度書き直した）
            //    第 2 波のほうが高いので、それが生まれた瞬間に「最大の山」は
            //    <b>内側へ跳ぶ</b>。それはモデルが正しい証拠であって、
            //    波が戻っているわけではない。
            //
            //    外へ広がることを見るなら<b>いちばん外の波がどこまで届いたか</b>で
            //    見る。こちらは単調でなければならない。
            float early = OuterEdgeAt(30f);
            float late = OuterEdgeAt(80f);
            float later = OuterEdgeAt(140f);

            Assert.True(late > early, "the front did not move outward: " + early + " -> " + late);
            Assert.True(later > late, "the front stalled: " + late + " -> " + later);
        }

        [Fact]
        public void TheBiggestCrestIsNotTheFirstOneToArrive()
        {
            // 第 1 波が最大とは限らない（実際の津波でも、避難を解いた人が
            // 第 2 波にさらわれるのが典型的な被害である）。
            float first = PeakDistanceAt(30f);
            float second = PeakDistanceAt(80f);

            Assert.True(second < first,
                        "the second crest is not the bigger one; that is not how a "
                        + "tsunami behaves (" + first + " -> " + second + ")");
        }

        [Fact]
        public void ThereAreSeveralCrestsNotOne()
        {
            // ★★ 「2-3 個の連続する山状」。ある地点で、時間とともに山が複数回来ること。
            const float Distance = 2500f;

            int peaks = 0;
            float previous = 0f;
            bool rising = false;

            for (int i = 0; i <= 800; i++)
            {
                float t = TsunamiWaveTrain.TotalSeconds * i / 800f;
                float r = TsunamiWaveTrain.RiseAt(Distance, t, Amplitude);

                if (r > previous + 1e-4f) rising = true;
                else if (rising && r < previous - 1e-4f) { peaks++; rising = false; }

                previous = r;
            }

            Assert.InRange(peaks, 2, TsunamiWaveTrain.CrestCount);
        }

        [Fact]
        public void TheSecondCrestIsTheBiggest()
        {
            // 実際の津波でも第 1 波が最大とは限らない。
            Assert.True(TsunamiWaveTrain.CrestWeights[1] > TsunamiWaveTrain.CrestWeights[0],
                        "the first crest is the biggest; that is not how a tsunami behaves");
            Assert.True(TsunamiWaveTrain.CrestWeights[1] > TsunamiWaveTrain.CrestWeights[2]);
        }

        [Fact]
        public void TheSeaAlwaysGoesBackDown()
        {
            // ★★ **ここが破れると海が戻らない。**
            for (float d = 0f; d <= 6000f; d += 250f)
            {
                Assert.Equal(0f, TsunamiWaveTrain.RiseAt(d, TsunamiWaveTrain.TotalSeconds,
                                                         Amplitude), 4);
                Assert.Equal(0f, TsunamiWaveTrain.RiseAt(d, TsunamiWaveTrain.TotalSeconds + 60f,
                                                         Amplitude), 4);
            }

            Assert.Equal(0f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds), 4);
        }

        [Fact]
        public void TheEnvelopeFadesRatherThanStopping()
        {
            // 急に 0 になると水面が段差で落ちる。終わりに向かってなだらかに下がること。
            float a = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.80f);
            float b = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.90f);
            float c = TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.99f);

            Assert.True(a > b && b > c, a + " " + b + " " + c);
            Assert.True(c < 0.1f);
        }

        [Fact]
        public void TheRiseNeverExceedsTheAmplitude()
        {
            // 3 本の山が重なっても、頼んだ高さより大きく持ち上げないこと
            // （重ねすぎると海が壁になる）。
            float worst = 0f;
            for (float t = 0f; t < TsunamiWaveTrain.TotalSeconds; t += 3f)
            {
                for (float d = 0f; d <= 8000f; d += 100f)
                {
                    float r = TsunamiWaveTrain.RiseAt(d, t, Amplitude);
                    if (r > worst) worst = r;
                }
            }

            Assert.True(worst <= Amplitude * 1.35f,
                        "the crests stacked to " + worst + " m for a " + Amplitude + " m wave");
            Assert.True(worst > Amplitude * 0.6f,
                        "the wave never even reached " + (Amplitude * 0.6f) + " m");
        }

        [Fact]
        public void TheRiseIsNeverNegative()
        {
            // 引き波はやらない（地面より下へ水位を下げると、乾いた海底が残る）。
            for (float t = 0f; t < TsunamiWaveTrain.TotalSeconds; t += 7f)
            {
                for (float d = 0f; d <= 8000f; d += 250f)
                {
                    Assert.True(TsunamiWaveTrain.RiseAt(d, t, Amplitude) >= 0f);
                }
            }
        }

        [Fact]
        public void AStrongerQuakeMakesAHigherWave()
        {
            Assert.True(TsunamiWaveTrain.AmplitudeOf(255) > TsunamiWaveTrain.AmplitudeOf(100));
            Assert.InRange(TsunamiWaveTrain.AmplitudeOf(255),
                           TsunamiWaveTrain.MinAmplitudeMetres,
                           TsunamiWaveTrain.MaxAmplitudeMetres);

            // 弱い地震でも津波と呼べる高さは出す。
            Assert.True(TsunamiWaveTrain.AmplitudeOf(1) >= TsunamiWaveTrain.MinAmplitudeMetres);
        }

        [Fact]
        public void TheFrontTellsTheCallerHowFarToLook()
        {
            Assert.Equal(0f, TsunamiWaveTrain.FrontRadiusAt(0f), 4);
            Assert.True(TsunamiWaveTrain.FrontRadiusAt(60f)
                        > TsunamiWaveTrain.FrontRadiusAt(30f));
        }

        [Fact]
        public void BrokenInputRaisesNoWater()
        {
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(float.NaN, 10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, float.NaN, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 10f, float.NaN), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(-100f, 10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, -10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 10f, 0f), 4);
        }

        /// <summary>その時刻に波が届いているいちばん外の距離（m）。</summary>
        private static float OuterEdgeAt(float seconds)
        {
            float edge = 0f;
            for (float d = 0f; d <= 20000f; d += 25f)
            {
                if (TsunamiWaveTrain.RiseAt(d, seconds, Amplitude) > 0.05f) edge = d;
            }
            return edge;
        }

        /// <summary>その時刻でいちばん高く持ち上がっている距離（m）。</summary>
        private static float PeakDistanceAt(float seconds)
        {
            float best = 0f;
            float at = 0f;

            for (float d = 0f; d <= 9000f; d += 25f)
            {
                float r = TsunamiWaveTrain.RiseAt(d, seconds, Amplitude);
                if (r > best) { best = r; at = d; }
            }

            return at;
        }
    }
}
