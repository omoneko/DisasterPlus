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
            float early = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.05f);
            float late = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.15f);
            float later = OuterEdgeAt(TsunamiWaveTrain.TotalSeconds * 0.30f);

            Assert.True(late > early, "the front did not move outward: " + early + " -> " + late);
            Assert.True(later > late, "the front stalled: " + late + " -> " + later);
        }

        [Fact]
        public void TheBiggestCrestIsNotTheFirstOneToArrive()
        {
            // 第 1 波が最大とは限らない（実際の津波でも、避難を解いた人が
            // 第 2 波にさらわれるのが典型的な被害である）。
            //
            // ★★ **「山のある場所」で比べてはいけない。**（テストを 2 度書き直した）
            //    k 本目がこの地点に届くのは <c>k×gap + d/speed</c> なので、
            //    サンプル時刻を gap の倍数で取ると<b>どの波の前線も同じ距離</b>に来る。
            //    比べるべきは位置ではなく<b>その地点が実際に何 m 上がるか</b>である。
            const float Distance = 2400f;
            float travel = Distance / TsunamiWaveTrain.SpeedMetresPerSecond;

            float first = TsunamiWaveTrain.RiseAt(Distance, travel, Amplitude);
            float second = TsunamiWaveTrain.RiseAt(
                Distance, TsunamiWaveTrain.CrestGapSeconds + travel, Amplitude);

            Assert.True(second > first,
                        "the second crest is not the bigger one; that is not how a "
                        + "tsunami behaves (" + first + " m -> " + second + " m)");
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
        public void TheRiseNeverExceedsTheAmplitudeAndThePlateauTogether()
        {
            // 山と台地が重なった高さより上には行かないこと（重ねすぎると海が壁になる）。
            //
            // ★★ **上限は定数から出す。**（テストを 1 度直した）1.35 と直書きして
            //    いたので、台地を足した瞬間に嘘になった。上限は
            //    「いちばん高い山（1.0）＋ 台地」そのものである。
            float ceiling = Amplitude * (1f + TsunamiWaveTrain.PlateauFraction) * 1.02f;

            float worst = 0f;
            for (float t = 0f; t < TsunamiWaveTrain.TotalSeconds; t += 3f)
            {
                for (float d = 0f; d <= 10000f; d += 100f)
                {
                    float r = TsunamiWaveTrain.RiseAt(d, t, Amplitude);
                    if (r > worst) worst = r;
                }
            }

            Assert.True(worst <= ceiling,
                        "the crests stacked to " + worst + " m for a " + Amplitude
                        + " m wave (ceiling " + ceiling + ")");
            Assert.True(worst > Amplitude,
                        "the wave never even reached its own amplitude (" + worst + ")");
        }

        [Fact]
        public void TheSeaStaysLiftedAfterTheCrestsHavePassed()
        {
            // ★★ これが所有者の指摘そのものである:
            //
            //    > 波は減衰していくので、水源をすぐに除去してしまうと
            //    > ただの高潮になってしまっています
            //
            //    山が通り過ぎたあとも、震源のまわりの海は<b>持ち上がったまま</b>で
            //    なければならない。戻ってしまうなら、それは津波ではなく高潮である。
            const float Distance = 1500f;

            // 3 本目が通り過ぎたあと。
            float afterAll = TsunamiWaveTrain.CrestGapSeconds * TsunamiWaveTrain.CrestCount
                             + Distance / TsunamiWaveTrain.SpeedMetresPerSecond
                             + TsunamiWaveTrain.CrestWidthMetres
                               / TsunamiWaveTrain.SpeedMetresPerSecond;

            Assert.True(afterAll < TsunamiWaveTrain.TotalSeconds * TsunamiWaveTrain.FadeFromFraction,
                        "the test window is past the fade; pick an earlier time");

            float rise = TsunamiWaveTrain.RiseAt(Distance, afterAll, Amplitude);

            Assert.True(rise > Amplitude * TsunamiWaveTrain.PlateauFraction * 0.9f,
                        "the sea dropped back to " + rise + " m once the crests passed; "
                        + "that is a storm surge, not a tsunami");
        }

        [Fact]
        public void ThePlateauRisesSmoothlyRatherThanInstantly()
        {
            // 一瞬で上げると水が壁になって岸へ倒れ込む（それは決壊であって津波ではない）。
            float a = TsunamiWaveTrain.PlateauShapeAt(0f, 1f);
            float b = TsunamiWaveTrain.PlateauShapeAt(0f, TsunamiWaveTrain.PlateauRampSeconds * 0.5f);
            float c = TsunamiWaveTrain.PlateauShapeAt(0f, TsunamiWaveTrain.PlateauRampSeconds);

            Assert.True(a < b && b < c, a + " " + b + " " + c);
            Assert.Equal(TsunamiWaveTrain.PlateauFraction, c, 3);
        }

        [Fact]
        public void ThePlateauStopsAtItsEdge()
        {
            float t = TsunamiWaveTrain.PlateauRampSeconds;

            Assert.Equal(TsunamiWaveTrain.PlateauFraction,
                         TsunamiWaveTrain.PlateauShapeAt(0f, t), 3);
            Assert.Equal(0f,
                         TsunamiWaveTrain.PlateauShapeAt(TsunamiWaveTrain.PlateauEdgeMetres, t), 4);
            Assert.Equal(0f,
                         TsunamiWaveTrain.PlateauShapeAt(TsunamiWaveTrain.PlateauEdgeMetres * 2f,
                                                         t), 4);
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

    }
}
