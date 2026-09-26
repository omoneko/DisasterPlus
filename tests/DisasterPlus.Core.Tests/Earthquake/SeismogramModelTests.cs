using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Pins down the synthetic seismogram (P wave, S wave and coda).
    ///
    /// **The three most important cases:**
    ///   - <see cref="ShapeNeverLeavesMinusOneToOne"/> —— the grounds for the full scale
    ///     staying at <c>ShakeWaveform.MaxDisplacement</c>. Break this and the vertical
    ///     scale becomes a lie when the trace is set beside the vanilla line.
    ///   - <see cref="ArrivalsSeparateWithDistance"/> —— the headline of this model. Once
    ///     the preliminary-tremor duration stops widening with distance, the picture drops
    ///     to being "merely plausible".
    ///   - <see cref="EveryCarrierStaysWellBelowNyquist"/> —— aliasing prevention. Add a
    ///     fast component and the tier-1 graph grows a spurious wave indistinguishable from
    ///     tier 2 (long-period ground motion) —— a recurrence of overall review I5.
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
            // When m_activeDuration cannot be read (0) we must not hard-code a window of our own.
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
            // The theoretical maximum at distance 0 is exactly MaxDisplacement (0.60).
            // These are the grounds for drawing at the same full scale as the vanilla waveform.
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

            // Directly above the hypocentre P and S arrive together (S-P = 0).
            // This is not an approximation.
            Assert.Equal(0f, model.SMinusPFrames(0f), 4);

            // S always arrives at √3 times the time of P.
            Assert.Equal(model.PArrivalFrames(4000f) * SeismogramModel.VpOverVs,
                         model.SArrivalFrames(4000f), 3);
        }

        [Fact]
        public void ArrivalsAreCappedSoTheCodaHasRoom()
        {
            var model = Model(9u);

            float cap = SeismogramModel.MaxSArrivalFraction * Window;
            Assert.True(model.SArrivalFrames(1000000f) <= cap + 1e-3f);

            // Past the cap, more distance no longer widens the gap
            // (an explicit limitation stated in the doc).
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
            // It must be a closed-form expression (filling in a skipped frame later gives
            // the same value).
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
            // The sample interval is 1 frame (SeismographRecorder fills in skipped frames),
            // so Nyquist is π rad/frame. We demand at least 6 samples per cycle.
            const float limit = 6.2831853f / 6f;   // ≈1.047 rad/frame

            float fastestP = SeismogramModel.BasePRate * 1.08f;      // upper bound of For()'s jitter
            float fastestS = SeismogramModel.BaseSRate * 1.10f * 1.618f;

            Assert.True(fastestP < limit, "the P carrier is too fast: " + fastestP);
            Assert.True(fastestS < limit, "the S carrier is too fast: " + fastestS);
            Assert.True(SeismogramModel.NoiseSegmentFrames >= 6,
                        "the wobble segment must span several samples");
        }

        [Fact]
        public void SampledOnceAPerFrameTheRecordDoesNotFold()
        {
            // Stepping one frame at a time, the difference between adjacent samples must not
            // span the full scale. If it does, that component is faster than 2 frames, i.e.
            // it is aliasing.
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

            // A negative distance is treated as 0 (on the caller's side it is an unsigned quantity).
            Assert.Equal(model.PArrivalFrames(0f), model.PArrivalFrames(-500f));
        }

        [Fact]
        public void TheVanillaDefaultIntensityStillScalesToExactlyOne()
        {
            // Swapping the camera makes the total "synthetic seismogram × (1 + IntensityFactor)".
            // Unless this is exactly 1 at intensity 55 (the vanilla default), the default
            // earthquake becomes unintentionally stronger or weaker.
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
