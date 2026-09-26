using System;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <see cref="TsunamiRingShape"/> —— the "shape" of the tsunami that rises in concentric
    /// circles from the hypocentre.
    ///
    /// What is protected here are <b>the formulas and numbers copied out of the IL</b>. The
    /// behaviour in the game itself is measured by the offline solver
    /// (<c>tools/WaterSolverSim</c>), so at this layer we only check that nothing was
    /// copied down wrongly.
    /// </summary>
    public class TsunamiRingShapeTests
    {
        /// <summary>The vanilla duration. **Only the default here; it is treated as a
        /// value.**</summary>
        private const int T0 = TsunamiRingShape.DurationTicks;

        [Fact]
        public void VanillaDelta_matches_the_IL_formula()
        {
            // m_delta = round(64 * 64 * intensity / 55)
            // Intensity 100 -> 7447 (116.4 m), 255 -> 18991 (296.7 m). Measured from the IL.
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
            // r = sqrt(rate)*0.4 + 10, whose inverse is rate = ((r-10)/0.4)^2.
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
            // A radius of 10 m comes out even at rate 0 (the constant term of the formula).
            // Anything below that cannot be asked for.
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
            // At t = T/2, off = -amp, i.e. level = sea + amp.
            // amp = delta * (65536 - t) / 65536 = 7447 * 0.875 = 6516.
            int half = TsunamiRingShape.DurationTicks / 2;
            int got = TsunamiRingShape.LevelOffsetUnits(half, 7447, T0);

            Assert.InRange(got, 6514, 6518);
        }

        [Fact]
        public void Retreat_comes_before_and_after_the_crest()
        {
            // It is 1.5 periods of sin under a (1-cos) envelope, so it goes
            // retreat -> push -> retreat.
            int T = TsunamiRingShape.DurationTicks;

            int early = TsunamiRingShape.LevelOffsetUnits(T / 6, 7447, T);
            int crest = TsunamiRingShape.LevelOffsetUnits(T / 2, 7447, T);
            int late = TsunamiRingShape.LevelOffsetUnits(T * 5 / 6, 7447, T);

            Assert.True(early < 0, "the sea should draw back first, got " + early);
            Assert.True(crest > 0, "the crest should push, got " + crest);
            Assert.True(late < 0, "the sea should draw back again, got " + late);

            // The retreat is far shallower than the push (0.25*amp against 1.0*amp).
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

            // Even when the retreat asks for more than the water depth, only the given
            // fraction gets through.
            int got = TsunamiRingShape.ClampDraw(-100000, depth, 0.45f);
            Assert.Equal(-(int)(depth * 0.45f), got);

            // A shallow request passes through untouched.
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
            // ★★ **Unless these three are green, the waveform's duration is pie in the sky.**
            //    It did in fact happen once that the caller specified 768 water steps while
            //    this code was looking at the constant and finishing after 256.
            int longT = 768 * TsunamiRingShape.TicksPerWaterStep;

            // 1. Past the vanilla duration it must not be 0 for a long waveform.
            Assert.NotEqual(0, TsunamiRingShape.LevelOffsetUnits(T0 + 640, 7447, longT));

            // 2. The crest comes around the middle of the duration given.
            //
            // ★ **Not exactly at the middle.** The amplitude (65536 - t)/65536 falls with
            //   time, so the peak shifts slightly earlier. So we test for "greatest near
            //   the middle" rather than "greatest at the middle".
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

            // 3. It ends at the duration given.
            Assert.Equal(0, TsunamiRingShape.LevelOffsetUnits(longT, 7447, longT));
        }

        [Fact]
        public void A_longer_waveform_decays_more_by_its_crest()
        {
            // ★ The decay term (65536 - t)/65536 acts on **absolute time**, so the longer
            //   the waveform the more the amplitude has already fallen by the crest.
            //   This is why the force dropped off at 1024 water steps.
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
            // m_target is a ushort. Even at intensity 255 the 1/64 m units must not overflow.
            for (int t = 0; t <= TsunamiRingShape.DurationTicks; t += 64)
            {
                int off = TsunamiRingShape.LevelOffsetUnits(t, 18991, T0);
                Assert.InRange(off, -19000, 19000);
            }
        }
    }
}
