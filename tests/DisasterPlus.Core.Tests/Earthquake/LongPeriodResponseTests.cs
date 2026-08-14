using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// **ここで固定しているのは「バニラと一致すること」ではない。**
    /// バニラに長周期地震動は存在しないので（IL 事実文書 §A-7）、突き合わせる相手が
    /// そもそも無い。固定するのは 3 つだけ:
    ///
    ///   1. 「長周期」を名乗る資格（バニラの 2 成分より十分遅いこと）
    ///   2. 追加被害が 0 になる境界（低層・範囲外・強さ 0）が確実に 0 であること
    ///   3. 追加確率が上限を超えず負にもならないこと
    ///
    /// 2 と 3 は**実際に建物を壊す値**なので、ここが崩れると都市が壊れる。
    /// </summary>
    public class LongPeriodResponseTests
    {
        [Fact]
        public void WavePeriodIsWellBelowTheVanillaComponents()
        {
            // バニラは 0.63 rad/frame（周期 ≒10）と 0.17（≒37）の 2 本だけ。
            // 「長周期」を名乗る以上、それより十分長いこと自体をテストで固定する。
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
            // 低層は対象外。範囲外も 0。ここが 0 にならないと「全部壊れる」になる。
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                LongPeriodResponse.MinHeightMetres - 1f, 100f, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(
                60f, LongPeriodResponse.RangeOf(100) + 1f, 100, 10f), 5);
            // 強さ 0 は完全に無効（設定で切れることを保証する）。
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, 100f, 100, 0f), 5);
            // 高さが読めなかったとき（0）も必ず 0。**推測した高さで建物を壊さない。**
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
            // 壊れた読み取りで建物を壊さない。NaN を確率として使うと比較が全て false に
            // なるので「何も起きない」に倒れるが、それは偶然であって設計ではない。
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(float.NaN, 100f, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, float.NaN, 100, 10f), 5);
            Assert.Equal(0f, LongPeriodResponse.ExtraCollapseChance(60f, 100f, 100, float.NaN), 5);
            Assert.Equal(0f, LongPeriodResponse.Resonance(float.NaN), 5);
        }

        /// <summary>
        /// **実際に使われる上限は 0.25 ではない**（第 2 層レビュー M8）。
        /// 呼び出し側は <see cref="LongPeriodResponse.MaxExtraChance"/> のクランプの
        /// **後**に時間帯係数を掛けるので、画面と被害選定に出る上限は 0.2875 である。
        /// doc と診断ダンプがこの数字を名乗っているので、値そのものを固定しておく。
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
