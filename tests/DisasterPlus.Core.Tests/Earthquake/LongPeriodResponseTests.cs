using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// **What is pinned down here is not "agreement with vanilla".**
    /// Vanilla has no long-period ground motion (IL facts document §A-7), so there is
    /// nothing to compare against in the first place. Only three things are pinned down:
    ///
    ///   1. the right to call itself "long-period" (far slower than vanilla's two components)
    ///   2. that the boundaries where the extra damage is 0 (low buildings, out of range,
    ///      strength 0) really do give 0
    ///   3. that the extra probability neither exceeds the cap nor goes negative
    ///
    /// 2 and 3 are **values that actually destroy buildings**, so if they break, the city
    /// breaks.
    /// </summary>
    public class LongPeriodResponseTests
    {
        [Fact]
        public void WavePeriodIsWellBelowTheVanillaComponents()
        {
            // Vanilla has only two: 0.63 rad/frame (period ≈10) and 0.17 (≈37).
            // Since we call this "long-period", the test pins down that it really is far
            // longer than those.
            double fast = 2.0 * System.Math.PI / ShakeWaveform.FastRate;
            double slow = 2.0 * System.Math.PI / ShakeWaveform.SlowRate;
            Assert.True(LongPeriodResponse.WavePeriodFrames > slow * 4.0,
                "the long-period component must be far slower than vanilla's slowest");
            Assert.True(LongPeriodResponse.WavePeriodFrames > fast * 4.0);
        }

        [Fact]
        public void TallerBuildingsHaveLongerPeriods()
        {
            Assert.True(LongPeriodResponse.BuildingPeriodFrames(80f)
                        > LongPeriodResponse.BuildingPeriodFrames(30f));
            Assert.Equal(0f, LongPeriodResponse.BuildingPeriodFrames(0f), 4);
        }

        [Fact]
        public void ResonancePeaksAtTheWavePeriod()
        {
            float peak = LongPeriodResponse.Resonance(LongPeriodResponse.WavePeriodFrames);
            Assert.Equal(1f, peak, 4);
            Assert.True(LongPeriodResponse.Resonance(LongPeriodResponse.WavePeriodFrames + 200f) < peak);
            Assert.True(LongPeriodResponse.Resonance(0f) < peak);
        }

        [Fact]
        public void ResonanceIsAlwaysInsideZeroToOne()
        {
            for (float t = 0f; t < 2000f; t += 7f)
            {
                Assert.InRange(LongPeriodResponse.Resonance(t), 0f, 1f);
            }
        }

        [Fact]
        public void ReachesFurtherThanTheVanillaDisc()
        {
            Assert.True(LongPeriodResponse.RangeOf(100) > SeismicIntensity.RadiusOf(100));
        }

        [Fact]
        public void LowBuildingsAndFarBuildingsGetNothing()
        {
            // Low buildings are out of scope. Out of range is 0 too. If these are not 0 it
            // becomes "everything collapses".
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                LongPeriodResponse.MinHeightMetres - 1f, 100f, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                60f, LongPeriodResponse.RangeOf(100) + 1f, 100, 10f), 5);
            // Strength 0 disables it entirely (this guarantees it can be switched off in
            // the settings).
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, 100f, 100, 0f), 5);
            // When the height could not be read (0) it is always 0 too.
            // **Never destroy a building on a guessed height.**
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(0f, 100f, 100, 10f), 5);
        }

        [Fact]
        public void ChanceIsCappedAndNeverNegative()
        {
            for (float h = 0f; h < 300f; h += 3f)
            {
                for (float d = 0f; d < 9000f; d += 250f)
                {
                    float c = LongPeriodResponse.ExtraCollapseChance(h, d, 255, 10f);
                    Assert.InRange(c, 0f, LongPeriodResponse.MaxExtraChance);
                }
            }
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            // Never destroy a building on a broken reading. Using a NaN as a probability
            // makes every comparison false, so it does collapse into "nothing happens" ——
            // but that is an accident, not a design.
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(float.NaN, 100f, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, float.NaN, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, 100f, 100, float.NaN), 5);
            Assert.Equal(0f, LongPeriodResponse.Resonance(float.NaN), 5);
        }

        /// <summary>
        /// **The ceiling actually in force is not 0.25** (tier-2 review M8).
        /// The caller multiplies by the time-of-day factor **after** the
        /// <see cref="LongPeriodResponse.MaxExtraChance"/> clamp, so the ceiling that shows
        /// up on screen and in the damage selection is 0.2875. The doc and the diagnostic
        /// dump quote this figure, so the value itself is pinned down.
        /// </summary>
        [Fact]
        public void TheCeilingActuallyAppliedIncludesTheTimeOfDayFactor()
        {
            Assert.Equal(0.25f, LongPeriodResponse.MaxExtraChance, 5);
            Assert.Equal(1.15f, TimeOfDayFactor.NightFactor, 5);
            Assert.Equal(0.2875f,
                LongPeriodResponse.MaxExtraChance * TimeOfDayFactor.NightFactor, 5);
        }
    }
}
