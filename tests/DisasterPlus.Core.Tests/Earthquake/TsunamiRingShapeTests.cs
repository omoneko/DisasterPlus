using System;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <see cref="TsunamiRingShape"/> —— 震源から同心円に立つ津波の「形」。
    ///
    /// ここで守るのは<b>IL から写した式と数字</b>である。ゲームの挙動そのものは
    /// オフラインのソルバ（<c>tools/WaterSolverSim</c>）で測るので、この層では
    /// 「写し間違えていないか」だけを見る。
    /// </summary>
    public class TsunamiRingShapeTests
    {
        /// <summary>バニラの長さ。**ここを既定にするだけで、値としては扱う。**</summary>
        private const int T0 = TsunamiRingShape.DurationTicks;

        [Fact]
        public void VanillaDelta_matches_the_IL_formula()
        {
            // m_delta = round(64 * 64 * intensity / 55)
            // 強度 100 -> 7447 (116.4 m)、255 -> 18991 (296.7 m)。IL 実測値。
            Assert.Equal(7447, TsunamiRingShape.VanillaDeltaUnits(100));
            Assert.Equal(18991, TsunamiRingShape.VanillaDeltaUnits(255));
            Assert.Equal(0, TsunamiRingShape.VanillaDeltaUnits(0));
        }

        [Fact]
        public void Duration_is_256_water_steps()
        {
            Assert.Equal(16384, TsunamiRingShape.DurationTicks);
            Assert.Equal(64, TsunamiRingShape.TicksPerWaterStep);
            Assert.Equal(256, TsunamiRingShape.WaterSteps);
        }

        [Fact]
        public void Radius_and_rate_are_inverses_of_each_other()
        {
            // r = sqrt(rate)*0.4 + 10、その逆が rate = ((r-10)/0.4)^2。
            foreach (float r in new[] { 100f, 640f, 1280f, 3840f })
            {
                long rate = TsunamiRingShape.RateForRadiusMetres(r);
                float back = TsunamiRingShape.RadiusMetresForRate(rate);
                Assert.True(Math.Abs(back - r) < 1f,
                    "radius " + r + " -> rate " + rate + " -> radius " + back);
            }
        }

        [Fact]
        public void Radius_below_the_floor_asks_for_nothing()
        {
            // 半径 10 m は流量 0 でも出る（式の定数項）。それ以下は要求できない。
            Assert.Equal(0L, TsunamiRingShape.RateForRadiusMetres(10f));
            Assert.Equal(0L, TsunamiRingShape.RateForRadiusMetres(0f));
            Assert.Equal(0f, TsunamiRingShape.RadiusMetresForRate(0L));
        }

        [Fact]
        public void Offset_is_zero_outside_the_waveform()
        {
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(0, 7447, T0));
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(-64, 7447, T0));
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(T0, 7447, T0));
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(8192, 0, T0));
        }

        [Fact]
        public void The_crest_is_in_the_middle_and_equals_the_decayed_amplitude()
        {
            // t = T/2 で off = -amp、つまり level = sea + amp。
            // amp = delta * (65536 - t) / 65536 = 7447 * 0.875 = 6516。
            int half = TsunamiRingShape.DurationTicks / 2;
            int got = TsunamiRingShape.LevelOffsetUnits(half, 7447, T0);

            Assert.InRange(got, 6514, 6518);
        }

        [Fact]
        public void Retreat_comes_before_and_after_the_crest()
        {
            // 1.5 周期の sin と (1-cos) の包絡なので、引き -> 押し -> 引き になる。
            int T = TsunamiRingShape.DurationTicks;

            int early = TsunamiRingShape.LevelOffsetUnits(T / 6, 7447, T);
            int crest = TsunamiRingShape.LevelOffsetUnits(T / 2, 7447, T);
            int late = TsunamiRingShape.LevelOffsetUnits(T * 5 / 6, 7447, T);

            Assert.True(early < 0, "the sea should draw back first, got " + early);
            Assert.True(crest > 0, "the crest should push, got " + crest);
            Assert.True(late < 0, "the sea should draw back again, got " + late);

            // 引き波は押し波よりずっと浅い（0.25*amp 対 1.0*amp）。
            Assert.True(-early < crest / 2, "retreat " + early + " vs crest " + crest);
        }

        [Fact]
        public void The_waveform_crosses_zero_at_one_third_and_two_thirds()
        {
            int T = TsunamiRingShape.DurationTicks;

            Assert.InRange(TsunamiRingShape.LevelOffsetUnits(T / 3, 7447, T), -40, 40);
            Assert.InRange(TsunamiRingShape.LevelOffsetUnits(T * 2 / 3, 7447, T), -40, 40);
        }

        [Fact]
        public void ClampDraw_never_bares_the_seabed()
        {
            int depth = 174 * 64;   // 174 m

            // 引き波が水深より深く要求しても、割合までしか通さない。
            int got = TsunamiRingShape.ClampDraw(-100000, depth, 0.45f);
            Assert.Equal(-(int)(depth * 0.45f), got);

            // 浅い要求はそのまま通る。
            Assert.Equal(-64, TsunamiRingShape.ClampDraw(-64, depth, 0.45f));
        }

        [Fact]
        public void ClampDraw_leaves_the_push_alone()
        {
            Assert.Equal(5000, TsunamiRingShape.ClampDraw(5000, 174 * 64, 0.45f));
            Assert.Equal(0, TsunamiRingShape.ClampDraw(0, 174 * 64, 0.45f));
        }

        [Fact]
        public void ClampDraw_on_dry_land_asks_for_nothing()
        {
            Assert.Equal(0, TsunamiRingShape.ClampDraw(-5000, 0, 0.45f));
            Assert.Equal(0, TsunamiRingShape.ClampDraw(-5000, -10, 0.45f));
        }

        [Fact]
        public void The_duration_really_is_a_parameter()
        {
            // ★★ **この 3 本が緑でなければ、波形の長さは絵に描いた餅である。**
            //    実際に一度、呼び出し側が 768 水ステップを指定しているのに
            //    ここが定数を見ていて 256 歩で終わっていた。
            int longT = 768 * TsunamiRingShape.TicksPerWaterStep;

            // 1. バニラの長さを過ぎても、長い波形なら 0 にならない。
            Assert.NotEqual(0, TsunamiRingShape.LevelOffsetUnits(T0 + 640, 7447, longT));

            // 2. 山は指定した長さの真ん中あたりに来る。
            //
            // ★ **ちょうど真ん中ではない。** 振幅 (65536 - t)/65536 が
            //   時間とともに落ちるので、頂点はわずかに手前へずれる。
            //   だから「真ん中で最大」ではなく「真ん中付近で最大」を試す。
            int crest = TsunamiRingShape.LevelOffsetUnits(longT / 2, 7447, longT);
            Assert.True(crest > 0, "crest should push, got " + crest);

            int best = int.MinValue;
            int bestAt = -1;
            for (int t = 64; t < longT; t += 64)
            {
                int v = TsunamiRingShape.LevelOffsetUnits(t, 7447, longT);
                if (v > best) { best = v; bestAt = t; }
            }

            Assert.InRange(bestAt, (int)(longT * 0.40), (int)(longT * 0.60));
            Assert.True(crest > best * 0.95,
                "the middle should be within 5% of the peak: " + crest + " vs " + best);

            // 3. 指定した長さで終わる。
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(longT, 7447, longT));
        }

        [Fact]
        public void A_longer_waveform_decays_more_by_its_crest()
        {
            // ★ 減衰項 (65536 - t)/65536 は**絶対時刻**で効くので、
            //   長い波形ほど山の時点で振幅が落ちている。
            //   これが 1024 水ステップで威力が落ちた理由である。
            int shortT = 256 * TsunamiRingShape.TicksPerWaterStep;
            int longT = 1024 * TsunamiRingShape.TicksPerWaterStep;

            int a = TsunamiRingShape.LevelOffsetUnits(shortT / 2, 7447, shortT);
            int b = TsunamiRingShape.LevelOffsetUnits(longT / 2, 7447, longT);

            Assert.True(b < a, "the long waveform's crest should be smaller: "
                               + b + " vs " + a);
        }

        [Fact]
        public void A_zero_or_negative_duration_asks_for_nothing()
        {
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(64, 7447, 0));
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(64, 7447, -1));
        }

        [Fact]
        public void The_offset_never_leaves_the_representable_range()
        {
            // m_target は ushort。強度 255 でも 1/64 m 単位で桁が溢れないこと。
            for (int t = 0; t <= TsunamiRingShape.DurationTicks; t += 64)
            {
                int off = TsunamiRingShape.LevelOffsetUnits(t, 18991, T0);
                Assert.InRange(off, -19000, 19000);
            }
        }
    }
}
