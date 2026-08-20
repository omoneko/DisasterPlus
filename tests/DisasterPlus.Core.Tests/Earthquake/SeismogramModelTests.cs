using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 合成記象（P 波・S 波・コーダ）を固定する。
    ///
    /// **いちばん重要なのは 3 件:**
    ///   - <see cref="ShapeNeverLeavesMinusOneToOne"/> —— 満目盛りが
    ///     <c>ShakeWaveform.MaxDisplacement</c> のままであることの根拠。
    ///     これが崩れると、バニラの線と並べたときの縦の尺度が嘘になる。
    ///   - <see cref="ArrivalsSeparateWithDistance"/> —— このモデルの看板。
    ///     初期微動継続時間が距離とともに開かなくなったら、絵が
    ///     「それらしいだけ」に落ちる。
    ///   - <see cref="EveryCarrierStaysWellBelowNyquist"/> —— 折り返し防止。
    ///     速い成分を足すと、第 1 層のグラフに第 2 層（長周期地震動）と
    ///     見分けの付かない偽の波が出る（全体レビュー I5 の再発）。
    /// </summary>
    public class SeismogramModelTests
    {
        private const uint Window = 1024u;

        private static SeismogramModel Model(uint seed)
        {
            return SeismogramModel.For(DeterministicRandom.Hash(seed, 777u), Window);
        }

        [Fact]
        public void ShortOrMissingWindowProducesNothing()
        {
            // m_activeDuration が読めていない（0）ときに勝手な窓を決め打ちしない。
            Assert.False(SeismogramModel.For(1u, 0u).Valid);
            Assert.False(SeismogramModel.For(1u, SeismogramModel.MinWindowFrames - 1u).Valid);
            Assert.Equal(0f, SeismogramModel.For(1u, 0u).DisplacementAt(1000f, 50f));
            Assert.Equal(0f, SeismogramModel.For(1u, 0u).SMinusPFrames(1000f));
        }

        [Fact]
        public void ShapeNeverLeavesMinusOneToOne()
        {
            for (uint seed = 0; seed < 24; seed++)
            {
                var model = Model(seed);
                for (float d = 0f; d <= 12000f; d += 750f)
                {
                    for (int t = 0; t <= (int)Window; t += 3)
                    {
                        float shape = model.ShapeAt(d, t);
                        Assert.InRange(shape, -1f, 1f);
                    }
                }
            }
        }

        [Fact]
        public void DisplacementSharesVanillaFullScale()
        {
            // 距離 0 の理論最大がちょうど MaxDisplacement（0.60）。バニラの波形と
            // 同じ満目盛りで描けることの根拠。
            for (uint seed = 0; seed < 12; seed++)
            {
                var model = Model(seed);
                for (float d = 0f; d <= 12000f; d += 1500f)
                {
                    float ceiling = 2f * ShakeWaveform.PeakAmplitudeAt(d);
                    for (int t = 0; t <= (int)Window; t += 5)
                    {
                        float v = model.DisplacementAt(d, t);
                        Assert.InRange(v, -ceiling - 1e-5f, ceiling + 1e-5f);
                        Assert.InRange(v, -ShakeWaveform.MaxDisplacement - 1e-5f,
                                       ShakeWaveform.MaxDisplacement + 1e-5f);
                    }
                }
            }
        }

        [Fact]
        public void NothingOutsideTheVanillaWindow()
        {
            var model = Model(3u);
            Assert.Equal(0f, model.DisplacementAt(1000f, 0f));
            Assert.Equal(0f, model.DisplacementAt(1000f, -5f));
            Assert.Equal(0f, model.DisplacementAt(1000f, Window));
            Assert.Equal(0f, model.DisplacementAt(1000f, Window + 100f));
        }

        [Fact]
        public void QuietBeforeThePWaveArrives()
        {
            var model = Model(5u);
            const float distance = 6000f;
            float tP = model.PArrivalFrames(distance);
            Assert.True(tP > 10f, "the P arrival must be measurably later than the window opening");

            for (float t = 1f; t < tP; t += 1f)
            {
                Assert.Equal(0f, model.DisplacementAt(distance, t));
            }
        }

        [Fact]
        public void ArrivalsSeparateWithDistance()
        {
            var model = Model(7u);

            float previous = -1f;
            for (float d = 0f; d <= 8000f; d += 500f)
            {
                float gap = model.SMinusPFrames(d);
                Assert.True(gap > previous, "S-P must grow with distance (d = " + d + ")");
                previous = gap;
            }

            // 震源直上では P と S が同時に着く（S-P = 0）。これは近似ではない。
            Assert.Equal(0f, model.SMinusPFrames(0f), 4);

            // S は必ず P の √3 倍の時刻。
            Assert.Equal(model.PArrivalFrames(4000f) * SeismogramModel.VpOverVs,
                         model.SArrivalFrames(4000f), 3);
        }

        [Fact]
        public void ArrivalsAreCappedSoTheCodaHasRoom()
        {
            var model = Model(9u);

            float cap = SeismogramModel.MaxSArrivalFraction * Window;
            Assert.True(model.SArrivalFrames(1000000f) <= cap + 1e-3f);

            // 頭打ちに達した先では距離を増やしても開かない（doc の明示的な制限）。
            Assert.Equal(model.SMinusPFrames(1000000f), model.SMinusPFrames(2000000f), 3);
        }

        [Fact]
        public void TheCodaDecays()
        {
            var model = Model(11u);
            const float distance = 2000f;
            float tS = model.SArrivalFrames(distance);

            float early = MeanAbsolute(model, distance, tS + 5f, tS + 85f);
            float late = MeanAbsolute(model, distance, tS + 400f, tS + 480f);

            Assert.True(late < early * 0.5f,
                        "the coda must decay (early " + early + ", late " + late + ")");
        }

        [Fact]
        public void TheSWaveIsBiggerThanThePWave()
        {
            var model = Model(13u);
            const float distance = 6000f;
            float tP = model.PArrivalFrames(distance);
            float tS = model.SArrivalFrames(distance);

            float p = PeakAbsolute(model, distance, tP + 2f, tS - 2f);
            float s = PeakAbsolute(model, distance, tS, tS + 60f);

            Assert.True(p > 0f, "the P wave must exist");
            Assert.True(s > p * 2f, "S must be much larger than P (P " + p + ", S " + s + ")");
        }

        [Fact]
        public void SameQuakeGivesTheSameRecordEveryTime()
        {
            // 閉じた式であること（飛んだフレームをあとから埋めても同じ値）。
            var a = SeismogramModel.For(4242u, Window);
            var b = SeismogramModel.For(4242u, Window);

            for (int t = 1; t < (int)Window; t += 7)
            {
                Assert.Equal(a.DisplacementAt(3000f, t), b.DisplacementAt(3000f, t));
            }
        }

        [Fact]
        public void DifferentQuakesGiveDifferentRecords()
        {
            var a = SeismogramModel.For(1u, Window);
            var b = SeismogramModel.For(2u, Window);

            int different = 0;
            for (int t = 1; t < (int)Window; t += 3)
            {
                if (a.DisplacementAt(3000f, t) != b.DisplacementAt(3000f, t)) different++;
            }

            Assert.True(different > 200,
                        "two quakes must not share one waveform (differing samples: " + different + ")");
        }

        [Fact]
        public void EveryCarrierStaysWellBelowNyquist()
        {
            // 標本間隔は 1 フレーム（SeismographRecorder が飛んだフレームを埋める）ので
            // ナイキストは π rad/frame。1 周期あたり 6 点は取れることを要求する。
            const float limit = 6.2831853f / 6f;   // ≒1.047 rad/frame

            float fastestP = SeismogramModel.BasePRate * 1.08f;      // For() のゆらぎの上限
            float fastestS = SeismogramModel.BaseSRate * 1.10f * 1.618f;

            Assert.True(fastestP < limit, "the P carrier is too fast: " + fastestP);
            Assert.True(fastestS < limit, "the S carrier is too fast: " + fastestS);
            Assert.True(SeismogramModel.NoiseSegmentFrames >= 6,
                        "the wobble segment must span several samples");
        }

        [Fact]
        public void SampledOnceAPerFrameTheRecordDoesNotFold()
        {
            // 1 フレーム刻みで隣り合うサンプルの差が満目盛りを跨がないこと。
            // 跨ぐようなら、その成分は 2 フレームより速い＝折り返している。
            var model = Model(17u);
            const float distance = 1000f;

            float previous = 0f;
            float biggestStep = 0f;
            for (int t = 1; t < (int)Window; t++)
            {
                float v = model.DisplacementAt(distance, t);
                float step = v - previous;
                if (step < 0f) step = -step;
                if (t > 1 && step > biggestStep) biggestStep = step;
                previous = v;
            }

            float ceiling = 2f * ShakeWaveform.PeakAmplitudeAt(distance);
            Assert.True(biggestStep < ceiling,
                        "one frame must not swing the full scale (step " + biggestStep + ")");
        }

        [Fact]
        public void BrokenInputsReturnZeroInsteadOfThrowing()
        {
            var model = Model(19u);
            Assert.Equal(0f, model.DisplacementAt(float.NaN, 100f));
            Assert.Equal(0f, model.DisplacementAt(1000f, float.NaN));
            Assert.Equal(0f, model.ShapeAt(float.NaN, 100f));
            Assert.Equal(0f, model.PArrivalFrames(float.NaN));

            // 負の距離は 0 として扱う（呼び出し側で符号を持たない量）。
            Assert.Equal(model.PArrivalFrames(0f), model.PArrivalFrames(-500f));
        }

        [Fact]
        public void TheVanillaDefaultIntensityStillScalesToExactlyOne()
        {
            // カメラの差し替えは合計を「合成記象 × (1 + IntensityFactor)」にする。
            // 強度 55（バニラ既定）でこれがちょうど 1 でなければ、既定の地震が
            // 意図せず強く／弱くなる。
            float scale = 1f + ShakeWaveform.IntensityFactor(
                SeismicIntensity.VanillaDefaultIntensity);
            Assert.Equal(1f, scale);
        }

        private static float MeanAbsolute(SeismogramModel model, float distance,
                                          float from, float to)
        {
            float sum = 0f;
            int count = 0;
            for (float t = from; t <= to; t += 1f)
            {
                float v = model.DisplacementAt(distance, t);
                sum += v < 0f ? -v : v;
                count++;
            }
            return count == 0 ? 0f : sum / count;
        }

        private static float PeakAbsolute(SeismogramModel model, float distance,
                                          float from, float to)
        {
            float peak = 0f;
            for (float t = from; t <= to; t += 1f)
            {
                float v = model.DisplacementAt(distance, t);
                if (v < 0f) v = -v;
                if (v > peak) peak = v;
            }
            return peak;
        }
    }
}
